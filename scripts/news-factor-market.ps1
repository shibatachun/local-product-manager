param(
  [string]$OutputPath = "outputs/news-factor-investing/market-data-2026-09-14.json"
)

$ErrorActionPreference = 'Stop'
$secret = Join-Path $PSScriptRoot '..\.codex\secrets\alpha-vantage.env'
$line = Get-Content $secret | Where-Object { $_ -match '^ALPHA_VANTAGE_API_KEY=' } | Select-Object -First 1
if (-not $line) { throw 'Alpha Vantage key missing' }
$key = $line.Substring($line.IndexOf('=') + 1).Trim()

$symbols = @('SPY','XOM','XLE','LMT','ITA','PANW','CIBR','MSFT','XLK','JPM','KBE','CAT','XLI','UNH','XLV','WMT','XLP','NVDA','MU','SMH','UPS','IYT')
$all = [ordered]@{}
foreach ($symbol in $symbols) {
  $uri = 'https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol=' + $symbol + '&outputsize=compact&apikey=' + [uri]::EscapeDataString($key)
  $response = Invoke-RestMethod -Uri $uri -TimeoutSec 30
  if (-not $response.'Time Series (Daily)') {
    $reason = if ($response.Note) { 'rate-limited' } elseif ($response.Information) { 'provider-information' } else { 'missing-series' }
    $all[$symbol] = [ordered]@{ error = $reason }
    Start-Sleep -Seconds 13
    continue
  }
  $rows = foreach ($property in $response.'Time Series (Daily)'.PSObject.Properties) {
    $v = $property.Value
    [pscustomobject]@{
      date = $property.Name
      open = [double]$v.'1. open'
      high = [double]$v.'2. high'
      low = [double]$v.'3. low'
      close = [double]$v.'4. close'
      volume = [double]$v.'5. volume'
    }
  }
  $rows = @($rows | Sort-Object date)
  $latest = $rows[-1]
  $previous = $rows[-2]
  $last21 = @($rows[($rows.Count-21)..($rows.Count-1)])
  $prior20 = @($rows[($rows.Count-21)..($rows.Count-2)])
  $returns = for ($i=$rows.Count-20; $i -lt $rows.Count; $i++) { [math]::Log($rows[$i].close / $rows[$i-1].close) }
  $meanRet = ($returns | Measure-Object -Average).Average
  $variance = (($returns | ForEach-Object { [math]::Pow($_ - $meanRet, 2) } | Measure-Object -Sum).Sum) / ($returns.Count - 1)
  $trs = for ($i=$rows.Count-14; $i -lt $rows.Count; $i++) {
    [math]::Max($rows[$i].high-$rows[$i].low, [math]::Max([math]::Abs($rows[$i].high-$rows[$i-1].close), [math]::Abs($rows[$i].low-$rows[$i-1].close)))
  }
  $all[$symbol] = [ordered]@{
    cutoff = $latest.date
    close = $latest.close
    daily_return = $latest.close / $previous.close - 1
    return20 = $latest.close / $rows[-21].close - 1
    ma20 = ($last21[1..20].close | Measure-Object -Average).Average
    ann_vol20 = [math]::Sqrt($variance) * [math]::Sqrt(252)
    volume_ratio = $latest.volume / (($prior20.volume | Measure-Object -Average).Average)
    atr14 = ($trs | Measure-Object -Average).Average
    avg_dollar_volume20 = (($last21[1..20] | ForEach-Object { $_.close * $_.volume }) | Measure-Object -Average).Average
    rows = $rows
  }
  Start-Sleep -Seconds 13
}

$folder = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$all | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 $OutputPath
$summary = foreach ($symbol in $symbols) {
  $x = $all[$symbol]
  [pscustomobject]@{ Symbol=$symbol; Cutoff=$x.cutoff; Close=$x.close; Return20=$x.return20; VolumeRatio=$x.volume_ratio; Error=$x.error }
}
$summary | Format-Table -AutoSize
