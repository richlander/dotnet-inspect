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

## Accepted evidence

The acceptance goal is parity or better with the private byte-native
`SymbolKey`, together with the large advantage over eager `InertString`
established by the earlier names-only pilot.

Candidate `11d7b7649730d7a0016f2472f98847d91eb10a2f` ran as linux-x64 NativeAOT
on `dotnet-inspect-perf` under one `perf-guard` lease. The host was 99.80
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
`ad8285adc26b1eea8c7c0dc1afb2227d998d5c6fe0d87d905036fe672070253a`.
Inputs were .NET 11 RC1 `System.Private.CoreLib.dll` (`9573ebab...`),
`System.Text.Json` 10.0.9 (`21bf5768...`), and
`Microsoft.Extensions.Configuration` 10.0.10 (`82e24fb2...`). All strategies
produced byte-identical keys, hashes, equality answers, and query counts.

Ratios are population-backed range divided by private `SymbolKey`:

| Asset | Keys | Build elapsed | KeyMatch elapsed | `ValueTextEquals` elapsed | Build allocation | Retained memory |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CoreLib | 62,986 | 0.831x, inconclusive | 0.996x, inconclusive | 1.061x, inconclusive | 0.885x, win | 0.805x, win |
| System.Text.Json | 54,592 | 0.746x, inconclusive | 1.125x, loss | 1.048x, inconclusive | 0.895x, win | 0.822x, win |
| Microsoft.Extensions.Configuration | 50,244 | 0.712x, inconclusive | 1.024x, inconclusive | 1.058x, inconclusive | 0.887x, win | 0.808x, win |

The absolute KeyMatch medians were 5.223 to 5.214 ms, 3.786 to 4.280 ms,
and 2.858 to 2.932 ms. The System.Text.Json paired control interval was
1.059x to 1.198x, so that result is a credible loss. `ValueTextEquals`
medians were 0.764 to 0.810 ms, 0.656 to 0.687 ms, and 0.587 to 0.620 ms,
but every paired control interval included parity.

The first range-reader candidate retained an `OpaqueByteRange` in the scoped
reader and produced a 1.133x to 1.154x `ValueTextEquals` loss. Borrowing the
already-bounded Name span in the final candidate removed that credible loss.
The unchanged KeyMatch path then exposed the remaining System.Text.Json loss.
Per the asymmetric evidence rule, the pilot stopped rather than seeking a
favorable second host.

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
