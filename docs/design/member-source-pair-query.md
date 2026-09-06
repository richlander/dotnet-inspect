# Selected member source pair query

## Owner and claim

`DotnetInspector.Queries` owns this explicit two-endpoint operation.

> Resolve the requested metadata member independently in two retained images,
> acquire each endpoint's PDB source without decompilation, and compare only
> complete verified declarations while retaining both endpoint associations
> and non-success outcomes.

This implements the bounded composition permitted by
[Research authored-source comparison](implementation-diff.md#research-authored-source-comparison).
Research supplies comparison policy, existing source services supply verified
member acquisition, and Metadata supplies exact member identity. This query
does not redefine those owners or establish Research correspondence, subject
absence, admission, or producer completion.

## Request and result

The request names one exact metadata type and member anchor, two participants
with their owning context groups, and explicit source capabilities. Each
endpoint resolves that anchor in its own retained image. A physical token from
one image is never reused to resolve the other. A missing or ambiguous
MethodDef is `NotFound`, not positive member absence; unsupported non-method
targets also have no source comparison.

Each endpoint retains its acquisition registration, assembly identity, and
provenance. A resolved endpoint also retains its actual exact member request
and typed PDB source attempt. Unresolved, rejected, and failed inspection
outcomes remain explicit. PDB unavailability or acquisition failure does not
suppress the other endpoint's attempt.

The pair is `Compared` only when both endpoints have complete verified member
source. It retains the native `FindingComparison<string>` and both endpoint
records, including an exact comparison with no edit rows. Otherwise it is
`Unavailable`, preserving endpoint acquisition reasons, or `Failed` when
query validation or comparison itself failed. An unavailable comparison may
contain a failed acquisition attempt; that attempt is not rewritten as missing
source or success.

No empty text, decompiled fallback, or generic one-sided line addition/removal
stands in for unavailable source. Exactness concerns the supplied declaration
text under the existing text-line semantics, not source authenticity, member
correspondence, C# equivalence, or IL equivalence.

## Execution boundary

The operation reuses exact-member lookup and PDB acquisition from
`AssemblyContextSourceQuery`. It does not invoke the ordinary query's
decompiled fallback or the same-member PDB-versus-decompiled comparison.
Ordinary source-query defaults remain unchanged.

An optional retained assembly path is not implicit permission to probe the
filesystem. `AllowAdjacentPdbReads` explicitly permits a matching PDB beside
that path; it defaults to false independently of `AllowLocalSourceReads`.
The CLI enables both. Embedded PDBs retain precedence, sidecars use Metadata's
existing identity-checked stream loader, and supplied acquisition byte limits
also bound a sidecar before loading. A missing sidecar permits the existing
symbol-acquisition path; read failures remain failed acquisition evidence.
Pathless participants do not gain a filesystem probe from this capability.

Each input's binding-policy version is captured independently. Existing
retained-image, cancellation, and source-disposal rules apply to both
acquisitions; both versions are revalidated before pair publication.
Cancellation propagates without publishing a partial pair. Invalidated query
evidence or failed owned cleanup cannot yield `Compared`. The query borrows
the groups and source capabilities; it does not acquire authority to close
host-owned groups or resources.

The query is `InspectionCost.Moderated` and requires explicit source intent.
Acquisition is sequential. Its result retains evidence, not metadata readers
or content-opening capabilities.

## Consumers and limits

The immediate adopter is CLI `diff --pdb-source` with one explicitly selected
method represented by an exact MethodDef anchor and one assembly on each side.
Accessor selections that cannot retain that anchor stay on the existing
enrichment path rather than being promoted to their owning declaration.
The shared query remains available to
the planned browser two-version Source view. Existing broader CLI enrichment
is not claimed migrated or removed by this bounded cutover.

The single delivery ledger is
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706):
S1 contract alignment (landed), S2 this query, S3 CLI adoption, S4 browser
facade, S5 browser view/state, S6 scoped retirement. Six milestones total;
CLI adoption uses S1-S3 and browser adoption S1/S2/S4/S5. S2 and S3 travel
together in [#5970](https://github.com/richlander/dotnet-inspect/issues/5970)
rather than leaving another unconsumed substrate. Browser ownership
remains under [#5083](https://github.com/richlander/dotnet-inspect/issues/5083).

CLI rendering consumes the typed pair alongside unchanged native C#/IL
results and lowers into its existing Markout Implementation Diff rows.
Retained line moves remain visible with their old and new declaration-relative
line numbers, including when no line content changed or moves coexist with
content edits. Only an exact pair receives an unchanged Source row. The
browser will consume the same pair and shared text-diff presentation; no
browser transport or interaction contract is defined here.

## Outcome gates

The query and CLI Release gates cover compiler-produced Source-only changes, equal
source, unavailable and failed PDB source, missing targets, same-token
different-image association, cancellation, and binding invalidation while the
other endpoint is acquired. Source-only execution does not invoke local
producers. Ordinary PDB-first/decompiled-fallback behavior is retained.

```bash
dotnet run --project src/DotnetInspector.Queries.Tests -c Release -- \
  --filter-class '*AssemblyContextSourceQueryTests'
dotnet run --project src/dotnet-inspect.Tests -c Release -- \
  --filter-class '*SelectedSourceDiffTests' --filter-class '*DiffCommandTests'
```

The query gate also covers explicit sidecar permission, pathless inputs,
acquisition byte limits, and owned PDB cleanup failure. The CLI gate exercises
text and JSON output, selected-document composition, native-lane preservation,
and resolved package coordinates through symbol acquisition. Compiler-produced
two-line moves, alone and alongside a content edit, gate retained move evidence;
swapping isolated lines is not a substitute for that boundary case.

Fixtures supply real compiler-produced assemblies, PDBs, and source. Product
lookup, checksum verification, extraction, and comparison produce the evidence;
tests do not fabricate successful source endpoints.
