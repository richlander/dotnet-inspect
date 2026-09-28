# Find Workspace scope

## Status and owner

Proposed. This is Find's host-adoption slice for the `Broad` candidate intent
that [Search scope resolution](search-scope-resolution.md#default-activation)
leaves to each consumer, tracked by
[#6761](https://github.com/richlander/dotnet-inspect/issues/6761). It owns one
claim:

> `find` searches a Workspace. With no source selector that Workspace is the
> Ecosystem-provided platform Workspace; any explicit selector starts from an
> empty Workspace and adds exactly what was named; `--workspace` restores a
> complete Workspace packet. The Workspace's named populations are realized
> through PlatformHouse and PackageHouse, and its prefix populations stay
> registered but inert.

Supporting owners, not additional claims:

| Owner | Contract consumed |
| --- | --- |
| [Static Ecosystem Packs](ecosystem-packs.md) | The platform Workspace plan (`EcosystemPackCatalog.CreatePlatformWorkspacePlan`) and each Ecosystem's populations |
| [Workspace definitions](workspace-definitions.md#packet-completeness) | Complete packets, including Ecosystem registrations |
| [Find type-search service](find-search-service.md#classification) | Classification over the realized population |
| [Package archive range access](package-archive-range-access.md) and [package cache policy](package-cache-policy.md) | Ranged, size-first acquisition through the House |
| [CLI change classification](cli-change-classification.md) | The breaking scope changes listed below |

## Why

`find` predates Ecosystems. Its default population is still the
source-normalizer list `runtime`, `aspnetcore`, `netstandard`, which matches
neither Ecosystem plan:

| Definition | Contents | Consumer today |
| --- | --- | --- |
| Platform Workspace plan | `runtime`, `aspnetcore`, `microsoft-extensions` Ecosystems | Inspect Web default Workspace, CLI external call graph |
| All-known Workspace plan | the three above plus Aspire, AI, Azure, Blazor, MAUI | Find's locator path, as inert registrations only |
| Find's effective default | runtime, ASP.NET Core, and .NET Standard frameworks | `find`, `implements`, `extensions`, type-mode `depends` |

The effective default includes .NET Standard, which no Ecosystem declares and
the SDK does not install, and omits the Microsoft.Extensions package set that
the product default includes. A new user's first `find JsonSerializer` spends
most of its 1.6-1.8 seconds downloading `NETStandard.Library.Ref` (43 MB, of
which 16.8 MB is XML documentation), and results carry duplicate `netstandard`
rows such as ``Dictionary`2``.

Find uses the platform plan rather than the all-known plan named by #6761
because the all-known plan's additional Ecosystems bring package sets well
outside the cold cost envelope measured below. The platform plan is also the
Browser's default, so both hosts search the same default definition.

## Gesture model

| Invocation | Workspace searched |
| --- | --- |
| `find Foo` | platform Workspace |
| `find Foo --platform` | platform Workspace (bare `--platform` adds it) |
| `find Foo --package Bar` | empty, plus `Bar` |
| `find Foo --package Bar --platform` | platform Workspace, plus `Bar` |
| `find Foo --package Bar --platform runtime` | empty, plus `Bar`, plus the `runtime` Ecosystem |
| `find Foo --platform runtime@10.0` | empty, plus the `runtime` Ecosystem with its platform pinned to the latest 10.0.x |
| `find Foo --platform System.Xml` | empty, plus the `System.Xml` platform Library |
| `find Foo --workspace <packet>` | exactly the packet's Workspace, plus any explicit selectors |

Rules:

1. No selector means the platform Workspace. Any selector starts empty.
2. Bare `--platform` adds the platform Workspace to an explicit composition.
3. `--platform <family>[@<version>]` for `runtime` or `aspnetcore`
   (case-insensitive) is an alias for `--ecosystem <family>`. A version pins
   that Ecosystem's platform population: `@10.0` selects the latest 10.0.x
   patch, and `@10.0.0` selects that exact version, matching the
   `family@version` form `library query --platform` already accepts. The pin
   applies to the platform population; whether it also selects the
   Ecosystem's package-set versions (such as ASP.NET Core 10.0 Packages) is an
   open question for the Ecosystem owner. `--platform netstandard` fails before
   acquisition with a hint, because no Ecosystem declares it, rather than
   falling through to a Library named `netstandard`.
4. Any other `--platform <value>` names one platform Library, as today.
5. `--workspace` restores the complete packet. Packet completeness is owned by
   Workspace definitions; a packet that relied on the default must carry the
   default's Ecosystem registrations. This slice verifies that an Inspect Web
   default-Workspace share does so, or records the gap as that owner's defect.

`find Foo --platform System.Xml` therefore means "only that assembly": it is
the cheap, complete answer. When an explicit scope returns weak results, stderr
may suggest the next scope (`Remove --platform to search the platform
Workspace.`); scope never widens silently.

## Registered versus executed populations

A Workspace carries named populations (platform families, package sets,
explicit Packages and Libraries) and prefix populations (`System.`,
`Microsoft.Extensions.`, `Microsoft.AspNetCore.`). Bounded `find` realizes and
searches the named populations. Prefix populations remain registered and
inert; running them is the separately designed streaming mode.

## Cost envelope and evidence

The target envelopes are proposals for the product owner:

| Host | Blocking answer | First streamed or Spotlight row |
| --- | --- | --- |
| CLI | at most 2 s warm, 5 s cold | 1 s |
| Browser/Wasm | 1 s after the Workspace is admitted | 300 ms per keystroke |

Measured with production NativeAOT `dotnet-inspect` 0.26.0 (`d236a7a`) and a
Browser/Wasm publish of this branch, on 2026-09-27. CLI scope scenarios ran on
macOS arm64; unit costs and all Wasm rows ran on `merritt` (Linux x64,
24 cores) with headless Firefox. Commands and raw results are listed under
[Reproduction](#reproduction).

### Scope scenarios (CLI, median of 5 warm and 3 cold)

| Scope | State | Direct | Miss | Members | Bytes |
| --- | --- | --- | --- | --- | --- |
| Installed platform | warm | 0.16 s | 0.30 s | 0.99 s | 42 MB cache |
| Installed platform, new user | cold | 1.6-1.8 s | 1.7 s | 2.5 s | 43 MB (`NETStandard.Library.Ref`) |
| Downloaded platform (Wasm-like) | cold | 1.36 s | 1.44 s | 2.19 s | 239 MB |
| `Avalonia@12.1.3` | cold | 0.65 s | 0.77 s | 0.66 s | 45 MB |
| `Avalonia@12.1.3` | warm | 0.24 s | 0.35 s | 0.30 s | |
| `Avalonia` prefix (up to 500 Packages) | cold, 1 sample | 118 s | 84 s | 53 s | 2.6 GB |
| `Avalonia` prefix | warm | 8.0-8.6 s | 12.0 s | 8.9 s | |
| Platform Workspace equivalent (`--platform --extensions --aspnetcore`) | cold | **22.3 s** | | | 157 MB |
| Platform Workspace equivalent | warm | 0.6 s | | 1.8 s | |

### Per-Package unit cost (34 Packages: 4 named, 30 sampled from three prefixes)

| Per Package | CLI cold | CLI warm | Wasm cold | Wasm warm |
| --- | --- | --- | --- | --- |
| Median | 0.16 s | 0.040 s | 0.68 s | 0.14 s |
| p90 | 0.57 s | 0.059 s | 1.37 s | 0.60 s |
| `Avalonia@12.1.3` (12,146 members) | 0.57 s | 0.15 s | 8.7 s | 6.9 s |

CLI process startup is 0.03 s. Wasm cost scales with API surface size: warm
Wasm is a median 3.6 times the CLI and up to 45 times for member-heavy
Packages. Wasm `queryPackage` projects every Type and member signature, so it
is an upper bound for a Wasm Find. The Wasm platform surface (`net10.0`) took
15.3 s cold and 13.0 s warm with a 40 MB transfer before failing the ordinary
Worker JSON size limit.

A complete names index for the whole platform (9,415 Types and 101,672
members) is 0.07 MB of Type names and 0.49 MB of member names gzip-compressed,
or 1.11 MB with signatures.

### What the evidence decides

- Explicit named scopes fit the CLI envelope cold and warm.
- Prefix populations never fit a blocking answer on either host.
- The platform Workspace fits warm (0.6 s Types, 1.8 s members) but not cold
  (22.3 s): its 97 package-set members are acquired whole and one at a time.
  Adopting it requires the House's ranged, parallel acquisition, or phasing
  the package sets behind the platform families with a visible status line.
- Current acquisition does not use ranged reads for any of these paths:
  production 0.26.0 predates ranged reads; the platform-pack path uses the
  legacy extractor; `find --package` without `--tfm` is ineligible; and with
  `--tfm` a mixed `ref/` and `lib/` Package reads by range and then downloads
  the whole Package through the compatibility path.
- Wasm needs precomputed names indexes for immutable content and progressive
  Workspace admission; whole-surface projection does not fit its envelope.

## Adoption

1. **Default through the House.** `find` with no selector realizes the
   platform Workspace plan's named populations through PlatformHouse and
   PackageHouse with ranged, parallel acquisition, replacing the
   source-normalizer framework default and the legacy extractor path for
   Find. Merge requires NativeAOT base/head evidence for every Find terminal
   and a cold platform-Workspace result inside the CLI envelope, or an
   explicit phased presentation that keeps the first answer inside it.
2. **Explicit selectors through the House.** `--package` with or without
   `--tfm`, `--platform <Library>`, `--library`, `--project`, and `--bin` build
   an empty Workspace; the compatibility double read is removed.
3. **`--workspace` for `find`**, with the packet-completeness verification
   above.
4. **`--platform` grammar.** Bare form adds the platform Workspace; family
   aliases, with optional version pins, map to Ecosystems; `netstandard` fails
   visibly.
5. **Browser.** Spotlight already uses the platform Workspace plan. Its Type
   Find consumes the same House-realized definition; precomputed platform
   names indexes are a separately designed follow-up.

`implements`, `extensions`, and type-mode `depends` share the source
normalizer and adopt the same model in their own slices; until then they keep
the current default.

## Compatibility

Breaking under CLI change classification:

- default `find` scope drops .NET Standard and adds the Microsoft.Extensions
  and ASP.NET Core package sets;
- bare `--platform` means the platform Workspace instead of three frameworks;
- `--platform runtime[@version]` and `--platform aspnetcore[@version]` select
  Ecosystems, with an optional platform version pin, instead of a Library of
  that name; `--platform netstandard` fails with a hint.

## Reproduction

- `eng/measure-find-scope-cost.sh <binary> <work-dir> [warm] [cold]`: scope
  scenarios; `ONLY="..."` selects a subset.
- `eng/measure-find-unit-cost.sh <binary> <sample.tsv> <work-dir> [warm]`:
  per-Package CLI unit costs.
- `inspect-web/playwright.find-unit-cost.config.ts` with
  `browser/find-unit-cost.spec.ts`: Wasm unit costs through the opt-in
  published runtime benchmark bridge; set `FIND_UNIT_SAMPLE` and
  `FIND_UNIT_OUTPUT`.
- Raw results and the 34-Package sample are in `eng/find-cost-evidence/`.

These are preserved design probes, not CI gates.

## Non-claims

This design does not define streaming output or prefix execution, change
match classification, define Browser names indexes, change Ecosystem or
package-set membership, or change `implements`, `extensions`, or `depends`.
