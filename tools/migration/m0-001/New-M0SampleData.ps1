[CmdletBinding()]
param(
    [string] $OutputRoot,
    [switch] $Verify,
    [switch] $KeepWorktrees
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed: git $($Arguments -join ' ')"
    }
}

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

function New-CleanDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $AllowedRoot
    )

    Assert-PathInside -Path $Path -Root $AllowedRoot -Description 'Output path'

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

function Invoke-FixtureCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string] $WorkTree,

        [Parameter(Mandatory = $true)]
        [string] $Message,

        [Parameter(Mandatory = $true)]
        [string] $Date
    )

    $oldAuthorDate = [Environment]::GetEnvironmentVariable('GIT_AUTHOR_DATE', 'Process')
    $oldCommitterDate = [Environment]::GetEnvironmentVariable('GIT_COMMITTER_DATE', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('GIT_AUTHOR_DATE', $Date, 'Process')
        [Environment]::SetEnvironmentVariable('GIT_COMMITTER_DATE', $Date, 'Process')
        Invoke-Git -Arguments @('-C', $WorkTree, 'commit', '-m', $Message)
    }
    finally {
        [Environment]::SetEnvironmentVariable('GIT_AUTHOR_DATE', $oldAuthorDate, 'Process')
        [Environment]::SetEnvironmentVariable('GIT_COMMITTER_DATE', $oldCommitterDate, 'Process')
    }
}

function Initialize-WorkTree {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    Invoke-Git -Arguments @('init', '--initial-branch=main', $Path)
    Invoke-Git -Arguments @('-C', $Path, 'config', 'user.name', 'GitCandy Fixture Bot')
    Invoke-Git -Arguments @('-C', $Path, 'config', 'user.email', 'fixture@gitcandy.local')
    Invoke-Git -Arguments @('-C', $Path, 'config', 'commit.gpgsign', 'false')
    New-Utf8File -Path (Join-Path $Path '.gitattributes') -Lines @('* text eol=lf')
}

function New-PublicRepository {
    param(
        [Parameter(Mandatory = $true)]
        [string] $WorkTree,

        [Parameter(Mandatory = $true)]
        [string] $BareRepository
    )

    Initialize-WorkTree -Path $WorkTree

    New-Utf8File -Path (Join-Path $WorkTree 'README.md') -Lines @(
        '# Public Demo',
        '',
        'This repository is readable by anonymous users in the M0 sample data.',
        'It exists to protect public clone and fetch behavior during migration.'
    )
    New-Utf8File -Path (Join-Path $WorkTree 'src/hello.txt') -Lines @(
        'hello from GitCandy public demo'
    )
    New-Utf8File -Path (Join-Path $WorkTree 'docs/overview.md') -Lines @(
        '# Overview',
        '',
        'Public repositories allow anonymous read access and authenticated writes only.'
    )
    Invoke-Git -Arguments @('-C', $WorkTree, 'add', '.')
    Invoke-FixtureCommit -WorkTree $WorkTree -Message 'Initial public sample content' -Date '2026-07-09T00:00:00Z'

    Invoke-Git -Arguments @('-C', $WorkTree, 'switch', '-c', 'docs')
    New-Utf8File -Path (Join-Path $WorkTree 'docs/permissions.md') -Lines @(
        '# Permissions',
        '',
        'Anonymous users can read this repository.',
        'Only authorized users can push.'
    )
    Invoke-Git -Arguments @('-C', $WorkTree, 'add', '.')
    Invoke-FixtureCommit -WorkTree $WorkTree -Message 'Document public permissions' -Date '2026-07-09T00:05:00Z'

    Invoke-Git -Arguments @('-C', $WorkTree, 'switch', 'main')
    New-Utf8File -Path (Join-Path $WorkTree 'CHANGELOG.md') -Lines @(
        '# Changelog',
        '',
        '## 0.1.0',
        '',
        '- Added the public sample repository fixture.'
    )
    Invoke-Git -Arguments @('-C', $WorkTree, 'add', '.')
    Invoke-FixtureCommit -WorkTree $WorkTree -Message 'Prepare public sample release' -Date '2026-07-09T00:10:00Z'
    Invoke-Git -Arguments @('-C', $WorkTree, 'tag', 'v0.1.0')

    Publish-BareRepository -WorkTree $WorkTree -BareRepository $BareRepository -Description 'GitCandy M0 public demo fixture'
}

