# Metadata Name Range Pilot

This NativeAOT pilot separates the cost of metadata Name representation from
the much larger declaration, signature, relationship, and marker work around
it. It compares eager contained UTF-16 with the byte-native shapes relevant to
the migration tracked by
[#10169](https://github.com/richlander/dotnet-inspect/issues/10169).

## Strategies

| Strategy | Storage | Budget work during build |
| --- | --- | --- |
| `EagerInert` | One decoded `string` retained by `InertString` per Name | Two containment scans, modeling the declaration baseline's measure-then-retain path |
| `PerNameOpaqueMeasured` | One `OpaqueByteRange.Copy` backing per Name | One UTF-8 scan measuring the contained UTF-16 length, matching #10344 |
| `PopulationOpaqueMeasured` | One immutable backing with one range per Name | The same UTF-8 contained-length scan |
| `PopulationOpaqueBytes` | One immutable backing with one range per Name | Exact stored byte count only |

The eager baseline deliberately constructs `InertString` twice over the same
decoded string. For ordinary names, both constructions retain that original
string without another string allocation. The real-asset check rejects a Name
that requires presentation spelling, so this models the common path rather
than overstating the baseline with discarded encoded strings.

## Scenarios

| Scenario | Timed operation | Retained graph |
| --- | --- | --- |
| `Build` | Construct and retain the complete Name population | One completed store |
| `MatchOnly` | Query every Name without presentation | One completed store |
| `SparseDisplay` | Query every Name and project only matching Names to `InertString` | Store plus selected output |
| `DisplayAll` | Project every Name to `InertString` | Store plus complete output |

The query is selected outside measurement from the least frequent non-empty
stored Name and encoded once. Every strategy must return the same match count,
roles, contained strings, and stable display hash. The local `check` command
also proves that distinct ill-formed bytes remain distinct and project to
their exact `\uDCNN` spellings.

This is kernel evidence. It does not establish an end-to-end `find`, inventory,
anchor, or declaration performance claim, and it does not change the product's
retained-text budget semantics.

## Run

Use the repository development SDK. Verify exact identity and presentation
parity first:

```bash
dotnet run --project tools/MetadataNameRangePilot -c Release -- \
  check <assembly>...
```

Run a local harness smoke test:

```bash
dotnet run --project tools/MetadataNameRangePilot -c Release -- \
  measure --rounds 3 --target-names 5000 <assembly>...
```

Accepted timing evidence uses a NativeAOT publication on an approved
performance host:

```bash
dotnet publish tools/MetadataNameRangePilot -c Release -r linux-x64 \
  -o artifacts/metadata-name-range-pilot
perf-guard artifacts/metadata-name-range-pilot/metadata-name-range-pilot \
  measure --rounds 30 --target-names 50000 --tsv results.tsv <assembly>...
```

SPC ratios are candidate divided by `EagerInert`. Values below `1` favor the
byte-native strategy, values above `1` favor eager containment, and a win or
loss requires the full paired control interval to clear `1`.
