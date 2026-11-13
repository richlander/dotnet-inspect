# API surface presentation

## Status and ownership

This document defines the `DotnetInspector.Presentation`-owned detached
presentation of one Metadata-owned API Type or Member. The first production
adopter is Inspect Web under the website thinning tracker
[#8779](https://github.com/richlander/dotnet-inspect/issues/8779).

The normative claim is that reusable declaration spelling, facets, effective
accessibility, durable Member identity, and call-graph selectors cross a host
boundary as owner-issued presentation values. A host may lower those values
into its transport but does not reconstruct them from `ApiType` or `ApiMember`.

The [inspection layers](inspection-layers.md#seam-rules) supply the governing
rule that a second implementation of a shared rule is a defect.
[API population scope](api-population-scope.md#spelling-within-api-visibility-scope)
owns declaration composition and effective accessibility. Metadata and
Analysis remain the owners of Member anchors and executable-body selectors.

## Contract

`ApiSurfacePresentation.Type` accepts one already-admitted `ApiType` and
returns detached presentation facts:

- exact definition, query, and metadata identities;
- C# display name, accessibility, kind text, and complete signature; and
- stable type-kind and type-trait facet identifiers.

`ApiSurfacePresentation.Member` accepts that Type and one of its already
admitted Members and returns detached presentation facts:

- declaration spelling, flags, generic arity, return and parameter fields;
- XML documentation identity when Metadata can issue one;
- the Metadata-owned stable Member anchor; and
- the Analysis-owned graph selector and exact executable-body selectors.

The projection is deterministic, synchronous, resource-free, and performs no
artifact acquisition or metadata traversal. Its records retain no workspace,
image, reader, stream, or lease authority.

## Host boundary

Inspect Web consumes the detached facts and retains:

- package, platform, assembly, and participant attribution;
- duplicate-Type ID qualification;
- Browser text budgets and whole-participant truncation;
- Browser ordering, totals, notices, and visible failures; and
- mapping into facade-owned wire records.

The presentation owner does not choose Browser limits, transport field names,
navigation behavior, or TypeScript serialization. It does not filter,
reorder, or count the admitted declaration population.

## Motivating asset and evidence

The motivating production asset is
`Microsoft.NETCore.App.Runtime.linux-x64@10.0.10`,
`System.Private.CoreLib.dll`. Its `System.Type` surface includes private
extension declarations whose receiver identity, declaring-Type identity,
effective accessibility, Member anchor, and executable selectors must remain
distinct. Existing Inspect Web real-asset coverage exercises that path.

Focused `DotnetInspector.Presentation.Tests` gate exact Type identity, C#
display facts, Member parameters, durable anchors, and body selectors.
Inspect Web boundary tests gate unchanged wire behavior and Browser allocation
bounds. Dependency policy prevents Web Core from reacquiring direct
`CSharpText` or `ILInspector.Research` dependencies after adopting this
presentation boundary.

## Non-claims

This contract does not:

- define API population admission, spelling modes, or contextual populations;
- acquire, inspect, compare, or rank assemblies;
- add documentation text, source, decompilation, or call-graph traversal;
- define a Markout document or renderer; or
- migrate CLI API rendering, which remains independently owned work.
