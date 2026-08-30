---
applyTo: "**"
---

# .NET SDK setup

Before running any `dotnet` command other than these preflight commands, inspect
the available and selected SDK:

```text
dotnet --list-sdks
dotnet --version
```

The [repository development SDK](../../README.md#repository-development-sdk) is
the current .NET 11 preview. As of Preview 7, that resolves to
`11.0.100-preview.7.26381.103`. Use the installed SDK when that exact version
appears in `dotnet --list-sdks` and `dotnet --version` selects it. If `dotnet`
is unavailable, treat the SDK as absent. Do not replace or modify a centrally
installed `dotnet`.

If the required SDK is absent or is not selected, install it with `dotnetup`
under the current worktree's ignored `artifacts/` directory. On Windows
PowerShell:

```powershell
$ErrorActionPreference = "Stop"
$requiredSdk = "11.0.100-preview.7.26381.103"
$dotnetupRoot = Join-Path $PWD "artifacts\dotnetup"
$dotnetRoot = Join-Path $PWD "artifacts\dotnet\$requiredSdk"
$dotnetupData = Join-Path $PWD "artifacts\dotnetup-data"
$bootstrap = Join-Path $PWD "artifacts\get-dotnetup.ps1"

New-Item -ItemType Directory -Force (Split-Path $bootstrap) | Out-Null
Invoke-WebRequest `
    https://aka.ms/dotnet/dotnetup/daily/get-dotnetup.ps1 `
    -OutFile $bootstrap
& $bootstrap -InstallDir $dotnetupRoot

$env:DOTNET_DOTNETUP_DATA_DIR = $dotnetupData
& "$dotnetupRoot\dotnetup.exe" sdk install $requiredSdk `
    --install-path $dotnetRoot `
    --untracked `
    --set-default-install false `
    --interactive false
if ($LASTEXITCODE -ne 0) {
    throw "dotnetup failed with exit code $LASTEXITCODE"
}

$env:DOTNET_ROOT = $dotnetRoot
$selectedSdk = (& "$dotnetRoot\dotnet.exe" --version | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $selectedSdk -ne $requiredSdk) {
    throw "Expected .NET SDK $requiredSdk; selected $selectedSdk"
}
$selectedSdk
```

On macOS or Linux:

```bash
set -euo pipefail

required_sdk="11.0.100-preview.7.26381.103"
dotnetup_root="$PWD/artifacts/dotnetup"
dotnet_root="$PWD/artifacts/dotnet/$required_sdk"
dotnetup_data="$PWD/artifacts/dotnetup-data"
bootstrap="$PWD/artifacts/get-dotnetup.sh"

mkdir -p "$PWD/artifacts"
curl -fsSL --retry 3 \
  https://aka.ms/dotnet/dotnetup/daily/get-dotnetup.sh \
  -o "$bootstrap"
bash "$bootstrap" --install-dir "$dotnetup_root"

DOTNET_DOTNETUP_DATA_DIR="$dotnetup_data" \
  "$dotnetup_root/dotnetup" sdk install "$required_sdk" \
    --install-path "$dotnet_root" \
    --untracked \
    --set-default-install false \
    --interactive false

export DOTNET_ROOT="$dotnet_root"
selected_sdk=$("$dotnet_root/dotnet" --version | tail -n 1)
if [[ "$selected_sdk" != "$required_sdk" ]]; then
  echo "Expected .NET SDK $required_sdk; selected $selected_sdk" >&2
  exit 1
fi
printf '%s\n' "$selected_sdk"
```

After a local install, keep process-scoped `DOTNET_ROOT` set to the local root
in every shell and invoke the worktree-local executable explicitly for all
repository commands. For example, in PowerShell:

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" build dotnet-inspect.slnx -c Release
```

Or in Bash:

```bash
"$DOTNET_ROOT/dotnet" build dotnet-inspect.slnx -c Release
```

Do not add it to the user or system `PATH`.
