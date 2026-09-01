# Projected JSON output

This document defines the target contract for issue #3494: JSON over the
section/shape model when a caller requests `--fields` or `--columns`. It is a
format contract, not a new shape or a replacement for the established typed
JSON contracts.

**Lowered** names the point where this JSON path joins Markout. The command
builds the same Markout view used by Markdown, TSV, and JSONL. Product-owned
section/view adapters resolve section identities, construct Markout inline
values, and supply stable table keys. The resolved writer plan is then passed
to Markout, which mechanically applies its section/projection/window options
while serializing the view. A JSON formatter receives the resulting field,
table, list, and tree callbacks, renders semantic inline slots with Markout's
plain-text semantics, and assembles them into one JSON document. It is a
sibling formatter, not a projection over the typed JSON graph and not a parser
for rendered Markdown or table text.

The current Markout formatter seam carries keys and values as strings, but
inline value slots can still contain Markout semantic markup such as
`<code>...</code>` with XML-escaped content. Product adapters own how typed
facts become those inline representations; Markout owns their interpretation
and the delivery of lowered callbacks; the JSON formatter owns JSON
containers, structural loss checks, and JSON escaping after applying Markout's
public inline-to-plain rendering. This string-valued seam is why lowered JSON
intentionally differs from the pre-lowered typed JSON contract described
below.

Implementation is partial. `find` type/member search and `vocabulary` have
lowered JSON paths; the main `type` and `member` document paths still reject
column projection under `--json`. `project` also rejects projection, while
`library`, `package`, `timeline`, `implements`, and `extensions` reject
otherwise-unclaimed `--json --fields/--columns` requests at the typed-document
serializer boundary. Discovery owns projected JSON for its `Name`/`Kind` row
schema under the lens contract; unadopted lens and nested routes such as
`library --il-offsets` and `package search` reject. New command families adopt
this contract individually rather than treating the existing formatter as
evidence that every section or route is correct.

The pilots do not yet satisfy the full contract. In particular, the current
global rendered-line limiter can truncate their lowered JSON, section and field
keys are still derived from display headings, and section-scoped projection has
not been proven over broad multi-section documents.
[Item and line limits](item-and-line-limits.md) now owns the settled target:
semantic item/range windows happen before encoding, and line windows modify
each printable content value rather than truncating serialized JSON. These are
hardening targets, not accepted compatibility behavior.

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

Lens routing has first precedence. An accepted lens such as `-D`/`--discover`
owns its payload and returns before the normal section pipeline; its own
projection contract must not be reinterpreted as a lowered document request.
Payload projection routing follows because `--fields`/`--columns` can select
the source column for `--value` or `--print`. After no lens or payload
projection has claimed the request, the routing rule is:

> `--json` uses typed JSON unless an otherwise-unclaimed, non-empty
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
| `--json --rows ...` | Typed | Do not lower; the command's typed absolute-range contract applies. |
| `--json --compact` | Typed | Do not lower; change whitespace where the typed contract supports it. |
| `--json -D ... --fields/--columns ...` | Lens contract | Let discovery own its JSON and projection; do not enter document routing. |
| `--json --fields/--columns ... --value/--print/...` | Payload contract | Resolve the accepted payload projection first; the field/column request selects its source where supported. |
| `--json --fields ...` | Lowered | Apply the selected section's declared field/annotation projection. |
| `--json --columns ...` | Lowered | Apply table-column projection through the section model. |
| Lowered JSON plus `-S` | Lowered | Select sections before applying per-section projection. |
| Lowered JSON plus `-n`/`--rows` | Lowered | Select semantic items/ranges before JSON serialization. |
| Lowered JSON plus `--compact` | Lowered | Change whitespace only. |
| Printable JSON plus `-n N --lines` | Payload contract | Clip each selected content value before serialization; never truncate encoded JSON. |

`-S`, `-n`, `--rows`, and `--compact` do not opt into lowering. They modify
whichever dialect the request already selected. Every adopted lowered path must
honor or reject these modifiers rather than ignore them. Typed modifier
conformance is separate work: this routing decision neither promises that every
command accepts those combinations nor legitimizes an existing silently
dropped modifier.