function New-PrivateRepository {
    param(
        [Parameter(Mandatory = $true)]
        [string] $WorkTree,

        [Parameter(Mandatory = $true)]
        [string] $BareRepository
    )

    Initialize-WorkTree -Path $WorkTree

    New-Utf8File -Path (Join-Path $WorkTree 'README.md') -Lines @(
        '# Private Demo',
        '',
        'This repository is private in the M0 sample data.',
        'It contains no secrets; it only exercises private repository authorization.'
    )
    New-Utf8File -Path (Join-Path $WorkTree 'src/internal-service.txt') -Lines @(
        'internal migration fixture content'
    )
    Invoke-Git -Arguments @('-C', $WorkTree, 'add', '.')
    Invoke-FixtureCommit -WorkTree $WorkTree -Message 'Initial private sample content' -Date '2026-07-09T00:15:00Z'

    Invoke-Git -Arguments @('-C', $WorkTree, 'switch', '-c', 'release/v1')
    New-Utf8File -Path (Join-Path $WorkTree 'ops/deploy-note.md') -Lines @(
        '# Deploy Note',
        '',
        'This branch exists so fetch behavior can verify non-main refs.'
    )
    Invoke-Git -Arguments @('-C', $WorkTree, 'add', '.')
    Invoke-FixtureCommit -WorkTree $WorkTree -Message 'Add private release note' -Date '2026-07-09T00:20:00Z'

    Invoke-Git -Arguments @('-C', $WorkTree, 'switch', 'main')
    New-Utf8File -Path (Join-Path $WorkTree 'docs/access-policy.md') -Lines @(
        '# Access Policy',
        '',
        'Anonymous users cannot read or write this repository.',
        'Team members can read and write through the core team role.'
    )
    Invoke-Git -Arguments @('-C', $WorkTree, 'add', '.')
    Invoke-FixtureCommit -WorkTree $WorkTree -Message 'Document private access policy' -Date '2026-07-09T00:25:00Z'
    Invoke-Git -Arguments @('-C', $WorkTree, 'tag', 'internal-v0.1.0')

    Publish-BareRepository -WorkTree $WorkTree -BareRepository $BareRepository -Description 'GitCandy M0 private demo fixture'
}

function Publish-BareRepository {
    param(
        [Parameter(Mandatory = $true)]
        [string] $WorkTree,

        [Parameter(Mandatory = $true)]
        [string] $BareRepository,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    Invoke-Git -Arguments @('init', '--bare', '--initial-branch=main', $BareRepository)
    Invoke-Git -Arguments @('-C', $WorkTree, 'remote', 'add', 'origin', $BareRepository)
    Invoke-Git -Arguments @('-C', $WorkTree, 'push', '--mirror', 'origin')
    Invoke-Git -Arguments @('-C', $BareRepository, 'update-server-info')
    New-Utf8File -Path (Join-Path $BareRepository 'description') -Lines @($Description)
}

function Get-RepositoryManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryPath,

        [Parameter(Mandatory = $true)]
        [string] $RepoRoot
    )

    $branches = Read-GitOutput -Arguments @('-C', $RepositoryPath, 'for-each-ref', '--format=%(refname:short)', 'refs/heads')
    $tags = Read-GitOutput -Arguments @('-C', $RepositoryPath, 'for-each-ref', '--format=%(refname:short)', 'refs/tags')
    $head = Read-GitOutput -Arguments @('-C', $RepositoryPath, 'rev-parse', 'HEAD')

    return [ordered]@{
        path = ConvertTo-RelativePath -BasePath $RepoRoot -Path $RepositoryPath
        isBare = (Read-GitOutput -Arguments @('-C', $RepositoryPath, 'rev-parse', '--is-bare-repository')) -eq 'true'
        head = $head
        branches = @($branches -split "`n" | Where-Object { $_ })
        tags = @($tags -split "`n" | Where-Object { $_ })
    }
}

function Test-BareRepository {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryPath,

        [Parameter(Mandatory = $true)]
        [string] $VerificationRoot
    )

    $cloneName = [System.IO.Path]::GetFileNameWithoutExtension($RepositoryPath)
    $clonePath = Join-Path $VerificationRoot $cloneName
    Invoke-Git -Arguments @('clone', $RepositoryPath, $clonePath)
    Invoke-Git -Arguments @('-C', $clonePath, 'fetch', '--all', '--tags')
    Invoke-Git -Arguments @('-C', $clonePath, 'status', '--short')
}

