# Progressive analysis workflow

## Status and authority

This document defines the **Progressive Analysis Workflow**, tracked by
[#8516](https://github.com/richlander/dotnet-inspect/issues/8516). The first
production consumer is the shipped `project-analysis` skill.

This is a focused cross-cutting pattern. It composes owner-issued inspection
results into an investigation narrative; it does not redefine package
acquisition, Library Metrics, performance Findings, dependency traversal,
SourceLink, API comparison, Inspection Envelope, Workspace, Share, or Browser
contracts.

## Owner and exact claim

The Progressive Analysis Workflow owns this claim:

> Given a selected .NET subject and a bounded investigation budget, publish
> useful ordered checkpoints whose claims retain their evidence class, exact
> subject identity, owner-issued support, qualifications, and continuation;
> choose later probes by their expected ability to strengthen the user's
> report rather than by a fixed inventory of available commands.

This owner defines:

- independent depth and breadth intents;
- the ordered investigation checkpoints;
- the evidence classes used in narrative claims;
- the claim ledger and explicit supersession rule;
- adaptive storyline selection;
- branch, budget, and stopping decisions;
- the minimum transparent progress record; and
- the report bundle expected from a workflow consumer.

It does not define:

- facts, completeness, or methodology owned by an inspection producer;
- package or source acquisition policy;
- identity, reference, resource-path, or Share codecs;
- section names, Query Space facets, or CLI grammar;
- visualization-specific metric semantics or layout;
- a universal package quality, maintainability, security, or performance
  score; or
- a new host-neutral inspection result spanning unrelated owners.

## User outcome

The workflow answers requests such as:

> Give me a report or analysis on this project, package, or dependency.

The user receives an early orientation and then increasingly useful
explanations. The workflow does not withhold all value until every possible
probe completes, nor does it force every subject through the same metric
template. A small integration package, an algorithm library, a dependency-heavy
application, and a versioned platform component should produce different
primary stories.

## Request

One request has:

- a selected subject: project, package coordinate, dependency, Library,
  assembly, or reusable inspection reference;
- a question or an open-ended analysis intent;
- a **depth** intent;
- a **breadth** intent;
- an optional time or work budget; and
- optional user-selected storylines.

Depth and breadth are independent:

| Intent | Meaning |
| --- | --- |
| Narrow and shallow | Fast orientation for one selected subject |
| Narrow and deep | Detailed implementation or source analysis of one subject |
| Wide and shallow | Survey Libraries, dependencies, versions, or integrations |
| Wide and deep | Expensive evidence across a broad selected population |

An omitted intent defaults to progressive analysis: begin narrow and shallow,
then propose or perform the next highest-value bounded step.

## Checkpoints

Each checkpoint is independently useful and can be rendered before later work
finishes.

### Orientation

Establish:

- exact subject and version or local project identity;
- selected TFM, RID, package asset, and Library where applicable;
- provenance and immediately available documentation;
- acquisition and evidence availability;
- initial diagnostics and qualifications; and
- a portable Workspace or explicit Share limitation.

Orientation answers "what did we inspect?" before interpreting the subject.

### Character

Run low-cost probes across plausible storylines and identify:

- the primary storyline;
- supporting storylines;
- de-emphasized storylines and the evidence for de-emphasis; and
- questions that remain unestablished.

Character is a routing result, not a quality judgment. It may identify
architecture, implementation concentration, performance, ecosystem
integration, dependencies, provenance, source, or evolution as promising.

### Investigation

Follow the strongest evidence through exact Libraries, Types, Members,
dependencies, source documents, or version pairs. Preserve the join currency
at every transition. A probe belongs here only when its result can strengthen,
qualify, or refute a stated question.

### Synthesis

Publish:

- the primary narrative and supporting observations;
- the evidence ledger;
- explicit qualifications and unresolved questions;
- suitable visualization data already issued by product owners;
- matching Inspect Web destinations where Share supports them; and
- the next optional deep or wide investigation with expected value and cost.

### Extended analysis

Deep or wide work is opt-in when it is materially more expensive than the
preceding checkpoints. It continues to publish intermediate checkpoints and
never converts a timeout, acquisition failure, or unsupported route into a
complete result.

## Evidence classes

Every narrative statement is one of four classes:

| Class | Required support |
| --- | --- |
| Fact | Direct owner-issued typed evidence for the exact subject |
| Derived observation | A reproducible calculation or join over retained facts |
| Interpretation | A labeled plausible explanation consistent with the facts |
| Hypothesis | A question plus the next supported probe that could test it |

An interpretation is never presented as a producer fact. A hypothesis is not
a weak fact. Unsupported quality labels such as "good", "safe", "simple", or
"maintainable" are absent unless an owner explicitly defines and supplies that
meaning.

## Claim ledger

Every material claim retains:

- stable claim identity within the investigation;
- evidence class;
- exact subject identities and selected context;
- owner-issued content or row identities;
- operation and methodology identity where available;
- resource path or reusable reference where available;
- Share outcome and website destination where available;
- diagnostics, coverage, and completeness;
- derivation inputs for a derived observation;
- the next probe for a hypothesis; and
- disposition: active, qualified, superseded, or unsupported.

Later checkpoints may add support, qualify a claim, or supersede it explicitly.
They never silently rewrite an earlier claim after the inspected subject,
selected asset, coverage, or evidence changes.

The ledger is a logical workflow contract, not a new serialized superset of all
inspection documents. Consumers retain owner-issued documents and reference
them rather than copying their schemas into one untyped result.

## Join currency

Use the strongest typed identity issued by each owner. Depending on the route,
that may include:

- package ID and exact version;
- project restore identity;
- TFM, RID, selected dependency group, and selected compile/runtime asset;
- assembly identity and module version ID;
- exact metadata Type key;
- Member stable selector, method token, evidence method, and IL offset;
- source repository, commit, document path, and checksum;
- old/new version correspondence;
- producer key, operation receipt, and methodology version;
- structural resource path or reusable inspection reference; and
- Workspace packet and Share outcome.

Display labels are never join keys. When no suitable reference or identity is
available, the workflow records that as a capability gap rather than deriving
one from rendered text.

Exact resource paths discovered through structural or query discovery should
be passed to `explain`. Explanation describes capability and meaning; it does
not substitute for subject-specific evidence. A reusable subject reference,
when an owner supplies one, should reopen or narrow the evidence without
guessing the coordinate.

## Envelope and completion discipline

When a route supports `InspectionEnvelope<TContent>`, consumers inspect all
three parts:

- **Content:** the owner-issued typed result;
- **Share:** the exact portable continuation or its explicit unavailable or
  non-projectable outcome; and
- **diagnostics:** partial, degraded, or failed evidence that qualifies
  Content.

An envelope is preferred when Share or service diagnostics matter. A route
without an envelope may still provide useful evidence, but the report records
the missing boundary and does not invent equivalent health from process exit
or empty rows.

Before describing a dependency closure as complete, use the dependency
owner's completion and acquisition evidence to account for requested roots,
admitted roots, traversal, package projections, selected dependency groups,
missing packages, source failures, and unresolved assemblies. The local cache
alone never proves the intended closure.

## Adaptive branch selection

The workflow first gathers inexpensive evidence that discriminates among
storylines. A branch is selected when it has:

1. a concrete user-relevant question;
2. owner-issued evidence suggesting the branch is material;
3. a supported next operation;
4. join currency connecting that operation to the current subject; and
5. a cost proportionate to the expected improvement.

A branch is de-emphasized when evidence says it is immaterial, the next route
cannot preserve identity, the required producer is unavailable, or its cost
exceeds the current intent. The reason remains visible.

The workflow can depart from prepared examples when current evidence creates a
question and installed capability discovery exposes a supported route. It does
not improvise command syntax or infer unsupported semantics.

## Cost and progress

Run the production NativeAOT tool for ordinary investigation. Separate:

- process startup;
- package or source acquisition;
- analysis;
- rendering; and
- cold- versus warm-cache observations.

Initial experience targets are orientation in seconds and a first narrative in
tens of seconds. These are product goals, not guarantees. A consumer records
observed timing and names its environment rather than generalizing from one
run.

Before a materially long operation, disclose its question, expected evidence,
and stopping condition. While work continues, publish bounded progress through
the owning operation when available. Every additional unit of work must
improve the report or establish why the next evidence cannot be obtained.

## Visualization and website handoff

Visualizations consume typed evidence already issued by an owner. Suitable
views may include:

- implementation and complexity treemaps;
- architecture neighborhoods and relationship crossings;
- dependency graphs or clustered matrices;
- package and assembly composition;
- version-change views;
- integration maps;
- supply-chain and provenance chains; and
- focused call or allocation relationships.

The report records exact node and edge identities, grouping, direction,
weights and units, qualifications, explanatory relationships, references, and
accessible text when the producer supplies them. It never reconstructs these
facts from prose or assigns visual meaning to an unlabeled number.

Inspect Web destinations derive from Share or the centralized Workspace
portable-query mechanism. A nearby page is not presented as a replay of an
unsupported analysis lens. Local or private evidence may be non-shareable and
must say so.

## Capability-gap protocol

When a supported investigation question reaches a missing product boundary:

1. retain the exact subject and command;
2. record expected and actual evidence;
3. record timing and diagnostics;
4. classify the gap as evidence, envelope, reference/explanation,
   acquisition, visualization data, or Share;
5. identify one owning component and focused design;
6. file a focused issue with a reproducible scenario; and
7. continue through other supported evidence when useful.

The workflow does not implement unrelated owner fixes, parse presentation as a
workaround, or report partial evidence as complete.

## First production consumer

The shipped `project-analysis` skill is the first bounded adopter. It teaches
an agent to:

- start with current installed capability and package/project identity;
- publish Orientation and Character before deep analysis;
- use discovery and explanation rather than remembered syntax;
- inspect envelopes where adopted;
- establish dependency completion before closure claims;
- choose storylines from evidence;
- preserve a claim ledger;
- select visualization and website continuations; and
- produce focused gap reports.

The skill is a workflow consumer. It does not add a new command, inspection
producer, report schema, or quality score.

## Initial evidence and gaps

Using production `dotnet-inspect 0.26.0+d236a7a` and the current machine's
cache, the Markout 0.35.2 probe observed:

| Step | Elapsed |
| --- | ---: |
| Package structural discovery | 1.63s |
| Package identity, signals, frameworks, and dependencies | 1.17s |
| Library Metrics | 0.63s |
| Complete package dependency traversal | 0.96s |
| Workspace Share URL | 0.42s |
| Library Metrics resource explanation | 0.38s |

The timings establish one feasible progressive path, not a portable
performance guarantee. The dependency document reported complete traversal,
two canonical package nodes, and acquisition of
`MarkdownTable.Formatting@0.3.4`.

The same probe found that CLI Library Metrics row output omits the Type
summaries and cross-Type relationship evidence already used by Inspect Web.
[#8517](https://github.com/richlander/dotnet-inspect/issues/8517) tracks the
focused complete-envelope and Share adoption.

## Validation

The first adopter is gated by CLI tests that:

- register and embed the `project-analysis` skill;
- source its listing description from frontmatter;
- retain current discovery, explanation, envelope, dependency-completion, and
  Share guidance; and
- distinguish facts, derived observations, interpretations, and hypotheses.

Real-subject probes remain reproducible design evidence until a focused
producer or host owner promotes a stable scenario into its Release suite.
