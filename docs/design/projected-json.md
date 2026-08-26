# Projected JSON output

This document defines the target contract for issue #3494: JSON over the
section/shape model when a caller requests `--fields` or `--columns`. It is a
format contract, not a new shape or a replacement for the established typed
JSON contracts.

Implementation is partial. `find` type/member search and `vocabulary` have
lowered JSON paths; the main `type` and `member` document paths still reject
column projection under `--json`. New command families adopt this contract
individually rather than treating the existing formatter as evidence that every
section is representable.

The pilots do not yet satisfy the full contract. In particular, the current
global rendered-line limiter can truncate their lowered JSON, section and field
keys are still derived from display headings, and section-scoped projection has
not been proven over broad multi-section documents. Issue #4677 owns the
in-progress redesign of semantic item limits versus explicit rendered-line
limits; this contract does not pre-empt that decision. These are hardening work,
not accepted compatibility behavior.

Related docs:

- [Output shapes](output-shapes.md) defines the Document → Table → Vector →
  Scalar ladder and is authoritative for what the projection flags mean.
- [Inspection layers](inspection-layers.md) assigns sections and shapes to L2
  and format selection to L3.
- [Progressive disclosure](progressive-disclosure.md) defines section
  selection, projection validation, and row windows.
- [Output composition](output-composition.md) defines writer capability and
  single- versus multi-section constraints.

## Decision: two JSON dialects

`--json` has two deliberately different inputs:

1. **Typed JSON** is the command's pre-lowered machine contract. It preserves
   native JSON value kinds, nullability, machine property names, and the
   command's established document envelope.
2. **Lowered JSON** is the display view produced after Markout section and
   projection lowering. It preserves the same selected display content as the
   table formats. Values are strings because the current formatter seam carries
   strings.

Payload projection routing has precedence because `--fields`/`--columns` can
select the source column for `--value` or `--print`. After an accepted payload
projection has claimed the request, the routing rule is:

> `--json` uses typed JSON unless an otherwise-unconsumed, non-empty
> `--fields` or `--columns` request selects lowered JSON.

Those flags are the dialect boundary because they name post-lowering
vocabulary. A computed column such as `Return Type` may not exist in the typed
object model, and a field annotation may be incorporated into a rendered graph
label. Applying such a name to the typed graph would be a second, divergent
projection implementation.

No hybrid path may serialize the typed graph and then infer the requested
display columns from JSON property names.

### Routing matrix

| Request | JSON dialect | Required behavior |
| --- | --- | --- |
| `--json` | Typed | Preserve the established typed contract. |
| `--json -S ...` | Typed | Do not lower; the command's typed section-selection contract applies. |
| `--json --rows ...` | Typed | Do not lower; the command's typed row-window contract applies. |
| `--json --compact` | Typed | Do not lower; change whitespace where the typed contract supports it. |
| `--json --fields/--columns ... --value/--print/...` | Payload contract | Resolve the accepted payload projection first; the field/column request selects its source where supported. |
| `--json --fields ...` | Lowered | Apply field-set projection through the section model. |
| `--json --columns ...` | Lowered | Apply table-column projection through the section model. |
| Lowered JSON plus `-S` | Lowered | Select sections before applying per-section projection. |
| Lowered JSON plus `--rows` | Lowered | Window data rows before JSON serialization. |
| Lowered JSON plus `--compact` | Lowered | Change whitespace only. |
| JSON plus `-n`/bare `-N`, `--tail`, or a future `--lines` | Unchanged | Follow the final #4677 limit contract without truncating serialized JSON. |

`-S`, `--rows`, and `--compact` do not opt into lowering. They modify whichever
dialect the request already selected. Every adopted lowered path must honor or
reject these modifiers rather than ignore them. Typed modifier conformance is
separate work: this routing decision neither promises that every command
accepts those combinations nor legitimizes an existing silently dropped
modifier.

Payload projections such as `--count`, `--value`, `--print`, `--urls`, and
`--paths` keep the contracts in [Output shapes](output-shapes.md). An accepted
payload projection claims the request before the JSON dialect is chosen.
`--fields`/`--columns` may then select which source feeds that payload; they do
not opt the enclosing request into lowered document JSON.

Issue #4677 decides the item domain, pipeline order, migration, and exact
relationship among `-n`/bare `-N`, `--tail`, `--rows`, and a future explicit
rendered-line mode. Projected JSON imposes only two format requirements:

- those flags do not choose the typed or lowered dialect; and
- a semantic item/row window is applied before JSON encoding, while any
  rendered-line mode must preserve one complete JSON value or reject the
  combination before stdout.

This work neither aliases `-n` to `--rows` nor mandates a rejection that would
pre-empt #4677.

## Ownership and pipeline

The dialect split does not change layer ownership.

