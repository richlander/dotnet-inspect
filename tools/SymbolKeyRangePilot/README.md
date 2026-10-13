# Symbol Key Range Pilot

This pilot compares the current private per-key `SymbolKey` representation with
a population-backed `OpaqueByteRange` prototype. It measures whether sharing
one immutable byte backing can reduce allocation and retained memory without
making construction or closed byte operations materially slower.

The baseline links the current
[`SymbolKey.cs`](../../src/ILInspector.Metadata/SymbolKey.cs) source directly
into the pilot assembly. The candidate retains one owner reference, offset, and
length per key. `Utf8RangeReader.ValueTextEquals` performs a closed comparison
without exposing the retained bytes.

## Scope

The workload constructs Type definition and Type reference keys from their
stored namespace, declaring-Type chain, and Name bytes. Method, Property,
Event, and Field keys embed their declaring Type key and stored Name; rows with
signatures also embed the real metadata signature blob. Both strategies use
the same key grammar and input order. `check` also rejects duplicate
Type-definition keys so a shared identity loss cannot pass as parity.

This is a representative Symbol Key storage-kernel pilot, not a complete Fast
Diff benchmark. It exercises marker, Name, nested Key, and Blob parts, but it
does not reproduce signature decoding, every Fast Diff fact shape, or the
end-to-end diff pipeline. Favorable results would support the shared-range
representation; they would not replace product-path evidence.

The candidate assumes that the producer has enumerated one population and knows
its exact byte budget before construction. The pilot obtains that budget while
opening the metadata census, outside the measured build operation. The builder
does not issue range values until it freezes the completed backing.

The separate user-text presentation experiment accepts text only through the
`present --user-text` host boundary. `UserSearchValue` retains that original
text and its one UTF-8 encoding. Exact equality may produce `UserTextMatch`,
which exposes the original host text for presentation; it does not expose or
promote the artifact range. The type makes host construction sites explicit
and auditable; it is not a capability barrier against trusted pilot code.

## Scenarios

| Scenario | Timed operation | Retained graph |
| --- | --- | --- |
| `Build` | Build all keys and consume their hashes | One completed store |
| `KeyMatch` | Compare and hash two equal stores | Two completed stores |
| `ValueTextEquals` | Parse each Name range and compare one UTF-8 query | One completed store |

`Build` reports allocation. The lookup scenarios prebuild their stores and
should allocate no bytes in the timed operation. All scenarios report elapsed
time and retained managed memory. Output identifies each exact input by
filename, assembly version, and module MVID.

`present` reports two additional comparisons over the population-backed store:

| Scenario | Normal path | User-text path |
| --- | --- | --- |
| `ScanAndPresent` | Scan with the encoded query, then decode and contain each match | Scan with the same bytes, then reuse the matched host text |
| `MatchedNamePresentation` | Repeatedly decode and contain one already-matched Name | Repeatedly consume the already-proven `UserTextMatch` |

The first scenario measures the realistic end-to-end list operation. The second
isolates the post-match cost that a large scan can hide. The normal path uses
the current UTF-16 `InertString` currency as a stand-in for constructing the
planned `InertUtf8String` from a Name-issued reader. Both cells emit identical
text; the command rejects a query whose `TextPolicy.Field` presentation would
require visual containment so the performance comparison does not time
different output.

## Run

The repository development SDK is required. First verify exact byte, equality,
hash, and query-match parity:

```bash
dotnet run --project tools/SymbolKeyRangePilot -c Release -- \
  check <assembly>...
```

Run a local harness smoke test:

```bash
dotnet run --project tools/SymbolKeyRangePilot -c Release -- \
  measure --rounds 3 --target-keys 5000 <assembly>...
```

Run the host-issued user-text experiment with a Name present in every input:

```bash
dotnet run --project tools/SymbolKeyRangePilot -c Release -- \
  present --user-text .ctor --rounds 3 --target-keys 5000 <assembly>...
```

`--rounds` must be at least 2 because XmR requires a moving range.

Accepted timing evidence must use a NativeAOT publication on an approved
performance host:

```bash
dotnet publish tools/SymbolKeyRangePilot -c Release -r linux-x64 \
  -o artifacts/symbol-key-range-pilot
perf-guard artifacts/symbol-key-range-pilot/symbol-key-range-pilot \
  measure --rounds 30 --target-keys 50000 --tsv results.tsv <assembly>...
```

SPC ratios are candidate divided by baseline. Values below `1` favor the
population-backed range, values above `1` favor the private per-key baseline,
and the assessment requires the full control interval to clear `1`.

## User-text presentation evidence

Candidate `8b61ed18e28103843d238eeaec42cd71c94bffcf` ran as
linux-x64 NativeAOT on `dotnet-inspect-perf` under one `perf-guard` lease. The
host was 99.75 percent idle when the lease began. Thirty rotated rounds measured
at least 50,000 keys per asset:

```text
perf-guard ./symbol-key-range-pilot present \
  --user-text .ctor --rounds 30 --target-keys 50000 \
  --tsv results.tsv \
  System.Private.CoreLib.dll \
  System.Text.Json.dll \
  Microsoft.Extensions.Configuration.dll
```

