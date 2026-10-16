param(
  [Parameter(Mandatory=$true)][string]$InputPath,
  [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$data = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json -AsHashtable
$bench = @{
  MU='SMH'; NVDA='SMH'; AMD='SMH'; INTC='SMH';
  MSFT='XLK'; GOOGL='XLK'; AMZN='XLK';
  AKAM='XLK'; NET='XLK'; ORCL='XLK';
  JPM='KBE'; BAC='KBE'; C='KBE';
  V='XLF'; MA='XLF'; AXP='XLF';
  GM='XLY'; F='XLY'; TM='XLY';
  XOM='XLE'; CVX='XLE'; COP='XLE';
  WMT='XLP'; COST='XLP'; TGT='XLP';
  CMCSA='XLC'; DIS='XLC'
}
function Mean($xs) { ($xs | Measure-Object -Average).Average }
function Metric($symbol) {
  $rows = @($data[$symbol].rows)
  $n = $rows.Count
  $latest=$rows[$n-1]; $prev=$rows[$n-2]; $base=$rows[$n-21]
  $last20=@($rows[($n-20)..($n-1)])
  $prior20=@($rows[($n-21)..($n-2)])
  $logs=@(); for($i=$n-20;$i -lt $n;$i++){ $logs += [math]::Log($rows[$i].adjclose/$rows[$i-1].adjclose) }
  $avg=Mean $logs; $sum=0.0; foreach($x in $logs){$sum += [math]::Pow($x-$avg,2)}
  $trs=@(); for($i=$n-14;$i -lt $n;$i++){ $trs += [math]::Max($rows[$i].high-$rows[$i].low,[math]::Max([math]::Abs($rows[$i].high-$rows[$i-1].close),[math]::Abs($rows[$i].low-$rows[$i-1].close))) }
  return [ordered]@{
    cutoff=$latest.date; price=[double]$latest.close
    daily_return_pct=100*($latest.adjclose/$prev.adjclose-1)
    return20_pct=100*($latest.adjclose/$base.adjclose-1)
    ma20=Mean @($last20 | ForEach-Object {[double]$_.adjclose})
    above_ma20_pct=100*($latest.adjclose/(Mean @($last20|ForEach-Object {[double]$_.adjclose}))-1)
    vol20_pct=100*[math]::Sqrt($sum/($logs.Count-1))*[math]::Sqrt(252)
    volume_ratio=[double]$latest.volume/(Mean @($prior20|ForEach-Object {[double]$_.volume}))
    atr14=Mean $trs
    adv20_usd=Mean @($last20|ForEach-Object {[double]$_.close*[double]$_.volume})
  }
}
$summary=[ordered]@{}
foreach($s in $bench.Keys){
  $m=Metric $s; $spy=Metric 'SPY'; $ind=Metric $bench[$s]
  $m.spy_spread_pp=$m.return20_pct-$spy.return20_pct
  $m.industry_spread_pp=$m.return20_pct-$ind.return20_pct
  $m.relative_average_pp=($m.spy_spread_pp+$m.industry_spread_pp)/2
  $m.M=if($m.relative_average_pp -lt -5){0}elseif($m.relative_average_pp -lt -2){1}elseif($m.relative_average_pp -lt 0){2}elseif($m.relative_average_pp -lt 2){3}elseif($m.relative_average_pp -lt 5){4}else{5}
  $m.V=if($m.daily_return_pct -lt 0){if($m.volume_ratio -ge 1.5){0}else{1}}elseif([math]::Abs($m.daily_return_pct) -lt 0.00001){2}elseif($m.volume_ratio -lt 0.8){1}elseif($m.volume_ratio -lt 1.2){2}elseif($m.volume_ratio -lt 1.5){3}elseif($m.volume_ratio -lt 2){4}else{5}
  $m.benchmark=$bench[$s]
  $summary[$s]=$m
}
$summary['SPY']=Metric 'SPY'
$summary['SMH']=Metric 'SMH'; $summary['XLK']=Metric 'XLK'; $summary['KBE']=Metric 'KBE'; $summary['XLE']=Metric 'XLE'; $summary['XLP']=Metric 'XLP'
$out=[ordered]@{generated_at=(Get-Date).ToString('o'); source=$InputPath; summary=$summary}
$out | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8
foreach($s in @('MU','NVDA','WMT','XOM','MSFT','JPM')){ $m=$summary[$s]; Write-Output ("{0} close={1:N2} day={2:N2}% r20={3:N2}% M={4} V={5} vol={6:N2}" -f $s,$m.price,$m.daily_return_pct,$m.return20_pct,$m.M,$m.V,$m.volume_ratio) }
