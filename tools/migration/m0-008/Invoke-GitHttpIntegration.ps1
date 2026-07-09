[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [string] $WorkRoot,

    [string] $PublicRepository = 'public-demo',

    [string] $PrivateRepository = 'private-demo',

    [string] $MissingRepository = 'missing-demo',

    [string] $OwnerUserName = 'alice',

    [string] $TeamUserName = 'bob',

    [string] $DeniedUserName = 'carol',

    [string] $OwnerPassword = $env:GITCANDY_M0_ALICE_PASSWORD,

    [string] $TeamPassword = $env:GITCANDY_M0_BOB_PASSWORD,

    [string] $DeniedPassword = $env:GITCANDY_M0_CAROL_PASSWORD,

    [switch] $SkipAuthenticatedScenarios,

    [switch] $KeepRemoteBranches
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$script:StepIndex = 0
$script:Steps = @()
$script:Warnings = @()
$script:LogsRoot = $null
$script:SummaryPath = $null
$script:RepoRoot = $null
$script:NormalizedBaseUrl = $null
$script:RunId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$script:WorkRoot = $null
$script:PublicRepository = $PublicRepository
$script:PrivateRepository = $PrivateRepository
$script:MissingRepository = $MissingRepository
$script:NoProxyHosts = 'localhost,127.0.0.1,::1'

function Invoke-RecordedWarning {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $script:Warnings += $Message
    Write-Warning $Message
}

function Get-NormalizedFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInside {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Root,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $normalizedPath = Get-NormalizedFullPath -Path $Path
    $normalizedRoot = Get-NormalizedFullPath -Path $Root
    $comparison = [StringComparison]::OrdinalIgnoreCase

    if ($PSVersionTable.PSEdition -eq 'Core') {
        $isWindows = Get-Variable -Name IsWindows -ValueOnly -ErrorAction SilentlyContinue
        if ($isWindows -eq $false) {
            $comparison = [StringComparison]::Ordinal
        }
    }

    $rootWithSeparator = $normalizedRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $normalizedPath.Equals($normalizedRoot, $comparison) -and -not $normalizedPath.StartsWith($rootWithSeparator, $comparison)) {
        throw "$Description must stay inside $normalizedRoot. Actual path: $normalizedPath"
    }
}

function New-CleanDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $AllowedRoot
    )

    Assert-PathInside -Path $Path -Root $AllowedRoot -Description 'WorkRoot'

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function New-Utf8File {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]] $Lines
    )

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $content = ($Lines -join "`n") + "`n"
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $content, $encoding)
}

function ConvertTo-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BasePath,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $baseUri = [System.Uri]::new((Get-NormalizedFullPath -Path $BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar)
    $pathUri = [System.Uri]::new((Get-NormalizedFullPath -Path $Path))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function ConvertTo-SafeFileName {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $safeName = ($Name -replace '[^A-Za-z0-9_.-]', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        return 'step'
    }

    return $safeName
}

function Set-ScopedEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $Values
    )

    $oldValues = @{}
    foreach ($key in $Values.Keys) {
        $oldValues[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $Values[$key], 'Process')
    }

    return $oldValues
}

function Restore-ScopedEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $Values
    )

    foreach ($key in $Values.Keys) {
        [Environment]::SetEnvironmentVariable($key, $Values[$key], 'Process')
    }
}

function Get-GitBaseEnvironment {
    return @{
        GIT_TERMINAL_PROMPT = '0'
        GIT_ASKPASS = $null
        SSH_ASKPASS = $null
        GIT_TRACE = $null
        GIT_TRACE_PACKET = $null
        GIT_CURL_VERBOSE = $null
        GIT_TRACE_CURL = $null
        GIT_TRACE_CURL_NO_DATA = $null
        NO_PROXY = $script:NoProxyHosts
    }
}

function Merge-Environment {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $Base,

        [Parameter(Mandatory = $true)]
        [hashtable] $Override
    )

    $merged = @{}
    foreach ($key in $Base.Keys) {
        $merged[$key] = $Base[$key]
    }

    foreach ($key in $Override.Keys) {
        $merged[$key] = $Override[$key]
    }

    return $merged
}

