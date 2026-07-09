[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [string] $OutputRoot,

    [string] $AdminUser = 'admin',

    [string] $AdminPassword = $env:GITCANDY_M0_ADMIN_PASSWORD,

    [string] $PublicRepository,

    [switch] $ExpectCustomErrors,

    [int] $TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Net.Http

function Read-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed: git $($Arguments -join ' ')"
    }

    return ($output -join "`n").Trim()
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

function New-CaseResult {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Passed', 'Failed', 'Skipped')]
        [string] $Status,

        [Parameter(Mandatory = $true)]
        [string] $Expected,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Actual,

        [AllowEmptyString()]
        [string] $Notes = ''
    )

    $script:Results.Add([ordered]@{
        name = $Name
        status = $Status
        expected = $Expected
        actual = $Actual
        notes = $Notes
    }) | Out-Null

    if ($Status -eq 'Failed') {
        $script:FailureCount++
        Write-Host "[FAIL] $Name - $Actual"
    }
    elseif ($Status -eq 'Skipped') {
        Write-Host "[SKIP] $Name - $Notes"
    }
    else {
        Write-Host "[PASS] $Name"
    }
}

function New-FormContent {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $Form
    )

    $items = [System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string, string]]]::new()
    foreach ($key in $Form.Keys) {
        $items.Add([System.Collections.Generic.KeyValuePair[string, string]]::new($key, [string] $Form[$key])) | Out-Null
    }

    return [System.Net.Http.FormUrlEncodedContent]::new($items)
}

function Resolve-SmokeUri {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if ($Path.StartsWith('http://', [StringComparison]::OrdinalIgnoreCase) -or $Path.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
        return [Uri]::new($Path)
    }

    return [Uri]::new($script:BaseUri, $Path.TrimStart('/'))
}

function Invoke-SmokeRequest {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('GET', 'POST')]
        [string] $Method,

        [Parameter(Mandatory = $true)]
        [string] $Path,

        [hashtable] $Form
    )

    $uri = Resolve-SmokeUri -Path $Path
    $httpMethod = if ($Method -eq 'GET') { [System.Net.Http.HttpMethod]::Get } else { [System.Net.Http.HttpMethod]::Post }
    $request = [System.Net.Http.HttpRequestMessage]::new($httpMethod, $uri)
    $request.Headers.UserAgent.ParseAdd('GitCandy-M0-MvcSmoke/1.0')
    $request.Headers.AcceptLanguage.ParseAdd('en-US,en;q=0.9')

    if ($Method -eq 'POST') {
        if ($null -eq $Form) {
            $Form = @{}
        }
        $request.Content = New-FormContent -Form $Form
    }

    return $script:HttpClient.SendAsync($request).GetAwaiter().GetResult()
}

function Read-ResponseText {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpResponseMessage] $Response
    )

    return $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}

function Test-ContainsAll {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Content,

        [Parameter(Mandatory = $true)]
        [string[]] $Snippets
    )

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($snippet in $Snippets) {
        if ($Content.IndexOf($snippet, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            $missing.Add($snippet) | Out-Null
        }
    }

    return $missing
}

function Assert-Redirect {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $TargetContains
    )

    try {
        $response = Invoke-SmokeRequest -Method GET -Path $Path
        $statusCode = [int] $response.StatusCode
        $location = if ($response.Headers.Location) { $response.Headers.Location.ToString() } else { '' }
        if ((301, 302, 303, 307, 308) -notcontains $statusCode) {
            New-CaseResult -Name $Name -Status Failed -Expected "Redirect to $TargetContains" -Actual "HTTP $statusCode"
            return
        }

        if ($location.IndexOf($TargetContains, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            New-CaseResult -Name $Name -Status Failed -Expected "Redirect location contains $TargetContains" -Actual "Location: $location"
            return
        }

        New-CaseResult -Name $Name -Status Passed -Expected "Redirect to $TargetContains" -Actual "HTTP $statusCode Location: $location"
    }
    catch {
        New-CaseResult -Name $Name -Status Failed -Expected "Redirect to $TargetContains" -Actual $_.Exception.Message
    }
}

