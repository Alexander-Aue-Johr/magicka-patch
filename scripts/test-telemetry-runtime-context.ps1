param(
    [string]$Mono = ""
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v2.0.50727\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "CLR 2 compiler not found: $compiler"
}
if (-not [string]::IsNullOrWhiteSpace($Mono)) {
    $Mono = (Resolve-Path -LiteralPath $Mono).Path
}

$work = Join-Path $repoRoot 'tmp\telemetry-runtime-context-test'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$testExecutable = Join-Path $work 'TelemetryRuntimeContextTests.exe'
$contextSource = Join-Path $repoRoot 'docs\injected-source\Magicka.CommunityPatch\TelemetryRuntimeContext.cs'
$harnessSource = Join-Path $repoRoot 'tools\telemetry-runtime-context-harness\Program.cs'

& $compiler /nologo /target:exe /optimize+ "/out:$testExecutable" $contextSource $harnessSource
if ($LASTEXITCODE -ne 0) {
    throw 'CLR 2 telemetry runtime-context test compilation failed.'
}

& $testExecutable
if ($LASTEXITCODE -ne 0) {
    throw 'Microsoft CLR 2 telemetry runtime-context tests failed.'
}

if (-not [string]::IsNullOrWhiteSpace($Mono)) {
    & $Mono $testExecutable
    if ($LASTEXITCODE -ne 0) {
        throw 'Mono telemetry runtime-context tests failed.'
    }
}
