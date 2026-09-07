# Custom-attribute package corpus

This is evidence for
[D3](../../../docs/design/custom-attribute-value-decoding.md#pinned-package-fidelity-gate),
not another normative owner or the decompiler's method baseline.
`custom-attribute-d3.json` selects eight managed assemblies from the published
`dotnet-inspect.any` 0.14.0 package and records the independent enum-width sources.
The bundled Markout image is retained only as a defining dependency.

## Producer association

The portable job in
[run 28294556178](https://github.com/richlander/dotnet-inspect/actions/runs/28294556178/job/83832135926)
checked out source commit
`8681f6eac3ff44b231925913c3e2b17c8be0ddd4`.
The release tag `v0.14.0` points elsewhere and is not used as the build identity.
The workflow at the actual build source is
[`release.yml`](https://github.com/richlander/dotnet-inspect/blob/8681f6eac3ff44b231925913c3e2b17c8be0ddd4/.github/workflows/release.yml).
It packages the checked-out source, uploads `package-any`, and publishes those
artifacts to NuGet and the GitHub release.

Relevant build-log excerpts, recorded on 2026-09-06:

```text
2026-06-27T16:10:12.4679910Z HEAD is now at 8681f6e Release 0.14.0: version bump and CLI usability fixes (#1690) (#1697)
2026-06-27T16:10:16.5699415Z dotnet-install: Installed version is 11.0.100-preview.5.26302.115
2026-06-27T16:11:00.8414980Z Run dotnet pack src/dotnet-inspect -c Release -r any -p:PublishAot=false -p:OfficialBuild=true
2026-06-27T16:11:03.0968435Z /usr/share/dotnet/sdk/11.0.100-preview.5.26302.115/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.RuntimeIdentifierInference.targets(383,5): message NETSDK1057: You are using a preview version of .NET.
2026-06-27T16:11:42.2984749Z Successfully created package '/home/runner/work/dotnet-inspect/dotnet-inspect/src/../artifacts/package/release/dotnet-inspect.any.0.14.0.nupkg'.
2026-06-27T16:11:44.0094600Z SHA256 digest of uploaded artifact is be5a1f6fb2ba338c8e1e56c4b64c0582756380c114bc79c856d6765ae45c83ec
2026-06-27T16:11:44.3553120Z Artifact package-any successfully finalized. Artifact ID 7926053949
```

The SDK targets path is evidence of the SDK used by the pack command, not
merely of an installed SDK. The original artifact's API record associates it
with the same run and source commit; it reported an expiration of
`2026-09-25T16:09:59Z`. Its downloaded archive matched the logged SHA-256.
The contained unsigned nupkg matched the
[GitHub release asset](https://github.com/richlander/dotnet-inspect/releases/tag/v0.14.0)
digest recorded in the JSON.

The NuGet archive has a different package digest and an additional
`.signature.p7s` entry. Every selected managed DLL matched byte-for-byte between
the original build artifact and NuGet; those per-image hashes are in the JSON.
The gate downloads through `NuGetClient` and verifies both the NuGet package
hash and each selected image hash before inspection.
This is recorded CI provenance, not a signed toolchain attestation.

## Independent enum evidence

The package corpus uses twelve source-declared `Int32` enum widths. Each entry
names the definition separately from its serialized input spellings and links
the exact source revision. These declarations are inputs to the test-owned SRM
provider; the provider never asks the product for an enum width.

- The two decompiler enums use the package's build-source commit.
- Markout's 0.14.0 nuspec names commit
  `2d0f2c4bbdb2539b2ddd3d79e966da117ab93773`.
- Framework declarations use dotnet/runtime's `v10.0.0` source commit
  `60629d14374c56f1cb51819049ad1fa529307f8d`. Their defining images come from the
  running framework, are separately identified in each report, and must expose
  the declared widths. This is not a reconstruction of the original runtime.

The product resolver is built separately over retained images. Every planned
definition must resolve and expose the source-declared underlying type before
the corpus walk. Unknown/defaulted widths fail this corpus; there is no
missing-image exception hidden in its success count.

Because this package has only `Int32` enum cases, each corpus invocation also
runs the existing four source-owned retained-image `long`/`byte` cases.
`D3FixtureProducerSdk` records their actual compilation SDK in the test assembly.
The report distinguishes that SDK from the historical package SDK. The
companions do not retroactively become output from the package's compiler.

## Execution

From the repository root, after SDK preflight:

```sh
DOTNET_INSPECT_D3_REPORT=artifacts/custom-attribute-d3/result.json \
dotnet run --project tests/ILInspector.Metadata.Tests -c Release -- \
  --filter-class ILInspector.Metadata.Tests.CustomAttributeCorpusTests \
  ILInspector.Metadata.Tests.CustomAttributeFidelityTests --no-progress
```

Use `--filter-not-trait "Speed=Slow"` to run the small controls without the
networked package sweep. Normal PR CI and the Windows PR workflow use that
selection. Deep Inspect's full metadata runs keep the sweep and upload its
JSON result separately for Windows, macOS, and Linux.

The report binds observations to the package/assembly hashes, decoder and
harness builds, actual oracle SRM/runtime, and retained defining images.
The input's `net10.0` directory does not pin the oracle.
The pass condition is complete equality for every selected attribute row,
with no refusals, oracle failures, differences, or defaulted widths.
Diagnostic examples are limited to the first twenty failures per image;
failure totals and row accounting are not capped.

This is a finite package/fixture evidence set, not all output from an SDK,
an SDK interval, all bundled dependencies, all NuGet packages, or exhaustive
grammar coverage. Expanding it requires another explicit input/provenance
entry and an independent oracle for any newly encountered enum types.
