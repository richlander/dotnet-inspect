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