| Decision | Owner |
| --- | --- |
| Parse flags, select JSON format/dialect, commit stdout, choose exit code | L3 CLI |
| Select ordered sections and apply row/field/column shape decisions | L2 section model |
| Resolve projection names against each selected section schema | L2 projection service |
| Produce display strings and stable table keys | Markout lowering |
| Map a representable lowered document to JSON | Lowered JSON formatter |
| Preserve the command's existing typed JSON schema | Typed command serializer |
| Produce inspection facts and typed failures | Owning query/producer |

The normal lowered path is:

```text
parse request
  -> resolve selected sections
  -> resolve projection separately for each selected section
  -> apply the resolved shape/window plan and lower display values
  -> validate/buffer the complete JSON document
  -> commit the document to stdout once
```

The CLI chooses the format but does not reimplement section selection,
projection matching, row ordering, or display-value construction. The JSON
formatter consumes those decisions and reports whether their result is
representable.

The ownership table describes the target boundary. Today
`ProjectionDiagnostics` still lives in the CLI output project; hardening the
shared path includes moving or exposing those decisions at the L2 boundary
rather than duplicating them per command.

## Document envelope and JSON arity

Lowered JSON keeps a document object at the root. Section selection narrows
that object; it does not unwrap it. Selecting one section therefore still gives
an object with one section property. This keeps `-S A` and `-S A,B` in one
stable document contract and agrees with the decision that `-S` selects a typed
document subset without choosing the lowered dialect.

Root fields and section members share the root property namespace. Each
section contributes exactly one JSON value:

| Lowered section content | JSON value |
| --- | --- |
| Field set | Object whose properties are the selected fields |
| Table | Array of row objects |
| List or intrinsic vector | Array of strings |
| Typed tree | Array of node objects with `text`, optional `badge`, and optional `children` |
| Code/text blob or scalar | JSON string, when the formatter receives the payload through a value-bearing callback |

A table projected to one column remains an array of one-property row objects:

```json
{
  "results": [
    { "type": "MemoryCache" },
    { "type": "HybridCache" }
  ]
}
```

The semantic shape is a Vector, but its structured document representation
retains the row key. This makes every projected table row content-identical to
the corresponding JSONL row and avoids a second wire shape when a caller adds
or removes one column. Bare arrays or scalar values remain the domain of
explicit payload projections, not an automatic consequence of `--columns X`.

Empty selected content keeps its container: an empty table is `[]`, an empty
field set is `{}`, and an empty list or tree is `[]`.

The distinct empty outcomes are:

| Condition | Outcome |
| --- | --- |
| A selected section is valid but has zero rows/items after filtering or `--rows` | Emit its empty container. |
| No selected section accepts the requested projection family | Emit the selected sections unchanged in the lowered dialect. |
| A requested name resolves in another selected compatible section but not this one | Omit this projected-out section. |
| A valid projected name has no runtime data in its section | Emit the section's empty container and preserve the existing no-data note on stderr. |
| No requested name resolves in any compatible selected section | Fail before rendering; stdout is empty. |
| The command has an established no-result diagnostic before a document/view exists | Preserve that command contract; no JSON document is required. |
| Selected content is unrepresentable | Fail nonzero; stdout is empty. |

Projected JSON therefore does not collapse "empty data", "projected away",
"invalid projection", and "no command result" into one shape.

## Keys, values, and ordering

### Machine keys

Table property keys must be the stable names used by JSONL for the same table,
not Markdown headings. At the same projection, decoded JSON and JSONL rows must
have the same keys in the same order and the same string values.

Sections and fields should use declared machine names when the section model
provides them. While the formatter seam exposes only display names, converting
them with the shared snake_case policy is an allowed pilot mapping, but those
derived keys are provisional. Before a command family is declared adopted, it
must either wire declared machine identities or publish and gate an explicit
machine-key manifest for every emitted root field, section, and field. Broad
multi-section adoption must not freeze keys derived accidentally from headings.

Every mapped namespace must reject collisions. It must not emit two properties
and rely on a JSON parser to keep the last one.

Collision checks cover:

- root fields against other root fields and section keys;
- section keys against other section keys;
- fields within a field-set section; and
- columns within every row-object schema.

### Display values

Every lowered leaf value is the display string Markout supplied. The formatter
must not guess that `"yes"` is a boolean, that `"3"` is a number, or that an
empty string is null. Display containment has already happened during
lowering; the JSON writer escapes that string but does not decode or reinterpret
it.

Typed JSON remains the path for native booleans, numbers, nulls, enums, and
typed graph identities.

### Deterministic order

Although JSON object order is not semantic, emitted text is deterministic:

1. root fields retain authored order;
2. sections retain the resolved section order;
3. fields and columns retain projection order, or authored order when not
   explicitly reordered;
