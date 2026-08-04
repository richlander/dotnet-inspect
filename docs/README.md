# dotnet-inspect Documentation

dotnet-inspect is a CLI tool for exploring .NET libraries and NuGet packages. It's designed for both humans and LLMs—the structured markdown output is easy to read and easy to parse.

The tool answers questions like:

- What methods does `JsonSerializer` have?
- What changed between v9 and v10 of a package?
- Where does this type come from?
- Was this library built by Microsoft or rebuilt by my distro?

Unlike decompilers, dotnet-inspect focuses on the **public API surface**—the contracts you code against, not implementation details. It pulls from multiple sources (libraries, PDBs, symbol servers, NuGet metadata) to give you a complete picture.

## Quick Example

```bash
$ dotnet-inspect type JsonSerializer --package System.Text.Json --shape

# System.Text.Json.JsonSerializer (System.Text.Json 10.0.2)

System.Text.Json.JsonSerializer (System.Text.Json 10.0.2)
   ├─ string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
   ├─ string Serialize(object? value, Type inputType, JsonSerializerOptions? options = null)
   ├─ void Serialize<TValue>(Stream utf8Json, TValue value, JsonSerializerOptions? options = null)
   └─ ...
```

## Documentation

### Current system docs

| Document | Need served |
| -------- | ----------- |
| [Inspection Space Architecture](inspection-space.md) | Target core workspace, query, acquisition, join, cache, and safety architecture organized around Rich, Fast, and Safe. |
| [Overview](overview.md) | Minimum system and architecture context for humans and agents. |
| [Architecture](architecture.md) | Current command and metadata architecture. |
| [LLM Design](llm-design.md) | Current agent-facing output and workflow design. |
| [Progressive Disclosure](design/progressive-disclosure.md) | Current model for verbosity, `-D`/`-S`, opt-in sections, `-S @All`, counts, and row limits. |
| [Bare `-S` Default View](design/info-view.md) | Bullseye questions and section profiles for curated high-density default views. |
| [Platform Components](platform-components.md) | Accessing SDK libraries vs NuGet packages. |
| [Private NuGet Feeds](private-feeds.md) | How to give the tool access to a private feed: installing a credential provider, unattended and CI setup, and the `nuget.config` fallback. |
| [Signals](assembly-audit.md) | Understanding Signals output and network scope flags. |
| [SourceLink Exposure](sourcelink-exposure.md) | Where SourceLink appears in package/library/type/member flows and how PDB/network costs are controlled. |
| [PDB Acquisition](pdb-acquisition.md) | How symbols and SourceLink are resolved. |
| [Sample References](sample-references.md) | Extracting code samples from XML docs. |
| [Reading IR Dumps](decompiler-ir-dumps.md) | How maintainers read DecompilerHarness per-pass IR dumps to diagnose decompiled output. |
| [Decompiler Correctness Pipeline](decompiler-correctness-pipeline.md) | The staged gauntlet of decompiler checks, from entry gates to changed-method fidelity. |
| [Burndown Roles](burndown-curator.md) | Index for curator, runner, ladder tester, and discovery roles. |
| [Burndown Curator](../agents/burndown-curator.md) | Owns #1568, row reconciliation, PR SLA hygiene, and orphan clustering. |
| [Burndown Runner](../agents/burndown-runner.md) | Claims one row from a burndown list and drives it to PR, blocker, or pivot. |
| [Ladder Tester](../agents/ladder-tester.md) | Measures product quality ladder legs and spawns focused issues or linked burndowns. |
| [Burndown Discovery](../agents/burndown-discovery.md) | Finds high-confidence defects before they become burndown rows. |

### Contributor docs

