# NuGet feed authentication

How `dotnet-inspect` authenticates to a NuGet feed, which credential mechanisms it honors,
and which it silently ignores.

For instructions on setting up access to a private feed, see
[Private NuGet Feeds](../private-feeds.md). This document describes *what the tool does*. For
what you *should* do — choosing between
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

## Supported credential forms

Two forms are supported: a credential provider, and `Username` with `ClearTextPassword` in
`nuget.config`. NuGet's guidance in
[Consuming packages from authenticated feeds](https://learn.microsoft.com/nuget/consume-packages/consuming-packages-authenticated-feeds#security-best-practices-for-managing-credentials)
ranks the available forms from most to least secure. The two supported here are that ranking's
first and last:

| NuGet's rank | Mechanism | Supported |
| --- | --- | --- |
| 1, "highly recommended" | Credential provider | Yes — see [Credential providers](#credential-providers). |
| 2 | Encrypted `<Password>` in `nuget.config` | No. `dotnet nuget add source --password` writes this form by default on Windows. |
| 3 | `%VAR%` macros in `nuget.config` | No. Values are taken verbatim, so the placeholder itself is sent as the password. |
| 4 | `NuGetPackageSourceCredentials_<name>` | No. The environment is not consulted for credentials. |
| 5, "only ... where no other secure option is available" | `Username` + `ClearTextPassword` | Yes. |

Rank 5 is supported because many feeds offer nothing else. Only three credential providers exist
in the ecosystem — Azure Artifacts, AWS CodeArtifact, and MyGet, the last of which is Visual
Studio-only. GitHub Packages, GitLab, Artifactory, Nexus, ProGet, Cloudsmith and Artifact
Registry all authenticate with a token in `nuget.config`.

Two forms absent from NuGet's list are also unsupported: a `ClearTextPassword` with no
`Username`, since both halves are required even though Azure DevOps ignores the username, and
userinfo in the source URL (`******host/...`), which is never turned into a header.

A credential in an unsupported form is treated as though no credential were supplied. The form
itself draws no diagnostic; what surfaces is the feed's response to an unauthenticated request,
described under [Reporting an unreadable source](#reporting-an-unreadable-source).

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

Discovery itself is deferred until a feed first answers with an authentication challenge.
Commands and public-feed requests therefore do not scan the convention directory or `PATH`.
Once discovery runs, its result is kept for the process lifetime.

Implementation: [`PluginDiscovery`](../../src/NuGetFetch/Plugins/PluginDiscovery.cs), mirroring
`PluginDiscoverer.cs` and `PluginDiscoveryUtility.cs` in
[NuGet.Client](https://github.com/NuGet/NuGet.Client/tree/dev/src/NuGet.Core/NuGet.Protocol/Plugins).

### The conversation

The plugin is launched as `<plugin> -Plugin` (or `dotnet <plugin.dll> -Plugin`), and both sides
exchange one compact JSON object per line, UTF-8 without a BOM. The sequence is handshake,
`MonitorNuGetProcessExit`, `Initialize`, `GetOperationClaims`, `SetLogLevel`, and then
`GetAuthenticationCredentials`.

Two details are easy to get wrong:

- **The handshake is symmetric.** The plugin sends its *own* handshake request at the same time
  as the host sends one. A host that only waits for a reply, without answering, deadlocks.
  This client requires protocol 2.0.0 because its source-agnostic `GetOperationClaims` request is
  only valid in that protocol version.
  `ReadyRequiresSymmetricHandshake` checks the design interaction;
  `PluginProtocolTests.CompatibleInboundHandshakeUsesProtocolTwo` and
  `PluginProtocolTests.InvalidOrUnsupportedInboundHandshakeReceivesAnErrorResponse` enforce the
  inbound response, while
  `PluginProtocolTests.InvalidOrUnsupportedOutboundHandshakeStopsInitialization` enforces the
  plugin response.
- **`Progress` messages restart the request timer.** They are the plugin saying "still working"
  during a slow sign-in. A host that ignores them times out a request that is progressing fine.
  `ProgressRenewsOnlyItsRequest` checks the design interaction; implementation correspondence is
  unverified. Progress may continue renewing the request indefinitely; the model's request
  liveness guarantee begins once those renewals stop.

The request timeout covers the whole admitted operation: registration, waiting for the serialized
writer, and waiting for a response. If timeout or caller cancellation preempts the request that
owns an in-progress pipe write, the connection terminates before another writer can use that pipe;
`WriterPreemptionIsContained` and `ClosedConnectionIsAbsorbing` check that rule. Terminal pipe loss
closes request admission before the read loop collects and settles pending requests, checked by
`RequestAdmissionHasLiveReceiver` and `ShutdownSettlementIsComplete`. A malformed
plugin-originated request receives an error response or the connection terminates; it never becomes
abandoned work, checked by `InboundFailureIsContained` and
`MalformedInboundEventuallySettles`. Active-writer timeout, cancellation, response, and write-fault
containment are enforced by
`PluginProtocolTests.AStalledWriterTimeoutTerminatesTheConnectionAndSettlesQueuedRequests`,
`PluginProtocolTests.CallerCancellationOfAStalledWriterRemainsCancellation`,
`PluginProtocolTests.AResponseCannotLeaveItsRequestWriterStalled`, and
`PluginProtocolTests.CallerCancellationWinsAConcurrentWriteFailure`. Connection resources remain
alive until admitted requests and inbound response writers have unwound, enforced by
`PluginProtocolTests.ConnectionResourcesWaitForInterruptedRequestsToQuiesce` and
`PluginProtocolTests.ConnectionResourcesWaitForInboundResponseWritersToQuiesce`; if a terminated
transport write cannot be observed completing, its resources remain retained, enforced by
`PluginProtocolTests.AnUnfinishedInterruptedWriteRetainsConnectionResources`. Terminal admission
and pending settlement are enforced by
`PluginProtocolTests.ARequestAfterReceiverLossIsRejectedWithoutWaitingForItsTimeout` and
`PluginProtocolTests.ReceiverLossSettlesARequestAdmittedBeforeThePendingSnapshot`, with the
atomic overlap enforced by
`PluginProtocolTests.AdmissionCannotRegisterDuringTheTerminalPendingSnapshot`. Malformed inbound
Handshake and Log payload handling is enforced by
`PluginProtocolTests.InvalidOrUnsupportedInboundHandshakeReceivesAnErrorResponse` and
`PluginProtocolTests.MalformedInboundLogReceivesAnErrorResponse`.

Implementation: [`PluginConnection`](../../src/NuGetFetch/Plugins/PluginConnection.cs) and
[`PluginCredentialProvider`](../../src/NuGetFetch/Plugins/PluginCredentialProvider.cs).
The concurrent conversation and shutdown rules are checked by the
[NuGet credential-plugin session lifecycle model](models/nuget-plugin-session-lifecycle/README.md).
The model checks the design under finite bounds; implementation correspondence for progress
renewal and concurrent correlation remains unverified. Writer preemption, terminal admission, and
pending settlement are enforced by the gates named above.

Plugins are started lazily and kept until their connection closes, because a launch costs a process
start plus five round trips. A request that loses a race with terminal publication retries once on
a replacement connection, and later requests likewise replace a cached terminal connection. These
rules are enforced by
`PluginProtocolTests.ARequestRacingTerminalPublicationRetriesOnAReplacementConnection` and
`PluginProtocolTests.AClosedCachedPluginConnectionIsRestartedOnTheNextRequest`. A plugin that fails
to start, or that does not claim the `Authentication` operation, is remembered as unusable rather
than retried.

A plugin process or pipe that dies during a request is likewise treated as no credential from
that plugin. Timeouts, malformed responses, I/O failures, disposed pipes, and invalid process
state are contained at the request boundary so another provider can answer or the feed's 401 can
surface normally. Caller cancellation is not a plugin fault and continues to propagate, enforced
before and after receiver loss by `PluginProtocolTests.CallerCancellationContinuesToPropagate` and
`PluginProtocolTests.CanceledRequestAfterReceiverLossRemainsCancellation`, including the
admission-monitor race checked by
`PluginProtocolTests.CancellationWhileWaitingForClosedAdmissionRemainsCancellation`.
Cancellation while a terminal cached connection is being replaced likewise propagates, enforced by
`PluginProtocolTests.CancellationWhileReplacingAClosedConnectionRemainsCancellation`.

### Unattended by default

Credentials are requested with `IsNonInteractive` set and `CanShowDialog` clear, matching
`dotnet restore` without `--interactive`. A tool that may run in CI must not block on a sign-in
prompt. Cached credentials and tokens supplied through the environment still work; only
interactive sign-in is withheld.

### How credentials arrive

Everything above describes the channel. This describes who speaks on it, because the answer
differs by environment, and "a credential provider supplies it" is not specific enough to act on.

The Azure Artifacts provider resolves in two levels. The outer level chooses a *credential
provider*, consulting the environment first and the hostname last:

| Order | Provider | Selected when |
| --- | --- | --- |
| 1 | `VstsBuildTaskServiceEndpointCredentialProvider` | `ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS` is set |
| 2 | `VstsBuildTaskCredentialProvider` | `ARTIFACTS_CREDENTIALPROVIDER_URI_PREFIXES` and `..._ACCESSTOKEN` are set |
| 3 | `VstsCredentialProvider` | the host is a well-known Azure DevOps hostname |

Only when the first two decline does the third run, and only then does MSAL enter the picture at
all. Its inner chain of bearer token providers is tried in this order:

`MSAL Service Principal` → `MSAL Managed Identity` → `MSAL Silent` → `MSAL Broker Interactive` →
`MSAL Interactive` → `MSAL Device Code`

Each inner provider logs itself as skipped when its configuration is absent, so `-V Debug` shows
which link answered without needing source access.

`VstsCredentialProvider` also issues its own unauthenticated request and reads the Entra authority
out of the **401 response headers**. It is 401-driven internally, independently of
[our handler](#preemptive-credentials-versus-401-driven-credentials).

The flows, distinguished by what supplies the secret:

| Flow | Driven by | Unattended |
| --- | --- | --- |
| `nuget.config` credential | the config file; no provider runs at all | yes |
| Pipeline build identity | `ARTIFACTS_CREDENTIALPROVIDER_URI_PREFIXES` and `..._ACCESSTOKEN`, set by `NuGetAuthenticate@1` | yes |
| External feed endpoints | `ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS`: endpoint, username, password | yes |
| Service principal and certificate | `ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS`: `clientId` plus `clientCertificateSubjectName` or `clientCertificateFilePath` | yes |
| Silent | the MSAL token cache, populated by an earlier sign-in | only if warm |
| Broker interactive, interactive, device code | a human | no |

`MSAL Managed Identity` sits in the chain between the service principal and silent providers, but
the provider's README documents no configuration for it, so it is deliberately absent from the
table above rather than guessed at.

Three consequences are worth stating plainly:

- **In an Azure DevOps pipeline there is no secret to configure.** `NuGetAuthenticate@1` installs
  the provider onto the agent for that run and points it at the build service identity, scoped by
  **URL prefix** — a semicolon-separated host list, not a feed list. A feed outside those prefixes
  falls through to the next provider. The two build-provider variables can be exported by hand and
  the provider honors them, but the provider's own documentation describes them only as the task's
  mechanism; `ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS` is what it directs unattended
  callers outside Azure DevOps Pipelines to use.
- **`az login` does not help.** There is no Azure CLI credential provider, and the two token
  caches are unrelated. A token from `az account get-access-token` has to be handed over
  explicitly, either through the build-provider variables or as a `ClearTextPassword`.
- **The certificate flow is the one that generalises.** It is configured once through an
  environment variable, needs no interactive session, and every NuGet-aware tool on the machine
  reads it from the same place. That is the mechanism for sharing one short-lived credential
  across build tools, rather than teaching each tool separately.

### The broker on Linux

The MSAL paths of the v2.0.2 Azure Artifacts tool package do not work on a current Ubuntu, for two
independent reasons that surface in that order:

1. The package ships `runtimes/linux-x64/native/libmsalruntime.so`, but nothing places it beside
   the entry assembly, so the load fails on a path that does not exist:
   `.../tools/net8.0/any/libmsalruntime: cannot open shared object file`.
2. Pointing `LD_LIBRARY_PATH` at that `runtimes/linux-x64/native` directory gets past the first
   failure, and the load then fails on `libwebkit2gtk-4.0.so.37`. Ubuntu 24.04 ships only
   `libwebkit2gtk-4.1-0`; the 4.0 ABI is gone, and only a transitional documentation package
   still carries the old name.

The consequence is worse than "interactive sign-in is unavailable", which would be unremarkable on
a headless machine. The broker is initialised *before* `MSAL Silent` is attempted, so a GUI
dependency removes the one MSAL path that is meant to be headless-safe, and the provider gives up
having tried nothing that could have succeeded.

Every unattended flow in the table above except `MSAL Silent` is unaffected, because none of them
constructs an MSAL public client. Supplying a token through the build-provider variables sidesteps
the area entirely.

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

That paragraph describes the current shared handler. The target below retains
the response-aware handler loop but binds each handler to one configured-source
context. A request outside a source-bound pipeline is intentionally
plugin-ineligible rather than inheriting authority from its URL.

Behaviour follows NuGet's:

| Aspect | Behaviour |
| --- | --- |
| Trigger | 401 always; 403 only when explicitly enabled, since 403 usually means "authenticated but not permitted" |
| Retry bound | 4 attempts per request, matching `AmbientAuthenticationState.MaxAuthRetries` |
| `IsRetry` | Clear on the first ask, set afterwards, so a plugin replaces a cached token the feed has already rejected |
| Scope | The current handler caches by request-target network scope; this does not isolate distinct configured sources on one origin |
| Concurrency | Concurrent requests in one current cache scope acquire credentials once |
| Precedence | A credential already on the request is never overwritten |

### Source-scoped plugin authentication context

This section's sole normative owner is **NuGet feed authentication**. Its claim
is that plugin credential state belongs to one configured source authority,
not to every request target sharing that authority's network scope. The target
design replaces request-target cache identity with one
**plugin-authentication context** per configured NuGet V3 source authority.
This is an authentication owner concept, not another package source or
producer identity. The context binds plugin credential state to the configured
authority that was allowed to ask for it.

This contract consumes, without redefining:

- the package-source model's canonical configured-source identity and alias
  decision;
- the browser-package-source design's caller-created
  `PackageSourceAssociation` reference for that configured authority;
- the configured-authority owner's provider-query URI for that canonical
  source;
- V3 source clients' already-resolved service-index and resource targets; and
- the built-in NuGet Gallery's credential-free transport boundary.

It returns an opaque context reference and a target-authorization decision to
the source-owned authentication pipeline. The caller supplies the same
`PackageSourceAssociation` only for aliases of one canonical configured
authority and distinct references for distinct authorities. Authentication
binds one context to that association and never reconstructs source identity
from the request target.

The reference contains no credential and does not expose source text. The
resolved configured-authority owner owns the context and its lifetime;
pipelines receive non-owning references. Disposing one pipeline disposes its
transport but does not retire the shared context, clear its credential state,
or cancel context-owned provider work that another pipeline joined. Changing
or releasing the configured authority retires the old context and requires a
new one. Retirement immediately denies new credential use or acquisition and
prevents an in-flight completion from publishing. Credential state remains
process-local and is released after its authority owner retires the context,
not when an individual authentication pipeline is disposed.

#### Anonymous is a context state, not a source classification

A configurable V3 source is not declared public or private. Its context starts
without a plugin credential. An associated request sends anonymously while
that context is empty. If the response does not challenge, the context stays
empty and the credential provider is never consulted.

An authorized 401 response, or an authorized 403 under the existing opt-in,
may start acquisition for that context. The challenge may come from the
service index or from an authorized resource it advertised; the service index
need not challenge first. A successful acquisition belongs only to that live
context. A null or failed acquisition leaves the context empty and returns the
challenge under the existing failure contract.

Once populated, the context may attach its cached credential preemptively to a
later associated and resource-authorized request. It never supplies that
credential to a request associated with another context. Consequently, a
private source and an anonymous source can share a network origin without the
anonymous source receiving the private source's credential.

An explicit `Authorization` header remains higher precedence. It is sent under
the configured-credential policy and neither reads nor seeds the plugin
context. A challenge to that request remains visible rather than silently
switching credential mechanisms.

#### Association, provider query, and resource authorization are separate

Plugin participation requires two independent owner facts:

1. the caller-issued source association bound to the authentication context;
   and
2. the authentication owner's result that the concrete target is inside that
   context's credential-resource scope.

Association answers *which source could own a credential*. Resource
authorization answers *whether this target may ask for or receive it*. Sharing
a network origin or resource scope does not create context association.
Feed-advertised metadata cannot mint or replace the context reference.

The configured-authority owner supplies one provider-query URI when it creates
the context. That URI retains the exact configured service-index spelling
selected by the owner's alias decision, including raw path and query spelling,
solely for `GetAuthenticationCredentials`. Parsing the URI to establish
resource scope must not replace that provider-query spelling. Every authorized
challenge for the context queries the plugin with that exact URI, including
when an advertised resource or redirect target supplies the first challenge.
The concrete challenge target never becomes provider lookup identity and
feed-advertised metadata cannot replace it. Provider-query identity does not
authorize a target. NuGet.Client's JSON URI serialization likewise emits the
URI's original string rather than its normalized presentation.

The configured service-index endpoint establishes the context's
credential-resource scope. For ordinary hosts the scope is the endpoint's URI
scheme, canonical IDN host, and effective port. For
`pkgs.dev.azure.com`, the first non-empty path segment, the Azure organization,
also participates. Deeper project and feed path segments do not participate,
so name-to-GUID endpoint aliases in one organization remain authorized while a
resource under another organization is rejected.

This rule assumes Azure preserves the organization segment's spelling between
the configured service index and its advertised resources. A changed spelling,
including a name-to-ID change in that first segment, fails closed rather than
guessing equivalence. When the live Azure feed sensor named below runs, it must
exercise the configured index and an advertised package resource so a service
change makes that assumption visible.

Scheme and host comparisons are case-insensitive. Default and explicit ports
compare by their effective value. Path, query, fragment, user information, and
display text do not participate except for the Azure organization segment
above. Failure to derive either scope is not authorization.

Authentication accepts a context reference and that source client's isolated
credential-free inner transport, whose owning contract disables automatic
desktop redirects, and returns a source-bound authentication handler. It does
not accept a shared or opaque caller handler. Its composition precondition is
that the package source owner's redirect orchestration wraps the returned
handler, so every redirect clone re-enters authentication. This is the required
handoff between the owners; authentication does not redefine redirect-target
admission, source-client factory, or transport-disposal behavior. Requests
formed by that client therefore enter an already-associated pipeline; source
association is not a string-valued request option that feed data can influence.
The handler injects plugin authorization only after the authentication owner
authorizes the concrete target.

Several V3 pipelines constructed with the same `PackageSourceAssociation`
share the one authentication context bound to that association. Pipeline or
handler identity does not create another credential authority. Pipelines
constructed with distinct associations remain isolated even when their
resource scopes are equal.

Each service-index, feed-advertised, and redirect target must independently
pass resource authorization. Source-owned redirect orchestration does not copy
a plugin-produced header; the clone re-enters the source-bound handler, which
may attach that context's credential only after authorizing the new target.
Retry and redirect clones preserve the handler-bound context and an existing
rejection; they cannot replace either, turn rejection into authorization, or
use an intermediate redirect hop as the comparison anchor.

An unassociated request, explicitly plugin-ineligible request, retired context,
or out-of-scope target bypasses plugin cache lookup, acquisition, and replay.
It is sent once without plugin authorization so the response remains visible.
Failing closed is required because the handler cannot invent configured-source
authority from a request URL.

#### Gallery is not an authentication context

The built-in NuGet Gallery client is a separate source implementation over
fixed public search, registration, CDN, package, and symbol capabilities. Its
factory creates an isolated credential-free transport. It creates no plugin
context, composes no source authentication handler, and cannot reach plugin
cache, acquisition, or replay. NuGet.org service topology is therefore neither
a positive nor a negative example for plugin resource authorization.

A user-configured V3 endpoint is different even when its host belongs to
NuGet.org: it follows the configurable-source rules above and does not acquire
the built-in Gallery's credential-free identity merely from its hostname.

Credential-provider contexts are desktop-only. Browser/Wasm callers cannot
create a `PluginAuthenticationContextOwner`; creation fails visibly with
`PlatformNotSupportedException`, and V3 factory composition independently
rejects a context on that platform. Browser sources continue to use the
explicit session PAT contract owned by the browser-package-source design.

#### Concurrency and refresh

Acquisition is single-flight per context. Concurrent authorized challenges for
one context consume a credential published while they waited before asking the
provider again. Different contexts are independent and may acquire
concurrently even when their resource scopes are equal.

Refresh after a rejected cached credential remains single-flight within the
same context. Concurrent requests may reject the same observed credential
version, but only one provider acquisition runs. A waiter consumes a newer
credential published while it waited before deciding whether another
acquisition is needed. Same-context provider acquisitions therefore do not
overlap and cannot complete out of order.

Only a completion authorized by the current live context state may publish.
Retirement or replacement makes an in-progress completion stale; stale work
cannot populate, clear, or replay from the context.

The existing retry bound, `IsRetry` behavior, provider ordering, cancellation,
and challenge reporting do not change.

#### Required implementation gates

The target is unverified until Release gates establish:

- `AnonymousSourceSharingOriginNeverReceivesPrivateSourceCredential`: a
  private source populates its context, then a distinct anonymous source on
  the same origin succeeds without receiving authorization or consulting the
  provider;
- `AuthorizedResourceReusesItsSourceContextCredential`: a source challenge
  populates one context and its associated resource reuses that credential;
- `ResourceFirstChallengeUsesConfiguredProviderQuery`: an anonymous service
  index advertises an authorized resource that supplies the first challenge;
  a provider that answers only for the configured service-index URI is queried
  with that URI, and the resulting credential is replayed only to the
  authorized resource;
- `CredentialRequestPreservesOriginalSourceSpelling`: raw-distinct configured
  service-index spellings remain distinct in plugin protocol requests rather
  than collapsing through parsed-URI presentation;
- `SharedAssociationPipelinesShareAuthenticationContext`: two V3 pipelines
  constructed with the same `PackageSourceAssociation` share credential
  publication and coalesce concurrent challenges into one provider
  acquisition;
- `SharedContextSurvivesIndividualPipelineDisposal`: with two pipelines
  carrying one association, disposing either pipeline neither clears a cached
  credential used by the survivor nor retires or cancels context-owned
  provider work joined by the survivor;
- `CrossContextResourceCannotReadAcquireOrReplayCredential`: equal resource
  scope does not permit a request carrying another or no context to consume
  the credential;
- `OutOfScopeResourceCannotReadAcquireOrReplayCredential`: a foreign resource
  challenge remains visible without plugin participation;
- `OrdinaryResourceScopeUsesCanonicalOrigin`: hermetic associated-request
  vectors authorize scheme and canonical IDN host case variants, Unicode and
  punycode host equivalents, implicit and explicit default ports, and changes
  only to path, query, fragment, or user information; different schemes,
  canonical hosts, or effective ports and any derivation failure cannot read
  cache state, invoke the provider, or replay authorization;
- `AzureResourceScopeIncludesOrganizationButAllowsNameGuidAliases`: name and
  GUID paths inside one organization are authorized while another
  organization is rejected;
- `ConcurrentAcquisitionIsSingleFlightPerContextAndIndependentAcrossContexts`:
  one context coalesces acquisition without serializing another;
- `RetiredContextRejectsLateCredentialPublication`: retirement during
  acquisition cannot publish or replay the result;
- `RetiredContextRejectsPendingChallengeJoinAndLaterRequest`: retirement after
  an authorized challenge but before resolution, including while another
  request has active provider work, makes the challenged waiter surface its
  response without joining or waiting on that work; a later request through
  the retired context cannot read cache state, start or join provider work, or
  replay authorization;
- `ConcurrentRejectedCredentialRefreshesPublishOneNewVersion`: two requests
  that reject one cached version produce one provider acquisition, one newer
  published version, and waiter replay from that newer version without a stale
  overwrite or clear;
- `ExplicitAuthorizationBypassesPluginContext`: an explicit configured
  `Authorization` header neither reads nor populates the plugin context, never
  invokes the provider, and leaves a resulting challenge visible;
- `RequestClonePropagationPreservesContextAndRejection`: the source-owned
  redirect layer wraps the authentication handler so every clone re-enters
  target authorization, preserves its association and any resource rejection,
  and carries no plugin-produced header unless the new target is independently
  authorized; an Azure cross-organization redirect cannot read cache state,
  invoke the provider, or replay authorization;
- `AuthenticationContextReferenceIsOpaque`: the public context reference
  exposes no credential, configured-source text, serialization value, or
  display value; and
- `NuGetGalleryTransportCannotReachPluginAuthentication`: the built-in Gallery
  transport has no plugin handler or context path.

`LiveAzureResourcePreservesConfiguredOrganizationSegment` is an optional,
non-gating service-drift sensor. When an authenticated live feed is available,
it confirms that Azure keeps the configured organization segment on an
advertised package resource. The hermetic
`AzureResourceScopeIncludesOrganizationButAllowsNameGuidAliases` Release gate,
not this environment-dependent sensor, establishes the target behavior.

The
[source-authentication context models](models/nuget-source-authentication-context/README.md)
are two focused TLA+ modules. The context model checks context isolation,
target authorization, authorized acquisition and publication, single-flight
acquisition within a context and independence across contexts, exogenous
retirement, Gallery and excluded-request non-participation, and
admitted-request progress. It consumes the association-to-context mapping as
an input; `SharedAssociationPipelinesShareAuthenticationContext` is the
required implementation gate for that mapping. It also omits provider-query
identity, consumes already derived resource scopes, and has no pipeline
lifetime; the resource-first challenge, ordinary-scope, and
individual-pipeline-disposal gates named above establish those implementation
boundaries. The refresh model checks one bounded rejected-version refresh
episode: single-flight refresh, joining an in-flight refresh, superseded
requests consuming the newer version instead of acquiring, read-only
consumption, and monotonic publication. Those checks establish the design
interaction, not implementation correspondence.

The context bound contains two distinct configurable contexts sharing one
resource scope, one foreign scope, and nine requests covering concurrent
challenges, later cache use, unassociated/ineligible/foreign targets, and
Gallery. Retirement is enabled for any live context in any state. TLC explored
6,794,613 generated and 1,485,245 distinct states to depth 29 without an
invariant or liveness violation. Separate reachability configurations exhibit
pre-acquisition retirement, retirement during active provider work,
populated-context retirement followed by a later request, simultaneous
equal-scope acquisitions, source isolation, and excluded/Gallery
non-participation. Mutations that removed the live-context gate, selected
credentials by resource scope, or published after retirement violated
`AllRetiredParticipationViolationsNotObserved`,
`PostRetirementRequestCannotUsePlugin`, `CacheReadsStayContextBound`, and
`PublicationIsAuthorized`.

The refresh bound contains one live context, two requests, one initial cached
version, and two distinct possible provider results. Sending, rejection, and
provider progress interleave freely inside one episode. TLC explored 91
generated and 65 distinct states to depth 11 without a violation.
Reachability configurations exhibit an in-flight join followed by follower
consumption, a rejection arriving after publication, and a request that first
observes the published version being accepted outside the episode. Mutations
that removed single-flight admission, admitted superseded requests to provider
work, let consumption write back an older observation, or published a
candidate verbatim violated `AtMostOneProviderAcquisition`,
`AtMostOneProviderCompletion`, `StaleObservedRequestCannotAcquire`,
`StaleObservedConsumptionIsReadOnly`, and `CredentialVersionNeverRegresses`.

Redirect mechanics, target-scope derivation, provider failure and
cancellation, HTTP retry bounds, plugin protocol, more than one refresh
episode, and implementation correspondence remain outside both models.

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
ambient-scope shape already used by `NetworkTelemetry`: a scope is opened at each command
boundary that turns those nullable results into an operator-facing answer. Package acquisition
opens one around each acquisition hop; direct `--version`, `--latest-version`, and `--versions`
queries open one around the complete query. Nested async work records into the same collector,
and the "nothing resolved" path consults it before choosing a message.

The scope is opened per *hop*, inside the tool-wrapper redirect loop, rather than once around
the whole traversal. Each hop resolves a different package id, so a shared collector would let
a refusal recorded while fetching the wrapper explain the redirect target going missing — and
the recorded URL carries the wrapper's id in its flat-container path, so the message would name
a source and a package that had nothing to do with the failure.

Two further rules keep the message honest:

- **404 is never recorded as a source failure.** A 404 from package/version enumeration means the
  package is absent, so a real miss still reports *not found*. A 404 from ancillary listing
  metadata is instead carried by that operation's typed incomplete-metadata result; range
  resolution fails closed without falsely declaring the package absent.
- **A recorded failure is advisory, not fatal.** The collector is only consulted when the
  overall lookup produced nothing, so if one source 401s and another answers, the successful
  result stands. That is this codebase's answer to the third open design question in #3417.

The phase (`reading the service index`, `listing versions`) is taken from the ambient
`NetworkTrafficKind`, which the network telemetry scope already tracks, so command boundaries do
not duplicate the phase labels.

### The URL is redacted before it is stored

This message prints a source URL, and some feeds put a credential in one. The URL is passed
through `NetworkRequestObservation.RedactSensitiveUrlText` on the way *into* the collector
rather than on the way out to the console — `FeedFailureCollector.Failures` is public, so an
unredacted URL sitting in it would already be an exposure.

```console
  https://pkgs.dev.azure.com/<org>/<project>/_packaging/<feed>/nuget/v3/index.json?access_token=REDACTED — HTTP 401 Unauthorized while reading the service index
```

Query names are matched on fragments (`token`, `key`, `secret`, `password`, `credential`,
`auth`, `sig`) rather than against exact names, because the same credential travels as
`access_token`, `accessToken`, `apiKey` or `x-api-key` depending on the feed. MyGet also issues
service index URLs shaped like `https://host/F/<feed>/auth/<token>/api/v3/index.json`, so the
segment following an `auth` segment is redacted as well.

Nothing else in the path is. On Azure DevOps the organization, project and feed name are all
path segments, so collapsing the path would leave a message that cannot say *which* source
refused — its one job.

## Rejecting credentials embedded in a source URL

NuGet does not support `https://<user>:<password>@host/...`, so a source URL carrying userinfo
is rejected rather than attempted. The check lives on `PackageSource`'s constructor, not at the
option parser, because a source arrives by four routes — `--source`, `--add-source`, an explicit
`--nugetconfig`, and a `nuget.config` discovered by walking up from the working directory — and
a validator attached to the two command-line ones silently misses the other two. Construction is
the single point every source passes through whatever route it took.

`SourceResolver` exposes both halves, so a caller chooses whether a bad source is an exception:

```csharp
if (!SourceResolver.IsSupportedSource(url, out InertString? problem)) { /* report it */ }
UnsupportedSourceException.ThrowIfUnsupported(url);   // the ArgumentNullException.ThrowIfNull shape
```

The message names the source but never quotes the credential — it prints the URL with userinfo
removed, so the operator can still tell *which* source was rejected.

### Untrusted URL text is made inert before it is printed

A source URL is untrusted input, and a message that quotes one hands attacker-controlled text to
a terminal. `Uri` percent-encodes C0 controls, so an ANSI escape cannot survive a round trip
through it — but it passes `Cf` straight through, and `Cf` is where Trojan Source (CVE-2021-42574)
lives. A right-to-left override in a feed name reorders the rest of the line.

Every URL that reaches a message or a log is therefore spelled by `InertString` first:

```csharp
InertString.Format(TextPolicy.Field, $"Source URL '{withoutCredentials}' embeds ...")
```

The mechanism — the policy/speller split, the audit boundary around the decoder, and how
composition preserves the guarantee — is described in
[the InertText design note](inert-text.md). Only what is specific to this path is recorded here.

**Which policy, and why this one.** `TextPolicy.Field` is deny-shaped: it refuses `Cc`, `Cf`,
`Cs`, `Zl` and `Zp`. That is what the message path needs today, and it is deliberately the weaker
choice. A URL host is a constrained grammar, and the central typosquatting vector is a homoglyph —
Cyrillic `а` and Latin `a` are the same glyph, both category `Ll`, and neither is a hazard. No
category rule catches that and none should; only an allow list does. Tightening the source URL to
an allow-shaped policy is tracked with the identifier work.

**What the retyping caught here.** Redaction returns `InertString` rather than `string`, so the
distinction between a redacted URL and a raw one is visible to the compiler:

```csharp
internal static InertString RedactSensitiveUrlText(string value)   // was string
public readonly record struct FeedFailure(InertString Url, ...)    // was string Url
public InertString? DescribeFailure(string packageName)            // was string?
```

That change found a live defect rather than merely documenting an intent.
`FeedFailureCollector.DescribeFailure` built its message with ordinary interpolation, which passed
the *package name* through untouched — and a package name arrives from a command line or a
dependency graph. Retyping the return value turned that into a build error at both call sites
instead of a review finding.

**What is still transactional.** The `Action<string>? log` delegate threaded through the package
layer, where roughly a dozen sites interpolate a raw URL. Retyping it would make the compiler
enumerate them the same way, and is tracked separately: it touches 27 files and does not belong in
a fix for feed failure reporting.

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

Where a credential provider is available, the current shared handler covers
this case because it sees the anonymous service-index request's 401. The target
source-scoped handler preserves that coverage only for requests made through
the associated V3 source pipeline; an unassociated call site cannot acquire
plugin authority. Neither shape fixes the `nuget.config` path, which still
depends on the caller threading an explicit credential through.

The legacy shared handler caches acquired credentials by request-target origin
so the service index and discovered package endpoints share one challenge
response. Azure Artifacts adds the first path segment, which is the
organization. This separates Azure organizations, but neither rule separates
distinct configured sources inside one cache scope.

The
[source-scoped context](#source-scoped-plugin-authentication-context) is
implemented for owner-composed V3 source pipelines through
`PackageSourceClientFactory.CreateWithPluginAuthentication`. Package-source
composition issue #5603 is the named consumer that will replace the legacy
shared-handler path; until then, that path does not claim source-scoped
isolation.

When an automatic redirect ends in an authentication challenge, credential acquisition remains
scoped to the caller-selected source URI and the retry starts again from that URI. A redirect
target cannot choose the plugin query, credential cache slot, replay destination, or successful
response body. This also matches feed authentication behavior: once the original source receives
valid credentials, it can serve the requested resource instead of redirecting to sign-in.

## Tests

Two tiers, in `tests/NuGetFetch.Tests`:

- **Hermetic**, no network and no real plugin binary, runs in PR CI:
  - `CredentialMechanismTests` pins every row of the ranking table above.
  - `ServiceIndexAuthenticationTests` pins which of the two requests carries the credential, and
    that a 401 stays distinguishable from a 404.
  - `PluginDiscoveryTests` pins all three discovery routes and their precedence, over a temporary
    directory tree and `PATH`.
  - `PluginAuthenticationHandlerTests` pins the current 401 loop: retry bound,
    `IsRetry` progression, request-target cache scoping for ordinary hosts,
    organization scoping and GUID-alias reuse for Azure Artifacts, redirect
    isolation from credential scope and returned content, 403 opt-in, and that
    an existing credential is not overwritten. It establishes only the legacy
    shared-handler behavior.
  - `PluginAuthenticationContextTests` pins every required source-scoped
    context gate above. Pipeline mapping, disposal, resource-first challenge,
    and redirect composition run through owner-composed V3 clients; focused
    state, scope, refresh, and retirement vectors use hermetic handler
    transports.
  - `PluginProtocolTests` runs a **real plugin process** — a cross-platform managed fixture that
    genuinely speaks the line protocol — so framing, the symmetric handshake, process death,
    selected shutdown behavior, and caller-cancellation classification are exercised end to end
    rather than mocked. The suite runs on Windows and Unix; only executable-bit discovery coverage
    remains Unix-specific.
    Concurrent request correlation, `Progress`-driven timeout extension, and pipe-loss admission
    remain unverified at the implementation boundary.
- **Live**, tagged `[Trait("Network", "Live")]` and skipped unless a feed and token are supplied.
  `AzureDevOpsFeedTests` covers the config path; `AzureDevOpsCredentialProviderTests` covers the
  provider path against a genuinely installed provider. Only a real Azure DevOps feed exercises
  an authenticated service index.

CI runs the offline tier only:

```bash
dotnet run --project tests/NuGetFetch.Tests -c Release -- --filter-not-trait "Network=Live"
```

The live tier needs a private feed, which CI and fork PRs do not have. To run it locally, mint a
token rather than storing one — the feed accepts an Entra access token exactly as it accepts a
PAT:

```bash
export DOTNET_INSPECT_TEST_AZDO_FEED=https://pkgs.dev.azure.com/ORG/PROJECT/_packaging/FEED/nuget/v3/index.json
export DOTNET_INSPECT_TEST_AZDO_TOKEN=$(az account get-access-token \
  --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv)
dotnet run --project tests/NuGetFetch.Tests -c Release -- --filter-trait "Network=Live"
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
