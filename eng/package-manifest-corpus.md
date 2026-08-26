# Package-manifest compatibility corpus

This corpus pins exact NuGet.org manifest bytes for representative real
packages without checking third-party content into the repository. The catalog
at `eng/package-manifest-corpus.json` contains only package coordinates,
SHA-256 hashes, and the compatibility shapes each entry covers.

| Package | Version | SHA-256 |
| --- | --- | --- |
| `Newtonsoft.Json` | `3.5.8` | `7990b971ca0f217da4c82a9a4606e5cbf08746857ad7ee7541559c80750fdfdb` |
| `dotnet-ef` | `9.0.0` | `7a6d4b662a24af6192ac0262c433f7a11d95f9a79a705888554ec242799160a3` |
| `Spectre.Console` | `0.49.1` | `12a7877ded4a2d3d96db03432d65afeeb8b8b7936894a722ef6b2a507a679379` |
| `Microsoft.SourceLink.GitHub` | `8.0.0` | `b1081e636501a0cdcf7b6e93ff97783d263d30b7b745f12ef4a266d3aa402cd6` |

Together these manifests cover a namespace-free root, a root schema
namespace, legacy metadata namespace placement, grouped and ungrouped
dependencies, package types, repository, license, and readme metadata, and an
older publication shape.

## Verification

Normal tests are offline. They embed the catalog, validate its schema,
coordinates, hashes, and complete declared coverage, and exercise hash and
oracle disagreement failures without downloading any package content:

```bash
dotnet run --project src/DotnetInspector.Queries.Tests -c Release -- \
  -method '*PackageManifestCorpusTests*'
```

Live verification is explicit:

```bash
dotnet run eng/verify-package-manifest-corpus.cs
```

The single-file C# app downloads each exact flat-container `.nuspec`, enforces
the product manifest byte limit, verifies its SHA-256 hash, runs
`PackageManifestFactsQuery`, and compares the result with the test-only
NuGet.Packaging oracle. The pinned baseline is four passing entries with all
declared compatibility shapes observed.

`PackageManifestCorpusTests.Catalog_PinsExpectedCoordinatesAndCoversEveryShape`
gates the coordinate set and required shape coverage.
`PackageManifestCorpusTests.Verifier_RejectsHashMismatchBeforeProjection` and
`PackageManifestCorpusTests.Comparer_ReportsOracleDisagreementWithoutValues`
gate visible, content-free failures. The live command is the acquisition and
real-byte compatibility gate; record its output when changing the catalog.

## Updating the corpus

Treat a hash change as a reviewable compatibility event, not routine lock-file
churn. First verify that the pinned coordinate still returns the intended
manifest and that both the product and independent oracle accept it. Then run:

```bash
dotnet run eng/verify-package-manifest-corpus.cs -- --refresh
git diff -- eng/package-manifest-corpus.json
dotnet run eng/verify-package-manifest-corpus.cs
```

`--refresh` writes new hashes only after product/oracle agreement and declared
coverage checks pass. Review every changed hash and update this table and the
recorded baseline. Prefer adding a coordinate for a newly required shape over
replacing an entry that supplies distinct historical coverage.

Do not vendor downloaded manifests or packages. Purpose-built first-party
hosted fixtures have a different immutability and publication contract owned by
[`eng/package-fixtures/README.md`](package-fixtures/README.md); they are not
members of this third-party coordinate-and-hash corpus.
