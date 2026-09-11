# Application dependency manifest format

## Status and adoption

This document owns host-neutral interpretation of SDK-generated application
`.deps.json` manifests. The immediate production host is ReturnToSender (RTS),
which directly consumes the format result while constructing its tools-owned
recompilation context.

The user approved this narrow tools-first adoption on September 11, 2026.
It is part of step 5 of the six-step RTS-primary and compile-back-retirement
tracker #6199, addressing the compilation-closure failures tracked by #6513.
It makes no CLI or Browser/Wasm adoption claim.

## Owner and claim

**Application Dependency Manifest Formats** owns:

> Given bounded immutable UTF-8 bytes for one SDK-generated application
> `.deps.json`, select its exact runtime target and associated compilation
> target and return their typed managed compile and runtime asset coordinates,
> correlated with top-level library kind and declared location metadata, or a
> typed rejection or incomplete outcome.

It owns:

- exact `runtimeTarget.name` selection;
- associated RID-less compilation-target selection;
- `compile` and `runtime` managed-asset roles;
- package, project, reference, runtime-pack, reference-assembly, and unknown
  library classification;
- logical asset, `localPath`, and library `path` coordinates;
- bounded deterministic results; and
- malformed, unsupported, invalid-coordinate, and work-limit outcomes.

It does not own:

- paths, application directories, package stores, installed platforms, or
  source authorization;
- projection from logical coordinates to physical files;
- package, project, platform, or Workspace realization;
- assembly decoding, binding, acquisition, or content lifetime;
- dependency traversal or package-relationship evidence;
- compiler references, Roslyn, recompilation, or fidelity policy; or
- presentation.

## Format basis

There is no published normative JSON Schema for `.deps.json`. The contract is
based on the SDK emitter and the managed dependency-model reader:

- `DependencyContextWriter` emits one portable target or, for RID-specific
  contexts, a RID-less compilation target plus a runtime target;
- `DependencyContextJsonReader` selects `runtimeTarget.name`, consumes the
  RID-less target as compilation libraries, and consumes the named target as
  runtime libraries; and
- `hostpolicy` supplies compatibility evidence for target and asset shapes.

These implementations are evidence, not transferred architecture. Tests use
the repository SDK's real generated test-host manifest and minimized
RID-specific forms of the writer's two-target shape.

## Input and result

The reader accepts exact already-acquired bytes. Hosts retain file discovery,
bounded acquisition, source identity, and diagnostics.

The root requires:

- a current object-valued `runtimeTarget` with a non-empty `name`, or the
  legacy non-empty string form;
- an object-valued `targets` containing the exact named runtime target; and
- an object-valued `libraries`.

For a runtime target containing `/`, the prefix before the first slash is the
associated compilation target and must also be present. Otherwise the runtime
and compilation target are the same object.

The result contains the union of library keys from those two targets. Compile
assets come only from the compilation target; runtime assets come only from
the runtime target. Targets unrelated to that pair contribute nothing.

Each selected library retains:

- its exact target key;
- its recognized top-level `type`, or `Unknown`;
- its optional validated top-level `path`; and
- ordered typed compile and runtime assets with optional validated
  `localPath`.

`_._` placeholders spend the asset budget but do not become managed assets.
Unknown properties and library types do not gain semantics.

## Coordinate boundary

Logical coordinates are relative `/`-separated values with no empty, `.`, or
`..` segment, backslash, control character, rooted path, or drive prefix. The
format result retains both the complete coordinate and its final file name.

Validation does not authorize filesystem access. A consumer may project:

- `localPath` beneath an admitted application directory;
- a library `path` and asset coordinate beneath an admitted package root; or
- a project asset's final file name beneath an admitted application directory.

Those choices and their fallback order belong to the physical-location owner,
not this format contract.

## RTS composition

RTS acquires the matching manifest bytes and invokes the reader directly. It
passes the immutable result to the existing assembly resolver for physical
projection, acquisition, provenance, and request-driven platform binding.
The resolver consumes the supplied result for both discovery inventory and
later selections; it does not reread or reinterpret the manifest.

RTS disables sibling discovery only after successful manifest interpretation.
Missing manifests retain sibling discovery. A rejected or incomplete matching
manifest fails closure construction rather than becoming a smaller successful
reference population.

The existing tools-owned frozen-reference and artifact-session contracts remain
unchanged. Only the final tools adapter creates Roslyn references.

## Evidence

`ApplicationDependencyManifestReaderTests` gates:

- admission of a real SDK-generated manifest;
- exact runtime and RID-less compilation target pairing;
- compile/runtime role preservation;
- project and package metadata;
- unrelated-target exclusion;
- malformed and duplicate-bearing JSON rejection;
- location-coordinate containment; and
- bounded failure without partial results.

`ReturnToSenderCompilationClosureTests` gates direct reader adoption, adjacent
SDK project-output projection, sibling suppression only after successful
interpretation, and visible malformed-manifest failure.
