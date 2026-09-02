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
if (-not [string]::IsNullOrWhiteSpace($Mono)) {
    $Mono = (Resolve-Path -LiteralPath $Mono).Path
}
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

function Copy-GacDependency([string]$name, [Version]$version) {
    $gacRoots = @(
        (Join-Path $env:WINDIR 'assembly\GAC_32'),
        (Join-Path $env:WINDIR 'assembly\GAC')
    )
    foreach ($gacRoot in $gacRoots) {
        $assemblyRoot = Join-Path $gacRoot $name
        if (-not (Test-Path -LiteralPath $assemblyRoot -PathType Container)) {
            continue
        }
        foreach ($candidate in Get-ChildItem -LiteralPath $assemblyRoot -Recurse -Filter "$name.dll" -File) {
            try {
                $candidateName = [Reflection.AssemblyName]::GetAssemblyName($candidate.FullName)
                if ($candidateName.Version -eq $version) {
                    Copy-Item -LiteralPath $candidate.FullName -Destination $payload -Force
                    return
                }
            }
            catch [BadImageFormatException] {
            }
        }
    }
    throw "Required framework dependency was not found in the GAC: $name $version"
}

if (-not [string]::IsNullOrWhiteSpace($Mono)) {
    Copy-GacDependency 'Microsoft.Xna.Framework' ([Version]'3.1.0.0')
    Copy-GacDependency 'Microsoft.Xna.Framework.Game' ([Version]'3.1.0.0')
    Copy-GacDependency 'Microsoft.DirectX.DirectInput' ([Version]'1.0.2902.0')
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
