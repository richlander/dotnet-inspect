#Requires -Version 7.3

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$PSNativeCommandArgumentPassing = "Standard"

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)]
        [string] $FileName,
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Close()
        $process.WaitForExit()

        $output = @()
        foreach ($text in @(
            $standardOutput.GetAwaiter().GetResult(),
            $standardError.GetAwaiter().GetResult()
        )) {
            if ($text.Length -gt 0) {
                $output += @($text -split "\r?\n" | Where-Object { $_.Length -gt 0 })
            }
        }

        return [PSCustomObject]@{
            ExitCode = $process.ExitCode
            Output = [string[]] $output
        }
    } finally {
        $process.Dispose()
    }
}

function Invoke-InheritedProcess {
    param(
        [Parameter(Mandatory)]
        [string] $FileName,
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $process.WaitForExit()
        return $process.ExitCode
    } finally {
        $process.Dispose()
    }
}

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

        $bootstrapResult = Invoke-CapturedProcess `
            -FileName (Join-Path $PSHOME "pwsh.exe") `
            -Arguments @(
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                $installer,
                "-InstallDir",
                $bootstrapDir
            )
        $bootstrapOutput = $bootstrapResult.Output
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

$installResult = Invoke-CapturedProcess `
    -FileName $dotnetup `
    -Arguments @("sdk", "install", "11", "--interactive", "false", "--no-progress")
$installOutput = $installResult.Output
$installExitCode = $installResult.ExitCode
$refreshFailed = $installExitCode -ne 0
if ($refreshFailed) {
    $sdkResult = Invoke-CapturedProcess `
        -FileName $dotnetup `
        -Arguments @("dotnet", "--", "--list-sdks")
    $sdkOutput = $sdkResult.Output
    $sdkExitCode = $sdkResult.ExitCode
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

}

$selectedResult = Invoke-CapturedProcess `
    -FileName $dotnetup `
    -Arguments @("dotnet", "--", "--version")
$selectedOutput = $selectedResult.Output
$selectedExitCode = $selectedResult.ExitCode
$selectedDotnet11 = $selectedExitCode -eq 0 -and @(
    $selectedOutput | Where-Object { $_ -match '^11\.' }
).Count -eq 1
if (-not $selectedDotnet11) {
    $diagnostics = if ($refreshFailed) { $installOutput } else { $selectedOutput }
    foreach ($line in $diagnostics) {
        [Console]::Error.WriteLine($line)
    }
    [Console]::Error.WriteLine(
        "dotnetup command isolation did not select the required .NET 11 SDK.")
    $failureExitCode = if ($refreshFailed) {
        $installExitCode
    } elseif ($selectedExitCode -ne 0) {
        $selectedExitCode
    } else {
        1
    }
    exit $failureExitCode
}

if ($refreshFailed) {
    [Console]::Error.WriteLine(
        "warning: dotnetup could not update .NET 11; using the installed .NET 11 SDK.")
}

$hasPowerShellPipelineInput =
    $MyInvocation.ExpectingInput -and $MyInvocation.InvocationName -eq "&"
if ($hasPowerShellPipelineInput) {
    # A static reference to the automatic input variable makes pwsh -File drain
    # redirected stdin before this script runs. Resolve it only for a real
    # in-process PowerShell object pipeline.
    $pipelineInput = Get-Variable -Name input -ValueOnly
    $pipelineInput | & $dotnetup dotnet -- @args
    exit $LASTEXITCODE
} else {
    $commandExitCode = Invoke-InheritedProcess `
        -FileName $dotnetup `
        -Arguments (@("dotnet", "--") + $args)
    exit $commandExitCode
}
