[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$InputPath,

    [Parameter(Position = 1)]
    [string]$OutputPath,

    [switch]$NoGui
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

function Get-ZipEntryText {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "The Excel file is missing required entry: $EntryName"
    }

    $stream = $entry.Open()
    $reader = [System.IO.StreamReader]::new($stream)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Convert-ColumnNameToIndex {
    param([string]$ColumnName)

    $result = 0
    foreach ($character in $ColumnName.ToUpperInvariant().ToCharArray()) {
        $result = ($result * 26) + ([int]$character - [int][char]'A' + 1)
    }
    return $result - 1
}

function Convert-CellReference {
    param([string]$Reference)

    if ($Reference -notmatch '^([A-Za-z]+)(\d+)$') {
        throw "Invalid Excel cell reference: $Reference"
    }

    return [pscustomobject]@{
        Column = Convert-ColumnNameToIndex $Matches[1]
        Row    = [int]$Matches[2] - 1
    }
}

function Get-StableUnicode {
    param([string]$Seed)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Seed))
        $bytes = New-Object byte[] 16
        [Array]::Copy($hash, $bytes, 16)
        # Mark it as a UUID-style version 5 / RFC 4122 value.
        $bytes[7] = ($bytes[7] -band 0x0F) -bor 0x50
        $bytes[8] = ($bytes[8] -band 0x3F) -bor 0x80
        return ([Guid]::new($bytes)).ToString().ToUpperInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Convert-ExcelSerialDate {
    param($Value)

    $number = 0.0
    if ([double]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) {
        return [DateTime]::FromOADate($number).ToString('yyyy-MM-dd HH:mm:ss')
    }
    return [string]$Value
}

