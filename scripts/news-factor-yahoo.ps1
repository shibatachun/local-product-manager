param(
  [string]$OutputPath = "outputs/news-factor-investing/market-data-yahoo.json"
)

$ErrorActionPreference = 'Stop'
$symbols = @('SPY','MU','NVDA','AMD','INTC','SMH','MSFT','GOOGL','AMZN','AKAM','NET','ORCL','XLK','JPM','BAC','C','KBE','V','MA','AXP','XLF','GM','F','TM','XLY','XOM','CVX','COP','XLE','WMT','COST','TGT','XLP','CMCSA','DIS','XLC')
$result = [ordered]@{}
$period1 = [DateTimeOffset]::Parse('2023-01-01T00:00:00Z').ToUnixTimeSeconds()
$period2 = [DateTimeOffset]::UtcNow.AddDays(2).ToUnixTimeSeconds()

foreach ($symbol in $symbols) {
  Write-Output "Fetching $symbol"
  $uri = "https://query1.finance.yahoo.com/v8/finance/chart/${symbol}?period1=$period1&period2=$period2&interval=1d&events=div%2Csplits&includeAdjustedClose=true"
  $json = Invoke-RestMethod -Uri $uri -TimeoutSec 30
  $chart = $json.chart.result[0]
  $quote = $chart.indicators.quote[0]
  $adj = $chart.indicators.adjclose[0].adjclose
  $rows = @()
  for ($i = 0; $i -lt $chart.timestamp.Count; $i++) {
    if ($null -eq $quote.close[$i] -or $null -eq $adj[$i]) { continue }
    $rows += [ordered]@{
      date = [DateTimeOffset]::FromUnixTimeSeconds([long]$chart.timestamp[$i]).UtcDateTime.ToString('yyyy-MM-dd')
      open = [double]$quote.open[$i]
      high = [double]$quote.high[$i]
      low = [double]$quote.low[$i]
      close = [double]$quote.close[$i]
      adjclose = [double]$adj[$i]
      volume = [double]$quote.volume[$i]
    }
  }
  $result[$symbol] = [ordered]@{
    currency = $chart.meta.currency
    exchange = $chart.meta.exchangeName
    adjustment = 'Yahoo adjclose for returns; unadjusted close/OHLC for quote and ATR'
    rows = $rows
  }
}

$folder = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Saved $OutputPath"
foreach ($symbol in $symbols) {
  $row = $result[$symbol].rows[-1]
  Write-Output ("{0} {1} {2}" -f $symbol,$row.date,$row.close)
}
