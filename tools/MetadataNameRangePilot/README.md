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

## Accepted evidence

The pilot published as linux-x64 NativeAOT and ran on `dotnet-inspect-perf`
under one `perf-guard` lease. The PR evidence records the exact candidate and
binary identities without making this document self-referential.

Thirty rotated rounds measured at least 50,000 Names per asset:

```text
perf-guard ./metadata-name-range-pilot measure \
  --rounds 30 --target-names 50000 --tsv samples.tsv \
  System.Private.CoreLib.dll \
  System.Text.Json.dll \
  Microsoft.Extensions.Configuration.dll
```

The inputs were .NET 11 RC1 `System.Private.CoreLib.dll` (`9573ebab...`),
`System.Text.Json` 10.0.9 (`21bf5768...`), and
`Microsoft.Extensions.Configuration` 10.0.10 (`82e24fb2...`). Their censuses
contained 142,677, 12,982, and 797 Names. Every strategy produced the same
roles, match cardinality, contained display, and stable display hash.

Ratios below are candidate divided by eager `InertString`; ranges span the
three assets:

| Observation | Ratio | Assessment |
| --- | ---: | --- |
| Per-name opaque build retained memory | 1.14x-1.20x | Loss |
| Population build allocation | 0.55x-0.57x | Win |
| Population retained memory | 0.44x-0.46x | Win |
| Population match-only elapsed time | 0.51x-0.56x | Win |
| Population sparse-display elapsed time | 0.56x-0.61x | Win |
| Byte-budget population build elapsed time | 0.16x-0.28x | Win |
| Population display-all elapsed time | 3.3x-3.9x | Loss |
| Population display-all allocation | 2.4x-2.7x | Loss |

The current per-name `OpaqueByteRange.Copy` shape is therefore not a memory
optimization: one array and one owner object per Name outweigh the UTF-8
payload reduction on all three assets. One population backing changes the
result, retaining about 45 percent of eager memory while making closed matching
44 to 49 percent faster and sparse display 39 to 44 percent faster.

The retained UTF-16 budget scan consumes much of the remaining construction
opportunity. Population build time with that scan was a win only for
System.Text.Json and inconclusive for CoreLib and Configuration; charging exact
stored bytes instead made construction 72 to 84 percent faster on every asset.
That result motivates a separately owned budget-unit decision rather than
silently changing product semantics in this pilot.

`DisplayAll` is the intentional control. Eager storage already owns the final
UTF-16 values, while byte-native storage must project every Name, so complete
display is 3.3x to 3.9x slower and allocates 2.4x to 2.7x as much. Byte-native
adoption therefore targets matching and sparse presentation; it is not a
universal replacement for eagerly displayed inventories.
