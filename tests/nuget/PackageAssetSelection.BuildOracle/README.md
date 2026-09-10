# Compatible empty compile-group build oracle

This retained build fixture records the NuGet/MSBuild behavior for the package
layout that motivated the compatible-selection rule:

```text
ref/net8.0/_._
lib/net6.0/Example.dll
```

The package and consumer are fixed at version `1.0.0` and target frameworks
`net6.0`, `net8.0`, and `net9.0`. Run the probe with the repository development
SDK from this directory. The recorded result below was produced with SDK
`11.0.100-rc.1.26425.128`.

```bash
dotnet pack Package/EmptyCompileGroup.Oracle.csproj -c Release \
  -p:IsPackable=true
dotnet build \
  Consumer/PackageAssetSelection.BuildOracle.Consumer.csproj \
  -c Release -f net6.0
dotnet build \
  Consumer/PackageAssetSelection.BuildOracle.Consumer.csproj \
  -c Release -f net8.0
dotnet build \
  Consumer/PackageAssetSelection.BuildOracle.Consumer.csproj \
  -c Release -f net9.0
```

The `net6.0` build succeeds because its compile asset is
`lib/net6.0/Example.dll`. The `net8.0` and `net9.0` builds fail with `CS0246`
for the `Example` namespace because NuGet selects `ref/net8.0/_._` as their
compile group. In both cases, `lib/net6.0/Example.dll` remains the runtime
asset. The selected paths can be inspected directly:

```bash
jq '.targets["net9.0"]["EmptyCompileGroup.Oracle/1.0.0"]
  | {compile, runtime}' \
  ../../../artifacts/obj/PackageAssetSelection.BuildOracle.Consumer/project.assets.json
```

Expected `net9.0` asset shape:

```json
{
  "compile": {
    "ref/net8.0/_._": {}
  },
  "runtime": {
    "lib/net6.0/Example.dll": {}
  }
}
```

This nested pack-and-build probe is preserved as SDK-dependent design evidence,
not run in normal CI. The product contract is enforced in Release by
`CompatibleImplementation_UsesRequestedFrameworkForEmptyGroupReduction`,
`PackageRootBinding_CompatibleEmptyGroupSuppressesCompileFallback`, and
`QueryPackage_CompatibleEmptyCompileGroupSuppressesLibraryFallback`.
