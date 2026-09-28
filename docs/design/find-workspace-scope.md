# Find Workspace scope

## Status and owner

Proposed. This is Find's host-adoption slice for the `Broad` candidate intent
that [Search scope resolution](search-scope-resolution.md#default-activation)
leaves to each consumer, tracked by
[#6761](https://github.com/richlander/dotnet-inspect/issues/6761). It owns one
claim:

> `find` searches a Workspace. With no source selector that Workspace is the
> Ecosystem-provided platform Workspace. Any explicit selector, including
> `--ecosystem`, starts from an empty Workspace and adds exactly what was
> named. `find` realizes a Workspace's named populations (platform families,
> core packages, explicit Packages and Libraries) through PlatformHouse and
> PackageHouse; its package-prefix populations stay registered and are not
> searched by this slice.

Motivating scenario: `find .Add* --ecosystem aspire` should return the
`Add*` members of `Aspire.Hosting` (`AddProject`, `AddContainer`,
`AddParameter`, …), and nothing from the platform.

Supporting owners, not additional claims:

| Owner | Contract consumed |
| --- | --- |
| [Static Ecosystem Packs](ecosystem-packs.md) | The platform Workspace plan (`EcosystemPackCatalog.CreatePlatformWorkspacePlan`), and each Ecosystem's populations and canonical IDs |
| [Package set retirement](package-set-retirement.md) | Package sets are not Workspace populations and are being retired; slice 6 removes `--extensions` and `--aspnetcore` from `find` once this design lands |
| [Workspace definitions](workspace-definitions.md#packet-completeness) | Complete packets, including Ecosystem registrations |
| [Find type-search service](find-search-service.md#classification) | Classification over the realized population |
| [Package archive range access](package-archive-range-access.md) and [package cache policy](package-cache-policy.md) | Ranged, size-first acquisition through the House |
| [CLI change classification](cli-change-classification.md) | The breaking changes listed under [Compatibility](#compatibility) |

Owner amendments this design requires. Each lands in its owner's document,
in the adoption step that implements it; this design does not restate those
contracts:

| Owner | Amendment |
| --- | --- |
| [Search scope resolution](search-scope-resolution.md#explicit-composition) | For `find` only: `--ecosystem` is an explicit selector that contributes the named Ecosystem's named populations; bare `--platform` contributes the platform Workspace plan's named populations instead of three frameworks. `implements`, `extensions`, and type-mode `depends` keep today's rows until their own slices |
| [CLI host architecture](../cli-architecture.md) (valued `--platform` disambiguation) | For `find`, a valued `--platform` equal to `runtime` or `aspnetcore` (optionally `@<version>`) lowers to that Ecosystem with its platform population pinned; `netstandard[@<version>]` lowers to the .NET Standard platform family, matching `library query --platform`; any other value names one platform Library, as today |

## Why

`find` predates Ecosystems. Its default population is still the
source-normalizer list `runtime`, `aspnetcore`, `netstandard`, which matches
neither Ecosystem plan:

| Definition | Named populations | Consumer today |
| --- | --- | --- |
| Platform Workspace plan | Runtime and ASP.NET Core platform families, plus the Microsoft.Extensions and ASP.NET Core core packages (5 on `main`) | Inspect Web default Workspace, CLI external call graph |
| All-known Workspace plan | the above plus the Aspire, AI, Azure, Blazor, and .NET MAUI core packages | Find's locator path, as inert registrations only |
| Find's effective default | Runtime, ASP.NET Core, and .NET Standard frameworks | `find`, `implements`, `extensions`, type-mode `depends` |

Each plan also registers package-prefix populations (`System.`,
`Microsoft.Extensions.`, `Microsoft.AspNetCore.`, and so on). Package sets
are not part of either plan: Workspace registrations never project them
([ecosystem-packs.md](ecosystem-packs.md), "Package-set composition"), and
[Package set retirement](package-set-retirement.md) removes them.

The effective default includes .NET Standard, which no Ecosystem declares and
the SDK does not install. A new user's first `find JsonSerializer` takes
0.95 s against 0.14 s warm, and the difference is acquiring
`NETStandard.Library.Ref`: a 3 MB transfer that writes 43 MB to the cache
(two extracted copies, each with a 16.8 MB XML documentation file). Results
also carry duplicate `netstandard` rows such as ``Dictionary`2``.

Find uses the platform plan rather than the all-known plan named by #6761:
the platform plan is the Browser's default, so both hosts search the same
default definition, and a bare `find` should answer platform questions
without also searching every registered Ecosystem's core packages. The core
package lists are owned by the Ecosystem packs; package-set retirement slice
3 empties the Microsoft.Extensions and ASP.NET Core lists, after which the
default realizes the two platform families alone.

## Gesture model

| Invocation | Workspace searched |
| --- | --- |
| `find Foo` | platform Workspace |
| `find Foo --platform` | platform Workspace (bare `--platform` adds it) |
| `find Foo --package Bar` | empty, plus `Bar` |
| `find Foo --package Bar --platform` | platform Workspace, plus `Bar` |
| `find Foo --ecosystem aspire` | empty, plus the Aspire Ecosystem's named populations |
| `find Foo --package Bar --platform runtime` | empty, plus `Bar`, plus the Runtime Ecosystem |
| `find Foo --platform runtime@10.0` | empty, plus the Runtime Ecosystem with its platform pinned to the latest 10.0.x |
| `find Foo --platform netstandard` | empty, plus the .NET Standard platform family |
| `find Foo --platform System.Xml` | empty, plus the `System.Xml` platform Library |
| `find Foo --workspace <packet>` | exactly the packet's Workspace, plus any explicit selectors |

Rules:

1. No selector means the platform Workspace. Any selector starts empty.
2. Bare `--platform` adds the platform Workspace to an explicit composition.
3. `--ecosystem <id>` is a selector. It accepts a canonical ID
   (`ecosystem.aspire`) or a short name (`aspire`), ASCII case-insensitive,
   and resolved to the canonical ID by the CLI before binding, as
   `ecosystem <name>` and `package activity --ecosystem` already do; adoption
   step 3 shares that lookup rather than copying it. It can
   repeat. It adds the Ecosystem's named populations: its platform family, if
   it has one, and its core packages at their latest stable versions.
4. `--platform runtime[@<version>]` and `--platform aspnetcore[@<version>]`
   (case-insensitive) are aliases for `--ecosystem runtime` and
   `--ecosystem aspnetcore`. A version pins that Ecosystem's platform
   population: `@10.0` selects the latest 10.0.x patch, and `@10.0.0` selects
   that exact version, matching `library query --platform`. Core packages are
   not pinned.
5. `--platform netstandard[@<version>]` selects the .NET Standard platform
   family, as `library query --platform` does. It is the only way `find`
   searches .NET Standard once the default drops it.
6. Any other `--platform <value>` names one platform Library, as today.
7. `--workspace` restores the complete packet. Packet completeness is owned by
   Workspace definitions; a packet that relied on the default must carry the
   default's Ecosystem registrations. This slice verifies that an Inspect Web
   default-Workspace share does so, or records the gap as that owner's defect.

`find Foo --platform System.Xml` therefore means "only that assembly": it is
the cheap, complete answer. When an explicit scope returns weak results, stderr
may suggest the next scope (`Remove --platform to search the platform
Workspace.`); scope never widens silently.

An explicit Workspace whose named populations are empty fails before
acquisition instead of reporting an empty success. For example, once
retirement slice 3 empties the Microsoft.Extensions core and before slice 5
gives it a platform population, `find Foo --ecosystem microsoft-extensions`
fails and names the Ecosystem's prefix (`--package-prefix
Microsoft.Extensions.` today).

## Registered versus searched populations

A Workspace carries named populations (platform families, core packages,
explicit Packages and Libraries) and package-prefix populations (`System.`,
`Microsoft.Extensions.`, `Aspire.`, and so on). This slice realizes and
searches the named populations only. Prefix populations remain registered,
survive in shared packets, and are not searched; `find --ecosystem aspire`
therefore finds `AddProject` in `Aspire.Hosting` but not `AddRedis` in
`Aspire.Hosting.Redis`.

Searching prefix populations is the following slice, tracked by
[#8811](https://github.com/richlander/dotnet-inspect/issues/8811): the cheap
named-population answer is printed complete first, prefix packages follow as
streamed TSV or JSONL rows, and blocking formats skip prefixes with a stderr
note naming `--include-prefix`. That slice owns its output, ordering, and
cancellation contract.

## Cost envelope and evidence

The target envelopes are proposals for the product owner:

| Host | Blocking answer | First streamed or Spotlight row |
| --- | --- | --- |
| CLI | at most 2 s warm, 5 s cold | 1 s |
| Browser/Wasm | 1 s after the Workspace is admitted | 300 ms per keystroke |

Measured with production NativeAOT `dotnet-inspect` 0.26.0 (`d236a7a`) and a
Browser/Wasm publish of this branch. Every row names its raw data under
[`eng/find-cost-evidence/`](../../eng/find-cost-evidence/) and the command in
[Reproduction](#reproduction). Harness timings measure the child process
alone.

### Scope scenarios

CLI, macOS arm64, 2026-09-28, `perf-bounded.tsv`: median of 3 cold samples
(fresh `HOME` and `NUGET_PACKAGES` each) and 5 warm samples.

| Scope | State | Direct | Miss | Members | Cache |
| --- | --- | --- | --- | --- | --- |
| Installed platform, today's default | warm | 0.14 s | 0.28 s | 0.96 s (442 rows) | 43 MB |
| Installed platform, today's default, new user | cold | 0.95 s | 1.00 s | 1.74 s | 43 MB written, all `NETStandard.Library.Ref` (3 MB transferred) |
| Downloaded platform, today's default (Wasm-like) | cold | 1.36 s | 1.47 s | 2.17 s (399 rows) | 240 MB |
| Downloaded platform, today's default | warm | 0.14 s | 0.27 s | 0.97 s | |
| Current core packages (5, by name) | cold | 1.22 s | 1.22 s | 1.29 s | 7 MB |
| Current core packages | warm | 0.05 s | 0.07 s | 0.06 s | |
| Package sets (`--platform --extensions --aspnetcore`) | cold | 14.4 s | 14.9 s | 14.2 s | 157 MB |
| Package sets | warm | 0.50 s | 0.98 s | 1.57 s | |
| `Avalonia@12.1.3` | cold | 0.73 s | 0.93 s | 0.71 s | 45 MB |
| `Avalonia@12.1.3` | warm | 0.22 s | 0.33 s | 0.28 s | |

Queries are a direct hit, a misspelling that forces the census and
similarity path, and a member search (`JsonSerializer`, `JsonSerialiser`,
`.Parse` for platform scopes; `ServiceCollection`, `ServiceColection`,
`.AddSingleton` for core packages; `Button`, `Buton`, `.Measure` for
Avalonia). The Cache column is the size of the fresh `HOME` after the run
(`du`), not bytes transferred. The downloaded platform's member search returns 399 rows against
the installed platform's 442 because it resolves a different ref-pack
version, so the two rows compare cost, not identical answers.

The default this design adopts (Runtime and ASP.NET Core families without
.NET Standard, plus the core packages) has no gesture in 0.26.0, so it is not
measured end to end. An estimate is the warm installed-platform row, which
still includes .NET Standard, plus the core-packages row: about 1.4 s cold and
0.2 s warm for a direct hit. Adoption step 1's merge gate measures the real
figure.

`perf-prefix.tsv`, 2026-09-27, an earlier harness whose timings include one
Python interpreter start (about 0.03 s, negligible at this scale): 1 cold and
2 warm samples.

| Scope | State | Direct | Miss | Members | Bytes |
| --- | --- | --- | --- | --- | --- |
| `Avalonia` package prefix (up to 500 Packages) | cold | 118 s | 84 s | 53 s | 2.6 GB |
| `Avalonia` package prefix | warm | 8.3 s | 12.1 s | 8.9 s | |

### Per-Package unit cost

Sample: 34 Packages, 4 named and 30 drawn from the `System.`,
`Microsoft.Extensions.`, and `Microsoft.AspNetCore.` prefixes
(`unit-sample.tsv`). Each figure is the median or nearest-rank p90 over
Packages. The CLI takes each Package's median of 3 warm samples; Wasm has one
cold and one warm sample per Package.

| Per Package | CLI cold census | CLI warm census | CLI warm member search | Wasm cold projection | Wasm warm projection |
| --- | --- | --- | --- | --- | --- |
| Packages measured | 34 | 34 | 34 | 27 | 27 |
| Median | 0.19 s | 0.040 s | 0.038 s | 0.68 s | 0.14 s |
| p90 | 0.29 s | 0.060 s | 0.051 s | 1.37 s | 0.60 s |
| `Avalonia@12.1.3` (12,146 members) | 0.88 s | 0.16 s | 0.10 s | 8.7 s | 6.9 s |

- **CLI** (`unit-cli.tsv`): `merritt` (Linux x64, 24 cores), 2026-09-28.
  Process startup is 0.029 s. Each CLI column is a separate whole process
  (`find ZzqNoSuchType` or `find .ZzqNoSuchMember`), including startup and
  opening the Package.
- **Wasm** (`unit-wasm.tsv`): `merritt`, headless Firefox, 2026-09-27,
  through the opt-in published runtime benchmark bridge. `queryPackage`
  projects every Type and member signature, so it is an upper bound for a
  Wasm Find. Seven of the 34 Packages failed and are excluded from the Wasm
  columns: `OpenAI` 2.14.0 exceeded the Browser retained-text bound, and six
  `System.*` 4.3.0 Packages failed because their selected reference and
  implementation assets have different assembly identities. Both are Browser
  defects outside this design.
- Over the 27 Packages both hosts measured, warm Wasm projection is a median
  3.5 times the CLI's warm Type census (up to 42 times) and 3.7 times its warm
  member search (up to 67 times, for Avalonia).
- The Wasm `net10.0` platform surface took 15.3 s cold and 13.0 s warm with a
  40 MB transfer before failing the ordinary Worker JSON size limit.

### What the evidence decides

- The default as defined fits the CLI envelope cold and warm: the installed
  platform families answer within the warm installed-platform time, and the
  current core packages add 1.2 s cold and 0.06 s warm (about 1.4 s cold in
  total for a direct hit). Retirement slice 3 removes
  those core packages from the default altogether.
- Dropping .NET Standard removes the new user's largest cold cost: the cold
  installed-platform row is the `NETStandard.Library.Ref` acquisition (3 MB
  transferred, 43 MB written).
- Explicit named scopes, including one uncached Package, fit the CLI envelope
  cold and warm.
- Package sets never fit a cold blocking answer (14 s in the package-sets
  row), and
  prefix populations fit neither host at any cache state. Both belong to the
  streamed follow-up (#8811), not to the default.
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
   PackageHouse, replacing the source-normalizer framework default and the
   legacy extractor path for Find. Merge requires NativeAOT base/head
   evidence for every Find terminal and a cold default inside the CLI
   envelope.
2. **Explicit selectors through the House.** `--package` with or without
   `--tfm`, `--platform <Library>`, `--library`, `--project`, and `--bin` build
   an empty Workspace; the compatibility double read is removed.
3. **`--ecosystem` as a selector,** with short names and the empty-population
   failure, together with the search-scope-resolution amendment. Demo:
   `find .Add* --ecosystem aspire`.
4. **`--workspace` for `find`**, with the packet-completeness verification
   above.
5. **`--platform` grammar,** together with the CLI host architecture
   amendment: bare form adds the platform Workspace; `runtime` and
   `aspnetcore` aliases with optional version pins; `netstandard` as its
   platform family.
6. **Browser.** Spotlight already uses the platform Workspace plan. Its Type
   Find consumes the same House-realized definition and Ecosystem selection;
   precomputed platform names indexes are a separately designed follow-up.

`implements`, `extensions`, and type-mode `depends` share the source
normalizer and adopt the same model in their own slices; until then they keep
the current default.

## Compatibility

Breaking under CLI change classification:

- default `find` scope drops .NET Standard and adds the platform Workspace
  plan's core packages (none once retirement slice 3 lands);
- `--ecosystem` changes from inert registration to a selector: it now
  suppresses the default and adds the Ecosystem's named populations, so
  `find Foo --ecosystem ecosystem.aspire` searches `Aspire.Hosting` instead of
  the three frameworks. It also accepts short names;
- bare `--platform` means the platform Workspace instead of three frameworks;
- `--platform runtime[@<version>]` and `--platform aspnetcore[@<version>]`
  select Ecosystems instead of a Library of that name, and
  `--platform netstandard[@<version>]` selects the .NET Standard family.

## Reproduction

- `eng/measure-find-scope-cost.sh <binary> <work-dir> [warm] [cold]`: scope
  scenarios; `ONLY="..."` selects a subset. `perf-bounded.tsv` is
  `ONLY="platform-installed platform-remote core-packages package-sets
  package-named"` with 5 warm and 3 cold samples; `perf-prefix.tsv` is
  `ONLY=package-prefix` with 2 and 1.
- `eng/measure-find-unit-cost.sh <binary> <sample.tsv> <work-dir> [warm]`:
  per-Package CLI unit costs, with 3 warm samples.
- `inspect-web/playwright.find-unit-cost.config.ts` with
  `browser/find-unit-cost.spec.ts`: Wasm unit costs through the opt-in
  published runtime benchmark bridge; set `FIND_UNIT_SAMPLE` and
  `FIND_UNIT_OUTPUT`.

These are preserved design probes, not CI gates.

## Non-claims

This design does not search prefix populations or define streaming output
(#8811), change match classification, define Browser names indexes, change
Ecosystem membership, or change `implements`, `extensions`, or `depends`.
