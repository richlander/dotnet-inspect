# Contextual Resource Explanation

## Status

This document is the normative design for **Contextual Resource Explanation**,
the composition behind command-local `--explain` and the reusable
`--references` projection proposed by
[#8148](https://github.com/richlander/dotnet-inspect/issues/8148).

The composition is designed but not implemented. Product-resource explanation
for the complete Library structural domain is already implemented by
[Resource Explanation](resource-explanation.md). Reusable inspection-reference
identity remains owned and staged by
[#7916](https://github.com/richlander/dotnet-inspect/issues/7916).

This document does not complete either adjacent adoption. It fixes the
operation boundary and handoffs so each owner can land independently without
inventing a second contextual-explanation path.

## Owner and exact claim

**Contextual Resource Explanation** owns this exact claim:

> Given one admitted inspection command and its owner-issued command resource
> or exactly resolved explainable subject, select explanation as the terminal
> operation without reconstructing identity from command text, rendered
> output, or a serialized reference. Separately, project the reusable
> inspection references already issued for selected semantic rows in that
> selected order. Direct explanation and reference projection preserve the
> command owner's exact resolution and visible failure outcomes.

This owner defines:

- the distinction between direct contextual explanation and reusable-reference
  projection;
- the command-level and exact-subject meanings of `--explain`;
- the row-preserving meaning of `--references`;
- the typed handoff sequence among command resolution, adjacent explanation
  owners, row selection, and presentation;
- operation admission, cardinality, and failure behavior; and
- the staged CLI and Browser/Wasm adoption path.

It does not define product-resource paths, subject or occurrence identity,
reference syntax, acquisition policy, row-selection semantics, explanation
Content, or presentation formats.

## Product role

Resource Explanation currently supports this explicit handoff:

```text
discover -> copy product-resource path -> explain
```

That remains valuable for navigating the installed product catalog. It is not
the shortest path when a user is already operating at a command or exact
subject:

```console
dotnet-inspect member
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1
```

Contextual explanation supplies the direct gesture:

```console
dotnet-inspect member --explain
dotnet-inspect member JsonSerializer --package System.Text.Json \
  Serialize:1 --explain
```

Reusable references serve a different need. They let agents, scripts, saved
rows, and later product operations carry selected subjects without scraping an
inspection envelope or using `jq`:

```console
dotnet-inspect member JsonSerializer --package System.Text.Json \
  -S Methods --references

dotnet-inspect explain <emitted-reference>
```

The direct path is primary. A user does not need to request, serialize, and
reparse a public reference merely to explain the subject the same invocation
has already resolved.

## Basis and deliberate difference

`kubectl` provides nearby but incomplete precedents:

- [`kubectl get -o name`](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_get/)
  emits reusable object references such as `pod/name`;
- [`kubectl explain pod.spec.containers`](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_explain/)
  explains a resource schema path; and
- `kubectl explain` does not accept the object reference emitted by `kubectl
  get -o name`.

dotnet-inspect deliberately joins those experiences. An owner-issued reusable
reference must be accepted unchanged by `explain`, while `--explain` avoids the
handoff entirely when the current command already holds the exact typed
subject.

This design also follows the existing dotnet-inspect projection split:

- `--where`, `--order-by`, `-n`, and `--rows` select semantic rows;
- `--urls`, `--paths`, and `--value` project reusable values from those rows;
- `--bare` changes decoration without changing the selected shape; and
- `-o` / `--output` names a destination under
  [#7946](https://github.com/richlander/dotnet-inspect/pull/7946).

`--references` therefore joins the semantic projection family. It is not a
row predicate, presentation format, destination, or decoration modifier.

## Owner map

| Owner | Responsibility consumed by this composition |
| --- | --- |
| Command owner | Parser admission, acquisition authorization, exact command and subject resolution, semantic rows, diagnostics, and the mapping to owner-issued explanation inputs |
| [Resource Explanation](resource-explanation.md) | Product-resource paths, installed descriptor adaptation, bounded explanation Document, and completed explanation envelope |
| Reusable Inspection Reference ([#7916](https://github.com/richlander/dotnet-inspect/issues/7916)) | Reference identity, qualification, shell-safe spelling, parsing, occurrence semantics, and the affordance descriptor used for subject explanation |
| [Inspection Subject Navigation](inspection-subject-navigation.md) | Workspace-rooted structural subject identity and routes where a Workspace-backed command consumes those values |
| [Output Shapes](output-shapes.md) | Semantic row selection, scalar/list projection boundaries, structured projection, presentation, and output destination |
| Host | CLI option lowering or Browser interaction, presentation selection, and destination handling |

The composition consumes those contracts without widening them:

- a product-resource path never becomes a subject reference;
- a reusable reference never becomes a Workspace occurrence identity;
- a physical path or URL never substitutes for either;
- a portable scenario remains broader than one subject reference; and
- display labels and rendered commands remain presentation.

## Contextual targets

### Command-level resource

An adopting command may issue one installed product-resource identity for its
command-level contract. The identity is registered through ordinary Resource
Explanation adaptation and is available without acquiring an inspection
subject.

For example:

```console
dotnet-inspect member --explain
```

explains the registered Member command resource. It does not run Member
inspection, choose a package or platform target, or infer a resource path from
the parser command name.

An adopting command without a registered command resource does not advertise
command-level `--explain`. The host does not manufacture a generic help
document as a success-shaped substitute.

### Exact resolved subject

A subject-level request uses the exact subject already resolved by the command
owner:

```console
dotnet-inspect member JsonSerializer --package System.Text.Json \
  Serialize:1 --explain
```

The command performs only the acquisition and resolution required to establish
that subject and its owner-issued explainability input. It does not execute the
ordinary section plan merely because ordinary inspection would have done so.

The handoff retains the command owner's exact package or platform source,
version, TFM, Library, Type, overload, generic, and occurrence associations
when those values participate in subject identity or explainability. The
composition does not rebuild them from positional arguments.

The subject-reference owner decides what explanation means for a reusable
subject, including its accepted operations and other affordances. This
composition selects and invokes that operation; it does not define the
affordance vocabulary or add subject facts to the installed-capability
`ResourceExplanationDocument`.

### Explain facade dispatch

The top-level `explain` facade accepts two distinct typed operand families:

- a `ResourcePath`, dispatched to Resource Explanation; or
- an owner-issued reusable inspection reference, dispatched to the
  subject-affordance explanation owned under #7916.

The reference owner must provide an unambiguous parser outcome that cannot be
mistaken for a registered product-resource path. This composition does not
choose the distinguishing syntax.

The two operations may return different typed Content contracts through
`InspectionEnvelope<TContent>`. The facade does not force installed capability
and resolved-subject explanation into one universal Document merely because
they share a command name and presentation style.

Contextual subject-level `--explain` invokes the same subject-affordance
operation with its already resolved typed input. It bypasses public reference
serialization and parsing but not the subject owner's validation or failure
contract.

### Cardinality

Direct subject-level `--explain` requires exactly one resolved explainable
subject.

- Zero resolved subjects produce the command owner's ordinary visible
  not-found or unavailable outcome.
- Multiple resolved subjects produce a visible refinement diagnostic.
- One resolved but non-explainable subject produces the owner-issued
  unavailable or failed explanation outcome.

The host never selects the first result, uses a display-label match, or
silently falls back to command-level explanation.

## Operation and projection semantics

### `--explain`

`--explain` is a terminal content operation. It replaces ordinary inspection
Content after preserving the subject inputs needed for exact resolution.

An adopting command classifies its options before acquisition:

- source, version, framework, Library, Type, Member, occurrence, and other
  subject-resolution inputs retain their ordinary meaning;
- options needed to select exactly one subject retain their ordinary meaning;
- explanation traversal and presentation options apply only when declared by
  the explanation owner; and
- ordinary section, row, payload, graph, Count, or competing content
  operations are rejected rather than ignored.

The completed explanation is returned through
`InspectionEnvelope<TContent>`. CLI and Browser/Wasm consumers lower the same
host-neutral Content rather than constructing explanation prose independently.

### `--references`

`--references` is a semantic row projection. It consumes the command owner's
already-selected semantic sequence after applicable section and row selection:

```text
resolve subject and section
  -> produce complete semantic rows
  -> apply --where / ordering / row window
  -> project one owner-issued reference per selected row
  -> present or write the projection
```

The projection:

- preserves selected row order;
- emits exactly one reference for each selected row;
- does not resolve or inspect the referenced subject again;
- does not change row membership;
- does not infer identity from a displayed Name, Signature, Path, URL, token,
  digest, or row number; and
- is available only for row sets whose owner supplies the complete reference
  projection.

The default text form emits one shell-safe reference per line. Structured
forms preserve one typed reference record per selected row. Output-format and
destination spelling remain owned by their focused CLI grammar.

A projection request is validated and materialized before the host commits it
to any admitted destination. If any selected row lacks the required
owner-issued reference or reference construction fails:

- stdout receives no reference payload;
- a requested destination that did not exist remains absent; and
- a requested existing destination remains byte-for-byte unchanged.

The operation then reports the exact failure. This preserves the invariant
that a committed output's cardinality equals selected-row cardinality.
Crash-atomic filesystem replacement is not part of this claim; destination
commit mechanics remain owned by the output-destination contract.

### Mutual exclusion

`--explain` and `--references` are mutually exclusive:

- `--explain` obtains one explanation result for the command or exact subject;
- `--references` emits reusable identities for an already selected row
  sequence.

`--references` is not a hidden prerequisite for direct `--explain`.
`--bare` retains its decoration-only meaning and is not accepted as an alias.
`--format name` and `-o name` are not introduced.

## Handoff invariants

### Direct handoff

Direct contextual explanation passes the owner-issued typed explanation input
in memory. It does not:

- render a reusable reference and parse it back;
- parse the original command line a second time;
- reconstruct identity from a result row;
- reacquire an already resolved source; or
- execute ordinary content producers to discover explainability.

This path may therefore become available before every resolved subject has a
portable public reference, provided the responsible owner can issue the exact
typed explanation input.

The first-adopter gate instruments both command preprocessing/resolution and
acquisition. It proves that the invocation is parsed and resolved once, each
required source is acquired at most once, and the exact resulting typed input
is handed to explanation. A fail-fast public-reference serializer and parser
separately prove that the direct path does not take the reusable-reference
route.

### Reusable handoff

`--references` uses the public spelling issued under #7916. `explain` accepts
that spelling unchanged and resolves it through the same reference owner.

The public spelling is one legible, shell-safe, unquoted argument. Generic
definition identity is represented without C# angle-bracket quoting and
without whole-value Base64. This composition records that prerequisite but
does not define its grammar.

Round-tripping a reusable reference may reacquire or reopen its subject under
the reference owner's explicit semantics. It does not claim the process-local
Workspace occurrence or artifact realization from the original invocation
still exists.

### Workspace-backed subjects

When an adopting command already consumes an
`Inspection Subject Navigation` identity, contextual explanation preserves
that exact subject and route evidence in the in-process handoff. A reusable
reference remains a separate portable projection and cannot serialize
Workspace identity, action authority, retained-session state, or live
acquisition authority.

## Failure model

Every unavailable boundary remains visible:

| Condition | Result |
| --- | --- |
| Command has no registered command resource | Command-level `--explain` is unavailable; no generic help fallback |
| Subject resolution returns no subject | Preserve the command owner's not-found or unavailable result |
| Subject resolution returns several subjects | Reject direct explanation and request refinement |
| Exact subject has no explanation affordance | Return the owner-issued unavailable outcome |
| Subject explanation fails | Return the exact typed failure and no success-shaped empty Document |
| Selected row set has no reference projection | Reject `--references` before stdout |
| One selected row cannot produce its required reference | Fail the complete projection with no partial list |
| Reusable reference cannot be parsed or reopened | `explain` returns the reference owner's exact invalid, unavailable, or failed outcome |

Diagnostics may include inert display text and actionable next gestures. They
never mint a fallback identity.

## Production demo

`System.Text.Json.JsonSerializer.Serialize` is the first authentic scenario.

Command-level explanation requires no target:

```console
dotnet-inspect member --explain
```

Exact-subject explanation preserves the already resolved package context:

```console
dotnet-inspect member JsonSerializer --package System.Text.Json@10.0.0 \
  Serialize:1 --explain
```

Member Index can select and project reusable references:

```console
dotnet-inspect member JsonSerializer --package System.Text.Json@10.0.0 \
  -S "Member Index" --where '<member predicate>' --references
```

Each emitted value is accepted unchanged:

```console
dotnet-inspect explain <emitted-reference>
```

The pathological neighboring cases are:

- two `Serialize` overloads remain selected and direct `--explain` refuses to
  choose one;
- two same-named members in different declaring Types receive distinct
  references;
- a generic Type or Member emits one legible shell-safe reference;
- a row window selects one exact reference without renumbering identity;
- one selected row lacks an owner-issued reference and no partial list is
  committed; and
- a reusable reference reopens after the original Workspace has closed without
  claiming the old occurrence or artifact realization.

## Invariants and evidence

| Property | Required gate |
| --- | --- |
| Command-level explanation consumes the exact owner-issued resource identity without deriving it from the parser token and performs no subject acquisition. | CLI composition test whose parser token deliberately differs from the registered canonical path, asserting the exact issued identity reaches Resource Explanation while all acquisition capabilities fail fast. |
| Exact-subject explanation preserves the command owner's resolved subject, parses and resolves once, acquires each required source at most once, and does not execute ordinary section producers. | First-adopter integration test with counting command-preprocessing, resolution, and acquisition collaborators, fail-fast ordinary producers, and a real `System.Text.Json` command. |
| Direct explanation does not serialize and parse a reusable reference. | Host-neutral composition test whose reference serializer and parser fail if called. |
| Zero, multiple, unavailable, and failed subject outcomes remain distinct and visible. | Cardinality and failure matrix over the first adopter. |
| `--explain` and `--references` admit only their declared subject, selection, traversal, presentation, and destination combinations; conflicting content operations fail before acquisition. | First-adopter CLI option-matrix test covering every accepted family, mutual exclusion, representative competing section/row/payload/Count operations, and fail-fast acquisition for every rejected combination. |
| `--references` observes the exact post-selection row sequence. | Row-selection integration tests covering predicate, order, head/tail, and absolute range selection. |
| `--references` emits the exact owner-issued reference attached to each selected row without reconstructing identity or resolving or acquiring the subject again. | First-adopter projection test with independently retained references, misleading and colliding display fields, and fail-fast subject-resolution and acquisition collaborators; each output record must equal its row's retained reference. |
| A row set without a complete owner-issued reference projection cannot fall through to ordinary output or omit unreferenceable rows. | First-adopter CLI test selecting an unsupported row set and asserting pre-output failure with no ordinary-content fallback. |
| Structured reference output contains one typed reference record per selected row in selected order. | Source-generated structured-output contract test over reordered and windowed authentic Member rows, including typed reconstruction rather than display-string inspection alone. |
| Projection cardinality equals selected-row cardinality and partial lists are never committed. | Mixed referenceable/unreferenceable row fixture asserting empty stdout plus absent-new and byte-identical-existing destination behavior before a successful neighboring write. |
| Owner-issued references distinguish overload, declaring Type, generic identity, and source context. | #7916 contract tests plus first-adopter integration cases. |
| Every emitted reference is accepted unchanged by `explain`. | CLI round-trip tests over authentic package and platform subjects. |
| CLI and Browser/Wasm consume equal contextual explanation Content for the same typed input. | Shared Content equality or serialization fixture in the Browser adoption slice. |

Until its named Release gate ships, each property is **unverified**.

No TLA+ model is required. The composition is a finite, single-operation
selection and projection pipeline with no retained state, concurrency,
replacement, retry, or scheduling semantics.

## Production adoption

1. Lock this composition contract and its owner boundaries.
2. Have Resource Explanation register the Member command-level product
   resource without changing subject acquisition semantics.
3. Have #7916 define the reusable reference and subject-affordance contracts,
   including shell-safe generic identity.
4. Add the host-neutral contextual-explanation selection and handoff
   substrate, with Member as the bounded first adopter.
5. Add Member Index `--references` as the first row projection and demonstrate
   unchanged consumption by `explain`.
6. Adopt the same composition one command owner at a time for Type, Library,
   Package, Findings, occurrences, and clusters.
7. Add a Browser/Wasm consumer over the shared contextual-explanation input
   and completed explanation envelope.
8. Update the shipped skill after production behavior exists so it teaches
   direct `--explain` first and reusable-reference composition second.

Steps 2 through 8 are separately owned implementation efforts. This document
does not authorize one PR to change all participating owners.

## Non-claims

This design does not:

- change Resource Explanation's acquisition-free installed-capability
  contract;
- add live-subject facts to `ResourceExplanationDocument`;
- define reusable-reference identity, grammar, parsing, reopening, versioning,
  or occurrence semantics;
- define Inspection Subject Navigation identity or routes;
- make a reusable reference equal to a Workspace occurrence, physical path,
  URL, digest, selector, acquisition coordinate, or portable scenario;
- redefine semantic row selection or the existing `--urls`, `--paths`,
  `--value`, or `--bare` projections;
- use `-o` / `--output` as a presentation or identity selector;
- treat identity as a `--format` value;
- infer explainability from parser metadata, display labels, rendered output,
  CLR reflection, or observed envelopes;
- require every command to support contextual explanation or references; or
- adopt every command family in one implementation change.
