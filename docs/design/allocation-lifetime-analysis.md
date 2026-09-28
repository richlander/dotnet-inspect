# Allocation Lifetime Analysis

## Decision

`ILInspector.Analysis` owns a bounded, intraprocedural lifetime verdict for
each exact managed allocation occurrence. `MethodAllocationFacts` follows the
produced value through method-local definitions and uses and publishes the
verdict on the existing typed `AllocationOccurrence`.

The first product claim is deliberately narrow:

> `LocalOnly` means every supported use of this exact allocation remains
> within the physical method body, and the analysis encountered no unsupported
> use or incomplete reaching-definition evidence.

`Escapes` means at least one supported use crosses that boundary. `ThrowPath`
means the allocated value is thrown. `Unknown` means the available evidence
cannot prove either non-escape or a known escape. These outcomes describe the
allocation lifetime; they do not imply resource-release semantics.

Performance Triage consumes the Analysis-owned verdict. It does not run a
parallel escape analysis. Array Pool Escapes does not consume ordinary-array
lifetime results because rented-buffer cleanup and managed-array lifetime are
different questions.

## Motivation

Small-array triage previously contained a second, narrower escape recognizer
beside `MethodAllocationFacts`. The two analyses could disagree: the
allocation Finding could carry one verdict while the stack-allocation policy
recomputed another. A single Analysis-owned verdict keeps the allocation
identity, lifetime claim, and product recommendation joined.

Jurassic 3.2.9 provides the motivating real shape.
`Jurassic.Compiler.Lexer.ReadExtendedUnicodeSequence()` allocates a
two-character array at IL `0x00cf`, initializes both surrogate characters,
and passes it to the trusted `System.String(char[])` constructor at IL
`0x0102`. The constructor copies the characters; it does not retain the
array. The array is outside the parser loop and is plausibly replaceable by a
stack-allocated character span plus the span-accepting string constructor.

`System.Text.Json` 10.0.2 provides a close negative.
`System.Text.Json.BitStack.PushToArray(bool)` allocates an `int[2]` at IL
`0x000a` and stores it in `_array` at IL `0x000f`. That allocation escapes the
method and cannot become a stack-allocation candidate.

## Evidence model

The allocation coordinate is the physical evidence method plus the
`newarr`, `newobj`, or `box` IL offset. Reaching definitions bind local aliases
to that exact occurrence. Supported consumers classify the value as:

- local-only element reads and writes, length reads, drops, unboxing, and
  trusted non-capturing framework calls;
- escaping returns, instance/static fields, collection stores, captures, and
  by-reference transfers;
- thrown values; or
- unknown calls, unsupported stack shapes, incomplete control flow, and
  incomplete reaching definitions.

Trusted non-capturing calls are closed semantic knowledge, not a naming
heuristic. The first array-copy boundary is the core-library
`System.String` constructor whose parameter matches the produced array type.
A user-defined `String` lookalike or arbitrary method taking the same array
remains `Unknown`.

The current `AllocationEscapeKind` refines a proven escape as return, field,
static, collection, or capture. Exact use and sink coordinates plus typed
incompleteness reasons are follow-up evidence within this owner; until they
are published, `Unknown` remains the visible fail-honest result and no
consumer may infer why the proof stopped.

## Stack-allocation policy

Non-escape is necessary but insufficient for a `stackalloc` recommendation.
`OptimizationOpportunityAnalysis` separately owns the initial policy:

- the allocation has a small constant length;
- the element type is one of the currently supported unmanaged primitive
  types;
- the allocation is outside a loop; and
- the Analysis-owned lifetime verdict is `LocalOnly`.

Future policy work may additionally prove operation representability,
target-framework API availability, dynamic-size bounds, async or iterator
lifetime compatibility, and array-identity constraints. Those are not
allocation-lifetime facts and must not be folded into the `LocalOnly` verdict.

## Execution and consumers

Allocation lifetime runs only when allocation occurrences are selected by the
library-body Analysis plan. The same classified occurrence feeds Allocation
Facts, Findings, browser Analysis export, and Performance Triage. The
optimization traversal may perform its own shape and size policy work, but it
must reuse the owner-issued lifetime verdict.

The end-to-end work is staged:

1. make the existing Analysis verdict the singular lifetime source and add
   trusted copy-boundary evidence;
2. publish exact use/sink coordinates and typed incompleteness;
3. expand separately owned `Span<T>` representability and stack-size policy;
4. expose the resulting candidates through Performance Triage in both CLI and
   Browser/Wasm hosts.

Array Pool Escapes remains unchanged throughout.

## Validation

Contract gates cover:

- a compiled local `char[4]` consumed by the trusted string-copy constructor;
- a neighboring returned array;
- incomplete reaching definitions;
- reused local slots containing one local and one escaping allocation; and
- the pinned Jurassic 3.2.9
  `Lexer.ReadExtendedUnicodeSequence()` real asset.

The real-asset gate must retain the exact package version, method, allocation
offset, lifetime verdict, and Performance Triage shape. Dynamic measurement is
required before claiming that a source rewrite improves runtime performance;
static lifetime evidence proves candidacy, not value.

## Analogous evidence

The CoreCLR object stack-allocation analysis asks whether an allocated object
can be accessed after the allocating method returns. It propagates aliases
between tracked locals, classifies supported uses, and conservatively treats
unknown destinations and calls as escape. That behavior supports this
allocation-site-specific, all-uses-accounted-for contract; its IR rewriting,
GC reporting, conditional cloning, and target-dependent size thresholds do
not transfer into this source-analysis owner.

The C# ref-safety rules constrain the replacement `Span<T>`, not the lifetime
of the original ordinary array. CA2014 similarly warns about `stackalloc`
inside loops but does not prove whether an array escapes. CodeQL's local flow
model reinforces that field, collection, and captured-variable content are
separate from ordinary intraprocedural value flow.

Sources:

- [CoreCLR object stack-allocation design](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/object-stack-allocation.md)
- [CoreCLR allocation escape implementation](https://github.com/dotnet/runtime/blob/main/src/coreclr/jit/objectalloc.cpp)
- [CoreCLR object stack-allocation tests](https://github.com/dotnet/runtime/blob/main/src/tests/JIT/opt/ObjectStackAllocation/ObjectStackAllocationTests.cs)
- [C# `stackalloc` reference](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/stackalloc)
- [CA2014 implementation](https://github.com/dotnet/sdk/blob/main/src/Microsoft.CodeAnalysis.NetAnalyzers/src/Microsoft.CodeAnalysis.CSharp.NetAnalyzers/Microsoft.NetCore.Analyzers/Runtime/CSharpDoNotUseStackallocInLoops.cs)
- [CodeQL C# local data-flow API](https://github.com/github/codeql/blob/main/csharp/ql/lib/semmle/code/csharp/dataflow/internal/DataFlowPublic.qll)