$repoRoot = Read-GitOutput -Arguments @('rev-parse', '--show-toplevel')
$repoRoot = Get-NormalizedFullPath -Path $repoRoot
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$m0ArtifactsRoot = Join-Path (Join-Path $artifactsRoot 'migration') 'm0-001'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $m0ArtifactsRoot
}

$OutputRoot = Get-NormalizedFullPath -Path $OutputRoot
Assert-PathInside -Path $OutputRoot -Root $m0ArtifactsRoot -Description 'OutputRoot'
New-CleanDirectory -Path $OutputRoot -AllowedRoot $m0ArtifactsRoot

$repositoriesRoot = Join-Path $OutputRoot 'repositories'
$worktreesRoot = Join-Path $OutputRoot 'worktrees'
$verificationRoot = Join-Path $OutputRoot 'verification'

New-Item -ItemType Directory -Path $repositoriesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $worktreesRoot -Force | Out-Null

$publicWorkTree = Join-Path $worktreesRoot 'public-demo'
$privateWorkTree = Join-Path $worktreesRoot 'private-demo'
$publicRepository = Join-Path $repositoriesRoot 'public-demo.git'
$privateRepository = Join-Path $repositoriesRoot 'private-demo.git'

New-PublicRepository -WorkTree $publicWorkTree -BareRepository $publicRepository
New-PrivateRepository -WorkTree $privateWorkTree -BareRepository $privateRepository

$seedData = [ordered]@{
    schemaVersion = 1
    fixture = 'M0-001'
    fixtureDateUtc = '2026-07-09T00:00:00Z'
    passwordPolicy = [ordered]@{
        storedInFixture = $false
        note = 'Passwords are intentionally not stored. Future Identity seed code must read local secrets or environment variables.'
        environmentVariables = @(
            'GITCANDY_M0_ADMIN_PASSWORD',
            'GITCANDY_M0_ALICE_PASSWORD',
            'GITCANDY_M0_BOB_PASSWORD',
            'GITCANDY_M0_CAROL_PASSWORD'
        )
    }
    users = @(
        [ordered]@{
            id = 'user-admin'
            userName = 'admin'
            normalizedUserName = 'ADMIN'
            email = 'admin@gitcandy.local'
            normalizedEmail = 'ADMIN@GITCANDY.LOCAL'
            displayName = 'System Administrator'
            description = 'M0 administrator account for migration smoke tests.'
            isSystemAdministrator = $true
        },
        [ordered]@{
            id = 'user-alice'
            userName = 'alice'
            normalizedUserName = 'ALICE'
            email = 'alice@gitcandy.local'
            normalizedEmail = 'ALICE@GITCANDY.LOCAL'
            displayName = 'Alice Owner'
            description = 'Repository owner and core team administrator.'
            isSystemAdministrator = $false
        },
        [ordered]@{
            id = 'user-bob'
            userName = 'bob'
            normalizedUserName = 'BOB'
            email = 'bob@gitcandy.local'
            normalizedEmail = 'BOB@GITCANDY.LOCAL'
            displayName = 'Bob Contributor'
            description = 'Ordinary user with team-based private repository access.'
            isSystemAdministrator = $false
        },
        [ordered]@{
            id = 'user-carol'
            userName = 'carol'
            normalizedUserName = 'CAROL'
            email = 'carol@gitcandy.local'
            normalizedEmail = 'CAROL@GITCANDY.LOCAL'
            displayName = 'Carol Guest'
            description = 'Ordinary user without private repository access.'
            isSystemAdministrator = $false
        }
    )
    teams = @(
        [ordered]@{
            id = 'team-core'
            name = 'core'
            normalizedName = 'CORE'
            description = 'Core maintainers used by M0 permission smoke tests.'
        }
    )
    userTeamRoles = @(
        [ordered]@{
            userId = 'user-alice'
            teamId = 'team-core'
            isAdministrator = $true
        },
        [ordered]@{
            userId = 'user-bob'
            teamId = 'team-core'
            isAdministrator = $false
        }
    )
    repositories = @(
        [ordered]@{
            id = 'repo-public-demo'
            name = 'public-demo'
            description = 'Public repository fixture for anonymous clone and fetch behavior.'
            isPrivate = $false
            allowAnonymousRead = $true
            allowAnonymousWrite = $false
            barePath = ConvertTo-RelativePath -BasePath $repoRoot -Path $publicRepository
            compatibleRoutes = @(
                'git/public-demo.git/{*verb}',
                'git/public-demo/{*verb}'
            )
        },
        [ordered]@{
            id = 'repo-private-demo'
            name = 'private-demo'
            description = 'Private repository fixture for owner, team, admin, and denied access behavior.'
            isPrivate = $true
            allowAnonymousRead = $false
            allowAnonymousWrite = $false
            barePath = ConvertTo-RelativePath -BasePath $repoRoot -Path $privateRepository
            compatibleRoutes = @(
                'git/private-demo.git/{*verb}',
                'git/private-demo/{*verb}'
            )
        }
    )
    userRepositoryRoles = @(
        [ordered]@{
            userId = 'user-alice'
            repositoryId = 'repo-public-demo'
            allowRead = $true
            allowWrite = $true
            isOwner = $true
        },
        [ordered]@{
            userId = 'user-alice'
            repositoryId = 'repo-private-demo'
            allowRead = $true
            allowWrite = $true
            isOwner = $true
        }
    )
    teamRepositoryRoles = @(
        [ordered]@{
            teamId = 'team-core'
            repositoryId = 'repo-private-demo'
            allowRead = $true
            allowWrite = $true
        }
    )
    expectedPermissionCases = @(
        [ordered]@{
            actor = 'anonymous'
            repositoryId = 'repo-public-demo'
            canRead = $true
            canWrite = $false
        },
        [ordered]@{
            actor = 'anonymous'
            repositoryId = 'repo-private-demo'
            canRead = $false
            canWrite = $false
        },
        [ordered]@{
            actor = 'user-admin'
            repositoryId = 'repo-private-demo'
            canRead = $true
            canWrite = $true
        },
        [ordered]@{
            actor = 'user-alice'
            repositoryId = 'repo-private-demo'
            canRead = $true
            canWrite = $true
        },
        [ordered]@{
            actor = 'user-bob'
            repositoryId = 'repo-private-demo'
            canRead = $true
            canWrite = $true
        },
        [ordered]@{
            actor = 'user-carol'
            repositoryId = 'repo-private-demo'
            canRead = $false
            canWrite = $false
        }
    )
}

