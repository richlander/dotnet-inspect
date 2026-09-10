# Platform library catalog generator

This SDK-side tool produces `assets/platform-index.json`: an exact-version
catalog that lets Inspect Web search and browse Platform before downloading
runtime packs. It reads metadata with SRM; it never loads inspected assemblies.

The browser defaults to .NET 11. Generation discovers the newest common
NuGet version for each supported release line, including previews and RCs,
across these four packages:

- `Microsoft.NETCore.App.Ref`
- `Microsoft.NETCore.App.Runtime.linux-x64`
- `Microsoft.AspNetCore.App.Ref`
- `Microsoft.AspNetCore.App.Runtime.linux-x64`

NuGet semantic version ordering and intersection keep reference membership and
runtime metadata on the same exact build. An unavailable version or invalid
managed inventory fails generation rather than producing a partial catalog.
Historical net6.0-net10.0 and reference-only netstandard catalogs remain
available; they are not substituted for the default target.

## Catalog format

The root has `schemaVersion: 1`, `defaultFramework: "net11.0"`, and `targets`.
Each target has `tfm`, exact package `version`, and `rows`. Each row carries:

| Field | Meaning |
| --- | --- |
| `tfm`, `pack`, `packVersion` | Exact target and supplying framework family |
| `assembly`, `file`, `version` | Physical assembly name, file, and assembly version |
| `kind` | `facade`, `impl`, or reference-only `ref` |
| `forwardsTo` | Dominant direct forwarding destination for a facade, otherwise null |
| `publicTypes` | Metadata-owned meaningful public type count, preferring reference metadata |
| `inReferencePack` | Membership in the reference-pack library inventory |
| `hasImplementation` | A corresponding runtime assembly is available |

The tool reuses `AssemblySurfaceClassifier`, `AssemblyDetailScanner`, and
`PackageAssetSelector.SelectPlatformPack`; it does not maintain a second
classification or runtime-layout policy. Facade detection uses the physical
assembly's forwarding-only meaningful public surface, excluding
compiler-generated names, not reference membership or `.Private.` naming.
The three visual roles are
facades, implementations represented in the reference pack, and private
implementations outside it. The last two both have `kind: "impl"`.
The dominant forwarding destination is a browsing hint, not authority for
type resolution.

## Regenerate

From the repository root, with the repository-selected SDK:

```bash
dotnet run inspect-web/tools/gen-platform-index/genindex.cs \
  -c Release -- inspect-web/assets/platform-index.json
```

Downloads are cached under the OS temporary directory's `inspect-pack-cache`
directory. Commit the generated JSON together with related producer changes.
The catalog does not update during an ordinary build or require network access
in PR CI. Runtime version discovery can offer newer builds, but their inventory
must be acquired for that exact version before changing a selected target.

The inspect-web CI job compiles the generator. `test/platform-index.test.ts`
exercises catalog loading, exact-version association, reference membership,
role examples, and the shipped .NET 11 target. Engine catalog tests cover
dynamic discovery and acquisition; browser tests cover Platform presentation
and navigation. The owning experience is issue #6013 and
[Platform subject](../../../docs/design/inspect-web-navigation-presentation.md#platform-subject).
