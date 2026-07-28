# Platform-assembly index generator

Offline build tool that produces `assets/platform-index.tsv` — a compact,
instantly-loadable map of the .NET platform assemblies for each target
framework: their file/assembly names, which are **facades**, and the
implementation assembly a facade forwards to.

It is **not** part of the WASM app; it runs on the full SDK. The prototype ships
the generated TSV as a static asset so the browser gets a first-take hint about
platform libraries without downloading or decoding any pack.

## What it indexes

- `Microsoft.NETCore.App.Ref` — the authoritative public assembly set + logical
  public type counts per assembly, for `net6.0`–`net10.0`.
- `Microsoft.NETCore.App.Runtime.linux-x64` — the physical assemblies, used only
  to detect facades (`ExportedType`-only, no `TypeDef`) and their forward target
  (e.g. `System.Runtime` → `System.Private.CoreLib`).
- `NETStandard.Library.Ref` (2.1) and `NETStandard.Library` (2.0) — the
  netstandard reference facades over `netstandard.dll`.

Everything is metadata-only via `System.Reflection.Metadata` (SRM); no assembly
is loaded.

## TSV schema

One row per `(tfm, assembly)`:

| column | meaning |
| --- | --- |
| `tfm` | `net6.0`…`net10.0`, `netstandard2.0`, `netstandard2.1` |
| `assembly` | simple assembly name (e.g. `System.Runtime`) |
| `file` | file name (e.g. `System.Runtime.dll`) |
| `kind` | `impl` \| `facade` \| `ref` |
| `forwardsTo` | for a facade, the implementation assembly its exported types resolve to |
| `version` | assembly version |
| `publicTypes` | logical top-level public type count (from the ref assembly) |

## Regenerate

Run after each SDK band ships a new patch (packs are cached under
`$TMPDIR/inspect-pack-cache`):

```bash
dotnet run genindex.cs -- ../../assets/platform-index.tsv
```

Downloaded pack `.nupkg`s are cached, so re-runs that only add a TFM are fast.
The result is deterministic and reviewable; commit the updated TSV.
