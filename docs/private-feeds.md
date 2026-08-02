# Using a Private NuGet Feed

This document explains how to give dotnet-inspect access to a private NuGet feed.

dotnet-inspect reads packages from the sources in your `nuget.config` — the same file
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

Add the feed, with no credential in it:

```bash
dotnet nuget add source https://pkgs.dev.azure.com/<org>/<project>/_packaging/<feed>/nuget/v3/index.json --name my-feed
```

Then use dotnet-inspect normally:

```bash
dotnet-inspect package MyCompany.Widgets
```

On a desktop machine the first run signs you in interactively and caches the token; later runs
reuse it. On a headless machine, supply a token through the environment as shown under
[Unattended and CI](#unattended-and-ci).

## Unattended and CI

In Azure Pipelines, add `NuGetAuthenticate@1` before any step that runs dotnet-inspect. It
installs the provider on the agent and points it at the build identity, so there is no token to
store anywhere:

```yaml
- task: NuGetAuthenticate@1
- script: dotnet-inspect package MyCompany.Widgets
```

Elsewhere, supply the token through `ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS`, the
documented mechanism for unattended agents outside Azure DevOps Pipelines. It takes the feed's
index URL as it appears in your `nuget.config`, so one form covers Azure Artifacts and every
other feed the provider serves:

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
| Encrypted `<Password>` | Not read. `dotnet nuget add source --password` writes this form by default on Windows; add `--store-password-in-clear-text`. |
| `NuGetPackageSourceCredentials_<name>` environment variable | Not read. |
| A token in the source URL, `https://user:token@host/...` | Not read. Put it in `packageSourceCredentials`. |

This file holds a token in plain text, so keep it out of source control. If you keep it outside
the normal discovery path, point at it explicitly:

```bash
dotnet-inspect package MyCompany.Widgets --nugetconfig ./private.nuget.config
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
