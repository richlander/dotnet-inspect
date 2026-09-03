# Hosted package fixtures

These packages provide immutable, purpose-built NuGet inputs for tests that
must exercise the remote package protocol. GitHub Packages hosts the published
artifacts at:

```text
https://nuget.pkg.github.com/richlander/index.json
```

The feed requires authentication even for public packages. CI is the primary
consumer and uses its repository `GITHUB_TOKEN`. Local feed-backed tests remain
opt-in and require a classic personal access token with `read:packages`; the
ordinary local suite does not require package-feed credentials.

## Tool v2 fixture

Version `1.0.0` deliberately models three facts:

- `DotnetInspect.TestAssets.ToolV2` is a TFM-agnostic Tool v2 pointer package
  with the SDK's name-only command shape.
- `DotnetInspect.TestAssets.ToolV2.linux-x64` exists at the same version.
- `DotnetInspect.TestAssets.ToolV2.win-x64` is referenced but intentionally
  absent at that version.

This allows one pinned package inspection to prove both positive and negative
RID-package availability without borrowing mutable ecosystem packages.

Version directories are append-only. Never change the package inputs beneath a
published version. Add a new version directory, update that family's version
property in `PackageFixtures.proj`, validate it, and publish that version
instead. Package publication is main-only and does not use `--skip-duplicate`,
so GitHub Packages' immutable version boundary remains visible as a failed
workflow rather than a success-shaped no-op.

`PackageFixtures.proj` is an isolated packaging driver, not a product or
solution project. Its `IsPackable` opt-in can only emit these versioned test
inputs; solution-level pack and publish commands still include only the
shipping tools.

Pack the two artifacts locally with:

```bash
dotnet pack eng/package-fixtures/PackageFixtures.proj -c Release \
  -p:FixtureFamily=tool-v2 \
  -p:FixturePackage=linux-x64
dotnet pack eng/package-fixtures/PackageFixtures.proj -c Release \
  -p:FixtureFamily=tool-v2 \
  -p:FixturePackage=pointer
```

`PackageFixtureTests.PackageFixtureCatalog_PacksDeclaredToolV2Packages` is the
structural gate for package IDs, versions, package types, paths, pointer
mappings, the deliberately missing sibling, and packing without an ambient
global package cache.

## Metadata-confusion fixture

`DotnetInspect.TestAssets.MetadataConfusion` version `1.0.0` contains a
deterministically generated ECMA-335 assembly with 18 valid adversarial
metadata specimens:

- identity-bearing assembly, module, namespace, type, member, parameter, and
  generic-parameter names;
- custom-attribute values, including a display-only bidirectional URL whose
  logical host uses reversed `dotnet`;
- module references and P/Invoke import names;
- resource names and method-body user strings;
- close controls for a literal `Outer+Inner` name, genuine nesting, and a
  generic arity/name mismatch.

The specimens exercise bidirectional and invisible formatting, line and
terminal controls, path-like parent segments, homoglyphs, and C# display
grammar. The package is inspection input only: do not load or execute its
assembly, and do not fetch any embedded display-only URL.

The generator builds the PE twice and requires byte equality. Its package
verifier then compares the packed assembly and canonical UTF-8 JSON manifest
byte-for-byte with fresh output and re-reads the metadata through SRM. Pack and
verify it locally with:

```bash
dotnet pack eng/package-fixtures/PackageFixtures.proj -c Release \
  -p:FixtureFamily=metadata-confusion \
  -p:FixturePackage=metadata-confusion
package=artifacts/package-fixtures/Release/\
DotnetInspect.TestAssets.MetadataConfusion.1.0.0.nupkg
dotnet run \
  --project eng/package-fixtures/metadata-confusion/1.0.0/generator \
  -c Release -- \
  verify-package "$package"
```

The generator belongs to the immutable version directory alongside the nuspec
and README. A new metadata-confusion version copies that directory, updates its
generator version, and then advances `MetadataConfusionFixtureVersion`; it does
not alter the generator for an already published version.

`PackageFixtureTests.PackageFixtureCatalog_PacksMetadataConfusionPackage` is
the structural gate for the package identity and paths, exact deterministic
verification, canonical LF-only manifest bytes, manifest shape, specimen
count, and representative raw metadata values.

## Consumption

CI runs the hosted manifest test in a dedicated step with `packages: read` and
passes its repository `GITHUB_TOKEN` only to that step. The test supplies the
GitHub Packages endpoint explicitly, writes a temporary source configuration,
and starts dotnet-inspect with an isolated cache. Its positive `linux-x64` row
proves authenticated fixture access; its negative `win-x64` row proves the
deliberately absent sibling remains visible.

Ordinary local runs skip the hosted test. To opt in, provide a classic personal
access token with `read:packages` without placing it on the command line:

```bash
export DOTNET_INSPECT_PACKAGE_FIXTURE_USER="$(gh api user --jq .login)"
read -rsp "GitHub Packages PAT: " DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN
export DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN
dotnet run --project src/dotnet-inspect.Tests -c Release -- \
  --filter-method '*Package_Manifest_RendersToolManifestRows*'
unset DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN
```

## Publication

Run the **Publish package fixtures** workflow from `main`, select one fixture
family, and type `publish` in its confirmation input. The workflow validates
the whole catalog but packs and publishes only the selected family, so adding a
new family never attempts to replace an existing immutable package. For Tool
v2, it publishes the RID package first and publishes the pointer only after its
required sibling succeeds. If publication partially fails, publish a new
fixture version rather than replacing a package version that already exists.
