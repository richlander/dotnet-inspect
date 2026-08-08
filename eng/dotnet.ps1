#Requires -Version 7.3

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$PSNativeCommandArgumentPassing = "Standard"

if (-not $IsWindows) {
    [Console]::Error.WriteLine(
        "eng/dotnet.ps1 supports Windows only; use eng/dotnet.sh on macOS or Linux.")
    exit 2
}

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

        $bootstrapOutput = & $installer -InstallDir $bootstrapDir *>&1
        if (-not (Test-Path -LiteralPath $bootstrapDotnetup -PathType Leaf)) {
            foreach ($line in $bootstrapOutput) {
                [Console]::Error.WriteLine($line)
            }
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

$installOutput = & $dotnetup sdk install 11 --interactive false --no-progress 2>&1
$installExitCode = $LASTEXITCODE
if ($installExitCode -ne 0) {
    $sdkOutput = & $dotnetup dotnet -- --list-sdks 2>&1
    $sdkExitCode = $LASTEXITCODE
    $hasDotnet11 = $sdkExitCode -eq 0 -and @(
        $sdkOutput | Where-Object { $_ -match '^11\.' }
    ).Count -gt 0
    if (-not $hasDotnet11) {
        foreach ($line in $installOutput) {
            [Console]::Error.WriteLine($line)
        }
        [Console]::Error.WriteLine(
            "dotnetup could not prepare the .NET 11 SDK (exit $installExitCode).")
        exit $installExitCode
    }

    [Console]::Error.WriteLine(
        "warning: dotnetup could not update .NET 11; using the installed .NET 11 SDK.")
}

& $dotnetup dotnet -- @args
exit $LASTEXITCODE
