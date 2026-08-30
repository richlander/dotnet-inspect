---
applyTo: "**"
---

# .NET SDK setup

Before running a .NET build or test command, inspect the available and selected
SDK:

```text
dotnet --list-sdks
dotnet --version
```

The current repository SDK is .NET 11 Preview 7,
`11.0.100-preview.7.26381.103`. Use the installed SDK when that exact version
appears in `dotnet --list-sdks` and `dotnet --version` selects it. Do not replace
or modify a centrally installed `dotnet`.

If the required SDK is absent or is not selected, install it with `dotnetup`
under the current worktree's ignored `artifacts/` directory. On Windows
PowerShell:

```powershell
$dotnetupRoot = Join-Path $PWD "artifacts\dotnetup"
$dotnetRoot = Join-Path $PWD "artifacts\dotnet"
$dotnetupData = Join-Path $PWD "artifacts\dotnetup-data"
$bootstrap = Join-Path $PWD "artifacts\get-dotnetup.ps1"

New-Item -ItemType Directory -Force (Split-Path $bootstrap) | Out-Null
Invoke-WebRequest `
    https://aka.ms/dotnet/dotnetup/daily/get-dotnetup.ps1 `
    -OutFile $bootstrap
& $bootstrap -InstallDir $dotnetupRoot

$env:DOTNET_DOTNETUP_DATA_DIR = $dotnetupData
& "$dotnetupRoot\dotnetup.exe" sdk install 11.0.100-preview.7.26381.103 `
    --install-path $dotnetRoot `
    --untracked `
    --set-default-install false `
    --interactive false
& "$dotnetRoot\dotnet.exe" --version
```

On macOS or Linux:

```bash
dotnetup_root="$PWD/artifacts/dotnetup"
dotnet_root="$PWD/artifacts/dotnet"
dotnetup_data="$PWD/artifacts/dotnetup-data"
bootstrap="$PWD/artifacts/get-dotnetup.sh"

mkdir -p "$PWD/artifacts"
curl -fsSL --retry 3 \
  https://aka.ms/dotnet/dotnetup/daily/get-dotnetup.sh \
  -o "$bootstrap"
bash "$bootstrap" --install-dir "$dotnetup_root"

DOTNET_DOTNETUP_DATA_DIR="$dotnetup_data" \
  "$dotnetup_root/dotnetup" sdk install 11.0.100-preview.7.26381.103 \
    --install-path "$dotnet_root" \
    --untracked \
    --set-default-install false \
    --interactive false
"$dotnet_root/dotnet" --version
```

After a local install, invoke the worktree-local executable explicitly for all
repository commands. For example:

```text
artifacts/dotnet/dotnet build dotnet-inspect.slnx -c Release
```

Do not add it to the user or system `PATH`.
