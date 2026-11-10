# CLI inspection representation inventory

## Scope and baseline

This is an implementation census, not a new architectural owner.
The baseline is `b203b6065e2259ea6ce9fc112a3b36370be755a3`, inspected on
2026-11-08. The executable command tree contains **56 registered command
paths**, including 17 public top-level command families, two hidden routing
commands, generated skill routes, and help-only parents.

The target is owned by
[Section representations](section-shapes.md#section-representations):
select an offered Hierarchy, Table, or Document endpoint before execution;
the endpoint determines `T` in `InspectionEnvelope<T>`, independently of its
encoding. Without explicit format intent, prefer Hierarchy/tree, then
Table/TSV, then Document/Markdown. A lone text payload retains its native
spelling. Heterogeneous selections require Document composition.

[Inspection envelope](inspection-envelope.md) separately owns Content, Share,
and diagnostics. An envelope around a Document does not establish a Table
endpoint, and a tree formatter over that Document does not establish a
Hierarchy endpoint. Likewise, accepting `--envelope` is neither necessary nor
sufficient evidence of representation adoption.

## Inspection commands

`Shared` below means an owner-issued envelope exists on the named route,
not that every route in the command family has migrated. `Host lowering`
means the CLI still selects or constructs that presentation from a richer
result; it is not a separately typed shared endpoint.

| Command or route | Envelope boundary at baseline | Representations and default at baseline | Remaining work |
| --- | --- | --- | --- |
| `package` children | Shared `PackageChildrenDocument` | Shared hierarchy projection; native tree. | Distinguish typed representation endpoints from the existing Document boundary; cover alternate package routes. |
| `package` selected sections, files, content, versions | Shared on file inventory/content, versions, measurements, and dependency routes; legacy composition remains. | Files/dependencies have hierarchy lowerings; selected Tables default to TSV; text sections expose payloads; compositions use Markdown. | Audit each source route and retire remaining host-owned composition. Existing native policy requires explicit section selection and suppresses itself for projections. |
| `package activity` | Shared `EcosystemChangeReportDocument` | Document, default Markdown; JSON is typed Content. | Document-only is a valid offered combination; a future activity Table must be an explicit owner adoption, not a formatter switch. |
| `library` | Shared `LibraryInspectionOutcome` and narrower name/metric/dependency results on adopted routes. | Default Library/Namespace/Type tree; selected Tables default to TSV. Dependency Structure has a typed Table route. | Finish legacy section/source paths and distinguish shared hierarchy projection from a separate endpoint. Track under #8088. |
| `library address` | Shared `LibraryAddressInspectionOutcome` | Address/document and host-selected row presentations. | Declare offered representations and select their typed demand before execution. |
| `type` | Shared exact-Type, signature, declaration, and overview operations; legacy paths remain. | Default Type/MemberGroup tree; selected Tables default to TSV; source/signature payloads retain native text. | Finish route convergence and representation-specific boundaries. Track under #10885 and #10694; do not duplicate inflight #11008. |
| `member` excluding Call Graph | Shared Member documents, source, decompilation, and selected analyses; legacy paths remain. | MemberGroup trees, exact-Member Documents, native text, and selected TSV Tables. | Finish content-defined sections and route-specific shared adoption under #10805. |
| `member -S "Call Graph"` | Shared `InspectionEnvelope<InspectionGraphDocument>`. | Graph Document; CLI edge-table, tree, and Mermaid lowerings. **Default is a Markdown edge table.** | Focused Hierarchy/Table/Document adoption is implemented under #11059; see status below. |
| `depends` Type mode | Shared `TypeDependencySectionResult` | Graph/tree/edge lowerings of the shared result; JSON retains complete semantic Content. | Separate structural representation demand from Document demand without changing relationship rows or traversal bounds. |
| `depends` asset modes | Shared dependency inspection on adopted routes; route-specific composition remains. | Dependency occurrence hierarchies, edge Tables, and Documents. | Complete envelope/representation classification per root kind under #6712 and #7117. |
| `depends --project-document` | Shared `ProjectInputDocument` | Document-backed selected project rows; native row format is selected separately. | Verify distinct Table demand rather than treating formatting as endpoint adoption. |
| `diff` API Changes | Shared typed Table and Document endpoints. | Changes default to TSV; explicit Markdown/JSON uses Document. | Positive representation-adoption precedent, not evidence that every Diff mode conforms. |
| `diff` Summary/Transitions | Shared typed Table and Document endpoints. | A single selected Table defaults to TSV; heterogeneous composition uses Document. | Positive precedent under #10385. |
| `diff` implementation, source, history, other analyses | Shared on several exact-pair/history/analysis routes; legacy orchestrator paths remain. | Owner-specific Documents and structural lowerings. | Complete per-mode census/adoption under #8828 and #9964; preserve exact-pair and projection admission. |
| `find` Package Query | Shared `PackageQueryDocument`. | Host-lowered result Tables; complete typed JSON Document. | Separate Table demand and row-only rendering from Document summary/context construction. |
| `find --libraries` | Shared `LibraryQueryDocument`. | Host-lowered Libraries and Query Summary Tables. **Default is Markdown.** | Existing focused adoption issue #10546 owns typed Table/Document endpoints and consolidation. |
| `find` API search and ecosystem search | API routes retain legacy results; ecosystem routes envelope a staged search summary. | API Find defaults to TSV; other routes retain route-specific reports. | TSV alone is not envelope adoption. Adopt the completed search boundary without replacing progressive report semantics; #10929 owns ordered CLI reports. |
| `extensions` | Legacy CLI orchestration; no completed inspection envelope in `ExtensionsCommand`. | Host-owned extension rows and Markdown/TSV/JSON lowerings; default Markdown. | Establish the shared operation/envelope and Table/Document demand before changing its default. |
| `ecosystem` | CLI builds selected knowledge sections; no completed inspection envelope in `EcosystemCommand`. | One default catalog/info section, rendered as Markdown. | Move selected knowledge content to an owner-issued envelope; offer Table/Document as appropriate and default a lone Table to TSV. |
| `project` | CLI builds restored-package document rows; no completed inspection envelope in `ProjectCommand`. | Selected Skills/README inventories use host-owned row/document lowerings; default Markdown. | Establish shared detached content and typed Table/Document endpoints; preserve explicit document acquisition/printing. |
| `match` direct and discovery | Shared `ResearchMatchResult` and discovery inspection on adopted routes. | Comparison Documents and host-lowered candidate Tables. | Classify direct comparison separately from candidate inventory; do not make a contextual comparison into a row stream by default. |
| `graph calls` | Shared `PackageDependencyMemberCallGraphInspectionOutcome`. | Supply-chain graph Document with host edge/tree/Mermaid lowerings. **Default is Markdown.** | Offer the same tree-projection/Table/Document choices without changing package realization or baseline policy. Avoid overlapping traversal work in #10859. |
| `graph integrations` | Calls the shared graph query directly and returns a bare `InspectionGraphDocument` to CLI rendering. | Host graph/tree/edge lowerings; default Markdown. | Add a completed operation envelope and explicit representation demand. |
| `graph libraries`, `graph packages`, `graph cluster` | Shared local/package pair outcomes and root-path inspections on adopted routes. | Cluster/call-site Tables and contextual Documents; default Markdown. | Declare per-section representations; default one Table to TSV, retain Document for heterogeneous selections, and retire display-lowered JSON where adopted. |
| `explain` root, resource, search | Shared resource-explanation and capability-search envelopes on adopted routes. | Root catalog defaults to TSV; exact explanation defaults to Document/Markdown; search has route-specific rendering. | Preserve the valid root/Document distinction; inventory component and projected forms separately before claiming full endpoint adoption. |
| `workspace` explicit inventory | Shared `WorkspaceTopLevelInventoryOutcome` and dependency enrichment. | Inventory rows are adapted by the host; default Markdown. | Separate runtime inventory Table/Document demand from packet authoring. |

### Source entry points

The command registration is
[`CommandLineBuilder.CreateRootCommand`](../../src/DotnetInspect.Cli/CommandLineBuilder.cs).
The older native-shape policy is
[`SectionShapeOutputPolicy`](../../src/DotnetInspect.Cli/Output/SectionShapeOutputPolicy.cs);
Package, Library, and API callers explicitly gate it on selection provenance
and additional non-format gestures.

The positive typed-representation precedents are the Diff owner and CLI
adoption under [#10385](https://github.com/richlander/dotnet-inspect/issues/10385).
The known Library Query residual is
[#10546](https://github.com/richlander/dotnet-inspect/issues/10546).
The broader cross-command architecture tracker is
[#6639](https://github.com/richlander/dotnet-inspect/issues/6639); its older
command list is historical and is not the current executable inventory.

## Call-graph finding and operator decision

A call graph is not a hierarchy: it can contain cycles, shared callees,
multiple parents, disconnected components, and several edge kinds.
[Call-graph projection](call-graph-projection.md#lowering-a-bidirectional-graph-to-a-tree)
already defines a finite tree rendering with revisit leaves without changing
the graph's identity or directed-edge row unit.

On 2026-11-08 the operator selected:

> Offer a tree projection, edge Table, and full graph Document; default to
> tree. Preserve cycles/revisits as references.

This does not relabel the canonical Graph as a Hierarchy. The tree projection
must preserve edge correspondence and revisit references; its context nodes
must not change Count or row selection. Table and hierarchy demand must retain
required limits/failures without computing Document-only context. Explicit
Markdown, TSV, JSONL, JSON, Mermaid, environment format selection, and
heterogeneous composition retain their own admitted meanings.

Member Call Graph and dependency-aware `graph calls` have different execution
owners. Their focused adoptions must not absorb package traversal, Platform
realization, or supply-chain policy changes.

## Focused adoption and residual tracking

The operator selected **Member Call Graph first**, tracked in
[#11059](https://github.com/richlander/dotnet-inspect/issues/11059).
The implementation adds separately typed shared Hierarchy and Table envelopes,
retains the complete Document endpoint, and chooses demand before constructing
generic graph occurrences and characteristics. A lone Call Graph selection
defaults to its finite tree projection; selected directed edges and Count
remain the row unit. Explicit formats, environment choices, and heterogeneous
compositions keep their prior meanings.

The normative adoption is
[Member inspection representations](call-graph-projection.md#member-inspection-representations).
The baseline table above deliberately remains a record of the starting point,
not a claim that the other families were migrated.

All remaining envelope, endpoint, and native-default work is separately tracked
in [#11060](https://github.com/richlander/dotnet-inspect/issues/11060), linking
the existing focused owners rather than starting overlapping implementations.

## Operational, conversion, and forwarding routes

These paths are included in the 56-path census. They are not evidence that an
inspection endpoint exists, and should not acquire dummy subjects, rows, or
Share packets merely to resemble inspection commands.

| Paths | Existing role |
| --- | --- |
| Root help, root `-v`, help-only parents | Command syntax and schema presentation. |
| Hidden `router`, `activate-workspace-packet` | Forward to an actual semantic command or activate invocation state. Audit the destination operation, not a second fabricated inspection. |
| `cache`, `cache clear` | Local operational state and mutation; classify separately from package/assembly inspection. |
| `demo`, `demo list` | Finite demo catalog or forwarding into ordinary section-backed commands. Execution inherits the selected command's boundary. |
| `skill`, `skill list`, generated focused routes | Embedded guidance and its finite catalog. |
| `workspace` packet authoring | Portable definition authoring rather than content inspection. |
| `workspace packet encode`, `decode` | Canonical format conversion, without acquisition or inspection execution. |
| `workspace component list` | Typed component/path extraction from an existing packet. |
| `workspace package add`, `remove`, `update` | Immutable definition editing; update additionally consumes a shared replacement outcome. |
| `workspace ecosystem add`, `remove` | Registration definition editing. |
| `workspace prefix add`, `remove` | Registration definition editing. |

Generated focused routes are `compatibility`, `correctness`, `decompiler`,
`metadata`, `package-skills`, `performance`, `private-feeds`,
`project-analysis`, `query`, `relationships`, `signals`, and `sourcelink`.
Help-only parents are `graph`, `workspace component`, `workspace ecosystem`,
`workspace package`, `workspace packet`, and `workspace prefix`.

## Baseline evidence

The required SDK selected in the isolated worktree was
`11.0.100-rc.3.26522.148`. The normal Release solution build passed with zero
warnings and errors.

The exact built executable produced the 56-path command tree with `-v:detailed`.
Offline smoke checks reproduced:

- Member Call Graph's Markdown edge-table default over the repository's
  `MemberCallGraphFixture.RootCall`.
- `ecosystem`'s single-section Markdown catalog.
- `find --libraries`' Markdown table over the built CallerGraphCaller fixture
  directory.
- Native Library and Type trees over the CLI test assembly.

The existing non-slow `MemberCallGraphSectionTests`,
`ExternalCallGraphCommandTests`, and `SectionShapeOutputPolicyTests` passed:
103 tests, zero failures. These are baseline observations, not proof that the
remaining command families conform, nor accepted performance evidence.

## Focused adoption evidence

The Release solution build passed with zero warnings and errors.
The three shared representation tests passed, covering cyclic/shared node
references, disconnected selected edges, stable edge correspondence, and
incompleteness retained through an empty window.

The complete Member Call Graph and section-shape policy selection passed
103 tests, including its slow row-window, Count, format, and annotation cases.
Two unrelated slow cases were excluded only after reproducing their identical
failures in an untouched worktree at the exact baseline:

- `SelectedProperty_UsesPropertyEnvelopeAcrossCSharpViews`: installed-runtime
  property-envelope failure, already tracked in
  [#8043](https://github.com/richlander/dotnet-inspect/issues/8043).
- `CallGraphSection_ProjectsAsyncAlternativesAcrossAssemblies`: missing
  cross-assembly async-alternative evidence, already tracked in
  [#10008](https://github.com/richlander/dotnet-inspect/issues/10008#issuecomment-6204091920).

Offline checks of the exact built executable confirmed the new tree default
on the actual product method `CallGraphSectionAdapter.ToGraph` with
`--index 1`. Environment-selected Markdown and complete graph JSON remained
admitted. A selected TSV edge window was byte-identical to the exact baseline;
an empty Table window retained its schema without context-node rows.
Unsupported edge-column projections retain their existing admission behavior;
this slice does not broaden the graph-field discovery contract.

Changed Markdown passed `npx markdownlint-cli`. This is local correctness
evidence, not release or performance certification. Review, CI, and
exact-base/head NativeAOT performance evidence remain required before merge
readiness.
