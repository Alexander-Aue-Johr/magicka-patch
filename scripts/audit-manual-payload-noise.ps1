param(
    [Parameter(Mandatory = $true)][string]$OriginalMagicka,
    [Parameter(Mandatory = $true)][string]$PatchedMagicka,
    [Parameter(Mandatory = $true)][string]$OriginalPolygonHead,
    [Parameter(Mandatory = $true)][string]$PatchedPolygonHead,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot `
    "tools\inventory-box-patcher-experiment\src\ManagedPayloadNoiseAudit\ManagedPayloadNoiseAudit.csproj"

& dotnet run --project $project --configuration Release -- `
    $OriginalMagicka `
    $PatchedMagicka `
    $OriginalPolygonHead `
    $PatchedPolygonHead `
    $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "Managed payload noise audit failed with exit code $LASTEXITCODE"
}
