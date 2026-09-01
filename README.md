# Authored-source correspondence corpus (vendor branch)

This orphan branch (`vendor/authored-source-corpus`) carries the vendored
authored-source correspondence corpus consumed by the decompiler harness'
offline benchmark run-mode. It is an **orphan branch**: it shares no history with
`main`, so the harvested third-party source snapshots never enter the main
project history.

Restore it into a working tree with:

```bash
bash eng/restore-authored-source-corpus.sh
```

which adds a git worktree at `external/authored-source-corpus`.

## What is here

| Path | Contents |
| --- | --- |
| `civil/corpus.jsonl` | CIVIL corpus (Curated Index of Varied IL), harvested from the same pinned assemblies as the fixed real-world decompiler corpus (`eng/prepare-decompiler-corpus.sh`). |
| `evil/corpus.jsonl` | EVIL corpus (Edge-case Verification of IL Legibility): the most diabolical real methods, difficulty-ranked, drawn from a much broader pool (top-100 NuGet packages plus the 14 real-world pins). |
| `oracle/corpus.jsonl` | Whole-file source-oracle members carrying versioned pre-normalization Printer bodies. |
| `oracle/manifest.json` | Complete immutable file/member identities and the source-layer requirements enforced for each oracle file. |

Each line is one JSON record with this schema (camelCase):

| Field | Meaning |
| --- | --- |
| `assembly`, `assemblyVersion`, `tfm` | Pinned assembly identity the method came from. |
| `type`, `method`, `overload`, `signature` | RTS `RequestedTarget` identity (overload is the RTS ordinal). |
| `metadataToken`, `moduleVersionId`, `parameterCount`, `ilSize` | Method metadata. The module ID binds the token to the logical module used for harvest and benchmark attribution. |
| `sourceUrl`, `checksumAlgorithm`, `checksum` | SourceLink provenance for the authored file at the pinned commit. |
| `authoredBody` | The checksum-verified authored member body (member-only slice). |
| `printerBody`, `printerBodyVersion` | Optional mechanically extracted block text and the version of its pre-normalization comparison contract. Required by Printer-exact oracle rows. |
| `difficulty` | EVIL only: IL-difficulty breakdown (`ilSize`, `blockCount`, `branchCount`, `switchCount`, `exceptionRegionCount`, `exceptionNestingDepth`, `rareOpcodeCount`, `localCount`, `maxStack`, `score`) used to rank candidates. Absent on CIVIL rows. |

## Provenance

Every `authoredBody` is a member-only excerpt of its upstream project, fetched at
harvest time through SourceLink from the pinned published package's documents,
and checksum-verified against the SourceLink document checksum before being
recorded. A target whose source does not resolve or verify is skipped, so every
row carries real, verified source. The `sourceUrl` + `checksum` on each row
identify the exact upstream source commit and content.

The CIVIL corpus was harvested from the 14 pinned assemblies in
`eng/prepare-decompiler-corpus.sh`. Three of them (`Newtonsoft.Json`,
`Microsoft.ApplicationInsights`, `NuGet.Versioning`) contributed no rows because
their pinned packages did not resolve authored source through SourceLink; they
are skipped, not failed.

The EVIL corpus was harvested from a broader pool assembled by
`eng/prepare-evil-corpus.sh`: the top-ranked NuGet packages
(`docs/data/nuget-top-packages.json`, currently 100 ranks) unioned with the 14
real-world pins, deduped by assembly name. Candidates are ranked by IL
difficulty so the corpus concentrates the hardest real methods; only libraries
whose authored source resolves and checksum-verifies through SourceLink
contribute rows.

The `moduleVersionId` field was added after the original harvest from the same
pinned NuGet artifacts. The migration validated every row's assembly version,
MethodDef token, declaring type, method name, and overload against that module;
all existing JSON fields, including the SourceLink checksum and authored body,
were preserved unchanged. A fresh token-based acquisition also confirmed that
all 24,000 rows still bind to their stored SourceLink document checksum. The
benchmark revalidates the module ID, token,
identity, overload, and normalized signature before using a body for fault
attribution. The module ID is a logical build identity under the non-hostile,
pinned-package corpus contract, not a cryptographic digest of the assembly
bytes.

This corpus lives on an orphan branch precisely so these snapshots stay out of
the main project history.

## Whole-file source oracles

Four `dotnet/dotnet` files are enrolled from package version 10.0.10. The
candidate ledger proved each file's complete authored-corpus-eligible method
set Valid, Correct, and Printer exact under `default-v1`. Constructors,
accessors, expression-bodied declarations, and other targets outside
`RealMethodTargetEnumerator` remain outside that claim.

| File | Eligible methods | Observed features | New features |
| --- | ---: | ---: | ---: |
| `JavaScriptEncoder.cs` | 2 | 3 | 3 |
| `BinaryAssemblyInfo.cs` | 1 | 11 | 8 |
| `VirtualPropertyBase.PropertyGetterBase.cs` | 1 | 5 | 1 |
| `HandleKind.cs` | 1 | 4 | 1 |

