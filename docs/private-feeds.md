# Using a Private NuGet Feed

This document explains how to give `dotnet-inspect` access to a private NuGet feed.

`dotnet-inspect` reads packages from the sources in your `nuget.config` — the same file
`dotnet restore` uses — so a feed that already works for `dotnet restore` usually works here too.
A private feed needs credentials. There are two ways to supply them, and a credential provider is
the one to prefer.

Examples below run `dotnet-inspect` directly. With `dnx`, prefix each command with
`dnx dotnet-inspect -y --`.

## Recommended: a credential provider

A credential provider is a small executable that NuGet asks for credentials when a feed answers
401. It keeps the token out of your files, so there is nothing to accidentally commit, and it
refreshes expired tokens without you editing anything.

Providers exist for Azure Artifacts and AWS CodeArtifact. Install the Azure Artifacts one as a
global tool:

```bash
dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool
```

There is no registration step, and nothing to add to `nuget.config`. NuGet discovers the provider
by name on `PATH`.

`dotnet-inspect` finds a provider the same way `dotnet restore` does: the
`NUGET_NETCORE_PLUGIN_PATHS` and `NUGET_PLUGIN_PATHS` variables, then `~/.nuget/plugins/netcore/`
and executables named `nuget-plugin-*` on `PATH`. It launches the one it finds as a separate
process and asks it for credentials over the standard NuGet plugin protocol. Those routes are the
ones implemented by NuGet's own
[`PluginDiscoverer`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Protocol/Plugins/PluginDiscoverer.cs),
which reads the same variables, scans `PATH`, and matches the same `nuget-plugin-` prefix. A
provider that already works for `dotnet restore` works here, with nothing further to install or
configure.

Add the feed, with no credential in it:

```bash
dotnet nuget add source https://pkgs.dev.azure.com/<org>/<project>/_packaging/<feed>/nuget/v3/index.json --name my-feed
```

Then use `dotnet-inspect` normally:

```bash
dotnet-inspect package MyCompany.Widgets
```