function Read-1688Workbook {
    param([string]$Path)

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $fileStream = [System.IO.FileStream]::new(
        $resolvedPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite)
    $archive = [System.IO.Compression.ZipArchive]::new(
        $fileStream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        $sharedStrings = @()
        $sharedEntry = $archive.GetEntry('xl/sharedStrings.xml')
        if ($null -ne $sharedEntry) {
            [xml]$sharedXml = Get-ZipEntryText $archive 'xl/sharedStrings.xml'
            $sharedStrings = @($sharedXml.SelectNodes("//*[local-name()='si']") | ForEach-Object {
                ($_.SelectNodes(".//*[local-name()='t']") | ForEach-Object { $_.InnerText }) -join ''
            })
        }

        [xml]$workbookXml = Get-ZipEntryText $archive 'xl/workbook.xml'
        [xml]$relationsXml = Get-ZipEntryText $archive 'xl/_rels/workbook.xml.rels'
        $firstSheet = $workbookXml.SelectSingleNode("//*[local-name()='sheets']/*[local-name()='sheet'][1]")
        if ($null -eq $firstSheet) {
            throw 'The workbook contains no worksheets.'
        }

        $relationshipId = $firstSheet.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
        $relationship = $relationsXml.SelectSingleNode("//*[local-name()='Relationship' and @Id='$relationshipId']")
        if ($null -eq $relationship) {
            throw 'Could not resolve the first worksheet in the workbook.'
        }

        $target = $relationship.Target.Replace('\\', '/')
        if ($target.StartsWith('/')) {
            $sheetEntryName = $target.TrimStart('/')
        }
        elseif ($target.StartsWith('xl/')) {
            $sheetEntryName = $target
        }
        else {
            $sheetEntryName = "xl/$target"
        }

        [xml]$sheetXml = Get-ZipEntryText $archive $sheetEntryName
        $dimension = $sheetXml.SelectSingleNode("//*[local-name()='dimension']")
        if ($null -eq $dimension -or [string]::IsNullOrWhiteSpace($dimension.ref)) {
            throw 'The worksheet has no data range.'
        }

        $lastReference = ([string]$dimension.ref -split ':')[-1]
        $lastCell = Convert-CellReference $lastReference
        $cellNodes = @($sheetXml.SelectNodes("//*[local-name()='sheetData']/*[local-name()='row']/*[local-name()='c']"))
        $maxRow = $lastCell.Row
        $maxColumn = $lastCell.Column
        foreach ($cellNode in $cellNodes) {
            $location = Convert-CellReference ([string]$cellNode.r)
            $maxRow = [Math]::Max($maxRow, $location.Row)
            $maxColumn = [Math]::Max($maxColumn, $location.Column)
        }
        $rowCount = $maxRow + 1
        $columnCount = $maxColumn + 1
        $cells = [System.Array]::CreateInstance([object], [int[]]@($rowCount, $columnCount))

        foreach ($cellNode in $cellNodes) {
            $location = Convert-CellReference ([string]$cellNode.r)
            $type = [string]$cellNode.GetAttribute('t')
            $valueNode = $cellNode.SelectSingleNode("./*[local-name()='v']")
            $value = if ($null -ne $valueNode) { $valueNode.InnerText } else { '' }

            switch ($type) {
                's' {
                    if ($value -ne '') { $value = $sharedStrings[[int]$value] }
                }
                'inlineStr' {
                    $value = ($cellNode.SelectNodes(".//*[local-name()='t']") | ForEach-Object { $_.InnerText }) -join ''
                }
                'b' { $value = if ($value -eq '1') { 'TRUE' } else { 'FALSE' } }
                default { }
            }
            $cells[$location.Row, $location.Column] = $value
        }

        $mergedRangeCount = 0
        foreach ($mergeNode in $sheetXml.SelectNodes("//*[local-name()='mergeCells']/*[local-name()='mergeCell']")) {
            $parts = ([string]$mergeNode.ref) -split ':'
            if ($parts.Count -ne 2) { continue }
            $start = Convert-CellReference $parts[0]
            $end = Convert-CellReference $parts[1]
            $topLeft = $cells[$start.Row, $start.Column]
            for ($row = $start.Row; $row -le $end.Row; $row++) {
                for ($column = $start.Column; $column -le $end.Column; $column++) {
                    $cells[$row, $column] = $topLeft
                }
            }
            $mergedRangeCount++
        }

        $headers = for ($column = 0; $column -lt $columnCount; $column++) {
            $header = [string]$cells[0, $column]
            if ([string]::IsNullOrWhiteSpace($header)) { "Column_$($column + 1)" } else { $header.Trim() }
        }

        # Make duplicate headings safe for DataTable/CSV consumers.
        $seenHeaders = @{}
        for ($column = 0; $column -lt $headers.Count; $column++) {
            $baseHeader = $headers[$column]
            if ($seenHeaders.ContainsKey($baseHeader)) {
                $seenHeaders[$baseHeader]++
                $headers[$column] = "$baseHeader`_$($seenHeaders[$baseHeader])"
            }
            else {
                $seenHeaders[$baseHeader] = 1
            }
        }

        $records = New-Object System.Collections.Generic.List[object]
        for ($row = 1; $row -lt $rowCount; $row++) {
            $record = [ordered]@{}
            for ($column = 0; $column -lt $columnCount; $column++) {
                $value = $cells[$row, $column]
                if ($headers[$column] -match '时间$' -and $null -ne $value -and [string]$value -ne '') {
                    $value = Convert-ExcelSerialDate $value
                }
                $record[$headers[$column]] = if ($null -eq $value) { '' } else { [string]$value }
            }
            $records.Add([pscustomobject]$record)
        }

        return [pscustomobject]@{
            Headers          = @($headers)
            Records          = $records
            MergedRangeCount = $mergedRangeCount
            SheetName        = [string]$firstSheet.name
        }
    }
    finally {
        $archive.Dispose()
        $fileStream.Dispose()
    }
}

function Get-GramsFromTitle {
    param([string]$Title)

    if ([string]::IsNullOrWhiteSpace($Title)) { return '' }
    $matches = [regex]::Matches($Title, '(?i)(?<![a-z])(?<grams>\d+(?:\.\d+)?)\s*g\b')
    if ($matches.Count -eq 0) { return '' }
    return $matches[$matches.Count - 1].Groups['grams'].Value
}

