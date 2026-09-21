# String materialization analysis

## Decision

`ILInspector.Analysis` owns a positive-only census of exact IL operations that
can produce a `System.String`. The census preserves the source-facing method,
physical evidence method, IL offset, operand token, resolved operation,
construction strategy, and local loop/multiplicity evidence.

The claim is deliberately narrow:

> A trusted framework operation at this MethodDef and IL offset can produce a
> string result using the classified construction strategy.

The analysis does not claim that each operation allocates a new object, how
many bytes it allocates, how frequently it executes, whether its result
escapes, or whether changing it is safe or profitable. Runtime implementations
may return `string.Empty`, reuse a string, or avoid allocation through another
implementation detail.

This design is owned by Analysis. Performance presentation consumes the
Analysis finding but does not redefine its identity or evidence.

## Motivation

Existing allocation analysis reports IL-visible `newobj`, `newarr`, boxing,
delegate, enumerator, and selected materialization shapes. It cannot compare
common text-construction strategies because most string production occurs
behind framework calls.

For example, the C# printer in `ILInspector.Decompiler` has a root
`StringBuilder`, while expression helpers compose strings through
`String.Concat`, interpolated-string handler finalization, `String.Join`, and
local builders. The previous Performance view reported delegate and array
findings but no string-construction evidence. `System.Text.Json` provides a
useful contrast: string serialization eventually decodes UTF-8 into a string,
while `SerializeToUtf8Bytes` and `Utf8JsonWriter` remain byte-oriented.

## Evidence model

`StringMaterializationAnalysis` classifies the existing resolved `DirectCall`
census. It does not rescan IL or introduce a parallel call identity.

The first supported strategies are:

- `System.String.Concat`
- `System.String.Join`
- `System.String.Format`
- `System.String.Create`
- `System.String` constructors
- `DefaultInterpolatedStringHandler.ToStringAndClear`
- `StringBuilder.ToString`
- `Encoding.GetString`

Type recognition uses trusted framework identity. Namespace and type names
alone are insufficient because an inspected assembly can define lookalikes.

Some C# compilers encode parameterless `StringBuilder.ToString()` as a virtual
call to `System.Object.ToString`. Analysis classifies that shape only when the
existing complete receiver-provenance result proves that every reaching direct
call produces a trusted `StringBuilder`. An unknown, raw, or mixed receiver is
not classified.

The census obtains constructor-aware receiver provenance from the existing
value-source resolver through a private per-method map. It does not populate or
alter the published `DirectCall.ReceiverSource`; ordinary call-value-flow
requests retain their existing call-kind and source semantics.

Each occurrence becomes an `analysis.string-materialization` Finding. Finding
identity combines the strategy with the resolved operation signature. IL
offsets and metadata tokens remain version-local provenance rather than
cross-version identity.

## Performance projection

Library output exposes exact occurrences through `Performance: Strings` and
the nested JSON key `performance.strings`. The row shape is
`string-materialization`; `Operation` identifies the strategy:

- `string.concat`
- `string.join`
- `string.format`
- `string.create`
- `string.constructor`
- `string.interpolation-handler`
- `string.builder-finalization`
- `string.encoding-decode`

The row retains `Finding=analysis.string-materialization`,
`Provenance=exact`, `MethodToken`, optional `EvidenceMethod`, `IL`, and operand
`Token`. It intentionally leaves the allocation type and allocation weight
empty: a string result is not proof of a fresh allocation.

Loop and caller-reach evidence remain static prioritization inputs. They do not
turn the occurrence into a runtime heat claim. Every proposed fix directs the
user to dynamic measurement first.

String-materialization rows remain outside bounded member-level optimization
rankings. Those rankings promise prioritized optimization candidates, while
this census deliberately has no runtime cost or profitability score. Dedicated
string sections can enumerate the exact operations without allowing a large
unscored census to displace established candidates or consume another host's
bounded result budget.

## Scope

This first slice is intraprocedural at the reported source method. A public API
that delegates string creation to another method does not inherit the
callee's exact Finding. Whole-library analysis still reports the terminal
operation and leverage data identifies broadly reached helpers.

Transitive string-materialization paths, nested composition depth, estimated
intermediate-string count, and builder-versus-concat rewrite advice are future
Analysis work. Such work needs its own explicit aggregate model and must not
present a callee occurrence as if it were physically located in the caller.

## Validation

Contract tests cover:

- every supported strategy;
- trusted framework identity versus a same-named untrusted type;
- `Object.ToString` accepted only with complete `StringBuilder` receiver
  provenance;
- stable Finding identity and IL ordering;
- compiled concat, interpolation, join, builder, constructor, and decode
  fixtures;
- byte encoding and `Utf8JsonWriter` paths that must not fabricate string
  findings;
- CLI section ordering, shape routing, and nested structured output.

Real-component validation uses the source-built product:

- `CSharpPrinter` must expose the exact concat, join,
  interpolation-handler, builder-finalization, and constructor operations
  present in its compiled implementation.
- `System.Text.Json` must expose `Encoding.GetString` at
  `JsonReaderHelper.TranscodeHelper`; verified source must show
  `JsonSerializer.WriteString` reaching that helper.
- `JsonSerializer.WriteBytes` and `Utf8JsonWriter` must not acquire a string
  finding merely because they produce text in UTF-8 form.

Static evidence selects candidates. A representative BenchmarkDotNet or trace
workload must confirm realized allocations and frequency before changing the
C# printer.
