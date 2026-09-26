---
name: dotnet-inspect-project-analysis
version: 0.2.0
description: Run evidence-backed workflows for supply chain, dependency neighborhoods, architecture, performance leverage, upgrade impact, and ecosystem integration.
---

# dotnet-inspect: project analysis workflows

Use this skill when the user wants a report rather than one isolated fact:

> Give me an analysis of this project, package, or dependency.

This skill selects and composes **workflows**. It does not replace the focused
skills that own command semantics:

- `skill signals` — provenance, safety, compatibility, and supply-chain facts;
- `skill relationships` — dependencies, calls, implementors, and integrations;
- `skill performance` — leverage and static performance triage;
- `skill compatibility` — version comparison and history;
- `skill sourcelink` and `skill decompiler` — source and implementation;
- `skill query` — discovery, selection, formats, envelopes, and limits.

Load the relevant focused skill before running a workflow. Use `-D`, `-Q`, and
exact `explain` resource paths when installed capabilities differ from the
examples below.

```bash
dnx dotnet-inspect -y -- <command>
```

## Choose a workflow

| User question or subject cue | Workflow |
| --- | --- |
| What evidence accounts for how this package was produced? | Supply-chain dossier |
| What does this project/package bring in, and where are the boundaries? | Dependency neighborhood |
| Where is the implementation and how is it organized? | Architecture and implementation map |
| Which code deserves performance investigation first? | Performance leverage |
| What changes if I upgrade? | Upgrade impact |
| Which frameworks or package families does this connect? | Ecosystem integration |

Run more than one only when the first workflow exposes a concrete join. Do not
produce six shallow sections merely because six workflows exist.

## 1. Supply-chain dossier

**Use when:** evaluating a dependency, provenance, release artifact, or package
intake decision.

Load `skill signals`; load `skill sourcelink` before networked source checks.

### 1a. Fast package receipt

```bash
dnx dotnet-inspect -y -- package Foo@1.2.3 \
  -S "Package Info,Signals,Signature,Vulnerabilities,Dependencies" --json
```

Report:

- exact package/version, selected TFM when known, repository and commit;
- signature kind and what it verifies;
- symbols, deterministic-build, SourceLink-map, RID/native, trim/AOT, unsafe,
  and P/Invoke observations;
- vulnerability evidence and its observation boundary; and
- direct dependency declarations.

Do not convert Signals into a trust verdict. Distinguish "checked with no
match" from unavailable evidence.

### 1b. Artifact and source verification

Discover the available audit and SourceLink sections before selecting them:

```bash
dnx dotnet-inspect -y -- package Foo@1.2.3 -D @Audit
dnx dotnet-inspect -y -- package Foo@1.2.3 -D @SourceLink
dnx dotnet-inspect -y -- package Foo@1.2.3 \
  -S "Audit: Artifact Text,Audit: Identifier Confusion" --json
dnx dotnet-inspect -y -- package Foo@1.2.3 \
  -S "SourceLink: Availability,SourceLink: Missing Files"
dnx dotnet-inspect -y -- package Foo@1.2.3 \
  -S "SourceLink: Integrity"
```

Availability and integrity may perform network work; present the fast receipt
first. Integrity applies to the exact compiler-mapped source documents and
does not establish that the repository itself is trustworthy.

### 1c. Transitive supply chain

Continue with Workflow 2. A supply-chain dossier is incomplete if it reports
only the root package while claiming to cover its dependency closure.

**Visual:** provenance chain from package coordinate to signature, repository
commit, PDB/SourceLink evidence, and dependency packages. Use typed identities
and availability states; do not infer the chain from URLs in prose.

**Stop when:** root provenance and direct declarations are accounted for, or
continue through the complete dependency closure when the user's decision
depends on transitives.

## 2. Dependency neighborhood

**Use when:** explaining package footprint, architecture boundaries, transitive
risk, or calls that cross package ownership.

Load `skill relationships`.

### 2a. Materialize and verify the package neighborhood

```bash
dnx dotnet-inspect -y -- depends \
  --package Foo@1.2.3 --tfm net10.0 --json
```

For a restored project:

```bash
dnx dotnet-inspect -y -- depends \
  --project ./src/App/App.csproj \
  -S "Dependency Hierarchy,Dependencies" --json
```

