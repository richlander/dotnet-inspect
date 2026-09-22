# Restored RID Workspace fixture

This real `System.Security.Cryptography.Pkcs` consumer retains four restored
RID cases: exact Windows-folder selection, two portable fallbacks, and the
Windows RID-fallback boundary. One project produces their `project.assets.json`;
`rid-cases.json` owns each case's SDK and current Workspace expectations.
The fixture is registered as `restored-project.rid-assets`.

The Release test
`CompleteRestorationExecutionTests.RestoredAssets_CreatesWorkspaceWithDocumentedRidSelection`
reads the product's restored-project facts for each exact target, takes the
resolved Pkcs Package occurrence, and supplies its coordinate to the ordinary
complete-restoration operation. It checks the realized member's provenance and
readable image, then consumes detached Package/API inventory after Workspace
close. Canonical packet, requested TFM/RID, compile role, and implementation
correspondence are preserved. The test's Package store is seeded from the real
restored nupkg and rejects network access.

This is a test of coordinate-based Workspace construction informed by restored
assets, **not a production whole-project assets importer**. SDK-selected paths
are the comparison oracle; they are not injected into the product to repair its
selection.

## Recorded result

With SDK `11.0.100-rc.1.26425.128` and Pkcs `10.0.5`, all four RID-specific
Release builds succeed. For `System.Security.Cryptography.Pkcs.dll`:

| RID | SDK runtime directory | Workspace runtime directory | Agreement |
| --- | --- | --- | --- |
| `win` | `runtimes/win/lib/net10.0` | `runtimes/win/lib/net10.0` | Yes |
| `win-x64` | `runtimes/win/lib/net10.0` | `lib/net10.0` | No |
| `linux-x64` | `lib/net10.0` | `lib/net10.0` | Yes |
| `osx-arm64` | `lib/net10.0` | `lib/net10.0` | Yes |

Every compile role uses `lib/net10.0`. The `win-x64` difference is intentional
evidence of the current package selector's exact-RID policy: it has no NuGet RID
fallback graph. Its green characterization case is not an SDK-parity claim.
Replaying only Package/version/TFM/RID coordinates therefore does not generally
reproduce the original project's selected runtime assets.

The normative selection boundary remains
[NuGet package structure and asset roles](../../../docs/nuget-package-structure.md#product-boundaries).
Issue [#7808](https://github.com/richlander/dotnet-inspect/issues/7808) tracks
this evidence, not an expansion of that boundary.

## Reproduce

From the repository root:

```bash
for rid in win win-x64 linux-x64 osx-arm64; do
  dotnet build \
    fixtures/queries/DotnetInspector.RestoredRidFixtures \
    -c Release -r "$rid"
done

dotnet run --project tests/DotnetInspector.Queries.Tests -c Release -- \
  --filter-method \
  'DotnetInspector.Queries.Tests.CompleteRestorationExecutionTests.RestoredAssets_*' \
  --report-xunit-xml --report-xunit-xml-filename restored-rid.xml \
  --results-directory artifacts/test-results
```

These are cross-builds and metadata inspection, not execution of foreign-RID
applications. Ordinary solution/test builds restore every declared RID and copy
the combined assets document, case expectations, and nupkg through the fixture
catalog. The four nested build commands are a reproducible SDK probe, not
subprocesses launched by the tests. The four outcome cases are PR-fast
(0.123-0.780 seconds each in the isolated initial run).
