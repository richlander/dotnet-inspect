# Resource Explanation

## Status

This document is the normative design for **Resource Explanation**, the
host-neutral object model behind every complete `explain` result. Production
implements an earlier installed-resource slice for the complete Library
structural domain plus the first bounded Query Space adoption: Package Query
document, route, operation facets, required-context links, and current-host
binding resources. The target object model defined here generalizes that
slice so installed resources, command resources, and detached resolved
subjects can use one typed explanation contract. Migration of the current
closed detail variants remains staged. Member now uses one installed command
resource or one detached resolved-subject snapshot in the common Document;
Value, broader result-contract, Browser/Wasm, and subject-reference adoption
remain staged.
It is the focused design for
[#9324](https://github.com/richlander/dotnet-inspect/issues/9324), continuing
the installed-resource lineage from
[#7964](https://github.com/richlander/dotnet-inspect/issues/7964) under the
structural and query composition tracked by
[#7814](https://github.com/richlander/dotnet-inspect/issues/7814).

The design records both the implemented structural contract and its staged
adoption. Each later production adoption names and gates its own current
behavior.

## Owner and exact claim

**Resource Explanation** owns this exact claim:

> Given one exact owner-issued explainable resource and its settled schema and
> resource snapshot, compose it with the installed explanation catalog and
> produce one deterministic bounded explanation Document that preserves typed
> facts, relationships, identities, and available navigation. Explanation does
> not itself acquire a subject or execute a query or inspection plan, derives
> no meaning from display text, CLR reflection, or rendered output, and never
> turns an absent declaration, invalid value, unresolved required relationship,
> or unavailable resource into success-shaped empty content.

This is one cross-cutting pattern. It owns:

- the distinction between owner identity and product-resource path;
- the explanation schema, resource snapshot, catalog, and Document object
  model;
- the typed value-shape and relationship vocabulary required to explain data
  without importing every concrete domain type;
- the shell-safe resource-path projection;
- exact path resolution and collection navigation;
- one host-neutral complete explanation Document for installed and contextual
  resources;
- typed cross-owner relationship projection;
- deterministic bounded traversal;
- the separation between semantic object model and output lowering;
- the semantic split between compact discovery and exact explanation; and
- the adoption boundary for CLI and Browser/Wasm consumers.

It does not own the structural, query, value, output-contract, reusable
reference, or subject facts that it composes. It also does not decide which
common declaration types move into `QuerySpace.Primitives`; the
[QuerySpace library boundary](query-space-library.md) owns that physical and
API placement after this object model identifies the required shapes.

## Product role

dotnet-inspect has several introspection surfaces with different jobs:

| Surface | Question |
| --- | --- |
| `-D` | Which structural resources are available here? |
| `-Q` | Which query capabilities does this route expose? |
| `explain vocabularies` | Which stable values may I supply? |
| `explain <search text>` | Which installed product resources might match this text? |
| `explain <resource path>` | What exactly is this one product resource, and how is it related to other resources? |
| `<command> --explain` | Make complete or projected explanation the primary result on `stdout`. |
| `<command> -E` | Preserve ordinary output and append complete or projected explanation on `stderr`. |
| Inspection commands | What does this subject contain or do? |

The top-level `explain` facade is not a more verbose form of every command. Its
search branch orients; exact Resource Explanation is the semantic drill-down
over one explainable resource. The compact surfaces remain the efficient way
to list and select within a known route; exact explanation resolves one
installed or detached contextual resource and composes the detail already
issued by its owners.

`--explain` and `-E` are placement gestures over one explanation projection
namespace. Bare selects the complete explanation Document. `.tips` selects a
bounded host-gesture projection over owner-issued affordances. The approved
successor to the former `--references` projection is singular `.reference`,
which selects reusable owner-issued inspection references. The
[Contextual Resource Explanation](contextual-resource-explanation.md) owner
defines their admission, stream, ordering, and direct-subject handoff. This
owner defines the complete Document they can select and the typed relationships
from which host projections may be derived.

This design records the approved singular target but does not admit that
spelling by itself. Contextual Resource Explanation and the dotted-gesture
grammar currently reserve plural `.references`; their focused #7916 adoption
must replace that reservation, diagnostics, and examples before `.reference`
is available in production.

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
| OpenAPI | Reusable schemas, explicit operation inputs and outputs, and named references make a large installed contract navigable. | QuerySpace and semantic owners retain product meaning; Resource Explanation is not an HTTP API description, does not use HTTP verbs or status codes as semantics, and does not make a wire schema authoritative. |
| GraphQL introspection | Stable typed relationships support tool-driven navigation. | Explanation is bounded product-contract introspection, not a remotely executable graph query language. |
| JSON Schema | Machine-readable wire shape can be linked from a contract. | Wire shape does not own product meaning, identity, ordering, completeness, effects, or correspondence. |
| HAL-JSON | A resource representation can carry direct state, navigable relationships, and bounded embedded resources in an agent-familiar form. | HAL terms do not define the object model. A future HAL-JSON projection would be one lowering of typed facts, relationships, and expansion, not their semantic owner. |
| CLI help systems | Installed behavior is version-matched and locally available. | Resource explanation is structured Content, not presentation prose or parser metadata. |

`kubectl describe` is not the primary analogy. It composes operational detail
about a selected live resource instance. dotnet-inspect's package, Library,
Type, Member, Diff, Depends, and other inspection commands already fill that
subject-oriented role.

## Terminology and owner map

### Explainable resource

An **explainable resource** is one owner-issued contract or detached resolved
subject that the owner admits to the explanation object model. Installed
examples include a structural section, query facet, row space, value
vocabulary, inspection route, or result contract. Contextual examples include
one resolved Member or other exact subject after its command owner has
completed the required resolution.

An explainable resource has:

- one owner-issued typed identity;
- one owner-issued explanation key projected from that identity;
- one owner-issued resource-type identity;
- one explanation schema that declares its available facts and relationships;
- one detached resource snapshot whose values conform to that schema;
- zero or more registered public addresses, such as a product-resource path
  or reusable inspection reference, when the responsible owner has issued
  them; and
- an explicit semantic owner.

An installed resource can be entered into the reusable catalog without subject
acquisition. A resolved resource is supplied as a detached snapshot by the
command or subject owner after resolution; Resource Explanation does not
reacquire it or require a public reusable reference for the direct handoff.

Being displayed in a table, emitted as a property, represented by a CLR type,
or observed in rendered output does not make a value an explainable resource.

### Schema, database, and currency

Resource Explanation serves three related product roles:

- **Schema.** Owner-issued resource types declare stable fact identities,
  value shapes, cardinalities, and relationship identities.
- **Database.** Host composition validates and combines the schemas, installed
  resources, addresses, and cross-owner relationships into one immutable
  catalog that exact explanation and capability search can consume.
- **Currency issuer.** The composed catalog publishes canonical
  product-resource paths, schema identities, fact identities, and relationship
  identities. Adjacent owners publish their own reusable inspection references
  and domain identities; explanation preserves and relates them rather than
  replacing them.

The common vocabulary is the grammar of typed data, not the complete universe
of product types. A domain owner may describe a `MemberAnchor`, Finding,
artifact identity, or other domain value through an owner-issued shape without
moving that concrete type into the common declaration floor.

### Query vocabulary and value vocabulary

**Query vocabulary** means facets, bindings, operators, named orders, scopes,
effects, terminals, and canonical query keys.

**Value vocabulary** means stable legal values from a host's composed
Product Vocabulary snapshot. Query Space defines an optional opaque
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
| [Section Shapes](section-shapes.md) and [Section Cardinality](section-cardinality.md) | Owner-issued Table, Hierarchy, or Text shape and scalar or inventory cardinality for each adopted section. |
| [Query Space Composition](query-space-composition.md) | Query spaces, scopes, row spaces, facets, bindings, operators, terminals, effects, continuation acceptance, and opaque value-vocabulary or result-contract references from `QuerySpaceDescriptor`. |
| [QuerySpace Library Boundary](query-space-library.md) | Physical and API placement of the general declaration vocabulary after this owner defines the required object-model shapes. |
| [Product Vocabulary](vocabulary.md) | Value-vocabulary identities, maps, accepted query inputs, ordering, defaults, and stable values from the host's composed snapshot. |
| [Contextual Resource Explanation](contextual-resource-explanation.md) | Command-resource or resolved-subject selection, direct typed handoff, projection admission, primary-versus-companion placement, and host gesture binding. |
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

For composition, each owner issues one schema-conforming explanation key:

```text
ExplanationResourceKey
  owner domain identity
  resource-type identity
  owner-issued identity value
```

The identity value uses an owner-declared scalar, record, or closed-choice
shape with canonical equality and ordering. It cannot be ordered-many. The
owner constructs the key from its native identity once; Resource Explanation
does not reconstruct it from a label, path, serialized document, hash, CLR
type name, or neighboring resource.

The key is the explanation graph's join currency, not a replacement semantic
identity. Metadata can retain `MemberAnchor`, Query Space can retain a facet
identity, and Findings can retain a Finding identity while each supplies the
key projection required for cross-owner composition. Code that already holds
the native identity continues to use it; hosts and the explanation catalog use
the key when they need one heterogeneous graph or wire value.

`ResourcePath` is a routing projection over those identities. The explanation
registry records:

```text
ResourcePathRegistration
  canonical path
  typed owner identity
  resource-type identity
  owner snapshot projector
```

The registration is explicit and statically enumerable. Reflection,
assembly scanning, parser-help scraping, rendered-document parsing, and
display-name normalization are not registration mechanisms.

### Adopted-domain totality

Each owner adapter declares one explicit adopted descriptor domain. The
declaration names:

- the owner and descriptor contract;
- the complete resource types adopted from that descriptor;
- the descriptor's authoritative resource and relationship enumerations;
- the schema and snapshot projection for each adopted resource type; and
- the canonical path registration source for every adopted resource.

The adopted boundary is a whole catalog or other owner-issued descriptor
domain, not an adapter-selected list of individual identities. The source owner
must expose complete resource and relationship enumeration for that domain; a
descriptor without such enumeration is not adoptable. Schema and relationship
dispatch is exhaustive, so a newly issued resource type, fact, or relationship
either receives a projection or prevents construction.

Adapter construction is bidirectionally total within that boundary:

- every authoritative resource of an adopted kind has exactly one canonical
  registration and exactly one typed resource projection;
- every canonical registration resolves to exactly one authoritative resource
  in the declared domain;
- every authoritative relationship of an adopted kind produces exactly one
  typed observation, preserving a targetless admitted outcome or every
  available graph target whose owner has issued an explanation resource type
  and key; and
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
bindings. Product Vocabulary adopts its complete composed domain under
[Value-vocabulary resources](#value-vocabulary-resources). Within each
adopted domain, an adapter cannot silently omit a newly added resource or
owner-issued relationship. Later owner adoptions add their complete declared
domains rather than cherry-picking resources by name.

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
- the count of direct members;
- one ordered membership relationship whose emitted targets are subject to the
  request's target limit;
- member ordering issued by the source owner; and
- the direct facts that apply to the collection as a whole.

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

### Object-model layers

The object model has four layers with different lifetimes and owners:

```text
ExplanationSchema
  owner-issued data-shape declarations
  owner-issued resource-type declarations
    fact declarations
    relationship declarations
  owner-issued public-address-kind declarations

ExplanationResourceSnapshot
  owner-issued explanation key and resource-type identity
  schema-conforming direct fact observations
  schema-conforming relationship observations
  zero or more owner-issued public addresses

ExplanationCatalog
  composed schemas and installed resource snapshots
  canonical product-resource path registrations
  validated cross-owner identity and relationship joins

ResourceExplanationDocument
  self-contained bounded schema slice
  one root resource
  bounded ordered resource expansion
  bounded ordered relationship observations
  bounded ordered relationship targets
  traversal receipt
```

Schemas and snapshots are owner-issued immutable data. The catalog is a
host-composed immutable database over the declarations and installed
snapshots that host ships. A contextual operation may overlay one detached
root snapshot and an explicit bounded set of owner-supplied related snapshots
for the duration of one explanation without mutating the installed catalog.
Resource Explanation does not acquire or discover that overlay. The Document
is one bounded projection of the settled installed graph plus request overlay.

This separation is load-bearing:

- a schema can exist before any resource snapshot uses it;
- an installed snapshot can be cataloged without executing its operation;
- a resolved snapshot can be explained without becoming an installed
  capability;
- a host can lower one completed Document without consulting owner catalogs
  again; and
- schema and resource identity remain distinct even when one CLR type
  currently represents both.

### Data-shape declarations

An owner describes product data exposed through explanation using one shared
declaration vocabulary. The vocabulary supports:

- scalar values with a stable scalar-kind identity;
- terms from one owner-issued value vocabulary;
- named records whose fields each have a stable field identity and data shape;
- closed choices whose cases each have a stable case identity and optional
  payload shape;
- references to another owner-issued data shape; and
- cardinality paired with every field, fact, relationship, input, or output as
  required-one, optional-one, or ordered-many. An ordered-many value embedded
  directly in a resource snapshot also declares a positive maximum count;
  relationship targets and ordinary operation result sequences use their
  owning request or execution bounds instead.

The declaration vocabulary is recursive by identity, not by embedding
arbitrary CLR object graphs. Composition rejects duplicate identities,
unresolved required shape references, invalid choice cases, contradictory
cardinality, missing or non-positive embedded-value budgets, and recursive
shape cycles without a finite declared depth and node bound. Recursive domain
structure remains expressible through a named reference.

The vocabulary is not limited to direct resource facts. Query operands, result
rows, reusable references, envelope contracts, and other owner-issued product
data may name the same shapes without turning every runtime value into an
explainable resource. Resource snapshots carry values selected for one
explanation; schemas remain the reusable contract currency for data that is
produced or consumed elsewhere.

Every named data shape has one semantic owner, stable shape identity, schema
version, kind, and concise meaning. Record field and choice-case order is
owner-issued. It also declares a finite embedded-value budget: maximum
canonical encoded bytes, maximum nesting depth, and maximum value-node count.
The common declaration vocabulary applies a fixed finite canonical
encoded-size budget to every declaration node's identity, meaning, and other
metadata.
Scalar kinds are common physical value carriers; semantic values such as
versions, member anchors, artifact coordinates, and durations retain named
owner shapes rather than collapsing to their physical string or integer
representation.

A schema-conforming value is correspondingly one scalar, one vocabulary term,
one record keyed by declared field identities, or one closed-choice case with
its declared payload. A named shape reference reuses that value contract.
Cross-resource joins use resource relationships and public addresses instead;
they are not hidden inside arbitrary nested values.

Snapshot construction validates the complete canonical value against all three
shape budgets before catalog or request-overlay composition. A value that
exceeds encoded bytes, nesting depth, node count, or an embedded ordered-many
maximum is not truncated. The owner supplies an admitted typed Failed or
Unavailable observation when its contract supports that outcome; otherwise
the explanation operation fails visibly without a partial Document. Keys,
public addresses, and typed outcome data use the same budgeted value contract.

Built-in scalar kinds are deliberately small: Boolean, arbitrary-precision
Integer, finite Decimal, finite Binary Floating Point, Unicode Text, and
Octets. Width, signedness, units, lexical format, and domain meaning belong to
an owner-issued named shape over that carrier. A domain value does not become
plain Text merely because the common floor does not contain its CLR type. Its
owner publishes a named data shape beside the type and supplies the
schema-conforming projection used for explanation. For example, metadata may
describe a member-anchor shape without moving `MemberAnchor` into
`QuerySpace.Primitives` or making Resource Explanation depend on the metadata
assembly. Non-finite numeric values require an explicit owner choice shape
rather than an invalid scalar encoding.

Ordered-many direct facts are for bounded descriptor data such as examples or
fixed supported cases, and their declarations include a positive maximum
embedded count. An owner collection that is independently addressable,
unbounded, pageable, selectable, or useful as a navigation target is modeled
as resources and relationships instead of being copied into a direct fact.

### Resource-type declarations

One resource-type declaration contains:

- a stable owner-issued resource-type identity;
- the semantic owner;
- an ordered set of fact declarations;
- an ordered set of relationship declarations; and
- an optional concise owner-issued summary.

A fact declaration contains one stable fact identity, one data-shape
reference, cardinality, admitted outcome states, and owner-issued meaning. Fact
identity is semantic currency; a rendered heading or JSON property name is a
lowering concern.

A relationship declaration contains one stable relationship identity, target
resource-type constraint, cardinality, admitted outcome states, and
owner-issued meaning. It may state that registered target navigation is
required, optional, or unavailable for that relationship kind. It does not
contain CLI syntax, Browser routes, HAL relation names, or inferred links.

The resource-type schema is closed for one published version. An owner adds a
fact or relationship by publishing a new compatible schema version under its
own rules; Resource Explanation does not accept undeclared extension facts.
This preserves extensibility without an untyped property dictionary or
cross-owner discriminated union that must be edited whenever one owner adds a
resource kind.

### Resource snapshots

One detached resource snapshot contains:

- the exact owner-issued explanation key;
- its declared resource-type identity and schema version;
- direct fact observations keyed by declared fact identity;
- relationship observations keyed by declared relationship identity;
- whether the snapshot represents an installed contract or a resolved
  request-scoped resource; and
- zero or more owner-issued public addresses.

Every available fact value is validated against its declared shape and
cardinality. Every available relationship target preserves its owner-issued
explanation key and must satisfy the declared target-type constraint. Equal
labels, property names, field names, CLR types, or serialized values never
create identity or relationships.

One fact or relationship observation has exactly one state:

- **Available** carries one schema-conforming value or an ordered target
  sequence, including a valid empty sequence for ordered-many cardinality;
- **Absent** carries no value and is valid only for optional-one cardinality;
- **Unavailable** carries one owner-issued typed reason admitted by the
  declaration; or
- **Failed** carries one owner-issued typed failure admitted by the
  declaration.

Unavailable and failed relationship observations carry no target. A required
relationship can therefore report a typed failure without inventing a target,
and an available empty many-target sequence remains distinct from absence,
unavailability, and failure.

Outcome identity, reason, and diagnostics remain typed owner data; they are not
encoded as `null`, an empty sequence, or display prose.

A public address is navigation, not semantic identity. Resource Explanation
owns canonical product-resource paths for installed resources. The reusable
inspection-reference owner publishes references for resolved subjects and
rows. A direct contextual handoff can explain a snapshot with no public
address; the operation does not manufacture a reference merely to make the
resource fit this model.

One public address contains an owner-issued address-kind identity and one
schema-conforming address value. The address-kind declaration names the
responsible owner, data shape, target resource-type constraint, and whether
the spelling is accepted by top-level `explain`. Parsing, formatting, shell
safety, and resolution remain with that owner. The Document therefore never
uses one untyped string field for both a product-resource path and a reusable
inspection reference.

One resource has at most one emitted canonical address per address kind.
Accepted aliases resolve to that canonical address but do not enlarge the
Document address set. Address-kind count is covered by the schema-declaration
limit.

An unavailable relationship remains its targetless owner-issued unavailable
or failed observation where that owner admits such an outcome. It is not
replaced by an empty target list, display placeholder, or omission that looks
like successful absence.

### Explanation catalog

Catalog composition receives explicit schemas, installed snapshots, address
registrations, and owner modules. It does not scan assemblies, inspect parser
metadata, reflect over CLR types, or parse rendered output.

Construction validates:

1. Schema, resource-type, fact, relationship, resource, and
   public-address-kind identities are unique in their declared scopes.
2. Every installed snapshot conforms to one available resource-type schema.
3. Every observation state is admitted by its declaration, and every available
   fact value or relationship target conforms to its shape, cardinality, and
   target constraint.
4. Every snapshot has at most one canonical address per address kind, every
   public-address value resolves to at most one resource within its kind, and
   every canonical path resolves to exactly one installed resource.
5. Every available relationship target whose declaration requires navigation
   has one registered address.
6. Every adopted owner domain is represented completely under that owner's
   declared adoption boundary.
7. No composed fact, relationship, or resource exists without an owner-issued
   declaration or snapshot.

Invalid composition prevents catalog publication. A host never advertises a
partially accepted database or silently drops an owner declaration.

### Document schema slice

The completed Document contains the smallest self-contained schema slice that
interprets every emitted value, observation, target, and address. In
deterministic owner declaration order it carries:

- each schema identity and version used by the Document;
- every complete resource-type declaration needed by an emitted resource or
  target, including its fact and relationship declarations;
- every data-shape declaration transitively referenced by those declarations
  and emitted values; and
- every public-address-kind declaration needed by an emitted address.

The slice carries owner-issued meanings, field and case declarations,
cardinalities, admitted outcomes, and target constraints. Hosts therefore
lower the completed Document without consulting owner catalogs or guessing
semantics from identifiers.

Every request contains a positive schema-declaration limit in addition to its
resource, relationship, and target limits. The root resource's required schema
closure must fit or the operation fails visibly without a partial Document.
Expansion admits another resource, relationship observation, or target only
when both the item and its newly required transitive declarations fit. The
traversal receipt records schema declarations emitted and schema-limit
truncation separately.

The schema-declaration limit counts every schema, data-shape, record-field,
choice-case, resource-type, fact, relationship, and public-address-kind
declaration node. Together with the declaration-node encoded-size budget, it
bounds the self-contained schema slice rather than counting one arbitrarily
large aggregate as one declaration.

### Resource Explanation Document

The completed Content value is:

```text
ResourceExplanationDocument
  object-model schema version
  ordered self-contained schema slice
    schema identities and versions
    data-shape declarations
    resource-type, fact, and relationship declarations
    public-address-kind declarations
  root explanation key
  root resource-type identity
  requested public-address kind and value when one was used
  ordered resources
    explanation key and resource-type identity
    available typed public addresses
    ordered fact observations
      fact identity
      available, absent, unavailable, or failed outcome
      schema-conforming value or typed outcome data
  ordered relationship observations
    source explanation key
    relationship identity
    available, absent, unavailable, or failed outcome
    target projection completeness
    ordered available targets
      target explanation key and resource-type identity
      available typed target addresses
  traversal receipt
    requested depth
    requested schema, resource, relationship, and target limits
    completed depth
    emitted schema declaration count
    visited resource count
    emitted relationship-observation count
    emitted target count
    completeness
    truncation reasons
```

It is an immutable semantic
[Document](host-observable-content-kinds.md#document). The Document is not the
rendered Markdown document shape, parser help, a dictionary of display
properties, or a serialized owner descriptor.

The completed host-neutral service returns
`InspectionEnvelope<ResourceExplanationDocument>`, preserving Content, Share,
and diagnostics. Registration of a stable `result_kind`, schema version, and
wire serializer remains an output-contract-owner adoption; it does not delay
use of the ordinary typed service envelope.

The requested root appears exactly once. Every available relationship target
is one of:

- an expanded resource in the Document;
- a registered resource outside the selected traversal bound, with its
  available public addresses; or
- an unexpanded typed target whose owner-issued resource type and explanation
  key are present in the schema slice even when no public address or target
  snapshot is available.

The last form preserves an adopted owner-issued typed target without minting
another owner's route or converting it to display text. An opaque external
identity whose owner has not issued an explanation resource type and key is a
schema-conforming fact value, not a relationship target.

### Lowering-neutral structure

The object model contains no Markdown headings, JSON property naming policy,
table columns, HAL `_links` or `_embedded` members, CLI command text, or
Browser controls. Hosts lower the same completed Document through an explicit
format contract.

A future HAL-JSON lowering is straightforward but not privileged:

- schema-conforming facts can lower as resource state;
- public addresses and typed relationships can lower as links;
- bounded expanded resources can lower as embedded resources; and
- the traversal receipt can preserve incomplete expansion.

The lowering must retain the typed relationship identity and target resource
identity even if it also emits a concise relation name and `href`. Query
facets are a representative case: their scalar facts describe key, value
shape, operators, effects, and examples, while relationships navigate to
their Query Space, scope, row set, value vocabulary, route, and result
contract. HAL-JSON, ordinary JSON, Markdown, and Browser presentation all
consume the same semantic graph.

### Proposed model collapse: resource data first

This is a proposal for the next focused Resource Explanation slice, not a
change to the implemented Document or its current serialization contract.

The motivating production assets are the installed C# Body Kinds and C# Style
Choices vocabularies issued by `ILInspector.Decompiler`. #9471 records their
explanation JSON at 122,959 bytes without related-resource expansion and
441,247 bytes with depth-one expansion. A two-scalar map entry occupies about
600 bytes. These observations justify investigating both the model and its
lowering; byte size alone does not establish which semantic types to remove.

The proposed claim is: given a validated resource snapshot, ordinary explanation
presents its facts, relationships, and available navigation directly, preserving
owner identity, value meaning, ordering, and visible absence, failure, or
incompleteness without requiring a consumer to decode the declaration model.

The `kubectl explain` precedent supports separating the source contract from
the explanation response. Its [command contract](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_explain/)
selects a resource or nested field path, with recursive expansion explicit.
Its [plaintext template](https://github.com/kubernetes/kubectl/blob/master/pkg/explain/v2/templates/plaintext.tmpl)
resolves OpenAPI references internally and emits selected descriptions, concise
types, required markers, and fields. Field lists show at most four enum values
with an omission marker; exact-field explanation shows the full enum. It does
not emit its OpenAPI declaration closure alongside that explanation.

This precedent justifies a concise projection and selective navigation. It does
not establish that our typed construction model is unnecessary: Kubernetes
already has OpenAPI as its declaration substrate, and `kubectl explain` does
not provide our machine-readable graph response. Evaluate internal model
retirement separately from removing its machinery from ordinary output.

The [CVE schema design](https://github.com/dotnet/designs/blob/b9bc7465feae40aa606b8e781fc290c20b690748/accepted/2025/cve-schema/cve_schema.md)
provides a complementary consumption precedent. Its
[example data](https://github.com/dotnet/designs/blob/b9bc7465feae40aa606b8e781fc290c20b690748/accepted/2025/cve-schema/cve.json)
uses flat arrays for discovery, a keyed dictionary for shared commit details,
and compact derived indexes for frequent relationship lookups. The schema is
published separately. Repetition earns its place by reducing consumer work;
normalization earns its place by avoiding repeated shared detail. The design
uses `jq` query simplicity as a proxy for LLM consumption ease, not as proof of
agent task success.

Apply that criterion to explanation: record representative questions first,
then exercise their actual JSON access paths. Getting a facet's accepted
values, finding its value vocabulary, listing a collection, and following a
related resource should not require repeated schema joins or wrapper decoding.
Introduce a derived lookup only when a demonstrated question justifies it;
derive it from the same owner-issued catalog rather than maintain a second
inventory. Compare query complexity, response size, and actual agent task
success instead of treating maximal normalization or minimal bytes as the goal.

Two open `dotnet/designs` proposals extend that precedent:
[Hypermedia release notes graph](https://github.com/dotnet/designs/pull/358)
(head `709feed618a38f6ab70cfe6eb83a483846e0d26e`) and
[Exposing Hypermedia Information Graphs to LLMs](https://github.com/dotnet/designs/pull/359)
(head `6f18aa6696f705e8e94234d5b07586570ccc36ce`). These are proposed designs,
not adopted contracts for this product. Their useful comparison points are
skeletal navigation nodes versus heavier content nodes, semantic relations
that communicate target purpose, selected embedded summaries with canonical
resource links, and shortcuts that reduce traversal. They evaluate both `jq`
queries and LLM navigation. Their reported observations support testing the
whole task path rather than optimizing one response in isolation.

For explanation, compare a small collection/navigation response with a focused
facet or vocabulary response that embeds enough data to answer the selected
question. Preserve the distinction between a partial embedded summary and its
complete canonical resource; do not call omitted detail absent or complete.
Measure total retrieved content and operation count together. Clear relation
names can reduce required guidance, but domain constraints remain owner-issued
facts rather than meanings inferred from the relation spelling.

The smallest useful resource view consists of:

- one resource identity and type, with its public addresses when available;
- ordered named facts containing their values or explicit non-available states;
- ordered named relationships containing targets or explicit non-available
  states;
- bounded related-resource expansion when requested; and
- the completeness information needed to interpret the selected result.

Declarations remain the authority for construction-time validation. They are
separately inspectable contract data, rather than information every ordinary
resource response must embed. Cross-owner identities remain unambiguous;
local field, case, fact, and relationship names use their enclosing declaration
instead of repeatedly carrying its fully qualified identity. A public path is
not a substitute identity for detached resources without an owner-issued path.

Evaluate these collapse candidates against existing production consumers:

- Scope member identities to their containing shape or resource type, retaining
  qualification where a value crosses that boundary.
- Represent values directly in the resource view instead of exposing nested
  scalar, field-value, and identity wrappers to readers.
- Give each navigation collection only its applicable member relationships;
  do not declare unrelated relationships and manufacture empty observations.
- Keep one value-shape vocabulary; #9401 owns Product Vocabulary's adoption
  and retirement of its parallel map-value grammar.
- Derive redundant owner/type information from the resource key where possible.

HAL is a candidate lowering for this view, using resource state and link
relations. The choice of lowering does not settle which facts belong in the
view, whether a declaration is necessary, or which internal types can retire.
Do not add a second independently maintained explanation inventory.

Before implementation, specify and gate the ordinary resource view versus
explicit contract inspection. Use the two installed vocabularies above and a
Package Query facet to measure byte and token counts and demonstrate that a
reader can obtain accepted values, query constraints, and related-resource
paths without walking the declaration grammar. Retain tests for cross-owner
identity, detached subjects, record and choice values, targetless outcomes,
ordering, and truncation. Compare NativeAOT production terminals and include
CLI and Browser/Wasm consumption in the adoption plan. Do not choose an
arbitrary size threshold before measuring these useful results.

### Data-first self-contained document design

Status: **proposed; measured style experiment, not production adoption**.
The draft compact lowering in #9774 reduces declaration overhead, but its
43,136-byte Style Choices response still preserves an incidental graph layout.
That is insufficient evidence for an agent-reading default. The
[executable shape comparison](../../tools/ExplainReadingScenarios/shapes/README.md)
starts instead with core data, critical queries, and normalization together.
The self-contained CVE document above is the primary precedent; HAL lowering
does not decide the data model inside one document.

The proposed claim is: each selected explanation dataset has a stated core
population and critical reading queries; its organization and normalization
are justified by query simplicity and whole-task retrieval size together.
Self-contained means those queries resolve locally within the selected dataset,
not that every neighboring resource or declaration closure is included.

For style data, the core population is 17 choices, four tiers, and their
declared properties. Critical queries list choices, select byte-preserving
choices, identify conflicts, select a tier's option/value pairs, resolve a
known choice, build a menu with tier names and descriptions, and list endorsed
choices. The grouped-menu query requires tier records that the draft depth-one expansion does not
contain. Other selections start with their own core: a query facet needs its
issued operand/operator/constraint facts and required-context meaning; a
resolved subject needs the facts selected about that subject. Tips and reusable
references select their own smaller currencies under Contextual Resource
Explanation, rather than inheriting the style dataset.

The preferred measured style candidate is a discoverable choice array with
direct stable facts, keyed shared tier records, and complete sparse property
tables. True boolean observations are stored as ID membership sets; optional
conflict observations are stored as group-to-member tables. These are the core
property data, not indexes that duplicate flags retained on every record. Choice IDs
are scoped to their declared vocabulary; tier references inherit the target
vocabulary from the property declaration. Shared scope and interpretation
appear once. Both populations, every property, descriptions, and presentation
order survive the lowering; complete OptionalOne absence remains distinct
from unavailable or failed observations. Unobserved or incomplete domain data
cannot be fabricated to make a document self-contained.

The experiment answers all seven questions from one 9,918-byte sparse document.
Keeping flags and optional conflicts on every choice uses 10,996 bytes; 55 of
68 such entries are false or absent. Sparse membership preserves those values
for known records because the source observations and selected populations are
complete. Unknown records and unavailable observations must not become false
through negative membership. Repeating tier records uses 14,420 bytes. Keyed choices, explicit ordering,
and reverse indexes use 12,972 bytes and simplify known-ID and group lookups,
but complicate ordered discovery. Every answer agrees, and the experiment
checks recovery of the selected core data. Current CLI retrieval for the menu
with tier descriptions is 49,935 bytes across two requests. These are
functional data/query measurements, not runtime performance evidence.

The [worked facet demo](../../tools/ExplainReadingScenarios/facets/README.md)
applies this reasoning to the 19-facet Package Query space and its 14 CLI-exposed
query terms. A derived host-exposure index distinguishes authorable terms from
required context; keyed facet records resolve that context locally. The demo
also exposes a completeness gap for the full agent task: exact operand bounds
and the CLI context gesture still require supplementary owner-contract guidance.
Those facts must be issued by Query Space and the host binding before the
selected facet document can claim to answer execution-preparation queries
without additional reads.

The proposed normalization rule is to store shared detail once and add derived
indexes or scalar repetition only when a critical query earns their cost.
Arrays support discovery; keyed tables support shared detail and known-key
lookup. Neither is mandatory for every resource. An index is a projection of
the same owner data, never another maintained inventory. The selected style
baseline does not yet justify all the tested indexes for 17 choices.

This experiment does not define a universal style-specific model in Sections,
change Product Vocabulary's declarations or value grammar, admit a new dotted
selection, or make the generic compact envelope obsolete for explicit graph
inspection. Production adoption must generalize the selected-document assembly
from owner-issued identities, property contracts, and relationship evidence,
retain selected observation outcomes and completeness, and demonstrate the
same data-first reasoning for the other adopted resource families. Only then
should host selection and HAL binding determine the final wire presentation.

#### Selected-data implementation

Exact vocabulary roots and query-space or query-facet roots admit explicit
`.data --json` and `.hal --json` selections. They assemble a bounded local
dataset from the validated catalog rather than using graph depth as a data
selection. Vocabulary datasets include their values and declared target
vocabularies. Facet datasets include required-context closure and the host
bindings that expose selected facets. Sparse tables are used only for complete,
available observations; unsupported or incomplete selected data fails visibly.
Identity, source addresses, property declarations, and order remain available.
Closure admits at most 256 resources and 4,096 observed targets. Binding
exposure lists contain only selected facets; their original total member count
remains in binding facts.
These selections do not claim that unregistered validation rules are present.

The HAL representation presents domain data directly as resource state, rather
than retaining a generic `facts` wrapper or the internal identity/address
receipt. A small `kind`, `name`, and `summary` header leads into `_links`; related
resources appear once under `_embedded` relation arrays. Vocabulary values are
embedded in their vocabulary, and referenced vocabularies embed their own
values. Query facets and exposing bindings are embedded resources. Property
sets and groups remain state on their owning vocabulary. Available data needs
no observation receipt; non-available outcomes other than explicit absence
remain in `data_states` beside the affected resource. `data_scope.completeness`
qualifies the selected population, including sparse negatives, without carrying
traversal budgets or execution bookkeeping.

The reading projection owns the `inspect` CURIE namespace,
`urn:dotnet-inspect:reading:{rel}`, declared once at the entry resource. Its
relations are `values`, `vocabularies`, `facets`, `required-context`, `bindings`,
and `exposed-facets`. They name reading destinations, not internal schema
identities. Embedded inventories supply member self links rather than repeating
the complete inventory in root links. Links carry titles and destination media
types. CLI HAL self and dataset links use
`inspect-resource:/<path>?projection=hal`; following them unchanged retains HAL.
JSON detail destinations are explicitly typed `application/json`. Root
`describedby` links use `?projection=contract` to disclose complete schema and
observation details separately. Projected addresses require JSON and reject
conflicting projection or depth overrides.

The direct JSON model remains independently usable with its typed metadata.
Compare equivalent data-reading answers and advertised navigation, not equality
of layouts after stripping links. These gates establish the representation and
link mechanics; whether unfamiliar agents immediately recognize HAL and need
fewer navigation tips remains an agent usability evaluation, not an asserted
outcome. Existing default output and explicit `.contract` remain comparison
surfaces.

### Compact resource projection contract

Status: **proposed; not implemented**. This is slice 2 of
[#9762](https://github.com/richlander/dotnet-inspect/issues/9762). It specifies
one projection of the validated catalog; it does not replace the catalog,
change owner declarations, or admit new CLI gestures. The existing complete
Document remains supported until the production adoption changes its default.

#### Claim and selection

For one resolved resource, the compact projection returns directly readable
owner-issued facts and typed relationship targets, with enough identity and
completeness information to navigate and interpret the selected response.
It performs no subject acquisition or query execution and cannot infer facts
from labels, rendered output, or neighboring resources.

Two explicit selections have different completeness obligations:

- **Resource data:** the selected resource's fact values, relationship outcomes,
  available addresses, and requested bounded expansion. Declaration closure
  is not part of this selection.
- **Contract inspection:** the declarations needed to interpret the selected
  resource type, including referenced value shapes and relationship target
  types. This selection retains the existing complete declaration semantics.

Resource-data completeness means completeness of the selected resource data,
not completeness of its contract or the entire catalog. Omitted declarations
are neither absent facts nor truncation. A partial embedded summary is marked
as a summary; it never claims complete resource-data coverage. Requested data
that exceeds a bound retains an explicit truncation outcome.

The host-neutral selection is shared by CLI and Browser/Wasm. Host admission
and dotted spelling remain with their focused owners; this section does not
reserve `.schema`, `.facts`, or another spelling by implication.

#### Data and identity

A resource carries its canonical key once, its owner-issued type once, and its
available public addresses. Public paths remain routing projections, not the
identity of a detached resource. Cross-owner targets retain canonical keys
and types even when they have no navigable public address.

Fact and relationship names are local to their resource type. Record fields
and choice cases are local to their declaring shapes. Crossing those scopes
requires an unambiguous qualified identity; nesting within them does not repeat
the enclosing owner, schema, and type on every value. Qualification must be
lossless and collision-free, not derived from display names.

Available facts expose native values: text, booleans, and finite numeric
values directly; ordered-many values as arrays; records by named fields;
choices by a local case and optional payload. Term values retain a stable term
identity, with their vocabulary supplied by the declared shape or explicitly
when several vocabularies are possible. Large integers and octets require an
explicit lossless encoding in the lowering contract; no precision-losing
conversion to a JSON number is allowed. The contract is authoritative even
when it is not embedded in every response.

Non-available observations retain their exact absent, unavailable, or failed
state and owner-issued outcome data. Missing selected data does not become
`null`, an empty array, or an omitted success. Available empty arrays remain
available empty arrays. An explicitly selected response must let a reader
distinguish these cases without fetching the contract.

Relationships preserve target order, target identity, target availability,
and target-projection completeness. An available target without an address
remains a typed target rather than a fabricated link. Targetless observations
remain visible. Each resource view uses the same validated catalog facts;
there is no separately authored compact inventory.

#### HAL lowering and navigation

HAL is the preferred machine-reading lowering to evaluate for this projection.
Its state contains directly readable facts; `_links` carries available
navigable targets under meaningful relation names; `_embedded` carries only
explicitly selected related-resource data or marked summaries. Relationship
states and non-navigable targets require explicit state data alongside HAL
links. HAL alone does not encode every observation outcome.

A relation name is an addressable projection of a declared relationship, not
a source of new domain meaning. Local names may be used where the resource
type supplies the scope. An alias or traversal shortcut requires owner-issued
relationship evidence; it cannot manufacture a capability or query.

Link targets must be supplied as usable addresses for the consuming host.
CLI product-resource paths are not silently promoted to HTTP URLs. Browser
URLs remain host bindings over the same typed target; command examples remain
CLI bindings. Lowering must specify URI resolution and address-kind handling
before implementation, including targets that are addressable only in one
host. A host binding may not replace the canonical target identity.

Contract inspection is explicitly discoverable from resource data when its
owner has registered a usable address. Until that registration exists, the
host must offer explicit contract selection over the current resource rather
than emit a dangling schema link. No JSON Schema or result-contract resource
is advertised before its owning catalog has adopted it.

#### First CLI lowering adoption

The bounded CLI adoption tracked by #9773 changes exact top-level
`explain <resource> --json` to compact HAL resource data. Explicit
`explain <resource> .contract --json` retains the existing self-contained
Document JSON, including its schemas and traversal receipt. `.contract`
requires JSON and an exact resource; search, contextual Member explanation,
and human output retain their existing contracts. This is the first production
consumer of the shared Sections projection; Browser/Wasm adoption remains
slice 4 of #9762.

The compact wire contains `identity` with separate owner, schema, type, and
native identity value; direct available `facts`; non-available `fact_states`;
local `relationships` with exact state, ordered targets, and completeness;
public `addresses`; and HAL `_links`. The root retains the existing traversal
receipt. Requested expanded resources appear once in `_embedded.resources` as
complete projections of their selected data, not as summary copies. This
expansion collection is a lowering of Document membership, not a new domain
relationship. Declared relationship links retain their own local relation names.

Integers and decimals use invariant decimal strings so arbitrary precision
survives JavaScript consumers. Octets use base64 strings. Booleans, finite
binary floating-point values, and text use native JSON primitives. Record fields
use their local names and declared cardinality; choices expose a local `case`
and optional `value`. Term identities carry separate catalog, vocabulary, and
term values to avoid delimiter collisions. The explicit contract supplies
value shapes; no declaration wrapper is repeated for each scalar.

CLI HAL links use absolute `inspect-resource:/<canonical-path>` URIs. The CLI
accepts these values unchanged as exact explanation operands and resolves them
against the installed explanation catalog; they never fall back to capability
search or trigger network access. Other host addresses are supplied by the
consumer binding to the same shared projection. No HTTP endpoint or schema
resource is minted. Explicit `.contract` selection provides contract inspection
until an owner registers a navigable contract address.

Gates: `ResourceExplanationDataProjectionTests` covers detached values,
precision, octets, empty available facts, and unavailable/failed outcomes;
`ResourceExplanationTests.SchemaDeclarationCount_IncludesComposedRelationships`
covers direct record/choice values and available-empty relationship outcomes;
`ResourceExplanationCommandTests` covers actual vocabularies and Package Query
facets, usable self links, explicit full-contract JSON, absent facts, and
rejected projection gestures. NativeAOT and cross-host completion evidence
remain required before their respective adoption claims are complete.

#### Query meaning and evidence

A facet response preserves its issued key, operand kind, operators, values,
examples, effects, and declared relationship targets. Vocabulary navigation
does not mean the facet accepts all vocabulary terms. An empty values list
does not establish an open domain. Query-local constraints remain Query Space
facts; an accepted subset or open-domain assertion requires that owner's
explicit declaration.

Acceptance uses three current production witnesses:

- `vocabularies/csharp.body-kinds`: read listed values and map entries without
  traversing identity wrappers or a declaration closure.
- `vocabularies/csharp.style-choices` at depth one: distinguish selected values,
  related data, summaries, and incomplete expansion.
- `package-query/query/facets/library-literal`: obtain the issued query key,
  operand kind, operators, examples, and related-resource targets without
  inferring acceptance from a vocabulary link.

The implementation slice records exact base/head bytes and token counts,
representative `jq` access paths, and agent task outcomes. It measures total
retrieved content and operation count, so smaller individual responses cannot
hide additional fetches. NativeAOT comparisons cover the adopted CLI terminals.
The executable [reading scenarios](../../tools/ExplainReadingScenarios/README.md)
compare actual style answers across the browser inspection and compact CLI
projection, expose map-entry and expansion joins, and record query/navigation
costs. Their filtered answers do not count as reduced retrieval; the evidence
supports a further selection experiment before an agent-readability claim.
Cross-host gates compare resource facts and relationship meaning, allowing
host-specific usable addresses. Boundary evidence covers identity collisions,
detached subjects, large integers, octets, record and choice values, non-available
observations, empty available collections, unaddressable targets, and truncation.

The ordinary compact view must reduce the two vocabulary witness payloads
relative to the current full Document while preserving their selected facts.
No absolute byte or token threshold is claimed before a measured candidate.
The full Document remains available for explicit contract inspection; its
round-trip equality is not an obligation for a resource-data projection that
intentionally omits declarations.

### Primitive-placement test

This object model determines the types that lower layers must be able to
publish; it does not itself choose their assembly.

A type is a candidate for the general declaration floor when it is:

- immutable, resource-free data or identity;
- required by semantic owners to publish schemas or detached snapshots;
- independent of acquisition, planning, execution, rendering, and host
  syntax; and
- meaningful across more than one inspection domain.

Concrete domain identities and mechanics remain with their owners. Catalog
composition, owner adapters, exact resolution, traversal, capability search,
format lowering, and host bindings remain above the declaration floor. The
follow-on QuerySpace library-boundary effort decides whether the shared
data-shape, resource-type, fact, relationship, and snapshot declaration types
belong in `QuerySpace.Primitives`, and names the dependency and public-surface
gates for that move.

### Determinism

Resource order is breadth-first from the root, then source-owner declaration
order, then owner-issued explanation key as a stable tie-breaker. A canonical
path is used only when the owner-issued ordering key otherwise compares equal.
Relationship-observation order follows source resource order, source-owner
identity, source-owner relationship order, and relationship identity. Targets
within one available observation follow owner-issued order, then target-owner
identity and target explanation key as stable tie-breakers. This is a total
order and does not require every resource to have a public address.

Equal schemas, snapshots, registrations, and traversal bounds produce equal
Content across CLI and Browser/Wasm. Hosts do not reorder the semantic
Document to match their visual layout.

## Bounded behavior

### Default explanation

Every host-neutral request contains resolved numeric limits for maximum depth,
schema declarations, resources, relationship observations, and emitted
relationship targets. Hosts may offer shorthands, but they resolve those
defaults before calling Resource Explanation. The completed Document records
all five limits.

Default explanation resolves depth zero:

- the root resource and its typed direct facts are complete;
- direct relationship observations are listed in deterministic order up to
  the explicit relationship limit, preserving targetless outcomes;
- available relationship targets are listed in owner order up to the explicit
  target limit, with every available address for each emitted target; and
- related resources are not expanded.

An available relationship observation records whether its target projection
is complete. Reaching the target limit may therefore emit a deterministic
prefix of one ordered-many relationship while preserving the source
observation's Available outcome and reporting that its projected targets are
truncated. It never changes that observation to available-empty.

This keeps the common response concise while making the next exact gesture
copyable.

Owner-issued examples are direct facts only when they are bounded descriptor
content. A value vocabulary's values are resources reached through an ordered
relationship, so traversal, not a direct fact, enumerates them.

Repeated owner collections are represented as relationships and are therefore
subject to the relationship limit. Direct facts contain scalar values, typed
records, fixed product terms, or owner-issued bounded examples; they do not
copy an unbounded descriptor collection.

### Recursive explanation

Recursive explanation expands declared relationships only. The semantic
request contains explicit non-negative maximum depth and positive schema,
resource, relationship, and target limits. A CLI shorthand may supply
documented finite defaults, but the host-neutral request always contains the
resolved numeric bounds.

Traversal:

1. starts with the root at depth zero;
2. visits each explanation key at most once;
3. records encountered relationship observations in total deterministic order
   until the relationship limit is reached;
4. records targets in owner order until the target limit is reached, marking
   the containing observation's target projection incomplete when needed; each
   emitted target occurrence consumes the limit even when the same resource is
   reached through another relationship;
5. expands an emitted target only when the next depth, schema, and resource
   limits admit it; and
6. records depth, schema, resource, relationship, and target truncation in the
   traversal receipt.

Cycles therefore remain visible as relationships when encountered before the
relationship bound, but cannot loop. No host may replace any bound with an
unbounded sentinel. A bounded Document reports `Complete` only when no required
schema declaration, declared resource, relationship observation, or target
within the requested depth was omitted.

### Installed, contextual, and resolved-plan explanation

The current implementation explains installed capability descriptors. It
performs no acquisition and accepts no inspection subject. The target object
model also admits a detached contextual resource snapshot after its command
owner has resolved one exact subject. That direct handoff does not make the
subject an installed capability, mutate the reusable catalog, or require a
serialized reference.

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
- owner-issued section Shape and Cardinality as typed direct facts;
- sections with no item-level vocabulary; and
- addressed resource identity separately from compact projected rows.

A `filterable` or `sortable` item remains a Schema Query structural-discovery
fact. It does not become a Query Space facet, binding, operator, stage, or
effect unless a separately adopted `QuerySpaceDescriptor` supplies that typed
resource and correspondence. Likewise, a column named `References` does not
become a query facet named `references`; a shared label establishes nothing.

Output capabilities, Shape, and Cardinality appear as typed direct facts.
Explanation of a format's global behavior requires a separately owner-issued
output-format descriptor; Resource Explanation does not infer it from the
capability enum or a renderer.

### Analysis resources

An analysis registered through
[analysis participation registration](inspection-capability-composition.md#analysis-participation-registration)
is an installed explainable resource with an Analysis resource-type identity:

- **Path.** Its canonical path is `analyses/<analysis-id>`. The analysis
  identity already satisfies the segment grammar and is reused unchanged. The
  collection `analyses` lists every registered analysis.
- **Relationships.** Its typed relationships name each operation and report
  surface it takes part in, and the Finding descriptors it issues there.
- **Facts.** Its descriptive facts come from the owner-issued analysis
  descriptor.

Resource Explanation does not infer an analysis from a section, a Finding
descriptor, or a CLI spelling.

### Query resources

`QuerySpaceDescriptor` is the complete query-capability input. Explanation
preserves the distinction among:

- operation and row query scopes;
- row spaces and structural sections;
- facets and their bindings;
- each facet's owner-issued value shape and cardinality;
- operators and named orders;
- Rows and exact Count terminal requirements;
- effects and continuation acceptance; and
- opaque external value-vocabulary and result-contract identities.

A structural section may present a row space without owning it. Operation and
row facets with equal display labels remain distinct resources when their typed
identities, scopes, stages, or effects differ.

Resource Explanation never reconstructs a facet from a schema column, a
rendered property, a parser option, or a command delegate.

Before an external owner adopts Resource Explanation, its opaque identity is a
typed fact under Query Space's named opaque-external-identity shape. It does
not become a relationship target, because Query Space cannot issue another
owner's explanation resource type or key. A later focused adoption may replace
or accompany that fact with an owner-issued relationship after the external
owner publishes the required schema and key projection.

### Value-vocabulary resources

Each product vocabulary a host composes under
[Product Vocabulary ownership](vocabulary.md#ownership) is an installed
explainable resource with a Value Vocabulary resource type, and each of its
values is a resource with a Vocabulary Value resource type. Both types are
issued by the Product Vocabulary owner. The host's composed snapshot is the
complete input. Explanation reads vocabularies, maps, and values from it
generically; it names no individual vocabulary and restates no value.
`ResourceExplanationCatalog.CreateVocabularies` is the one catalog factory.

- **Paths.** The collection is `vocabularies`, a vocabulary is
  `vocabularies/<vocabulary-id>`, and a value is
  `vocabularies/<vocabulary-id>/values/<value-segment>`. Vocabulary identities
  satisfy the segment grammar and are reused unchanged. Value identities need
  not: body kinds and style tiers are PascalCase, and some style choices
  contain `:`. The Product Vocabulary adoption therefore registers each
  value's segment explicitly as its owner identity in ASCII lower case with
  `:` replaced by `.`. The rule depends only on the stable owner identity,
  never on a display label. Construction fails visibly when a segment falls
  outside the grammar or two values of one vocabulary share a segment. The
  exact identity remains the value's identity fact, and it is the identity
  queries accept. The segment is the only path spelling; an exact identity
  that differs from it, such as `ObjectCreationExpression`, is not a canonical
  path.
- **Collection.** `vocabularies` lists every vocabulary in the composed
  sections index, in index order. The index vocabulary itself is not a member;
  the collection is its explanation. `vocabularies/<id>/values` is not a
  resource, so depth 1 from a vocabulary reaches its values directly.
- **Vocabulary facts.** Identity, display name, optional summary, value
  count, accepted query-input identities, maps, and the values whose Boolean
  `default` map is true. `maps` is an ordered-many record fact: each record
  holds a map's identity, owner-issued display label and summary, value kind
  (`text`, `integer`, `boolean`, or `term`), target vocabulary for a term map,
  cardinality, and coverage, in owner map order. Accepted query inputs are
  opaque external identities until their owners publish explanation
  resources.
- **Value facts.** Identity, display name, optional summary, and the value's
  map entries. Resource-type schemas are closed, so the Vocabulary Value type
  declares one ordered-many record fact with a declared maximum count, rather
  than a per-map property or a per-vocabulary resource type. Each record holds
  the map identity and a value that is a choice of the map's declared scalar
  kind (text, integer, or Boolean) or, for a term map, the target value's
  exact identity as text, resolved through the target vocabulary that the
  vocabulary's `maps` fact names. Records follow owner map order and then each
  map's value order, so a one-or-more map yields one record per value. The map
  identity lives in this fact, so a vocabulary with several term maps stays
  unambiguous.
- **Relationships.** A vocabulary has an ordered relationship to every one of
  its values, in owner order, and a relationship to each distinct vocabulary
  its term maps target. A value has a relationship to each distinct value its
  term-map entries name, in first-occurrence order. Term-map relationships are
  unqualified navigation; the maps behind them are the vocabulary's `maps`
  fact and the value's map-entry fact, because the explanation model's
  relationship declarations and targets carry no qualifier. Construction
  fails visibly for a term-map target that is not an explained vocabulary in
  the snapshot rather than inventing one. A term
  map into another snapshot already fails when the host composes its product
  snapshot, because hosts supply no external snapshots.

The explanation is the complete listing. A vocabulary's values relationship
names every value, and depth 1 returns each value's resource. The limit that
binds is the resource limit, which counts the vocabulary itself and each
vocabulary its term maps target, so a complete depth-1 listing holds for up to
that many fewer values than the limit; beyond it the Document reports
`ResourceLimit` truncation visibly. Depth 1 reports depth truncation for the
links from values to further values. The largest current vocabulary,
`csharp.body-kinds`, has about 70 values. Every product host issues
`ResourceExplanationRequest.ForHost`, whose resource and relationship limits
are the shared host limits. No separate command or document carries
vocabulary values; the `vocabulary` command retired under
[Product Vocabulary](vocabulary.md#retirement).

Both hosts request the same explanation. The CLI's `explain` and Inspect
Web's catalog-facade export `CatalogExports.ExplainVocabularies(path, depth)`
resolve the same path against their own composed snapshots and return the
same Document Content under `ResourceExplanationRequest.ForHost`. Inspect Web
calls `VocabularyExplanation` in `DotnetInspector.Sections`, the host-neutral
request surface over `CreateVocabularies`; the CLI dispatches its
`vocabularies` root to `CreateVocabularies` with the same request. Equal
Content is gated the way `ProductVocabularyPin` gates the snapshot:
`ProductVocabularyPin.ExplanationContent`, a linked file, pins the SHA-256 of
the Document JSON for representative requests, and the CLI suite hashes
`explain --json` output while the Inspect Web suite hashes the export's
Content. The Browser export carries the completed Document, serialized by
`ResourceExplanationJsonContext`, as owner-issued content in the facade-local
`BrowserVocabularyExplanation` record, the established form for owner content
that crosses a facade boundary; its Share and diagnostics reuse the
facade-local vocabulary records. A non-canonical path, a path outside
`vocabularies`, an unknown path with its suggestions, or a negative depth is a
typed rejection rather than an empty Document.

A CLI query key whose values are one vocabulary's identities, such as the
Body Shapes `Kind` key, carries that vocabulary's name and canonical path on
its query-key descriptor, and query discovery presents the path. This is a
navigation link for the key's legal values, not a Query Space facet
relationship.

For Query Space facets, the initial query adapter preserves an optional
opaque value-vocabulary identity and query-local operand constraints as
separate facts. It does not claim whole-vocabulary acceptance
or a reusable subset identity. A later query-owner adoption may issue a typed
complete, constrained-subset, or open-domain relationship for explanation to
preserve.

### Contextual resources

Contextual Resource Explanation supplies one installed command resource or one
detached resolved-subject root snapshot, plus any explicitly admitted related
snapshots needed for bounded expansion. Every snapshot uses the same
resource-type, fact, relationship, address, and outcome declarations as every
other explainable resource.

For the Member first adopter, package or platform context, Library, Type,
member-group or exact-member identity, default facet, selected semantic
sections, and related operations remain owner-issued facts or relationships.
Resource Explanation does not restate their semantics. The adoption retires
the Member-specific contextual Document wrapper after those values are
represented by one schema-conforming resource snapshot and the common
`ResourceExplanationDocument`.

The complete Document may expose every owner-issued related-operation
relationship. `.tips` applies the Contextual Resource Explanation host-binding
and ranking contract to those relationships.

The approved singular `.reference` remains the separate row-preserving
projection owned by Contextual Resource Explanation, staged until that owner
and the dotted-gesture grammar replace their current plural reservation. Each
selected semantic row retains one owner-issued reusable-reference value
conforming to a declared reference shape. The projection does not require
every selected row to become an explainable resource, enter the catalog, or
appear in the complete Document. When a resolved root resource itself has a
reusable inspection reference, that reference may also be one of its public
addresses; neither use changes the resource key or schema.

### Output-contract resources

Query Space may expose an opaque result-contract identity. Resource
Explanation preserves that pre-adoption identity as a schema-conforming typed
fact under Query Space's opaque-external-identity shape. It does not list a
result-contract relationship before the contract owner issues an explanation
resource type and key.

A later envelope-contract catalog must own exact contract identity, Content
Kind, `(result_kind, schema_version)`, serializer, JSON Schema, producers, and
compatibility. Its adoption adds an owner-issued resource schema, snapshots,
relationships, and paths. Resource Explanation does not synthesize that
catalog from generic arguments, source-generation contexts, static
registrations, or observed envelopes.

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

Command-local `--explain` and `-E` consume the same complete
`ResourceExplanationDocument` for one admitted command resource or detached
resolved-subject snapshot. The placement changes, not the semantic Content:
`--explain` writes it as the primary stdout result, while `-E` writes it after
unchanged successful ordinary output on stderr. Dotted projections select
typed currencies from the same owner declarations without manufacturing a
second explanation model.

Human output lowers the Document through a typed Markout view. `--json`
serializes the same Content contract with source-generated metadata; the final
general output-format spelling remains owned by its focused work. Human
headings and tables are not machine identity.

Diagnostics expose canonical paths that can be copied unchanged into
`explain`. Compact `-D` and `-Q` output should do the same when their owning
adoptions can preserve current concise shapes.

After Library structural explanation presents the same Formats facts from
`DiscoveryDocument`, the temporary Library `-D --details` bridge is removed.
`explain` does not become another `--details` flag.

### Browser/Wasm

Browser/Wasm consumes the same completed
`InspectionEnvelope<ResourceExplanationDocument>`.
It may render links, breadcrumbs, expandable relationships, and
purpose-specific controls, but it does not reconstruct the graph from CLI
text or restate owner catalogs in TypeScript. The first Browser request
surface is the vocabulary explanation export under
[Value-vocabulary resources](#value-vocabulary-resources); the structural
explanation consumer remains adoption step 4.

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

`System.Text.Json@10.0.0` motivates the complete production flow:

1. Use Library structural discovery to find `Reference Hierarchy`.
2. Copy its canonical resource path into `explain`.
3. Read its Shape, Cardinality, fields, columns, output capabilities, and
   declared query relationships without acquiring a Library.
4. Follow one query-facet relationship to its value shape, operators, row
   scope, value vocabulary, route, and result contract.
5. Run the ordinary Library inspection against
   `System.Text.Json@10.0.0`.
6. Resolve one exact `JsonSerializer.Serialize` Member and request
   `--explain`; the command passes a detached Member snapshot directly into
   the same object model.
7. Run the same exact Member with `-E`; ordinary stdout remains unchanged and
   the same complete explanation Content follows on stderr.
8. Select `.tips` to project applicable host gestures. After the focused
   contextual and #7916 adoption replaces the plural reservation, select
   `.reference` to issue the reusable Member identity.

The neighboring missing-target case runs the same structural explanation
without a package, file, or platform selection. It must produce equal Content,
proving that installed capability explanation does not acquire.

The pathological graph includes:

- one section in several categories;
- one section column and one query facet with the same display label;
- operation and row facets with the same display label but different effects;
- one owner-specific record shape whose concrete CLR type is unavailable to
  the declaration floor;
- one recursive optional record value at its depth and node budgets and one
  value exceeding each budget;
- legitimate optional absence beside a distinct unavailable or failed fact;
- one resolved contextual resource with no public reusable reference;
- a snapshot whose fact value violates its declared data shape;
- a relationship whose target violates its declared resource-type constraint;
- an opaque value-vocabulary reference and query-local operand constraints;
- a later owner-issued complete, constrained-subset, or open value domain;
- a section with no item vocabulary;
- a category whose complete capability set differs from one member;
- an authoritative resource added without a path registration;
- an authoritative relationship omitted by its adapter;
- a cycle of cross-owner navigational relationships;
- valid expansions truncated by depth, schema, resource, and relationship
  limits;
- one ordered-many relationship whose targets exceed the target limit; and
- one opaque external result-contract identity before and after its owner
  issues an explainable resource type and key.

## Invariants and evidence

Implementation slices must name Release gates for the following properties:

| Property | Required gate |
| --- | --- |
| Owner schemas are closed, internally valid, finitely budgeted, and independent of concrete domain assemblies. | Schema-construction tests covering scalar, term, record, choice, reference, cardinality, encoded-byte, nesting-depth, and node-count declarations, including recursive shapes and a real owner-specific shape whose CLR type stays in its owner assembly. |
| Every snapshot fact and relationship observation uses an admitted state and every available value or target conforms to its declared shape, cardinality, embedded-value budgets, and target-type constraint. | Construction matrix covering valid values, optional absence, available-empty many values, targetless unavailable and failed outcomes, invalid scalar/term/record/choice values, excess cardinality, over-budget bytes/depth/nodes, and wrong-target relationships. |
| Every completed Document carries the bounded transitive schema slice needed to interpret all emitted observations, targets, and addresses without owner-catalog access. | Document-construction tests over colliding physical scalar values with distinct named meanings, nested record/choice references, typed addresses, schema-limit truncation, and a root closure that exceeds the schema limit and fails without partial Content. |
| An opaque external identity remains a typed fact until its owner issues an explanation resource type and key; Resource Explanation never invents the target declaration. | Query result-contract fixture before and after output-contract-owner adoption, asserting a typed opaque fact in the first case and the exact owner-issued relationship target in the second. |
| Installed resources and detached resolved resources use the same Document model without placing contextual snapshots in the installed catalog. | Shared Content-shape tests over one installed query facet and one exact `System.Text.Json` Member, with catalog immutability asserted before and after contextual explanation. |
| Canonical paths are shell-safe, unique case-insensitively, registered rather than derived from labels, and emitted once per address kind despite accepted aliases. | Registry-construction tests over every shipped registration. |
| Each adopted descriptor domain has bidirectionally total resource registration and relationship-observation projection. | Adapter-construction tests comparing the complete real owner enumerations with projected identities and observations, plus omission, duplicate, and extra-projection contract fixtures. |
| Exact resolution returns one root or a visible failure with no partial Document. | Resolver contract tests including unknown paths and bounded suggestions. |
| Structural, query, value, and contextual snapshots preserve owner-issued typed identities and native values. | Adapter contract tests against real owner descriptors and the exact Member contextual basis. |
| Equal labels do not create resource identity or relationships. | Collision fixture spanning structural and query owners. |
| Recursive traversal visits each identity once, preserves encountered cycle edges, bounds emitted relationship targets independently, marks partial target projections, and reports depth, schema, resource, relationship, and target truncation. | Cyclic and permuted-input graph fixtures exercising every bound, including one ordered-many relationship larger than the target limit, and the total ordering key. |
| Capability explanation performs no acquisition. | Host-level missing-target test with acquisition and planning services replaced by fail-fast recording fakes, asserting no capability was requested. |
| Direct contextual explanation resolves and acquires once, then hands one detached snapshot to Resource Explanation without serializing a reusable reference. | Authentic exact-Member integration test with counting resolution/acquisition collaborators and fail-fast reference serialization and ordinary-content producers. |
| CLI and Browser/Wasm receive equal Content for equal descriptor inputs. | Shared Content equality or serialization fixture exercised by both hosts. |
| Every lowering consumes the completed Document and preserves typed fact and relationship identity. | Cross-format fixture comparing ordinary JSON and human output; a future HAL-JSON adoption adds its own equality and navigation gate. |
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

The original installed-resource slices remain:

1. **Complete:** lock the original owner, path contract, installed-resource
   Document, and host boundaries.
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
7. **Complete:** Product Vocabulary adopts resource schemas, snapshots, and
   typed term-map links under
   [Value-vocabulary resources](#value-vocabulary-resources), and a CLI query
   key that accepts a value vocabulary links to its resource. Vocabulary and
   value resources, generic snapshot reading, and the query-key link are
   complete for the CLI, and Inspect Web requests the same explanation
   through its catalog-facade export. The `vocabulary` command retired.
8. Register the stable explanation result contract; then let the focused
   envelope-contract catalog adopt explanation paths and machine-readable
   schemas.
9. Let #7916 adopt reusable subject references and affordance explanation.

Steps 6 through 9 are separately owned adoptions. They do not block the
independently coherent structural explanation slice.

The object-model convergence sequence is:

1. Lock the schema, snapshot, catalog, and Document model in this owner.
2. Have the QuerySpace library-boundary owner place the general declaration
   vocabulary at the lowest valid layer and gate its dependency/content
   boundary.
3. Migrate the installed Resource Explanation implementation from closed
   cross-owner detail variants to schema-conforming resource snapshots while
   preserving current CLI Content.
4. Have Contextual Resource Explanation adopt the same Document for Member and
   retire the Member-specific wrapper as the bounded first contextual adopter.
   **Complete in #9418.**
5. Let Section Shapes, Query Space, Product Vocabulary, result contracts,
   analyses, Findings, and reusable references adopt one owner at a time.
6. Add Browser/Wasm over the same Content and decide whether HAL-JSON earns a
   supported lowering through an agent-understanding comparison.

Each step after the first is a focused owner adoption. This design does not
authorize one implementation PR to move types and migrate every owner.

## Non-claims

This design does not claim:

- ownership of structural discovery, Query Space, value vocabularies,
  Content Kind, envelope transport, output-format semantics, or reusable
  inspection references;
- the physical assembly or namespace placement of the general declaration
  vocabulary;
- one universal semantic base class for all product resources;
- an untyped property dictionary or arbitrary extension bag;
- that a section owns every query capability it presents;
- that fields, columns, row properties, labels, or CLR types imply facets;
- subject acquisition or query execution during capability explanation;
- target-effective availability inferred from installed schema rather than
  supplied by an owner-issued resolved snapshot;
- wildcard, fuzzy, or natural-language resource resolution;
- complete value catalogs embedded in facet explanations;
- JSON Schema embedded in every envelope;
- HAL-JSON as a required or privileged lowering;
- resolved-plan explanation in the first implementation;
- the rename from `library coordinate` to `library address`.

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
schema-declared typed graph projection.

### Treat resolved subjects as installed resources

Rejected. Installed capability explanation must remain deterministic,
acquisition-free, and reusable before the user chooses a package, file,
platform, or Workspace subject. Explicit contextual explanation may add one
detached resolved snapshot for one request, but it does not publish that
snapshot into the installed catalog or reinterpret it as installed
capability.