4. rows retain producer order after filtering and `--rows`; and
5. tree children and list items retain producer order.

`--compact` changes indentation and insignificant whitespace only.

## Section-scoped projection

Projection is resolved after effective section selection and before lowering.
It is never applied as one global column set to every table in a multi-section
document.

For each selected section:

1. identify whether `--fields` or `--columns` applies to that section kind;
2. resolve requested names against that section's schema;
3. preserve requested-name order for names that resolve; and
4. lower the section with only its resolved projection.

The two projection families remain distinct:

- `--fields` filters named entries in field-set sections. It does not reinterpret
  table columns.
- `--columns` filters columns in table sections. It does not reinterpret a
  field set as a synthetic `Field`/`Value` table.

Across multiple compatible sections, a requested name is valid when it resolves
in at least one selected section. A compatible section with no resolved names
contributes no projected section. A projection family that does not apply to a
section kind leaves that kind unchanged, matching the behavior of the other
formats. If none of the selected sections accepts that projection family, the
request is the same no-op in every lowered format rather than an unmatched-name
error.

Partially unknown requests retain the existing diagnostic contract: known names
render, unknown names produce warnings with discovery guidance. If no requested
name resolves anywhere it can apply, the request fails before rendering.
Valid names that have no data may produce a note on stderr; they are not
reclassified as unknown.

This partitioning is required even if the underlying Markout API still exposes
one global `IncludeColumns` collection. An implementation may serialize
sections separately or introduce a per-section projection plan, but it may not
hand the global set to unrelated sections and accept whichever one throws
first.

## Representability

A lowered JSON request is accepted only when every contributing section can be
represented without inventing, dropping, merging, or relabelling content.

The target representation covers:

- grouped field sets;
- one table schema per section, including repeated writes with the same schema;
- lists; and
- trees delivered with typed parent/child relationships.

Code/text blobs and scalar sections become representable only when their full
payload reaches the formatter through a value-bearing callback. Writing prose
to a discarded text stream is not representation.

The following conditions are unrepresentable until a specific JSON shape is
designed:

- one section mixes fields, tables, lists, trees, or blobs;
- one section emits tables with different schemas;
- a row has more cells than its table schema;
- a field callback supplies a key without its value;
- a streaming tree callback supplies presentation prefixes instead of typed
  parent/child relationships;
- machine-key normalization produces a duplicate;
- one lowered request contains multiple independent subject documents without
  an explicit array/envelope contract; or
- a selected section uses a formatter callback the JSON formatter does not
  understand.

Short table rows follow the table/JSONL contract: absent trailing cells remain
absent properties. The formatter must not invent nulls.

An unrepresentable selected section is a visible error. The command must name
the section and unsupported condition, exit nonzero, and leave stdout empty.
It must not omit the section, fall back to typed JSON, or emit the unprojected
document.

## Atomic stdout and diagnostics

Projected JSON is transactional with respect to product validation and
rendering:

1. route and validate the request;
2. buffer the complete lowered document;
3. finish all structural checks and JSON encoding; and
4. write the completed document to stdout.

Any projection, representability, or serialization failure before step 4
produces no stdout bytes. Once output-sink I/O begins, ordinary sink failures
remain I/O failures; the contract does not claim that a broken pipe can be
rolled back.

Diagnostics are written only to stderr:

- the L2 projection service owns unknown-name, suggestion, and no-match facts;
- the formatter owns structural and representability failures;
- the CLI adds command/section context, selects the exit code, and may suggest
  a supported alternate format.

Known failures must not be collapsed into a broad success-shaped `{}` or `[]`.
Likewise, a warning or no-data note must not be embedded in the JSON document.
Typed producer failures that are already part of a command's output model
remain data; rendering failures remain diagnostics.

## Compatibility boundary

The typed and lowered dialects have separate compatibility promises.

### Typed JSON

Adding projected JSON must not change plain `--json`:

- document envelope and property names;
- JSON value kinds and null behavior;
- ordering where the existing command guarantees it;
- compact versus indented behavior; and
- source-generated serialization/AOT characteristics.

A fast path is acceptable only if it is observably equivalent to the established
typed contract. The lowered formatter is not a reason to delete a typed DTO or
source-generation context.

### Lowered JSON

Lowered JSON promises:

- the section, row, field, and column selection of the equivalent display view;
- table keys and decoded row values equal to JSONL at the same projection;
- string leaf values;
- deterministic output order;
- valid complete JSON on successful document emission; and
- visible failure when selected content is unrepresentable.

Escaping and insignificant whitespace need not be byte-identical to JSONL.
Consumers compare decoded content.

Replacing a prior fail-closed `--json --fields/--columns` combination with this
output is additive. Once a command ships lowered JSON, changing its strings to
inferred native JSON types is breaking. A future typed Markout seam may support
a separately designed contract, but it must not silently change this dialect's
value kinds.