The binary SHA-256 was
`885ee6954f968bdfd70e044b46f1b636e024968546fec57b4ef239909fefad5b`.
Every normal and user-text cell produced identical output. Ratios are
`ReuseHostText` divided by `DecodeAndContain`:

| Asset | Keys | Matches | Scan and present | Matched presentation | Normal scan allocation | User-text allocation |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CoreLib | 62,986 | 2,553 | 0.927x, win | 0.124x, win | 81,696 B | 0 B |
| System.Text.Json | 53,662 | 2,422 | 0.917x, win | 0.124x, win | 77,504 B | 0 B |
| Microsoft.Extensions.Configuration | 50,290 | 2,889 | 0.881x, inconclusive | 0.124x, win | 92,448 B | 0 B |

The isolated matched-presentation baseline allocated 32 bytes per operation;
the user-text path allocated none. Its paired control intervals were
0.079x-0.159x, 0.088x-0.161x, and 0.106x-0.142x. Full scan-and-presentation
allocation also fell to zero. CoreLib and System.Text.Json had timing intervals
fully below parity; Configuration remained inconclusive.

The shortcut therefore has a clear post-match benefit and can materially help
when one query presents many exact matches. It does not establish a general
scan-speed win. The result supports a typed host-input design, not promotion of
the matched opaque range into trusted text. Product adoption must compare this
path with the actual `InertUtf8String` implementation once that currency exists;
the pilot's normal cell uses the current UTF-16 `InertString` path.

## Range representation evidence

The acceptance goal is parity or better with the private byte-native
`SymbolKey`, together with the large advantage over eager `InertString`
established by the earlier names-only pilot.

Candidate `08367168c9d5c6cc88b02935824427e7184278b7` ran as linux-x64 NativeAOT
on `dotnet-inspect-perf` under one `perf-guard` lease. The host was 99.75
percent idle when the lease began. Thirty rotated rounds measured at least
50,000 keys per asset:

```text
perf-guard ./symbol-key-range-pilot measure \
  --rounds 30 --target-keys 50000 --tsv results.tsv \
  System.Private.CoreLib.dll \
  System.Text.Json.dll \
  Microsoft.Extensions.Configuration.dll
```

The binary SHA-256 was
`63835f7884a79551a237b000de97cf27867380ff54ed46aabc2d7f96ca9a64ac`.
Inputs were .NET 11 RC1 `System.Private.CoreLib.dll` (`9573ebab...`),
`System.Text.Json` 10.0.9 (`21bf5768...`), and
`Microsoft.Extensions.Configuration` 10.0.10 (`82e24fb2...`). All strategies
produced byte-identical keys, hashes, equality answers, and query counts.
This run includes the declaring-Type chains for nested definitions and
references; it supersedes the earlier flattened-Type evidence.

Ratios are population-backed range divided by private `SymbolKey`:

| Asset | Keys | Build elapsed | KeyMatch elapsed | `ValueTextEquals` elapsed | Build allocation | Retained memory |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CoreLib | 62,986 | 0.953x, inconclusive | 1.010x, inconclusive | 1.065x, inconclusive | 0.892x, win | 0.817x, win |
| System.Text.Json | 54,592 | 0.814x, inconclusive | 1.036x, loss | 1.067x, inconclusive | 0.901x, win | 0.833x, win |
| Microsoft.Extensions.Configuration | 50,244 | 0.822x, inconclusive | 1.020x, inconclusive | 1.074x, inconclusive | 0.903x, win | 0.834x, win |

The absolute KeyMatch medians were 5.266 to 5.326 ms, 3.847 to 3.980 ms,
and 3.066 to 3.139 ms. The System.Text.Json paired control interval was
1.010x to 1.060x, so that result is a credible loss. `ValueTextEquals`
medians were 0.759 to 0.808 ms, 0.645 to 0.689 ms, and 0.583 to 0.627 ms,
but every paired control interval included parity.

Borrowing the already-bounded Name span avoids resolving a second
`OpaqueByteRange` in the scoped reader. On the corrected workload,
`ValueTextEquals` remains inconclusive on all three assets; the unchanged
KeyMatch path exposes the remaining System.Text.Json loss. Per the asymmetric
evidence rule, the pilot stopped rather than seeking a favorable second host.

The earlier names-only NativeAOT pilot measured its compact UTF-8 arena at
0.272x, 0.209x, and 0.223x matching time and 0.323x, 0.330x, and 0.318x
retained memory versus eager `InertString` on the same three asset families.
That evidence supports the large-win side of the goal, but it is not the same
full-key workload and cannot erase the private-`SymbolKey` regression here.

The population-backed representation therefore does not yet meet its
parity-or-better goal. The operator accepted this pilot result as sufficient
to proceed with staged implementation while retaining private `SymbolKey`
parity as a final Fast Diff adoption gate. The remaining KeyMatch gap must be
closed before that adoption is complete.