On a desktop machine the first run signs you in interactively and caches the token; later runs
reuse it. On a headless machine, supply a token through the environment as shown under
[Unattended and CI](#unattended-and-ci).

## Unattended and CI

In Azure Pipelines, add `NuGetAuthenticate@1` before any step that runs `dotnet-inspect` or
`dotnet restore`. Both consume the same credentials from the same place, so one task covers both.
It installs the provider on the agent and points it at the build identity, so there is no token to
store anywhere:

```yaml
- task: NuGetAuthenticate@1
- script: dotnet restore
- script: dotnet-inspect package MyCompany.Widgets
```

Elsewhere, you need to supply the token yourself, through
`ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS`. This is what the provider's documentation
directs unattended agents outside Azure DevOps Pipelines to use — see
[Other automated build scenarios](https://github.com/microsoft/artifacts-credprovider#other-automated-build-scenarios).
It takes the feed's index URL as it appears in your `nuget.config`, so one form covers Azure
Artifacts and every other feed the provider serves:

```bash
export ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS='{"endpointCredentials":[{"endpoint":"https://example.com/index.json","username":"unused","password":"'"$TOKEN"'"}]}'
```

The username is not used by Azure DevOps. Any token the feed accepts works as the password,
including a Microsoft Entra access token where personal access tokens are disabled.

To authenticate as a service principal with a certificate — no interactive session, and every
NuGet tool on the machine picks it up from the same place:

```bash
export ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS='{"endpointCredentials":[{"endpoint":"https://example.com/index.json","clientId":"<app-id>","clientCertificateSubjectName":"<subject>"}]}'
```

Use `clientCertificateFilePath` instead of `clientCertificateSubjectName` to point at a
certificate file rather than the certificate store.

The examples above use `export`, the shell syntax. In PowerShell, set the same variables with
`$env:ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS = '...'`, and in `cmd.exe` with
`set ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS=...`. The variable names and the JSON
are identical on every platform.

## Alternative: a credential in `nuget.config`

Use this when your feed has no credential provider. GitHub Packages, GitLab, Artifactory, Nexus,
ProGet, Cloudsmith and Artifact Registry are all in this category.

Both `Username` and `ClearTextPassword` are required, even for feeds that ignore the username:

```xml
<packageSourceCredentials>
  <my-feed>
    <add key="Username" value="unused" />
    <add key="ClearTextPassword" value="YOUR_TOKEN" />
  </my-feed>
</packageSourceCredentials>
```

The source name inside `<packageSourceCredentials>` must match the `key` of the entry in
`<packageSources>`.

Four forms look like they should work here and do not:

| Form | What happens |
| --- | --- |
| `%TOKEN%` macro | Not expanded. The literal text `%TOKEN%` is sent as the password. |
| Encrypted `<Password>` | Not read. See below. |
| `NuGetPackageSourceCredentials_<name>` environment variable | Not read. |
| A token in the source URL, `https://user:token@host/...` | Not read. Put it in `packageSourceCredentials`. |

The encrypted `<Password>` form is the one most likely to catch you out, because
`dotnet nuget add source --password` writes it by default on Windows. It is encrypted with a
Windows-only, user-specific mechanism, so the value cannot be read on Linux or macOS, cannot be
read by a different user, and cannot be copied between machines. NuGet itself gives up on it with
*"Password decryption is not supported on .NET Core for this platform … You can use a clear text
password as a workaround"*, and that workaround is what this tool supports. Pass
`--store-password-in-clear-text` when adding the source, or write the `ClearTextPassword` entry
by hand.

This file holds a token in plain text, so it should not live inside a repository at all. NuGet
discovers `nuget.config` by walking up from the working directory, which makes a working tree a
convenient place to put one and a dangerous one — a config written next to your code can be
committed and published along with it.

Put it in your user-level NuGet configuration instead, which applies to every project without
sitting next to any of them. To keep it somewhere else entirely, point at it explicitly:

```bash
dotnet-inspect package MyCompany.Widgets --nugetconfig ~/private-feeds/nuget.config
```

## Cached packages stay with their source

Downloaded content is cached against the source it came from. A later run is served that content
only if its configuration still lists that source; otherwise the package is fetched again, or
reported as not found.

This matters when a package id and version exist on more than one feed. Removing a private feed
from your configuration does not leave its packages readable from cache, and adding a public feed
does not let public content stand in for a package you previously read from a private one. Each
source's copy is kept separately, so switching between configurations gives you what that
configuration's sources actually serve.

Two practical consequences:

- Inspecting one package from two feeds stores it twice. This is deliberate — the two feeds may
  not be publishing identical bytes.
- `--source` and `--add-source` take part in this. A run that replaces your sources will not read
  content cached under the sources it replaced.

The NuGet global folder (`~/.nuget/packages`) is also a payload cache, but its directory layout
does not include a source. dotnet-inspect therefore reads the `source` recorded in the package's
`.nupkg.metadata` file and uses the payload only when that producer is authorized for the exact
coordinate. A package restored from a removed or different feed, or one with missing or malformed
source metadata, is ignored.

For a concrete `Package@Version`, any active eligible feed can authorize a matching global-folder
payload. For a discovered version, such as bare `Package`, `Package@latest`, or a wildcard, the
recorded producer must be one of the feeds that reported the selected version. Installed payloads
never introduce version candidates by themselves.

Use `--no-nuget-cache` to disable the global folder entirely. This is useful when testing a cold
feed path; it is not required for strict source fidelity, which is always enforced:

```bash
dotnet-inspect package MyCompany.Widgets --source https://example.com/index.json --no-nuget-cache
```

## Checking a source without changing your config

`--add-source` adds one feed for a single run, and `--source` replaces the configured sources
entirely. Both are useful for confirming which feed answers:

```bash
dotnet-inspect package MyCompany.Widgets --add-source https://example.com/index.json
```

## When a feed cannot be read

A source that requires credentials you have not supplied is reported as unreadable, naming the
source and the status:

```text
Error: Package 'mycompany.widgets' could not be resolved because a source requires credentials.
  https://example.com/index.json — HTTP 401 Unauthorized while reading the service index
The package may exist; the source was not readable. Supply credentials for this source and retry.
```

This means the credential was missing, rejected, or written in one of the unsupported forms
above. A package that is genuinely absent reports *not found* instead, so the two cases are
distinguishable.

### Linux

Version 2.0.2 of the Azure Artifacts provider cannot start its MSAL paths on a current Ubuntu,
because it depends on `libwebkit2gtk-4.0`, which Ubuntu 24.04 no longer ships. This affects
interactive sign-in *and* the silent token-cache read, so the provider fails even when a valid
cached token exists.

Set the token in the environment instead, as under [Unattended and CI](#unattended-and-ci). The
environment-driven paths do not use MSAL and are unaffected.

## See also

- [NuGet Feed Authentication](design/nuget-authentication.md) — how the tool authenticates, which
  flow supplies the credential in each environment, and why each unsupported form behaves as it
  does.
