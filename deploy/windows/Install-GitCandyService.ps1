[CmdletBinding()]
param(
  [string]$InstallDir = "$env:ProgramFiles\GitCandy",
  [string]$DataDir = "$env:ProgramData\GitCandy"
)

$ErrorActionPreference = 'Stop'
$serviceName = 'GitCandy'
$packageDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$installPath = [System.IO.Path]::GetFullPath($InstallDir)
$dataPath = [System.IO.Path]::GetFullPath($DataDir)

if ($installPath -eq [System.IO.Path]::GetPathRoot($installPath) -or
    $dataPath -eq [System.IO.Path]::GetPathRoot($dataPath)) {
  throw 'InstallDir and DataDir must not be filesystem roots.'
}

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existingService -and $existingService.Status -ne 'Stopped') {
  Stop-Service -Name $serviceName
  $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

New-Item -ItemType Directory -Force -Path $installPath | Out-Null
New-Item -ItemType Directory -Force -Path $dataPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dataPath 'repositories') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dataPath 'cache') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dataPath 'logs') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dataPath 'data-protection-keys') | Out-Null
Copy-Item -Path (Join-Path $packageDir 'app\*') -Destination $installPath -Recurse -Force

$productionSettings = Join-Path $installPath 'appsettings.Production.json'
$isNewSettings = -not (Test-Path -LiteralPath $productionSettings)
if ($isNewSettings) {
  Copy-Item -LiteralPath (Join-Path $packageDir 'appsettings.Production.json') -Destination $productionSettings
  $settings = Get-Content -LiteralPath $productionSettings -Raw | ConvertFrom-Json
  $settings.GitCandy.Application.RepositoryPath = Join-Path $dataPath 'repositories'
  $settings.GitCandy.Application.CachePath = Join-Path $dataPath 'cache'
  $settings.GitCandy.Application.LogPathFormat = Join-Path $dataPath 'logs\gitcandy-{0}.log'
  $settings.GitCandy.Application.UserConfigurationPath = Join-Path $dataPath 'config.xml'
  $settings.GitCandy.Application.SshHostKeyPath = Join-Path $dataPath 'ssh-host-key.xml'
  $settings.GitCandy.Application.DataProtectionKeysPath = Join-Path $dataPath 'data-protection-keys'
  $settings.ConnectionStrings.GitCandy = "Data Source=$(Join-Path $dataPath 'GitCandy.db')"
  $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $productionSettings -Encoding UTF8
}

$executable = Join-Path $installPath 'GitCandy.exe'
& $executable --migrate --environment Production
if ($LASTEXITCODE -ne 0) {
  throw "Database migration failed with exit code $LASTEXITCODE."
}

if ($null -eq $existingService) {
  & sc.exe create $serviceName binPath= "`"$executable`"" start= auto obj= 'NT SERVICE\GitCandy'
  if ($LASTEXITCODE -ne 0) {
    throw "Service creation failed with exit code $LASTEXITCODE."
  }
} else {
  & sc.exe config $serviceName binPath= "`"$executable`"" start= auto obj= 'NT SERVICE\GitCandy'
  if ($LASTEXITCODE -ne 0) {
    throw "Service update failed with exit code $LASTEXITCODE."
  }
}

& sc.exe failure $serviceName reset= 86400 actions= 'restart/5000/restart/15000/none/0' | Out-Null
& icacls.exe $dataPath /grant 'NT SERVICE\GitCandy:(OI)(CI)M' /T /C | Out-Null
Start-Service -Name $serviceName
Get-Service -Name $serviceName
