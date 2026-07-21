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

Each line is one JSON record with this schema (camelCase):

| Field | Meaning |
| --- | --- |
| `assembly`, `assemblyVersion`, `tfm` | Pinned assembly identity the method came from. |
| `type`, `method`, `overload`, `signature` | RTS `RequestedTarget` identity (overload is the RTS ordinal). |
| `metadataToken`, `parameterCount`, `ilSize` | Method metadata. |
| `sourceUrl`, `checksumAlgorithm`, `checksum` | SourceLink provenance for the authored file at the pinned commit. |
| `authoredBody` | The checksum-verified authored member body (member-only slice). |
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

This corpus lives on an orphan branch precisely so these snapshots stay out of
the main project history.

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

Bump the pinned assembly set (and re-harvest) to grow the corpus and the
real-world library set in lock step. Because it is a git worktree, edits made
under `external/authored-source-corpus` commit directly to this vendor branch.