Payload projections such as `--count`, `--value`, `--print`, `--urls`, and
`--paths` keep the contracts in [Output shapes](output-shapes.md). An accepted
payload projection claims the request before the JSON dialect is chosen.
`--fields`/`--columns` then select which source feeds that payload where
supported; applicability and source selection are validated before `--count`
reduces the rows, and an unsupported request rejects. They do not opt the
enclosing request into lowered document JSON.

Lens modes keep the same precedence. Discovery, package-content, version,
layout, and other lens-owned output either honors its own accepted projection
or rejects it under the lens contract; a central JSON router may not pull that
request into the normal lowered-document path.

The settled [item and line limit](item-and-line-limits.md) contract imposes
three format requirements:

- limit flags do not choose the typed or lowered dialect;
- semantic item/range windows apply before JSON encoding; and
- printable line windows clip each content string before encoding, preserving
  one complete structured success/failure object per selected row.

Non-print document JSON has no textual payload to line-window and rejects
`--lines` before stdout.

## Ownership and pipeline

The dialect split does not change layer ownership.

| Decision | Owner |
| --- | --- |
| Parse flags, select JSON format/dialect, commit stdout, choose exit code | L3 CLI |
| Select ordered sections and apply row/field/column shape decisions | L2 section model |
| Resolve projection names against each selected section schema | L2 projection service |
| Produce display strings, section identities, and stable table keys | L2 section/view adapters |
| Apply the resolved writer plan and deliver formatter callbacks | Markout serialization |
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
| Unlabeled list or intrinsic vector | Array of strings |
| One or more labeled arrays | Object whose properties are label keys and whose values are arrays of strings |
| Typed tree | Array of node objects with `text`, optional `badge`, optional structural `state`, and optional `children` |
| Code/text blob or scalar | JSON string, when the formatter receives the payload through a value-bearing callback |

Typed-tree nodes preserve Markout's structural node state. `state` is omitted
for `Normal`; a non-normal state uses its stable lower_snake_case machine name,
currently `"revisit"`. State is not a badge and is not gated by badge
selection. Node properties emit in `text`, `badge`, `state`, `children` order,
omitting inapplicable optional properties. A revisit node with no children must
therefore remain distinguishable from a normal leaf:

```json
[
  {
    "text": "Shared dependency",
    "state": "revisit"
  }
]
```

Markout supplies each labeled array as a label plus its values. Lowered JSON
must retain both:

```json
{
  "overview": {
    "target_frameworks": ["net8.0", "net9.0"],
    "package_types": ["Dependency"]
  }
}
```

Labels use the machine-key rules below. Multiple labeled arrays may share a
section only when their mapped keys are unique. A section that mixes labeled
arrays with unlabeled list items is unrepresentable until a different envelope
is designed; flattening either form would lose identity.

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
field set is `{}`, and an empty unlabeled list or tree is `[]`. An empty
labeled-array section is `{}`. If an applicable labeled array has no values
after filtering or windowing, its key remains present with `[]`; this preserves
the array's identity and sibling boundary. A label disappears only when section
selection or projection makes that array inapplicable before lowering.

The distinct empty outcomes are:

| Condition | Outcome |
| --- | --- |
| A selected section is valid but has zero rows/items after filtering or `--rows` | Emit its empty container. |
| Every selected section declares `PassThrough` for the projection family | Emit the selected sections unchanged in the lowered dialect. |
| A requested name resolves in another selected `Project` section but not this one | Omit this projected-out section. |
| Every selected section is `Incompatible` for the projection family | Fail before rendering; stdout is empty. |
| A valid projected name has no runtime data in its section | Emit the section's empty container and preserve the existing no-data note on stderr. |
| At least one selected section is `Project`, but no requested name resolves in any selected `Project` section | Fail before rendering; stdout is empty. |
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
them with the shared snake_case policy is an allowed transitional mechanism.
The keys already emitted by the shipped `find` and `vocabulary` pilots are
nevertheless compatibility-significant now; changing `results`, `members`, or
any shipped vocabulary section key requires an explicit migration. Before a
new command family is adopted, it must either wire declared machine identities
or publish and gate an explicit machine-key manifest for every emitted root
field, section, field, and labeled array.

