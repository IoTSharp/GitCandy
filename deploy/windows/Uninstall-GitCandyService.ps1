[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'GitCandy'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
  Write-Host 'GitCandy service is not installed.'
  return
}

if ($service.Status -ne 'Stopped') {
  Stop-Service -Name $serviceName
  $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

& sc.exe delete $serviceName
if ($LASTEXITCODE -ne 0) {
  throw "Service removal failed with exit code $LASTEXITCODE."
}

Write-Host 'GitCandy service removed. Application and data directories were preserved.'