function Invoke-ExternalStep {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $FileName,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [hashtable] $Environment = @{},

        [switch] $ExpectFailure,

        [switch] $SensitiveArguments
    )

    $script:StepIndex++
    $safeName = ConvertTo-SafeFileName -Name $Name
    $logPath = Join-Path $script:LogsRoot ('{0:D2}-{1}.log' -f $script:StepIndex, $safeName)
    $displayArguments = if ($SensitiveArguments) { @('<redacted>') } else { $Arguments }
    $exitCode = -1
    $outputText = ''
    $failure = $null
    $oldEnvironment = @{}
    $oldErrorActionPreference = $ErrorActionPreference

    Write-Host ('[{0:D2}] {1}' -f $script:StepIndex, $Name)

    try {
        $oldEnvironment = Set-ScopedEnvironment -Values $Environment
        $ErrorActionPreference = 'Continue'
        $output = & $FileName @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        if ($null -eq $exitCode) {
            $exitCode = 0
        }

        $outputText = ($output | ForEach-Object { $_.ToString() }) -join "`n"
    }
    catch {
        $failure = $_
        $outputText = $_ | Out-String
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
        Restore-ScopedEnvironment -Values $oldEnvironment
    }

    $expected = if ($ExpectFailure) { 'failure' } else { 'success' }
    $passed = if ($ExpectFailure) { $exitCode -ne 0 } else { $exitCode -eq 0 -and $null -eq $failure }

    $logLines = @(
        "Name: $Name",
        "Command: $FileName $($displayArguments -join ' ')",
        "Expected: $expected",
        "ExitCode: $exitCode",
        '',
        'Output:',
        $outputText
    )
    New-Utf8File -Path $logPath -Lines $logLines

    $script:Steps += [ordered]@{
        name = $Name
        command = "$FileName $($displayArguments -join ' ')"
        expected = $expected
        exitCode = $exitCode
        passed = $passed
        log = ConvertTo-RelativePath -BasePath $script:RepoRoot -Path $logPath
    }

    if (-not $passed) {
        $failureDetail = if ($failure) { $failure.Exception.Message } else { "exit code $exitCode" }
        throw "Step '$Name' expected $expected but got $failureDetail. See $logPath"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $outputText
        LogPath = $logPath
    }
}

function Invoke-GitStep {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [hashtable] $Environment = @{},

        [switch] $ExpectFailure
    )

    $gitEnvironment = Merge-Environment -Base (Get-GitBaseEnvironment) -Override $Environment
    $gitArguments = @('-c', 'credential.helper=') + $Arguments

    return Invoke-ExternalStep -Name $Name -FileName 'git' -Arguments $gitArguments -Environment $gitEnvironment -ExpectFailure:$ExpectFailure
}

function New-BasicAuthEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [string] $UserName,

        [Parameter(Mandatory = $true)]
        [string] $Password
    )

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "Password for user '$UserName' is required. Set the matching GITCANDY_M0_*_PASSWORD environment variable or pass the parameter explicitly."
    }

    $bytes = [System.Text.Encoding]::ASCII.GetBytes("${UserName}:$Password")
    $basicValue = [Convert]::ToBase64String($bytes)

    return @{
        GIT_CONFIG_COUNT = '1'
        GIT_CONFIG_KEY_0 = 'http.extraHeader'
        GIT_CONFIG_VALUE_0 = "Authorization: Basic $basicValue"
    }
}

function Get-RepositoryUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryName,

        [switch] $WithoutGitSuffix
    )

    $suffix = if ($WithoutGitSuffix) { '' } else { '.git' }
    return "$script:NormalizedBaseUrl/git/$RepositoryName$suffix"
}

