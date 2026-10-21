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
| `PopulationSlotMeasured` | One immutable backing with one population-local slot per Name | The same UTF-8 contained-length scan |
| `PopulationSlotBytes` | One immutable backing with one population-local slot per Name | Exact stored byte count only |
| `PopulationContainedUtf8Measured` | One immutable backing with one retained `InertUtf8String` per Name | Contain the bytes and separately measure the contained UTF-16 length |
| `PopulationContainedUtf8Bytes` | One immutable backing with one retained `InertUtf8String` per Name | Contain the bytes and charge the contained UTF-8 byte count |

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
| `Utf8OutputAll` | Contain every Name and write it directly to a destination-owned UTF-8 buffer | Store plus complete UTF-8 output |

`Build` measures construction. Every other scenario starts with a completed
store and measures only its terminal operation. A terminal ratio therefore
answers how representations behave when reused; it is not an end-to-end
build-plus-terminal ratio.

The query is selected outside measurement from the least frequent non-empty
stored Name and encoded once. Every strategy must return the same match count,
roles, contained strings, and stable display hash. The local `check` command
also requires byte-for-byte equal direct UTF-8 output and proves that distinct
ill-formed bytes remain distinct and project to their exact `\uDCNN`
spellings.

This is kernel evidence. It does not establish an end-to-end `find`, inventory,
anchor, or declaration performance claim, and it does not change the product's
retained-text budget semantics.

## Scopes

The default `all` scope reads each TypeDef namespace/name, TypeRef
namespace/name, MethodDef, Param, GenericParam, Property, Event, and Field Name
once.

The `method-production` scope models the names retained by
`MetadataMethodDeclarationEvidence`: each method name and Param row appears
once, each method generic name appears once, and each declaring type's generic
names repeat for every method on that type.

## Run

Use the repository development SDK. Verify exact identity and presentation
parity first:

```bash
dotnet run --project tools/MetadataNameRangePilot -c Release -- \
  check <assembly>...
dotnet run --project tools/MetadataNameRangePilot -c Release -- \
  check --scope method-production <assembly>...
```

Run a local harness smoke test:

```bash
dotnet run --project tools/MetadataNameRangePilot -c Release -- \
  measure --scope method-production --rounds 3 \
  --target-names 5000 <assembly>...
```

Accepted timing evidence uses a NativeAOT publication on an approved
performance host:

```bash
dotnet publish tools/MetadataNameRangePilot -c Release -r linux-x64 \
  -o artifacts/metadata-name-range-pilot
perf-guard artifacts/metadata-name-range-pilot/metadata-name-range-pilot \
  measure --scope method-production --rounds 30 \
  --target-names 50000 --tsv results.tsv <assembly>...
```

SPC ratios are candidate divided by `EagerInert`. Values below `1` favor the
byte-native strategy, values above `1` favor eager containment, and a win or
loss requires the full paired control interval to clear `1`.

## Accepted evidence

The pilot published as linux-x64 NativeAOT and ran on `dotnet-inspect-perf-2`
under one `perf-guard` lease. The PR evidence records the exact candidate and
binary identities without making this document self-referential.

Thirty rotated rounds measured at least 50,000 Names per asset:

```text
perf-guard ./metadata-name-range-pilot measure \
  --scope method-production --rounds 30 \
  --target-names 50000 --tsv samples.tsv \
  System.Private.CoreLib.dll \
  System.Text.Json.dll \
  Microsoft.Extensions.Configuration.dll
```

The inputs were .NET 11 RC1 `System.Private.CoreLib.dll` (`9573ebab...`),
`System.Text.Json` 10.0.9 (`21bf5768...`), and
`Microsoft.Extensions.Configuration` 10.0.10 (`82e24fb2...`). Their
production-shaped censuses contained about 128,000, 10,000, and 400 Names.
Every strategy produced the same roles, match cardinality, contained display,
and stable display hash.

Ratios below are candidate divided by eager `InertString`; ranges span the
three assets:

| Observation | Ratio | Assessment |
| --- | ---: | --- |
| Per-name build retained memory | 1.17x-1.22x | Loss |
| Shared-range build retained memory | 0.44x-0.46x | Win |
| Shared-slot build retained memory | 0.25x-0.27x | Win |
| Shared-range match-only elapsed time | 0.50x-0.61x | Win |
| Shared-slot match-only elapsed time | 3.1x-3.6x | Loss |
| Shared-range sparse-display elapsed time | 0.57x-0.73x | Win |
| Shared-slot sparse-display elapsed time | 3.0x-3.4x | Loss |
| Shared-range display-all retained memory | 1.02x-1.05x | Loss |
| Shared-slot display-all retained memory | 0.88x-0.91x | Win |
| Byte-budget shared-slot build elapsed time | 0.1x-0.2x | Win |

The current per-name `OpaqueByteRange.Copy` shape is therefore not a memory
optimization: one byte array and one owner object per Name outweigh the UTF-8
payload reduction on all three assets. Retaining one range per Name over one
population backing changes the result, retaining about 45 percent of eager
memory while making closed matching 39 to 50 percent faster and sparse display
27 to 43 percent faster.

Retaining slots instead of ranges removes the repeated owner reference and
cuts the stored graph to about 25 percent of eager memory. It is not currently
a usable match representation: resolving and validating an
`OpaqueByteRange` through `Slice(slot)` for every candidate makes matching and
sparse selection about three times slower than eager strings. A future
owner-level population API must provide closed operations over slots before a
production adoption can claim both the slot-space result and the range-time
result.

The retained UTF-16 budget scan consumes much of the remaining construction
opportunity. Population build time with that scan was a win only for
System.Text.Json and inconclusive for CoreLib and Configuration; charging exact
stored bytes instead made construction 70 to 90 percent faster on every asset.
That result motivates a separately owned budget-unit decision rather than
silently changing product semantics in this pilot.

`DisplayAll` is the intentional control. Eager storage already owns the final
UTF-16 values, while byte-native storage must project every Name. Retaining
ranges leaves the combined byte-plus-UTF-16 graph 2 to 5 percent larger than
eager; retaining slots leaves it 9 to 12 percent smaller, but both still pay
the projection time and transient allocation. Byte-native adoption therefore
targets matching and sparse presentation; complete-display inventories need
an explicit representation decision.
