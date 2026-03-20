# Install script for dotnet-inspect (Windows).
# Usage: irm https://raw.githubusercontent.com/richlander/dotnet-inspect/main/install.ps1 | iex
#
# Installs dotnet-inspect using dotnet-install. If dotnet-install is
# not available, it is installed first via `dotnet tool install -g`.
#
# Requires the .NET SDK.
#
# Environment variables:
#   DOTNET_INSTALL_DIR   Override the install directory

$ErrorActionPreference = "Stop"

# Ensure dotnet-install is available
if (-not (Get-Command "dotnet-install" -ErrorAction SilentlyContinue)) {
    if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        Write-Error "dotnet-inspect: error: need 'dotnet' (command not found)"
        exit 1
    }
    Write-Host "dotnet-inspect: dotnet-install not found; installing via dotnet tool..."
    dotnet tool install -g dotnet-install
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "dotnet-inspect: installing dotnet-inspect..."
dotnet-install --package dotnet-inspect
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "dotnet-inspect: done"