function Add-SmokeCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string] $WorkTree,

        [Parameter(Mandatory = $true)]
        [string] $ScenarioName
    )

    Invoke-GitStep -Name "$ScenarioName-config-user-name" -Arguments @('-C', $WorkTree, 'config', 'user.name', 'GitCandy HTTP Smoke Bot') | Out-Null
    Invoke-GitStep -Name "$ScenarioName-config-user-email" -Arguments @('-C', $WorkTree, 'config', 'user.email', 'git-http-smoke@gitcandy.local') | Out-Null
    Invoke-GitStep -Name "$ScenarioName-config-gpg" -Arguments @('-C', $WorkTree, 'config', 'commit.gpgsign', 'false') | Out-Null

    $relativePath = "m0-http-smoke/$ScenarioName-$script:RunId.txt"
    $filePath = Join-Path $WorkTree ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    New-Utf8File -Path $filePath -Lines @(
        "M0 #008 Git HTTP smoke commit.",
        "Scenario: $ScenarioName",
        "RunId: $script:RunId"
    )

    Invoke-GitStep -Name "$ScenarioName-add" -Arguments @('-C', $WorkTree, 'add', $relativePath) | Out-Null
    Invoke-GitStep -Name "$ScenarioName-commit" -Arguments @('-C', $WorkTree, 'commit', '-m', "M0 #008 Git HTTP smoke: $ScenarioName $script:RunId") | Out-Null
}

function Invoke-VerifiedPush {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $WorkTree,

        [Parameter(Mandatory = $true)]
        [string] $RemoteUrl,

        [Parameter(Mandatory = $true)]
        [string] $BranchName,

        [Parameter(Mandatory = $true)]
        [hashtable] $Environment
    )

    $remoteRef = "refs/heads/$BranchName"
    Invoke-GitStep -Name "$Name-push" -Arguments @('-C', $WorkTree, 'push', $RemoteUrl, "HEAD:$remoteRef") -Environment $Environment | Out-Null
    Invoke-GitStep -Name "$Name-ls-remote" -Arguments @('ls-remote', '--exit-code', $RemoteUrl, $remoteRef) -Environment $Environment | Out-Null

    if ($KeepRemoteBranches) {
        Invoke-RecordedWarning -Message "Kept remote branch $remoteRef for scenario $Name because -KeepRemoteBranches was specified."
        return
    }

    try {
        Invoke-GitStep -Name "$Name-cleanup" -Arguments @('-C', $WorkTree, 'push', $RemoteUrl, ":$remoteRef") -Environment $Environment | Out-Null
    }
    catch {
        Invoke-RecordedWarning -Message "Could not delete remote branch $remoteRef after $Name. Delete it manually if needed. $($_.Exception.Message)"
    }
}

function Assert-RequiredSecrets {
    param(
        [Parameter(Mandatory = $true)]
        [string] $OwnerSecret,

        [Parameter(Mandatory = $true)]
        [string] $TeamSecret,

        [Parameter(Mandatory = $true)]
        [string] $DeniedSecret
    )

    $missing = @()
    if ([string]::IsNullOrWhiteSpace($OwnerSecret)) {
        $missing += 'GITCANDY_M0_ALICE_PASSWORD'
    }

    if ([string]::IsNullOrWhiteSpace($TeamSecret)) {
        $missing += 'GITCANDY_M0_BOB_PASSWORD'
    }

    if ([string]::IsNullOrWhiteSpace($DeniedSecret)) {
        $missing += 'GITCANDY_M0_CAROL_PASSWORD'
    }

    if ($missing.Count -gt 0) {
        throw "Authenticated Git HTTP scenarios require these environment variables: $($missing -join ', '). Use -SkipAuthenticatedScenarios for anonymous-only checks."
    }
}

function Write-Summary {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Status,

        [string] $FailureMessage
    )

    if ([string]::IsNullOrWhiteSpace($script:SummaryPath)) {
        return
    }

    $summary = [ordered]@{
        schemaVersion = 1
        fixture = 'M0-008'
        status = $Status
        failure = $FailureMessage
        runId = $script:RunId
        baseUrl = $script:NormalizedBaseUrl
        workRoot = ConvertTo-RelativePath -BasePath $script:RepoRoot -Path $script:WorkRoot
        repositories = [ordered]@{
            public = $script:PublicRepository
            private = $script:PrivateRepository
            missing = $script:MissingRepository
        }
        authenticatedScenariosSkipped = [bool] $SkipAuthenticatedScenarios
        remoteBranchesKept = [bool] $KeepRemoteBranches
        steps = $script:Steps
        warnings = $script:Warnings
    }

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($script:SummaryPath, ($summary | ConvertTo-Json -Depth 12) + "`n", $encoding)
}

