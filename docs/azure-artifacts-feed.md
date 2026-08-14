# Azure Artifacts release feed

Official `dotnet-inspect` releases are published to Azure Artifacts as well as
NuGet.org:

```text
https://pkgs.dev.azure.com/richlander/dotnet-inspect/_packaging/dotnet-inspect@Local/nuget/v3/index.json
```

The two destinations receive the same package files from the same release
workflow run. They therefore have the same source commit, package versions, and
package bytes. Azure publication completes before NuGet.org publication and
GitHub release creation.

## Install or update

Install the Azure Artifacts credential provider once:

```bash
dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool
```

Install a release immediately after it is published:

```bash
dotnet tool install -g dotnet-inspect \
  --version <version> \
  --add-source https://pkgs.dev.azure.com/richlander/dotnet-inspect/_packaging/dotnet-inspect@Local/nuget/v3/index.json \
  --interactive
```

Update an existing installation to the newest available release:

```bash
dotnet tool update -g dotnet-inspect \
  --add-source https://pkgs.dev.azure.com/richlander/dotnet-inspect/_packaging/dotnet-inspect@Local/nuget/v3/index.json \
  --interactive
```

Keep the company proxy enabled. RID-specific .NET tools also resolve host
packages during installation, so Azure Artifacts is an additional source
rather than a replacement for the configured proxy.

## Feed design

The `dotnet-inspect` feed is scoped to the private `dotnet-inspect` project and
has no NuGet.org upstream. Its `@Local` view is visible to authenticated
organization members. The feed contains only packages published by this
repository.

The package set matches NuGet.org and the GitHub release:

- `dotnet-inspect`
- `dotnet-inspect.win-x64`
- `dotnet-inspect.win-arm64`
- `dotnet-inspect.linux-x64`
- `dotnet-inspect.linux-arm64`
- `dotnet-inspect.osx-arm64`
- `dotnet-inspect.any`

Azure Artifacts package versions are immutable. A partial retry uses
`--skip-duplicate`, but a version is not complete until all seven packages are
present in both registries and the GitHub release exists.

## Publishing credentials

The GitHub `azure-artifacts` environment contains the `AZURE_DEVOPS_PAT`
secret. The workflow writes it only to an ephemeral runner-local NuGet
configuration. Rotate the token before it expires.

`AzureArtifactsMirrorWorkflowTests` is the gate that pins artifact reuse,
destination ordering, the complete feed URL, and pointer-last publication.