function Get-CalculatedValues {
    param(
        [string]$Grams,
        [string]$UnitPrice,
        [string]$Quantity
    )

    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $gramsNumber = 0.0
    $priceNumber = 0.0
    $quantityNumber = 0.0
    $hasGrams = [double]::TryParse($Grams, [System.Globalization.NumberStyles]::Float, $culture, [ref]$gramsNumber)
    $hasPrice = [double]::TryParse($UnitPrice, [System.Globalization.NumberStyles]::Float, $culture, [ref]$priceNumber)
    $hasQuantity = [double]::TryParse($Quantity, [System.Globalization.NumberStyles]::Float, $culture, [ref]$quantityNumber)

    $ounces = if ($hasGrams) { ($gramsNumber / 28.349523125).ToString('0.####', $culture) } else { '' }
    $totalCost = if ($hasGrams -and $hasPrice -and $hasQuantity) {
        (($gramsNumber * 0.05) + ($priceNumber * $quantityNumber)).ToString('0.00', $culture)
    }
    else { '' }

    return [pscustomobject]@{ Ounces = $ounces; TotalCost = $totalCost }
}

function Convert-ToDataTable {
    param($WorkbookData)

    $table = [System.Data.DataTable]::new('BarTender_Data')
    [void]$table.Columns.Add('Unicode', [string])
    foreach ($header in $WorkbookData.Headers) {
        [void]$table.Columns.Add($header, [string])
    }
    [void]$table.Columns.Add('重量(g)', [string])
    [void]$table.Columns.Add('重量(oz)', [string])
    [void]$table.Columns.Add('总成本', [string])

    $index = 0
    foreach ($record in $WorkbookData.Records) {
        $row = $table.NewRow()
        $orderId = [string]$record.'订单编号'
        $skuId = [string]$record.'SKU ID'
        $itemSku = [string]$record.'单品货号'
        $row['Unicode'] = Get-StableUnicode "$orderId|$skuId|$itemSku|$index"

        foreach ($header in $WorkbookData.Headers) {
            $row[$header] = [string]$record.$header
        }

        $title = [string]$record.'货品标题'
        $grams = Get-GramsFromTitle $title
        Write-Verbose "Row $($index + 1) detected grams: '$grams' from '$title'"
        $price = [string]$record.'单价(元)'
        $quantity = [string]$record.'数量'
        $calculated = Get-CalculatedValues $grams $price $quantity
        $row['重量(g)'] = $grams
        $row['重量(oz)'] = $calculated.Ounces
        $row['总成本'] = $calculated.TotalCost
        [void]$table.Rows.Add($row)
        $index++
    }
    return ,$table
}

