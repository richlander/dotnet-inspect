# View facet query composition

## Status and authority

Proposed thin composition map, tracked by
[#9101](https://github.com/richlander/dotnet-inspect/issues/9101). The product
owner explicitly requested this cross-owner plan after establishing three
connected experience goals:

- selectable views have stable registry identity separate from display names;
- semantic result shape supplies the omitted-format default; and
- explanation, tips, and selected-row references are available through one
  explicit companion-output family.

This map owns one composition claim:

> Given one resolved inspection subject and one or more exact registered View
> Facet identities, resolve the facets before acquisition, execute their
> owner-issued bindings, retain the result owners' semantic shapes, and let the
> CLI choose each admitted selection's natural presentation only when the user
> did not select a format. Discovery, execution, and explanation consume the
> same identities and descriptors; named companion projections preserve the
> selected semantic request and rows. No host derives identity, query behavior,
> result shape, reference, or presentation support from a display label or
> rendered section.

The map sequences focused owner adoptions. It does not redefine View Facet
identity, QuerySpace execution, result construction, output representability,
format syntax, or explanation internals. Each participating owner adopts the
composition in an independently reviewable slice.

### Coordination with active work

This composition plan does not wait for the entire contextual-explanation or
section-substrate programs:

- [#9060](https://github.com/richlander/dotnet-inspect/pull/9060) is the narrow
  short-only, zero-arity `-T` parser and post-success output migration. It may
  land independently. The later `-E` adoption replaces that public syntax while
  reusing its stream-separation, laziness, and positional-ownership evidence.
- [#8148](https://github.com/richlander/dotnet-inspect/issues/8148) owns the
  current reusable-reference proposal. The later `-E references` adoption
  preserves its owner-issued row references, order, cardinality, and reusable
  spelling while moving them from primary-output replacement to explicit
  companion output.
- [#9095](https://github.com/richlander/dotnet-inspect/pull/9095) moves section
  planning into L2 without changing section identity, selection, or output.
  Facet and facet-set adoption follows or integrates that extraction rather
  than competing with it.
- No current design owner issues a general semantic Hierarchy result shape.
  The shape-classification slice must first establish that focused contract, or
  consume it if a separate in-flight design establishes it before adoption.

The composition document can therefore land before either implementation PR
settles. The behavior-changing slices form candidates from their then-current
effective bases.

## Product outcome

The target CLI has one compact discovery, query, and companion-output model:

| Gesture | Role |
| --- | --- |
| `-D` | Discover the registered facets available for the command or resolved subject. |
| `-Q <facet>` | Execute one or more registered facets. |
| `-E` | Emit the complete default explanation for the executed request as a final `stderr` sidecar. |
| `-E tips` | Emit only the bounded related-gesture projection from that explanation. |
| `-E references` | Emit one owner-issued reusable reference per selected semantic row as the final `stderr` sidecar. |

`--explain` remains the terminal form: it returns explanation Content on
`stdout` instead of executing the selected facet. Top-level `explain` remains
the installed-resource and reusable-reference facade.

For example:

```console
dotnet-inspect library System.Text.Json \
  -Q library.integration-opportunities \
  --where "integration=integration.logging"
```

The exact Integration Opportunities ID above is illustrative. The View Facet
owner must mint an available ID under its append-only rules; the retired
`library.opportunities` ID cannot be reused.

The same query can request an explanation sidecar:

```console
dotnet-inspect library System.Text.Json \
  -Q library.integration-opportunities \
  --where "integration=integration.logging" \
  -E \
  > opportunities.tsv \
  2> opportunities.explain.md
```

Selected rows can instead retain ordinary output while exporting reusable
references:

```console
dotnet-inspect member JsonSerializer Serialize \
  --package System.Text.Json \
  -Q member.index \
  --where "<member predicate>" \
  -E references \
  > members.tsv \
  2> members.refs
```

Or explain the registered query without executing its producer:

```console
dotnet-inspect library \
  -Q library.integration-opportunities \
  --explain
```

The global path reaches the same explanation Content:

```console
dotnet-inspect explain \
  library/queries/library.integration-opportunities
```

The final resource-path grammar remains with Resource Explanation. The examples
show the required unchanged identity handoff, not a path registration decision.

## Composition

The composition preserves one identity from selection through explanation:

```text
command or resolved subject
  -> exact ViewFacetId selection
  -> View Facet resolution and applicability
  -> private owner-issued execution binding
  -> operation and row-query resolution
  -> owner-issued Content and semantic result shape
  -> explicit format, or natural presentation when omitted
  -> stdout

the same ViewFacetId and resolved request
  -> owner-issued explanation, gesture, or selected-row-reference facts
  -> terminal explanation on stdout, or one explicit companion on stderr
```

Equal display titles, section headings, result schemas, or query keys do not
establish identity. The join currency is the exact `ViewFacetId` returned by
the View Facet Registry and retained by every adopting binding.

## Facet identity and display

[View Facet Registry](view-facet-registry.md) remains the sole authority for:

- globally unique, permanent facet IDs;
- structural subject kind;
- stable purpose;
- product-owned title and summary;
- static and target-aware discovery;
- applicability and availability; and
- exact resolution outcomes.

The CLI uses the complete canonical View Facet ID as the durable selector:

```console
dotnet-inspect library System.Text.Json -Q library.references
```

Display titles remain first-class:

```text
ID:     library.references
Title:  References
```

Hosts render titles in headings, tables, controls, and prose. A CLI adoption
may continue accepting one exact, unambiguous title as a human convenience,
but copied commands, documentation, structured output, completion, and
diagnostics prefer the ID. Title lookup is host convenience, not a portable
alias and not another registry identity.

An issued ID never changes when its title improves. An ID is never generated
by lowercasing, slugging, or otherwise normalizing a title. Existing tombstones
remain known and cannot be repurposed for a current section with similar
display text.

### Facets and rendered sections

A selectable product facet and a rendered document section are separate:

```text
facet identity       library.references
display title        References
result shape         Table
Markdown lowering    one section containing a table
```

A facet may produce one section, several sections, a hierarchy, or a payload
that is not represented as a section. Conversely, a composed Document may
contain internal structural sections that are not independently executable
facets.

Every independently selectable current section must either:

- bind to one registered View Facet identity; or
- be retired as an independently selectable surface.

Structural sections that remain addressable for fields, columns, or document
projection retain Schema Query identity without automatically becoming facets.
This prevents the registry from treating every heading as an executable
operation.

### Categories and facet sets

Current authored section categories become authored facet sets. A set has a
stable machine identity and a display title, and contains exact View Facet
identities. The Section Model owner decides the focused identity contract and
migration for those sets.

The CLI may retain the recognizable `@` gesture:

```console
dotnet-inspect library System.Text.Json -Q @integration
```

The corresponding title may remain `Integrations`. Membership is always
explicit. A dotted or hyphenated facet ID does not enter a set because of a
shared string prefix.

## Facet execution

`-Q` selects registered View Facets for execution. It replaces the current
`-S`/`--select` role and the current `-Q` query-help role.

```console
dotnet-inspect library System.Text.Json \
  -Q library.integration-opportunities \
  --where "ecosystem=aspire"
```

Selection and row querying remain distinct:

- `-Q` chooses owner-issued Content;
- `--where`, `--order-by`, `--top`, `-n`, and `--rows` refine declared semantic
  row sets where the selected facet admits them;
- `--fields` and `--columns` project declared content;
- `--count` selects the admitted Count terminal; and
- `--format` selects presentation without changing the facet or rows.

Query Operation Infrastructure and QuerySpace retain operation identity,
portable intent, query vocabulary, row associations, effects, bounds,
completion, and execution. A View Facet binding may consume those descriptors
but never reconstructs them from section metadata.

`-Q` requires one or more exact selectors. Bare command behavior comes from the
command owner's registered default facet, not from a valueless `-Q`. `-D`
remains the compact inventory gesture.

## Semantic shapes and natural presentation

Result owners classify the semantic result before host presentation. A focused
shape-classification adoption adds Hierarchy to the owner-issued vocabulary
rather than inferring it from a tree renderer. The first shape-native defaults
are:

| Semantic shape | Natural CLI presentation |
| --- | --- |
| Document | Markdown |
| Table | TSV |
| Hierarchy | Tree |

The natural presentation is the omitted-format behavior, not the only
supported representation.

| Shape | Typical supported alternatives |
| --- | --- |
| Document | Markdown and structured JSON; tabular streams only after selecting one admitted Table result. |
| Table | TSV, Markdown table, pretty table, JSONL, and JSON. |
| Hierarchy | Tree, Markdown, and JSON; tabular streams only through an owner-defined faithful row projection. |

Graph, Scalar, Vector, exact payload, source, and other existing shapes retain
their owners and focused defaults. A Graph does not become a Hierarchy because
a host can draw it as a tree. An exact payload does not acquire document
decoration merely to participate in this composition.

Explicit format selection wins when the selected facet and complete result
shape support it. Unsupported facet-format pairs fail before acquisition.
`DOTNET_INSPECT_FORMAT` retains its position in the format precedence defined
by CLI Output Format and Destination; that owner decides its migration relative
to shape-native defaults.

Runtime cardinality never changes semantic shape:

- one Table row remains a Table;
- an empty Table remains a Table with its declared schema;
- a Document with one populated section remains a Document; and
- a one-node Hierarchy remains a Hierarchy.

Redirection and terminal detection do not change the default.

### Selection composition

The complete selected set has one owner-declared or composition-derived shape:

| Selection | Resulting shape |
| --- | --- |
| One Table facet | Table |
| Several facets declared as one homogeneous Table family | Table |
| Several independent Tables | Document |
| One Hierarchy facet | Hierarchy |
| One owner-declared forest retaining one hierarchy contract | Hierarchy |
| Several independent Hierarchies | Document |
| Mixed Tables, Hierarchies, fields, prose, or payloads | Document |

Composition never concatenates unrelated TSV schemas, chooses the first
compatible facet, drops incompatible facets, or lets a renderer change the
semantic selection. An authored facet set may declare a homogeneous result
only when its owner issues that combined contract.

If rows cannot be interpreted faithfully without neighboring headings,
provenance, explanatory fields, or independently meaningful result sets, the
result is a Document rather than a decorated Table.

## Primary subject defaults

[Primary Subject Views](primary-subject-views.md) remains the owner for the
Package, Library, Type, and Member default experience. Each adopting command
registers its default through one exact View Facet identity.

The target experience is:

| Command subject | Default Content | Shape | Natural presentation |
| --- | --- | --- | --- |
| Package | Selected Libraries, or the tool-package child population | Hierarchy | Tree |
| Library | Public-surface Type declarations | Hierarchy | Tree |
| Type | Member groups | Hierarchy | Tree |
| Member group | Exact Member declarations | Hierarchy | Tree |
| Exact Member | Signature or exact declaration result | Owner-declared leaf shape | Owner-declared |

Subject facts remain opt-in facets such as Package Info, Library Info, Type
Info, and exact-Member facts. Those facets are Documents unless their owners
declare a narrower semantic result. Selecting Info never repeats the child
population merely to preserve the former default document.

The default facet is discoverable and explainable. Bare invocation and exact
selection of that facet lower to the same typed request and result contract.

## Discovery

`-D` projects the registered facet catalog rather than treating display section
names as identity. Compact discovery includes at least:

```text
ID                           Title                       Shape       Default
library.references           References                  Table       TSV
library.types                Types                       Hierarchy   Tree
library.info                 Library Info                Document    Markdown
```

The exact issued IDs remain with the View Facet owner. The rows above state the
target information shape.

Target-free discovery is resource-free. Target-aware discovery consumes
already-authorized applicability and availability facts and preserves
Unavailable and Failed states. It does not execute a facet to learn whether it
exists.

Structural field, column, and item discovery remains with Schema Query. The CLI
may present those resources beneath the selected facet, but it must not merge
facet and schema identity or recover either from a rendered document.

## Explanation and companion output

Facet explanation composes existing owner-issued facts around exact
`ViewFacetId` identity. It can report:

- ID, title, summary, subject kind, and stable purpose;
- default or explicitly selected status;
- Content and result-contract identity;
- semantic shape and natural presentation;
- supported alternate formats;
- category or facet-set membership;
- query facets, operators, values, stages, orders, and effects;
- cost, capabilities, bounds, completion implications, and availability;
- related semantic operations; and
- applicable or unavailable host gestures with owner-issued reasons.

No single registry must own or duplicate all of those facts. Resource
Explanation joins descriptors issued by their existing owners and preserves
typed relationships among them.

The current `-Q` query-help mode retires. Query operators are learned through
the same explanation contract:

```console
dotnet-inspect library \
  -Q library.integration-opportunities \
  --explain
```

An executing request can obtain the same metadata as a sidecar:

```console
dotnet-inspect library System.Text.Json \
  -Q library.integration-opportunities \
  --where "integration=integration.logging" \
  -E
```

### Companion family

`-E` is the explicit companion-output family. Exactly one projection is
selected per invocation:

- bare `-E` requests the complete default contextual explanation for the
  resolved request;
- `-E tips` requests only the deterministic bounded executable-gesture
  projection; and
- `-E references` requests one owner-issued reusable reference for every
  selected semantic row, in selected order.

Complete explanation means complete at the explanation owner's documented
finite default extent. It does not mean recursive traversal, execution of Info
facets, exhaustive vocabulary enumeration, or authorization of unrelated
analysis. Tips do not invoke an independent tip registry.

References are not reconstructed from display cells, rendered output, command
text, or a second resolution. They consume the command owner's selected
semantic row sequence after facet, predicate, order, and row-window selection.
Each row contributes its already-issued reusable reference. A selected row set
without a complete reference projection fails before output rather than
falling through to ordinary Content or omitting rows.

Without `-E`, explanation descriptors, affordances, host bindings, ranking,
reference projection, and companion rendering remain unrealized beyond
ordinary option parsing and descriptor work already required by the selected
facet.

When any `-E` projection is requested, the CLI:

1. resolves and produces ordinary Content once;
2. constructs the selected companion from retained typed subject, facet
   identity, request, result facts, or row references without reacquisition;
3. validates and materializes both outputs before publication;
4. writes and flushes primary `stdout` completely;
5. writes the selected companion as the final successful `stderr` block; and
6. flushes `stderr` before returning success.

This is an application write-order guarantee. It does not claim that every
terminal, shell, pipe, remote transport, or independent file-descriptor reader
visually interleaves the streams in that order. Progress, warnings, and
diagnostics may precede primary output; the requested companion block is the
final successful `stderr` block.

Each companion block begins with a stable projection-specific marker and
extends to end of stream. Earlier diagnostics remain outside that frame, so a
redirected sidecar can locate the requested companion without treating a
warning as explanation, a tip, or a reference.

Full `-E` uses the explanation owner's Markdown lowering so redirection creates
an agent-friendly sidecar. It does not inherit the primary output's
`--format`. Structured explanation remains available through terminal
`--explain --format json`.

`-E references` uses one legible, shell-safe reusable reference per line. The
framed reference block preserves selected-row order and cardinality. Each
reference line is accepted unchanged by top-level `explain`. The block does not
inherit the primary output's format and contains no diagnostics or prose beyond
its framing marker.

Companion construction failure prevents primary output publication. A sink
failure after one stream has committed remains a visible non-success under the
owning output-sink contract; the process cannot roll back bytes already
accepted by an external stream.

`--explain -E` is rejected as duplicate full explanation. `--explain -E tips`
is useful and remains admitted. `--explain -E references` is rejected because
terminal explanation does not execute and select the ordinary semantic rows
whose references it would project. The focused CLI grammar adoption defines
how the exact `tips` and `references` tokens are consumed without stealing
unrelated positional input.

The current `--references` primary-output projection retires when
`-E references` reaches replacement parity. It does not remain as an alias.

## Browser/Wasm

Browser/Wasm consumes the same View Facet IDs, titles, summaries, applicability,
query descriptors, result shapes, and supported presentations. It binds them
to host-native controls rather than parsing CLI flags or commands.

Shape-native defaults inform initial presentation but do not force terminal
renderers into the Browser. A Table may become a grid, a Hierarchy a tree
control, and a Document a composed page while retaining the same facet and
result identities.

Browser explanation presents semantic affordances and native interactions. It
does not render CLI command text or interpret `-E`.

## Owner map

| Concern | Owner | Role in this composition |
| --- | --- | --- |
| Stable facet identity, title, purpose, subject kind, applicability, availability, and exact resolution | [View Facet Registry](view-facet-registry.md) | Supplies the identity joined across discovery, execution, defaults, and explanation. |
| Facet execution binding | Each facet and operation owner | Maps exact facet resolution to one owner-issued request and Content contract. |
| Operation and row-query capability | [Query Operation Infrastructure](query-operation-infrastructure.md), [Query Space Composition](query-space-composition.md), and row-query owners | Supply query terms, operators, stages, orders, effects, bounds, completion, and typed execution. |
| Structural sections and items | [Schema Query](schema-query.md) and [Section Model](section-model.md) | Retain document structure and migrate selectable sections/categories to facet bindings and facet sets. |
| Semantic Content shape and composition | [Output Shapes](output-shapes.md), the focused Hierarchy-shape adoption, Host-observable Content Kinds, and each result owner | Supply Document, Table, Hierarchy, and other result contracts without deriving them from format. |
| Explicit format and destination grammar | [CLI Output Format and Destination](cli-output-format-and-destination.md) | Retains `--format`, `--output`, precedence, and unsupported-pair admission. |
| Natural omitted-format policy | Output Shapes and Progressive Disclosure focused adoption | Maps admitted complete semantic shapes to Markdown, TSV, or Tree without changing Content. |
| Primary subject defaults and Info separation | [Primary Subject Views](primary-subject-views.md) and each command owner | Supply exact default and Info facet identities and subject-specific Content. |
| Installed and contextual explanation | [Resource Explanation](resource-explanation.md) and [Contextual Resource Explanation](contextual-resource-explanation.md) | Compose owner-issued descriptors and provide terminal explanation, tips, and reusable-reference companion projections. |
| CLI grammar and stream publication | CLI host | Owns `-D`, `-Q`, the `-E` companion family, display-title convenience, diagnostics, completion, and output ordering. |
| Browser interaction | Inspect Web owners | Bind the same descriptors to native controls and presentations. |

This map transfers none of those internal responsibilities.

## Adoption sequence

Each slice changes one focused owner and references this map.

1. **Lock this composition map.** Record the target experience, identity
   currency, handoffs, sequencing, and retirement boundaries without changing
   product behavior.
2. **Complete View Facet coverage.** Register every independently selectable
   current surface that will survive migration, retain tombstones, and map
   current sections to exact facet IDs. Record sections that will retire rather
   than receive facets.
3. **Issue facet-set identity.** Replace display-name category identity with
   stable authored sets whose membership references exact View Facet IDs.
4. **Issue and classify semantic shapes.** Establish the focused Hierarchy
   result contract, then have each result owner declare Document, Table,
   Hierarchy, or another existing shape. Define complete-selection composition
   and homogeneous Table families without using rendered output.
5. **Adopt shape-native CLI defaults.** Map Document to Markdown, Table to TSV,
   and Hierarchy to Tree when no explicit format or environment override wins.
   Preserve alternate Markdown, table, JSONL, and JSON representations where
   supported.
6. **Adopt explanation composition.** Project identity, Content, shape,
   formats, query capabilities, costs, effects, and related operations through
   Resource Explanation without duplicating owner descriptors.
7. **Adopt the `-E` companion family.** Replace the standalone tips projection
   with full contextual explanation and `-E tips`; replace primary
   `--references` with additive `-E references`. Preserve lazy absent-demand
   behavior, selected-row reference order and cardinality, atomic companion
   materialization, and the stdout-before-sidecar publication contract.
8. **Cut over CLI selection.** Reclaim `-Q` for exact facet execution, migrate
   `-S` selections and query-help examples, retain `-D` for discovery, and
   retire synthetic `Query: ...` companion sections.
9. **Adopt primary subjects.** Bind Package, Library, Type, and Member defaults
   to exact hierarchy facets and make Info explicit document facets.
10. **Adopt Browser/Wasm.** Consume the same IDs and descriptors through
    host-native controls and remove local catalogs that duplicate product
    registration.
11. **Retire superseded surfaces.** Remove `-S`, current query-help `-Q`,
    `--references`, compatibility-only section aliases, and scattered tip
    construction after replacement parity. Update CLI reference and shipped
    product skills in the behavior-changing slices.

The sequence may use a stack where one production consumer depends on the
preceding substrate. No slice adopts several command owners merely to complete
the inventory.

## Pathological cases

Focused adoptions must demonstrate:

1. **Equal titles, different identities.** Package, Library, Type, and Member
   facets titled `Metadata`, `Source`, or `Overview` resolve only through their
   exact IDs.
2. **Retired identity.** The tombstoned `library.opportunities` ID remains
   retired and is never rebound to the current Integration Opportunities
   section.
3. **Title correction.** Changing a title does not change selection,
   persistence, explanation paths, or Browser binding.
4. **Mixed facet set.** A set containing two unrelated Tables or a Table and a
   Hierarchy becomes a Document and defaults to Markdown rather than emitting
   concatenated TSV or selecting one member.
5. **Homogeneous Table family.** Several owner-declared compatible facets form
   one Table and default to TSV with one stable schema.
6. **Empty and singleton results.** Runtime cardinality does not change shape
   or default presentation.
7. **Hierarchy versus Graph.** A rooted occurrence hierarchy defaults to Tree;
   identity-preserving topology retains Graph semantics even when a tree
   renderer is available.
8. **Explicit alternate format.** A Table defaults to TSV but produces the same
   selected rows through explicit Markdown, pretty table, JSONL, and JSON
   where supported.
9. **Unsupported format.** A multi-result Document rejects TSV before
   acquisition rather than dropping context or choosing one Table.
10. **Ambiguous title.** Human-title lookup fails with exact ID candidates and
    never chooses by registration order.
11. **Query scope.** `--where` and ordering terms bind only to row sets declared
    by the exact selected facet; equal column labels do not transfer operators.
12. **Reference correspondence.** `-E references` emits exactly one
    owner-issued reference per selected semantic row in selected order, and
    every value is accepted unchanged by top-level `explain`.
13. **Incomplete references.** One unreferenceable selected row prevents both
    primary and companion publication rather than producing a partial reference
    file or ordinary-output fallback.
14. **Companion failure.** Explicit `-E` does not publish primary output when
    the selected companion cannot be constructed, and emits no plausible empty
    sidecar.
15. **Output ordering.** Successful primary output is flushed before the final
    companion block is written to `stderr`.
16. **Absent companion demand.** An invocation without `-E` realizes no
    explanation-only registry, affordance, reference projection, ranking, or
    companion-rendering work.

## Evidence plan

The implementation slices provide Release gates for their owned properties.
The composition requires, across those slices:

- registry totality from every surviving selectable surface to one exact View
  Facet ID;
- append-only ID and tombstone compatibility;
- exact-ID execution independent of title and section heading;
- explicit and default facet equivalence;
- semantic shape and complete-selection composition;
- natural format and explicit-format equivalence over the same selected
  Content;
- target-free discovery without acquisition;
- query explanation without producer execution;
- one-shot execution plus explanation without reacquisition;
- selected-row reference order, cardinality, round-trip acceptance, and
  all-or-nothing materialization;
- lazy absent-demand behavior;
- stdout-before-sidecar application write order;
- CLI and Browser descriptor parity; and
- current product-skill examples matching the cutover grammar.

Until those focused gates land, this document describes target behavior rather
than current product support. A Markdown-only plan provides no runtime,
performance, cross-platform, or NativeAOT evidence.

## Non-claims

This map does not:

- change or relax permanent View Facet compatibility;
- choose the final ID for every current section or category;
- make title lookup a portable alias contract;
- make every structural document section independently executable;
- infer category membership from ID prefixes;
- define operation, row, hierarchy, graph, or document internals;
- treat the current tree presentation mode as an already-issued Hierarchy
  result contract;
- make every facet a QuerySpace operation before its focused adoption;
- authorize acquisition, network, source, exhaustive, or expensive work from
  discovery or explanation;
- make a renderer define semantic shape;
- require every shape to support every format;
- turn Graph into Hierarchy or exact payload into Document;
- combine explanation with ordinary Content in one universal envelope;
- reinterpret reusable references as explanation prose or reconstruct them
  from rendered rows;
- guarantee cross-file-descriptor visual interleaving outside application
  write order;
- specify Browser layout or CLI completion implementation; or
- implement all owner adoptions in one PR.
