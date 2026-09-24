# CLI member implementation profiles

## Status

This document is the normative owner for exact-family implementation-profile
adoption by the CLI `member` command, tracked by
[#7098](https://github.com/richlander/dotnet-inspect/issues/7098).

The Analysis and Query contracts remain owned by
[`library-body-analysis-service.md`](library-body-analysis-service.md) and
`AssemblyContextImplementationProfileFamilyQuery`. The Browser consumer remains
owned by
[`inspect-web-implementation-profiles.md`](inspect-web-implementation-profiles.md).
This document owns only the CLI selection, handoff, and compatibility boundary.

## Authority and exact claim

**CLI Member Implementation Profiles** owns:

> When an explicit CLI `Member Metrics` request selects one complete public
> method-overload family by exact name, acquire that family through
> `ImplementationProfileFamilyInspectionOperation` and lower its completed
> `InspectionEnvelope` to the existing `Member Metrics` rows.

The adoption changes acquisition, not presentation. The row schema, ordering,
relationship evidence, output formats, and explicit-only disclosure remain
unchanged.

## Eligible route

The exact-family route requires all of the following:

- the `member` command explicitly requests `Member Metrics`;
- exactly one member name is selected without a wildcard;
- no overload index, digest, body token, or generic-arity narrowing is active;
- `--all` is absent, so the CLI and operation share the public API population;
- the selected API type has exact metadata definition identity; and
- the selected name resolves to at least two public methods or extension
  methods with distinct stable selectors.

The CLI passes the metadata Type definition ID and the complete stable-selector
set to `ImplementationProfileFamilyInspectionOperation`. The operation remains
responsible for proving that the selectors identify one complete public method
family before Analysis runs.

## Compatibility boundary

The existing member Analysis path remains authoritative for:

- one method without overload siblings;
- an exact overload, digest, accessor, or physical body selection;
- constructors, operators, properties, events, and other non-method members;
- wildcard, generic-arity, non-public, or `--all` selections;
- `Type Metrics`, library `Member Metrics`, and `Library Metrics`; and
- requests that combine `Member Metrics` with another section whose existing
  acquisition requires the broader execution.

An eligible family envelope supplies only `Member Metrics`. Other requested
sections may independently open their existing Analysis execution. The CLI
must not use the broader compatibility index solely to repopulate rows already
provided by the family envelope.

## Envelope and failure behavior

The command retains the completed
`InspectionEnvelope<AssemblyContextEntry<AssemblyImplementationProfileFamilyInspection>>`
through rendering. It does not extract successful content at acquisition time.

- `Available` content is lowered to the existing `ImplementationProfileRow`
  shape.
- envelope diagnostics remain visible through the ordinary CLI diagnostic
  channel;
- `Rejected` and `Failed` participant outcomes fail the command visibly rather
  than falling back to success-shaped compatibility output; and
- incomplete Analysis or API-surface evidence remains represented by
  diagnostics, coverage, and incomplete profile rows.

The envelope's `Share` value remains intact even though implementation-profile
families do not yet have a canonical Workspace Share projection.

## Rendering

The CLI reuses the existing row lowering:

- physical profile and evidence-method identity;
- generated-body classification;
- exact overload-call relationships;
- structural metric columns;
- completeness and incomplete reasons; and
- stable member selectors and drill metadata.

The exact-family operation determines the analyzed population. Rendering must
not refilter it through a broader type or assembly result.

## Gates

The focused CLI gates prove:

- a named public overload family renders the same rows and relationships;
- unrelated methods remain absent;
- exact overload and accessor requests retain the compatibility behavior;
- type and library metric sections retain their existing broader populations;
  and
- an attached family envelope renders without opening the broader Type Analysis
  path solely for `Member Metrics`.
