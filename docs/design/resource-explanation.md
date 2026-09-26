# Resource Explanation

## Status

This document is the normative design for **Resource Explanation**, the
host-neutral contract behind `dotnet-inspect explain`. Production implements
the complete Library structural domain plus the first bounded Query Space
adoption: Package Query document, route, operation facets, required-context
links, and current-host binding resources. Value, broader result-contract,
Browser/Wasm explanation, and subject-reference adoption remain staged.
It is the focused design for
[#7964](https://github.com/richlander/dotnet-inspect/issues/7964) under the
structural and query composition tracked by
[#7814](https://github.com/richlander/dotnet-inspect/issues/7814).

The design records both the implemented structural contract and its staged
adoption. Each later production adoption names and gates its own current
behavior.

## Owner and exact claim

**Resource Explanation** owns this exact claim:

> Given one exact product-resource path and a settled set of owner-issued
> explainable descriptors, resolve exactly one root resource, preserve its
> canonical identity and declared relationships, and produce one deterministic
> bounded explanation Document. Explanation performs no subject acquisition,
> executes no query or inspection plan, derives no meaning from display text,
> CLR reflection, or rendered output, and never turns an absent descriptor into
> success-shaped empty content.

This is one cross-cutting pattern. It owns:

- the distinction between owner identity and product-resource path;
- the shell-safe resource-path projection;
- exact path resolution and collection navigation;
- the host-neutral `ResourceExplanationDocument`;
- typed cross-owner relationship projection;
- deterministic bounded traversal;
- the semantic split between compact discovery and exact explanation; and
- the adoption boundary for CLI and Browser/Wasm consumers.

It does not own the structural, query, value, output-contract, or subject facts
that it composes.

## Product role

dotnet-inspect has several introspection surfaces with different jobs:

| Surface | Question |
| --- | --- |
| `-D` | Which structural resources are available here? |
| `-Q` | Which query capabilities does this route expose? |
| `vocabulary` | Which stable values may I supply? |
| `explain <search text>` | Which installed product resources might match this text? |
| `explain <resource path>` | What exactly is this one product resource, and how is it related to other resources? |
| Inspection commands | What does this subject contain or do? |

The top-level `explain` facade is not a more verbose form of every command. Its
search branch orients; exact Resource Explanation is the semantic drill-down
over one installed product contract. The compact surfaces remain the efficient
way to list and select within a known route; exact explanation resolves one
resource and composes the detail already issued by its owners.

The intended agent loop is:

```text
discover -> explain -> inspect -> refine -> compose
```

The shipped skill teaches that act. The installed binary supplies the
version-matched resource inventory and semantics.

## Basis and analogous systems

Resource Explanation uses several precedents by role, not as authorities:

| Precedent | Adopted idea | Deliberate difference |
| --- | --- | --- |
| `kubectl api-resources` and `kubectl explain` | Compact inventory and exact schema explanation are separate gestures. | dotnet-inspect composes several owner-issued descriptor families rather than treating one OpenAPI document as the complete semantic source. |
| GraphQL introspection | Stable typed relationships support tool-driven navigation. | Explanation is bounded product-contract introspection, not a remotely executable graph query language. |
| JSON Schema | Machine-readable wire shape can be linked from a contract. | Wire shape does not own product meaning, identity, ordering, completeness, effects, or correspondence. |
| CLI help systems | Installed behavior is version-matched and locally available. | Resource explanation is structured Content, not presentation prose or parser metadata. |

`kubectl describe` is not the primary analogy. It composes operational detail
about a selected live resource instance. dotnet-inspect's package, Library,
Type, Member, Diff, Depends, and other inspection commands already fill that
subject-oriented role.

## Terminology and owner map

### Product resource

A **product resource** is a stable, owner-issued contract element that can be
named without selecting an inspection subject. Examples include a structural
section, a query facet, a row space, or a value vocabulary.

An explainable resource has:

- one typed owner identity;
- one registered canonical product-resource path;
- one resource kind;
- owner-issued descriptive facts;
- zero or more declared typed relationships; and
- an explicit owner.

Being displayed in a table, emitted as a property, or represented by a CLR
type does not make a value an explainable resource.

### Query vocabulary and value vocabulary

**Query vocabulary** means facets, bindings, operators, named orders, scopes,
effects, terminals, and canonical query keys.

**Value vocabulary** means stable legal values from
`VocabularyDocument`. Query Space defines an optional opaque
value-vocabulary identity and separate query-local operand constraints.
Resource Explanation preserves those facts without claiming that the facet
accepts the whole vocabulary or that the query-local constraints define one
stable reusable subset. A future query-owner contract may issue that stronger
complete, constrained-subset, or open-domain distinction; Resource Explanation
may project it only after that owner adoption. It never copies a full value
catalog into every facet.

Unqualified *vocabulary* is avoided when the distinction matters.

### Resource path, inspection reference, address, and scenario

These values have separate jobs:

| Value | Job | Owner |
| --- | --- | --- |
| Product-resource path | Address an installed product contract for explanation. | Resource Explanation |
| Inspection reference | Carry a reusable subject or occurrence between product operations. | [#7916](https://github.com/richlander/dotnet-inspect/issues/7916) |
| Address | Select a subordinate IL or metadata point inside an already selected artifact. | The coordinate-child owner |
| Scenario | Carry broader Workspace and Share context. | Workspace and Share owners |

Resource Explanation does not parse an inspection reference as a resource
path, treat an artifact-internal address as a subject identity, or infer a
scenario from either value.

The future `explain` facade may accept a reusable inspection reference to
answer "which operations accept this item unchanged?" That adoption consumes a
typed affordance descriptor from #7916. This design owns only product-resource
path behavior and does not define the subject-ID grammar.

That owner must preserve the approved handoff invariant: one canonical subject
ID is a legible shell-safe unquoted argument, including generic definitions.
Generic arity belongs to definition identity; C# angle-bracket display syntax
and whole-value Base64 do not satisfy the canonical handoff requirement.

### Authority map

| Owner | Facts consumed by Resource Explanation |
| --- | --- |
| [Schema Query](schema-query.md) | Structural catalogs, categories, sections, items, membership, order, selection, and output capabilities from `DiscoveryDocument`. |
| [Query Space Composition](query-space-composition.md) | Query spaces, scopes, row spaces, facets, bindings, operators, terminals, effects, continuation acceptance, and opaque value-vocabulary or result-contract references from `QuerySpaceDescriptor`. |
| [Product Vocabulary](vocabulary.md) | Value-vocabulary identities, field schemas, legal operators, accepted query inputs, ordering, defaults, and stable values from `VocabularyDocument`. |
| [Host-observable Content Kinds](host-observable-content-kinds.md) | The semantic meaning of Document Content. |
| [Inspection Envelope](inspection-envelope.md) and [Output Shapes](output-shapes.md) | Completed service transport, Share, diagnostics, `(result_kind, schema_version)`, serializers, and output-contract identity. |
| Reusable Inspection Reference | Subject-reference identity, shell-safe subject IDs, unchanged handoff, and affordance descriptors. |

Resource Explanation may project those facts into its Document. It may not
repair, reinterpret, or supplement a missing owner fact from labels, rendered
output, reflection, or a neighboring descriptor.

## Resource identity and path projection

### Typed identity remains authoritative

Every adopted owner retains its typed identity. A structural section identity,
query-facet identity, and value-vocabulary identity do not become instances of
one universal semantic identity merely because `explain` can navigate among
them.

`ResourcePath` is a routing projection over those identities. The explanation
registry records:

```text
ResourcePathRegistration
  canonical path
  typed owner identity
  resource kind
  owner projector
```

The registration is explicit and statically enumerable. Reflection,
assembly scanning, parser-help scraping, rendered-document parsing, and
display-name normalization are not registration mechanisms.

### Adopted-domain totality

Each owner adapter declares one explicit adopted descriptor domain. The
declaration names:

- the owner and descriptor contract;
- the complete resource kinds adopted from that descriptor;
- the descriptor's authoritative resource and relationship enumerations;
- the typed direct facts selected for each resource-detail variant; and
- the canonical path registration source for every adopted resource.

The adopted boundary is a whole catalog or other owner-issued descriptor
domain, not an adapter-selected list of individual identities. The source owner
must expose complete resource and relationship enumeration for that domain; a
descriptor without such enumeration is not adoptable. Closed kind dispatch is
exhaustive, so a newly issued resource or relationship kind either receives a
projector or prevents construction.

Adapter construction is bidirectionally total within that boundary:

- every authoritative resource of an adopted kind has exactly one canonical
  registration and exactly one typed resource projection;
- every canonical registration resolves to exactly one authoritative resource
  in the declared domain;
- every authoritative relationship of an adopted kind produces exactly one
  typed graph edge, including an opaque external edge when the target owner has
  no registered path; and
- no projected resource or relationship exists without its owner-issued
  source.

Construction rejects missing resources, missing relationships, duplicate
projections, extra projections, and path registrations outside the declared
domain. This check runs against the completed owner descriptor, not a
hand-maintained expected-count snapshot.

Partial product adoption is expressed only by a different whole domain. The
first slice adopts the complete Library structural domain from its
`DiscoveryDocument`. The first query adoption adds the complete effective
Package Query operation-facet domain from its `QuerySpaceBinding`, together
with its document, route, required-context relation, and selected host
bindings. Product Vocabulary remains staged. Within each adopted domain, an
adapter cannot silently omit a newly added resource or owner-issued
relationship. Later owner adoptions add their complete declared domains rather
than cherry-picking resources by name.

### Canonical path grammar

A canonical path is one unquoted shell argument:

```text
segment *( "/" segment )
```

Each segment:

- is non-empty;
- begins with an ASCII lower-case letter or digit;
- thereafter contains only ASCII lower-case letters, digits, `.`, `_`, or
  `-`; and
- contains no whitespace, quote, backtick, variable marker, wildcard,
  redirection character, bracket, parenthesis, or path separator.

Representative paths are:

```text
library
library/sections
library/sections/reference-hierarchy
library/sections/reference-hierarchy/items/column/name
library/categories/dependencies
library/query-spaces/library-query
library/query-spaces/library-query/facets/references
vocabularies/csharp.body-kinds
```

`/` is the hierarchy separator so an existing stable owner ID such as
`csharp.body-kinds` can remain one legible segment. A product path is not a
filesystem path or URI.

New path segments use concise lower-kebab names. An owner identity that already
satisfies the segment grammar may be reused unchanged. Otherwise, adoption
registers a stable shell-safe segment explicitly. It must not derive one by
kebab-casing a display label at runtime.

Canonical registrations are unique under ordinal case-insensitive comparison.
The CLI may accept ASCII case differences for consistency with structural
selection, but it always emits the registered lower-case path. Aliases, when a
compatibility migration requires them, resolve to and emit the one canonical
path; aliases are not peer identities.

The Library structural adapter uses Markout's owner-issued section and item
machine keys, replacing the machine-key `_` separator with the path grammar's
`-` separator. It does not read rendered headings. Category segments and the
query-oriented Performance items that Schema Query injects outside Markout are
explicit registrations.

### Collections are resources

A collection path such as `library/sections` is an explainable navigation
resource. Resource Explanation owns a typed `NavigationCollectionIdentity`
containing the catalog identity, parent resource identity when present, and
collection kind. That identity is registered independently of the path. The
collection carries:

- its collection kind and owner;
- the count and canonical paths of direct members;
- member ordering issued by the source owner; and
- the direct properties that apply to the collection as a whole.

Collection membership does not copy member descriptors. A section belonging to
several categories remains one section resource with several incoming
membership relationships.

The path hierarchy is navigational, not semantic ownership. For example, a
row-query scope may be reachable from a structural section while remaining
owned by Query Space.

### Exact resolution

Explanation accepts one exact canonical path or registered alias. It does not
add wildcard, glob, prefix, fuzzy, or natural-language selection. Those
orientation jobs remain with compact discovery and Capability Catalog Search.
The top-level `explain` facade may dispatch an operand classified as search
text to that separate operation, but the Resource Explanation resolver never
receives or interprets the search text.

Resolution has three outcomes:

1. **Resolved** — one registration and descriptor produce one root resource.
2. **Unknown** — no registration matches; the operation fails with bounded
   canonical suggestions and produces no partial Document.
3. **Invalid registry** — duplicate, dangling, or owner-inconsistent
   registration, or incomplete adopted-domain coverage, prevents publication
   of the catalog.

A host facade may first probe whether an operand is an exact registered path
without computing unknown-path suggestions. A miss from that probe is not an
`Unknown` outcome: after the facade classifies the operand as an exact path, it
invokes full resolution to obtain the bounded suggestions.

A path-shaped value that resolves to several resources is an invalid registry,
not a runtime ambiguity to rank.

## Host-neutral model

### Resource Explanation Document

The completed Content value is:

```text
ResourceExplanationDocument
  schema version
  requested canonical path
  root typed identity
  ordered resources
    canonical path
    typed owner identity
    resource kind
    owner identity
    typed detail variant
  ordered relationships
    source typed identity
    relationship kind
    target typed identity
    target canonical path when registered
    source and target owners
  traversal receipt
    requested depth
    requested resource and relationship limits
    completed depth
    visited resource count
    emitted relationship count
    completeness
    truncation reasons
```

It is an immutable semantic
[Document](host-observable-content-kinds.md#document). The Document is not the
rendered Markdown document shape, parser help, or a dictionary of display
properties.

The completed host-neutral service returns
`InspectionEnvelope<ResourceExplanationDocument>`, preserving Content, Share,
and diagnostics. Registration of a stable `result_kind`, schema version, and
wire serializer remains an output-contract-owner adoption; it does not delay
use of the ordinary typed service envelope.

The requested root appears exactly once. Every relationship target is one of:

- an expanded resource in the Document;
- a registered resource outside the selected traversal bound, with its
  canonical path; or
- a non-navigable external owner reference whose owner has not registered an
  explainable path.

The last form preserves an opaque result-contract or value-vocabulary identity
without minting another owner's route. When that owner later registers the
same typed identity, registry composition adds the canonical path without
changing the source descriptor.

Construction rejects duplicate resource identities, duplicate canonical
paths, dangling expanded edges, inconsistent path-to-identity mappings, and
resource-detail variants that do not match the registered kind.

### Typed detail variants

Common framing supports graph navigation. Resource details remain a closed
discriminated set for the adopted owners. The target contract admits:

- structural catalog, category, section, and item details;
- query-space, query-scope, row-space, facet, operator, terminal, and effect
  details;
- value-vocabulary and value-field-schema details; and
- collection details.

One structural-item variant preserves the `DiscoveryResourceIdentity` owner,
section, item name, and opaque owner-issued `itemKind`. Its canonical path is:

```text
<section-path>/items/<item-kind-segment>/<item-segment>
```

`field`, `column`, `filterable`, `sortable`, `default-order`, `order-step`, and
later Schema Query item kinds use the same variant. Safe path segments are
explicit registrations; they are not derived from item-kind or item display
text.

The implementation contains navigation-collection and structural variants,
plus bounded inspection-document, host-neutral-route, operation-query-space,
query-facet, and consumer-binding variants for Package Query. Row-query,
value-vocabulary, broader envelope-contract, and subject-affordance variants
enter through focused versioned owner adoptions rather than one cross-owner
implementation sweep.

The variants preserve native types such as counts, booleans, operator IDs,
output-capability IDs, terminal kinds, and effect kinds. They do not lower
those values to display strings.

Adding envelope-contract or subject-affordance detail is a versioned adoption
of the explanation Content contract. Unknown future variants are not silently
lowered to an `other` property bag.

### Relationships

Relationships are typed navigational projections of owner-issued links.
Representative relationships include:

- collection membership;
- category membership;
- structural item ownership;
- eligible row space;
- facet binding;
- operator acceptance;
- terminal or effect membership;
- value-vocabulary reference; and
- result-contract reference.

The explanation relationship says how to navigate; the cited owner descriptor
remains authoritative for the relationship's semantics. Equal labels,
property names, field names, or CLR types never create an edge.

The first query adapter does not classify a value-vocabulary reference as
complete or constrained. Query-local operand constraints remain typed facet
facts. If the query owner later issues a stable value-domain relationship, the
explanation relationship preserves its discriminator, vocabulary identity,
and owner-issued constraint identity. An owner-issued open value domain may
carry bounded examples without manufacturing a vocabulary resource.

### Determinism

Resource order is breadth-first from the root, then source-owner declaration
order, then canonical path as a stable tie-breaker. Relationship order follows
source resource order, source-owner identity, source-owner relationship order,
relationship kind, target-owner identity, and target canonical path or typed
identity. This is a total order.

Equal registered descriptor inputs and equal traversal bounds produce equal
Content across CLI and Browser/Wasm. Hosts do not reorder the semantic
Document to match their visual layout.

## Bounded behavior

### Default explanation

Every host-neutral request contains resolved numeric limits for maximum depth,
resources, and relationships. Hosts may offer shorthands, but they resolve
those defaults before calling Resource Explanation. The completed Document
records all three limits.

Default explanation resolves depth zero:

- the root resource and its typed direct facts are complete;
- direct relationships are listed in deterministic order up to the explicit
  relationship limit, with a canonical target path when registered; and
- related resources are not expanded.

This keeps the common response concise while making the next exact gesture
copyable.

Owner-issued examples are direct facts only when they are bounded descriptor
content. Explanation does not enumerate a value vocabulary's complete rows;
the `vocabulary` command retains that bulk role.

Repeated owner collections are represented as relationships and are therefore
subject to the relationship limit. Direct detail fields contain scalar facts,
fixed product enums, or owner-issued bounded examples; they do not copy an
unbounded descriptor collection.

### Recursive explanation

Recursive explanation expands declared relationships only. The semantic
request contains explicit non-negative maximum depth and positive resource and
relationship limits. A CLI shorthand may supply documented finite defaults,
but the host-neutral request always contains the resolved numeric bounds.

Traversal:

1. starts with the root at depth zero;
2. visits each typed resource identity at most once;
3. records encountered relationships in total deterministic order until the
   relationship limit is reached;
4. expands a target only when the next depth and resource limit admit it; and
5. records depth, resource, and relationship truncation in the traversal
   receipt.

Cycles therefore remain visible as relationships when encountered before the
relationship bound, but cannot loop. No host may replace any bound with an
unbounded sentinel. A bounded Document reports `Complete` only when no
declared resource or relationship within the requested depth was omitted.

### Capability and resolved-plan explanation

The first contract explains installed capability descriptors. It performs no
acquisition and accepts no inspection subject.

Query Space separately exposes a resolved structural plan containing normalized
operands, row-intent associations, semantic stages, effects, terminal
requirement, and source-delegation boundary. A future plan-explanation adoption
may project that settled value through a distinct typed request. It must not
mutate the capability Document, execute the plan, or treat target-effective
availability as an installed capability fact.

## Owner-specific composition

### Structural resources

`DiscoveryDocument` is the complete structural input. Resource Explanation
uses its catalog identity, canonical resource identities, membership, order,
item kinds, and typed output capabilities.

Structural adaptation must preserve:

- category and section identity as separate kinds;
- one section identity across all category memberships;
- every item identity as the owning section plus opaque owner-issued item kind
  plus name;
- sections with no item-level vocabulary; and
- addressed resource identity separately from compact projected rows.

A `filterable` or `sortable` item remains a Schema Query structural-discovery
fact. It does not become a Query Space facet, binding, operator, stage, or
effect unless a separately adopted `QuerySpaceDescriptor` supplies that typed
resource and correspondence. Likewise, a column named `References` does not
become a query facet named `references`; a shared label establishes nothing.

Output capabilities appear as typed direct facts. Explanation of a format's
global behavior requires a separately owner-issued output-format descriptor;
Resource Explanation does not infer it from the capability enum.

### Query resources

`QuerySpaceDescriptor` is the complete query-capability input. Explanation
preserves the distinction among:

- operation and row query scopes;
- row spaces and structural sections;
- facets and their bindings;
- operators and named orders;
- Rows and exact Count terminal requirements;
- effects and continuation acceptance; and
- opaque external value-vocabulary and result-contract references.

A structural section may present a row space without owning it. Operation and
row facets with equal display labels remain distinct resources when their typed
identities, scopes, stages, or effects differ.

Resource Explanation never reconstructs a facet from a schema column, a
rendered property, a parser option, or a command delegate.

### Value-vocabulary resources

`VocabularyDocument` is the complete value-vocabulary input. Explanation
preserves:

- stable vocabulary identity;
- summary and accepted query-input identities;
- field schema and legal operators;
- value count, ordering, and defaults; and
- owner-issued bounded examples.

The explanation of a vocabulary is not its full value listing. It points to
the ordinary `vocabulary` command for bulk rows. The initial query adapter
preserves an optional opaque value-vocabulary identity and query-local operand
constraints as separate facts. It does not claim whole-vocabulary acceptance
or a reusable subset identity. A later query-owner adoption may issue a typed
complete, constrained-subset, or open-domain relationship for explanation to
preserve.

### Output-contract resources

Query Space may expose an opaque result-contract identity. Resource
Explanation can list that relationship before the contract itself is
explainable.

A later envelope-contract catalog must own exact contract identity, Content
Kind, `(result_kind, schema_version)`, serializer, JSON Schema, producers, and
compatibility. Its adoption adds a typed resource detail and path. Resource
Explanation does not synthesize that catalog from generic arguments, source
generation contexts, static registrations, or observed envelopes.

JSON Schema can describe a wire shape. It cannot replace owner-issued product
semantics such as identity, ordering, completeness, provenance, effects, or
correspondence.

## Host projections

### CLI

The first production gesture is:

```console
dotnet-inspect explain library/sections/reference-hierarchy
```

Neighboring gestures include:

```console
dotnet-inspect explain library/categories/dependencies
dotnet-inspect explain \
  library/query-spaces/library-query/facets/references
dotnet-inspect explain vocabularies/csharp.body-kinds
```

CLI parsing produces one typed `ResourcePath` and one resolved traversal
request. The command obtains the completed explanation envelope before
presenting its Content and diagnostics.

The top-level `explain` facade also accepts reusable inspection references and
capability-search text under
[Contextual Resource Explanation](contextual-resource-explanation.md).
Exact registered paths and aliases select this operation first. After
reusable-reference shape recognition, any remaining canonical multi-segment
`ResourcePath` also selects this operation and preserves its exact unknown
outcome. An unregistered canonical single segment such as `literal`, or a
noncanonical slash-bearing value such as `https://`, selects capability search
because `ResourcePath` grammar alone is intentionally broader than the
facade's exact-path discriminator.

Human output lowers the Document through a typed Markout view. `--json`
serializes the same Content contract with source-generated metadata; the final
general output-format spelling remains owned by its focused work. Human
headings and tables are not machine identity.

Diagnostics expose canonical paths that can be copied unchanged into
`explain`. Compact `-D`, `-Q`, and `vocabulary` output should do the same when
their owning adoptions can preserve current concise shapes.

After Library structural explanation presents the same Formats facts from
`DiscoveryDocument`, the temporary Library `-D --details` bridge is removed.
`explain` does not become another `--details` flag.

### Browser/Wasm

Browser/Wasm consumes the same completed
`InspectionEnvelope<ResourceExplanationDocument>`.
It may render links, breadcrumbs, expandable relationships, and
purpose-specific controls, but it does not reconstruct the graph from CLI
text or restate owner catalogs in TypeScript.

The shared implementation targets NativeAOT and single-threaded Browser/Wasm
and uses explicit static registrations and source-generated serialization.
The adoption plan proposes partial absence coverage through Browser and
NativeAOT build-and-execution gates plus registry-construction tests; it does
not claim a repository-wide reflection, Roslyn, or assembly-loading absence
gate.

### Skill

The shipped skill retains:

- command-family orientation;
- acquisition and safety boundaries;
- output and envelope semantics;
- identity distinctions;
- the `discover -> explain -> inspect` operating loop; and
- a small set of end-to-end recipes.

It does not inventory every section, facet, operator, format combination,
value vocabulary, or result contract. Those details belong to the installed
descriptor set and `explain`.

An agent acceptance scenario starts with only that shallow skill and an
unfamiliar task, then measures whether discovery and explanation lead to a
valid command without guessing semantic identities.

## Demo scenario

`System.Text.Json@10.0.0` motivates the first production flow:

1. Use Library structural discovery to find `Reference Hierarchy`.
2. Copy its canonical resource path into `explain`.
3. Read its fields, columns, output capabilities, and declared query
   relationships without acquiring a Library.
4. Run the ordinary Library inspection against
   `System.Text.Json@10.0.0`.
5. Follow one emitted related path to explain a query facet or value
   vocabulary.

The neighboring missing-target case runs the same structural explanation
without a package, file, or platform selection. It must produce equal Content,
proving that installed capability explanation does not acquire.

The pathological graph includes:

- one section in several categories;
- one section column and one query facet with the same display label;
- operation and row facets with the same display label but different effects;
- an opaque value-vocabulary reference and query-local operand constraints;
- a later owner-issued complete, constrained-subset, or open value domain;
- a section with no item vocabulary;
- a category whose complete capability set differs from one member;
- an authoritative resource added without a path registration;
- an authoritative relationship omitted by its adapter;
- a cycle of cross-owner navigational relationships; and
- valid expansions truncated by depth, resource, and relationship limits.

## Invariants and evidence

Implementation slices must name Release gates for the following properties:

| Property | Required gate |
| --- | --- |
| Canonical paths are shell-safe, unique case-insensitively, and registered rather than derived from labels. | Registry-construction tests over every shipped registration. |
| Each adopted descriptor domain has bidirectionally total resource registration and relationship projection. | Adapter-construction tests comparing the complete real owner enumerations with projected identities and edges, plus omission, duplicate, and extra-projection contract fixtures. |
| Exact resolution returns one root or a visible failure with no partial Document. | Resolver contract tests including unknown paths and bounded suggestions. |
| Structural, query, and value details preserve owner-issued typed identities and native values. | Adapter contract tests against real owner descriptors. |
| Equal labels do not create resource identity or relationships. | Collision fixture spanning structural and query owners. |
| Recursive traversal visits each identity once, preserves encountered cycle edges, and reports depth, resource, and relationship truncation. | Cyclic and permuted-input graph fixtures exercising every bound and the total ordering key. |
| Capability explanation performs no acquisition. | Host-level missing-target test with acquisition and planning services replaced by fail-fast recording fakes, asserting no capability was requested. |
| CLI and Browser/Wasm receive equal Content for equal descriptor inputs. | Shared Content equality or serialization fixture exercised by both hosts. |
| Structured output uses source-generated serialization and static registrations on supported hosts. | Serializer round-trip and registry-construction tests plus partial absence coverage from NativeAOT and Browser build-and-execution gates. |
| Library explanation presents Formats before `--details` is removed. | CLI before/after compatibility test in the retirement slice. |
| A shallow skill leads an agent to one valid unfamiliar query. | Reproducible agent E2E harness recording first-valid-command rate, failed attempts, tokens, latency, and unsupported inferences. |

The registry tests are product-contract tests, not defenses against hostile
in-repository callers. The security boundary remains untrusted external data.
Paths and owner descriptors are product-authored installed data; future
subject-reference explanation must apply the containment contract owned by
that external-input path.

Until a production slice supplies a listed gate, the corresponding
implementation property is **unverified**.

## Production adoption

1. **Complete:** lock this owner, path contract, explanation Document, and host
   boundaries.
2. **Complete:** add the host-neutral adopted-domain manifest, total registry,
   exact resolver, structural detail variant, and envelope-returning Resource
   Explanation service.
3. **Complete:** add the CLI `explain` facade and Library structural adoption,
   including structured Content JSON.
4. Add one Browser/Wasm consumer of the same structural explanation envelope.
5. Remove Library `-D --details` after equivalent Formats explanation ships.
6. **In progress:** Package Query adopts operation query-resource variants,
   canonical paths, required-context links, and its current-host production
   binding. Remaining Query Space owners and row-query resources stay staged.
7. Let Product Vocabulary adopt value-vocabulary variants and typed links.
8. Register the stable explanation result contract; then let the focused
   envelope-contract catalog adopt explanation paths and machine-readable
   schemas.
9. Let #7916 adopt reusable subject references and affordance explanation.

Steps 6 through 9 are separately owned adoptions. They do not block the
independently coherent structural explanation slice.

## Non-claims

This design does not claim:

- ownership of structural discovery, Query Space, value vocabularies,
  Content Kind, envelope transport, output-format semantics, or reusable
  inspection references;
- one universal semantic base class for all product resources;
- an untyped property dictionary or arbitrary extension bag;
- that a section owns every query capability it presents;
- that fields, columns, row properties, labels, or CLR types imply facets;
- subject acquisition or query execution during capability explanation;
- target-effective availability in the first contract;
- wildcard, fuzzy, or natural-language resource resolution;
- complete value catalogs embedded in facet explanations;
- JSON Schema embedded in every envelope;
- resolved-plan explanation in the first implementation;
- a rename of `library coordinate`.

## Rejected alternatives

### Make `-D --details` the permanent experience

Rejected. It keeps accumulating unrelated columns in compact discovery,
cannot compose query and vocabulary owners cleanly, and does not provide one
stable exact-resource navigation model.

### Put all explanation facts in `DiscoveryDocument`

Rejected. Query spaces, facets, value vocabularies, result contracts, and
subject affordances have separate owners. Copying them into structural
discovery would blur authority and make descriptors drift.

### Derive paths from display labels

Rejected. Labels contain spaces and punctuation, may change for presentation,
and can collide across owners. Canonical path segments are explicit product
API.

### Use dotted paths

Rejected as the canonical separator. Existing stable IDs such as
`csharp.body-kinds` already contain dots. `/` preserves those IDs as legible
segments without introducing quoting or escaping.

### Serialize owner descriptors as one arbitrary object graph

Rejected. It leaks implementation composition, weakens schema evolution, and
encourages reflection or untyped property bags. Explanation Content uses a
closed typed graph projection.

### Explain live subjects by default

Rejected. Installed capability explanation must remain deterministic,
acquisition-free, and available before the user chooses a package, file,
platform, or Workspace subject. Subject affordance and resolved-plan
explanation are separate typed operands.
