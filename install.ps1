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

$installDirectory = if (
    [string]::IsNullOrWhiteSpace($env:DOTNET_INSTALL_DIR)
) {
    $null
} else {
    $env:DOTNET_INSTALL_DIR
}
$installerCommand = if ($installDirectory) {
    $null
} else {
    Get-Command "dotnet-install" -ErrorAction SilentlyContinue
}
$bootstrapDirectory = $null

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

        Write-Host "dotnet-inspect: installing temporary bootstrap tool..."
        dotnet tool install --tool-path $bootstrapDirectory dotnet-install
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $nativeInstallers = @(
            Get-ChildItem `
                -LiteralPath $bootstrapDirectory `
                -Filter "dotnet-install.exe" `
                -File `
                -Recurse
        )
        if ($nativeInstallers.Count -ne 1) {
            Write-Error `
                "dotnet-inspect: error: temporary dotnet-install executable not found"
            exit 1
        }

        # Keep the invoked path shallow. The tool store path can exceed the
        # Windows process-launch limit when TEMP is long.
        $nativeDirectory = Join-Path $bootstrapDirectory "native"
        New-Item -ItemType Directory -Path $nativeDirectory | Out-Null
        Get-ChildItem -LiteralPath $nativeInstallers[0].DirectoryName |
            Copy-Item -Destination $nativeDirectory -Recurse
        $installerCommand = Join-Path $nativeDirectory "dotnet-install.exe"
    }

    $installArguments = @("--package", "dotnet-inspect")
    if ($installDirectory) {
        $installArguments += @("--output", $installDirectory)
    }

    Write-Host "dotnet-inspect: installing dotnet-inspect..."
    & $installerCommand @installArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "dotnet-inspect: done"
} finally {
    if ($bootstrapDirectory -and (Test-Path -LiteralPath $bootstrapDirectory)) {
        Remove-Item -LiteralPath $bootstrapDirectory -Recurse -Force
    }
}
