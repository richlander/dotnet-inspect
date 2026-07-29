# NuGet feed authentication

How `dotnet-inspect` authenticates to a NuGet feed, which credential mechanisms it honors,
and which it silently ignores.

This document describes *what the tool does*. For what you *should* do — choosing between
Microsoft Entra tokens, PATs, service principals, and workload identity federation — follow
the Azure DevOps guidance, which is authoritative and kept current:

- [Authentication guidance](https://learn.microsoft.com/azure/devops/integrate/get-started/authentication/authentication-guidance)
  — picks a mechanism by scenario. Entra ID for applications; PATs only for personal, ad hoc use.
- [Use Microsoft Entra tokens](https://learn.microsoft.com/azure/devops/integrate/get-started/authentication/entra)
- [Service principals and managed identities](https://learn.microsoft.com/azure/devops/integrate/get-started/authentication/service-principal-managed-identity)
- [Manage PATs using policies](https://learn.microsoft.com/azure/devops/organizations/accounts/manage-pats-with-policies-for-administrators)
  — tenant policies for maximum PAT lifespan and scope restriction.
- [Consuming packages from authenticated feeds](https://learn.microsoft.com/nuget/consume-packages/consuming-packages-authenticated-feeds)
  and the [Azure Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider)
  — the recommended way to supply feed credentials.
- [Angel Wong's posts on the Azure DevOps blog](https://devblogs.microsoft.com/devops/author/wonga/)
  — the Senior PM who owns Azure DevOps authentication, and the best source for *direction of
  travel* rather than current state. Reference docs say what works now; this feed says what is
  being restricted next, with dates. Two that bear directly on this document:
  [Retirement of Global Personal Access Tokens](https://devblogs.microsoft.com/devops/retirement-of-global-personal-access-tokens-in-azure-devops/)
  (global PATs stop working December 1, 2026; the recommendation is "short-lived, Microsoft
  Entra-backed authentication") and
  [Authentication Tokens Are Not a Data Contract](https://devblogs.microsoft.com/devops/authentication-tokens-are-not-a-data-contract/)
  ("assume any token claim may change or disappear without notice"; tokens are being further
  encrypted, so anything decoding them will break). Their avatar has been a crossed-out "PATs"
  sign since 2021, which is a fair summary of the trajectory.

## What the tool sends

HTTP Basic, and nothing else:

```text
Authorization: Basic base64("{Username}:{Password}")
```

There is no bearer path and no token exchange. A PAT and a Microsoft Entra access token both
work, and work identically, because each is simply the password. Azure DevOps ignores the
username. This is also why there is no separate "client certificate" mode: a certificate
authenticates a service principal to Entra ID, Entra returns a token, and the feed still only
sees Basic. See
[`PackageSource.GetAuthHeader`](../../src/NuGetFetch/PackageRecords.cs).

## The credential sources

Two, described in full below: a `nuget.config` entry, and a credential provider.

### `nuget.config`

A [`packageSourceCredentials`](https://learn.microsoft.com/nuget/reference/nuget-config-file#packagesourcecredentials)
entry in a `nuget.config`, carrying **both** `Username` and `ClearTextPassword`:

```xml
<packageSourceCredentials>
  <my-feed>
    <add key="Username" value="pat" />
    <add key="ClearTextPassword" value="%TOKEN%" />
  </my-feed>
</packageSourceCredentials>
```

The config may come from `--nugetconfig`, from discovery walking up from the working directory,
or from the user-level config. Parsing lives in
[`SourceResolver`](../../src/NuGetFetch/SourceResolver.cs).

Note that the `%TOKEN%` placeholder above is *not* expanded — see the next section. It is
written that way only to keep a secret out of the example.

## What is ignored

NuGet's own guidance in
[Consuming packages from authenticated feeds](https://learn.microsoft.com/nuget/consume-packages/consuming-packages-authenticated-feeds#security-best-practices-for-managing-credentials)
ranks credential mechanisms from most to least secure. Lining that ranking up against what this
tool honors is the clearest statement of the gap:

| NuGet's rank | Mechanism | Supported here |
| --- | --- | --- |
| 1, "highly recommended" | Credential provider | **Yes** — see [Credential providers](#credential-providers). |
| 2 | Encrypted `<Password>` in `nuget.config` | **No** — only `ClearTextPassword` is parsed. `dotnet nuget add source --password` writes this form by default on Windows. |
| 3 | `%VAR%` macros in `nuget.config` | **No** — values are taken verbatim, so the placeholder is sent as the password. |
| 4 | `NuGetPackageSourceCredentials_<name>` | **No** — the environment is never consulted for credentials. |
| 5, "only ... where no other secure option is available" | `Username` + `ClearTextPassword` | **Yes.** |

The two supported mechanisms are the ranking's top and bottom, which is less odd than it looks:
rank 1 is the most secure where it exists, and rank 5 is the only one that exists everywhere.
Ranks 2 through 4 remain unsupported, and are still dropped in silence.

### Rank 5 is the floor, not a mistake

That table reads like an argument for dropping `ClearTextPassword` support and requiring a
credential provider. It is not, and the reason is worth recording so the conclusion is not
re-litigated: NuGet's ranking is *security* guidance, not *availability* guidance.

NuGet's [list of credential providers](https://learn.microsoft.com/nuget/consume-packages/consuming-packages-authenticated-feeds#list-of-credential-providers)
names three in the entire ecosystem — Azure Artifacts, AWS CodeArtifact, and MyGet, the last of
which is Visual Studio-only and therefore irrelevant to a CLI. Everything else authenticates by
putting a token in `nuget.config`:

| Feed | If rank 5 were dropped |
| --- | --- |
| Azure Artifacts | Fine — cross-platform provider exists. |
| AWS CodeArtifact | Split. Fine with the provider; broken with `aws codeartifact login`, which writes a plaintext token into the *user-level* `nuget.config` and is documented to do exactly that on Linux and macOS. |
| GitHub Packages | **Broken.** PAT-only, no provider, and the documented setup command is `dotnet nuget add source --username U --password T --store-password-in-clear-text`. |
| GitLab, Artifactory, Nexus, ProGet, Cloudsmith, Artifact Registry | **Broken.** No NuGet credential providers exist. |

So the mechanism NuGet ranks last is the only one that works everywhere, and for GitHub Packages
it is not merely the easiest path but the *only* path — GitHub's own documentation instructs
users to store the token in clear text.

The defect is therefore not that rank 5 is supported. It is that rank 5 was supported
*exclusively*, and that a dropped credential cannot be told apart from a missing package.
Credential provider support closes the first half for Azure Artifacts and AWS CodeArtifact; it
does nothing for GitHub Packages, where only better diagnosis helps.

Two mechanisms that are not on NuGet's list are also worth stating, because both look plausible
and neither works: a `ClearTextPassword` with no `Username` (both halves are required, even
though Azure DevOps ignores the username), and userinfo in the source URL
(`https://user:pass@host/...`, which is never turned into a header).

Every one of these is dropped without a diagnostic. Because an unauthenticated feed reports as
*package not found* rather than as an authentication failure (issue #3417, bug 1), each is
indistinguishable from a typo in the package name. That is what makes the silence expensive, and
it is why the diagnosis is worth fixing independently of which mechanisms are supported.

## Credential providers

A [NuGet credential provider](https://learn.microsoft.com/nuget/reference/extensibility/nuget-cross-platform-plugins)
is a separate executable that answers credential questions over a JSON protocol on stdin/stdout.
It is how a private feed is read with no secret stored anywhere on disk, and it is the mechanism
NuGet ranks first.

### Discovery: there is no registration step

Credential providers are **never registered in `nuget.config`**. The Azure Artifacts provider has
no install or register verb and writes to no configuration file. Discovery is entirely by
convention, over three routes tried in strict precedence order:

| Route | Where | Notes |
| --- | --- | --- |
| 1 | `NUGET_NETCORE_PLUGIN_PATHS` | Explicit list of entry-point files. |
| 2 | `NUGET_PLUGIN_PATHS` | Same, consulted only if route 1 is unset. Entries may be files or directories. |
| 3 | `~/.nuget/plugins/netcore/<Name>/<Name>.dll` and executables named `nuget-plugin-*` on `PATH` | The convention directory is what the classic install script populates; the `PATH` scan finds global dotnet tools. |

Either environment variable **replaces** route 3 rather than adding to it.

The `PATH` scan is the one most easily missed, because it looks like an ordinary tool install.
`dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool` creates no
convention directory at all — it only puts `nuget-plugin-microsoft-artifacts-credential-provider`
on `PATH`. An implementation that scans `~/.nuget/plugins` alone finds nothing on such a machine
while `dotnet restore` authenticates happily.

Implementation: [`PluginDiscovery`](../../src/NuGetFetch/Plugins/PluginDiscovery.cs), mirroring
`PluginDiscoverer.cs` and `PluginDiscoveryUtility.cs` in
[NuGet.Client](https://github.com/NuGet/NuGet.Client/tree/dev/src/NuGet.Core/NuGet.Protocol/Plugins).

### The conversation

The plugin is launched as `<plugin> -Plugin` (or `dotnet <plugin.dll> -Plugin`), and both sides
exchange one compact JSON object per line, UTF-8 without a BOM. The sequence is handshake,
`MonitorNuGetProcessExit`, `Initialize`, `GetOperationClaims`, `SetLogLevel`, and then
`GetAuthenticationCredentials`.

Two details are easy to get wrong and are pinned by tests:

- **The handshake is symmetric.** The plugin sends its *own* handshake request at the same time
  as the host sends one. A host that only waits for a reply, without answering, deadlocks.
- **`Progress` messages restart the request timer.** They are the plugin saying "still working"
  during a slow sign-in. A host that ignores them times out a request that is progressing fine.

Implementation: [`PluginConnection`](../../src/NuGetFetch/Plugins/PluginConnection.cs) and
[`PluginCredentialProvider`](../../src/NuGetFetch/Plugins/PluginCredentialProvider.cs).

Plugins are started lazily and kept for the process lifetime, because a launch costs a process
start plus five round trips. A plugin that fails to start, or that does not claim the
`Authentication` operation, is remembered as unusable rather than retried.

### Unattended by default

Credentials are requested with `IsNonInteractive` set and `CanShowDialog` clear, matching
`dotnet restore` without `--interactive`. A tool that may run in CI must not block on a sign-in
prompt. Cached credentials and tokens supplied through the environment still work; only
interactive sign-in is withheld.

On Linux the v2.0.2 Azure Artifacts tool package ships no `msalruntime.so`, so its interactive
and broker paths throw `DllNotFoundException` regardless. Supplying
`ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN` avoids those paths entirely.

## Preemptive credentials versus 401-driven credentials

The official client is 401-driven: it "will make an unauthenticated request, and if the server
responds with an HTTP 401 response, NuGet will search for credentials".

This tool now works both ways, and the split is deliberate:

- A credential **parsed from `nuget.config`** is attached preemptively, as before. It is already
  in hand, and withholding it would only add a round trip.
- A credential from a **plugin** is acquired only after a 401, because acquiring it costs a
  process launch. A public feed must not pay that.

The 401 loop lives in
[`PluginAuthenticationHandler`](../../src/NuGetFetch/Plugins/PluginAuthenticationHandler.cs), a
`DelegatingHandler` installed into the shared client. This copies NuGet's own structure —
[`HttpSourceAuthenticationHandler`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSourceAuthenticationHandler.cs)
is also a `DelegatingHandler` — and the reason to copy it is that the loop needs the
`HttpResponseMessage` in hand. Doing it in the pipeline rather than at call sites means every
request is covered, including any whose call site forgot to pass a credential.

Behaviour follows NuGet's:

| Aspect | Behaviour |
| --- | --- |
| Trigger | 401 always; 403 only when explicitly enabled, since 403 usually means "authenticated but not permitted" |
| Retry bound | 4 attempts per request, matching `AmbientAuthenticationState.MaxAuthRetries` |
| `IsRetry` | Clear on the first ask, set afterwards, so a plugin replaces a cached token the feed has already rejected |
| Scope | Credentials are cached per scheme, host and port, so one feed's token is never offered to another |
| Concurrency | Concurrent requests to one source acquire credentials once |
| Precedence | A credential already on the request is never overwritten |

The `-IsRetry` flag matters more than it looks. The provider's own help says that without it
"INVALID CREDENTIALS MAY BE RETURNED. The caller is required to validate returned credentials
themselves, and if invalid, should call the credential provider again with -IsRetry set."

Credentials from a plugin are **not** written to any configuration file. They live in memory for
the process lifetime and are re-acquired on the next run.

Bug 1 (issue #3417) — an authentication failure reported as *package not found* — is not a
prerequisite for any of this, because the handler never needs the status code to reach a caller.
It remains worth fixing so that a feed which refuses every available credential says so. NuGet's
wording is a good target: `NU1301: Unable to load the service index for source ... 401
(Unauthorized)`.

## Service index discovery

Resolving a package from a non-nuget.org feed takes two requests: the V3 service index, to find
the `PackageBaseAddress` endpoint, and then the flat-container version index. Azure DevOps
authenticates both.

`NuGetFetch.NuGetClient.GetPackageBaseAddressAsync` takes no credential and issues a bare
`GetStreamAsync`, so its service index request is always anonymous. A caller that passes a
credential to `GetVersionsAsync` and points it at a private feed therefore fails at discovery,
before the credential is ever offered.

The CLI does not hit this, because its package path does not go through `NuGetClient`:
[`PackageExtractor`](../../src/DotnetInspector.Packages/PackageExtractor.cs) has its own
service-index reader that passes `source.GetAuthHeader()` on the discovery request. The gap is
confined to the `NuGetFetch` library.

Where a credential provider is available the handler covers this case anyway, since it sits in
the HTTP pipeline and so sees the anonymous service index request 401 like any other. That is a
second reason NuGet put the loop in a handler: no call site can forget to participate. It is not
a fix for the `nuget.config` path, which still depends on the caller threading a credential
through.

## Tests

Two tiers, in `src/NuGetFetch.Tests`:

- **Hermetic**, no network and no real plugin binary, runs in PR CI:
  - `CredentialMechanismTests` pins every row of the ranking table above.
  - `ServiceIndexAuthenticationTests` pins which of the two requests carries the credential, and
    that a 401 stays distinguishable from a 404.
  - `PluginDiscoveryTests` pins all three discovery routes and their precedence, over a temporary
    directory tree and `PATH`.
  - `PluginAuthenticationHandlerTests` pins the 401 loop: retry bound, `IsRetry` progression,
    per-authority scoping, 403 opt-in, and that an existing credential is not overwritten.
  - `PluginProtocolTests` runs a **real plugin process** — a shell script that genuinely speaks
    the line protocol — so framing, the symmetric handshake, `Progress`-driven timeout extension,
    and shutdown are exercised end to end rather than mocked.
- **Live**, tagged `[Trait("Network", "Live")]` and skipped unless a feed and token are supplied.
  `AzureDevOpsFeedTests` covers the config path; `AzureDevOpsCredentialProviderTests` covers the
  provider path against a genuinely installed provider. Only a real Azure DevOps feed exercises
  an authenticated service index.

CI runs the offline tier only:

```bash
dotnet run --project src/NuGetFetch.Tests -c Release -- -trait- "Network=Live"
```

The live tier needs a private feed, which CI and fork PRs do not have. To run it locally, mint a
token rather than storing one — the feed accepts an Entra access token exactly as it accepts a
PAT:

```bash
export DOTNET_INSPECT_TEST_AZDO_FEED=https://pkgs.dev.azure.com/ORG/PROJECT/_packaging/FEED/nuget/v3/index.json
export DOTNET_INSPECT_TEST_AZDO_TOKEN=$(az account get-access-token \
  --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv)
dotnet run --project src/NuGetFetch.Tests -c Release -- -trait "Network=Live"
```

The token is read from the environment and never written to a config file.

`AzureDevOpsCredentialProviderTests` additionally needs a provider on the machine and a token it
will hand out:

```bash
dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool
export ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN=$(az account get-access-token \
  --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv)
export ARTIFACTS_CREDENTIALPROVIDER_URI_PREFIXES=https://pkgs.dev.azure.com/ORG/
```

Those tests skip, rather than fail, when no provider is discoverable — so they are quiet on a
machine that has never installed one.