Every mapped namespace must reject collisions. It must not emit two properties
and rely on a JSON parser to keep the last one.

Collision checks cover:

- root fields against other root fields and section keys;
- section keys against other section keys;
- fields within a field-set section; and
- columns within every row-object schema; and
- labeled arrays within their section object.

### Display values

Every lowered leaf value is a string. Inline value slots, including field
values, table cells, unlabeled or labeled-array items, and tree text or badges,
can arrive with Markout semantic inline markup. Before JSON encoding, the
formatter must apply the same `FormatHelper.RenderInlinePlainText` semantics
that JSONL uses: remove the semantic tags and decode their escaped text. This
is Markout-owned inline rendering, not parsing rendered Markdown, TSV, or
JSONL.

Literal blob or code payload callbacks are not inline slots and retain their
payload unchanged. After the slot-specific rendering step, the JSON writer
escapes the resulting string but does not otherwise decode, reinterpret, or
infer a type. It must not guess that `"yes"` is a boolean, that `"3"` is a
number, or that an empty string is null.

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

For each selected section and each active projection family:

1. read that section's declared disposition for `--fields` or `--columns`;
2. resolve the family's requested names against the schema of every `Project`
   section;
3. preserve requested-name order for names that resolve; and
4. compose the family plans into one retained-section plan before lowering.

Each section declares one disposition per projection family:

- **`Project`** — the family addresses this section's schema. Resolve names and
  lower only the requested subset.
- **`PassThrough`** — the family does not address this section, but established
  format behavior keeps the section unchanged.
- **`Incompatible`** — retaining the section would answer a different question.
  It can be omitted only when that family successfully projects another
  selected section; a family with no `Project` section and at least one
  `Incompatible` section cannot be answered and fails the request.

The two projection families remain distinct by default:

- `--fields` selects the named field/annotation vocabulary declared by the
  section. It filters entries in field-set sections. For graph/tree sections it
  selects cue annotations that the product adapter incorporates into the
  lowered node labels or badges before Markout serialization. It does not
  generally reinterpret table columns.
- `--columns` filters columns in table sections. It does not reinterpret a
  field set as a synthetic `Field`/`Value` table.

Ordinary tables are `Project` for `--columns` and may be `PassThrough` for
`--fields`. Graph/tree sections with declared cue annotations are `Project` for
`--fields` and `Incompatible` for `--columns`. Therefore a selected Call Graph
is omitted when `--columns` successfully projects a companion facts table, and
a graph-only column request fails rather than emitting the unprojected graph.

When both projection families are non-empty, each family must be answerable on
its own:

- a family with at least one selected `Project` section must resolve at least
  one requested name in that family;
- a family for which every selected section is `PassThrough` is a no-op; and
- a family with no `Project` section and at least one `Incompatible` section
  fails the whole request.

After those checks, a section projected by any active family is retained and
applies every family for which it is `Project`. An `Incompatible` disposition
from another active family does not veto that section, because the section is
answering an explicit projection that addresses its own schema. A section with
no `Project` disposition is omitted when any active family marks it
`Incompatible`; it remains unchanged only when every active family is
`PassThrough`.

For example, a Call Graph plus a facts table under simultaneous `--fields` and
`--columns` retains both sections: fields project graph cues and columns
project the table. The same combined request against a graph alone fails
because the column family has no `Project` section. This composition preserves
the independent meaning of both flags instead of silently discarding either
request.

Lowered tree JSON carries graph cue annotations in the resulting `text` or
`badge` values, exactly as the display adapter produced them. It does not
reconstruct native annotation properties from those strings. A graph lowering
that cannot deliver the selected cues through a structured tree/graph callback
is unrepresentable and fails visibly rather than emitting the unannotated
graph.

`vocabulary` is one explicit shipped compatibility exception. That command
accepts `--fields` as an alias for table-column projection, so
`vocabulary --fields Section` and `--columns Section` retain equivalent rows
and diagnostics. The alias is resolved by the command before the generic
section plan and must not spread to other table commands.

