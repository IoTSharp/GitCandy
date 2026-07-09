[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$SkipRestore,

    [switch]$SkipTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$solutionPath = Join-Path $repoRoot "GitCandy.slnx"

if (-not (Test-Path $solutionPath)) {
    throw "Migration solution was not found: $solutionPath"
}

Push-Location $repoRoot
try {
    if (-not $SkipRestore) {
        Invoke-DotNet restore ".\GitCandy.slnx"
    }

    Invoke-DotNet build ".\GitCandy.slnx" --configuration $Configuration --no-restore

    if (-not $SkipTest) {
        Invoke-DotNet test ".\GitCandy.slnx" --configuration $Configuration --no-build
    }
}
finally {
    Pop-Location
}
