# Package Query stream prototype

This standalone prototype measures a fast package-prefix experience without
using dotnet-inspect product code. It requests a bounded NuGet Search page,
incrementally reads the response, and writes each literal prefix match as soon
as its package ID and latest version are available.

The output is headerless TSV with one `ID<TAB>VERSION` row. Each row is flushed
immediately, and the process exits as soon as the requested `-n` rows have been
written. This makes ordinary shell timing measure time to the complete visible
window:

```bash
dotnet build prototypes/package-query-stream/PackageQuery.StreamingPrototype.csproj \
  -c Release

time dotnet run \
  --project prototypes/package-query-stream/PackageQuery.StreamingPrototype.csproj \
  -c Release --no-build -- 'System.*' -n 20
```

The terminal `*` is optional:

```bash
time dotnet run \
  --project prototypes/package-query-stream/PackageQuery.StreamingPrototype.csproj \
  -c Release --no-build -- 'AWSSDK.' -n 20
```

The prototype intentionally has no cache, retries, facets, manifest
acquisition, or product dependencies. It asks NuGet for at most the remaining
requested row count, capped at 100 raw rows per page, applies literal
case-insensitive prefix filtering, and skips all unneeded response properties
without constructing their object graphs. If the source cannot produce `-n`
matches within its bounded pagination range, the tool reports the observed
count and exits nonzero.