Within each active family, a requested name is valid when it resolves in at
least one `Project` section. A `Project` section with no resolved names
contributes no projected section for that family. `PassThrough` sections do
not participate in name matching. `Incompatible` sections follow the
composition rule above. A family with only `PassThrough` sections is the same
no-op in every lowered format rather than an unmatched-name error.

Partially unknown requests retain the existing diagnostic contract independently
per family: known names render, and unknown names produce warnings with
discovery guidance. If a family has at least one `Project` section but none of
its requested names resolves there, the request fails before rendering. Valid
names that have no data may produce a note on stderr; they are not reclassified
as unknown.

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
- unlabeled lists;
- one or more uniquely keyed labeled arrays; and
- trees delivered with typed parent/child relationships.

Code/text blobs and scalar sections become representable only when their full
payload reaches the formatter through a value-bearing callback. Writing prose
to a discarded text stream is not representation.

The following conditions are unrepresentable until a specific JSON shape is
designed:

- one section mixes fields, tables, lists, trees, or blobs;
- one section mixes labeled arrays with unlabeled list items;
- a labeled array has no usable key or collides with another mapped label;
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

Short table rows follow the pinned JSONL contract: every declared column key
remains present, and absent trailing cells are padded with `""`. The formatter
must not invent nulls or omit keys. Rows wider than the declared schema remain
unrepresentable.

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

Replacing a genuinely fail-closed `--json --fields/--columns` combination with
this output is additive. That applies to routes such as current `type` and
`member`, which reject rather than return a document.

Before the routing audit, `library`, `package`, `timeline`, `implements`, and
`extensions`, plus early-return discovery, IL-offset, and nested package-search
routes, succeeded after silently dropping the projection. Establishing visible
routing was therefore an explicit compatibility change: discovery now honors
the request under its lens contract, while the unadopted routes reject before
writing stdout. Replacing those rejections with conforming lowered JSON is
additive. A future route found to succeed after dropping projection must
likewise establish visible fail-closed routing or an approved migration before
adoption; it may not call the change additive.

Once a command ships lowered JSON, changing its strings to inferred native JSON
types is breaking. A future typed Markout seam may support a separately designed
contract, but it must not silently change this dialect's value kinds.

The dialect boundary itself may change the root kind. For example, a command's
typed JSON may be a root array while its lowered JSON is a document object
containing the projected section. That change is intentional because adding
`--fields`/`--columns` explicitly selects the other dialect.

Every key already emitted by a shipped lowered path is covered by the
compatibility promise, including heading-derived root, section, and field keys.
The derivation mechanism may be replaced, but changing the emitted key requires
an explicit migration. Adding a root field or section is
compatibility-significant because both share the root namespace and must remain
collision-free.

Shipped route semantics are compatibility-significant as well.
`vocabulary --fields` remains a table-column alias; conforming other commands
to the default field/column distinction must not remove or generalize that
exception accidentally.

## Adoption sequence

Adopt one coherent command family at a time.

1. **Harden the shared path.** Add dialect routing, section-scoped projection,
   combined-family composition, lens precedence, labeled-array preservation,
   Markout inline-to-plain rendering, pinned machine-key plans, graph-field
   parity, the pinned `vocabulary --fields` alias, representability preflight,
   transactional stdout, and integration with the item/range/line limit
   contract around the existing `find`/`vocabulary` formatter. Move or expose
   projection decisions at the L2 boundary.
2. **Audit every projection-capable route.** Prove that each accepted
   `--json --fields/--columns` request is owned by a lens/payload, rendered as
   lowered JSON, or rejected visibly. Add fail-closed routing or an explicit
   migration before changing a route that currently succeeds after dropping
   the projection.
3. **Adopt single-document table and field views.** Start with `type` and
   `member`, using their existing section manifests and projection diagnostics.
4. **Adopt multi-section library/package documents.** Partition projection per
   selected section and reject unsupported sections before running the writer.
