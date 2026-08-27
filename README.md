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

The first enrolled file is
`System.Text.Encodings.Web/.../JavaScriptEncoder.cs` from
`System.Text.Encodings.Web` 10.0.10. Its two normal methods are the complete
authored-corpus-eligible member set; the constructor and expression-bodied
properties are excluded by `RealMethodTargetEnumerator`. Both `Create`
overloads clear Valid, Correct, and Printer exact under `default-v1`.

The immutable artifact and source identities are:

| Identity | Value |
| --- | --- |
| Package | `System.Text.Encodings.Web` 10.0.10 |
| Package SHA-512 | `TXtTo3/0UBfe6AAvB/4onunh+kGhRJcF/opONc+eU/f3Pf/u5mHWrtMwb/NUUHr6/74VvQD5O6ejZqgfk75eUg==` |
| Assembly asset | `lib/net10.0/System.Text.Encodings.Web.dll` |
| Assembly SHA-256 | `91f4b016890cfd5468d46d32c451931cac34096f869cc1c8077c902d9a7f5ccd` |
| Module version ID | `f7b10d91-a6d8-42ea-a59a-1e3aa37c31c3` |
| Source commit | `dotnet/dotnet@f7d90799ce4ef09a0bb257852a57248d2a8fb8dd` |
| Source SHA-256 | `CD1085BF738003442ABE2532A2693D2C14E608423A8D477ABD0055E0DDB14730` |

`AuthoredSourceOracleManifestTests` enforces the complete-set and nested-source
contracts. A follow-up main-branch change will add a periodic benchmark of the
product-produced rows against `oracle/manifest.json`. The existing
`--verify-authored-corpus` command independently re-acquires the source and
verifies its checksum and extracted bodies.

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

The source-oracle rows were harvested from the exact package asset above, then
selected by their immutable `sourceUrl`:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- \
  --harvest-authored-corpus /tmp/system-text-encodings-web.jsonl \
  --harvest-target 10000 \
  ~/.nuget/packages/system.text.encodings.web/10.0.10/lib/net10.0/System.Text.Encodings.Web.dll

jq -c \
  'select(.sourceUrl | endswith("/JavaScriptEncoder.cs"))' \
  /tmp/system-text-encodings-web.jsonl > oracle/corpus.jsonl
```

Bump the pinned assembly set (and re-harvest) to grow the corpus and the
real-world library set in lock step. Because it is a git worktree, edits made
under `external/authored-source-corpus` commit directly to this vendor branch.