function Convert-ToCsvField {
    param($Value)

    $text = if ($null -eq $Value -or $Value -is [DBNull]) { '' } else { [string]$Value }
    if ($text.Contains('"')) { $text = $text.Replace('"', '""') }
    if ($text.IndexOfAny([char[]]",`r`n`"") -ge 0) { return '"' + $text + '"' }
    return $text
}

function Export-BarTenderCsv {
    param(
        [System.Data.DataTable]$Table,
        [string]$Path
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add((($Table.Columns | ForEach-Object { Convert-ToCsvField $_.ColumnName }) -join ','))
    foreach ($row in $Table.Rows) {
        $lines.Add((($Table.Columns | ForEach-Object { Convert-ToCsvField $row[$_.ColumnName] }) -join ','))
    }

    $encoding = [System.Text.UTF8Encoding]::new($true)
    [System.IO.File]::WriteAllLines($Path, $lines, $encoding)
}

function Update-DataRowCalculations {
    param([System.Data.DataRow]$Row)

    $calculated = Get-CalculatedValues ([string]$Row['重量(g)']) ([string]$Row['单价(元)']) ([string]$Row['数量'])
    $Row['重量(oz)'] = $calculated.Ounces
    $Row['总成本'] = $calculated.TotalCost
}

function Show-ConverterWindow {
    param(
        [System.Data.DataTable]$Table,
        [string]$SourcePath,
        [int]$MergedRangeCount
    )

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $form = [System.Windows.Forms.Form]::new()
    $form.Text = '1688 Order to BarTender CSV'
    $form.StartPosition = 'CenterScreen'
    $form.Size = [System.Drawing.Size]::new(1280, 760)
    $form.MinimumSize = [System.Drawing.Size]::new(900, 560)

    $header = [System.Windows.Forms.Label]::new()
    $header.Dock = 'Top'
    $header.Height = 55
    $header.Padding = [System.Windows.Forms.Padding]::new(12, 8, 12, 4)
    $header.Text = "Source: $SourcePath`r`nRows: $($Table.Rows.Count)    Expanded merged ranges: $MergedRangeCount    Edit 重量(g) to recalculate 重量(oz) and 总成本."
    $form.Controls.Add($header)

    $buttons = [System.Windows.Forms.FlowLayoutPanel]::new()
    $buttons.Dock = 'Bottom'
    $buttons.Height = 52
    $buttons.FlowDirection = 'RightToLeft'
    $buttons.Padding = [System.Windows.Forms.Padding]::new(8)
    $form.Controls.Add($buttons)

    $exportButton = [System.Windows.Forms.Button]::new()
    $exportButton.Text = 'Export CSV'
    $exportButton.Width = 120
    $exportButton.Height = 30
    $buttons.Controls.Add($exportButton)

    $grid = [System.Windows.Forms.DataGridView]::new()
    $grid.Dock = 'Fill'
    $grid.DataSource = $Table
    $grid.AllowUserToAddRows = $false
    $grid.AllowUserToDeleteRows = $false
    $grid.AutoSizeColumnsMode = 'DisplayedCells'
    $grid.RowHeadersVisible = $false
    $grid.SelectionMode = 'CellSelect'
    $form.Controls.Add($grid)
    $grid.BringToFront()

    foreach ($readOnlyColumn in @('Unicode', '重量(oz)', '总成本')) {
        $grid.Columns[$readOnlyColumn].ReadOnly = $true
        $grid.Columns[$readOnlyColumn].DefaultCellStyle.BackColor = [System.Drawing.Color]::FromArgb(238, 244, 246)
    }

    $grid.add_CellEndEdit({
        param($sender, $eventArgs)
        if ($sender.Columns[$eventArgs.ColumnIndex].Name -in @('重量(g)', '单价(元)', '数量')) {
            Update-DataRowCalculations $Table.Rows[$eventArgs.RowIndex]
            $sender.Refresh()
        }
    })

    $exportButton.add_Click({
        $dialog = [System.Windows.Forms.SaveFileDialog]::new()
        $dialog.Filter = 'CSV files (*.csv)|*.csv'
        $dialog.FileName = ([System.IO.Path]::GetFileNameWithoutExtension($SourcePath) + '_BarTender.csv')
        $dialog.InitialDirectory = [System.IO.Path]::GetDirectoryName($SourcePath)
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            Export-BarTenderCsv $Table $dialog.FileName
            [System.Windows.Forms.MessageBox]::Show(
                "CSV exported successfully.`r`n$($dialog.FileName)",
                'Conversion complete',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        }
    })

    [void]$form.ShowDialog()
}

if ([string]::IsNullOrWhiteSpace($InputPath)) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.Application]::EnableVisualStyles()
    $openDialog = [System.Windows.Forms.OpenFileDialog]::new()
    $openDialog.Title = 'Select an original 1688 order workbook'
    $openDialog.Filter = 'Excel workbooks (*.xlsx)|*.xlsx'
    if ($openDialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }
    $InputPath = $openDialog.FileName
}

$workbookData = Read-1688Workbook $InputPath
$dataTable = Convert-ToDataTable $workbookData

if ($NoGui) {
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $OutputPath = Join-Path ([System.IO.Path]::GetDirectoryName((Resolve-Path -LiteralPath $InputPath).Path)) (([System.IO.Path]::GetFileNameWithoutExtension($InputPath)) + '_BarTender.csv')
    }
    Export-BarTenderCsv $dataTable $OutputPath
    Write-Host "Conversion completed successfully."
    Write-Host "Source rows            : $($dataTable.Rows.Count)"
    Write-Host "Merged ranges expanded : $($workbookData.MergedRangeCount)"
    Write-Host "Output                 : $OutputPath"
}
else {
    Show-ConverterWindow $dataTable (Resolve-Path -LiteralPath $InputPath).Path $workbookData.MergedRangeCount
}