function Assert-Page {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [int[]] $ExpectedStatusCodes,

        [string[]] $RequiredSnippets = @()
    )

    try {
        $response = Invoke-SmokeRequest -Method GET -Path $Path
        $statusCode = [int] $response.StatusCode
        $content = Read-ResponseText -Response $response
        if ($ExpectedStatusCodes -notcontains $statusCode) {
            New-CaseResult -Name $Name -Status Failed -Expected "HTTP $($ExpectedStatusCodes -join '/')" -Actual "HTTP $statusCode"
            return
        }

        if ($RequiredSnippets.Count -gt 0) {
            $missing = Test-ContainsAll -Content $content -Snippets $RequiredSnippets
            if ($missing.Count -gt 0) {
                New-CaseResult -Name $Name -Status Failed -Expected "Page contains required snippets" -Actual "Missing: $($missing -join ', ')"
                return
            }
        }

        New-CaseResult -Name $Name -Status Passed -Expected "HTTP $($ExpectedStatusCodes -join '/')" -Actual "HTTP $statusCode"
    }
    catch {
        New-CaseResult -Name $Name -Status Failed -Expected "HTTP $($ExpectedStatusCodes -join '/')" -Actual $_.Exception.Message
    }
}

function Assert-OptionalPublicForm {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string[]] $RequiredSnippets
    )

    try {
        $response = Invoke-SmokeRequest -Method GET -Path $Path
        $statusCode = [int] $response.StatusCode
        if ((301, 302, 303, 307, 308) -contains $statusCode) {
            $location = if ($response.Headers.Location) { $response.Headers.Location.ToString() } else { '' }
            New-CaseResult -Name $Name -Status Skipped -Expected 'HTTP 200 form when enabled by configuration' -Actual "HTTP $statusCode" -Notes "Configuration or authorization redirected to $location"
            return
        }

        $content = Read-ResponseText -Response $response
        if ($statusCode -ne 200) {
            New-CaseResult -Name $Name -Status Failed -Expected 'HTTP 200 form or auth redirect' -Actual "HTTP $statusCode"
            return
        }

        $missing = Test-ContainsAll -Content $content -Snippets $RequiredSnippets
        if ($missing.Count -gt 0) {
            New-CaseResult -Name $Name -Status Failed -Expected 'Form contains required fields' -Actual "Missing: $($missing -join ', ')"
            return
        }

        New-CaseResult -Name $Name -Status Passed -Expected 'HTTP 200 form' -Actual 'HTTP 200'
    }
    catch {
        New-CaseResult -Name $Name -Status Failed -Expected 'HTTP 200 form or auth redirect' -Actual $_.Exception.Message
    }
}

function Assert-LoginFailurePost {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    try {
        $response = Invoke-SmokeRequest -Method POST -Path '/Account/Login?ReturnUrl=%2FRepository%2FIndex' -Form @{
            ID = '__m0_missing_user__'
            Password = '__m0_wrong_password__'
        }
        $statusCode = [int] $response.StatusCode
        $content = Read-ResponseText -Response $response
        if ($statusCode -ne 200) {
            New-CaseResult -Name $Name -Status Failed -Expected 'HTTP 200 login form with validation' -Actual "HTTP $statusCode"
            return
        }

        $missing = Test-ContainsAll -Content $content -Snippets @('name="ID"', 'name="Password"')
        if ($missing.Count -gt 0) {
            New-CaseResult -Name $Name -Status Failed -Expected 'Login form is returned after failed login' -Actual "Missing: $($missing -join ', ')"
            return
        }

        New-CaseResult -Name $Name -Status Passed -Expected 'HTTP 200 login form with validation' -Actual 'HTTP 200'
    }
    catch {
        New-CaseResult -Name $Name -Status Failed -Expected 'HTTP 200 login form with validation' -Actual $_.Exception.Message
    }
}

