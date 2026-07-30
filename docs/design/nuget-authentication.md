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

Credentials embedded in the source URL itself — `https://<user>:<access-token>@host/...` — are
ignored too, and that is not an omission here. NuGet does not support them at all: the client
never sends URL userinfo, so no feed authenticates that way, and this tool has no code that
reads it. The shape is a git and curl convention, which is precisely why operators reach for it,
and why the failure message redacts it rather than assuming it will never turn up.

The two supported mechanisms are the ranking's top and bottom. Rank 1 is the most secure where
it is available; rank 5 is available everywhere. Ranks 2 through 4 remain unsupported, and are
still dropped in silence.

### Why clear text is still supported

The table invites the conclusion that `ClearTextPassword` should be dropped in favour of
requiring a credential provider. It is worth recording why that has not been done: NuGet's
ranking is security guidance, not availability guidance.

NuGet's [list of credential providers](https://learn.microsoft.com/nuget/consume-packages/consuming-packages-authenticated-feeds#list-of-credential-providers)
names three in the entire ecosystem — Azure Artifacts, AWS CodeArtifact, and MyGet, the last of
which is Visual Studio-only and so does not apply to a CLI. Other feeds authenticate by putting
a token in `nuget.config`:

| Feed | If rank 5 were dropped |
| --- | --- |
| Azure Artifacts | Works — a cross-platform provider exists. |
| AWS CodeArtifact | Mixed. Works with the provider; not with `aws codeartifact login`, which writes a plaintext token into the user-level `nuget.config` on Linux and macOS. |
| GitHub Packages | Unsupported. PAT-only, no provider, and the documented setup command is `dotnet nuget add source --username U --password T --store-password-in-clear-text`. |
| GitLab, Artifactory, Nexus, ProGet, Cloudsmith, Artifact Registry | Unsupported. No NuGet credential providers exist. |

The mechanism NuGet ranks last is therefore the one with the widest reach, and for GitHub
Packages it is the only documented option.

The gap this document describes is not that rank 5 is supported. It is that rank 5 was, until
recently, the only supported mechanism, and that a dropped credential cannot be told apart from
a missing package. Credential provider support addresses the first point for Azure Artifacts and
AWS CodeArtifact. It does not help GitHub Packages, where better diagnosis is the only remedy.

Two mechanisms that are not on NuGet's list are also worth stating, because both look plausible
and neither works: a `ClearTextPassword` with no `Username` (both halves are required, even
though Azure DevOps ignores the username), and userinfo in the source URL
(`https://user:pass@host/...`, which is never turned into a header).

Every one of these is dropped without a diagnostic, so a source configured through an
unsupported mechanism is read as though no credential were supplied at all.

What that then looks like has changed. A source that answers 401 or 403 is now reported as
unreadable rather than as a missing package, naming the source, the status, and the phase:

```console
$ dotnet-inspect package Markout --source https://pkgs.dev.azure.com/<org>/<project>/_packaging/<feed>/nuget/v3/index.json
Error: Package 'markout' could not be resolved because a source requires credentials.
  https://pkgs.dev.azure.com/<org>/<project>/_packaging/<feed>/nuget/v3/index.json — HTTP 401 Unauthorized while reading the service index
The package may exist; the source was not readable. Supply credentials for this source and retry.
```

A genuine 404 still reports as *not found*, because that is the one status that means the
package is actually absent. The remaining gap is narrower than it was: the credential is still
dropped silently, but the resulting failure is no longer indistinguishable from a typo in the
package name.

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

This tool now works both ways, and the split follows from what is known at the time:

- A credential **parsed from `nuget.config`** is attached preemptively, as before. The user has
  already declared that this credential belongs to this source, so there is nothing to discover
  and no reason to wait to be asked.
- A credential from a **plugin** is acquired only after a 401. Nothing about a source URL says
  whether it is public or private; the 401 is the signal. Asking a provider to produce a token
  for a feed that never requested one would mint credentials that are not needed and widen the
  set of hosts they exist for.

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

