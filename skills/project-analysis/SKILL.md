---
name: dotnet-inspect-project-analysis
version: 0.1.0
description: Build a progressive evidence-backed report on a .NET project, package, dependency, or assembly, from fast orientation through selective deep analysis.
---

# dotnet-inspect: progressive project analysis

Use this skill for requests such as:

> Give me a report or analysis on this project, package, or dependency.

Deliver useful findings early, then deepen the report only where evidence
shows value. Do not run every analysis before responding, apply the same metric
template to every subject, or turn static observations into a quality score.

```bash
dnx dotnet-inspect -y -- <command>
```

## Choose depth and breadth

Treat these as independent:

- **narrow + shallow:** fast orientation for one subject;
- **narrow + deep:** implementation, source, or performance detail;
- **wide + shallow:** survey Libraries, dependencies, integrations, or
  versions;
- **wide + deep:** expensive evidence across a broad population.

When the user does not choose, begin narrow and shallow. Publish that result,
then continue with the highest-value bounded investigation.

## Report progressively

Use five checkpoints:

1. **Orientation** — exact subject, selected TFM/assets, provenance,
   acquisition state, diagnostics, and Share outcome.
2. **Character** — likely primary and supporting stories, plus areas that look
   immaterial or unavailable.
3. **Investigation** — exact Libraries, Types, Members, dependencies, source,
   or versions supporting the strongest story.
4. **Synthesis** — narrative, evidence ledger, qualifications,
   visualizations, and Inspect Web destinations.
5. **Extended analysis** — optional deep or wide work with disclosed cost and
   expected value.

Do not wait for Extended analysis before presenting Orientation or Character.

## Start from the subject

For an exact package, establish identity, provenance, available frameworks,
and direct declarations together:

```bash
dnx dotnet-inspect -y -- package Foo@1.2.3 \
  -S "Package Info,Signals,Target Frameworks,Dependencies" --json
```

For a restored project, discover its project sections and inspect its existing
dependency assets:

```bash
dnx dotnet-inspect -y -- project ./src/App/App.csproj -D
dnx dotnet-inspect -y -- depends \
  --project ./src/App/App.csproj \
  -S "Dependency Hierarchy,Dependencies" --json
```

For a known package Library, discover before selecting expensive sections:

```bash
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library -D --details --json
```

If the package has no unique namesake Library, inspect its package/Workspace
inventory and select the exact Library rather than guessing from a display
name.

Use `find Pattern` when the subject is an API rather than a known package.
Load `skill query` for section and query shaping, `skill relationships` for
dependency and call evidence, `skill performance` for static performance
triage, `skill signals` for observable dependency signals, and `skill
sourcelink` or `skill decompiler` only when those storylines become material.

## Discover; do not remember

Use the installed binary's capabilities:

```bash
dnx dotnet-inspect -y -- library -Q
dnx dotnet-inspect -y -- library Foo -D --details
dnx dotnet-inspect -y -- library Foo -Q "Performance: Arrays" --json
dnx dotnet-inspect -y -- explain library/sections/library-metrics --depth 1
```

`-D` discovers sections and stable resource paths. `-Q` discovers executable
facets, operators, and values. Pass an emitted exact resource path to
`explain` to understand its owner, shape, relationships, and capabilities.
Explanation describes a capability; it is not evidence about the selected
package.

Prefer an owner-issued reusable reference when a result supplies one. If a
route has no reference, do not manufacture one from rendered labels.

## Build the Character checkpoint

Probe a few cheap, discriminating surfaces:

| Story | First evidence |
| --- | --- |
| Package shape and provenance | Package Info, Signals, frameworks, files |
| Architecture | Libraries, references, integrations, implementation volume |
| Structural complexity | Explicit Library Metrics |
| Performance | Top Leverage, then a selected `@Performance` kind |
| Dependencies | Dependency hierarchy plus completion summary |
| Source | SourceLink map and diagnostics before networked integrity |
| Evolution | API or implementation diff over an exact version pair |

Select a primary story only when the evidence is material. Record why other
stories were deferred: no implementation, few relationships, no version
question, missing source, unsupported route, or disproportionate cost are
useful outcomes.

Library Metrics is explicit:

```bash
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library \
  -S "Library Metrics"
```

Use it as compiled-IL structural evidence, not authored-source intent or a
maintainability score. Use `--jsonl` only for its flattened aggregate rows.
The current CLI does not expose the complete visualization document through
an envelope; do not infer Type summaries or relationship edges from those
rows.

## Preserve evidence classes

Label every material statement as:

