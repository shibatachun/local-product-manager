param(
  [string]$OutputPath = "outputs/news-factor-investing/macro-20261015.json"
)

$ErrorActionPreference = 'Stop'
$symbols = @('HYG','%5ETNX','CL%3DF')
$labels = @('HYG','TNX','CL_F')
$result = [ordered]@{}
$period1 = [DateTimeOffset]::UtcNow.AddDays(-60).ToUnixTimeSeconds()
$period2 = [DateTimeOffset]::UtcNow.AddDays(2).ToUnixTimeSeconds()
for ($j = 0; $j -lt $symbols.Count; $j++) {
  $uri = "https://query1.finance.yahoo.com/v8/finance/chart/$($symbols[$j])?period1=$period1&period2=$period2&interval=1d"
  $chart = (Invoke-RestMethod -Uri $uri -TimeoutSec 30).chart.result[0]
  $quote = $chart.indicators.quote[0]
  $rows = @()
  for ($i = 0; $i -lt $chart.timestamp.Count; $i++) {
    if ($null -eq $quote.close[$i]) { continue }
    $rows += [ordered]@{
      date = [DateTimeOffset]::FromUnixTimeSeconds([long]$chart.timestamp[$i]).UtcDateTime.ToString('yyyy-MM-dd')
      close = [double]$quote.close[$i]
    }
  }
  $result[$labels[$j]] = [ordered]@{ currency=$chart.meta.currency; rows=$rows }
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$result.GetEnumerator() | ForEach-Object { Write-Output ("{0} {1} {2}" -f $_.Key,$_.Value.rows[-1].date,$_.Value.rows[-1].close) }
