param(
    [Parameter(Mandatory = $true)][string]$PreviousAssembly,
    [Parameter(Mandatory = $true)][string]$CurrentAssembly,
    [Parameter(Mandatory = $true)][string]$DependencyDirectory,
    [string]$Mono = ""
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previous = (Resolve-Path $PreviousAssembly).Path
$current = (Resolve-Path $CurrentAssembly).Path
$dependencies = (Resolve-Path $DependencyDirectory).Path
$work = Join-Path $repoRoot 'tmp\changed-method-jit'
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Force $payload | Out-Null
Get-ChildItem -LiteralPath $payload -File | Remove-Item -Force
Copy-Item -Path (Join-Path $dependencies '*.dll') -Destination $payload
Copy-Item -LiteralPath $current -Destination (Join-Path $payload 'Magicka.exe')
foreach ($name in @('PolygonHead.dll', 'Magicka.GcDiagnostics.dll')) {
    $patchedDependency = Join-Path $repoRoot $name
    if (Test-Path -LiteralPath $patchedDependency -PathType Leaf) {
        Copy-Item -LiteralPath $patchedDependency -Destination (Join-Path $payload $name)
    }
}

$manifest = Join-Path $work 'changed-methods.tsv'
& dotnet run --project (Join-Path $repoRoot 'tools\changed-method-jit-manifest\ChangedMethodJitManifest.csproj') --configuration Release -- $previous $current $manifest
if ($LASTEXITCODE -ne 0) { throw 'Changed-method manifest generation failed.' }

$probeSource = Join-Path $repoRoot 'tools\changed-method-jit-probe\Program.cs'
$probe = Join-Path $payload 'ChangedMethodJitProbe.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v2.0.50727\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw "CLR 2 compiler not found: $csc"
}
& $csc /nologo /platform:x86 "/out:$probe" $probeSource
if ($LASTEXITCODE -ne 0) { throw 'CLR 2 probe compilation failed.' }

& $probe (Join-Path $payload 'Magicka.exe') $manifest
if ($LASTEXITCODE -ne 0) { throw 'Microsoft CLR 2 changed-method JIT failed.' }

if (-not [string]::IsNullOrWhiteSpace($Mono)) {
    & $Mono $probe (Join-Path $payload 'Magicka.exe') $manifest
    if ($LASTEXITCODE -ne 0) { throw 'Mono changed-method JIT failed.' }
}