5. **Adopt lists and typed trees.** Prove labels, hierarchy, and ordering without
   recovering them from rendered text.
6. **Design remaining envelopes.** Add blobs, mixed-content sections, and
   multi-subject output only after each has an explicit JSON representation.

Each slice keeps plain `--json` on the typed path. A fail-closed command removes
its guard only when its lowered path passes the required gates; a silently
dropping command follows the separate migration rule above.

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
- `ShortTableRows_PreserveEveryKeyWithEmptyStringPadding` gates JSONL-compatible
  trailing-cell padding.
- `LabeledArrays_PreserveKeysEmptyValuesAndSiblingBoundaries`,
  `LabeledAndUnlabeledLists_FailRatherThanFlattening`, and
  `LabeledArraysSharingAMachineKey_FailRatherThanMerging` gate labeled-array
  identity and losslessness.
- `UnlabeledLists_NormalizeInlineValuesWithoutInferringTypes` and
  `FieldsAndTrees_NormalizeInlineValuesAndPreserveTreeState` gate supported
  inline slots, string value kinds, deterministic tree properties, and
  structural node state. `TreeBadgeSelection_DoesNotSuppressStructuralState`
  gates badge selection independently from structural state.
- `DeferredSerialization_SnapshotsBatchRowsAndTypedTrees` gates formatter
  ownership of mutable callback inputs until the buffered document is encoded.
- The mixed-content, duplicate-key, over-wide-row, and streaming-callback tests
  in `JsonSectionFormatterTests` gate visible failure for the formatter's known
  loss cases.
