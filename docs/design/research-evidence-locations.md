# Research evidence locations

Status: implemented by
[#7360](https://github.com/richlander/dotnet-inspect/pull/7360).

**Owner:** `ILInspector.Research` evidence-location composition.

## Claim

Research identifies evidence in a physical method before it identifies any
instruction within that method. An instruction location is therefore the pair
of an Analysis-owned `MethodIdentity` and a non-negative IL offset. Evidence
that applies to a whole method carries the same method identity with no
instruction coordinate.

This evidence location is independent from where a relationship Finding is
presented. A caller-side `cost.callee`, `semantics.callee`, or `safety.callee`
annotation remains anchored at the caller invocation while its typed payload
retains the callee evidence location.

## Motivation

The motivating production case is
[`System.Text.Json.JsonElement.DeepEquals`](https://github.com/dotnet/runtime/blob/8970fe8a2fb3301d03a9de8e75892fb31b49291d/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonElement.cs#L1258-L1291),
which calls
[`JsonReaderHelper.UnescapeAndCompareBothInputs`](https://github.com/dotnet/runtime/blob/8970fe8a2fb3301d03a9de8e75892fb31b49291d/src/libraries/System.Text.Json/src/System/Text/Json/Reader/JsonReaderHelper.Unescaping.cs#L162-L204).
The caller relationship and the callee's two `stackalloc` instructions are
different evidence locations. Showing the caller line as though it were the
unsafe implementation evidence would be false.

The relevant convention is the repository's Analysis model:
`DirectCall.EvidenceMethod`, `UnsafeEvidence.Member`, and their IL offsets keep
physical body identity and instruction coordinates together. Research
preserves that association instead of flattening it to a bare integer.

## Contract

`ResearchEvidenceLocation` has exactly two valid forms:

- **Method-only:** one physical `MethodIdentity`, with no singular instruction
  claim.
- **Instruction:** one physical `MethodIdentity` paired with one non-negative
  IL offset.

The type has no unqualified offset form. Construction rejects negative
instruction offsets. Equality includes the complete method identity and the
optional offset, so equal offsets in distinct methods remain distinct.

Admission to a method-specific projection compares the complete
Analysis-owned `MethodIdentity`. A match returns the original location. A
mismatch returns a typed rejection carrying both the expected method and the
unmatched location. Callers do not repair, rebind, or infer identity from
method names, display text, offsets, or metadata-token equality alone.

## First adoption

The first adopter is the Research call-site fact family:

- `semantics.callee` retains the resolved callee and each physical exception
  construction location.
- `safety.callee` retains the resolved callee and each physical `localloc` or
  `calli` location.
- `cost.callee` retains the resolved callee as method-only aggregate evidence.

The annotation's existing `SourceOffset` remains the caller relationship
coordinate. The typed payload owns the remote evidence subject and locations.
This slice does not project or render remote documents.

## Pathological case and gates

Two methods may both contain meaningful evidence at `IL_0000`. Their
`ResearchEvidenceLocation` values must remain unequal, and admitting one
against the other's method must return a typed rejection.

Release tests gate:

- equal offsets in distinct methods remaining distinct;
- method-only evidence having no instruction coordinate;
- matching method admission retaining the exact location;
- mismatched method admission returning the expected and actual identities;
- `safety.callee` retaining the physical unsafe-evidence method and offsets;
  and
- `cost.callee` retaining method-only callee evidence while the annotation
  remains anchored at the caller invocation.

## Production adoption

This is step 1 of 4:

1. Define the carrier and adopt it in Research call-site facts (#5610).
2. Expose method-qualified remote evidence in CLI Facts output (#7358).
3. Project exact remote safety and semantics evidence into Inspect Web
   (#4641).
4. Present method-level aggregate cost evidence in Inspect Web (#4642), with
   shared evidence-document deduplication and bounds tracked by #4640.

The CLI and browser remain thin consumers of the same Research-owned
locations. Each host owns only its presentation and navigation behavior.

## Non-claims

- No change to Analysis `MethodIdentity`, call, signal, leverage, or unsafe
  evidence construction.
- No change to Decompiler `IAnnotation`, source-node identity, provenance, or
  correspondence.
- No browser wire shape, remote-document acquisition, node mapping,
  deduplication, payload budget, or interaction behavior.
- No CLI formatting or structured-output change in this slice.
- No claim that aggregate cost evidence has one truthful source line.
