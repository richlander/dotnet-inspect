# Research assembly-context association

## Owner and claim

This document owns the request-local `ResearchAssemblyContext` association in
`src/ILInspector.Research/ResearchFactRegistry.cs`.

> Research may derive lazy assembly-wide finding joins only from the exact
> focused Analysis results carried by one `MemberProjectionAnalysisInput`.

`MemberProjectionAnalysisInput` requires its allocation, safety, call-graph,
and leverage results to carry the same execution receipt. That receipt
association is the complete join currency. Research does not infer continuity
from an assembly path, module identity, content shape, or equal result values.

## Retention boundary

`ResearchAssemblyContext` retains one `MemberProjectionAnalysisInput` and lazily
derives only the finding joins required by its request: method signals,
leverage by token, physical call sites by evidence method, and unsafe evidence
by member token.

The context has no static cache, path lookup, reacquisition behavior, or
cross-request continuity. Releasing the request releases the context and its
focused inputs. `AnalysisIndexCache` separately owns path-backed execution
reuse and does not become a context dependency.

## Boundary case and evidence

`ContentShapedMemberProjectionTests` rejects focused results from different
execution receipts. `AssemblyContextResearchProjectionQueryTests` gates the
callee finding-evidence projections that consume the request-local context,
including instruction coordinates and typed safety evidence.

## Non-goals

- No migration of query-owned call relationships, invocation destinations, or
  local-throw paths that still use the compatibility index.
- No change to Analysis execution or `AnalysisIndexCache`.
- No cross-Workspace Research cache.
- No redesign of individual Research fact producers.
