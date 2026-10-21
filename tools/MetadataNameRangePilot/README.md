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
| `BuildAndDisplayAll` | Build the store, then project every Name to `InertString` | Store plus complete output |
| `BuildAndUtf8OutputAll` | Build the store, then write every Name to a destination-owned UTF-8 buffer | Store plus complete UTF-8 output |

`Build` measures construction. Every other scenario starts with a completed
store and measures only its terminal operation, except the two `BuildAnd...`
scenarios, which measure one-shot end-to-end work. A terminal-only ratio
answers how representations behave when reused.

The query is selected outside measurement from the least frequent non-empty
stored Name and encoded once. Every strategy must return the same match count,
roles, contained strings, and stable display hash. The local `check` command
also requires byte-for-byte equal direct UTF-8 output and proves that distinct
ill-formed bytes remain distinct and project to their exact `\uDCNN`
spellings. Its budget oracle compares measured strategies with contained
UTF-16 length, raw-byte strategies with source UTF-8 bytes, and contained-byte
strategies with contained UTF-8 bytes. A non-ASCII `é` known answer keeps those
units observably distinct.

This is kernel evidence. It does not establish an end-to-end `find`, inventory,
anchor, or declaration performance claim, and it does not change the product's
retained-text budget semantics.

## Slot latency diagnostic

The `slot-latency` command isolates descriptor mechanics from containment and
presentation. It builds one shared byte population and compares:

- a 16-byte retained descriptor containing the array reference, offset, and
  length;
- an 8-byte slot resolved by an owner-fused checked operation; and
- the same 8-byte slot through a trusted-coordinate diagnostic that omits the
  array bounds check.

The trusted-coordinate path is a diagnostic ceiling, not a product
recommendation. Comparing it with the checked slot isolates the bounds-check
cost; comparing both slots with the retained range determines whether compact
coordinates can preserve match latency. The three no-inline count kernels also
provide stable method boundaries for JIT or NativeAOT code inspection.

Thirty rotated NativeAOT rounds on the three accepted assets found:

| Asset | Checked slot / retained range | Trusted slot / retained range |
| --- | ---: | ---: |
| CoreLib | approximately 0.99x, inconclusive | approximately 0.73x, win |
| System.Text.Json | approximately 1.00x, inconclusive | approximately 0.70x, win |
| Configuration | approximately 1.00x, inconclusive | approximately 0.69x, win |

The checked 8-byte descriptor therefore preserves the 16-byte descriptor's
match latency. NativeAOT disassembly shows that it does not do so through
bounds-check elimination: both checked kernels emit an `offset + length`
comparison with the backing array length and branch to the throw path. The
trusted-coordinate kernel omits that comparison and establishes the cost
ceiling shown above.

The result explains the earlier slot loss. Descriptor width was not the
problem; resolving each slot through the general checked
`OpaqueByteRange.Slice(slot)` path before every comparison was. An owner-fused
checked operation recovers range latency while retaining the 8-byte
descriptor. Omitting the remaining check would require a stronger
collection-private invariant that prevents foreign or otherwise unvalidated
slots from reaching the operation; this pilot does not recommend an unsafe
public slot API.

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
dotnet run --project tools/MetadataNameRangePilot -c Release -- \
  slot-latency --scope method-production --rounds 3 \
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
stable display hash, and direct UTF-8 output.

Ratios below are candidate divided by eager `InertString`; ranges span the
three assets:

| Retained graph | Shared ranges | Shared slots | Contained UTF-8 |
| --- | ---: | ---: | ---: |
| Built store | 0.447x-0.450x | 0.251x-0.265x | 0.647x-0.656x |
| Store plus complete UTF-8 output | 0.512x-0.523x | 0.339x-0.364x | 0.694x-0.697x |
| Store plus complete UTF-16 output | 1.020x-1.040x | 0.882x-0.908x | 1.168x-1.182x |

| Reused-store terminal time | Shared ranges | Shared slots | Contained UTF-8 |
| --- | ---: | ---: | ---: |
| Match every Name | 0.49x-0.63x | 3.2x-3.6x | 0.88x-1.03x |
| Match all, display selected | 0.56x-0.73x | 2.9x-3.3x | 1.00x-1.15x |
| Write every Name as UTF-8 | 3.2x-4.3x | 3.2x-4.3x | 0.35x-0.90x |
| Project every Name to UTF-16 | 2.4x-4.0x | 2.3x-5.3x | 1.3x-2.5x |

The contained UTF-8 match and sparse medians were near eager, but their
control intervals did not establish a general time win. Direct contained
UTF-8 output was a time win on CoreLib and System.Text.Json and inconclusive
on the smaller Configuration asset. All retained-space differences above
were wins or losses as shown.

The one-shot `BuildAnd...` scenarios changed the interpretation:

- build plus complete UTF-8 output was time-inconclusive for every byte-native
  representation, while allocating 34% to 80% as much and retaining 34% to
  70% as much as eager;
- build plus complete UTF-16 projection was also time-inconclusive, while
  shared ranges allocated 10% to 12% more, slots allocated 9% to 12% less,
  and contained UTF-8 allocated about 25% more.

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

Retaining `InertUtf8String` pays visual containment once and keeps the
contained bytes bound to their owner. It uses about 65 percent of eager
storage: more than raw ranges, but it makes repeated complete UTF-8 output
35 to 61 percent faster on the two assets with conclusive timing while
retaining only about 70 percent of eager's store-plus-output graph. Raw ranges
and slots save more space, but repeated byte output is 3.3 to 4.2 times slower
because every write repeats containment.

The retained UTF-16 budget scan consumes much of the remaining construction
opportunity. Charging contained UTF-8 bytes instead produced medians 25 to
38 percent below eager without establishing a consistent all-asset win. That
signal motivates a separately owned budget-unit decision rather than silently
changing product semantics in this pilot.

`DisplayAll` is the intentional control. Eager storage already owns the final
UTF-16 values, while byte-native storage must project every Name. Retaining
ranges leaves the combined byte-plus-UTF-16 graph 2 to 4 percent larger than
eager; retaining slots leaves it 9 to 12 percent smaller, but both still pay
the projection time and transient allocation. The one-shot end-to-end time was
inconclusive rather than a demonstrated loss.

## Recommendations

| Product scenario | Recommended retained representation | Reason |
| --- | --- | --- |
| Match-heavy analysis or match-all/project-few | Shared `OpaqueByteRange` values | Best established time result with about 55% less retained memory |
| Repeated output to a UTF-8-capable sink | Shared `InertUtf8String` values | Containment is paid once; later writes copy contained bytes without UTF-16 |
| One-shot output to a UTF-8-capable sink | Shared ranges when lower allocation and peak memory matter | End-to-end time was inconclusive, but allocation and retained memory were substantially lower |
| Inventory that always retains every UTF-16 value | Eager `InertString` | No byte-native end-to-end time win was established; eager storage is already the sink representation |
| Maximum compactness | Slots only after owner-level closed operations exist | Best space result, but current checked `Slice(slot)` composition loses reusable terminal time |

These recommendations select a representation for the terminal rather than
claiming that one representation should replace every other one.
