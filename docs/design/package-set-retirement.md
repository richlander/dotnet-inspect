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
  those two Ecosystems register.

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

  `Microsoft.Extensions.VectorData.Abstractions` leaves the core and its
  prefix is removed; it is reached only through traversal from a root that
  depends on it. Community-published
  `Anthropic.SDK` and `Mistral.SDK` are not lab-published SDKs and are not
  included.

Every other pack's core is already within the bound: Runtime 0, Microsoft
Extensions 3, ASP.NET Core 2, Aspire 1, Blazor 4, .NET MAUI 5.

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
| [Static Ecosystem Packs](ecosystem-packs.md) | Core bound and its validation; core entries as traversal roots; `PackageSet` removed from pack descriptors and from the capability requirement; "Package-set composition" retired; Azure removed; AI core replaced and its prefixes reduced to `Microsoft.Extensions.AI` |
| [Package Set Registry](package-set-registry.md) | Retired: shipped inventory, registry, descriptor, identity, and catalog types deleted |
| [Search scope resolution](search-scope-resolution.md) | `--extensions` and `--aspnetcore` removed from `find`, `implements`, `extensions`, and `depends`; `--ecosystem` is the replacement selector |
| [Package query CLI](package-query-cli.md) | `depends-ecosystem=` matches the Ecosystem's core packages exactly and its recorded prefixes; set membership is no longer consulted |
| [Ecosystem change report](ecosystem-change-report.md) and [package activity experience](package-activity-experience.md) | `package activity --ecosystem` and the Browser picker scope by the Ecosystem's prefixes through the existing `PackagePrefix` selection |
| [Workspace definitions](workspace-definitions.md) | No contract change. Packet format 3 carries each Ecosystem declaration inline; the Ecosystem slice verifies that a packet naming `ecosystem.azure` still restores or fails visibly |

## Slices

Each slice lands independently from `main`, keeps every gate green, and
amends its owner's document with its code.

1. **This composition map.**
2. **Ecosystem data.** Remove Azure; replace the AI core and reduce its
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
6. **Deletion.** Remove `PackageSet` from pack descriptors, the registry and
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
