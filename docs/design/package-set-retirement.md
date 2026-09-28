# Package set retirement and small Ecosystem cores

## Status

Proposed composition map. The product owner requested this cross-owner change
on 2026-09-27: curated package sets are stale data that defeat the intended
dynamism of Ecosystems, and discovery beyond a small authored core belongs to
package-prefix query. This document owns only the composition and its slice
order; each slice amends its owning design together with its code.

## Claim

> An Ecosystem contributes a small authored core of specific packages or
> Libraries (guidance: zero to six; hard cap: twelve) and optional
> package-prefix populations. Package sets no longer exist. Discovery beyond
> the core uses the Ecosystem's recorded prefixes, and only in scenarios whose
> own contract admits a prefix population.

Traversal from roots is the existing
[core-package contract](ecosystem-packs.md#core-package-priorities): a bounded
operation that selects an Ecosystem may follow ordinary package dependencies
from its roots. This map does not add or change traversal.

## Why

- **Stale by construction.** Each shipped set is a dated audit (major-10
  Extensions and ASP.NET Core ids; the Aspire 13 line), frozen in source and
  pinned twice in tests. New packages, new major lines, and deprecations do
  not reach users until someone re-audits.
- **Defeats dynamism.** Every set member already matches its pack's recorded
  prefix, so the set duplicates what prefix query can discover.
- **Outside the cost envelope.** Sets hold 44, 53, and 82 packages. The
  [Find Workspace scope evidence](https://github.com/richlander/dotnet-inspect/pull/8750)
  measured 22.3 s and 157 MB cold for the Extensions and ASP.NET Core sets
  together, against about 1.1 s and 7 MB cold for the five core packages
  those two Ecosystems registered before this change.

## The Ecosystem model after retirement

| Contribution | Meaning | Bound |
| --- | --- | --- |
| Core packages and Libraries | Specific, authored roots a bounded operation may realize | 0-6 by guidance; at most 12, enforced by pack registration |
| Package-prefix populations | Recorded literal prefixes that discover more packages | Optional; executed only by operations whose contract admits prefix populations |
| Platform populations | Platform families, where the Ecosystem has one | Unchanged until the partition slice |
| Namespace roots | Descriptive namespace subtrees | Unchanged except AI's vector-data root and Azure's deletion |
| Tools, demos, scanner | Unchanged | Unchanged |

Core entries are roots, not an inventory. They are the entry points from
which a bounded operation's call-graph and package-dependency traversal
discovers related packages, so the core does not list what traversal can
reach. A prefix covers a package-ID family the Ecosystem owns and reaches the
members traversal from the roots would not, such as sibling provider and
evaluation packages.

The Ecosystem set changes:

- **Microsoft.Extensions becomes prefix-only.** Its three roots
  (`Microsoft.Extensions.DependencyInjection.Abstractions`,
  `Microsoft.Extensions.Configuration.Abstractions`, and
  `Microsoft.Extensions.Logging.Abstractions`) are removed. The family fans
  out too quickly for a small core to represent it: about 117 packages, of
  which about 60 are outside the shared frameworks. The classic stack
  (dependency injection, configuration, logging, options, hosting, and the
  HTTP client factory) ships in the ASP.NET Core shared framework, which the
  platform Workspace already contains, so the removed roots duplicated it.
  The remainder, such as `Microsoft.Extensions.Http.Resilience` and
  `Microsoft.Extensions.Caching.Hybrid`, is reached through the
  `Microsoft.Extensions.` prefix. An empty core remains a valid Workspace
  declaration because the prefix population and namespace root contribute,
  and supply-chain baseline classification is unchanged because the prefix
  matches every removed root.

- **Azure is removed.** `ecosystem.azure` leaves the pack registry and the
  all-known Workspace plan, with its `Azure.`, `Microsoft.Azure.`,
  `Microsoft.Extensions.Azure`, `Aspire.Azure.`, and `Aspire.Hosting.Azure.`
  prefixes.
- **AI keeps one prefix and seven roots.** Its prefix population becomes
  `Microsoft.Extensions.AI` alone, which reaches the Microsoft.Extensions.AI
  family (provider bridges such as `Microsoft.Extensions.AI.OpenAI` and the
  `Microsoft.Extensions.AI.Evaluation` packages); the
  `Microsoft.Extensions.VectorData`, `Microsoft.Agents.AI`, and
  `ModelContextProtocol` prefixes are removed. Its core becomes, in
  preference order, seven roots observed on 2026-09-27:

  | Package | Version | Publisher repository |
  | --- | --- | --- |
  | `Microsoft.Extensions.AI` | 10.10.0 | `dotnet/extensions` |
  | `Microsoft.Extensions.AI.Abstractions` | 10.10.1 | `dotnet/extensions` |
  | `OpenAI` | 2.14.0 | `openai/openai-dotnet` |
  | `Anthropic` | 12.50.0 | `anthropics/anthropic-sdk-csharp` |
  | `Google.GenAI` | 1.22.0 | `googleapis/dotnet-genai` |
  | `ModelContextProtocol` | 2.2.0 | `modelcontextprotocol/csharp-sdk` |
  | `Microsoft.Agents.AI` | 1.22.0 | `microsoft/agent-framework` |

  The AI Ecosystem's scenario is `IChatClient`-style model consumption.
  Vector stores are a separate scenario and are not mixed in:
  `Microsoft.Extensions.VectorData.Abstractions` leaves the core and its
  prefix is removed. That prefix matched only the abstractions and a
  conformance-test package; vector-store providers live under
  `Microsoft.SemanticKernel.Connectors.*` and would need their own
  Ecosystem. Community-published
  `Anthropic.SDK` and `Mistral.SDK` are not lab-published SDKs and are not
  included.

- **ASP.NET Core becomes zero-root.** Its platform population is its hub;
  `Microsoft.AspNetCore.OpenApi` and
  `Microsoft.AspNetCore.Authentication.JwtBearer` are reached through the
  `Microsoft.AspNetCore.` prefix.
- **Aspire roots its hub.** `Aspire.Hosting` holds the application model (379
  public types: the builder, resources, `AddProject`, `AddContainer`,
  `WithReference`, `WaitFor`, parameters, and connection strings) and
  `Aspire.Hosting.Testing` holds the testing builder. Every integration API
  (`AddRedis`, `AddPostgres`, `AddRedisClient`, `AddNpgsqlDbContext`) lives in
  one of about 110 per-technology hosting or client packages, reached through
  the `Aspire.` prefix. The core becomes `Aspire.Hosting` and
  `Aspire.Hosting.Testing`.

The guiding rule is **root the hub, prefix the fan-out**. An Ecosystem needs
no root when a platform population is its hub. Runtime 0, ASP.NET Core 0,
Microsoft.Extensions 0, Aspire 2, AI 7, Blazor 4, and .NET MAUI 5 are all
within the bound.

## Partitioned platform populations

The platform Workspace should never contain the same assembly twice, and
Microsoft.Extensions should own its assemblies. The two shared frameworks
share no assembly today (checked at 10.0.12 and 11.0.0-rc.1), so the only
overlap to resolve is the `Microsoft.Extensions*` assemblies: they physically
ship in `Microsoft.AspNetCore.App` and, on net11, partly in
`Microsoft.NETCore.App` (nine `Abstractions`, `Options`, and `Primitives`
assemblies), so today they are searchable only as part of those frameworks.

The partition moves `Microsoft.Extensions*` assemblies out of both
frameworks by name and into the Microsoft.Extensions Ecosystem. Each
framework keeps every other assembly, whatever its name: ASP.NET Core keeps
`System.Diagnostics.EventLog`, `System.Formats.Cbor`,
`System.Security.Cryptography.Pkcs`, `System.Security.Cryptography.Xml`,
`System.Threading.RateLimiting`, `Microsoft.JSInterop`, and
`Microsoft.Net.Http.Headers`, and Runtime keeps `WindowsBase`. Dependency
recognition's name families are a labeling rule and are not the partition.

`PlatformLibraryPopulationDeclaration` carries only a `PlatformFamily`, so it
cannot yet select part of a framework. The platform-population owner adds an
assembly-name selection: Microsoft.Extensions declares the
`Microsoft.Extensions*` assemblies of both shared frameworks, and Runtime and
ASP.NET Core declare their frameworks excluding them. Microsoft.Extensions then
finds its classic stack in bounded operations on its own, and the platform
Workspace contains each assembly once by construction. Until that slice
lands, a Microsoft.Extensions-only selection has no bounded population
beyond its prefix.

## Curation is not rebuilt

Each set encoded a rule, not only a list: for Extensions and ASP.NET Core, a
verified Microsoft-owned stable major-10 release that is not deprecated, not a
tool, has a managed `lib/` or `ref/` assembly, and supplies an assembly the
shared frameworks do not. This retirement does not reproduce that rule as
dynamic predicates. Prefix results therefore include shared-framework-only
packages, runtime payloads, tools, and older major lines. Consumers that run
prefixes present those rows as prefix discovery, not as curated membership,
and remain bounded by their own candidate limits and source page limits
(`Microsoft.Extensions.*` currently stops at 123 matches with
`SourcePageLimitReached`).

## Owner changes

| Owner | Change |
| --- | --- |
| [Static Ecosystem Packs](ecosystem-packs.md) | Core bound and its validation; AI namespace roots and summary drop vector data; core entries as traversal roots ("root the hub, prefix the fan-out"); Microsoft.Extensions and ASP.NET Core zero-root; Aspire core; `PackageSet` removed from pack descriptors and from the capability requirement; "Package-set composition" retired; Azure removed; AI core replaced and its prefixes reduced to `Microsoft.Extensions.AI` |
| [Package Set Registry](package-set-registry.md) | Retired: shipped inventory, registry, descriptor, identity, and catalog types deleted |
| [Search scope resolution](search-scope-resolution.md) | `--extensions` and `--aspnetcore` removed from `find`, `implements`, `extensions`, and `depends` once each command has a working replacement (see slice 6) |
| [Package query CLI](package-query-cli.md) | `depends-ecosystem=` matches the Ecosystem's core packages exactly and its recorded prefixes; set membership is no longer consulted |
| [Ecosystem change report](ecosystem-change-report.md) and [package activity experience](package-activity-experience.md) | A new Ecosystem-membership selection arm: core packages exactly plus every recorded prefix, the membership `PackageQueryEcosystemMembershipDeclaration` already models. The existing `PackagePrefix` arm holds one prefix and cannot express roots or several prefixes, so this changes the change-report selection contract; `package activity --ecosystem` and the Browser picker adopt the new arm |
| [Platform library population declaration](platform-library-population-declaration.md) | Assembly-name selection within a platform family, so `Microsoft.Extensions*` assemblies partition out of both shared frameworks |
| [Package-backed platform realization](package-backed-platform-realization.md) | Realization honors the selection for CLI and Browser |
| [Ecosystem dependency recognition](ecosystem-dependency-recognition.md) | AI associations become `Microsoft.Extensions.AI` plus exact package and assembly associations for the seven roots, so community `Anthropic.SDK` stays unrecognized; Azure associations removed |
| [Workspace definitions](workspace-definitions.md) | Packet format 3 carries Ecosystem declarations inline, so the data slice needs no format change and verifies that a packet naming `ecosystem.azure` still restores or fails visibly. The partition slice changes the platform-population encoding in the share packet (`["t", family]`) and definition JSON to carry the selection, with a stated treatment for older packets that carry only the family |

## Slices

Each slice lands independently from `main`, keeps every gate green, and
amends its owner's document with its code.

1. **This composition map.**
2. **`depends-ecosystem=`.** Match the Ecosystem's core packages exactly and
   its recorded prefixes. Results are unchanged today, because every current
   core entry and set member already matches its pack's prefix; only the
   evidence basis changes, from prefix to exact package, for core entries
   and former set members. This lands before the data
   slice so AI's new roots match as soon as they exist.
3. **Ecosystem data.** Remove Azure; empty the Microsoft.Extensions and
   ASP.NET Core cores; set the Aspire core; replace the AI core, reduce its
   prefixes to `Microsoft.Extensions.AI`, and drop vector data from its
   namespace roots and summary; update the dependency-recognition profile;
   add the core bound to pack validation with a boundary test at twelve and
   thirteen entries. With slice 2 in place, `depends-ecosystem=ai` matches
   the seven roots and the `Microsoft.Extensions.AI` prefix. Packages only
   the removed prefixes matched stop matching: the
   `Microsoft.Extensions.VectorData.*`, `Microsoft.Agents.AI.*`, and
   `ModelContextProtocol.*` families other than the roots themselves. When
   AI is registered, those packages also stop classifying as supply-chain
   baseline; the baseline owner's contract is unchanged, only its data.
4. **Package activity.** Add the Ecosystem-membership selection arm (core
   packages exactly plus every recorded prefix) to the change report; CLI
   `package activity --ecosystem` and the Browser picker adopt it, and the
   picker lists Ecosystems instead of package sets.
5. **Partitioned platform populations.** Assembly-name selection in platform
   populations, honored by realization and carried by the share packet and
   definition JSON, adopted by Runtime, ASP.NET Core, and
   Microsoft.Extensions, with a gate that the platform Workspace contains no
   assembly twice. The slice's own design confirms that assembly reference
   resolution does not depend on the partitioned population, so an
   ASP.NET Core-only selection still resolves references to
   `Microsoft.Extensions*` assemblies it no longer lists.
6. **Search flags.** Today `--ecosystem` exists only on `find`, where it
   registers Ecosystems inertly and adds no search content, and
   `implements`, `extensions`, and `depends` have no `--ecosystem` at all.
   `--package-prefix Microsoft.Extensions.` and
   `--package-prefix Microsoft.AspNetCore.`, available on all four commands,
   are today's equivalent. Each command removes `--extensions` and
   `--aspnetcore` only once `--ecosystem` is a search selector there:
   [Find Workspace scope](https://github.com/richlander/dotnet-inspect/pull/8750)
   does that for `find`; the other three adopt it in their own slices. The
   `skills/relationships/SKILL.md` update goes to the release tracker.
7. **Deletion.** Remove `PackageSet` from pack descriptors, the registry and
   its types, both literal membership copies in tests, the `ecosystem` Info
   "Package Set" row, and the Extensions-set workspace-budget census script
   and its pinned data. Registry deletion waits for slice 6 on every
   command, because the flags read set membership until they are removed.

Open issues that assume package sets survive are re-scoped when their slice
lands: #8271 (flags to `--ecosystem` once it selects search content), #8285 (versioned sets), #7862 (Browser
"Add package set"), and #8749 (Ecosystem destination with "Load more").

## Non-claims

This composition does not add package-query predicates for owner,
verification, deprecation, package type, or major line; change prefix query
limits; define streaming Find; or change any Ecosystem's tools, demos, or
scanner. It changes namespace roots only by removing AI's vector-data root
and Azure's roots, and platform populations only through the partition
slice.
