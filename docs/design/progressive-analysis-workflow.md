# Progressive analysis workflows

## Status and authority

This document defines **Progressive Analysis Workflows**, tracked by
[#8516](https://github.com/richlander/dotnet-inspect/issues/8516). The first
production consumer is the shipped `project-analysis` skill.

This is a focused composition pattern. It defines how a named customer
workflow selects and joins existing owner-issued inspections. It does not
replace the command and evidence guidance in the `signals`, `relationships`,
`performance`, `compatibility`, `sourcelink`, `decompiler`, or `query` skills.
It does not redefine package acquisition, Research reports, Findings,
dependency traversal, SourceLink, API comparison, Inspection Envelope,
Workspace, Share, or Browser contracts.

## Owner and exact claim

Progressive Analysis Workflows owns this claim:

> Given a selected .NET subject and a named customer question, execute the
> smallest useful sequence of owner-issued inspections for that question,
> publish an independently useful result before optional deeper stages, and
> preserve the identities, coverage, qualifications, and continuations needed
> to connect each stage.

This owner defines:

- the catalog of customer workflows;
- the question and subject cues that select each workflow;
- each workflow's ordered stages, report outcome, visual form, and stopping
  rule;
- when one workflow may hand off to another;
- common narrative evidence classes; and
- focused capability-gap reporting.

It does not define:

- the syntax or semantics of an inspection command;
- facts, completion, or methodology owned by an inspection producer;
- package or source acquisition policy;
- identity, reference, resource-path, or Share codecs;
- visualization-specific metric semantics or layout;
- a universal package quality, maintainability, security, or performance
  score; or
- a new aggregate inspection document spanning unrelated owners.

## Why named workflows

A generic "inspect everything, then summarize" procedure duplicates the root
and focused skills, spends work without a customer question, and tends toward
the same report for every subject. A named workflow instead has:

- one recognizable customer decision;
- a bounded first useful result;
- evidence-specific follow-up stages;
- a defined report and visualization;
- a stop condition; and
- explicit adjacent workflows.

A facade package can therefore lead with ecosystem integration, an intake
review with supply-chain evidence, an algorithm library with its
implementation map, and an application with dependency neighborhoods.

## Workflow contract

Every workflow declares:

| Part | Requirement |
| --- | --- |
| Question | The customer decision or explanation it serves |
| Entry subjects | Project, package, dependency, Library, Type, Member, or version pair |
| First result | A bounded result useful without later stages |
| Stages | Ordered owner-issued inspections and their joins |
| Evidence boundary | Claims the workflow may and may not make |
| Report | The workflow-specific narrative outcome |
| Visual | A typed visual form, or an explicit absence |
| Stop | The condition that prevents unbounded drilling |
| Handoffs | Concrete evidence that can start another workflow |

Command spelling and detailed interpretation remain in focused product skills.
The workflow consumer loads those skills and uses installed `-D`, `-Q`, and
exact Resource Explanation when capabilities differ from examples.

## Shared report discipline

These rules apply to every workflow without becoming another general command
guide:

- **Fact:** directly present in owner-issued typed evidence.
- **Derived observation:** a reproducible calculation or join over facts.
- **Interpretation:** a labeled plausible explanation.
- **Hypothesis:** a question plus the next supported probe.

Material claims retain the strongest owner-issued identity: package/version,
TFM/RID, selected asset, assembly/MVID, exact Type key, stable Member selector,
method/IL coordinate, source commit/document/checksum, or version
correspondence. Display labels are never join keys.

When a route supports `InspectionEnvelope<TContent>`, the workflow checks
Content, Share, and diagnostics. A route without an envelope can still support
the claims present in its output, but process success or empty rows do not
manufacture completion or service health.

The first stage should complete in seconds where current operations permit.
Networked, exhaustive, source-integrity, dense-history, or runtime-confirmation
stages follow an initial useful result and disclose their added question and
cost.

## Workflow catalog

### Supply-chain dossier

**Question:** What artifact did we receive, how was it produced, what evidence
supports its provenance, and what dependency supply chain accompanies it?

**First result:** exact package receipt, repository/commit, signature,
build/symbol/SourceLink signals, vulnerabilities, and direct dependency
declarations.

**Stages:**

1. Package identity, Signals, Signature, Vulnerabilities, and Dependencies.
2. Artifact-text and identifier-confusion audits when their observations
   matter.
3. SourceLink availability and integrity as explicit networked work.
4. Dependency Neighborhood when transitive supply-chain coverage is required.

**Boundary:** observations are not a trust or safety verdict. Checked-empty and
unavailable vulnerability or source evidence remain distinct. SourceLink
integrity verifies compiler-mapped content, not repository trust.

**Report:** artifact receipt, provenance chain, concerning or unavailable
signals, and direct/transitive coverage.

**Visual:** typed provenance chain from package coordinate through signature,
repository commit, PDB/SourceLink evidence, and acquired dependencies.

**Stop:** when the root receipt is accounted for, or when the selected
dependency closure is also accounted for if the user's decision depends on
transitives.

### Dependency neighborhood

**Question:** What does this subject bring in, where are its important
boundaries, and how does selected code reach external packages?

**First result:** direct and transitive package graph with completion,
framework selection, package acquisition, and unresolved-boundary evidence.

**Stages:**

1. Package or restored-project dependency hierarchy and declarations.
2. Completion review: requested/admitted/failed roots, traversal, depth
   boundaries, package projections, sources, and selected dependency groups.
3. Exact Type dependency envelope for a selected concern.
4. External-focused Member call graph for a consequential package boundary.

**Boundary:** cache presence does not prove intended closure; unclassified
assemblies do not establish package ownership; layout proximity does not
establish a dependency community.

**Report:** center, direct ring, important transitive branches, shared hubs,
framework-provided/prunable candidates when evaluated, and incomplete
boundaries.

**Visual:** directed package graph, then an external-focused call graph for one
selected Member. Research-owned clustered communities are tracked separately
by [#8406](https://github.com/richlander/dotnet-inspect/issues/8406).

**Stop:** once closure is accounted for and the few consequential boundaries
are identified; do not drill every leaf.

### Architecture and implementation map

**Question:** Where is implementation concentrated, how is it organized, and
which Types collaborate?

**First result:** exact Library selection, compiled-IL population coverage,
implementation volume, structural distributions, concentration, and maxima.

**Stages:**

1. Exact Library asset selection.
2. Research Library Metrics report.
3. Complexity Explorer and Relationship Crossing in Inspect Web.
4. Exact Type/Member drill-down and call graph for a surprising region.
5. Authored or decompiled source only when the implementation question needs
   code.

**Boundary:** normal-flow complexity is compiled-IL structural evidence, not
authored intent or a maintainability score. Decompiled C# is reconstructed,
not authored source.

**Report:** major implementation regions, concentration, collaboration
crossings, coverage, and a small number of evidence-backed outliers.

**Visual:** implementation/complexity treemap, bounded cross-Type
relationships, and a focused Member call tree.

**Stop:** after the major regions and selected outliers are explained. Complete
CLI envelope and Metrics Share support is tracked by
[#8517](https://github.com/richlander/dotnet-inspect/issues/8517).

### Performance leverage

**Question:** Which code deserves profiling or benchmark attention first?

**First result:** Members ranked by call-graph leverage.

**Stages:**

1. Top Leverage over the selected Library.
2. Effective performance section discovery.
3. Intersection with high-priority static Findings and exact IL coordinates.
4. Runtime confirmation with a representative benchmark or trace.

**Boundary:** static evidence identifies candidates; it does not prove runtime
frequency, elapsed cost, allocated bytes, or rewrite benefit. Priority and
Confidence remain separate.

**Report:** bounded candidate list, why each candidate has leverage, exact
static evidence, and the runtime-confirmation plan or result.

**Visual:** typed leverage-versus-evidence view or focused call/allocation
path.

**Stop:** when a small candidate set has an explicit confirmation plan; do not
turn every allocation instruction into an optimization task.

### Upgrade impact

**Question:** What user-relevant API, implementation, dependency, or Finding
change accompanies an upgrade?

**First result:** exact endpoint identities and complete pairwise API changes,
including changes without a compatibility classification.

**Stages:**

1. Complete API Changes view, retaining assessed and unclassified changes.
2. Breaking and additive API views as classified subsets.
3. Implementation or Finding comparison when the question requires it.
4. Exact Type/Member correspondence for consequential changes.
5. Sparse, major-version, or explicitly dense history to locate when a change
   appeared.
6. Dependency Neighborhood or Performance Leverage only for a concrete changed
   boundary.

**Boundary:** API, implementation, analysis, and dependency changes retain
their distinct meanings and completion. One generic changed count cannot
replace them. Breaking and additive filters omit API changes without a
compatibility assessment, so neither filtered view can replace the complete
API Changes population.

**Report:** user-relevant compatibility changes, implementation shifts, exact
correspondence, and migration implications supported by evidence.

**Visual:** categorized old/new change view with explicit correspondence.

**Stop:** when consequential changes are explained; do not enumerate unrelated
history.

### Ecosystem integration

**Question:** Which frameworks, extension points, or package families does this
subject connect?

**First result:** exact Integration rows for the selected Library.

**Stages:**

1. Integration facet discovery.
2. Exact Library Integration evidence.
3. Explicit package-set integration graph when relationships among several
   known packages matter.
4. Dependency Neighborhood only when package closure affects the explanation.

**Boundary:** an integration graph is an induced explicit set, not dependency
traversal. Missing endpoints remain absent until their owning packages are
included. Package naming is not ecosystem evidence.

**Report:** the subject's framework role, exact supporting APIs, principal
bindings, and unavailable or ambiguous endpoints.

**Visual:** typed ecosystem integration graph.

**Stop:** when the principal extension and binding relationships are clear.
This may be the primary report for a facade with little implementation.

## Workflow handoffs

One workflow starts another only from concrete evidence:

| From | Evidence | To |
| --- | --- | --- |
| Supply-chain dossier | Transitive coverage matters | Dependency neighborhood |
| Dependency neighborhood | One external boundary dominates | Architecture map or performance leverage |
| Architecture map | High-leverage or allocation-bearing outlier | Performance leverage |
| Upgrade impact | Dependency edge changed | Dependency neighborhood |
| Upgrade impact | Performance Finding changed | Performance leverage |
| Ecosystem integration | Missing endpoint ownership matters | Dependency neighborhood |

The report names the handoff question, expected evidence, and cost. It does not
run every adjacent workflow automatically.

## Website and visualization handoff

Visuals consume typed evidence already issued by their owner. The workflow
preserves exact node/edge identities, grouping, direction, weights and units,
qualifications, references, and accessible text where available. It never
reconstructs graph semantics from prose.

Inspect Web destinations derive from an operation's Share or the centralized
Workspace portable-query mechanism. A nearby package or API page is not
presented as replaying an unsupported analysis lens. Local/private evidence
may be explicitly non-projectable. General reusable subject-reference identity
and projection remains owned by
[#7916](https://github.com/richlander/dotnet-inspect/issues/7916); workflows do
not derive references from display text while that path is unavailable.

## Capability-gap protocol

When a workflow reaches a missing product boundary:

1. retain the exact subject, command, expected evidence, actual result,
   diagnostics, and timing;
2. classify the gap as evidence, envelope, reference/explanation,
   acquisition, visualization data, or Share;
3. identify one owning component and focused design;
4. file a focused issue with the workflow consequence; and
5. continue only through other supported evidence.

The workflow never parses presentation as a substitute for typed evidence or
reports partial data as complete.

## First production consumer and adoption

The shipped `project-analysis` skill is the first bounded adopter:

1. register and embed the workflow catalog in the CLI;
2. route report requests to one primary workflow;
3. compose existing focused skills rather than duplicate their command
   manuals;
4. retain workflow-specific boundaries and stop conditions; and
5. identify adjacent workflows only from concrete evidence.

The skill adds no command, inspection producer, report schema, renderer, or
quality score. Focused owner issues add missing product evidence independently.

## Initial evidence

Using production `dotnet-inspect 0.26.0+d236a7a` and the current machine's
cache, a Markout 0.35.2 architecture probe observed:

| Step | Elapsed |
| --- | ---: |
| Package identity, signals, frameworks, and dependencies | 1.17s |
| Library Metrics | 0.63s |
| Complete package dependency traversal | 0.96s |
| Workspace Share URL | 0.42s |

The timings demonstrate a feasible early Architecture result followed by a
Dependency Neighborhood handoff; they are not portable performance
guarantees. The dependency document reported complete traversal and
acquisition of `MarkdownTable.Formatting@0.3.4`.

## Validation

CLI gates verify that the skill:

- is registered, embedded, and listed from frontmatter;
- names the six workflows and their customer questions;
- delegates command semantics to existing focused skills;
- preserves dependency-completion and static/runtime boundaries;
- describes typed visuals and exact Share limitations; and
- identifies #7916, #8406, and #8517 rather than simulating missing evidence.