function Invoke-GitHttpScenarios {
    $clonesRoot = Join-Path $script:WorkRoot 'clones'
    New-Item -ItemType Directory -Path $clonesRoot -Force | Out-Null

    $publicUrl = Get-RepositoryUrl -RepositoryName $PublicRepository
    $publicUrlWithoutGitSuffix = Get-RepositoryUrl -RepositoryName $PublicRepository -WithoutGitSuffix
    $privateUrl = Get-RepositoryUrl -RepositoryName $PrivateRepository
    $missingUrl = Get-RepositoryUrl -RepositoryName $MissingRepository

    Invoke-GitStep -Name 'preflight-git-version' -Arguments @('--version') | Out-Null

    $publicAnonymousClone = Join-Path $clonesRoot 'public-anonymous'
    Invoke-GitStep -Name 'public-anonymous-clone' -Arguments @('clone', $publicUrl, $publicAnonymousClone) | Out-Null
    Invoke-GitStep -Name 'public-anonymous-fetch' -Arguments @('-C', $publicAnonymousClone, 'fetch', '--all', '--tags') | Out-Null

    $publicNoSuffixClone = Join-Path $clonesRoot 'public-no-suffix'
    Invoke-GitStep -Name 'public-no-suffix-clone' -Arguments @('clone', $publicUrlWithoutGitSuffix, $publicNoSuffixClone) | Out-Null

    $anonymousDeniedBranch = "m0-http-smoke/$script:RunId/public-anonymous-denied"
    Invoke-GitStep -Name 'public-anonymous-push-denied-branch' -Arguments @('-C', $publicAnonymousClone, 'switch', '-c', $anonymousDeniedBranch) | Out-Null
    Add-SmokeCommit -WorkTree $publicAnonymousClone -ScenarioName 'public-anonymous-push-denied'
    Invoke-GitStep -Name 'public-anonymous-push-denied' -Arguments @('-C', $publicAnonymousClone, 'push', $publicUrl, "HEAD:refs/heads/$anonymousDeniedBranch") -ExpectFailure | Out-Null

    Invoke-GitStep -Name 'private-anonymous-clone-denied' -Arguments @('clone', $privateUrl, (Join-Path $clonesRoot 'private-anonymous-denied')) -ExpectFailure | Out-Null
    Invoke-GitStep -Name 'missing-repository-clone-denied' -Arguments @('clone', $missingUrl, (Join-Path $clonesRoot 'missing-repository-denied')) -ExpectFailure | Out-Null

    if ($SkipAuthenticatedScenarios) {
        Invoke-RecordedWarning -Message 'Authenticated Git HTTP scenarios were skipped. Push success and authenticated private repository coverage were not verified.'
        return
    }

    Assert-RequiredSecrets -OwnerSecret $OwnerPassword -TeamSecret $TeamPassword -DeniedSecret $DeniedPassword

    $ownerEnvironment = New-BasicAuthEnvironment -UserName $OwnerUserName -Password $OwnerPassword
    $teamEnvironment = New-BasicAuthEnvironment -UserName $TeamUserName -Password $TeamPassword
    $deniedEnvironment = New-BasicAuthEnvironment -UserName $DeniedUserName -Password $DeniedPassword
    $wrongPasswordEnvironment = New-BasicAuthEnvironment -UserName $OwnerUserName -Password "m0-008-invalid-password-$script:RunId"

    Invoke-GitStep -Name 'public-owner-push-switch-main' -Arguments @('-C', $publicAnonymousClone, 'switch', 'main') | Out-Null
    $publicOwnerBranch = "m0-http-smoke/$script:RunId/public-owner"
    Invoke-GitStep -Name 'public-owner-push-branch' -Arguments @('-C', $publicAnonymousClone, 'switch', '-c', $publicOwnerBranch) | Out-Null
    Add-SmokeCommit -WorkTree $publicAnonymousClone -ScenarioName 'public-owner-push'
    Invoke-VerifiedPush -Name 'public-owner' -WorkTree $publicAnonymousClone -RemoteUrl $publicUrl -BranchName $publicOwnerBranch -Environment $ownerEnvironment

    $privateTeamClone = Join-Path $clonesRoot 'private-team'
    Invoke-GitStep -Name 'private-team-clone' -Arguments @('clone', $privateUrl, $privateTeamClone) -Environment $teamEnvironment | Out-Null
    Invoke-GitStep -Name 'private-team-fetch' -Arguments @('-C', $privateTeamClone, 'fetch', '--all', '--tags') -Environment $teamEnvironment | Out-Null

    $privateTeamBranch = "m0-http-smoke/$script:RunId/private-team"
    Invoke-GitStep -Name 'private-team-push-branch' -Arguments @('-C', $privateTeamClone, 'switch', '-c', $privateTeamBranch) | Out-Null
    Add-SmokeCommit -WorkTree $privateTeamClone -ScenarioName 'private-team-push'
    Invoke-VerifiedPush -Name 'private-team' -WorkTree $privateTeamClone -RemoteUrl $privateUrl -BranchName $privateTeamBranch -Environment $teamEnvironment

    Invoke-GitStep -Name 'private-denied-user-clone-denied' -Arguments @('clone', $privateUrl, (Join-Path $clonesRoot 'private-denied-user')) -Environment $deniedEnvironment -ExpectFailure | Out-Null
    Invoke-GitStep -Name 'private-wrong-password-clone-denied' -Arguments @('clone', $privateUrl, (Join-Path $clonesRoot 'private-wrong-password')) -Environment $wrongPasswordEnvironment -ExpectFailure | Out-Null
}

