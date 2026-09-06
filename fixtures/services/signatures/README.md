# Signed-package fixtures

`SignatureVerifierTests` consumes these unchanged NuGet archives to exercise
package-content binding, author identity, and repository countersignatures
without downloading test inputs. This fixes the network-dependent required gate
tracked in [#5271](https://github.com/richlander/dotnet-inspect/issues/5271).

## Provenance

Both archives were downloaded from their immutable nuget.org version URLs on
2026-09-05. They are the same package versions previously downloaded by the
tests, not new dependency references or assemblies to execute.

| Archive | Source | License retained inside archive |
| --- | --- | --- |
| `newtonsoft.json.13.0.3.nupkg` | [NuGet flat container](https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg) | MIT, `LICENSE.md` |
| `system.text.json.9.0.4.nupkg` | [NuGet flat container](https://api.nuget.org/v3-flatcontainer/system.text.json/9.0.4/system.text.json.9.0.4.nupkg) | MIT, `LICENSE.TXT`; additional `THIRD-PARTY-NOTICES.TXT` |

SHA-256:

```text
872fc189e638ab1056555b03aaa38f68bcb54286e221aa646eb1129babf63c77  newtonsoft.json.13.0.3.nupkg
a083aa7ce2085175d591f1624c223dc302090444d0a85ed970e26fda262eab5b  system.text.json.9.0.4.nupkg
```

Keep the complete archives, including their licenses, certificate chains,
timestamps, and `.signature.p7s` entries. Repacking or trimming them changes the
signed package hash and invalidates the evidence.

## Consumption and scope

The Services test project copies these static assets to
`Fixtures/Signatures/` in its output directory using ordinary MSBuild content
items. Tests resolve them from `AppContext.BaseDirectory`; a missing fixture is
a failure, never a download or skip. Tests that modify a package use a temporary
copy, leaving the committed archive unchanged.

This is a narrow exception to the built-assembly `FixtureCatalog` convention:
the archives are pre-existing signed inputs, not projects built by this
repository. Creating a placeholder assembly project or expanding the catalog
for two static files would add no signature-verification evidence.

The positive tests still call the production `SignatureVerifier.Verify` and
require both author and repository verification. A modified copy must fail
package-content verification; unsigned, malformed-signature, and missing-file
cases remain covered. The verifier's existing pinned-root, offline certificate
policy is unchanged.

Other network integration suites, including `NuGetFetch.Tests`, are outside this
fix. These fixtures remove the Services signature tests' acquisition dependency;
they do not make a repository-wide claim about network use.

## Running the focused cases

```sh
dotnet run --project src/DotnetInspector.Services.Tests -c Release -- \
  -class '*SignatureVerifierTests'
```

After building, `--no-build --no-restore` permits running the same cases when
package transport is unavailable.

## Updating fixtures

Replace a fixture only as an explicit test-input change. Download the chosen
version from its nuget.org flat-container URL, preserve the archive verbatim,
and update its filename, ignore exception, test references, provenance, and
SHA-256 together. Retain the original licenses and notices and re-run the
focused cases with package transport unavailable. Do not add a test-time
download fallback or regenerate the signatures.
