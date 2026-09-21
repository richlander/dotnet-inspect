# Type Source PDB latency hedge model

This directory model-checks the conservative PDB-only scheduling and
publication rules owned by
[Type Source latency hedge implementations](../../type-source-acquisition.md#latency-hedge-implementations).
It proves nothing about elapsed wall-clock time, HTTP progress, decompiler
quality, Browser/Wasm scheduling, SourceHouse internals, or cache persistence.

## Scope

`TypeSourcePdbLatencyHedge.tla` models one ordinary type-source operation:

- Portable PDB preparation may settle ready or unavailable before or after the
  initial preference window;
- prompt PDB readiness completes authored settlement before decompilation;
- decompilation starts with the PDB only after authored source is unavailable;
- window expiry permits no-PDB decompilation while PDB acquisition remains
  live;
- available no-PDB decompilation wins the expired-PDB path without a second
  authored preference window;
- unavailable decompilation cannot truncate the remaining PDB/authored path;
- pending PDB/authored losers settle before publication.

Cancellation, binding-policy currency, Library leases, Artifact cleanup, and
the concrete timing mechanism retain their owning contracts and are abstracted
as the final loser-settlement barrier.

## Checked properties

| Design property | Model property |
| --- | --- |
| Publication waits for loser settlement | `PublicationRequiresSettlement`, `PublishedWorkIsSettled` |
| Every selected result has its required completed producer | `SelectedResultExists` |
| PDB-assisted decompilation follows authored failure | `PdbAssistedDecompilationFollowsAuthoredFailure` |
| No-PDB decompilation requires PDB unavailability or window expiry | `NoPdbDecompilationRequiresBoundary` |
| Unavailable decompilation preserves the remaining source path | `UnavailableWaitsForRemainingSource` |
| Available decompilation wins after PDB-window expiry | `ExpiredPdbPathDoesNotPreferLateAuthored` |

## Running TLC

Use the repository-pinned tools from
[Installing TLA+ and Java](../../../runbooks/tla-plus-setup.md):

```bash
cd docs/design/models/type-source-pdb-latency-hedge
java -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Safety.cfg TypeSourcePdbLatencyHedge.tla
```

`Safety.cfg` is expected to exit successfully.

The recorded run used OpenJDK 25.0.4.1 and TLA+ tools 1.8.0
(`TLC2 2026.08.11.125311`): 113 states generated, 90 distinct states, depth
10, and no error.
