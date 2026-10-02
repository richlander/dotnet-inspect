# Type declared-method population

## Status

This focused design owns the source-native Count contract for one exact Type's
declared `MethodDef` population. Metadata owns the physical population.
QuerySpace owns Count/Rows terminal routing, and Sections exposes the detached
result through `InspectionEnvelope<T>`. The performance harness is the first
production host; a CLI or Inspect Web surface is intentionally deferred until
this population proves its physical value.

## Owner and exact claim

Given one TypeDef binding authenticated to an immutable assembly image:

- Count is the exact cardinality of that TypeDef's metadata method range;
- Count reads no MethodDef row, decodes no method name, signature, or
  attribute, and projects no row;
- Rows returns one MethodDef token row for every handle in the same range; and
- Count and complete Rows have the same cardinality.

The binding joins a validated TypeDef token with the module version identifier
of the image that issued it. A binding for another image is rejected rather
than interpreted against the current image.

## Composition

`ILInspector.Metadata` owns the authenticated binding, Count and Rows
lowerings, typed outcomes, and structural work receipt. It has no QuerySpace or
host dependency.

`DotnetInspector.Queries` owns the assembly-context adapter. It accepts the
owner-issued authenticated TypeDef binding, executes the Metadata population
while the participant session is alive, and detaches the outcome before the
session closes.

`DotnetInspector.Sections` owns the `exact-type` to `declared-method`
QuerySpace route and the host-neutral inspection operation. The initial query
space:

- supports only Count and Rows;
- has one `declared-methods` row set;
- admits no filters, sorting, row stages, or continuation;
- uses distinct Count and Rows result contracts; and
- returns a non-projectable `InspectionEnvelope<T>` because no portable
  Workspace scenario has yet been named.

The authenticated binding is required input to the operation. Accepting only a
type name would repeat locator work for every terminal, merge identity lookup
with population execution, and hide the source-native closing behind unrelated
TypeDef scans and allocations.

The operation keeps participant-open, malformed metadata, binding, and
bounded-Rows failures visible as typed outcomes. Type lookup is a separate
locator concern: missing, ambiguous, forwarded, or rejected names do not form a
declared-method population request. The operation never turns a failed or
incomplete population into zero or an empty Rows success.

## Boundaries

This population is metadata-shaped. It includes constructors, accessors, and
other compiler-emitted MethodDefs because they occupy the declaring TypeDef's
method range.

It is not:

- the public API Member count shown on Library Type rows;
- the accessibility-composed exact-Member population;
- a grouped-name or overload-family population; or
- a replacement for C# declaration Rows.

Accessibility, hidden-member admission, grouping, and declaration spelling
belong to later populations whose predicates require MethodDef-row evidence.
They must not weaken this unfiltered Count closing.

## Physical lowering

Count is a source-native closing:

```text
bound TypeDef
  -> TypeDefinition.GetMethods()
  -> MethodDefinitionHandleCollection.Count
  -> exact Count
```

Rows is the reference traversal:

```text
bound TypeDef
  -> TypeDefinition.GetMethods()
  -> enumerate MethodDef handles
  -> project MethodDef tokens
```

Both terminals return a product-owned work receipt. The receipt separately
reports TypeDef rows read, MethodDef handles visited, MethodDef rows read,
names, signatures, and attributes decoded, and rows projected. Count is valid
only when every MethodDef-specific counter is zero.

## Production adoption and evidence

The performance harness is the first production caller of the host-neutral
operation. It resolves the exact Type once, then measures the QuerySpace-routed
Sections path from the resulting authenticated binding. It also preserves a
focused Metadata measurement for the physical Count/Rows comparison.

The adoption gate is one pinned real assembly on one Linux NativeAOT
performance host. Count must show a material latency and allocation collapse
relative to complete Rows, and the Sections operation must preserve the same
answer and structural receipt. If it does not, the candidate is evidence
against this physical pattern rather than a feature to preserve.
