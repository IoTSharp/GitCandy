param(
  [Parameter(Mandatory = $true)][string]$ResultsPath,
  [ValidateRange(0, 100)][double]$MinimumLineRate = 80
)
$ErrorActionPreference = 'Stop'
$reports = Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter coverage.cobertura.xml
if ($reports.Count -eq 0) { throw "No Cobertura reports were found under $ResultsPath." }
foreach ($report in $reports) {
  [xml]$coverage = Get-Content -Raw -LiteralPath $report.FullName
  $lineRate = [double]$coverage.coverage.'line-rate' * 100
  Write-Host "$($report.FullName): $($lineRate.ToString('F2'))%"
  if ($lineRate -lt $MinimumLineRate) { throw "Coverage $($lineRate.ToString('F2'))% is below $MinimumLineRate%." }
}