The dialect boundary itself may change the root kind. For example, a command's
typed JSON may be a root array while its lowered JSON is a document object
containing the projected section. That change is intentional because adding
`--fields`/`--columns` explicitly selects the other dialect.

Table machine keys are already covered by the lowered compatibility promise.
Heading-derived root, section, and field keys remain provisional until the
command passes its machine-key manifest gate. After adoption, adding a root
field or section is compatibility-significant because both share the root
namespace and must remain collision-free.

## Adoption sequence

Adopt one coherent command family at a time.

1. **Harden the shared path.** Add dialect routing, section-scoped projection,
   declared machine-key plans, representability preflight, transactional
   stdout, and integration with the final #4677 limit contract around the
   existing `find`/`vocabulary` formatter. Move or expose projection decisions
   at the L2 boundary.
2. **Adopt single-document table and field views.** Start with `type` and
   `member`, using their existing section manifests and projection diagnostics.
3. **Adopt multi-section library/package documents.** Partition projection per
   selected section and reject unsupported sections before running the writer.
4. **Adopt lists and typed trees.** Prove hierarchy and ordering without
   recovering them from rendered text.
5. **Design remaining envelopes.** Add blobs, mixed-content sections, and
   multi-subject output only after each has an explicit JSON representation.

Each slice keeps plain `--json` on the typed path and removes that command's
fail-closed guard only when its lowered path passes the required gates.

## Gates

Existing pilot coverage proves only the currently wired slice:

- `JsonSectionFormatterTests.Projection_ChangesTheJson` and
  `SectionSelection_ChangesTheJson` gate that section/shape decisions reach the
  formatter.
- `Find_ProjectedJsonRows_CarryTheSameContentAsJsonl` gates decoded row parity
  and machine keys.
- `Find_RowWindowUnderProjectedJson_MatchesTheTableFormats` and
  `Find_RowWindowUnderProjectedJson_KeepsTheDocumentParsable` gate row-window
  identity and valid empty windows.
- `Find_CompactUnderProjectedJson_IsHonored` gates compact output.
- The mixed-content, duplicate-key, over-wide-row, and streaming-callback tests
  in `JsonSectionFormatterTests` gate visible failure for the formatter's known
  loss cases.

The full contract remains **unverified** until these required future gates are
implemented:

| Required future gate | Claim it must enforce |
| --- | --- |
| `JsonDialectRoutingTests` | Only otherwise-unconsumed, non-empty `--fields`/`--columns` select lowered JSON; payload projections win, and `-S`, `--rows`, and `--compact` do not lower. |
| `ProjectedJsonTypedCompatibilityTests` | Adopting a command does not change its plain typed `--json` schema or value kinds. |
| `ProjectedJsonSectionConformanceTests` | Every adopted section kind maps to the documented envelope and arity. |
| `ProjectedJsonSectionScopedProjectionTests` | Multi-section projections resolve independently and preserve requested ordering. |
| `ProjectedJsonMachineKeyTests` | Every adopted root field, section, field, and column has a unique pinned machine key independent of display-heading changes. |
| `ProjectedJsonDiagnosticsTests` | Partial, unmatched, projected-away, empty, no-result, no-data, and unrepresentable requests have the documented output/stderr/exit behavior. |
| `ProjectedJsonAtomicityTests` | Every pre-commit projection/formatter failure leaves stdout empty; removing the buffer fails the test. |
| `ProjectedJsonWindowingTests` | Under the final #4677 contract, semantic item/row windows happen before encoding and any rendered-line mode emits one complete JSON value or rejects with empty stdout. |
| `ProjectedJsonFormatParityTests` | Every adopted table section has decoded key/order/value parity with JSONL from the same `-S <section>` shape. |
| `ProjectedJsonNativeAotSmoke` | A published NativeAOT CLI executes both dialects without reflection fallback or trim/AOT warnings on the emit path. |

Every adoption PR adds a command-level non-vacuity test that removes or bypasses
the lowered route and then fails because the projection no longer changes the
JSON.

## Non-goals

- No new flag, section, category, or shape rung.
- No jq-like query language over typed JSON.
- No conversion of display strings back into guessed native types.
- No requirement to migrate every typed JSON command in one change.
- No retroactive promise that every typed JSON command already supports `-S`
  or `--rows`; silently dropped typed modifiers remain separate defects.
- No decision about the `-n` item domain, `--rows` interaction, `--lines`
  grammar, or migration; issue #4677 owns those contracts.
- No silent fallback from requested lowered JSON to typed or unprojected JSON.
- No reconstruction of sections, rows, or trees from rendered Markdown.
- No assumption that a future typed Markout seam may change the shipped lowered
  dialect without a compatibility decision.