$manifest = [ordered]@{
    schemaVersion = 1
    fixture = 'M0-001'
    outputRoot = ConvertTo-RelativePath -BasePath $repoRoot -Path $OutputRoot
    seedData = ConvertTo-RelativePath -BasePath $repoRoot -Path (Join-Path $OutputRoot 'seed-data.json')
    repositories = [ordered]@{
        publicDemo = Get-RepositoryManifest -RepositoryPath $publicRepository -RepoRoot $repoRoot
        privateDemo = Get-RepositoryManifest -RepositoryPath $privateRepository -RepoRoot $repoRoot
    }
}

$seedJson = $seedData | ConvertTo-Json -Depth 16
$manifestJson = $manifest | ConvertTo-Json -Depth 16
$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $OutputRoot 'seed-data.json'), $seedJson + "`n", $encoding)
[System.IO.File]::WriteAllText((Join-Path $OutputRoot 'manifest.json'), $manifestJson + "`n", $encoding)

New-Utf8File -Path (Join-Path $OutputRoot 'README.md') -Lines @(
    '# GitCandy M0 #001 Fixtures',
    '',
    'This directory is generated and ignored by git.',
    '',
    'Tracked instructions live in docs/migration/m0-001-test-data-and-sample-repositories.md.',
    '',
    'Generated files:',
    '',
    '- seed-data.json: provider-neutral sample data specification.',
    '- manifest.json: generated repository refs and paths.',
    '- repositories/public-demo.git: public bare repository fixture.',
    '- repositories/private-demo.git: private bare repository fixture.'
)

if (-not $KeepWorktrees) {
    Assert-PathInside -Path $worktreesRoot -Root $OutputRoot -Description 'Worktrees path'
    Remove-Item -LiteralPath $worktreesRoot -Recurse -Force
}

if ($Verify) {
    New-Item -ItemType Directory -Path $verificationRoot -Force | Out-Null
    Test-BareRepository -RepositoryPath $publicRepository -VerificationRoot $verificationRoot
    Test-BareRepository -RepositoryPath $privateRepository -VerificationRoot $verificationRoot
}

Write-Host "Generated M0 #001 fixtures at $OutputRoot"
Write-Host "Seed data: $(Join-Path $OutputRoot 'seed-data.json')"
Write-Host "Manifest:  $(Join-Path $OutputRoot 'manifest.json')"
