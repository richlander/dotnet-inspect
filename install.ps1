# Install script for dotnet-inspect (Windows).
# Usage: irm https://raw.githubusercontent.com/richlander/dotnet-inspect/main/install.ps1 | iex
#
# Installs dotnet-inspect using dotnet-install. If dotnet-install is
# not available, it is installed temporarily via `dotnet tool install`.
#
# Requires the .NET SDK.
#
# Environment variables:
#   DOTNET_INSTALL_DIR   Override the install directory

$ErrorActionPreference = "Stop"

$installerCommand = Get-Command "dotnet-install" -ErrorAction SilentlyContinue
$bootstrapDirectory = $null
$originalPath = $null
$pathChanged = $false
$originalToolBinExists = Test-Path Env:DOTNET_TOOL_BIN
$originalToolBin = $env:DOTNET_TOOL_BIN
$toolBinChanged = $false

try {
    if (-not $installerCommand) {
        if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
            Write-Error "dotnet-inspect: error: need 'dotnet' (command not found)"
            exit 1
        }

        $bootstrapDirectory = Join-Path `
            ([IO.Path]::GetTempPath()) `
            "dotnet-inspect-bootstrap-$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $bootstrapDirectory | Out-Null

        Write-Host "dotnet-inspect: dotnet-install not found; installing temporary bootstrap tool..."
        dotnet tool install --tool-path $bootstrapDirectory dotnet-install
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $originalPath = $env:PATH
        $env:PATH = if ([string]::IsNullOrEmpty($originalPath)) {
            $bootstrapDirectory
        } else {
            "$bootstrapDirectory$([IO.Path]::PathSeparator)$originalPath"
        }
        $pathChanged = $true
        $installerCommand = Get-Command "dotnet-install" -ErrorAction SilentlyContinue
        if (-not $installerCommand) {
            Write-Error "dotnet-inspect: error: temporary dotnet-install command not found"
            exit 1
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_INSTALL_DIR)) {
        $env:DOTNET_TOOL_BIN = $env:DOTNET_INSTALL_DIR
        $toolBinChanged = $true
    }

    Write-Host "dotnet-inspect: installing dotnet-inspect..."
    & $installerCommand --package dotnet-inspect
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "dotnet-inspect: done"
} finally {
    if ($pathChanged) {
        $env:PATH = $originalPath
    }
    if ($toolBinChanged) {
        if ($originalToolBinExists) {
            $env:DOTNET_TOOL_BIN = $originalToolBin
        } else {
            Remove-Item Env:DOTNET_TOOL_BIN -ErrorAction SilentlyContinue
        }
    }
    if ($bootstrapDirectory -and (Test-Path -LiteralPath $bootstrapDirectory)) {
        Remove-Item -LiteralPath $bootstrapDirectory -Recurse -Force
    }
}
