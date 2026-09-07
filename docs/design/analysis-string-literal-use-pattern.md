# Analysis string-literal-use pattern

## Status and owner

This document is the focused owner for finding decoded string-literal uses in
one admitted implementation assembly. It is tracked by
[#5795](https://github.com/richlander/dotnet-inspect/issues/5795) and supplies
the first semantic producer used by the production Package Query delivery in
[#6030](https://github.com/richlander/dotnet-inspect/issues/6030), under the
overall tracker
[#5766](https://github.com/richlander/dotnet-inspect/issues/5766).

The implementation belongs to `ILInspector.Analysis`. Metadata owns image
admission and bounded access to method rows, copied IL bodies, and decoded user
strings. `ILInspector.Instructions` owns IL decoding. Package Query owns
candidate selection and evaluation, while the CLI and Browser own gestures and
presentation.

This first delivery has no byte prefilter. The optional prefilter proof and
gate from #5795 remain deferred by #6030.

## Claim

Given:

- one callback-scoped `AssemblyInspectionSession` for a Metadata-admitted
  ordinary ECMA-335 assembly;
- one validated, non-empty `StringLiteralUseOperand`;
- one finite positive `StringLiteralUsePatternBudget`; and
- one cancellation token;

`StringLiteralUsePatternAnalysis.Inspect` returns every decoded `ldstr`
instruction whose decoded user string contains the operand according to
`StringComparison.Ordinal`, or returns one typed rejection or work-limit
outcome.

A semantic miss exists only when every MethodDef row and every applicable
method body completed within the admitted bounds. Decode failure, unsupported
input, work-limit exhaustion, and cancellation never become a miss. Any such
outcome discards matches accumulated before the interruption.

## Public contract

The producer surface is:

```csharp
public static class StringLiteralUsePatternAnalysis
{
    public static StringLiteralUsePatternResult Inspect(
        AssemblyInspectionSession session,
        StringLiteralUseOperand operand,
        StringLiteralUsePatternBudget budget,
        CancellationToken cancellationToken = default);
}
```

`StringLiteralUsePatternAnalysis.ProducerId` is the stable product-authored
identity `analysis.ldstr.ordinal-substring.v1`.

`StringLiteralUseOperand.Create(string)` is the only constructor. It rejects a
null, empty, or over-limit value. `StringLiteralUseOperand.MaximumLength` is
`1024` UTF-16 code units. The exact input string remains private to Analysis
for matching; `DisplayText` is an `InertString` under `TextPolicy.Field`.
Construction does not normalize, case-fold, trim, or otherwise rewrite the
matching value.

The following data shapes describe public members, not constructor or setter
availability. Budgets validate all limits during construction and expose
get-only properties; occurrence, receipt, and result constructors are internal
to Analysis.

The budget shape is:

```csharp
public sealed record StringLiteralUsePatternBudget(
    int MaximumMethods,
    int MaximumMethodBodyBytes,
    long MaximumMethodBodyBytesVisited,
    long MaximumInstructions,
    long MaximumDecodedUserStringCharacters,
    int MaximumOccurrences)
{
    public static StringLiteralUsePatternBudget Default { get; }
}
```

`Default` is:

```text
MaximumMethods                    50,000
MaximumMethodBodyBytes         1,000,000
MaximumMethodBodyBytesVisited 16,000,000
MaximumInstructions            4,000,000
MaximumDecodedUserStringCharacters
                                4,000,000
MaximumOccurrences                10,000
```

These values are conservative correctness limits, not measured throughput
claims or final product policy.

The resource-free occurrence is:

```csharp
public readonly record struct StringLiteralInstructionAddress(
    Guid ModuleVersionId,
    int MethodDefinitionToken,
    int ILOffset);

public sealed record StringLiteralUseOccurrence(
    StringLiteralInstructionAddress Address,
    int UserStringToken,
    int LiteralCharacterCount,
    InertString LiteralText);
```

The result is a closed hierarchy:

```csharp
public abstract record StringLiteralUsePatternResult
{
    public sealed record Match(
        ImmutableArray<StringLiteralUseOccurrence> Occurrences,
        StringLiteralUsePatternReceipt Receipt)
        : StringLiteralUsePatternResult;

    public sealed record NoMatch(
        StringLiteralUsePatternReceipt Receipt)
        : StringLiteralUsePatternResult;

    public sealed record Rejected(
        StringLiteralUseRejection Rejection,
        StringLiteralUsePatternReceipt Receipt)
        : StringLiteralUsePatternResult;

    public sealed record WorkLimitExceeded(
        StringLiteralUseLimitKind Limit,
        StringLiteralUsePatternReceipt Receipt)
        : StringLiteralUsePatternResult;
}
```

`Match.Occurrences` is always non-empty. `NoMatch` is always a completed
semantic scan. `Rejected` distinguishes incomplete access, bounded decode, and
unsupported input. `WorkLimitExceeded` identifies the first exhausted limit.
Every arm carries:

```csharp
public sealed record StringLiteralUsePatternReceipt(
    int MethodsVisited,
    int MethodBodiesVisited,
    long MethodBodyBytesVisited,
    long InstructionsVisited,
    long UserStringsDecoded,
    long UserStringCharactersDecoded,
    int OccurrencesRetained);
```

The receipt reports completed charged work and is not evidence that a failed
or limited scan covered the remaining assembly. On a rejected or limited
result, `OccurrencesRetained` records provisional occurrences charged before
the interruption even though those occurrence values are discarded.

## Semantic scope

The producer visits MethodDef rows in metadata order and instructions in
increasing IL offset. `MaximumMethods` charges every visited MethodDef row,
including abstract, external, runtime, native, or otherwise bodyless rows.
Bodiless rows contain no `ldstr` instruction and are not failures.

Only physical `ldstr` instructions participate. The producer excludes:

- unreferenced `#US` entries;
- metadata names and string constants;
- custom-attribute values;
- manifest resources;
- PDB and source text; and
- values reconstructed through dataflow or constant propagation.

Repeated `ldstr` instructions are separate occurrences even when they use the
same user-string token. Occurrence order is MethodDef row order followed by IL
offset. The user-string token is supporting evidence, not occurrence identity.

Matching uses the decoded string and:

```csharp
literal.Contains(operand, StringComparison.Ordinal)
```

The operand and literal are compared as exact UTF-16 sequences. BMP
characters, surrogate pairs, combining sequences, embedded NUL characters,
and ordinal case distinctions retain their raw meaning. Matching never uses
the contained display form.

## Identity and evidence

`StringLiteralInstructionAddress` identifies one physical instruction by:

- the admitted module's non-empty MVID;
- its MethodDef metadata token as an `int`; and
- its non-negative IL offset.

The address deliberately does not expose `MethodDefinitionHandle`. It is a
resource-free physical address, not cross-version correspondence and not a
cryptographic module identity.

`LiteralText` is constructed from the exact decoded literal with
`InertString(TextPolicy.Field, literal)`. `LiteralCharacterCount` records the
unmodified UTF-16 length. The raw string is used only while matching and
containment are performed and is not retained in the public result.

The result graph contains no reader, handle, session, stream, byte buffer,
lease, delegate, package coordinate, or package-selection state. It may outlive
the callback-scoped assembly session.

## Bounds and charging

All budget values must be positive. Invalid budget construction is a caller
error and throws before traversal.

Each governed action is admitted against its remaining budget before it runs;
the receipt records the work actually completed:

1. Read the scalar MethodDef row count. If it exceeds `MaximumMethods`, refuse
   the scan before reading any row. Otherwise charge one method before reading
   each MethodDef row, including rows with no body.
2. Ask Metadata for a body under the smaller of `MaximumMethodBodyBytes` and
   the remaining aggregate body-byte budget. Metadata checks the IL length
   before copying it. Record the admitted copy's length in
   `MethodBodyBytesVisited`.
3. Decode under the remaining `MaximumInstructions`. The Instructions-owned
   bounded decoder checks the limit before decoding and retaining each next
   instruction.
4. For each `ldstr`, ask Metadata for the user string under the remaining
   `MaximumDecodedUserStringCharacters`. Metadata validates the token and
   checks the entry's UTF-16 length before allocating the string; Analysis
   records the returned string's length in `UserStringCharactersDecoded`.
5. If the raw literal matches, charge one occurrence before constructing its
   `InertString` and retaining evidence.

The live working set is finite as a function of the admitted bounds:

- at most one copied IL body of `MaximumMethodBodyBytes`;
- decoded instruction records bounded by both that body's byte length and the
  remaining `MaximumInstructions`;
- switch-target storage bounded by the admitted body bytes;
- at most one newly decoded raw string within the remaining decoded-character
  budget;
- retained occurrence records bounded by `MaximumOccurrences`; and
- retained inert literal text bounded by six encoded characters per charged
  UTF-16 code unit under the current `TextPolicy.Field` spelling set.

The producer does not claim a measured allocation total or throughput target.
The bounds above prevent work or retention from becoming open-ended; the
Package Query evaluator composes them with its separate retained-image and
candidate-count bounds.

## Cancellation

The producer checks cancellation:

- before traversal;
- before each MethodDef row;
- before each bounded body read;
- before instruction decoding;
- while visiting decoded instructions;
- before each bounded user-string decode;
- before retaining each match; and
- before returning a completed result.

Cancellation propagates as `OperationCanceledException`. It is not a result
arm and discards provisional matches.

## Failure classification

`StringLiteralUseRejectionKind.Incomplete` covers a Metadata-owned access that
cannot provide complete evidence for an otherwise admitted input and is not
classified as malformed or unsupported. The producer does not reinterpret
that absence as a bodyless method or a miss.

`StringLiteralUseRejectionKind.BoundedDecode` covers a selected body,
instruction stream, `ldstr` token, or user-string entry that cannot be decoded
within the supported ECMA-335 representation. The rejection carries a
resource-free site containing the MVID, MethodDef token, and optional IL
offset.

`StringLiteralUseRejectionKind.UnsupportedInput` covers a decodable method
implementation shape that is not a managed IL body supported by this producer.
The producer does not guess that such a method has no matching literal.

Only scoped Metadata and Instructions outcomes are classified. Cancellation
and unexpected exceptions propagate. The producer has no broad catch.

Dedicated malformed-body and malformed-user-string fixture coverage is
unverified in this slice unless an existing benign rejection fixture exercises
the branch. This slice does not create, mutate, or fuzz malformed binaries.

## Consumer contract

`DotnetInspector.PackageQueries` binds one static registry entry to this exact
producer. It invokes `Inspect` inside
`ArtifactAssemblyInspection.Execute`'s callback-scoped
`AssemblyInspectionSession` and directly retains the returned resource-free
occurrences. The evaluator maps the four result arms without reinterpreting
literal semantics or reconstructing occurrence identity.

The first hosts evaluate up to five explicitly supplied `ID@VERSION`
candidates serially. Candidate acquisition, package selection, event delivery,
CLI and Browser gestures, rendering, and result opening remain outside this
owner.

## Gates

The focused Release suite
`dotnet run --project src/ILInspector.Analysis.Tests -c Release` owns:

- all matching physical `ldstr` sites, including repeated uses of one token;
- completed miss versus text present only outside `ldstr`;
- exact ordinal behavior for case, composed/decomposed text, string
  boundaries, BMP text, surrogate pairs, and embedded NUL;
- deterministic exact and exhausted method, body-byte, instruction,
  decoded-character, and occurrence limits;
- exhaustion after earlier matches and non-matches without partial evidence;
- cancellation propagation;
- contained artifact-authored display text;
- resource-free evidence that remains usable after session disposal; and
- operand and budget validation.

The bounded Instructions decode extension has its own focused tests for exact
limit completion, pre-decode limit exhaustion, and unchanged malformed-IL
classification.

## Non-goals

- No byte prefilter or raw-image substring search.
- No regex, glob, culture-aware, normalized, or case-insensitive matching.
- No value-flow, control-flow, or call-argument reconstruction.
- No package, evaluator, archive, Workspace, renderer, worker, CLI, or Browser
  behavior.
- No package-wide conclusion from one selected implementation assembly.
- No claim that the optional prefilter work in #5795 is complete.