Read the completion summary before narrating the graph. Account for requested,
admitted, and failed roots; traversal completion; depth boundaries; selected
dependency groups; every acquired or unavailable package projection; source
failures; and unresolved relationships. The local package cache does not prove
the requested dependency closure.

Deliver the neighborhood as:

- center/root and selected framework context;
- direct dependencies;
- important transitive branches;
- shared hubs and repeated dependency occurrences;
- framework-provided or prunable candidates when explicitly evaluated;
- incomplete boundaries; and
- the few branches worth drilling into.

### 2b. Explain a focal Type neighborhood

When the package graph raises a Type-level question, use the complete envelope:

```bash
dnx dotnet-inspect -y -- depends SomeType \
  --package Foo@1.2.3 --tfm net10.0 --envelope
```

Inspect `content`, `share`, and `diagnostics`. The Share URL can replay an exact
package-backed Type dependency request when projectable.

### 2c. Follow calls across package boundaries

After selecting a concrete root Member:

```bash
dnx dotnet-inspect -y -- graph calls Some.Type Method~stable \
  --root-package Foo@1.2.3 \
  --root-tfm net10.0 --tfm net10.0 --all
```

Use boundary ownership and shortest local connectors to explain how the root
reaches external packages. Do not treat an unclassified assembly as package
ownership.

**Visual:** directed package graph for closure; external-focused call graph for
a selected Member. A true clustered dependency-community view requires
Research-owned community evidence tracked by #8406; do not simulate it with
layout proximity.

**Stop when:** the closure is accounted for and the report has identified the
few consequential boundaries. Do not drill every leaf.

## 3. Architecture and implementation map

**Use when:** the question is where implementation lives, how concentrated it
is, or which Types collaborate.

Load `skill performance` for Library Metrics semantics and `skill
relationships` for calls.

### 3a. Select the exact Library

```bash
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library -D --details --json
```

If the package has no unique namesake Library, select its exact Library asset
through Workspace inventory rather than guessing from a display name.

### 3b. Map implementation

```bash
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library \
  -S "Library Metrics"
```

Report compiled-IL population coverage, implementation volume, distribution,
concentration, and exact maxima. Complexity is normal-flow cyclomatic
complexity over physical evidence bodies; it is neither authored-source intent
nor a maintainability score.

Use Inspect Web's Metrics lens for the existing Complexity Explorer and
Relationship Crossing views. CLI `--jsonl` currently exposes flattened
aggregate rows, not the complete Type-summary and cross-Type relationship
document. Complete envelope and Metrics Share adoption is tracked by #8517.

### 3c. Explain a surprising area

Open the exact Type and Member selected by the evidence:

```bash
dnx dotnet-inspect -y -- type Some.Type \
  --package Foo@1.2.3 --all
dnx dotnet-inspect -y -- member Some.Type Method~stable \
  --package Foo@1.2.3 -S "Call Graph" --tree
```

Use Source or Decompiled Source only when the implementation question needs
code. Preserve the distinction between authored source and reconstructed C#.

**Visual:** Complexity Explorer treemap, Relationship Crossing, then a focused
Member call tree. Do not create a whole-library call hairball.

**Stop when:** the report explains the major implementation regions and a
small number of evidence-backed outliers.

## 4. Performance leverage

**Use when:** deciding where profiling or optimization effort should begin.

Load `skill performance`. Static evidence prioritizes investigation; it does
not prove runtime hotness or allocation volume.

### 4a. Rank leverage

```bash
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library \
  -S "Top Leverage"
```

Use root reach, callers, fanout, depth, and loop calls to choose a small set of
Members with broad influence.

### 4b. Intersect leverage with actionable shapes

```bash
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library \
  -D @Performance --effective
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library \
  -S "Performance:*" \
  --where "Priority>=high" --top 20 --json
```

Keep Priority separate from Confidence. Preserve exact Method token, evidence
method, IL offset, operation, and Finding identity for a profiler or benchmark
join.

### 4c. Confirm before recommending a rewrite

Use BenchmarkDotNet or a representative trace against the same build. Report
static candidates separately from runtime-confirmed findings.

