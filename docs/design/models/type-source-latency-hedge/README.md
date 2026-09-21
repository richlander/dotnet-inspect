# Type Source latency hedge model

This directory model-checks the scheduling and publication rules owned by
[Shared type source acquisition](../../type-source-acquisition.md#latency-hedge).
It proves nothing about elapsed wall-clock time, HTTP progress, decompiler
quality, Browser/Wasm scheduling, SourceHouse internals, or cache persistence.

## Scope

`TypeSourceLatencyHedge.tla` models one ordinary type-source operation:

- Portable PDB preparation may settle ready or unavailable before or after the
  initial preference window;
- authored settlement starts only after the PDB is ready;
- decompilation starts after PDB settlement or expiry of the initial window and
  records whether the PDB was ready at that point;
- available decompilation opens the finite authored preference window;
- unavailable decompilation cannot truncate the remaining authored path;
- verified authored source has priority over decompilation;
- pending PDB/authored losers settle before publication.

Cancellation, binding-policy currency, Library leases, Artifact cleanup, and
the concrete timing mechanism retain their owning contracts and are abstracted
as the final loser-settlement barrier.

## Checked properties

| Design property | Model property |
| --- | --- |
| Publication waits for loser settlement | `PublicationRequiresSettlement`, `PublishedWorkIsSettled` |
| Every selected result has its required completed producer | `SelectedResultExists` |
| Available authored source cannot be replaced by decompilation | `AuthoredSourceHasPriority` |
| Unavailable decompilation waits for the authored path to settle | `UnavailableWaitsForAuthored` |
| PDB-assisted decompilation starts only after PDB readiness | `PdbAssistedDecompilationRequiresReadyPdb` |

## Running TLC

Use the repository-pinned tools from
[Installing TLA+ and Java](../../../runbooks/tla-plus-setup.md):

```bash
cd docs/design/models/type-source-latency-hedge
java -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Safety.cfg TypeSourceLatencyHedge.tla
```

`Safety.cfg` is expected to exit successfully.

The recorded run used OpenJDK 25.0.4.1 and TLA+ tools 1.8.0
(`TLC2 2026.08.21.155922`, revision `9787e65`): 243 states generated,
171 distinct states, depth 11, and no error.
