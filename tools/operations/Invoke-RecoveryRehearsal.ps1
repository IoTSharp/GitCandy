param(
  [Parameter(Mandatory = $true)][string]$DataRoot,
  [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) "gitcandy-recovery-$([guid]::NewGuid().ToString('N'))")
)
$ErrorActionPreference = 'Stop'
$source = [System.IO.Path]::GetFullPath($DataRoot)
$working = [System.IO.Path]::GetFullPath($WorkingRoot)
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Data root does not exist: $source" }
if ($working.StartsWith($source + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Working root must not be inside the live data root.' }
$backup = Join-Path $working 'backup'
$restore = Join-Path $working 'restore'
New-Item -ItemType Directory -Path $backup,$restore -Force | Out-Null
$items = @('GitCandy.db','repositories','lfs','data-protection-keys')
foreach ($item in $items) {
  $path = Join-Path $source $item
  if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $backup -Recurse -Force }
}
$manifest = Get-ChildItem -LiteralPath $backup -File -Recurse | ForEach-Object {
  [pscustomobject]@{ Path = [System.IO.Path]::GetRelativePath($backup, $_.FullName); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
if (-not ($manifest.Path -contains 'GitCandy.db')) { throw 'GitCandy.db was not included in the recovery set.' }
Copy-Item -Path (Join-Path $backup '*') -Destination $restore -Recurse -Force
foreach ($entry in $manifest) {
  $restored = Join-Path $restore $entry.Path
  if (-not (Test-Path -LiteralPath $restored -PathType Leaf)) { throw "Missing restored file: $($entry.Path)" }
  if ((Get-FileHash -LiteralPath $restored -Algorithm SHA256).Hash -ne $entry.Sha256) { throw "Hash mismatch: $($entry.Path)" }
}
[pscustomobject]@{ Files = $manifest.Count; Bytes = ($manifest | Measure-Object Length -Sum).Sum; WorkingRoot = $working } | ConvertTo-Json
