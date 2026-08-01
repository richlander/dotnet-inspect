# dotnet-inspect overview

`dotnet-inspect` is a CLI tool for inspecting .NET packages, platform libraries, local assemblies, public APIs, dependencies, SourceLink/symbol provenance, and version-to-version API changes.

It is built for both humans and agents. Markdown is the default output because headings, compact context rows, tables, and code fences are readable and easy for agents to quote. JSON, `--table`, and `--tsv` are available when structured automation or compact row output is more useful.

## Core architecture

- `src/dotnet-inspect/` contains the CLI, command routing, parsers, options, output views, section descriptors, and inspectors.
- `src/ILInspector.Metadata/` reads PE metadata, API surfaces, SourceLink/PDB data, method classification, and assembly details. `MetadataFindings` projects API, source-document, member-source, and portable-PDB build-context observations and comparisons onto the shared Finding spine while retaining compatibility classification through `ApiDiff`.
- `src/ILInspector.CSharp/` is the lightweight C# spelling and type-view layer over Metadata shapes. `CSharpFormatter` is the declaration-spelling seam; `CSharpTypePrinter` composes exact typed requests, including skeleton, full, stub, mixed-accessor, primary-constructor, and nested-type shapes, without taking a Decompiler or Research dependency.
- `src/ILInspector.Analysis/` indexes IL method-body evidence such as direct call sites, allocation and unsafety occurrences, method signals, and whole-assembly leverage without decompiling to C#. `AnalysisFindings` exposes reusable typed censuses and comparisons for allocations, call sites, unsafe operations, and unsafe declaration/body evidence.
- `src/ILInspector.Analysis.App/` is a temporary console harness for exercising Analysis queries until CLI wiring exists.
- `src/ILInspector.ControlFlow/` contains shared block-edge, dominance, and dataflow kernels used below Analysis and Decompiler without depending on either.
- `src/ILInspector.Findings/` contains the domain-free observation, inspection, matching, transition, comparison, whole-census correlation, and exact-identity correlation contracts shared by product producers. The `timeline` command composes Metadata and Analysis producers over those same correlation contracts.
- `src/ILInspector.Instructions/` is the shared IL decode + EH-aware basic-block substrate (one decoder the analyzer and decompiler converge onto); see [instruction substrate](design/instruction-substrate.md).
- `src/ILInspector.Text/` provides the reusable `TextFindings` API for exact, ordered line inspection and generic text comparison on the shared Finding spine.
- `src/DotnetInspector.Packages/` handles NuGet package extraction, package/source caches, feeds, symbol package acquisition, and version resolution.
- `src/DotnetInspector.Services/` contains shared services such as assembly-set acquisition, platform/package resolution, dependency resolution, signatures, source fetching, and nuspec parsing.
- `src/ILInspector.Decompiler/` emits lowered C#, raw IL, and structural annotated IL from method bodies.
- `src/ILInspector.Research/` owns the offset-keyed fact overlay above Analysis and Decompiler: its registry orders fact producers, joins R1 analysis occurrences with R2 decompiler projections, and projects facts into the Annotated Source, annotated IL, and Facts views used by `member`.
- `tools/DecompilerHarness/` owns ReturnToSender closure discovery and type-cluster planning. RTS specifies the required Metadata/CSharp request shape; `ILInspector.CSharp` owns rendering it.

## Engineering guidance

[AGENTS.md](../AGENTS.md) is the source of truth for repository-wide
engineering and workflow rules. This document describes subsystem ownership;
use the task map in `AGENTS.md` to find the focused guidance for a change.

## Important systems

- [Architecture](architecture.md): command and metadata architecture.
- [Inspection layers](design/inspection-layers.md): layer split for multiple consumers, vocabulary, and seam rules.
- [Signals](assembly-audit.md): package/library signal semantics and network scope.
- [PDB acquisition](pdb-acquisition.md): symbols and SourceLink acquisition.
- [Untrusted data threat model](design/untrusted-data-threat-model.md): trust boundaries and security rules for inspected artifacts, network input, caches, output paths, and rendering.
- [Bounded metadata traversal](design/bounded-metadata-traversal.md): cycle, depth, count, text-budget, failure, and verification rules for artifact-derived metadata graphs.
- [Rendering model](design/rendering-model.md): output mode and verbosity design.
- [Progressive disclosure](design/progressive-disclosure.md): verbosity, `-D`/`-S`, opt-in sections, `-S @All`, and limiter behavior.
- [Command transitions](design/command-transition-model.md): when source, focus, operation arity, lens, traversal, or rendering changes should switch commands versus stay within one command.
- [Row query and ordering](design/row-query-order.md): proposed field-scoped row predicates, ordering, `--top`, and schema-discoverable defaults.
- [Analysis UX scopes](design/analysis-ux-scopes.md): shared analysis vocabulary across offset, member, type, and library scopes.
- [IL coordinate workflows](design/il-coordinate-workflows.md): prototype workflows for explaining sparse runtime coordinates from debugger, profiler, or analyzer artifacts.
- [IL Diff canonicalization](design/il-diff-canonicalization.md): current `CanonicalIlOperation` guarantees, boundaries, and extension points.
- [Finding nomenclature](design/finding-nomenclature.md): observation/change semantics, operation outcomes, and Research composition boundaries.
- [Finding producer design](design/finding-producers.md): how to choose owners, payloads, identities, result shapes, and matching modes.
- [Finding coordinates](design/finding-coordinates.md): separation of subject identity, correspondence, optional producer order, and typed provenance.
- [Finding adoption](design/finding-adoption.md): consumer migration, failure visibility, native-case presentation, and quality-gate rules.
- [Source Finding producers](design/source-finding-producers.md): portable-PDB source/build-context inputs, outputs, identities, and migration boundaries.
- [Implementation Diff](design/implementation-diff.md): product C# + IL/body diff projection shared by the opt-in `diff` section, RTS, and harnesses.
- [C# assembly round-trip testing](design/csharp-member-recompilation.md): proposed tools-only `cluster`/`all` artifact compilation and layered IL/C# comparison.
- [Fixture governance](fixture-governance.md): fixture catalog, project-boundary, and semantic-axis rules.
- [Integrations](design/integrations.md): library ecosystem integration roll-ups and focused API currency.
- [Section model](design/section-model.md): section selection and query behavior.
- [Capability section registry spike](design/capability-section-registry-spike.md): measured static lambda-table and precompiled-plan pilot layered on `SectionPipeline`.
- [Hidden-fact annotations](design/hidden-fact-annotations.md): offset-keyed fact overlay semantics, validation, and projections.
- [Caret stacking](design/caret-stacking.md): `--focus` caret display model — one numbered caret per fact extent, with the fact texts listed below.
- [Member Index](design/member-index.md): overload selector and digest contract.
- [Member target resolution](design/member-target-resolution.md): typed member selector, anchor, and body-target resolution.
- [Member ordering](design/member-order.md): canonical type/member section order and member-kind mapping.
- [Version resolution](design/version-resolution.md): package/platform version and cache behavior.
- [Cache concurrency and publication](design/cache-concurrency.md): process-local single-flight, atomic publication, dependency overlap, and filesystem guarantees.
- [Skill guidance taste](../taste/skill-guidance.md): how to maintain the embedded agent skill.
