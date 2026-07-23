---
name: corpus-maintenance
description: Use for authored-source correspondence corpus maintenance: verify the vendored CIVIL/EVIL snapshot for drift against upstream, re-harvest or re-pin it, run the offline benchmark, and decide when to advance the pin.
---

# Corpus maintenance

Use this skill when the user asks to maintain, refresh, drift-check, re-pin, or
benchmark the **authored-source correspondence corpus** — the vendored source
oracle behind the decompiler's offline benchmark. Produce a short status report:
whether the pinned snapshot still corresponds to upstream, whether it needs a
re-harvest or a pin bump, and what the benchmark says about the current
decompiler.

Run from the repository root. The corpus tooling is the `DecompilerHarness`
(`tools/DecompilerHarness`), a dev tool, not the shipped `dotnet-inspect`
product; run it with `dotnet run --project tools/DecompilerHarness -c Release --`.

## What the corpus is

The corpus is a vendored JSONL where each row is a real method identity plus a
checksum-verified authored member body captured through SourceLink at harvest
time. It lets the benchmark grade source correspondence fully offline, so
`SourceUnavailable` becomes a drift signal rather than an expected network
outcome. It has two halves:

- **CIVIL** (Curated Index of Varied IL) — `civil/corpus.jsonl`, harvested from
  the 14 pinned assemblies in `eng/prepare-decompiler-corpus.sh`. This is the
  stable regression target that grows in lock step with the fixed real-world
  corpus.
- **EVIL** (Edge-case Verification of IL Legibility) — `evil/corpus.jsonl`, an
  adversarial stress set of the most difficult real methods drawn from a much
  broader pool (`eng/prepare-evil-corpus.sh`), difficulty-ranked off the shared
  IL substrate.

Both live on the `vendor/authored-source-corpus` orphan branch so the harvested
third-party source snapshots never enter `main`'s history. Restore them into a
git worktree first:

```bash
bash eng/restore-authored-source-corpus.sh
```

That materializes `external/authored-source-corpus`. Because it is a worktree on
the orphan branch, edits made there (a re-harvest) commit directly to
`vendor/authored-source-corpus`.

## The pin, and why it is stable

