param([string]$GameAssemblyPath = '', [switch]$SkipBuild)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root (
    "tools\LocalGameplaySaveSafetyProbe\LocalGameplaySaveSafetyProbe.csproj"
)
$upstreamAssembly = Join-Path $root (
    "upstream\godot-export\.godot\mono\publish\arm64\sts2.dll"
)
$required = @(
    $project,
    (Join-Path $root "tools\LocalGameplaySaveSafetyProbe\Program.cs"),
    $upstreamAssembly
)

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required local-gameplay save-safety input is missing: $path"
    }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "Could not locate dotnet. Install the .NET 9 SDK or add it to PATH."
}

if (-not $SkipBuild) {
    & $dotnet.Source build $project --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Local gameplay save-safety build failed.' }
}
if (-not $GameAssemblyPath) {
    foreach ($candidate in @('tmp/ios-deps/gameplay-runtime/sts2.dll', 'tmp/ref_extracted/assets/dotnet_bcl/sts2.dll')) {
        $path = Join-Path $root $candidate
        if (Test-Path -LiteralPath $path -PathType Leaf) { $GameAssemblyPath = $path; break }
    }
    if (-not $GameAssemblyPath) { $GameAssemblyPath = $upstreamAssembly }
}
$runtime = (Resolve-Path -LiteralPath $GameAssemblyPath).Path
$output = Join-Path $root 'tools/LocalGameplaySaveSafetyProbe/bin/Release/net9.0'
$staged = Join-Path $output 'sts2.dll'
$backup = Join-Path $output 'sts2.dll.save-safety-reference'
Copy-Item -LiteralPath $staged -Destination $backup -Force
try {
    # ISaveStore interface calls require an executable game assembly, rather
    # than the metadata-only compile reference staged by the iOS build.
    Copy-Item -LiteralPath $runtime -Destination $staged -Force
    & $dotnet.Source (Join-Path $output 'LocalGameplaySaveSafetyProbe.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Local gameplay save-safety validation failed.' }
}
finally {
    Copy-Item -LiteralPath $backup -Destination $staged -Force
    Remove-Item -LiteralPath $backup
}
