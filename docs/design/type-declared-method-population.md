# Type declared-method population

## Status

This focused design owns the source-native Count proof for one exact Type's
declared `MethodDef` population. It is an implementation and performance
candidate, not yet a supported CLI or Inspect Web surface.

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

## Adoption gate

The first gate is one real assembly on one performance host using a NativeAOT
scorecard. Count must show a material latency and allocation collapse relative
to complete Rows before this population is adopted by an explicit exact-Type
CLI or Browser/Wasm operation. If it does not, the candidate is evidence
against this physical pattern rather than a feature to preserve.
