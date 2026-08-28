[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $VersionFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($VersionFile)) {
    $VersionFile = Join-Path (Split-Path -Parent $PSScriptRoot) "VERSION"
}

$resolvedVersionFile = [System.IO.Path]::GetFullPath($VersionFile)
if (-not [System.IO.File]::Exists($resolvedVersionFile)) {
    throw "VERSION file does not exist: $resolvedVersionFile"
}

$currentVersion = [System.IO.File]::ReadAllText($resolvedVersionFile).Trim()
$match = [System.Text.RegularExpressions.Regex]::Match(
    $currentVersion,
    '^(?<major>[0-9]+)[.](?<minor>[0-9]+)[.](?<revision>[0-9]{2})$')
if (-not $match.Success) {
    throw "VERSION must use x.x.xx format: $currentVersion"
}

$major = [int] $match.Groups['major'].Value
$minor = [int] $match.Groups['minor'].Value
$revision = [int] $match.Groups['revision'].Value
if ($revision -lt 10 -or $revision -gt 99) {
    throw "VERSION final component must be between 10 and 99: $currentVersion"
}

if ($revision -eq 99) {
    $minor++
    $revision = 10
}
else {
    $revision++
}

$nextVersion = "{0}.{1}.{2:D2}" -f $major, $minor, $revision
if ($PSCmdlet.ShouldProcess(
        $resolvedVersionFile,
        "Update VERSION from $currentVersion to $nextVersion")) {
    [System.IO.File]::WriteAllText(
        $resolvedVersionFile,
        $nextVersion + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

$nextVersion
