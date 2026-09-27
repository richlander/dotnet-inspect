# Vocabulary mappings

## Status

This document is the normative owner for the host-neutral Vocabulary Mappings
pattern tracked by [#8593](https://github.com/richlander/dotnet-inspect/issues/8593).
The pattern and its Product Vocabulary first adoption are designed but not yet
implemented.

[Product Vocabulary](vocabulary.md) is the first adopter. Its existing CLI
output and Browser catalog prove the need and supply the first production data.
The later [JSON Schema Vocabulary Bindings](json-schema-vocabulary-bindings.md)
design consumes this pattern without extending its claim.

## Owner and exact claim

**Vocabulary Mappings** owns:

> Given owner-issued vocabularies, publish one immutable, exactly identified
> snapshot whose stable terms expose named maps to typed scalar values or
> stable terms. Preserve map direction, cardinality, coverage, and owner-issued
> order so hosts can display and compose the vocabulary without reconstructing
> identity or relationships from labels, field spellings, or collection
> position.

This owner defines:

- catalog, snapshot, vocabulary, term, and map identities;
- the distinction between scalar-value and term-reference maps;
- map direction, cardinality, and coverage;
- owner-issued vocabulary, map, term, and multi-value ordering;
- exact snapshot identity and replacement semantics;
- construction-time validation; and
- host-neutral lookup and resolution behavior.

It does not define:

- the represented domain's classifications, labels, defaults, relationships,
  or query meaning;
- executable query predicates, operators, ordering, or target-aware facets;
- structural document sections, fields, columns, or projection;
- JSON object properties, tuple positions, serialization direction, or JSON
  Schema;
- Browser controls, CLI syntax, rendering, localization, or persistence;
- inferring which vocabulary or map should drive a product feature;
- runtime plugin discovery or reflection-based catalog construction; or
- one global catalog into which every product identity must be registered.

Domain owners issue the facts. Vocabulary Mappings makes those facts
addressable and composable across hosts without becoming their semantic owner.

## Product need and production witness

At repository commit
[`0c4fd3c`](https://github.com/richlander/dotnet-inspect/tree/0c4fd3c5363297df0e0c1b2d846ea6243e92ee01),
`VocabularyCatalog` already composes product-owned query vocabularies for the
CLI and Browser. The Browser receives the same rows through `ListVocabulary`,
but the generic row payload becomes `unknown` in TypeScript. Inspect Web then
redeclares `StyleTier` and `StyleOption`, writes `isStyleTier` and
`isStyleOption` guards, and joins `option.tier` to `tier.id` in
`inspect-web/src/settings-panel.ts` and
`inspect-web/src/dotnet-inspect.ts`.

That path preserves the current experience but leaves the relationship
implicit:

- `csharp.style-choices.tier` is an ordinary string field even though it
  identifies one term in `csharp.style-tiers`;
- display identity depends on the Browser knowing that one vocabulary uses
  `title` while another uses `label`;
- a misspelled or stale target remains a valid string; and
- a new consumer must understand Product Vocabulary's row conventions before
  it can group the picker.

The first production outcome keeps the existing Settings experience while
removing those reconstructions. The product publishes one typed snapshot; the
CLI projects its established vocabulary document from that snapshot, and
Inspect Web groups choices through the declared `tier` term map.

Conceptually, the C# producer supplies:

```csharp
VocabularySnapshot snapshot = VocabularyCatalog.Snapshot;
VocabularyMap tierMap = snapshot.GetMap(
    "csharp.style-choices",
    "tier");
```

The generated Browser contract then supports the feature-bound TypeScript
consumer:

```ts
const choices = vocabulary.terms("csharp.style-choices");
const tiers = vocabulary.index("csharp.style-tiers");
const grouped = choices.groupBy(choice =>
  tiers.require(choice.term("tier")));
```

The names are illustrative. The contract is that the Browser consumes the
declared map and target identity, not that implementation must expose these
exact helpers.

## Basis

### Existing product owners

- [Product Vocabulary](vocabulary.md) owns the current stable query values,
  labels, fields, operators, accepted inputs, and host behavior. Its catalog is
  the first adopter, not a contract redefined here.
- [Schema Query](schema-query.md) owns addressable document structure and
  `DiscoveryDocument`. A structural item may later participate in a vocabulary,
  but its section and projection meaning remains with Schema Query.
- QuerySpace vocabularies own executable row keys, predicates, orders, and
  binding. A mapping to one of their identities does not make this owner an
  execution engine.
- [Inspection Capability Composition](inspection-capability-composition.md)
  provides the precedent that owner-issued forward declarations can support
  derived cross-host discovery without transferring authority to the composed
  graph.

### Analogous implementations

[W3C SKOS](https://www.w3.org/TR/skos-reference/) separates concept identity
from human labels and makes mappings explicit between identified concepts in
identified schemes. Its mapping properties also demonstrate that direction and
relationship strength cannot be recovered safely from a generic link.
Vocabulary Mappings adopts explicit schemes, terms, and directed map
definitions. It does not adopt RDF, global URIs, inferred inverse or transitive
relationships, or SKOS's knowledge-organization relation taxonomy because the
first product mappings are operational relationships such as style choice to
presentation tier.

[SSSOM](https://mapping-commons.github.io/sssom/dev/) represents a mapping as
an identified subject, predicate, and object inside an identified mapping set,
with optional provenance and justification. It reinforces that mapping
identity and predicate belong in the data rather than in host code.
Vocabulary Mappings keeps the explicit source, named map, target, and snapshot
identity. It omits per-edge confidence, curation provenance, and ontology
reasoning because current mappings are deterministic declarations from trusted
product owners.

These standards are evidence, not authorities for dotnet-inspect's product
model. The deliberate divergence is a smaller closed, ordered, typed snapshot
suited to NativeAOT and Browser/Wasm rather than a general semantic-web graph.

## Completed API boundary

The host-neutral operation returns:

```text
InspectionEnvelope<VocabularySnapshot>
```

`VocabularySnapshot` is a host-observable Document. The envelope carries the
same snapshot, owner-issued non-projectable Share outcome, and diagnostics to
the CLI and Browser. Hosts do not call catalog internals and construct their
own result wrappers.

The baseline operation returns the complete snapshot for one catalog. It has no
section selector, pagination, partial-reference mode, or "latest compatible"
negotiation. A later large-catalog owner may add bounded discovery without
changing the meaning of this complete operation.

The immutable snapshot supports exact vocabulary, term, map, and value lookup.
An unknown identity is a visible miss; lookup does not normalize case, search
labels, or choose a near match. Host search remains a projection over the
complete content.

## Discovery and semantic binding

The API is structurally self-describing, not semantically self-applying.

`csharp.style-choices` is a vocabulary identity.
`explicit-long-literal-cast` is a term identity within that vocabulary.
`tier` and `conflict_group` are map identities whose definitions describe
their targets, cardinality, coverage, and display metadata.

An unfamiliar consumer can enumerate vocabularies, display their terms, and
inspect their maps without prior knowledge. It cannot infer that
`csharp.style-choices` should drive a decompiler Settings control, that a
Boolean scalar should become a badge, or that selecting one term should invoke
a particular operation. Those are product-feature semantics rather than
vocabulary structure.

A feature-specific consumer therefore has an explicit semantic binding to the
stable vocabulary and map identities it uses. The simplest binding is
a priori knowledge in that feature's code. A broader capability owner may
instead issue a typed consumer binding, but Vocabulary Mappings does not invent
or discover that binding.

The first Browser adopter intentionally knows:

```text
choice vocabulary: csharp.style-choices
tier vocabulary:   csharp.style-tiers
tier map:           tier
conflict map:       conflict_group
```

This knowledge is part of the Settings feature contract. The Browser does not
duplicate the terms, labels, summaries, order, map targets, cardinality, or
coverage behind those identities. Product Vocabulary's `accepted_by` values
may help people and discovery tools locate a likely consumer, but they do not
authorize a host to synthesize that consumer or infer its behavior.

## Model

```text
VocabularySnapshot
  format version
  catalog identity
  snapshot identity
  ordered Vocabularies
    vocabulary identity
    display label
    summary
    ordered Map definitions
      map identity
      display label
      summary
      target: Scalar kind | Term-set reference
      cardinality
      coverage
    ordered Terms
      term identity
      display label
      optional summary
      map identity -> ordered values
```

The snapshot is complete for the catalog it names. It may compose declarations
from several domain owners, but composition copies neither domain logic nor a
second identity table. Each term and map value comes from its issuing owner.

### Identity

A catalog identity names one independently published vocabulary family. A
vocabulary identity is stable within that catalog. A term identity is stable
within its vocabulary. A map identity is stable within its source vocabulary.

The complete identities are therefore:

```text
Term = catalog + vocabulary + term
Map  = catalog + source vocabulary + map
```

Display labels, summaries, JSON keys, CLR names, enum ordinals, and collection
positions never participate in equality. Labels may change without renaming a
term. Two terms may have the same label.

An alias is not an untyped alternate spelling silently accepted by a host. A
domain that needs aliases declares an alias vocabulary and a map to canonical
terms, or adds a separately owned parser contract. This pattern does not make
labels into aliases.

### Display metadata

Every vocabulary and term has one required product-owned display label and may
have one summary. This standardizes the minimum information a host needs to
present an unfamiliar vocabulary. Additional user-visible or machine metadata
uses named maps.

Display metadata is plain owner-issued content. Localization, culture
selection, rich text, and host layout are outside this contract. A later
localization owner may supply localized labels without changing term identity.

### Scalar-value maps

A scalar-value map associates each source term with values of one declared
primitive kind. The initial closed kinds are text, integer, and Boolean because
they cover the first adopter. Adding a kind is a contract change, not permission
to carry arbitrary JSON.

Examples in Product Vocabulary include:

- accessibility term to `default` Boolean;
- style tier to `order` integer;
- style choice to `byte-divergent` Boolean; and
- vocabulary term to one or more accepted-input text tokens.

A scalar string that happens to equal a term ID remains a scalar. It becomes a
term relationship only when the map declares a term-set target.

### Term-reference maps

A term-reference map associates source terms with stable target terms. Its
target identifies an exact catalog snapshot and one vocabulary in that
snapshot. A target in the current snapshot may use a local reference; an
external target retains the complete snapshot identity.

The source is always the term that owns the map value. The target is always the
declared term set. No inverse, symmetry, hierarchy, equivalence, or transitive
closure is inferred. A domain needing an inverse or another relation publishes
another named map with that exact meaning.

The first term-reference map is:

```text
csharp.style-choices / tier
  -> csharp.style-tiers
  cardinality: exactly one
  coverage: complete
```

`accepted_by`, `option`, `value`, and `conflict_group` remain scalar maps in the
first adoption. Their strings are not promoted to term references until an
owner publishes the corresponding target vocabulary.

### Cardinality

Every map declares one of four cardinalities:

| Cardinality | Values for one source term |
| --- | --- |
| Exactly one | One |
| Optional one | Zero or one |
| One or more | At least one |
| Zero or more | Any number |

Cardinality is enforced during construction. A consumer never guesses it from
whether one example used a scalar or array.

Map values retain owner-issued order. Consumers preserve that order. The order
does not imply rank, preference, or hierarchy unless the map's domain-owned
meaning says so.

Several source terms may target one term, and one source term may target
several terms when its cardinality permits. Many-to-one and many-to-many maps
therefore use the same declared shape rather than separate container types.

### Coverage

Coverage distinguishes a complete map from a partial assertion:

- **Complete** means every source term has an explicit map entry. For a
  cardinality that admits zero values, an explicit empty sequence means that
  the owner asserts no targets.
- **Partial** means an omitted source term makes no mapping assertion.

Missing and explicitly empty are therefore distinct. Construction rejects a
missing entry in a complete map even when its cardinality permits zero targets.

The complete Product Vocabulary snapshot uses complete maps. Partial coverage
exists for later owners that publish deliberately bounded correspondences; it
is not a fallback for incomplete construction.

## Snapshot identity and replacement

The wire-format version and snapshot identity answer different questions:

- **Format version** says which Vocabulary Mappings document contract a reader
  must understand.
- **Snapshot identity** names the exact immutable catalog contents, including
  vocabulary, map, term, display, order, cardinality, coverage, and value
  changes.

The initial snapshot identity is a content identity over a deterministic
canonical projection of the typed snapshot body. Its initial spelling is
`sha256:` plus the lowercase hexadecimal SHA-256 digest. Consumers treat that
spelling as opaque. The snapshot identity itself is excluded from the hashed
body. The projection includes the format version, catalog identity, every
ordered declaration, and every value with explicit type, cardinality, and
coverage. Construction produces equal identities for equal typed inputs and a
different identity for any observable snapshot change. The digest is a
deterministic change identity, not an authentication or trust claim.

A consumer may cache a snapshot by exact identity. A producer that requires a
mapping supplied by another operation accepts the expected snapshot identity
and rejects a mismatch before publishing dependent data. It does not continue
under the latest catalog, reinterpret old values with a new catalog, or fall
back to labels.

This is exact replacement, not version negotiation. A newer snapshot governs
future requests. Values already retained with an older snapshot remain
interpretable only with that exact snapshot.

## Construction and validation

Snapshot construction is typed and all-or-failure. It rejects:

- missing or duplicate catalog, vocabulary, term, or map identities;
- blank required labels;
- a map value naming an undeclared map;
- a scalar value of the wrong primitive kind;
- a cardinality violation;
- a missing entry in a complete map;
- a duplicate value in one map entry;
- a dangling local vocabulary or term reference;
- an unavailable or mismatched external snapshot dependency; and
- a snapshot identity that does not match the constructed content.

Construction completes before publication. A host never receives a
success-shaped partial catalog.

Lookups are ordinal and case-sensitive. A host may offer case-insensitive
search over labels as presentation, but submitted identities must match
exactly.

## Product Vocabulary first adoption

The first adoption is the one-owner exception permitted by
[Design scope and composition](../design-scope.md#stage-implementation-after-locking-the-design).
It changes Product Vocabulary's internal source model while preserving its
owned behavior.

The shared operation returns
`InspectionEnvelope<VocabularySnapshot>`. Product Vocabulary supplies the
Content, an owner-issued non-projectable Share outcome for the static catalog,
and any cross-host diagnostics. The CLI and Browser consume that same baseline
envelope.

The adoption projects the existing catalog as follows:

| Existing fact | Vocabulary Mappings projection |
| --- | --- |
| Section ID | Vocabulary identity |
| Row `id` | Term identity |
| Section name and summary | Vocabulary display metadata |
| Row `label` or `title` | Term display label |
| Row summary, when present | Term summary |
| Other typed row fields | Scalar-value maps |
| `csharp.style-choices.tier` | Complete, exactly-one term-reference map to `csharp.style-tiers` |
| Existing section and row sequence | Owner-issued vocabulary and term order |

The compatibility projection retains the existing Product Vocabulary field
IDs, operators, selected-section behavior, JSON shape, and schema version. It
is a projection of the new snapshot, not a second catalog. In particular,
`tier` remains the same string in existing CLI JSON while the core snapshot
also authenticates it as a term reference.

Query operators are Product Vocabulary behavior, not general map metadata. Its
compatibility adapter retains the existing field-to-operator declarations and
combines them with the snapshot's term values. The adapter does not duplicate
term IDs, labels, summaries, order, defaults, or map targets.

The Browser replaces its generic `JsonElement` row handling and handwritten
`StyleTier`/`StyleOption` semantic twins with the generated Vocabulary
Mappings contract plus typed resolution under its explicit stable-ID binding.
The Settings panel continues to present the same groups, labels, summaries,
badges, conflicts, persistence, and selection behavior.

## Rendering and host responsibilities

The snapshot is data, not rendered output.

- The CLI continues to lower Product Vocabulary through its existing typed
  Markout view for Markdown, plaintext, table, TSV, JSONL, and projected JSON.
- Existing unprojected CLI JSON remains its approved typed compatibility
  projection.
- Browser/Wasm receives generated JSON-wire declarations and owns interaction
  and HTML presentation.
- Hosts may select, search, group, or omit terms for a specific experience.
  They do not rename identities, restate labels, invent mappings, or infer
  relationships from scalar equality.
- A feature host may know the stable vocabulary and map identities it consumes.
  It does not infer the feature's semantics from generic vocabulary metadata.

This design neither bypasses Markout for a new CLI format nor turns Markout
column labels into vocabulary identities.

## Failure and diagnostics

Construction defects throw before a snapshot becomes globally available.
Host requests carrying an unknown catalog, vocabulary, term, map, or stale
snapshot identity receive a typed rejection at their operation boundary.

A missing vocabulary service is a visible host failure. Browser code may show
an unavailable state, as it does today, but must not silently substitute a
compiled TypeScript catalog. CLI compatibility projection failure likewise
fails the command rather than rendering an empty vocabulary.

## Platform and trust boundary

The first catalog is trusted product-authored static data. There is no new
untrusted-internet input path and no claim about admitting third-party
vocabularies.

The model uses explicit immutable data and generated serialization. It requires
no reflection, dynamic assembly loading, runtime code generation, threads, or
host-specific types and remains usable from NativeAOT and single-threaded
Browser/Wasm under existing repository platform contracts.

## Pathological cases and gates

The implementation must gate:

- two source terms with equal labels retaining distinct identities;
- a complete exactly-one map with one omitted source term being rejected;
- an optional complete map distinguishing explicit empty from omission;
- a term map rejecting a scalar that merely resembles a target ID;
- a dangling local target being rejected;
- an external target built against another snapshot identity being rejected;
- one source term mapping to several ordered targets without target loss;
- equal snapshots producing equal identities and one changed label, order,
  cardinality, or target producing a different identity;
- Product Vocabulary's existing CLI formats and JSON remaining unchanged;
- Inspect Web grouping every style choice through the declared tier map;
- removal or corruption of the tier map causing the Browser production gate to
  fail rather than falling back to `choice.tier` string matching; and
- a generic catalog viewer rendering an unfamiliar vocabulary without claiming
  feature behavior, while the Settings consumer resolves only its explicitly
  bound vocabulary and maps.

The typed construction and identity cases belong in a focused
`DotnetInspector.Vocabulary` Release suite or the existing CLI suite until that
suite exists. Existing `VocabularyCommandTests` retain CLI compatibility.
`BrowserStyleOptionsTests`, strict generated-TypeScript compilation, and the
Inspect Web test/build gates own the Browser adoption.

## Delivery plan

This shared substrate has a counted three-step path to both production hosts:

1. **Focused design — current.** Lock this pattern and the bounded Product
   Vocabulary adoption.
2. **Core and CLI adoption.** Implement the typed snapshot, validation, exact
   identity, completed inspection envelope, Product Vocabulary projection, and
   unchanged CLI compatibility output. The CLI then consumes the snapshot as
   its one term-and-map source while its Product Vocabulary adapter retains
   query-operator behavior.
3. **Browser adoption and retirement.** Export the same snapshot through the
   generated facade, migrate Settings to typed map resolution under its
   explicit feature binding, and remove the Browser-local semantic row
   interfaces, guards, double-materialized `JsonElement` path, and any
   superseded internal `ListVocabulary` shape.

Step 3 resolves the allocation follow-up in
[#4494](https://github.com/richlander/dotnet-inspect/issues/4494) if its
before/after NativeAOT evidence demonstrates that the double materialization
has been removed without a regression. The existing CLI wire projection is a
public compatibility surface and remains intentionally; it is not a second
semantic catalog.

After these three steps,
[JSON Schema Vocabulary Bindings](json-schema-vocabulary-bindings.md) may bind
exact schema locations to terms in an exact Vocabulary Mappings snapshot.
Other catalogs adopt one owner at a time.

## Non-goals

- A universal ontology, knowledge graph, RDF store, or property bag.
- Built-in `exactMatch`, hierarchy, transitivity, or inverse reasoning.
- Cross-catalog fuzzy matching or automatic relationship discovery.
- Accepting labels, aliases, CLR names, or JSON keys as term identities.
- Automatic feature discovery, UI generation, or behavior inference from
  generic vocabulary metadata.
- Target-aware availability, counts, or rejection reasons.
- Structural schema, JSON Schema, query execution, or progressive transport.
- Arbitrary JSON values or owner-defined runtime types in the common map
  substrate.
- Localization or rich-text presentation.
- Third-party or package-provided vocabulary loading.
- Migrating structural, analysis, QuerySpace, or other catalogs in the first
  implementation.