**Visual:** leverage-versus-evidence scatter or focused call/allocation path,
provided the exported rows retain typed axes and exact coordinates.

**Stop when:** a bounded candidate list has an explicit runtime-confirmation
plan. Do not turn every allocation instruction into an optimization task.

## 5. Upgrade impact

**Use when:** evaluating a package update, migration, or regression.

Load `skill compatibility`.

### 5a. Establish endpoint identity

Pin both versions and the intended TFM. Start with the complete API Changes
view, then use classified subsets for distinct questions:

```bash
dnx dotnet-inspect -y -- diff --package Foo@1.2.3..2.0.0 -S Changes
dnx dotnet-inspect -y -- diff --package Foo@1.2.3..2.0.0 --breaking
dnx dotnet-inspect -y -- diff --package Foo@1.2.3..2.0.0 --additive
dnx dotnet-inspect -y -- diff --package Foo@1.2.3..2.0.0 --implementation
```

`-S Changes` preserves the complete API population, including **Other API
Changes** (`unclassified` in structured rows). Breaking and additive views are
assessed subsets; neither can establish that no other API changed. Keep API,
analysis, and implementation observations separate rather than combining them
into one generic "changed" count.

### 5b. Trace a consequential change

Use exact Type/Member correspondence or history when the pairwise diff raises a
specific question:

```bash
dnx dotnet-inspect -y -- diff --history \
  --package Foo@1.2.3..2.0.0 \
  --type Some.Type --members --at all
```

Dense history is explicit bounded work. Prefer sparse or major-version probes
when the user asks when rather than requesting every release.

**Visual:** old/new categorized change view with exact correspondence and
separate API, implementation, and Finding transitions.

**Stop when:** user-relevant breaking changes and important implementation or
dependency shifts are explained. Add performance or dependency workflows only
for a concrete changed boundary.

## 6. Ecosystem integration

**Use when:** the subject is an adapter, extension package, hosting component,
or framework bridge with little local implementation.

Load `skill relationships`.

### 6a. Identify integrations

```bash
dnx dotnet-inspect -y -- library \
  --package Foo@1.2.3 --namesake-library \
  -S Integrations --jsonl
```

Discover filters before narrowing:

```bash
dnx dotnet-inspect -y -- library -Q Integrations
```

Report canonical integration and ecosystem identities, the exact APIs
supporting each observation, and unavailable or ambiguous bindings.

### 6b. Compare an explicit package set

```bash
dnx dotnet-inspect -y -- graph integrations \
  --package Foo@1.2.3 \
  --package Bar@4.5.6 \
  --tfm net10.0 --mermaid
```

This is an induced set, not dependency traversal. Missing endpoints remain
outside the graph until their owning package is explicitly included.

**Visual:** typed ecosystem integration graph. Direction and relationship kind
come from product evidence, not package naming.

**Stop when:** the package's role and principal extension/binding relationships
are clear. For a facade, this workflow may be the primary report even when
Library Metrics are unremarkable.

## Compose the report

For the selected workflow, publish:

1. **Scope** — exact subject, version, TFM/RID, selected assets, and workflow.
2. **Result** — the workflow's primary evidence-backed answer.
3. **Key evidence** — only the rows and receipts needed to support it.
4. **Qualifications** — completion, unavailable evidence, and static/runtime
   boundaries.
5. **Explore** — appropriate typed visualization and exact Inspect Web URL.
6. **Next workflow** — at most one adjacent workflow, justified by a concrete
   finding.

Classify narrative statements as fact, derived observation, interpretation, or
hypothesis. Prefer `--envelope` when the chosen route supports it and Share or
diagnostics matter. Use:

```bash
dnx dotnet-inspect -y -- workspace \
  --package Foo@1.2.3 --tfm net10.0 --share url
```

for the closest portable Workspace state. Do not present a nearby URL as a
replay of an unsupported analysis lens.

Use reusable subject references when an owner supplies them. General reusable
inspection-reference identity and projection remains tracked by #7916; do not
invent a reference from display text while that route is unavailable.

When a workflow needs missing typed evidence, envelope health, reference,
acquisition receipt, visualization data, or Share support, capture the exact
subject, command, expected and actual evidence, diagnostics, and timing. File
one focused issue against the owning component; do not parse presentation as a
workaround.
