# Finding instance census

This document owns producer-issued identity for one sealed Finding census. It
defines the receipt, per-instance key, canonical entry association, sealing
operation, and validation outcome supplied by `ILInspector.Findings`.

[Finding nomenclature](finding-nomenclature.md) owns observation, census, and
operation-outcome meanings. [Finding coordinates](finding-coordinates.md) owns
subject, correspondence, producer order, and provenance. [Finding value
semantics](finding-value-equality.md) owns .NET equality and hashing.

The end-to-end adoption is tracked by
[issue #5515](https://github.com/richlander/dotnet-inspect/issues/5515):

- Research preserves the identity through Facts and Annotated Source under
  [issue #4717](https://github.com/richlander/dotnet-inspect/issues/4717);
- the CLI consumes it under
  [issue #4718](https://github.com/richlander/dotnet-inspect/issues/4718); and
- Inspect Web transports and uses it under
  [issues #5516](https://github.com/richlander/dotnet-inspect/issues/5516) and
  [#5517](https://github.com/richlander/dotnet-inspect/issues/5517).

## Problem

One successful producer execution may emit multiple Findings with equal
subjects, descriptors, correspondence keys, payloads, details, and rendered
coordinates. Those observations remain separate census instances. A consumer
that derives an identifier from those fields collapses multiplicity and cannot
prove that independently constructed projections describe the same execution.

Existing Finding coordinates answer different questions:

| Coordinate | Question |
| --- | --- |
| `FindingSubject.Key` | What subject was inspected? |
| `FindingKey` | Which observations correspond across versions? |
| `Finding<T>.Ordinal` | Where did an ordered producer observe this value? |
| Producer payload | What typed provenance supports the observation? |
| Census receipt and instance key | Which exact occurrence belongs to this sealed execution? |

The new identity is a separate axis. It must not be reconstructed from another
axis even when current values happen to agree.

## Contract

`ILInspector.Findings` supplies four host-neutral types:

| Type | Contract |
| --- | --- |
| `FindingCensusReceipt` | Opaque, non-default identifier for one sealed census. |
| `FindingInstanceKey` | Positive compact key, injective only within one receipt. |
| `FindingCensusEntry<T>` | One key associated with the exact `Finding<T>` reference admitted at sealing. |
| `FindingCensus<T>` | Immutable ordered Findings and keyed entries from one seal operation. |

The durable instance identity is the pair:

```text
(FindingCensusReceipt, FindingInstanceKey)
```

A key alone is not an identity. Equal numeric keys in different censuses are
expected and unrelated.

### Sealing

`FindingCensus<T>.Seal` performs one eager enumeration and immutable
materialization. It rejects:

- a null collection;
- a default `ImmutableArray<Finding<T>>`; and
- a null Finding at any position.

Only after materialization succeeds does the operation issue a non-default
receipt and assign keys in census order. Keys are positive and one-based:
entry zero receives key one, entry one receives key two, and so on. This leaves
the default integer representation invalid without adding a second validity
bit.

The census retains:

- `Findings`, preserving the original order and multiplicity; and
- `Entries`, preserving the same order and exact Finding references.

For every valid index `i`:

```text
Entries[i].Key.Value == i + 1
ReferenceEquals(Entries[i].Finding, Findings[i])
```

The key is instance identity, not producer order authority. A later projection
may filter or reorder entries while preserving each owner-issued key. Consumers
must not reconstruct a key from a filtered position, a producer ordinal, or
Finding content. The canonical `Entries` collection is the assignment
authority; its one-based construction is not a public key-minting algorithm.

### Empty census

A successful empty census receives a non-default receipt and empty initialized
collections. It is distinct from:

- a second empty census produced by another seal;
- `FindingInspection<T>.Absent`; and
- `FindingInspection<T>.Failed`.

The receipt proves which successful execution produced the empty census even
though there are no instance keys.

## Identity and equality

The identity and equality shapes are deliberate:

- `FindingCensusReceipt` has value equality over its opaque `Guid` value.
- `FindingInstanceKey` has value equality over its positive integer value.
- A receipt/key pair supplies one execution-local instance identity.
- `FindingCensus<T>` uses reference identity. Independently sealing equal
  Finding collections creates different census operations and different
  receipts.
- `FindingCensusEntry<T>` is an association object, not a structural value.
  Consumers compare its key under the containing receipt and retain its exact
  Finding reference.
- `Finding<T>` equality, hashing, correspondence, and payload behavior do not
  change.

The receipt uses `Guid.NewGuid()` rather than object identity or a process-wide
counter. A typed `Guid` wrapper is compact, host-neutral, NativeAOT-compatible,
and can cross the managed/browser result boundary later without making this
design own a serialization format. The seal operation excludes `Guid.Empty`.

Receipt and key constructors are internal. Consumers retain owner-issued typed
values in process; they cannot mint or rehydrate typed identity from raw
components. A host adapter may project the public raw values outward under its
own wire contract, but this design does not make inbound parsing or
reconstruction valid.

Per-census keys use a counter rather than random values. One sealing operation
coordinates the full collection, so sequential assignment provides absolute
injectivity within the receipt without probabilistic collision handling.

## Validation

`FindingCensus<T>.Validate` admits a candidate receipt and a proposed complete
bijection. `FindingCensus<T>.ValidateEntry` admits one retained entry so a
filtered projection does not need to pretend it contains the whole census.
Candidate entries are constructible so projections can return their retained
key/Finding associations to the owner for validation. Constructibility does
not mint a valid identity: only the census receipt, canonical key range, and
exact association establish admission.

Validation does not require candidate enumeration order to match census order.
It verifies a bijection by key and returns either
`FindingCensusValidation.Valid` or
`FindingCensusValidation.Invalid(FindingCensusValidationFailure)`.

Failures use this deterministic precedence:

1. `DefaultReceipt`
2. `WrongReceipt`
3. `UninitializedEntries`
4. `NullEntry`
5. `DefaultKey`
6. `DuplicateKey`
7. `ExtraKey`
8. `MissingKey`
9. `SubstitutedFinding`

Collection and key-shape checks complete before exact Finding association is
evaluated. Duplicate, extra, missing, and substituted failures identify the
relevant `FindingInstanceKey`. Null-entry and default-key failures identify the
first offending index in candidate enumeration order; their key remains
default. When more than one non-default key could report the same failure
class, the smallest key is reported. Key-set and association outcomes are
therefore independent of candidate enumeration order.

Failure coordinates are closed by kind:

| Failure | `Key` | `InputIndex` |
| --- | --- | --- |
| `DefaultReceipt`, `WrongReceipt`, `UninitializedEntries` | Default | Null |
| `NullEntry` | Default | First offending index for `Validate`; null for `ValidateEntry` |
| `DefaultKey` | Default | First offending index for `Validate`; null for `ValidateEntry` |
| `DuplicateKey`, `ExtraKey`, `MissingKey`, `SubstitutedFinding` | Relevant non-default key | Null |

Substitution uses `ReferenceEquals`, not Finding value equality. Two
structurally equal Findings remain distinct instances, and placing one under
the other's valid key is rejected.

`ValidateEntry` uses the same receipt, default-key, key-range, and substitution
rules. Duplicate and missing keys are whole-projection properties and therefore
apply only to `Validate`.

The validator throws only for a null `entries` argument. Invalid typed state,
including a default immutable array or null entry, returns a typed failure
rather than an exception or a success-shaped empty result.

## Composition boundaries

This contract is the Finding-owned substrate. Adoption remains with its
consumers:

- producers decide where a sealed census belongs in their operation result;
- Research owns preserving receipt/key identity through its projection
  lifecycle;
- the CLI owns command and structured-output presentation;
- Inspect Web owns managed result transport and cross-view interaction.

`FindingInspection<T>.Complete` remains an ordered Finding value. It does not
gain receipt identity in this slice: inspection topology and sealed-census
instance identity are independent concerns. A producer or composer may retain
both where both contracts are required.

The public receipt and key values are sufficient for a host adapter to project
them. This design does not define JSON names, text formatting as a durable
protocol, parsing, deserialization, packet persistence, or cross-session
rehydration.

Receipt inequality is the staleness and replacement discriminator available to
consumers. Research and host owners decide when a newer operation supersedes an
older one and reject non-matching receipts at their respective admission
boundaries.

## Convention basis

The design follows established conventions by role:

- Roslyn workspace IDs use opaque producer-issued `Guid` wrappers.
- Roslyn `DiagnosticBag` separates mutable collection from one immutable seal.
- OpenTelemetry uses a two-level trace/item identity rather than deriving an
  item identity from display data.
- SARIF separates run and result GUIDs from content-derived fingerprints.

Two conventions deliberately do not transfer:

- SARIF fingerprints identify logical recurrence across runs and may collapse
  display-equal results.
- vstest derives test identity from content and source fields; that behavior is
  the anti-pattern for preserving multiplicity within one execution.

## Evidence

The Release executable gates in
`src/ILInspector.ILDiff.Tests/FindingCensusTests.cs` verify:

- `Seal_PreservesOrderMultiplicityAndExactInstances` proves independent equal
  seals, distinct receipts and keys, exact references, and reordered
  validation;
- `Seal_ReceiptsSuccessfulEmptyCensusesIndependently` proves initialized,
  separately receipted empty censuses;
- `Seal_RejectsInvalidCollections` proves construction-time collection
  containment;
- `Validate_DistinguishesReceiptAndCollectionFailures` proves receipt,
  initialization, null-entry, and null-argument behavior;
- `Validate_DistinguishesKeySetFailures` proves default, duplicate, extra, and
  missing key outcomes;
- `Validate_RejectsValueEqualFindingSubstitution` proves exact-reference
  association rather than Finding value equality; and
- `ValidateEntry_AdmitsSubsetsWithoutWeakeningAssociation` proves filtered
  projections retain receipt and exact-association admission;
- `Validate_UsesDeterministicFailurePrecedence` proves validation ordering is
  stable when duplicate, extra, missing, and substituted defects coexist; and
- `Validate_ReportsSmallestKeyIndependentOfCandidateOrder` proves smallest-key
  diagnostics do not depend on projection order.

The invariant is finite construction and validation over one immutable
collection. It has no concurrent admission, replacement, or scheduling
lifecycle, so executable exhaustive gates are the appropriate oracle; no TLA+
model is required.

## Non-claims

This design does not:

- define cross-version correspondence, matching, or fingerprints;
- make instance identity durable across process executions or seal operations;
- change Finding value equality or hashing;
- make the instance key an ordinal, source coordinate, or provenance value;
- require every Finding producer to adopt the census in this slice;
- define Research, CLI, or browser presentation behavior; or
- define serialization, parsing, packet persistence, or cross-session identity.
