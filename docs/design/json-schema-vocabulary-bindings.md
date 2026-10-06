# JSON Schema vocabulary bindings

## Status

This document is the normative owner for **JSON Schema Vocabulary Bindings**,
tracked by
[#8594](https://github.com/richlander/dotnet-inspect/issues/8594).
The pattern is designed but not yet implemented.

[Vocabulary Mappings](vocabulary-mappings.md) supplies stable vocabulary and
term identities. [`ts-jsexport`](ts-jsexport.md) and
`ILInspector.JsExportSurface` supply the authenticated, direction-specific JSON
wire contract. The progressive compact JSONL work tracked by
[Progressive JSONL Delivery](progressive-jsonl-delivery.md) is the first
transport adopter.

## Owner and exact claim

**JSON Schema Vocabulary Bindings** owns:

> Given one authenticated JSON wire contract in one declared direction and one
> exact Vocabulary Mappings snapshot, publish a deterministic JSON Schema
> Draft 2020-12 document plus exact bindings from locations in that schema to
> stable vocabulary terms.

This owner defines:

- stable JSON contract identity;
- exact schema and descriptor identity;
- JSON Schema dialect and deterministic lowering;
- schema-location-to-term bindings;
- exact vocabulary snapshot correspondence;
- binding completeness profiles;
- cache replacement and mismatch behavior; and
- visible rejection of unsupported or ambiguous contracts.

It does not define:

- serializer reachability, direction, member participation, nullability,
  requiredness, union cases, converter evidence, or the existing input/output
  split algorithm;
- vocabulary, term, map, label, or relationship semantics;
- TypeScript declaration names or syntax;
- JSON instance validation as a new runtime admission layer;
- JSONL ordering, chunking, batching, credit, cancellation, or completion;
- a general envelope-contract registry for every product result;
- OpenAPI operations, HTTP endpoints, or servers; or
- product-feature behavior inferred from vocabulary or schema metadata.

The binding describes where one owner-issued semantic term appears in one
exact wire schema. Neither the schema location nor its JSON spelling becomes
the term's identity.

## Product need and production witness

Package Query currently publishes each durable match as a
`BrowserPackageQueryRow` object through the production managed-operation and
Worker event path. The row repeats property names in every event, and
`engine-worker-package-query.ts` parses and validates each complete JSON
document before publication.

The progressive JSONL direction replaces repeated object keys with compact
positional rows. That encoding is efficient only if the consumer can obtain
the same information separately:

- the exact ordered wire shape;
- the stable semantic identity of each position;
- the vocabulary snapshot containing its label and summary; and
- one exact identity proving that the descriptor and streamed rows correspond.

A header record inside the JSONL stream would mix schemas and delay ordinary
record handling. A large enclosing `Content` array would abandon progressive
delivery. A TypeScript-authored column table would recreate product-owned
identity and display data in the host. This design therefore publishes a
separate complete descriptor before any row data flows.

The first contract identity is:

```text
package-query.durable-row
```

Its first published direction is `Serialize`: managed code produces the row
and the Browser consumes it. The identifier is owner-issued. It is not derived
from `BrowserPackageQueryRow`, an export method name, a JSON property name, or
a tuple ordinal.

## Existing direction binding is authoritative

This design does not create another direction model.

`JsExportSurface` already authenticates whether a wire type is reached for
`Serialize`, `Deserialize`, or both. The implemented
`DtsEmitter.WireDeclarationPlan` then:

- retains one direction for one-way types;
- retains one `Both` declaration when input and output projections are equal;
- creates distinct `Deserialize` and `Serialize` declarations when they
  differ; and
- propagates that split through nested, generic, recursive, collection,
  dictionary, and union references.

Implementation extracts that existing algorithm and its direction-qualified
declaration identity into a target-language-neutral wire contract plan in
`ILInspector.JsExportSurface`. The TypeScript emitter continues to consume it
for declaration naming and syntax. JSON Schema Vocabulary Bindings consumes
the same plan for schema structure and binding resolution.

Extraction must not change:

- which contracts are authenticated;
- the meaning of `Serialize`, `Deserialize`, or equivalent `Both`;
- split propagation;
- fail-closed constructor, converter, member-presence, or union behavior; or
- the existing generated TypeScript surface.

There is one directional plan and several projections, not a TypeScript plan
and a separately inferred schema plan.

## Basis

### Existing product owners

- [`ts-jsexport`](ts-jsexport.md) owns generated TypeScript facade policy and
  the existing directional declaration contract. It consumes authenticated
  `JsExportSurface` facts rather than serializer attributes or display text.
- `ILInspector.JsExportSurface` owns authenticated JSON roots, exact
  source-generated serializer context provenance, direction, member presence,
  nullability, naming, unions, and supported converter evidence.
- [Vocabulary Mappings](vocabulary-mappings.md) owns catalog, snapshot,
  vocabulary, term, and map identities. This design references one exact
  snapshot and does not redefine its contents.
- [Inspection Envelope](inspection-envelope.md) owns the completed
  cross-host result boundary.
- [Output Shapes](output-shapes.md) owns current public CLI JSON and JSONL
  compatibility. Compact positional JSONL is an explicit later contract, not
  a silent change to object-per-row `--jsonl`.
- [Resource Explanation](resource-explanation.md) reserves a later general
  envelope-contract catalog. This design is narrower: it describes
  authenticated JS-export wire contracts and does not register every
  `(result_kind, schema_version)` product result.

### In-box `System.Text.Json` exporter

.NET 11 RC1 contains:

```csharp
JsonNode JsonSchemaExporter.GetJsonSchemaAsNode(
    JsonTypeInfo typeInfo,
    JsonSchemaExporterOptions? exporterOptions = null);
```

The inspected platform assembly was
`System.Text.Json` `11.0.0-rc.1.26425.128`, informational version
`11.0.0-rc.1.26425.128+3551975be08744f0418857c5bed8ab1545c5dd47`.
Microsoft documents
[`JsonSchemaExporter`](https://learn.microsoft.com/dotnet/api/system.text.json.schema.jsonschemaexporter)
as exporting the combined JSON serialization contract from `JsonTypeInfo`.
The analogous runtime implementation is visible in
[`dotnet/runtime`](https://github.com/dotnet/runtime/blob/30f6d1ef50d0698ad70b3491f71baf49c5b2af1f/src/libraries/System.Text.Json/src/System/Text/Json/Schema/JsonSchemaExporter.cs).

A source-generated SDK probe established three relevant boundaries:

1. A record with `WhenWriting` and `WhenReading` members produced one schema
   containing both members. The API has no direction parameter.
2. A producer-defined positional-array converter produced the Boolean schema
   `true`; application converters cannot publicly supply the exporter's
   internal schema hook.
3. `TransformSchemaNode` exposes generated schema paths and can add
   annotations, but it does not authenticate a missing directional or custom
   converter shape.

The exporter also requires a live `JsonTypeInfo`. The repository's inspection
and generation path does not load or execute the inspected assembly.

The in-box exporter is therefore an oracle and mapping baseline for overlapping
source-generated contracts, not the production whole-graph generator. The
implementation adds no third-party schema dependency. Focused parity tests
compare supported primitive, collection, dictionary, nullability, naming, and
closed-polymorphism cases with the in-box exporter. Directional and
owner-issued positional cases are generated from the authenticated shared wire
plan.

### JSON Schema Draft 2020-12

The published dialect is:

```text
https://json-schema.org/draft/2020-12/schema
```

[Draft 2020-12](https://json-schema.org/draft/2020-12/json-schema-core)
supplies the standard schema resource model, local references, `prefixItems`
for positional arrays, and annotation keywords.
[RFC 6901](https://www.rfc-editor.org/rfc/rfc6901) JSON Pointers provide exact
locations within the published schema document.

The design deliberately uses an external binding list rather than a custom
JSON Schema vocabulary. General product terms are not validation keywords, and
consumers should not need a custom dialect implementation merely to resolve
display identity.

OpenAPI `readOnly`/`writeOnly` and TypeSpec visibility are evidence that input
and output projections may differ. They are not direction authorities here;
the existing authenticated wire plan is.

## Contract model

One completed description returns:

```text
InspectionEnvelope<JsonSchemaVocabularyDescriptor>
```

Conceptually:

```text
JsonSchemaVocabularyDescriptor
  format version
  stable contract identity
  direction
  schema dialect
  exact schema identity
  exact descriptor identity
  vocabulary catalog identity
  vocabulary snapshot identity
  JSON Schema document
  ordered Bindings
    schema location
    vocabulary identity
    term identity
```

The baseline operation selects one stable contract identity and exactly one
direction. It returns one complete descriptor with no pagination, projection,
or "latest compatible" negotiation.

`Serialize` and `Deserialize` are the only published direction values.
Equivalent input and output plans may reuse one schema identity, but each
requested descriptor still states its direction. A one-way contract is
available only in its authenticated direction.

The schema is a JSON value inside the typed descriptor. Generated TypeScript
represents that value with the existing recursive `JsonValue` contract rather
than a handwritten TypeScript model of every JSON Schema keyword.

## Identity

### Stable contract identity

The producer explicitly issues a stable contract identity such as
`package-query.durable-row`. It identifies the product wire contract across
versions. CLR names, export names, TypeScript declaration names, schema
locations, property spellings, and tuple positions are not contract identity.

The lookup key is:

```text
(contract identity, direction)
```

An unknown key is a visible miss. Lookup does not choose the opposite
direction or a similarly named contract.

### Exact schema identity

The exact schema identity has the initial spelling:

```text
sha256:<lowercase hexadecimal digest>
```

The digest covers a deterministic UTF-8 projection containing the dialect and
all schema assertions and annotations except `$id`. The generator then derives
the schema's `$id` from that digest:

```text
urn:dotnet-inspect:json-schema:sha256:<digest>
```

Excluding `$id` from its own digest avoids a circular definition. Equal
directional schemas produce the same schema identity even when reached through
different directions or stable contract identities.

The canonical projection:

- writes UTF-8 without a byte-order mark or insignificant whitespace;
- orders object keys ordinally;
- preserves array order where JSON Schema assigns order, including
  `prefixItems` and union alternatives;
- sorts set-like arrays such as `required` ordinally; and
- uses one fixed spelling for numbers, strings, escapes, and Boolean schemas.

This is a schema-specific canonical projection, not a new general JSON
canonicalization API.

### Exact descriptor identity

The descriptor identity uses the same `sha256:<digest>` spelling and covers:

- format version;
- stable contract identity;
- direction;
- schema dialect and exact schema identity;
- exact vocabulary catalog and snapshot identities; and
- the ordered schema-location-to-term bindings.

The descriptor embeds the canonical schema value. Its digest uses the same
UTF-8, object-key, primitive, and whitespace rules as the schema projection.
Bindings appear in deterministic schema traversal order; `prefixItems`
bindings therefore retain numeric slot order rather than lexical pointer
order. The descriptor identity excludes itself. A vocabulary label, term
order, binding, schema assertion, or schema location change therefore replaces
the descriptor. A vocabulary-only change does not replace the structural
schema identity.

Neither digest is an authentication or trust claim. They are exact
correspondence and cache-replacement currencies.

## Directional schema lowering

The schema generator consumes one direction-qualified root from the shared
wire contract plan.

### Objects

For each active object contract:

- `properties` uses authenticated JSON property names;
- `required` follows the active direction's required-presence contract;
- null appears in the value schema only when the active direction permits JSON
  null;
- conditionally omitted output members remain optional without introducing
  JSON `undefined`;
- input-only and output-only members appear only in their active schema; and
- output objects reject undeclared properties unless authenticated extension
  data changes that contract, while input openness follows authenticated
  unmapped-member and extension-data behavior.

The current wire plan must gain any missing openness or required-constructor
facts before a schema depending on them can publish. Missing evidence is not
lowered to permissive `additionalProperties` or optional input.

### Collections and dictionaries

Homogeneous arrays and collections use `items`. Supported string-keyed
dictionaries use `additionalProperties` with the exact value schema.
Unsupported key conversion fails before publication.

Nested types select the same active direction. A split nested declaration
therefore cannot accidentally reference the opposite schema.

### Reuse and recursion

Reusable and recursive declarations use local `$defs` and `$ref`. `$defs`
names are deterministic schema-local allocation labels derived from the
direction-qualified declaration identity. They are not product or vocabulary
identities and consumers must not persist them as such.

### Closed unions and discriminators

Closed unions use ordered `anyOf` alternatives from owner-issued case
evidence. Authenticated discriminated object unions include the exact
discriminator property, constant value, and requiredness. Open alternatives,
unknown discriminator behavior, or deserialize-side case selection that the
wire plan does not authenticate fail visibly.

### Standard and custom converters

Built-in converter mappings already authenticated by `JsExportSurface`, such
as supported scalar and semantic string contracts, lower through explicit
known mappings and in-box parity gates.

A producer-defined converter is publishable only when an owner-issued shape
provider contributes an exact target-language-neutral wire node to the shared
plan. The provider declaration must be the same product-owned declaration
consumed by the runtime serializer or writer. Raw handwritten JSON Schema, an
unconstrained Boolean schema, observed sample JSON, or a TypeScript interface
does not authenticate a converter contract.

Unsupported converter evidence rejects the direction. The supported opposite
direction does not authenticate it.

## Positional row contracts

A compact row is an owner-issued positional wire contract, not a CLR tuple
convention and not an array inferred from runtime samples.

The row owner declares one ordered slot sequence. The runtime row writer,
directional wire plan, JSON Schema projection, and vocabulary bindings consume
that same sequence. Each slot has:

- one internal plan-node identity;
- one exact value shape and nullability;
- one stable vocabulary term identity; and
- one ordinal assigned by owner order.

The JSON Schema lowering is:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "urn:dotnet-inspect:json-schema:sha256:<digest>",
  "type": "array",
  "prefixItems": [
    { "type": "string" },
    { "type": "string" },
    { "type": ["string", "null"] }
  ],
  "items": false,
  "minItems": 3,
  "maxItems": 3
}
```

The published bindings use locations:

```text
/prefixItems/0 -> <package-id term>
/prefixItems/1 -> <version term>
/prefixItems/2 -> <description term>
```

The ordinals are structural locations, not semantic identities. Reordering
slots changes the schema and descriptor identities. Every displayable slot in
a compact row uses the `complete-display-slots` profile and must have exactly
one binding.

The JSONL payload remains ordinary JSONL: one complete positional JSON value
per line, one schema per stream, and no header or descriptor record mixed into
the data.

## Vocabulary binding

A producer authors a binding against an exact node identity in the shared wire
plan, not against a JSON property string or JSON Pointer. Construction lowers
the plan and then resolves that node to one RFC 6901 pointer in the final
schema.

Each published binding contains:

- one schema location;
- one vocabulary identity; and
- one term identity.

The descriptor additionally names the exact vocabulary catalog and snapshot
identity. Construction rejects:

- an unknown vocabulary or term;
- a schema location that does not resolve;
- two different terms bound to one location;
- a required complete-profile location with no binding;
- a binding to a node absent in the selected direction; or
- a binding resolved against another schema or vocabulary snapshot.

One term may bind several schema locations. Bindings do not imply that equal
wire types, names, values, or labels identify the same term.

The baseline schema does not copy vocabulary labels or summaries into `title`
or `description`. Consumers resolve display metadata from the exact vocabulary
snapshot. A later deterministic annotation projection may add those
conveniences, but doing so changes the schema identity and does not transfer
authority from the vocabulary.

## API and host use

The host-neutral service accepts:

```text
JsonSchemaVocabularyRequest
  stable contract identity
  direction
  expected vocabulary snapshot identity
```

It returns `InspectionEnvelope<JsonSchemaVocabularyDescriptor>`.

A stale vocabulary snapshot, unknown contract, unsupported direction, or
failed schema construction is a typed non-success outcome with diagnostics.
The service does not silently substitute the latest snapshot or return an
empty schema.

The `ts-jsexport` command is the first CLI host. For explicitly declared
contracts, one generation run emits:

1. direction-specific TypeScript declarations;
2. the exact JSON Schema vocabulary descriptor; and
3. generated facade access to that descriptor.

All three consume the same shared wire plan. TypeScript names remain a
TypeScript projection and do not enter the descriptor identity.

Inspect Web is the first Browser/Wasm host. It obtains the generated
`package-query.durable-row` output descriptor, resolves every binding against
the named Vocabulary Mappings snapshot, and uses those terms for column
identity and display metadata. It does not define a handwritten row interface,
schema, or display-column table.

The schema is descriptive. Neither host is required to run a general JSON
Schema validator on every trusted product-produced value. Runtime probes and
typed decoders retain their existing boundary roles.

## Replacement and cache behavior

Consumers cache by:

```text
(stable contract identity, direction, descriptor identity)
```

The descriptor's schema identity and vocabulary snapshot identity are exact
join currencies. A stream request in #8595 supplies the expected descriptor
identity. The producer rejects a stale or unknown identity before publishing
durable rows.

This deliberately tightens #8595's preliminary phrase "exact schema identity."
Structural schema identity alone cannot detect a changed positional binding or
vocabulary snapshot. The stream joins the complete descriptor identity while
the nested schema identity remains available for structural equivalence and
schema-content caching.

Replacement is whole-descriptor replacement. Bindings from one descriptor are
never merged with a schema or vocabulary snapshot from another. A consumer may
retain older exact descriptors while matching streams remain active, but
"latest" is not a compatibility claim.

Compatibility is explicit:

- equal descriptor identity means byte-for-byte equal canonical descriptor
  content;
- equal schema identity means equal canonical schema content;
- a changed identity is replacement, not proof of breaking or compatible
  change; and
- any future compatibility assessment belongs to a separately owned
  comparison operation.

## Failure and diagnostics

Construction fails before publication for:

- incomplete or conflicting source-generated serializer evidence;
- a direction not authenticated by the shared plan;
- unsupported constructor, member-presence, naming, converter, dictionary,
  union, discriminator, recursion, or positional-row evidence;
- a schema pointer that cannot be resolved uniquely;
- a vocabulary or term missing from the exact snapshot;
- incomplete required positional-slot coverage;
- non-deterministic duplicate contract or binding declarations; or
- inability to produce the canonical schema or descriptor identity.

The CLI fails the generation command. Browser generation publishes no
replacement facade artifact. Runtime descriptor lookup returns a typed
non-success envelope. No path substitutes `true`, `{}`, `unknown`, stale
metadata, or an empty binding list as a successful descriptor.

## Rendering, platform, and trust boundary

The descriptor is structured data, not a Markout rendering domain. The CLI
writes its canonical JSON artifact directly because JSON Schema is itself the
standardized structured format. Human-oriented inspection may later lower the
typed descriptor through Markout without changing the canonical artifact.

The production generator remains SRM-only and does not load or execute the
inspected assembly. It uses authenticated metadata and source-generated
serializer evidence already admitted by `JsExportSurface`.

Runtime consumers use generated data and ordinary JSON parsing. The design
adds no reflection, dynamic assembly loading, runtime code generation,
threads, network fetches, or third-party dependency and remains compatible
with NativeAOT and single-threaded Browser/Wasm.

The initial declarations and vocabulary snapshots are trusted product-authored
data. Schema and descriptor JSON crossing into JavaScript use the existing
generated JSON boundary. This design does not add an untrusted-internet schema
loader or remote `$ref`; all references are local to the published document.

## Demonstration

The first production witness is a Package Query for the real nuget.org asset
`System.Text.Json@10.0.0`. Its durable package-result objects currently repeat
the same property names across rows on the managed Browser/Worker path. The
adoption projects those same result semantics into the declared compact row;
it does not derive the contract from that package's sample values.

Conceptually, the C# producer declares one stable output contract and binds its
slots to owner-issued terms:

```csharp
JsonContractDeclaration packageRows = new(
    "package-query.durable-row",
    JsonWireDirection.Serialize,
    PackageQueryRowWire.Contract,
    PackageQueryRowVocabulary.Bindings);
```

The generated TypeScript facade exposes the corresponding descriptor:

```ts
const descriptor = packageQueryDurableRowDescriptor;
const terms = vocabularies.requireSnapshot(
  descriptor.vocabularySnapshotIdentity);
const columns = descriptor.bindings.map(binding => ({
  location: binding.schemaLocation,
  term: terms.require(binding.vocabulary, binding.term),
}));
```

The API names are illustrative. The contract is one owner-issued declaration
feeding the row writer, TypeScript projection, schema, and bindings.

The neighboring case is one direction-sensitive object used as both input and
output. Its input and output descriptors select the already-planned
`Deserialize` and `Serialize` shapes. They share a schema identity only when
those complete schemas are equal.

## Pathological cases and gates

Implementation must gate:

- a `WhenWriting` member appearing only in the input schema and a
  `WhenReading` member appearing only in the output schema;
- equivalent input and output plans reusing one schema identity;
- nested, generic, collection, dictionary, recursive, closed-union, and
  discriminator schemas selecting the active direction throughout;
- conditional key absence remaining distinct from nullable present values;
- escaped property names producing correct RFC 6901 binding locations;
- one recursive declaration using valid local `$defs` and `$ref`;
- an unsupported custom converter failing instead of publishing `true`;
- one owner-issued positional row producing exact `prefixItems`, `items:
  false`, and equal `minItems`/`maxItems`;
- every displayable positional slot resolving to exactly one vocabulary term;
- a stale vocabulary snapshot or descriptor identity failing before row
  publication;
- equal inputs producing byte-stable schema and descriptor identities;
- a schema assertion, slot order, binding, or vocabulary snapshot change
  replacing the appropriate identity;
- generated TypeScript declarations and JSON Schema selecting the same shared
  directional plan;
- a runtime generated-wrapper probe serializing one positional row whose value
  matches the published schema; and
- strict TypeScript compilation consuming the generated descriptor without a
  handwritten row interface or display-column table.

The shared plan and schema lowering cases belong in the existing
`ILInspector.JsExportSurface.Tests` Release suite. The generated artifact,
compiler mutation, and wrapper probe extend
`eng/test-ts-jsexport-typescript.sh`. The Browser descriptor and vocabulary
join belong in `DotnetInspect.Web.Tests` and the Inspect Web build.

The in-box parity fixture covers only shapes both systems claim. Directional
and positional cases use the product-owned plan as their oracle rather than
weakening the expected schema to match the in-box exporter's broader result.

## Delivery plan

This focused pattern has four counted steps to production:

1. **Focused design - current.** Lock schema, binding, identity, failure, and
   host contracts without redefining direction or vocabulary.
2. **Shared plan and descriptor builder.** Extract the implemented directional
   plan into `ILInspector.JsExportSurface`, add deterministic Draft 2020-12
   lowering and binding validation, and prove in-box parity for overlapping
   shapes.
3. **CLI and generated-facade adoption.** Extend `ts-jsexport` so one explicit
   producer declaration emits TypeScript plus the exact descriptor. Publish
   the `package-query.durable-row` descriptor through the generated Inspect Web
   facade and remove any authored schema or column metadata introduced during
   development.
4. **Progressive JSONL adoption.**
   [Progressive JSONL Delivery](progressive-jsonl-delivery.md) makes the Package
   Query row writer and stream consume the same positional declaration,
   requires the exact descriptor identity in the request and completion, and
   publishes compact JSONL records through the existing event bridge.

Step 2 retires the TypeScript-only location of `WireDeclarationPlan`; it does
not retire or replace its behavior. Step 4 changes only the new compact stream
lane. Existing Package Query object events and public CLI object-per-row JSONL
remain compatible until their own owner explicitly retires them.

## Non-goals

- Another direction enum, split algorithm, or serializer evidence model.
- Treating the in-box combined schema as direction-specific.
- Deriving product identity from CLR names, JSON names, TypeScript names,
  `$defs` labels, JSON Pointers, or tuple positions.
- Hand-maintained JSON Schema or TypeScript row declarations.
- Embedding vocabulary contents in every descriptor or JSONL stream.
- Custom JSON Schema vocabulary keywords for product terms.
- Remote schemas, remote `$ref`, schema registries, or network acquisition.
- General JSON Schema validation of trusted product output.
- Schema compatibility classification.
- OpenAPI, AsyncAPI, HTTP, or server generation.
- Stream lifecycle, batching, sizing, backpressure, cancellation, or terminal
  semantics.
- Migrating every JS-export JSON contract in the first implementation.
- Implementing the general envelope-contract catalog reserved by Resource
  Explanation.
