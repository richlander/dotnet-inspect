# External-focused CLI call graph

This document owns the CLI composition that exposes the
[external-focused Inspection Graph](external-focused-inspection-graph.md) as a
real package command and lowers its typed result through Markout.

Implementation is tracked by
[#7595](https://github.com/richlander/dotnet-inspect/issues/7595). The complete
relationship experience remains
[#7451](https://github.com/richlander/dotnet-inspect/issues/7451).

## Claim and owner

`DotnetInspect.Cli` owns one command and presentation boundary:

1. resolve one exact member from an explicit root package;
2. load that root and explicit participant packages into one
   `AssemblyContextGroup`;
3. invoke
   `MemberCallGraphSession.CrossLibraryCalleeNeighborhood` with finite depth
   and node bounds; and
4. lower the returned `InspectionGraphDocument` through one Markout graph shape
   and one ordered semantic edge-row projection.

Queries owns assembly-generation classification and the resulting typed
document. CallGraph owns boundary and shortest-connector selection. Workspace
owns package realization and participant identity. Analysis owns physical call
receipts. Markout owns graph and table formatting. The CLI does not duplicate
those decisions.

## Command

The command is:

```bash
dotnet-inspect graph calls \
  <type> \
  <member> \
  --root-package <name[@version]> \
  [--package <name[@version]> ...] \
  --tfm <framework>
```

`--root-package` identifies the package whose assembly declares the selected
member. Repeatable `--package` values add explicit participants that may be
classified as external generations. The command does not silently acquire a
transitive package closure.

Type matching and `Name:N` or `Name~digest` member selection reuse the existing
Metadata-owned selectors. A missing or ambiguous type or member fails before
call-graph construction with stable-selector guidance. `--all` explicitly
admits non-public declarations.

`--depth` is a non-negative maximum edge depth and defaults to `3`.
`--max-nodes` is a positive call-node budget and defaults to `25`. These inputs
remain independent of output row selection.

The existing `member -S "Call Graph"` command remains the general
bidirectional member-analysis view. `graph calls` is an integration-style,
cross-package operation and therefore uses external-focused topology by
default rather than exposing an ordinary-versus-external mode switch.

## Markout lowering

The command builds one `Markout.Graph` from the selected logical edges:

- each member node keeps its typed call-graph identity;
- the node label is the fully qualified member name;
- the node group is its definition assembly when known;
- the selected seed is emphasized;
- each edge label states its typed external-focus role and physical call-site
  count; and
- Mermaid, plaintext/tree, and Markdown edge-table output lower that same
  graph.

The CLI reads
`queries.call.external-focus-role` from the document. It does not infer a role
from endpoint labels, groups, or edge position. A missing or duplicate role is
a visible command failure because the Queries contract requires exactly one
role for every retained edge.

## Semantic rows

One semantic row is one retained logical call edge. Table, TSV, JSONL, Count,
Head/Tail, and Window all address that same ordered row sequence.

Each row contains:

- source member and assembly;
- role;
- target member and assembly;
- physical call-site count; and
- exact physical call receipts.

A receipt retains caller module version id, caller MethodDef token, IL offset,
operand token, call kind, dispatch kind, and loop state. Presentation may
compact these values, but it does not replace them with a count or reconstruct
them from text.

Row selection happens after the complete bounded neighborhood is constructed.
It does not reduce acquisition, traversal, classification, or diagnostics.

## Empty and incomplete results

A complete zero-edge result names the selected member and states that no
external boundary calls were found in the explicit package context. It is not
rendered as missing output.

Typed limitations remain visible on stderr:

- call traversal incompleteness;
- catalog correspondence incompleteness;
- unavailable physical occurrences; and
- unclassified external boundaries.

Analysis failures remain visible and produce a nonzero exit. Requested depth
and node bounds are context, not warnings merely because their typed evidence
is present.

## Real-package demonstration

The production demo uses OpenTelemetry 1.18.0:

```bash
dotnet-inspect graph calls \
  Microsoft.Extensions.DependencyInjection.ProviderBuilderServiceCollectionExtensions \
  AddOpenTelemetrySharedProviderBuilderServices \
  --root-package OpenTelemetry@1.18.0 \
  --package OpenTelemetry.Api@1.18.0 \
  --tfm net10.0 \
  --all
```

The ordinary bounded graph contains 28 edges. External-focused projection
retains seven cross-assembly boundary edges, or nine edges with shortest local
connectors. With only `OpenTelemetry.Api` declared as an external participant,
one boundary is exactly classified and six remain typed unclassified
boundaries.
The shortest explanation into `OpenTelemetry.Api` remains:

```text
AddOpenTelemetrySharedProviderBuilderServices
  -> Sdk.get_SuppressInstrumentation
  -> SuppressInstrumentationScope.get_IsSuppressed
  -> OpenTelemetry.Api: RuntimeContextSlot<T>.Get
```

The exact evidence and neighboring relationship measurements are preserved on
[#7451](https://github.com/richlander/dotnet-inspect/issues/7451).

## Required gates

Release gates cover:

1. exact root-package type and member selection;
2. direct boundary and shortest local connector roles rendered distinctly;
3. omission of an external-to-external continuation;
4. unclassified boundaries retained with a visible incompleteness warning;
5. physical call receipts surviving row-oriented output;
6. complete zero-edge output naming the selected member;
7. independent depth, node, and output-row bounds;
8. stable ambiguity and missing-body failures;
9. unchanged `member -S "Call Graph"` behavior; and
10. the OpenTelemetry command as a slow real-package demonstration.

## Production path

The four-step path under #7451 is:

1. #7477: CallGraph-owned boundary and connector projection — complete.
2. #7508: Queries/Inspection Graph composition — complete.
3. #7595: this CLI Graph and Markout composition.
4. Inspect Web consumption of the same host-neutral document.

## Non-claims

This composition does not:

- change `member -S "Call Graph"`;
- add incoming external-focused composition;
- infer package ownership from display text;
- automatically acquire undeclared package dependencies;
- add package clustering or a dependency-strength score;
- change Queries, CallGraph, Workspace, Analysis, or Markout contracts; or
- add Inspect Web behavior.
