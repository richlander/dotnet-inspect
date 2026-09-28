# NLinq provenance

`NLinq/` is a verbatim copy of the NLinq library source, vendored as the
standing performance oracle for QuerySpace enablement. See
[Performance oracles for QuerySpace
enablement](../../docs/evidence-and-validation.md#performance-oracles-for-queryspace-enablement).

| Field | Value |
| --- | --- |
| Upstream | <https://github.com/agocke/NLinq> |
| Commit | `229e2435fc10f8a50e0fecb5e5a57bb18832f415` (2026-05-30) |
| Upstream path | `src/NLinq/*.cs` |
| Author | Andy Gocke |
| License | MIT, as the upstream README's License section declares |
| Modifications | None. Additions live in `tests/DotnetInspector.PerformanceOracles`. |

The upstream repository has no `LICENSE` file and no copyright line at this
commit; the README's `## License` section reads `MIT`. The copy is test
infrastructure: it is never built into a shipped artifact, so it is not listed
in `THIRD-PARTY-NOTICES.TXT`. Adding a `LICENSE` file upstream would complete
the record.

## File checksums

SHA-256 of each file as copied:

| File | SHA-256 |
| --- | --- |
| `ArrayEnumerator.cs` | `3d598802f8832918cb20c1bfe21f2de12eff86d4350e4a809c5f9052ebc8969e` |
| `IEnumerator.cs` | `ce03eac7456e8f10852a70893fafd9ac9900c81fe5783bb2c3de356c23ba8198` |
| `IFunc.cs` | `4fb23b0d0e97cdc3f604fc316bbbebe3d224df24622ea80b0d329fa44cb94a4e` |
| `ImmutableArrayEnumerator.cs` | `b31c5d1d6e794fc1fc366c8b0404913c9c7f3e67fe27184fb6363e2e5fa3b5c2` |
| `ListEnumerator.cs` | `dc213b47aedc9abbd5b2af1f4b343619e85283031b511c205fdbabc79725b523` |
| `NLinqExtensions.cs` | `78e8b4e89753f272040574d830606af00b28056fc227f78d4e2c58e350266729` |
| `ReadOnlyListEnumerator.cs` | `b1e1a486aa721b0ce0cf99f9f2a0965a921b91c4cc05e1180e74c6de7bea1470` |
| `TerminalExtensions.cs` | `7ec512494ac1d6a5614969b2d46c92bb45b698529173c3a5f18c9dd45b07dbf0` |

To advance the pin, copy `src/NLinq/*.cs` from the new upstream commit, update
this table, and rerun the oracle tests
(`dotnet run --project tests/DotnetInspector.PerformanceOracles.Tests -c Release`).
