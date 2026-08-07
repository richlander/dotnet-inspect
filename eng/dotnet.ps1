$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

if ($args.Count -eq 0) {
    [Console]::Error.WriteLine("Usage: .\eng\dotnet.ps1 <dotnet arguments>")
    exit 2
}

$installDir = if ($env:DOTNETUP_INSTALL_DIR) {
    $env:DOTNETUP_INSTALL_DIR
} else {
    Join-Path $HOME ".local\bin"
}
$dotnetup = Join-Path $installDir "dotnetup.exe"

if (-not (Test-Path -LiteralPath $dotnetup -PathType Leaf)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    $installer = Join-Path ([System.IO.Path]::GetTempPath()) "get-dotnetup-$([Guid]::NewGuid()).ps1"
    $bootstrapDir = Join-Path $installDir ".dotnetup-bootstrap-$([Guid]::NewGuid())"
    $bootstrapDotnetup = Join-Path $bootstrapDir "dotnetup.exe"
    try {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest https://aka.ms/dotnetup/get-dotnetup.ps1 `
                    -OutFile $installer -UseBasicParsing
                break
            } catch {
                if ($attempt -eq 3) {
                    throw
                }
                Start-Sleep -Seconds $attempt
            }
        }

        & $installer -InstallDir $bootstrapDir
        if (-not (Test-Path -LiteralPath $bootstrapDotnetup -PathType Leaf)) {
            throw "dotnetup installation did not produce $bootstrapDotnetup."
        }

        try {
            Move-Item -LiteralPath $bootstrapDotnetup -Destination $dotnetup
        } catch {
            if (-not (Test-Path -LiteralPath $dotnetup -PathType Leaf)) {
                throw
            }
        }
    } finally {
        Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $bootstrapDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

& $dotnetup sdk install 11 --interactive false --no-progress
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine(
        "dotnetup could not prepare the .NET 11 SDK (exit $LASTEXITCODE).")
    exit $LASTEXITCODE
}

& $dotnetup dotnet -- @args
exit $LASTEXITCODE