Every corpus assembly is a **pinned** published package version, so the only
thing that varies across a decompiler change is the tool, never the input. The
dotnet-inspect self-assemblies come from the published `dotnet-inspect.any`
package (`SELF_VERSION` in `eng/prepare-decompiler-corpus.sh`), not the local
`artifacts/bin` build — that removes corpus drift (#1404) and breaks the
circularity where a decompiler change would rebuild both the tool and its own
corpus at once.

Two pin axes matter:

- **Package version** — `SELF_VERSION` (and the third-party versions in
  `prepare-decompiler-corpus.sh`).
- **Source commit** — each row's SourceLink commit, which is where its authored
  body was captured.

Pinning buys attribution and reproducibility; "update to latest" is a
deliberate, reviewed re-pin (a re-harvest), because pins rot as upstream moves.
Drift verification is the bridge that turns "should we re-pin?" into an
evidence-driven decision.

## Verify drift (start here)

Drift verification re-acquires each vendored row's authored source *today* and
compares it byte-for-byte (newline-normalized) against the stored snapshot. It
does **not** run the decompiler; it audits the corpus, not the tool. Prepare the
pinned assemblies, then verify CIVIL, resolving dotnet-inspect's own rows from
local git via `--repo`:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- \
  --verify-authored-corpus external/authored-source-corpus/civil/corpus.jsonl \
  --repo "$(git rev-parse --show-toplevel)" \
  $(cat /tmp/corpus-assemblies.txt)
```

Each row lands in one of three states:

- **Verified** — the re-acquired body matches the vendored snapshot.
- **Drifted** — the body differs; the upstream source, the harvest slice, or the
  stored row itself has changed. The report names the row with a short
  line-count/first-diff summary.
- **Unavailable** — the source could not be re-acquired or sliced (offline with
  no `--repo`, commit gone, checksum mismatch, or an extraction regression). The
  row is surfaced, never silently dropped.

It is report-only by default (exit 0). The run's own integrity still governs the
exit code with a named blocker: any corpus row whose assembly was not supplied
(`unmatchedRows > 0`) or a run that evaluated no rows fails, so an empty or
partially-unmatched run is never a success. Add `--fail-on-drift` to make it a
fail-closed gate — the run then exits nonzero unless *every* evaluated row is
Verified (any Drifted or Unavailable row fails):

```bash
dotnet run --project tools/DecompilerHarness -c Release -- \
  --verify-authored-corpus external/authored-source-corpus/civil/corpus.jsonl \
  --repo "$(git rev-parse --show-toplevel)" --fail-on-drift \
  $(cat /tmp/corpus-assemblies.txt)
```

Always supply the same pinned assemblies the corpus was harvested from; a
missing assembly is a blocker, not a pass.

## Interpret and decide

- **All Verified** — the pinned snapshot still corresponds to upstream. No
  action; the corpus is a faithful oracle.
- **Drifted rows** — upstream (or the slice) moved off the pinned commit. This is
  expected as repositories evolve and is **not a bug** — the fix is a deliberate
  re-harvest / re-pin (below), not a code change. Confirm the drift is upstream
  churn and not a harvester regression before advancing.
- **Unavailable rows** — usually an outage, a missing PDB, or a bad/incomplete
  `--repo` checkout, not corpus rot. Re-run with a complete `--repo` and network
  before concluding the source is gone. Persistent Unavailable at a known-good
  commit points at an extraction regression worth an issue.

Distinguish "the corpus drifted from upstream" (re-pin) from "the harness can no
longer resolve known-good source" (investigate the harness).

## Re-harvest and re-pin

Advancing the snapshot is a deliberate, reviewed act. To re-harvest CIVIL from
the current pins straight into the vendored worktree:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- \
  --harvest-authored-corpus external/authored-source-corpus/civil/corpus.jsonl \
  --harvest-target 12000 \
  --repo "$(git rev-parse --show-toplevel)" \
  $(cat /tmp/corpus-assemblies.txt)
```

`--repo` (repeatable) reads each target's authored source from a local git clone,
arbitrated by the PDB checksum, falling back to the network on any mismatch or
miss. Pointing it at this checkout resolves the dotnet-inspect self-corpus rows
with no GitHub round-trip; it also unlocks private, large, or offline source
repositories. Third-party rows are checksum-arbitrated and fall back to the
network.

To **update to latest** (advance the pin, not just re-capture the same commit),
bump `SELF_VERSION` in `eng/prepare-decompiler-corpus.sh` (and any third-party
version), re-emit the real-world baseline, then re-harvest as above. Re-harvest
EVIL from its broad pool the same way:

```bash
bash eng/prepare-evil-corpus.sh /tmp/evil-pool
dotnet run --project tools/DecompilerHarness -c Release -- \
  --harvest-evil-corpus external/authored-source-corpus/evil/corpus.jsonl \
  --harvest-target 12000 \
  $(cat /tmp/evil-pool/assemblies.txt)
```

Commit the regenerated `corpus.jsonl` in the `external/authored-source-corpus`
worktree (it commits to the orphan branch), and land the pin bump on `main`
through a normal reviewed PR. Keep the two in step so the benchmark and the
fixed real-world corpus stay aligned.

## Benchmark

The benchmark feeds the vendored authored bodies into the same
source-correspondence oracle the on-demand census uses and emits a taste card. It
validates the corpus and the current decompiler together; run it after a
re-harvest or when asked for a decompiler correspondence read:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- \
  --benchmark-authored-corpus external/authored-source-corpus/civil/corpus.jsonl \
  $(cat /tmp/corpus-assemblies.txt)
```

Add `--json` for structured output. Each target is `Correct`, one of four
valid-but-different taste buckets, `Invalid`, or a diagnostic bucket. The run
exits nonzero on any `Invalid`, `Drift`, or `Unsupported`, and on a dishonest run
(unmatched rows or zero targets). A `Drift` bucket here means the corpus identity
no longer resolves in the pinned assembly — treat it like a Drifted verify row.

## Guardrails

- Keep the benchmark hermetic: it must stay fully offline over the vendored
  bodies. Do not add live network resolution to the benchmark path.
- Do not delete the `vendor/authored-source-corpus` orphan branch or migrate its
  bodies onto `main`. The vendored snapshot is the reproducible oracle.
- Build the harness with the solution graph: `dotnet build dotnet-inspect.slnx
  -c Release`. If a user-global NuGet feed trips NU1507, add `--configfile
  ./nuget.config`.
- Verify and benchmark over the pinned assemblies, never a fresh local build of
  the tool (that reintroduces #1404 circularity).

## Report shape

Keep the report short:

```md
Corpus maintenance: <verified | drifted | re-pinned | benchmarked>

- Corpus: CIVIL <verified/drifted/unavailable counts>; EVIL <if checked>.
- Drift cause: <none | upstream churn (re-pin) | outage/--repo (retry) | harness regression (issue)>.
- Action: <none | re-harvest | bump SELF_VERSION + re-harvest | file issue>.
- Benchmark (if run): <correct / taste-bucket / invalid / drift counts>, exit <code>.
```
