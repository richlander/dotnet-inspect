# Project input inspection

Status: shared input boundary implemented under
[#10514](https://github.com/richlander/dotnet-inspect/issues/10514).
Production host adoption remains pending in
[#10478](https://github.com/richlander/dotnet-inspect/issues/10478).

## Owner and claim

The normative owner is
[Dependency Inspection](dependency-inspection-command.md#owner-and-claim).
This document records its focused supplied-byte input-composition adoption,
not a new dependency parser, Workspace owner, or navigation authority.

`ProjectInputInspection` owns this claim:

> One already-acquired authored project or restored assets document produces
> one completed inspection envelope preserving its dependency-evidence basis,
> provenance, available facts, limitations, and failures.

The result is `InspectionEnvelope<ProjectInputDocument>`. Content retains the
existing normalized dependency projection and the relevant provider result.
Local project content has an explicit nonprojectable Share outcome.
Provider outcome variants remain typed content; a host's future wire lowering
must preserve their discriminator and payload rather than serializing only
their abstract base types.

## Consumed contracts

- [Authored project dependency facts](authored-project-dependency-facts.md)
  owns XML admission, syntax interpretation, authored identity, declarations,
  constraints, conditions, and incompleteness.
- [Restored project dependency traversal](restored-project-dependency-traversal.md)
  owns the exact selected root-relative graph and completion. Its facts owner
  owns assets admission, schema support, target selection, resolved
  coordinates, pruning observations, and content provenance.
- [Package dependency evidence](package-dependency-evidence.md) owns normalized
  input kinds, declaration basis, phase evidence, and typed failures.
- [Dependency inspection](dependency-inspection-command.md) owns the existing
  shared dependency document and evidence projection.
- [Inspection envelope](inspection-envelope.md) owns Content, Share, and
  diagnostics. Hosts preserve all three.

These contracts are consumed unchanged. A `.csproj` input here is the supplied
XML document, not the CLI's existing project-path locator for restored assets.
No authored constraint becomes a resolved coordinate.

## Real input and evidence

The motivating authored shape is
`src/DotnetInspect.Cli/DotnetInspect.Cli.csproj` at repository commit
`bebfab236a6a3061bc74c53d22067acb86050afc`: its target framework comes from
`$(DefaultTargetFramework)`, and centrally versioned Package references do not
carry literal versions. Uploading that file alone must preserve those
declarations without pretending that the dependency set was evaluated or
resolved. `CentralVersionsRemainVisibleAsIncompleteDeclarations` retains that
shape as a small deterministic source fixture.

The restored input uses the existing facts and traversal owners' real
restored-project evidence, together with a focused Newtonsoft.Json `13.0.3`
coordinate control in `RestoredProjectPreservesExactVersionsAndTarget`.
This slice does not claim to improve provider admission or traversal.

`ProjectInputInspectionTests` gates the completed boundary: authored
constraints without resolution, centrally versioned declarations, restored
exact versions and target, document rejection, and provider byte limits.
The gates also cover explicit runtime-target selection, an unavailable
requested target, partial traversal, and graph failure without discarding
independently available declarations. Browser wire and interactive adoption
are deferred and unverified.

## Production adoption

The operator approved focused successors rather than a multi-owner design.
The counted delivery map is #10478, with five independently owned slices:

| Slice | Issue | Owner and immediate boundary |
| --- | --- | --- |
| Shared supplied-byte inspection | #10514 | Dependency Inspection composes existing facts and evidence. |
| Project occurrence and Workspace construction | #10513 | Workspace Scope establishes membership and its identity handoff. |
| Project subject navigation | #10516 | Navigation consumes the Workspace-owned handoff. |
| Production Inspect Web adoption | #10517 | Browser consumes the settled shared, Scope, and Navigation contracts. |
| Explicit CLI document adoption | #10515 | CLI consumes the same shared envelope without changing restored-project locators. |

Inspect Web is the initial production consumer; its local Project-open
experience requires slices 1-4. The CLI adoption depends on slice 1 and may
proceed independently of slices 2-4. Hosts own acquisition and presentation,
not parsing, target selection, normalization, or traversal.

Existing CLI project locators retain their restored-assets behavior. A new
explicit authored-document gesture must not change that convention silently.
This document does not specify the other owners' internal changes or claim
that their adoption is implemented. No current host path is replaced here.

## Non-claims

No MSBuild evaluation, restore, build, neighboring-file discovery, filesystem
paths, package caches, network access, inferred package sources, dependency
acquisition, application call-usage inference, or quality/trust ranking.
An authored project with a complete supported-syntax projection still does
not establish the complete evaluated dependency set.