- **Fact** — directly present in owner-issued evidence.
- **Derived observation** — reproducible calculation or join over facts.
- **Interpretation** — plausible explanation, clearly labeled.
- **Hypothesis** — question with a named next probe.

For each claim retain:

- exact package/project, TFM/RID, selected asset, assembly, Type, or Member;
- supporting section, row, document, or operation receipt;
- reference or resource path where available;
- Share outcome and URL where available;
- diagnostics and completeness;
- derivation inputs; and
- active, qualified, superseded, or unsupported status.

Never use display text as an identity. Do not silently replace a claim when a
later probe selects a different asset or yields incomplete evidence.

## Inspect envelopes when available

Load `skill query` to confirm current envelope routes and incompatibilities.
When `--envelope` is supported, inspect:

- `content` for the complete owner-issued result;
- `share` for an exact continuation or explicit limitation; and
- `diagnostics` for partial or degraded evidence.

For example, one exact package-backed Type dependency request can preserve all
three:

```bash
dnx dotnet-inspect -y -- depends SomeType \
  --package Foo@1.2.3 --tfm net10.0 --envelope
```

A successful process or empty row list is not a substitute for envelope
health. When the needed route has no envelope, record the missing boundary and
continue only with claims the available output supports.

## Establish dependency completeness

Before saying a dependency graph is complete, inspect its summary and package
projections:

```bash
dnx dotnet-inspect -y -- depends \
  --package Foo@1.2.3 --tfm net10.0 --json
```

Account for:

- requested, admitted, and failed roots;
- traversal completion and depth boundaries;
- selected dependency groups and frameworks;
- every acquired or unavailable package candidate;
- source and authentication failures;
- framework substitution or pruning when requested; and
- unresolved assemblies for implementation relationships.

The local package cache does not prove the intended closure. A partial graph
can still support explicitly partial observations.

## Follow evidence, not a checklist

Continue when a concrete question, join currency, supported operation, and
proportionate cost all exist. Examples:

- concentrated implementation -> inspect the largest or most complex exact
  Types and their Members;
- high call-graph leverage -> inspect callers and performance Findings, then
  ask for runtime confirmation before claiming hotness;
- an integration-heavy facade -> map extension points and ecosystem bindings
  instead of emphasizing complexity;
- a dependency boundary -> inspect exact package ownership and external calls;
- provenance uncertainty -> inspect SourceLink diagnostics and integrity;
- a version question -> compare exact endpoints and retain correspondence.

Stop or de-emphasize a branch when it is immaterial, cannot preserve identity,
has unavailable evidence, or exceeds the current depth/breadth budget. Say why.

## Select visualizations from typed evidence

Choose a visual only when its units and identities are available:

- implementation volume + complexity -> treemap;
- bounded Type relationships -> relationship crossing or neighborhoods;
- dependency closure -> directed graph or clustered matrix;
- package/assembly inventory -> composition view;
- exact old/new evidence -> version-change view;
- typed integrations -> ecosystem map;
- provenance steps -> supply-chain chain;
- exact calls or allocations -> focused relationship view.

Retain node/edge identities, grouping, direction, weights and units,
qualifications, references, and accessible text. Do not scrape Markdown or
invent missing relationships to make a diagram.

## Produce an Inspect Web continuation

Use Workspace Share for the closest exact portable state:

```bash
dnx dotnet-inspect -y -- workspace \
  --package Foo@1.2.3 --tfm net10.0 --share url
```

Prefer an operation envelope's Share when it reproduces a more specific
result. Never present a package home page or API Overview URL as if it replays
an unsupported analysis lens. Report local/private evidence as
non-projectable when appropriate.

## Report shape

Keep the user-facing result concise while retaining an evidence appendix:

1. **What this is** — identity, purpose, selected assets, and qualifications.
2. **Primary story** — the most useful evidence-backed explanation.
3. **Supporting stories** — only material secondary observations.
4. **Evidence and coverage** — facts, joins, diagnostics, and completeness.
5. **Explore** — visualizations and exact Inspect Web destinations.
6. **Next investigation** — one or two bounded probes with expected value and
   cost.

Expose enough investigation state to show what was selected, what was
de-emphasized, and why. Do not dump a command transcript.

## File product gaps precisely

When the report needs evidence the installed product cannot provide, capture:

- exact command, subject, version, TFM/RID, and tool version;
- expected owner-issued evidence;
- actual output, diagnostics, and timing;
- the missing envelope, reference/explanation, acquisition receipt,
  visualization data, or Share route;
- the single owning component; and
- the user-visible consequence.

Do not parse presentation as a workaround or broaden the current change across
unrelated owners.
