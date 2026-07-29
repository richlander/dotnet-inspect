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

## The one supported credential source

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
| 1, "highly recommended" | Credential provider | **No** — no plugin is invoked, and no `ARTIFACTS_CREDENTIALPROVIDER_*` or `VSS_NUGET_*` variable is read. |
| 2 | Encrypted `<Password>` in `nuget.config` | **No** — only `ClearTextPassword` is parsed. `dotnet nuget add source --password` writes this form by default on Windows. |
| 3 | `%VAR%` macros in `nuget.config` | **No** — values are taken verbatim, so the placeholder is sent as the password. |
| 4 | `NuGetPackageSourceCredentials_<name>` | **No** — the environment is never consulted for credentials. |
| 5, "only ... where no other secure option is available" | `Username` + `ClearTextPassword` | **Yes — the only one.** |

The tool therefore supports exactly the mechanism the documentation ranks last and attaches a
leak warning to, and ignores all four that are preferred. The top rank is the costly one: a
credential provider is what a correctly configured CI pipeline uses, so such a pipeline still
reaches a private feed unauthenticated.

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

The defect is therefore not that rank 5 is supported. It is that rank 5 is supported *exclusively*
while the four preferred mechanisms are dropped in silence, and that a dropped credential cannot
be told apart from a missing package. Supporting a credential provider is a real improvement for
Azure Artifacts and AWS; it does nothing for GitHub Packages, where only better diagnosis helps.

Two mechanisms that are not on NuGet's list are also worth stating, because both look plausible
and neither works: a `ClearTextPassword` with no `Username` (both halves are required, even
though Azure DevOps ignores the username), and userinfo in the source URL
(`https://user:pass@host/...`, which is never turned into a header).

Every one of these is dropped without a diagnostic. Because an unauthenticated feed currently
reports as *package not found* rather than as an authentication failure (issue #3417, bug 1),
each is indistinguishable from a typo in the package name. That is what makes the silence
expensive, and it is the argument for fixing the diagnosis before adding mechanisms.

## Preemptive credentials versus 401-driven credentials

The official client is 401-driven: it "will make an unauthenticated request, and if the server
responds with an HTTP 401 response, NuGet will search for credentials" — env var, then
`nuget.config`, then credential provider. This tool instead attaches whatever credential it
parsed up front and treats a 401 as terminal. It never re-requests, and never widens its search
after a challenge.

That difference is why adding a mechanism is not simply a parser change: the credential provider
contract expects a caller that can detect a 401 and re-invoke with `-IsRetry`, which is
[documented as required](https://github.com/microsoft/artifacts-credprovider) to avoid reusing
invalid cached credentials. Bug 1 is a prerequisite for doing this properly.

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

## Tests

Two tiers, in `src/NuGetFetch.Tests`:

- **Hermetic**, in-memory transport, runs in PR CI. `CredentialMechanismTests` pins every row of
  the table above. `ServiceIndexAuthenticationTests` pins which of the two requests carries the
  credential, and that a 401 stays distinguishable from a 404.
- **Live**, `AzureDevOpsFeedTests`, tagged `[Trait("Network", "Live")]` and skipped unless a feed
  and token are supplied. Only a real Azure DevOps feed exercises an authenticated service index.

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