The batch raises the aggregate syntax inventory from 3 to 13 features. The
immutable artifact identities are:

| Package | Asset | Assembly SHA-256 | Module version ID |
| --- | --- | --- | --- |
| `System.Text.Encodings.Web` 10.0.10 | `lib/net10.0/System.Text.Encodings.Web.dll` | `91f4b016890cfd5468d46d32c451931cac34096f869cc1c8077c902d9a7f5ccd` | `f7b10d91-a6d8-42ea-a59a-1e3aa37c31c3` |
| `System.Runtime.Serialization.Formatters` 10.0.10 | `lib/net8.0/System.Runtime.Serialization.Formatters.dll` | `33693c0971e95d158efc64307e6ef379a9dc322f1642178e3c29c8e1d4db255e` | `53810d48-6104-4fad-b854-9b5c1cb93d2b` |
| `System.Reflection.Context` 10.0.10 | `lib/net10.0/System.Reflection.Context.dll` | `94da27080f9aaa03e3719828976838ba39b0d8d7299fe9bd6130b1c822014f3b` | `94498402-c7a5-4de7-b614-b6fe0ea2eae7` |
| `System.Reflection.Metadata` 10.0.10 | `lib/net10.0/System.Reflection.Metadata.dll` | `2a8c49aa47e910f4e690bce79be3986d3cfb0df8d8e978bbdf51b76d594a378d` | `ef1403e6-bb3c-4941-bb0c-0d714056a836` |

The corresponding package SHA-512 values are:

| Package | Package SHA-512 |
| --- | --- |
| `System.Text.Encodings.Web` | `TXtTo3/0UBfe6AAvB/4onunh+kGhRJcF/opONc+eU/f3Pf/u5mHWrtMwb/NUUHr6/74VvQD5O6ejZqgfk75eUg==` |
| `System.Runtime.Serialization.Formatters` | `rHuimY/dvbyi68WAmFtpu9IK0YasJNLocf1OTtJvStEqrOtRN3iwjrkqw4sZUTAJoqhb+OHHf1MAOSpo9+aOqg==` |
| `System.Reflection.Context` | `4VPsXIHiATwLKxjBm4agfaS3EcVwvLVj5+ONXkOZs2SW3FcM8VXzViN8J3u5a6U71vY6QrYTZJ3yNMN/EKqs/w==` |
| `System.Reflection.Metadata` | `YLol8JpRVrJJ+Me1RQlmXztNO7+O+wMp//h/I3hMQO17VtfEroEsHxkgcAkdBBLxes70sW/j31TrwN5eLbkJ7w==` |

All four files resolve to
`dotnet/dotnet@f7d90799ce4ef09a0bb257852a57248d2a8fb8dd`. Their exact source
SHA-256 checksums are recorded in `oracle/manifest.json` and every corresponding
corpus row.

`AuthoredSourceOracleManifestTests` enforces the complete-set, nested-source,
Printer-exact, and syntax-inventory contracts. The existing
`--verify-authored-corpus` command independently re-acquires each source file
and verifies its checksum and extracted body.

## Regenerating

The CIVIL corpus is reproducible from the pinned assemblies:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- \
  --harvest-authored-corpus civil/corpus.jsonl --harvest-target 12000 \
  $(cat /tmp/corpus-assemblies.txt)
```

The EVIL corpus is reproducible from the broad-source pool:

```bash
bash eng/prepare-evil-corpus.sh /tmp/evil-pool
dotnet run --project tools/DecompilerHarness -c Release -- \
  --harvest-evil-corpus evil/corpus.jsonl --harvest-target 12000 \
  $(cat /tmp/evil-pool/assemblies.txt)
```

The source-oracle rows are harvested from the exact package assets above, then
selected by their immutable `sourceUrl`:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- \
  --harvest-authored-corpus /tmp/source-oracle-harvest.jsonl \
  --harvest-target 10000 \
  ~/.nuget/packages/system.text.encodings.web/10.0.10/lib/net10.0/System.Text.Encodings.Web.dll \
  ~/.nuget/packages/system.runtime.serialization.formatters/10.0.10/lib/net8.0/System.Runtime.Serialization.Formatters.dll \
  ~/.nuget/packages/system.reflection.context/10.0.10/lib/net10.0/System.Reflection.Context.dll \
  ~/.nuget/packages/system.reflection.metadata/10.0.10/lib/net10.0/System.Reflection.Metadata.dll

jq -c \
  'select(
    .sourceUrl
    | endswith("/JavaScriptEncoder.cs")
      or endswith("/BinaryAssemblyInfo.cs")
      or endswith("/VirtualPropertyBase.PropertyGetterBase.cs")
      or endswith("/HandleKind.cs")
  )' \
  /tmp/source-oracle-harvest.jsonl > oracle/corpus.jsonl
```

Grow this corpus only from candidate-ledger-qualified rows. After a vendor
change lands, update the main-branch source-oracle assembly preparation and
periodic consumer before treating the expanded gate as active. Because this is
a git worktree, edits commit directly to the vendor branch.