- `ProjectedJsonRoutingAudit_InventoryIncludesEveryProjectionCapableCommand`
  recursively fixes the audited executable-route set using each route's own
  and inherited options. The
  `ProjectedJsonRoutingAudit_*TypedDocumentFailsClosed`,
  `ProjectedJsonRoutingAudit_*DiscoveryOwnsProjectedJson`,
  `ProjectedJsonRoutingAudit_EffectiveDiscoveryProjectionPreservesRows`,
  `ProjectedJsonRoutingAudit_EmptyEffectiveDiscoveryValidatesProjection`,
  `ProjectedJsonRoutingAudit_IlOffsetsProjectionFailsClosed`,
  `ProjectedJsonRoutingAudit_MultiPackagePayloadProjectionsFailClosed`,
  `ProjectedJsonRoutingAudit_NarrowedDiscoveryOwnsProjectionValidation`,
  `ProjectedJsonRoutingAudit_PackageLensFieldsFailBeforeAcquisition`,
  `ProjectedJsonRoutingAudit_PackageLensPayloadFailsBeforeAcquisition`,
  `ProjectedJsonRoutingAudit_PackageLensRoutesFailClosed`,
  `ProjectedJsonRoutingAudit_PackageSearchCountProjectionFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchCountRejectsCappedSource`,
  `ProjectedJsonRoutingAudit_PackageSearchCountAndTakeRejectBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchEmptyWindowIsNotAnEmptySearch`,
  `ProjectedJsonRoutingAudit_PackageSearchDirectionRequiresCarrier`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritedDiscoveryFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritedModesFailBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritedPayloadFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritedProjectionFailsClosed`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritedInvalidItemLimitFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritedWindowAndCountAreRejected`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritsPrerelease`,
  `ProjectedJsonRoutingAudit_PackageSearchInvalidItemLimitFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchDuplicateLimitIsContained`,
  `ProjectedJsonRoutingAudit_PackageSearchLineWindowUsesTakeForAcquisition`,
  `ProjectedJsonRoutingAudit_PackageSearchInheritedLineWindowUsesTakeForAcquisition`,
  `ProjectedJsonRoutingAudit_PackageSearchTrailingExplicitTailAppliesLineWindow`,
  `ProjectedJsonRoutingAudit_PackageSearchItemLimitConflictsFailBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchItemLimitSpellingsAreEquivalent`,
  `ProjectedJsonRoutingAudit_PackageSearchItemLimitWorksAfterSubcommand`,
  `ProjectedJsonRoutingAudit_PackageSearchInvalidWindowFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchOutputPathFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchParentTargetFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchProjectionListFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchTailItemLimitFailsBeforeNetwork`,
  `ProjectedJsonRoutingAudit_PackageSearchWindowConflictsFailBeforeNetwork`,
  `ProjectedJsonRoutingAudit_TypeShapeFailsClosed`, and
  `ProjectedJsonRoutingAudit_TypeShapePayloadProjectionsFailClosed`
  tests, together with the existing `type`, `member`, `project`, `find`,
  `vocabulary`, and payload-projection tests, gate that every current route
  lowers, rejects, or is claimed before typed-document serialization.

The full contract remains **unverified** until these remaining future gates are
implemented:

| Required future gate | Claim it must enforce |
| --- | --- |
| `JsonDialectRoutingTests` | Lenses and payload projections claim requests first; only otherwise-unclaimed, non-empty `--fields`/`--columns` select lowered JSON, while `-S`, `--rows`, and `--compact` do not lower. |
| `ProjectedJsonTypedCompatibilityTests` | Adopting a command does not change its plain typed `--json` schema or value kinds. |
| `ProjectedJsonSectionConformanceTests` | Every adopted section kind maps to the documented envelope and arity. |
| `ProjectedJsonLabeledArrayTests` | Labeled-array keys, empty applicable labels, section object type, and sibling boundaries survive; labeled/unlabeled mixtures and mapped-key collisions fail before stdout. |
| `ProjectedJsonTreeTests` | Typed-tree hierarchy, authored order, badges, and non-normal structural state survive; a normal leaf and a childless `revisit` node remain distinguishable. |
| `ProjectedJsonInlineValueTests` | Markout semantic inline markup is rendered to plain text in fields, table cells, unlabeled and labeled-array items, and tree text/badges; literal blob/code payloads remain unchanged, and primitive-looking strings remain strings. |
| `ProjectedJsonSectionScopedProjectionTests` | Multi-section projections honor each section's `Project`/`PassThrough`/`Incompatible` disposition, including graph-plus-table omission, graph-only failure, and simultaneous field/column composition without discarding either family, and preserve requested ordering. |
| `ProjectedJsonGraphFieldTests` | Requested graph/tree cue fields change lowered `text`/`badge` content exactly as in the display view; unrequested cues stay absent and unsupported graph lowerings fail visibly. |
| `ProjectedJsonLegacyAliasTests` | Shipped `vocabulary --fields` and equivalent `--columns` requests retain decoded row and diagnostic parity without enabling that alias for other table commands. |
| `ProjectedJsonMachineKeyTests` | Every shipped or newly adopted root field, section, field, column, and labeled array has a unique pinned machine key independent of display-heading changes. |
| `ProjectedJsonDiagnosticsTests` | Partial, unmatched, projected-away, all-`PassThrough`, empty, no-result, no-data, and unrepresentable requests have the documented output/stderr/exit behavior; unmatched-name failure requires at least one applicable `Project` section. |
| `ProjectedJsonAtomicityTests` | Every pre-commit projection/formatter failure leaves stdout empty; removing the buffer fails the test. |
| `ProjectedJsonWindowingTests` | Semantic item/range windows happen before encoding; multi-print line windows modify each content value; every structured result remains complete. |
| `ProjectedJsonFormatParityTests` | Every adopted table section has decoded key/order/value parity with JSONL from the same `-S <section>` shape, including Markout semantic inline values and empty-string padding for short rows. |
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
  or `--rows`; silently dropped typed modifiers remain defects.
- No compatibility waiver for commands that currently drop
  `--fields`/`--columns`; their migration must be explicit.
- No general field-to-column alias; the shipped `vocabulary` exception is
  command-owned and gated.
- No second item-domain, range, line-window, or multi-print contract; this
  document consumes [Item and line limits](item-and-line-limits.md).
- No silent fallback from requested lowered JSON to typed or unprojected JSON.
- No reconstruction of sections, rows, or trees from rendered Markdown.
- No assumption that a future typed Markout seam may change the shipped lowered
  dialect without a compatibility decision.
