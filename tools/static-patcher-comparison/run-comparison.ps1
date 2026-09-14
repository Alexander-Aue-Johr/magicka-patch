param(
    [string]$MagickaDirectory = "G:\SteamLibrary\steamapps\common\Magicka"
)

$ErrorActionPreference = "Stop"
$input = Join-Path $MagickaDirectory "Magicka.exe"
$ilOutput = Join-Path $MagickaDirectory "Magicka.experimental-il.exe"
$structuralOutput = Join-Path $MagickaDirectory `
    "Magicka.experimental-structural.exe"

& dotnet run --project (Join-Path $PSScriptRoot `
    "IlStaticPatcher\IlStaticPatcher.csproj") --configuration Release -- `
    $input $ilOutput
if ($LASTEXITCODE -ne 0) { throw "IL patcher failed." }

& dotnet run --project (Join-Path $PSScriptRoot `
    "StructuralStaticPatcher\StructuralStaticPatcher.csproj") `
    --configuration Release -- $input $structuralOutput
if ($LASTEXITCODE -ne 0) { throw "Structural patcher failed." }

Get-FileHash -Algorithm SHA256 $input, $ilOutput, $structuralOutput
