# Platform manifest formats

## Status and approved scope

This document is the normative owner for host-neutral interpretation of .NET
shared-framework runtime configuration and dependency manifests. The first
production consumer is the explicit-hive implementation source in
`DotnetInspector.Platforms.Installed`; package-backed and Browser/Wasm sources
may supply the same bytes without adopting installed-location semantics.

This is the format-owner portion of PlatformHouse production-adoption step 4b
in
[PlatformHouse realization and reference processing](platform-house-reference-processing.md).
The end-to-end Workspace and call-graph adoption remains tracked by
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012).

## Authority and exact claim

**Platform Manifest Formats** owns:

> Given bounded immutable UTF-8 bytes for one shared-framework
> `runtimeconfig.json` or `deps.json`, either produce the exact typed framework
> references or managed target assets declared by that document, or return a
> typed rejection or incomplete outcome without filesystem or source-policy
> knowledge.

It owns:

- portable shared-framework name validation;
- runtime-configuration framework-reference and compatibility-setting
  interpretation;
- dependency-manifest runtime-target selection;
- logical managed-asset coordinates;
- malformed, duplicate-bearing, unsupported-shape, and work-limit outcomes;
  and
- deterministic immutable format results.

It does not own:

- paths, dotnet hives, package archives, HTTP, caches, or source authorization;
- whether a manifest is present or which source supplied its bytes;
- installed or package-backed framework inventories;
- framework-version selection, reference reconciliation, or graph traversal;
- projection from logical assets to source-specific member locations;
- assembly decoding, identity, immutable content leases, or platform proof;
- PlatformHouse settlement; or
- Workspace admission or presentation.

## Real-asset basis

The behavior is grounded in the .NET runtime repository at commit
[`aa036afce592ad80e938a35bd376222fb232cba9`](https://github.com/dotnet/runtime/tree/aa036afce592ad80e938a35bd376222fb232cba9):

- [`runtime_config.cpp`](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/src/native/corehost/hostfxr/runtime_config.cpp)
  establishes the `runtimeOptions`, `framework`, `frameworks`, and compatibility
  setting shapes;
- [`fx_reference.cpp`](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/src/native/corehost/hostfxr/fx_reference.cpp)
  and
  [`fx_resolver.cpp`](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/src/native/corehost/hostfxr/fx_resolver.cpp)
  establish the framework-reference and roll-forward behavior consumed by the
  installed composition owner; and
- [`deps_format.cpp`](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/src/native/corehost/hostpolicy/deps_format.cpp)
  establishes target-specific dependency-manifest asset interpretation.

The exact product claim is narrower than host activation: these readers recover
shared-framework references and managed target membership without performing
launch-time probing or loading inspected code.

The executable tests preserve minimized JSON manifestations of those real
shapes. They do not copy an installed shared-framework payload because its
version, RID, and location vary across CI hosts and the assemblies would
duplicate a large external distribution. The pinned runtime sources remain the
reproducible oracle; an installed layout can be observed with
`dotnet --list-runtimes` followed by inspection of the selected framework's
same-named `runtimeconfig.json` and `deps.json`.

## Boundary and dependency direction

The implementation lives in `DotnetInspector.Platforms.Formats`.

```text
DotnetInspector.Platforms
DotnetInspector.Core.HardenedJson
        |
        v
DotnetInspector.Platforms.Formats
        |
        v
source adapters, including DotnetInspector.Platforms.Installed
```

The dependency on `DotnetInspector.Core` is transitional and only consumes the
repository's centralized duplicate-rejecting JSON entry point. The planned
`UntrustedDocuments` extraction may replace that dependency without changing
this contract.

The API accepts bytes, never paths, streams, package coordinates, or installed
source identities. The project remains SRM-independent, Roslyn-free,
NativeAOT-compatible, and usable by single-threaded Browser/Wasm.

## Runtime-configuration contract

The reader accepts one complete UTF-8 `runtimeconfig.json` document. The root
must contain an object-valued `runtimeOptions`. The optional `framework` object
and `frameworks` array declare direct shared-framework references.

Each reference requires:

- one portable, case-sensitive framework name;
- one canonical exact `PlatformVersion`; and
- effective `rollForward` and `applyPatches` settings.

The supported roll-forward values are `Disable`, `LatestPatch`, `Minor`,
`LatestMinor`, `Major`, and `LatestMajor`, compared case-insensitively.
Document-wide defaults apply before per-reference values. The legacy
`rollForwardOnNoCandidateFx` values `0`, `1`, and `2` map to `LatestPatch`,
`Minor`, and `Major`. As in hostfxr, one runtime configuration cannot combine
`rollForward` with `applyPatches` or `rollForwardOnNoCandidateFx` across its
global and per-reference scopes.

A valid configuration with no references is a dependency-free leaf. Repeating
one framework name is rejected rather than resolved by document order.
Unknown properties do not become platform semantics.

## Dependency-manifest contract

The reader requires an object-valued `runtimeTarget` with a non-empty `name`,
an object-valued `targets`, and an exact target property matching that name.
Only that target contributes membership.

Within the selected target, every `runtime` asset is a managed-member
coordinate. For historical shared-framework manifests, a `native` asset also
contributes when its final segment is exactly
`System.Private.CoreLib.dll`. Other target sections and other native assets do
not become managed members.

A logical asset coordinate:

- is relative and uses `/` separators;
- contains no empty, `.` or `..` segment and no backslash or control
  character; and
- ends in one non-empty file name.

The result retains the complete logical coordinate and its final segment.
Projection of that final segment into an installed directory or package
payload belongs to the source adapter.

## Failure and work semantics

The closed parse outcome is:

- `Succeeded` with one immutable typed result;
- `Rejected` for malformed or duplicate-bearing JSON, invalid shape,
  framework name, version, setting, target, or asset coordinate; or
- `Incomplete` when the byte, framework-reference, library, or asset bound is
  reached.

Cancellation remains `OperationCanceledException`. No non-success outcome
returns a partial result.

Input bytes are bounded before JSON materialization. The reader counts every
framework reference, selected-target library, and `runtime` or `native` asset
it observes, including entries that do not become managed members.

## Production adoption

1. This owner introduces reusable byte-to-model interpretation.
2. The step 4b first adopter,
   `DotnetInspector.Platforms.Installed`, acquires manifests from one explicit
   hive and consumes these models while resolving the installed implementation
   closure.
3. Step 5 package-backed platform sources may consume the same readers from
   package content.
4. Browser/Wasm consumes the format owner only through non-installed sources;
   it never references installed-hive adapters.

## Evidence gates

`DotnetInspector.Platforms.Formats.Tests` proves:

- global and per-reference runtime settings;
- dependency-free runtime configurations;
- duplicate-property and duplicate-framework rejection;
- portable framework-name and canonical-version validation;
- exact runtime-target selection;
- runtime assets plus only the historical CoreLib native asset;
- logical asset containment; and
- byte and collection work bounds without partial success.

Installed-source tests separately prove source acquisition, graph resolution,
member projection, assembly identity, and immutable snapshots. The normal
solution build and dependency-policy gates enforce the project graph.

## Demo

The same bytes:

```json
{
  "runtimeOptions": {
    "rollForward": "LatestPatch",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "11.0.0"
    }
  }
}
```

produce the same typed framework reference whether they came from:

- `<dotnet-root>/shared/Microsoft.AspNetCore.App/...`;
- a package payload;
- an embedded catalog; or
- a Browser/Wasm download.

Only the supplying adapter knows or exposes that location. The format result
contains no path or source authority.