function Assert-ErrorPage {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $path = '/__m0_missing_page_' + [Guid]::NewGuid().ToString('N')

    try {
        $response = Invoke-SmokeRequest -Method GET -Path $path
        $statusCode = [int] $response.StatusCode
        $content = Read-ResponseText -Response $response
        if ($statusCode -ne 404) {
            New-CaseResult -Name $Name -Status Failed -Expected 'HTTP 404' -Actual "HTTP $statusCode"
            return
        }

        if ([string]::IsNullOrWhiteSpace($content)) {
            New-CaseResult -Name $Name -Status Failed -Expected 'HTTP 404 with error response body' -Actual 'Empty response body'
            return
        }

        if ($ExpectCustomErrors -and $content.IndexOf('HTTP Error 404', [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            New-CaseResult -Name $Name -Status Failed -Expected 'CustomErrors/404.html body' -Actual '404 response did not contain the custom error marker'
            return
        }

        New-CaseResult -Name $Name -Status Passed -Expected 'HTTP 404 error response' -Actual 'HTTP 404'
    }
    catch {
        New-CaseResult -Name $Name -Status Failed -Expected 'HTTP 404 error response' -Actual $_.Exception.Message
    }
}

function Assert-AdminLoginAndForms {
    if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
        New-CaseResult -Name 'Authenticated form smoke tests' -Status Skipped -Expected 'Admin password supplied through parameter or GITCANDY_M0_ADMIN_PASSWORD' -Actual '' -Notes 'Set -AdminPassword or GITCANDY_M0_ADMIN_PASSWORD to check admin-only forms.'
        return
    }

    try {
        $response = Invoke-SmokeRequest -Method POST -Path '/Account/Login?ReturnUrl=%2FRepository%2FIndex' -Form @{
            ID = $AdminUser
            Password = $AdminPassword
        }
        $statusCode = [int] $response.StatusCode
        $location = if ($response.Headers.Location) { $response.Headers.Location.ToString() } else { '' }
        if ((301, 302, 303, 307, 308) -notcontains $statusCode) {
            New-CaseResult -Name 'Admin login' -Status Failed -Expected 'Redirect after successful login' -Actual "HTTP $statusCode"
            return
        }

        if ($location.IndexOf('/Repository/Index', [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            New-CaseResult -Name 'Admin login' -Status Failed -Expected 'Redirect to /Repository/Index' -Actual "Location: $location"
            return
        }

        New-CaseResult -Name 'Admin login' -Status Passed -Expected 'Redirect to /Repository/Index' -Actual "HTTP $statusCode Location: $location"
    }
    catch {
        New-CaseResult -Name 'Admin login' -Status Failed -Expected 'Redirect after successful login' -Actual $_.Exception.Message
        return
    }

    Assert-Page -Name 'Repository create form' -Path '/Repository/Create' -ExpectedStatusCodes @(200) -RequiredSnippets @(
        'name="Name"',
        'name="IsPrivate"',
        'name="AllowAnonymousRead"',
        'name="AllowAnonymousWrite"',
        'name="Description"',
        'name="HowInit"',
        'name="RemoteUrl"'
    )
    Assert-Page -Name 'Team create form' -Path '/Team/Create' -ExpectedStatusCodes @(200) -RequiredSnippets @(
        'name="Name"',
        'name="Description"'
    )
    Assert-Page -Name 'Settings form' -Path '/Setting/Edit' -ExpectedStatusCodes @(200) -RequiredSnippets @(
        'name="IsPublicServer"',
        'name="ForceSsl"',
        'name="AllowRegisterUser"',
        'name="AllowRepositoryCreation"',
        'name="RepositoryPath"',
        'name="CachePath"',
        'name="GitCorePath"'
    )
    Assert-Page -Name 'User list page' -Path '/Account/Index' -ExpectedStatusCodes @(200) -RequiredSnippets @(
        '<table',
        'name="query"'
    )
    Assert-Redirect -Name 'Logout redirects to repository list' -Path '/Account/Logout?ReturnUrl=%2FRepository%2FIndex' -TargetContains '/Repository/Index'
}

$normalizedBase = $BaseUrl.Trim()
if (-not $normalizedBase.EndsWith('/')) {
    $normalizedBase += '/'
}
$script:BaseUri = [Uri]::new($normalizedBase)

$repoRoot = Read-GitOutput -Arguments @('rev-parse', '--show-toplevel')
$repoRoot = Get-NormalizedFullPath -Path $repoRoot
$defaultOutputRoot = Join-Path (Join-Path (Join-Path $repoRoot 'artifacts') 'migration') 'm0-007'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $defaultOutputRoot
}

$OutputRoot = Get-NormalizedFullPath -Path $OutputRoot
Assert-PathInside -Path $OutputRoot -Root $defaultOutputRoot -Description 'OutputRoot'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate

$script:HttpClient = [System.Net.Http.HttpClient]::new($handler)
$script:HttpClient.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
$script:Results = [System.Collections.Generic.List[object]]::new()
$script:FailureCount = 0

try {
    Assert-Redirect -Name 'Home page redirects to repository list' -Path '/' -TargetContains '/Repository/Index'
    Assert-Page -Name 'Repository list page' -Path '/Repository/Index' -ExpectedStatusCodes @(200) -RequiredSnippets @(
        '<table',
        'Account/Login'
    )
    Assert-Page -Name 'Login page' -Path '/Account/Login?ReturnUrl=%2FRepository%2FIndex' -ExpectedStatusCodes @(200) -RequiredSnippets @(
        'name="ID"',
        'name="Password"',
        'type="submit"'
    )
    Assert-LoginFailurePost -Name 'Login failure keeps user on login form'
    Assert-OptionalPublicForm -Name 'Account create form' -Path '/Account/Create' -RequiredSnippets @(
        'name="Name"',
        'name="Nickname"',
        'name="Password"',
        'name="ConformPassword"',
        'name="Email"',
        'name="Description"'
    )

    if ([string]::IsNullOrWhiteSpace($PublicRepository)) {
        New-CaseResult -Name 'Public repository tree page' -Status Skipped -Expected 'Public repository name supplied with -PublicRepository' -Actual '' -Notes 'This optional check needs a seeded repository record and bare repository.'
    }
    else {
        Assert-Page -Name 'Public repository tree page' -Path "/Repository/Tree/$PublicRepository" -ExpectedStatusCodes @(200) -RequiredSnippets @(
            $PublicRepository,
            "/git/$PublicRepository.git"
        )
    }

    Assert-ErrorPage -Name 'Missing page returns error page'
    Assert-AdminLoginAndForms
}
finally {
    $script:HttpClient.Dispose()
}

$summary = [ordered]@{
    fixture = 'M0-007'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    baseUrl = $script:BaseUri.ToString()
    adminUser = $AdminUser
    adminPasswordSupplied = -not [string]::IsNullOrWhiteSpace($AdminPassword)
    publicRepository = $PublicRepository
    expectCustomErrors = [bool] $ExpectCustomErrors
    failures = $script:FailureCount
    cases = $script:Results
}

$resultPath = Join-Path $OutputRoot 'mvc-smoke-results.json'
$json = $summary | ConvertTo-Json -Depth 12
$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($resultPath, $json + "`n", $encoding)

Write-Host "M0 #007 MVC smoke results: $resultPath"
if ($script:FailureCount -gt 0) {
    throw "M0 #007 MVC smoke tests failed: $script:FailureCount failure(s)."
}
