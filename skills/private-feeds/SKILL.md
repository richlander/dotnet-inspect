---
name: dotnet-inspect-private-feeds
version: 0.1.0
description: Inspect packages from private and custom NuGet feeds safely — select and map sources, use credential providers or explicit configuration, diagnose authentication failures, and reason about source-bound caches and offline operation.
---

# dotnet-inspect: private NuGet feeds

Use this skill when package evidence lives outside NuGet.org. Start with a
credential-free source configuration and a NuGet credential provider; keep
tokens out of repositories and command lines.

```bash
dnx dotnet-inspect -y -- <command>
```

## Select the feed

`--nugetconfig` uses exactly the named configuration. `--source` replaces the
configured source set and can repeat; `--add-source` augments it for one run.
Use a source-only config when a credential provider supplies authentication:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="private" value="https://example.com/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

```bash
dnx dotnet-inspect -y -- package MyCompany.Widget --nugetconfig ./NuGet.Config
dnx dotnet-inspect -y -- package search Widget --nugetconfig ./NuGet.Config
dnx dotnet-inspect -y -- package MyCompany.Widget --versions-with-feed \
  --nugetconfig ./NuGet.Config
```

Version discovery combines all eligible sources and chooses the highest
semantic version; source order is not precedence. Pin `Package@Version` when
the exact coordinate matters.

### Query versions from a folder feed

Online version queries support NuGet V2/V3 folder feeds, specified as a
path, a `file://` URI, or a mapped source in `NuGet.Config`:

```bash
dnx dotnet-inspect -y -- package MyCompany.Widget --versions --source ./feed
dnx dotnet-inspect -y -- package MyCompany.Widget --versions -n 5 --preview \
  --source ./feed --jsonl
dnx dotnet-inspect -y -- package MyCompany.Widget --latest-version --source ./feed
dnx dotnet-inspect -y -- package MyCompany.Widget@1.2.3 --version --source ./feed
dnx dotnet-inspect -y -- package MyCompany.Widget@1.0.0..2.0.0 --versions \
  --source ./feed --include-unlisted
dnx dotnet-inspect -y -- package MyCompany.Widget --versions-with-feed \
  --source ./feed --add-source https://api.nuget.org/v3/index.json
```

Local and HTTP versions are combined and sorted before the result limit.
Missing folders or invalid archives are source failures, not package absence;
usable peer results carry an explicit partial warning on stderr. Local reads
use bounded enumeration rather than treating filenames as version evidence.
Latest, single-version discovery, and range selection require complete
evidence: an unreadable peer makes them fail instead of choosing from a
healthy subset. A pinned verification may report a coordinate observed on a
readable feed, with peer failures disclosed. `--include-unlisted` and
`--versions-with-feed` retain their listing and feed columns; local versions
use the non-Gallery `listed` convention.

These are metadata-only queries without `--offline`. Local-feed payload
inspection, including latest/wildcard/range selection that downloads packages,
has not migrated yet.

### Restrict package ids to feeds

dotnet-inspect honors `<packageSourceMapping>` from the selected NuGet
configuration:

```xml
<packageSourceMapping>
  <packageSource key="private">
    <package pattern="MyCompany.*" />
  </packageSource>
  <packageSource key="nuget.org">
    <package pattern="*" />
  </packageSource>
</packageSourceMapping>
```

An exact package id wins over prefixes; otherwise the longest matching prefix
wins. Mapping is applied independently to top-level packages, dependencies,
RID companions, platform packs, tool redirects, searches, and routing probes.
Every package id must match an active named source. `--source` and
`--add-source` do not disable mapping; an override must match the configured
endpoint to retain its mapped source name.

## Azure Artifacts credential provider

Install Microsoft's provider as a global tool. dotnet-inspect discovers
`NUGET_NETCORE_PLUGIN_PATHS`, then `NUGET_PLUGIN_PATHS`, then
`~/.nuget/plugins/netcore/` and `nuget-plugin-*` executables on `PATH`.

```bash
dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool
```

Credential plugins run only after a feed answers `401`. dotnet-inspect requests
credentials noninteractively: cached and environment-supplied credentials work,
but it never opens a sign-in prompt. `az login` alone does not supply a NuGet
credential.

In Azure Pipelines, authenticate before restore or inspection:

```yaml
- task: NuGetAuthenticate@1
- script: dotnet restore
- script: dnx dotnet-inspect -y -- package MyCompany.Widget
```

Outside Azure Pipelines, provide the provider's documented unattended
credential input. Do not print the expanded value:

```bash
export ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS='{"endpointCredentials":[{"endpoint":"https://example.com/nuget/v3/index.json","username":"unused","password":"'"$TOKEN"'"}]}'
```

For an Azure Pipelines-compatible build identity, the provider also accepts
`ARTIFACTS_CREDENTIALPROVIDER_URI_PREFIXES` with
`ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN`. Service-principal certificate
configuration uses `ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS`.

## Explicit credentials

When no provider serves the feed, dotnet-inspect accepts both `Username` and
`ClearTextPassword` under a matching `packageSourceCredentials` key. Keep that
config outside the repository and select it with `--nugetconfig`.

Do not rely on `%VAR%` expansion, encrypted `<Password>`,
`NuGetPackageSourceCredentials_*`, or credentials embedded in a source URL;
dotnet-inspect does not use those forms.

## Diagnose failures and caches

A reported `401 Unauthorized` means the source was unreadable, not that the
package is absent. Check provider discovery, URI matching, token scope, and
expiration before changing the package id.

dotnet-inspect's package payload and candidate caches retain source provenance.
The NuGet global folder is payload-only: it can fulfill an exact resolved
coordinate only when `.nupkg.metadata.source` names an authorized producer.
Missing or mismatched provenance is a cache miss, and installed payloads do not
introduce version candidates. Use `--no-nuget-cache` to exclude that layer.
`--offline` forbids network access and does not start credential plugins, so it
succeeds only from producer-authorized caches. Online version queries bypass
these legacy caches. Outside metadata-only online version queries,
configured folder feeds remain on legacy paths that skip them;
`--verbose` reports the skip. Pass a local `.nupkg` path directly for package
inspection when the package is available as a file.

```bash
dnx dotnet-inspect -y -- package MyCompany.Widget@1.2.3 \
  --source https://example.com/nuget/v3/index.json --no-nuget-cache
dnx dotnet-inspect -y -- package MyCompany.Widget@1.2.3 \
  --nugetconfig ./NuGet.Config --offline
```
