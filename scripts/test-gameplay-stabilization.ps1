param(
    [Parameter(Mandatory=$true)][string]$GodotPath,
    [Parameter(Mandatory=$true)][string]$ManagedRuntimeDirectory,
    [Parameter(Mandatory=$true)][string]$GameAssemblyPath,
    [string]$BaseGamePckPath = '',
    [int]$TimeoutSeconds = 120,
    [switch]$SkipBuild,
    [switch]$RuntimeInvestigation
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'tools/LauncherUiPreview'
$report = Join-Path $root 'artifacts/gameplay-stabilization'
if ($RuntimeInvestigation) { $report = Join-Path $root 'artifacts/runtime-investigation' }
New-Item -ItemType Directory -Force -Path $report | Out-Null
$godot = (Resolve-Path -LiteralPath $GodotPath).Path
$runtime = (Resolve-Path -LiteralPath $ManagedRuntimeDirectory).Path
$assembly = (Resolve-Path -LiteralPath $GameAssemblyPath).Path
if (-not $SkipBuild) {
    dotnet build (Join-Path $project 'LauncherUiPreview.csproj') -c Debug
    if ($LASTEXITCODE) { throw 'Gameplay probe build failed.' }
}
$output = Join-Path $project '.godot/mono/temp/bin/Debug'
$staged = Join-Path $output 'sts2.dll'
$backup = Join-Path $report 'compile-reference.dll'
Copy-Item -LiteralPath $staged -Destination $backup -Force
try {
    Copy-Item -LiteralPath $assembly -Destination $staged -Force
    $arguments = @('--headless', '--log-file', (Join-Path $report 'godot.log'), '--path', $project, '--', '--gameplay-stabilization-test=true',
        "--managed-runtime-directory=$runtime", "--report-directory=$report")
    $completionMarker = 'GAMEPLAY_STABILIZATION_PASS'
    if ($RuntimeInvestigation) {
        $pck = Join-Path $report 'runtime-fixtures.pck'
        python (Join-Path $root 'scripts/make-bootstrap-pck.py') $pck
        if ($LASTEXITCODE) { throw 'Runtime fixture PCK generation failed.' }
        $arguments = @('--headless', '--log-file', (Join-Path $report 'godot.log'), '--path', $project, '--', '--runtime-investigation-test=true',
            "--managed-runtime-directory=$runtime", "--report-directory=$report", "--runtime-fixture-pck=$pck")
        $completionMarker = 'RUNTIME_INVESTIGATION_COMPLETE'
    }
    if ($BaseGamePckPath) { $arguments += "--base-game-pck-path=$((Resolve-Path -LiteralPath $BaseGamePckPath).Path)" }
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $godot
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['STS2_GAMEPLAY_RUNTIME_DIRECTORY'] = $output
    $start.Arguments = ($arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $process.WaitForExit()
        throw "Gameplay probe exceeded $TimeoutSeconds seconds."
    }
    $text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult()
    $text | Set-Content -LiteralPath (Join-Path $report 'engine.log') -Encoding utf8
    $text | Select-String 'RUNTIME_INVESTIGATION|GAMEPLAY_STABILIZATION|GameplayFailure|GameplayProbe'
    if ($process.ExitCode -ne 0 -or $text -notmatch $completionMarker) {
        throw "Gameplay probe failed. Inspect $report"
    }
}
finally {
    Copy-Item -LiteralPath $backup -Destination $staged -Force
}
if ($RuntimeInvestigation) { Write-Host "Desktop investigation completed; iOS-only contracts remain incomplete. Evidence: $report" }
else { Write-Host "Desktop gameplay contracts passed. Evidence: $report" }