$repoRootOutput = & git rev-parse --show-toplevel 2>&1
if ($LASTEXITCODE -ne 0) {
    $repoRootError = ($repoRootOutput | ForEach-Object { $_.ToString() }) -join "`n"
    throw "Could not determine repository root. git rev-parse failed: $repoRootError"
}

$script:RepoRoot = Get-NormalizedFullPath -Path (($repoRootOutput | ForEach-Object { $_.ToString() }) -join "`n").Trim()
$artifactsRoot = Join-Path $script:RepoRoot 'artifacts'
$m0ArtifactsRoot = Join-Path (Join-Path $artifactsRoot 'migration') 'm0-008'

if ([string]::IsNullOrWhiteSpace($WorkRoot)) {
    $WorkRoot = Join-Path $m0ArtifactsRoot 'work'
}

$script:WorkRoot = Get-NormalizedFullPath -Path $WorkRoot
Assert-PathInside -Path $script:WorkRoot -Root $m0ArtifactsRoot -Description 'WorkRoot'
New-CleanDirectory -Path $script:WorkRoot -AllowedRoot $m0ArtifactsRoot
$script:LogsRoot = Join-Path $script:WorkRoot 'logs'
New-Item -ItemType Directory -Path $script:LogsRoot -Force | Out-Null
$script:SummaryPath = Join-Path $script:WorkRoot 'summary.json'

$baseUri = [System.Uri]::new($BaseUrl)
if (-not $baseUri.IsAbsoluteUri -or ($baseUri.Scheme -ne 'http' -and $baseUri.Scheme -ne 'https')) {
    throw "BaseUrl must be an absolute HTTP or HTTPS URL. Actual value: $BaseUrl"
}

$script:NormalizedBaseUrl = $baseUri.AbsoluteUri.TrimEnd('/')
$noProxyHosts = @('localhost', '127.0.0.1', '::1')
if (-not [string]::IsNullOrWhiteSpace($baseUri.Host) -and $noProxyHosts -notcontains $baseUri.Host) {
    $noProxyHosts += $baseUri.Host
}

$script:NoProxyHosts = $noProxyHosts -join ','

$status = 'Failed'
$failureMessage = $null
try {
    Invoke-GitHttpScenarios
    $status = 'Passed'
}
catch {
    $failureMessage = $_.Exception.Message
    throw
}
finally {
    Write-Summary -Status $status -FailureMessage $failureMessage
}

Write-Host "M0 #008 Git HTTP integration script completed."
Write-Host "Summary: $script:SummaryPath"