Bug 1 (issue #3417) — an authentication failure reported as *package not found* — was never a
prerequisite for any of this, because the handler never needs the status code to reach a caller.
It has since been fixed: a feed that refuses every available credential now says so, naming the
source, status, and phase. The mechanism is described under [Reporting an unreadable
source](#reporting-an-unreadable-source).

## Reporting an unreadable source

Azure DevOps answers **401** rather than 404 for a feed the caller cannot see, so an
unauthenticated private feed and a mistyped package name arrive at the same place: nothing
resolved. Reporting both as *package not found* was issue #3417 bug 1, and it is the failure
users hit most often once credentials are configured, because an expired credential is the
normal end state of every PAT.

The status is known inside
[`HttpRetryHelper`](../../src/DotnetInspector.Packages/HttpRetryHelper.cs), but the signatures
between there and the caller return `string?` and `List<string>?`, so it cannot be returned
without changing every one of them. Instead
[`FeedFailureTelemetry`](../../src/DotnetInspector.Core/FeedFailureTelemetry.cs) follows the
ambient-scope shape already used by `NetworkTelemetry`: a scope is opened around each package
acquisition, nested async work records into the same collector, and the "nothing resolved" path
consults it before choosing a message.

The scope is opened per *hop*, inside the tool-wrapper redirect loop, rather than once around
the whole traversal. Each hop resolves a different package id, so a shared collector would let
a refusal recorded while fetching the wrapper explain the redirect target going missing — and
the recorded URL carries the wrapper's id in its flat-container path, so the message would name
a source and a package that had nothing to do with the failure.

Two further rules keep the message honest:

- **404 is never recorded.** It is the one status that genuinely means the package is absent,
  so a real miss still reports *not found*. A recorder that captured every non-success status
  would destroy that message, which is why the test suite pins the 404 case as a control.
- **A recorded failure is advisory, not fatal.** The collector is only consulted when the
  overall lookup produced nothing, so if one source 401s and another answers, the successful
  result stands. That is this codebase's answer to the third open design question in #3417.

The phase (`reading the service index`, `listing versions`) is taken from the ambient
`NetworkTrafficKind`, which the network telemetry scope already tracks, so no call site had to
be taught to describe itself.

### The URL is redacted before it is stored

This message prints a source URL, and a source URL can carry a secret: userinfo
(`https://user:pass@host/...`, a shape NuGet does not support and this tool
never reads, but which operators type out of git and curl habit) or a token query parameter. The URL is therefore passed through
`NetworkRequestObservation.RedactSensitiveUrlText` on the way *into* the collector, not on the
way out to the console — `FeedFailureCollector.Failures` is public, so an unredacted URL sitting
in it would already be an exposure.

```console
$ dotnet-inspect package Markout --source 'https://<user>:<access-token>@pkgs.dev.azure.com/<org>/<project>/_packaging/<feed>/nuget/v3/index.json?access_token=<access-token>'
  https://pkgs.dev.azure.com/<org>/<project>/_packaging/<feed>/nuget/v3/index.json?access_token=REDACTED — HTTP 401 Unauthorized while reading the service index
```

Only the credential is removed. The host *and the whole path* survive, because on Azure DevOps
the organization, project and feed name all live in path segments, and an operator reading this
line needs to know **which** source refused — two feeds in the same organization differ only
there. Redaction that collapsed the path would leave a message unable to do its one job.

Query names are matched on fragments (`token`, `key`, `secret`, `password`, `credential`,
`auth`, `sig`) rather than against a list of exact names. The same credential travels as
`access_token`, `accessToken`, `apiKey` or `x-api-key` depending on the feed, and an exact-name
list quietly passes every spelling it has not been taught. Fragment matching errs toward
redacting a parameter that turns out to be harmless, which costs a little diagnostic detail;
the reverse error prints a live credential.

A credential can also ride in the *path*: MyGet issues service index URLs shaped like
`https://host/F/<feed>/auth/<token>/api/v3/index.json`. The segment following an `auth` segment
is therefore redacted too. Nothing else in the path is, deliberately — an Azure DevOps
organization, project and feed all live in path segments, and blanking them would leave a
message that cannot distinguish two feeds on the same host, which is precisely what this
message exists to do.

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
