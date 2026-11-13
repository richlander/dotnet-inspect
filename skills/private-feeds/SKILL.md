---
name: dotnet-inspect-private-feeds
version: 0.1.0
description: Inspect packages from private or folder NuGet feeds using explicit sources, mapping and noninteractive authentication; preserve source authority and distinguish failures from package absence.
---

# dotnet-inspect: Inspect NuGet Packages from Private and Local Feeds

Use this skill when package evidence lives outside NuGet.org.
Run `dnx dotnet-inspect -y -- <command>`. Start with a credential-free
configuration and a NuGet credential provider; keep tokens out of repositories,
URLs and command lines.

## Select and pin the source

```bash
dnx dotnet-inspect -y -- package MyCompany.Widget@1.2.3 \
  --nugetconfig ./NuGet.Config
dnx dotnet-inspect -y -- package MyCompany.Widget --versions-with-feed \
  --nugetconfig ./NuGet.Config
dnx dotnet-inspect -y -- package MyCompany.Widget --versions -n 1 \
  --source ./feed
dnx dotnet-inspect -y -- package MyCompany.Widget@1.2.3 \
  --source ./feed --path @readme --content --raw
```

`--nugetconfig` selects exactly that configuration. Repeatable `--source`
replaces configured sources; `--add-source` augments them for one invocation.
Folder feeds accept a path or `file://` URI. Source order is not version
precedence: discovery chooses among eligible observations. Pin `Id@version`
when the exact coordinate matters; `Id@latest --versions` requests a fresh
latest-version check, and `--preview` opts into prereleases.

`find --packages` is NuGet.org-only and rejects source overrides.
For API/history inspection, carry the same `--source`/`--nugetconfig` and TFM
through every probe. Load `skill compatibility` for ranges, checkpoints and
`--major-versions`; do not reuse ordinals from an unlisted-inclusive version
listing in a listed-only history vector.

For detailed command admission use `--help`; for unfamiliar inspection/query
depth use `explain "<question>"` and follow its returned binding.

## Source mapping

Use normal `<packageSources>` plus `<packageSourceMapping>` in NuGet.Config.
Every requested package, including dependencies and tool redirects, needs an
eligible source. Exact-ID mapping wins over prefixes; otherwise the longest
prefix wins.

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

An override does not disable mapping. Its endpoint must match a configured
source to retain that source's mapped name. Do not recover mapping failures by
silently switching to another authority.

## Authenticate noninteractively

Install Microsoft's Azure Artifacts provider:

```bash
dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool
```

dotnet-inspect discovers `NUGET_NETCORE_PLUGIN_PATHS`, `NUGET_PLUGIN_PATHS`,
the normal `~/.nuget/plugins/netcore/` directory, and `nuget-plugin-*` tools on
PATH. Providers run after a `401` and are requested noninteractively: cached
and environment-supplied credentials work, but no sign-in prompt opens.
`az login` alone does not supply a NuGet credential.

In Azure Pipelines, run `NuGetAuthenticate@1` before inspection. Outside it,
use the provider's documented unattended input such as
`ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS`, without printing its
expanded secret value. For build identities, the provider also accepts
`ARTIFACTS_CREDENTIALPROVIDER_URI_PREFIXES` and
`ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN`; certificate inputs use
`ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS`.

If no provider serves the feed, a private config outside the repository can
supply both `Username` and `ClearTextPassword` under the matching
`packageSourceCredentials` key. Do not rely on `%VAR%` expansion, encrypted
`Password`, `NuGetPackageSourceCredentials_*`, or URL credentials:
dotnet-inspect does not consume those forms.

## Share a credential-free declaration

```bash
dnx dotnet-inspect -y -- workspace \
  --package Private.Package@1.2.3 --tfm net10.0 \
  --nuget-source-auth-required \
    https://nuget.pkg.github.com/example/index.json --share url
```

Inspect Web prompts per authentication-required endpoint. Credentials stay in
page-session memory, not the packet, URL, browser storage or telemetry.
Reloading prompts again. The declaration carries source requirements, not
credentials or proof that acquisition succeeded.

## Diagnose failures without changing authority

A `401` means unreadable source, not absent package. Check provider discovery,
endpoint matching, token scope and expiry before changing the ID. Missing
folders and malformed archives are source failures too. A healthy peer does
not make incomplete automatic version discovery complete.

Payload caches do not authorize a new source or introduce version candidates.
Retain source provenance and warnings; a warm cache must not become an
unqualified cross-feed answer. `--no-nuget-cache` excludes the NuGet global
package-cache layer where supported.

`--offline` prohibits network and credential plugins; successful inspection
requires already authorized local content. It is not a metadata-only online
version query. An offline cache miss is not evidence that the package does
not exist.
