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
| Platform populations | Platform families, where the Ecosystem has one | Unchanged |
| Namespace roots, tools, demos, scanner | Unchanged | Unchanged |

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

The platform Workspace should never contain the same assembly twice. Each
shared-framework assembly belongs to exactly one Ecosystem, following the
assembly-name families the Ecosystem dependency-recognition profile already
uses: `System*`, `Microsoft.Win32*`, `Microsoft.CSharp`,
`Microsoft.VisualBasic`, `mscorlib`, and `netstandard` belong to Runtime;
`Microsoft.Extensions*` to Microsoft.Extensions; and `Microsoft.AspNetCore*`
to ASP.NET Core. The `Microsoft.Extensions*` assemblies physically ship in
the ASP.NET Core shared framework (and, on net11, partly in
`Microsoft.NETCore.App`), so today they are searchable only through ASP.NET
Core's whole-framework population.

`PlatformLibraryPopulationDeclaration` carries only a `PlatformFamily`, so it
cannot yet select part of a framework. The platform-population owner adds an
assembly-family selection: Microsoft.Extensions declares the
`Microsoft.Extensions*` assemblies of both shared frameworks, and Runtime and
ASP.NET Core declare their frameworks without them. Microsoft.Extensions then
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
| [Static Ecosystem Packs](ecosystem-packs.md) | Core bound and its validation; core entries as traversal roots ("root the hub, prefix the fan-out"); Microsoft.Extensions and ASP.NET Core zero-root; Aspire core; `PackageSet` removed from pack descriptors and from the capability requirement; "Package-set composition" retired; Azure removed; AI core replaced and its prefixes reduced to `Microsoft.Extensions.AI` |
| [Package Set Registry](package-set-registry.md) | Retired: shipped inventory, registry, descriptor, identity, and catalog types deleted |
| [Search scope resolution](search-scope-resolution.md) | `--extensions` and `--aspnetcore` removed from `find`, `implements`, `extensions`, and `depends`; `--ecosystem` is the replacement selector |
| [Package query CLI](package-query-cli.md) | `depends-ecosystem=` matches the Ecosystem's core packages exactly and its recorded prefixes; set membership is no longer consulted |
| [Ecosystem change report](ecosystem-change-report.md) and [package activity experience](package-activity-experience.md) | `package activity --ecosystem` and the Browser picker scope by the Ecosystem's prefixes through the existing `PackagePrefix` selection |
| [Platform library population declaration](platform-library-population-declaration.md) | Assembly-family selection within a platform family, so shared-framework assemblies partition across Runtime, ASP.NET Core, and Microsoft.Extensions |
| [Ecosystem dependency recognition](ecosystem-dependency-recognition.md) | AI families reduced to `Microsoft.Extensions.AI` and the root packages' families; Azure families removed |
| [Workspace definitions](workspace-definitions.md) | No contract change. Packet format 3 carries each Ecosystem declaration inline; the Ecosystem slice verifies that a packet naming `ecosystem.azure` still restores or fails visibly |

## Slices

Each slice lands independently from `main`, keeps every gate green, and
amends its owner's document with its code.

1. **This composition map.**
2. **Ecosystem data.** Remove Azure; empty the Microsoft.Extensions and
   ASP.NET Core cores; set the Aspire core; update the dependency-recognition
   profile; replace the AI core and reduce its
   prefixes to `Microsoft.Extensions.AI`; add the core bound to pack validation with a boundary test at
   twelve and thirteen entries.
3. **`depends-ecosystem=`.** Match the Ecosystem's core packages exactly and
   its recorded prefixes. For Aspire, Microsoft.Extensions, and ASP.NET Core results are unchanged,
   because every set member already matches its pack's prefix, and only the
   evidence basis for former set members changes. AI matches its seven roots
   and the `Microsoft.Extensions.AI` prefix; packages only its removed
   prefixes matched, such as `Microsoft.Extensions.VectorData.*`, stop
   matching `depends-ecosystem=ai`.
4. **Package activity.** CLI and Browser adopt prefix scope; the Browser
   picker lists Ecosystems instead of package sets.
5. **Search flags.** Remove `--extensions` and `--aspnetcore`; suggest the
   `skills/relationships/SKILL.md` update on the release tracker.
6. **Partitioned platform populations.** Assembly-family selection in
   platform populations, adopted by Runtime, ASP.NET Core, and
   Microsoft.Extensions, with a gate that the platform Workspace contains no
   assembly twice.
7. **Deletion.** Remove `PackageSet` from pack descriptors, the registry and
   its types, both literal membership copies in tests, the `ecosystem` Info
   "Package Set" row, and the Extensions-set workspace-budget census script
   and its pinned data.

Open issues that assume package sets survive are re-scoped when their slice
lands: #8271 (flags to `--ecosystem`), #8285 (versioned sets), #7862 (Browser
"Add package set"), and #8749 (Ecosystem destination with "Load more").

## Non-claims

This composition does not add package-query predicates for owner,
verification, deprecation, package type, or major line; change prefix query
limits; define streaming Find; or change any Ecosystem's platform populations,
namespace roots, tools, demos, or scanner.
