function Resolve-GodotSourceDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GodotDir,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    if ([System.IO.Path]::IsPathRooted($GodotDir)) {
        return [System.IO.Path]::GetFullPath($GodotDir)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $Root $GodotDir))
}

function Test-GodotGitApply {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & git @Arguments *> $null
        return $LASTEXITCODE -eq 0
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Apply-GodotPatches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GodotDir,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $patchDir = Join-Path $Root "patches\godot"
    if (-not (Test-Path -LiteralPath $patchDir)) {
        return
    }

    & python (Join-Path $PSScriptRoot "apply-godot-patches.py") $GodotDir $patchDir
    if ($LASTEXITCODE -ne 0) {
        throw "Godot patch stack validation failed. See the diagnostic above."
    }
}