| Document | Need served |
| -------- | ----------- |
| [Style Guide](design/style-guide.md) | Output formatting conventions. |
| [Output Shapes](design/output-shapes.md) | The Document → Table → Vector → Scalar shape ladder, how Markout produces it, and how the output flags select a shape. |
| [Graph Signal Annotations](design/graph-signal-annotations.md) | Projecting analysis signals (alloc/copy/unsafe, and exception-risk follow-ups) onto call-graph nodes via `--fields`. |
| [Allocation Triage Pre-Filters](design/allocation-triage-prefilters.md) | Which allocation candidates Performance Triage surfaces, why the pre-filters prune cold-by-construction shapes, and what realized cost the static side cannot predict. |
| [Finding Nomenclature](design/finding-nomenclature.md) | Canonical observation/change vocabulary, arity ladder, operation outcomes, and Research composition boundary. |
| [Finding Producer Design](design/finding-producers.md) | Choosing producer ownership, payloads, identities, result shapes, matching modes, and higher-rung boundaries. |
| [Performance Analysis Baselines](analysis-baselines.md) | Internal baselines of what each analysis type finds over a fixed corpus, with effectiveness ratings for the one-stop-shop Performance Analysis view. |
| [Dynamic Leak-Watch](design/dynamic-leak-watch.md) | The retention axis: how `runfaster leak-watch` separates a managed leak from a churn storm from native/committed growth, and why static triage and the allocation-tick join cannot. |
| [Rendering Model](design/rendering-model.md) | Historical/current rendering model notes; prefer [Progressive Disclosure](design/progressive-disclosure.md) for current agent-facing behavior. |
| [Section Model](design/section-model.md) | Section selection design notes; use with [Progressive Disclosure](design/progressive-disclosure.md). |
| [Schema Query](design/schema-query.md) | `-D`/`-S` schema/query implementation notes. |
| [Hidden-Fact Annotations](design/hidden-fact-annotations.md) | Allocation/unsafety/lifetime annotation model and the static IL pair-agreement oracle strategy. |
| [Caret Stacking](design/caret-stacking.md) | `--focus` display model: one caret per fact extent, packed onto as few rows as fit, with the numbered fact texts listed below. |
| [Decompiler Inspection & Oracle](design/decompiler-inspection-oracle.md) | Unifies single-method inspection (dump/stages) with the corpus-wide fidelity check oracle; product-vs-tool scoping. |
| [ReturnToSender: Fact-Planned Compile-Back Harness](design/fact-planned-compile-back-harness.md) | Spec for a fresh tools-side compile-back harness with fact-planned TypeProducer/TypePrinter shells. |
| [Method Body Inspection](design/method-body-inspection.md) | Target service seam for shared `member` and `library --il-offset` method-body facts and coordinate inspection. |
| [Member Body Substrate](design/member-body-substrate.md) | One base for skeleton/full/merged/diff body rendering: `ApiType` shape, `MemberAnchor` address, one scope, and `MemberBody`'s scalar (whole-body) and vector (offset-keyed) shapes. |
| [NuGet API](design/nuget.md) | NuGet API endpoints used by the tool. |
| [NuGet Feed Authentication](design/nuget-authentication.md) | How feeds are authenticated: `nuget.config` credentials, credential provider discovery and the 401-driven plugin protocol, which flow supplies the credential in each environment, which credential forms are supported, and the hermetic/live test tiers. See [Private NuGet Feeds](private-feeds.md) for setup instructions. |
| [Version Resolution](design/version-resolution.md) | Package/platform version and cache behavior. |
| [Cache concurrency and publication](design/cache-concurrency.md) | Process-local single-flight, cross-process atomic publication, dependency overlap, and filesystem guarantees. |
| [Assembly Inspection Query Model](design/assembly-inspection-query.md) | Target boundary where the CLI forms a query and the metadata/service layer resolves, opens, and returns the final shape (why the CLI should not hold a `PEReader`). |
| [Skill Guidance Taste](../taste/skill-guidance.md) | Good and bad examples for maintaining the embedded skill. |

### Design history and backlog

Some files under `docs/design/` and `docs/backlog*.md` were written during ideation. They are useful design history, but may not describe current CLI behavior. When current behavior matters, start with Overview, Architecture, Progressive Disclosure, the embedded skill, and tests.

## Getting Started

```bash
# Install and run with dnx (like npx)
dnx dotnet-inspect -y -- --help

# Or install globally
dotnet tool install -g dotnet-inspect
dotnet-inspect --help
```
