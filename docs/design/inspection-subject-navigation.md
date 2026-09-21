# Inspection subject navigation

Inspection Subject Navigation is the product owner for choosing and retaining
the structural subject inside one exact inspection Workspace. It supplies a
host-neutral contract for Workspace, Ecosystem, Package, Library, Type, and
Member navigation so that browser, CLI, and future hosts do not invent
different identity, route, default, or recovery rules.

## Status

This is the target architecture for issue #4794, corrected by #5582 after the
approved split of #5434 and PR #5524 to de-conflate Workspace,
retained-coordinate selection, and Package inspection. Issue #5013 completes
its focused lens-recommendation semantics. The
Workspace-rooted structural kind and exact subject identity family is
implemented by
`StructuralSubjectIdentity` and gated by
`StructuralSubjectIdentityTests.WorkspaceSubject_BindsOneExactWorkspaceOccurrence`,
`KindVocabulary_IsClosedAndWorkspaceRooted`,
`Identities_BindExactOwnerIssuedComponents`,
`PortableCoordinateAlone_CannotIdentifyRetainedPackageSubject`,
`PackageSubject_RequiresPackageOccurrence`,
`MemberIdentity_BindsExactDeclaringTypeAndAnchor`, and
`Construction_RejectsAbsentOwnerIssuedComponents`. Exact lens identity,
retained evaluation bases, and pure lens recommendation are implemented by
`NavigationLensRecommendation` and gated at their claims below. Pure pre-#7318
initial subject ranking over available Library candidates and their retained
Type inventory is implemented by `NavigationInitialSubjectRecommendation` and
gated at its claim below for one already selected Package occurrence.
Generation-free classification of bounded Type and Member inventory evidence
is implemented by
`NavigationSubjectInventoryClassification` and gated at its claim below. Pure
standalone exact-lens activation is implemented by
`NavigationLensActivation` and gated by
`StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution`,
`ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback`, and
`ExplicitLensResolution_RetainsExactRegistryEvidence`.

Pure stateless Workspace-rooted snapshot composition is implemented by
`NavigationWorkspaceSnapshotEvaluation`. It preserves exact Workspace and
occurrence identity, ordered Package descriptors, retained hierarchy, bounded
Type and Member inventory, complete target-aware Registry options, and the
effective or non-effective recommendation basis. It is gated by
`NavigationWorkspaceSnapshotTests.ZeroOneOrManyOccurrences_DoNotInventActiveOccurrence`,
`ExactSelectedOccurrence_PreservesAncestryInventoriesAndEvidence`, and
`PerSubjectAvailabilityProvider_RetainsUnavailableAndFailedEvidence`,
`SubjectlessRetainedContext_IsRejectedBeforeRecommendation`,
`MemberHierarchy_UnresolvedEvidenceIsFailed`,
`PreparedPackage_RequiresExactOwnerIssuedAssetParticipantAssociation`,
`TypeHierarchy_UsesTheExactLibraryInventoryOutcome`,
`SelectorMiss_RetainsIncompleteScopedInventoryEvidence`, and
`MemberSelector_UsesTheSelectedContainingTypeInventory`.

The pure stateless descendant subject plus exact-lens mapping is implemented by
`NavigationDescendantLensEvaluation`. It validates the exact source,
Workspace, occurrence, and eligible Library-to-Type or Type-to-Member
relationship before Registry resolution, then reuses
`NavigationLensActivation.ResolveExact` without recommendation or partial
posting. It is gated by
`NavigationDescendantLensEvaluationTests.LibraryToType_AppliesExactDestinationPair`,
`AllLibrariesType_RetainsExactDefiningLibrary`,
`TypeToMember_AppliesExactDestinationPair`,
`InvalidAncestry_RejectsBeforeRegistryResolution`, and
`NonAvailableDestinationLenses_PostNeitherRequestedHalf`.

The CLI `workspace` consumer evaluates this product result and lowers its
portable projection through Markout and structured formats. Its focused gates
are
`WorkspaceCommandTests.ActivePackage_ProjectsPortableNavigationThroughJson`,
`ExactTypeAndMemberLens_UseAtomicStatelessNavigation`,
`UnknownDestinationLens_RetainsSourceAndDiagnostic`,
`ActivePackage_MarkdownLowersNavigationThroughMarkout`, and
`ActivePackage_JsonlCarriesPortableDescriptorRecords`,
`ActivePackage_ActiveEntriesExecuteAndTombstoneRemainsRetired`,
`PortableSelectors_RoundTripThroughDisplayContainment`,
`PortableSelectors_RoundTripThroughSupportedOutputFormats`,
`PortableSelectors_PreservePrintableAsciiThroughMarkdown`,
`GenericTypeSelector_CopiesFromMarkdownAndRebinds`,
`WhitespaceOnlyTypeSelector_CopiesAndRebinds`,
`WhitespaceOnlyTypeSelector_RebindsAdvertisedIdentity`,
`MissingType_RendersNonSuccessSnapshotAndDiagnostic`,
`RootOnlyAllLibraries_RendersTypedUnavailableSnapshot`, and
`AllLibrariesRejectsIgnoredLibraryWithoutTypeDestination`. Default package
inventory, row selection, and Count remain unchanged. Portable Type and Member
rows retain defining Library asset IDs; Member rows distinguish containing
from declaring Type. Portable Library, Type, and Member selectors use a
reversible backslash transport spelling before display containment and
tabular lowering, and the CLI decodes that spelling before exact ordinal
resolution. Exact selector identity is nonempty rather than non-whitespace;
metadata names made only of whitespace or line separators remain selectable.
Runtime Workspace, occurrence, generation, action, and authority identities
are not serialized.

Issue #6113 adopts the stateless-core pattern within this existing Navigation
owner: immutable product-issued `NavigationState` is passed explicitly to
`NavigationTransitions`, while the host owns the current-state slot and
operation execution. The approved slice separates semantic revision from
action-publication generation. Issue #6112 adds the canonical preparation
participant for a fresh unpublished Workspace: it validates the exact retained
context and optional subject/lens pair, then returns either one complete
independent `NavigationState` with effect authority or a typed non-prepared
result with no state or authority. It does not implement Browser cutover
(#6757), the
[protected Scope-result consumption contract](navigation-scope-operation-consumption.md),
complete Definitions restoration, or Workspace publication. Implementation conformance is gated by
the named Release tests in [Verification](#verification). The workspace-owned
identity prerequisite is implemented by `InspectionWorkspaceIdentity`; the
observational occurrence view remains available for its unmigrated Browser
consumer. Registry adoption is tracked by #5509, and portable
Workspace/Package subject projection by #5525.

The concurrency claims are specified separately as executable TLA+ models under
[`models/inspection-subject-navigation/`](models/inspection-subject-navigation/).
Those models check the design state machines; they do not prove that a future
C# or TypeScript implementation conforms to them.

The Workspace-rooted subject graph specified below is **target-only and
unverified** under [#7301](https://github.com/richlander/dotnet-inspect/issues/7301).
The current `StructuralSubjectIdentity`, snapshot, action, restoration, and
CLI gates prove only the implemented Package-backed subset. They do not yet
prove Ecosystem subjects, direct Workspace-to-Library routes, route-independent
subject identity, or route reconciliation.

The first preparatory Ecosystem-intake slice is implemented by the
Navigation-owned result contract in `DotnetInspector.Queries` and
`EcosystemPopulationNavigationProjection` in
`DotnetInspector.EcosystemLoading`. The adapter classifies each owner-issued
historical accepted Focus witness against one caller-supplied current Workspace
registration revision and returns exact current contribution evidence only
while the same declaration object remains registered in the same Workspace.
This slice does not implement the target Ecosystem occurrence identity,
structural subject, route, activation, or reconciliation.

PR #5433 demonstrates the intended browser distinction: Workspace manages
retained coordinates, Package is inspectable, and package tabs are absent.
Those browser identities and transitions remain host-local migration facts,
not authority for this product contract. PR #5501 refines only their responsive
presentation. The browser still seeds a Type cursor, widens accessibility
to admit it, reconstructs coordinate activation from package keys, and
reconciles subject levels locally; #5510 and #5511 track removal of those
migration paths.

Within that current Browser migration boundary, a Package Version or Framework
change carries the initiating Package inspector preference: Overview stays
Overview and Dependencies stays Dependencies. This is inspector intent for the
replacement coordinate, not correspondence for an old subject or bound lens
identity. Retry retains the same intent; dependency queries use the returned
coordinate rather than reusing the previous coordinate's results. The existing
content-local loading path keeps coordinate focus through those result renders.
`System.Text.Json@10.0.0`, inspecting Dependencies and selecting `net9.0` or
version `10.0.1`, motivates this bounded behavior. The production-composition
cases in `inspect-web/browser/library-hierarchy.package-loading.spec.ts` gate
both inspectors, both controls, pending/success, immediate return,
failure/retry, and narrow layout. Initial package selection keeps its existing
behavior. This Package-view slice does not establish Library/Type/Member
correspondence or retain filters.

A separately user-approved interim Browser slice carries Library-selection
intent through the same coordinate controls and retry. It requests the selected
Library's name through the existing library selector against the returned
package. A unique resolution retains the Library inspector preference and uses
the returned asset ID for fresh content, counts, and the Type inventory. Missing
or ambiguous resolution selects Package Overview with a visible explanation;
it never selects an arbitrary neighboring Library. A Library with no public
Types remains selected when its descriptor is available. Initial package
selection and explicit links are unchanged; Type/Member and filter retention
remain out of scope.
`System.Text.Json@10.0.0`, inspecting its Library Metadata while selecting
`net9.0` or version `10.0.1`, motivates this behavior; the
`library-hierarchy.package-loading.spec.ts` production-composition gate includes
changed asset IDs, missing and ambiguous names, empty Type inventories, and
failed-load retry.

This is a reissued selector preference, not proof of subject identity or
cross-Workspace correspondence. The operator chose this bounded migration
behavior instead of expanding the task into the Browser Navigation
prerequisites. It does not alter the target reconciliation policy below.
Browser adoption in #5511 retires these host-local preference mechanisms in
favor of product-issued Navigation results.

The ancestor Type fallback and coordinate inspector-request retention specified
below are implemented for protected Package-coordinate replacement by
`NavigationScopeOperations` under
[#7061](https://github.com/richlander/dotnet-inspect/issues/7061), with the
implemented cases identified in the focused protected-consumer design.
Ordinary `NavigationWorkspaceSnapshotEvaluation.Refresh` can still choose a
sibling Type; its ancestor-fallback adoption remains **unverified**. Browser
adoption remains separate and the shipped Browser preferences above are
unchanged.

The [forwarded ancestry policy](#forwarded-api-ancestry) settles
[#7169](https://github.com/richlander/dotnet-inspect/issues/7169) within that
same adoption path. The shared correspondence producer and completed matching
envelope landed in #7188/#7189. Protected Navigation now consumes their actual
defining-Library result, preserves active ancestors, and applies forwarded
fallback under #5584. CLI and Browser/Wasm adoption remain separately
unverified.

The aggregate-first Package policy below is likewise **target-only and
unverified** under
[#7318](https://github.com/richlander/dotnet-inspect/issues/7318). Current
`NavigationInitialSubjectRecommendation` still prefers one primary or
declaration-order Library before `All libraries`. The #7318 slice instead
recommends the existing Package-scoped aggregate and defines exact and
namesake-Library narrowing as explicit gestures. It changes neither Package
acquisition nor the shipped CLI and Browser consumers until their focused
adoption slices land.

## Consumer and complexity record

The end-to-end tracker is #5512. The concrete consumers are:

- the Browser/Wasm Workspace and structural-subject experience demonstrated by
  #5433, with product descriptor adoption tracked by #5510, presentation
  composed by [Inspect Web Navigation
  Presentation](inspect-web-navigation-presentation.md), and result-authority
  adoption by #5511; and
- the agent-inspectable CLI Workspace navigation surface tracked by #5513.

The first host-neutral implementation slice is #5518. Workspace Definitions
adoption for portable Workspace and Package subjects is #5525.

This is shared product substrate; no single-consumer or single-host exception
applies. The simplest sufficient boundary is one product-issued Navigation
state lineage bound to one exact active Workspace realization, with one
Workspace subject, zero or more exact Ecosystem, Package, and Library subjects,
and exact Type and Member descendants. It adds no generic Root, global Package,
`All packages` subject, cross-Workspace correspondence, or second concurrency
protocol.

Artifact inputs do not become structural kinds merely because they can contain
Libraries. Project, file, embedded, and future source families remain outside
the closed kind vocabulary until a focused Navigation design demonstrates a
concrete subject identity and behavior. Artifact Acquisition continues to use
Root for its broader physical realization contract. Scope's
`WorkspacePackage*` vocabulary continues to identify exact Package occurrences;
it does not make Package mandatory ancestry for a Library. The earlier
package-specific decision is recorded on
[PR #6184](https://github.com/richlander/dotnet-inspect/pull/6184#issuecomment-5574458614);
issue #7301 replaces that Navigation constraint without relabeling non-package
artifacts as Packages.

The atomic descendant-subject plus exact-lens capability added by #6490 follows
that same two-host adoption:

- #6111's stateless Navigation producer evaluates one exact descendant pair,
  and #5513 exposes that result through the agent-oriented CLI surface when a
  CLI request supplies an exact subject and facet. The CLI receives the same
  exact Registry mapping and complete snapshot result, but no action ID,
  retained effect authority, Browser history, or Compare mode.
- #6113's explicit Navigation state binds the same request to current intent
  and synchronization authority. #5510 and #5511 adopt the resulting opaque
  interactive action and complete result in Browser/Wasm; Compare uses it in
  stages 4 and 5 of its nine-stage adoption path.

The pure exact-pair evaluator and retained-state transitions are one Navigation
capability, not separate host policies. The CLI does not acquire a retained
terminal session, and Browser/Wasm does not reconstruct the exact pair from
display state.

Issue #7318 adds one policy step to the focused successor stack recorded on
PR #7372. Its PackageHouse prerequisite is PR #7435; #7431 supplies the
compile-selection evidence. The production consumers are #7430
(Package/Library CLI), #7429 (API/Type/Member), #7432
(Workspace/Navigation), and #7428 (Inspect Web). Find participant selection in
issue #7433 consumes the same package evidence but does not make Find a
Navigation subject. Command-local aggregate, TFM, primary-Library, and namesake
preference retire only as those consumers adopt the shared result.

The exact Workspace and retained-occurrence ancestry is necessary for
correctness: without it, distinct logical occurrences inside one Workspace can
alias, and display keys can target the wrong retained occurrence. A
corresponding physical-generation replacement intentionally preserves that
occurrence; its refreshed `ArtifactRootScopeProjection` and
generation-scoped actions distinguish current physical authority and reject
stale work. The existing opaque-snapshot TLA+ state machines remain sufficient
for Navigation-local intent, maintenance, and authority ordering. Workspace
scope-operation results are owned by
[Workspace Scope and Expansion](workspace-scope-and-expansion.md). Their
protected Navigation consumption is implemented by the shared producer in
[Navigation Scope-operation consumption](navigation-scope-operation-consumption.md)
under [#5584](https://github.com/richlander/dotnet-inspect/issues/5584).
Source-retiring correspondence orchestration is implemented there; CLI and
Browser adoption remain unverified follow-on slices.
Structural containment remains implementation-gated rather than model-checked.

Navigation returns typed descriptors, identities, evidence, and outcomes. The
CLI consumer lowers those types through Markout. Browser/Wasm uses the
host-specific interactive rendering owned by
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
because focus, accessibility, and responsive interactive navigation are
browser concerns; it does not reconstruct product semantics from rendered
text.

The approved #7061 delivery map counts six capability steps: this Navigation
policy, the focused correspondence producer, protected Navigation replacement
consumption, CLI adoption, Browser descriptor/control adoption, and Browser
result posting. Existing shared realization and restoration prerequisites
are separate costs, not hidden inside those six steps. The CLI #5513 consumes
the base stateless result. Replacement adoption follows the newer
definition-first Workspace boundary under
[#7466](https://github.com/richlander/dotnet-inspect/issues/7466) and
[portable coordinate replacement](portable-coordinate-replacement.md), rather
than growing the transitional `workspace --active-package` grammar.
Browser/Wasm #5510/#5511 consumes the same policy through retained results.
At Browser cutover, retire the corresponding host-local
Package/Library preferences only after preserving their shipped behavior.
The added policy serves one experience: a coordinate change must not replace
the selected API with a sibling or silently discard an explicit inspector
request. It adds no matching algorithm, cache, or concurrency protocol.

Forwarded ancestry is part of protected replacement step 3, not a seventh
capability step. The ordinary policy preserves ancestors before descendants;
the bounded exception below lets an exact Type counterpart replace its
non-active Library context with its actual defining Library. An explicitly
active Library still wins over lower-path retention. This avoids both false
ancestry and a surprising Library switch without adding another retained path.

## Design demo

The target experience has one singular active Workspace as its universal
inspection root. Package participates only when the selected route and subject
actually have Package ancestry:

```text
Workspace
|- .NET Runtime
|  |- System.Text.Json (.NET Runtime Library)
|  |  |- System.Text.Json.JsonSerializer
|  |- System.Text.Json 10.0.0 (Package)
|  |  |- System.Text.Json (Library)
|  |- System.Text.Json 10.0.1 (Package)
|     |- System.Text.Json (Library)
|- ASP.NET Core
|  |- Microsoft.AspNetCore.Http.Abstractions
|- Newtonsoft.Json 13.0.4
|  |- Newtonsoft.Json
|- one directly admitted Library
```

The target `.NET Runtime` Ecosystem authors the .NET runtime platform
contribution and the `System.` Package Prefix contribution. The source-native
`System.Text.Json` Library and both admitted Package occurrences therefore
have exact routes beneath that Ecosystem. The prefix provides population and
route evidence; it did not admit either Package, grant source authorization,
or make the Ecosystem their exclusive provenance owner.

That Ecosystem composition is an adjacent Ecosystems-catalog target, not
Navigation-owned policy. The current product pack declares both the .NET
runtime platform population and the `System.` Package Prefix. Focused
Navigation adoption must preserve those exact contributions and the
`.NET Runtime` presentation identity before this complete demo is supported.
Prefix matching semantics remain owned by `PackagePrefixDeclaration`.

The three visible `System.Text.Json` observations remain distinct:

- the source-native Library has no invented Package ancestry;
- `System.Text.Json@10.0.0` is one exact Package occurrence with its admitted
  Library; and
- `System.Text.Json@10.0.1` is another exact Package occurrence with its own
  admitted Library.

The `.NET Runtime` Ecosystem may share either Package or Library with another
Ecosystem because route membership is many-to-many. `ASP.NET Core` is another
Ecosystem with its source-native
`Microsoft.AspNetCore.Http.Abstractions` Library.
`Newtonsoft.Json@13.0.4` is the neighboring Package without a `.NET Runtime`
Ecosystem route. A directly admitted Library may be activated from Workspace
without passing through either Ecosystem or Package.

The real assets are
`Microsoft.NETCore.App.Ref@10.0.0/ref/net10.0/System.Text.Json.dll`,
`Microsoft.AspNetCore.App.Ref@10.0.0/ref/net10.0/Microsoft.AspNetCore.Http.Abstractions.dll`,
`System.Text.Json@10.0.0`, `System.Text.Json@10.0.1`, and
`Newtonsoft.Json@13.0.4`. They preserve the source-native framework,
two-version package, source-native ASP.NET Core, and package-backed neighboring
cases through ordinary product acquisition rather than synthetic subject
labels.

Spotlight may reach the same exact `System.Text.Json` Library directly:

```text
Workspace -> System.Text.Json -> JsonSerializer
```

That direct route and
`Workspace -> .NET Runtime -> System.Text.Json -> JsonSerializer` retain the
same exact Library and Type subject identities when they bind the same
owner-issued Workspace occurrences. The route differs; the inspected subject
does not.
Conversely, the `System.Text.Json` framework Library and a package-origin
Library with the same visible assembly name remain distinct exact subjects
because their owner-issued source occurrences differ. The two package
occurrences also remain distinct despite equal Package ID and Library name
because their exact versions and Workspace occurrence identities differ.

The existing production Browser/Wasm scenario in #5433 proves the
Package-backed subset: Workspace and Package are distinct navigation
identities, the Workspace surface lists retained coordinates without package
tabs, and Package has its own inspection surface. #7301 extends that
Workspace-rooted distinction rather than replacing Package identity.

The plural **Workspaces** experience remains separate. A saved Workspace
definition is comparable to a saved game: opening it constructs a fresh
singular Workspace realization. The saved-definition collection, editor,
Open, Save, Forget, and lifecycle controls are not structural subjects in this
graph.

### Convention and deliberate divergence

[Visual Studio Code workspaces](https://code.visualstudio.com/docs/editing/workspaces/workspaces)
separate the active window's Workspace from a saved `.code-workspace`
definition, including an untitled live Workspace that may later be saved. That
is the conventional basis for singular Workspace versus plural Workspaces.
dotnet-inspect keeps its existing local packet and saved-entry owners rather
than adopting VS Code's file format or lifecycle.

[Visual Studio Solution
Explorer](https://learn.microsoft.com/en-us/visualstudio/ide/use-solution-explorer)
demonstrates the conventional value of one visible root containing typed
children. This design deliberately does not impose one canonical tree beneath
that root. Ecosystem contribution is many-to-many, direct Library activation
is valuable, and Package is not natural ancestry for every Library. Exact
subject identity plus an independently retained typed route preserves the
orientation benefit without inventing ownership.

### Coordinate retention

This is a mockup of the #7061 target, not current Browser output. The motivating
coordinate pair is `System.Text.Json@10.0.0` and `10.0.1`, with `net10.0` and
`net9.0` as the framework variation. The implementing gate must acquire the
real pair and retain its API evidence; the mockup does not certify a particular
overload's presence in those assets.

```text
Before: JsonSerializer -> one exact Deserialize overload -> requested Source
Action: change Version or Framework
After:  corresponding overload -> requested Source, resolved for the new subject

Missing Member: containing Type, with the correspondence diagnostic
Missing Type:   defining Library, even when another Type is available
Inspector unavailable: resolved API remains selected; exact reason is visible
```

The missing-Type case deliberately differs from the former sibling-Type
recommendation. Returning to a containing subject leaves the person in control
of the next API selection. Existing Member-to-Type fallback and exact Registry
resolution without a neighboring facet are the analogous policies; they do not
establish correspondence. An exact supplied Package replacement and typed
descendant correspondence are prerequisites, not facts inferred from this
example's labels. Coordinate controls on Type/Member views, fresh content
queries, focus, and history remain counted Browser adoption work.

#### Forwarded coordinate retention

The real witness is `Avalonia.Data.MultiBinding` in
`Avalonia@11.3.14 -> 12.1.2 / net8.0`: `Avalonia.Markup` defines it before,
then forwards it to `Avalonia.Base`. The
[correspondence owner](forwarded-api-coordinate-correspondence.md#product-question-and-real-demo)
records the assets and route; its acceptance gates cover the exact Type and
constructor, and a non-matching `Converter` declaration after relocation.
The following Navigation outcomes are a mockup, not current Browser behavior:

```text
Before: Package P  -> Library A  -> Type T  -> Member M
After:  Package P' -> Library B' -> Type T' -> Member M'
Route:  entry A' forwards T to B'; A still pairs with A', not B'

Type or exact Member active: follow the declaration into B'.
Package or Workspace active: keep it active; retain the lower path through B'.
Library A active: keep paired A' active; discard T/M outside A', explain why.
Member absent, Type exact: retain B'.T'; an active Member falls back to T'.
Type not exact: retain the available entry ancestor A', with native evidence.
```

The active-Library case deliberately sacrifices lower context rather than
change the subject the person selected. The Type/Member case instead follows
that selected API; the Library change is ancestry of its exact counterpart,
not a Library correspondence or a separate Library activation.

## Problem

Workspace lifetime, saved definitions, registrations, admitted content,
structural subject identity, and the route used to reach a subject are
different concepts. One singular Workspace owns an isolated inspection world.
Package identifies one exact retained package occurrence when Package ancestry
exists; it is not the root of every Library, Type, or Member.

Today the host owns too much of that distinction:

- it treats retained coordinates as package tabs and reconstructs their
  selection from browser state;
- it conflates the active Workspace subject with plural Workspace lifecycle
  and retained-coordinate management;
- it cannot represent an Ecosystem or direct Library route without either
  omitting structural context or inventing Package ancestry;
- it binds subject identity to one ancestry shape instead of retaining route
  separately;
- it chooses initial Type and lens state;
- it reconstructs parent relationships from browser data;
- it decides what survives version, framework, or inventory changes;
- it coordinates subject and lens requests with local mutable state; and
- it can expose partial or completion-order-dependent navigation.

That makes the website's defaults and recovery behavior impossible to reuse,
hard to test, and vulnerable to races between acquisition, refresh, and user
navigation.

## Decision

Inspection Subject Navigation owns:

- structural subject identity and hierarchy composition;
- the closed Workspace-rooted relation vocabulary and exact route composition;
- separation of exact subject identity from the route used to reach it;
- subject applicability, availability, and failure classification;
- initial subject recommendation and subject-scoped lens recommendation;
- Package aggregate recommendation plus exact- and namesake-Library narrowing;
- Workspace, Ecosystem, Package, Library, Type, Member, and lens navigation
  descriptors;
- exact subject and lens activation outcomes;
- same-occurrence and coordinate-variation reconciliation within one exact
  Workspace;
- retained navigation-session authority; and
- subject-and-lens initialization in a fresh restored Workspace.

The owner returns one internally consistent navigation snapshot. Interactive
consumers render its descriptors and submit opaque commands from it. They do
not select defaults, infer identity from display text, or apply fallback after
a failed request.

The expected implementation is host-neutral and normally belongs in
`DotnetInspector.Queries`. The architecture owner is the contract described
here, not a project boundary.

## Ownership and boundaries

### Inputs

The owner consumes:

- one exact open Workspace identity and owner-ordered exact subject
  descriptors;
- exact Workspace-bound Ecosystem registration occurrences and their
  owner-issued contribution relations when Ecosystem navigation is requested;
- zero or one active retained-package occurrence and its realized Package
  facts;
- exact Workspace-bound admitted Library occurrences and their owner-issued
  Package, Ecosystem, and direct-Workspace relation witnesses;
- zero or one scope-result requested active/replacement occurrence plus typed
  effect and correspondence outcomes;
- owner-issued retained-coordinate activation operations;
- admitted Library identities, owner-issued managed assembly simple names,
  declaration order, and primary preference;
- bounded Type and Member inventories in producer-issued navigation order;
- product accessibility descriptors;
- exact type-definition identities and member anchors;
- product View Facet Registry target-aware options and exact-resolution
  results;
- typed identity-resolution and correspondence outcomes; and
- either a retained-session operation or an explicit stateless evaluation.

A retained operation receives the host's current product-issued
`NavigationState` explicitly. A UI consumer cannot substitute a consumer
snapshot or author semantic fields. Standalone evaluation may receive an
explicit prior snapshot as data without retaining a state lineage.

### Outputs

The owner returns:

- one Workspace-bound active structural subject;
- one exact Workspace-rooted route for that subject;
- ordered Workspace, Ecosystem, Package, and Library descriptors supplied by
  their owners;
- ordered retained-package and Package descriptors;
- one Type-inventory Library context;
- hierarchy and Library descriptors;
- Type and Member inventory rows wrapped with activation state;
- subject-scoped lens descriptors and one lens outcome;
- scoped diagnostics and partial-result evidence;
- typed transition or reconciliation outcomes;
- opaque retained-session authority; and
- a typed consumer-synchronization disposition plus a fresh-authority
  synchronization result for retained consumers.

Evaluation returns detached evidence bound to its exact work ticket, including
exact lens-resolution and descendant-request evidence when available.
Completion returns `NavigationOperationResult(Consumer, LensResolution)`;
operation evidence is never recovered from a mutable `LastLensResolution`
side channel. No state, work ticket, transition, or result retains a service,
availability provider, task, live operation authority, lease, or resource.
Facts and providers are invocation inputs only. The invocation-availability
retention gate covers that provider's object graph.
`NavigationDetachmentTests.ArtifactBackedStateTicketsAndExactResults_DoNotRetainAcquisitionAuthority`
exercises the real Package Root producer and retains Navigation state, tickets,
and exact Library, Type, and Member results after Workspace settlement; its
acquisition registration, artifact identity, and producer exception must remain
collectible.

Retained participant failure evidence preserves an erasing exact failure
identity, exception type, HRESULT, message, and diagnostic detail, not the
exception's arbitrary object graph or `Data` attachments. Producer participant
identity likewise projects exact registration identity without access
authority. Stateless inventory classification retains its original
producer-evidence contract. Exact Registry evidence remains owner-issued;
availability providers must supply detached diagnostic evidence, not resource
owners or callbacks.

### Adjacent owners

[Artifact acquisition and workspace
composition](artifact-acquisition-and-workspaces.md) owns
Workspace identity, including the #5508 construction and close contract,
isolation,
admitted artifacts and contexts, query authorization, and lifetime. [Workspace
Scope and
Expansion](workspace-scope-and-expansion.md) owns retained-coordinate
membership and order, Workspace-bound occurrence construction, retention, and
retirement, selective dependency expansion, revisions, and scope-operation
results. Navigation consumes the Workspace identity, complete ordered occurrence
descriptors, the scope result's requested active/replacement occurrence, and
typed correspondence without defining identity construction, equality, scope
policy, replacement policy, or closure. Scope-operation production is the
focused successor to #5583; protected Navigation consumption remains #5584.

Artifact acquisition and package realization own package coordinate,
`PackageRootBinding`, content-generation, selection, and acquired-descendant
identity currencies. The current #5656 substrate composes a Workspace-local
occurrence with `PackageRootBinding`; Workspace Scope and Expansion replaces
that resource-bearing association with a complete
`WorkspacePackageOccurrence` containing its own identity, typed
`WorkspacePackageDescriptor`, and the adjacent
owner's `ArtifactRootCorrespondence`, plus a point-in-time
`ArtifactRootScopeProjection` in the occurrence descriptor. Navigation
consumes those owner-issued exact values. A portable package coordinate alone
cannot identify one retained occurrence.

[PackageHouse Composition](package-house.md) owns package-local compile
selection policy and preserves the compile selector's available slices,
selected projection, requested-versus-selected framework, roles, and
correspondence. Artifact acquisition and package realization bind that receipt
and its admitted Library outcomes to one exact Package occurrence in one
`PackageLibraries` basis. Navigation consumes that composed value, not
separately pairable occurrence and projection arguments. It neither reselects
a TFM nor ranks package asset paths. The compile-selection owner resolves exact
asset IDs within that projection. Library admission and Metadata supply the
exact admitted Library identities and managed assembly facts used by
narrowing.

The `ArtifactRoot*` names in the adjacent Artifact owner describe one physical
realization and publication unit and remain correct. Issue #6293 replaces
Scope's pre-issuance `WorkspaceRoot*` names in place; its implementation
exposes only Package occurrences.
Navigation consumes that Package-specific contract and never exposes a Root
subject.

[Type, member, and API representation](type-member-api-representation.md) owns
the Type and Member identity currencies used here.

[Forwarded API coordinate correspondence](forwarded-api-coordinate-correspondence.md)
owns the exact source/entry/defining-Library association, declaration result,
and detached forwarding evidence. It consumes Library pairing and Metadata
resolution/matching; Navigation consumes that composed evidence, not a second
forwarder walker or a host-selected destination.

[Workspace definitions](workspace-definitions.md) owns portable view-facet
registry binding. The [View Facet Registry](view-facet-registry.md), established
by [#4880](https://github.com/richlander/dotnet-inspect/issues/4880), owns
runtime lens membership, labels, order, structural applicability, and
facet-availability outcomes. Registry adoption of Workspace and Package
subjects is tracked by #5509.

[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
owns descriptor rendering, accessibility, and widget interaction; [Inspect Web
Navigation Consumer](inspect-web-navigation-consumer.md) owns post-result
effect-authority validation, snapshot/history commitment, and
result-authorized focus/announcement ordering.
[Workspace Definitions](workspace-definitions.md) owns portable projection and
complete restoration composition. #4787 established the current version-2
shape; #5525 tracks adoption of explicit Workspace and Package subjects plus an
optional retained occurrence and descendant context independent from the active
subject.

[Workspace registration and call-graph focal
length](workspace-registration-and-call-graph-scope.md) owns registration
meaning and keeps subject, registration, and operation scope independent.
[Workspace ecosystem registration
handoff](workspace-ecosystem-registration-handoff.md) owns projection of an
application Ecosystem selection into a lower-layer Workspace declaration.
Navigation consumes an exact Workspace-bound registration occurrence and
owner-issued contribution relations; it does not infer them from an Ecosystem
label or recreate catalog projection.

[Workspace top-level
inventory](workspace-top-level-inventory.md) owns the complete flat report of
Package occurrences and inert Exact Library, Package Prefix, and Ecosystem
registrations from one definition/scope observation. Its selection receipt may
resolve a reported row to the owner-issued Package occurrence or registration
arm before the responsible activation owner runs. Navigation never treats the
document-local row key as subject identity, changes that inventory's canonical
order, or deduplicates the `.NET Runtime` Ecosystem registration and two
admitted `System.Text.Json` Package occurrences merely because its route graph
groups their exact subjects beneath `.NET Runtime`. The platform and `System.`
prefix remain ordered populations inside the Ecosystem entry rather than being
flattened into independent registrations.

[Inspect Web saved Workspaces](inspect-web-saved-workspaces.md) owns the plural
saved-definition collection and its Save, Open, and Forget behavior.
[Inspect Web Workspace editing](inspect-web-workspace-editing.md) owns editor
draft and leave decisions. Neither saved entries nor editor drafts are active
structural subjects.

[Stateless core services](stateless-core-services.md) owns the explicit-state
composition pattern. Navigation adopts it without redefining Workspace
admission or the retained host's operation-authority and current-slot
contracts. One active realization gets one Navigation state lineage; a saved
`WorkspacePlan` or definition carries detached intent, not live Navigation
state, actions, receipts, work tickets, or authority. Realizing the same plan
again creates fresh state and authority. This consumes the existing
realization boundary; #6757's Browser cutover is not part of #6113.

### Non-claims

This owner does not define:

- Workspace identity construction, opening, closing, retention, ordering, or
  lifetime, or membership policy;
- saved-Workspace storage, lifecycle, selection, editing, or presentation;
- coordinate acquisition, authorization, admission, occurrence identity, or
  successor selection;
- Ecosystem catalog, registration, Package, Library, project, file, or
  package-icon construction;
- construction, equality, or lifetime of owner-issued structural relation
  witnesses;
- discovery candidates that have not become exact Workspace-bound subjects;
- structural navigation for project, file, embedded, or other source kinds;
- metadata, Type, Member, API, or view-facet registry internals;
- Type and Member inventory extraction;
- lens contents, section execution, or rendering;
- browser history, URL encoding, or complete restoration atomicity;
- package-source selection, credentials, provenance, or caching;
- target-framework compatibility, compile-slice selection, package asset roles,
  managed-image classification, or Library admission; or
- cross-Workspace inspection, aggregation, correspondence, or Spotlight
  composition.

## Domain model

### Structural subjects

Subjects form one Workspace-rooted typed graph:

| Kind | Meaning |
| --- | --- |
| Workspace | One exact open Workspace and its structural inventory; Navigation does not combine descendant inspection results |
| Ecosystem | One exact Workspace-bound ecosystem registration occurrence; registration remains inert and resource-free |
| Package | One exact retained package occurrence in that Workspace |
| Library | One exact admitted Workspace-bound Library occurrence, independent of the route used to reach it |
| Type | One exact type definition in one admitted Library |
| Member | One exact API member in one Type |

The closed route-relation vocabulary is:

```text
Workspace -> Ecosystem
Workspace -> Package
Workspace -> Library
Ecosystem -> Package
Ecosystem -> Library
Package   -> Library
Library   -> Type
Type      -> Member
```

Every route starts at the exact singular Workspace. Ecosystem and Package are
optional context, not mandatory levels. Type always retains an exact Library
ancestor, and Member always retains an exact Type ancestor. No route skips
those definition boundaries.

Workspace is always applicable while its owner-issued lifetime remains open.
An Ecosystem is applicable while its exact registration occurrence remains
present. Package and Library subjects require exact owner-issued Workspace
occurrences. Lower levels remain applicable when their exact ancestor supports
them even if an inventory is validly empty. Structurally unsupported levels
are omitted; applicable but empty levels remain visible as unavailable.

An Ecosystem registration may describe discoverable populations without
acquiring them. The Ecosystem subject can expose the registration's own typed
identity while Navigation supplies exact descendant descriptors already
admitted to the Workspace. Contribution contents and rendering remain owned by
their inspection and presentation components. Discovery rows do not become
Package or Library subjects until their owners issue exact Workspace-bound
occurrences. Registration therefore supplies relevance and route evidence, not
fabricated membership.

Package remains an exact occurrence, not an aggregate over all packages.
The Package-scoped `All libraries` aggregate is the recommended Library/API
subject where that Package supports it; it does not become ancestry for an
exact Type and it does not create Workspace-wide or Ecosystem-wide aggregate
subjects.
Navigation may supply owner-issued Ecosystem, Package, and Library inventories
without inventing `All ecosystems`, `All packages`, or `All libraries`
structural identities.

A Library retains its exact source and provenance through its owner-issued
occurrence. Package-origin provenance may bind that occurrence to an exact
Package even when the active route is `Workspace -> Library`. Direct routing
does not erase provenance, and provenance does not force a Package segment
into every route.

The same exact Package or Library may have more than one available route. Two
Ecosystems may contribute the same admitted Package, and Spotlight may provide
a direct Workspace route to a Library also available beneath an Ecosystem.
Those routes do not duplicate the subject. Equal visible names do not merge
different source occurrences.

### Identity

The conceptual subject identity family is:

| Kind | Identity components |
| --- | --- |
| Workspace | Artifact-owner `InspectionWorkspaceIdentity` established by #5508 |
| Ecosystem | Exact Workspace plus an owner-issued Workspace ecosystem registration occurrence |
| Package | Complete scope-owner `WorkspacePackageOccurrence` and its separate `WorkspacePackageDescriptor` |
| All Libraries | Exact Package plus explicit aggregate Library identity |
| One Library | Exact Workspace-bound admitted Library occurrence, including source provenance and any owner-issued Package association |
| Type | Exact Library occurrence plus exact metadata definition |
| Member | Type identity plus product-owned member anchor |

Identity equality never uses display text, filename, list position, metadata
token alone, portable package coordinate alone, browser cache key, or backend
arrival order. Subject identity also excludes the active route. Workspace,
registration-occurrence, retained-coordinate, and admitted-Library occurrence
identities are process-local and never serialized. Artifact Acquisition issues
the Workspace identity under #5508; adjacent owners construct and retire their
occurrence identities under that live Workspace authority.

One active route contains the Workspace identity followed by ordered pairs of
an exact relation witness and exact subject identity:

```text
StructuralSubjectRoute
  Workspace  InspectionWorkspaceIdentity
  Segments   (StructuralSubjectRelationIdentity, StructuralSubjectIdentity)*
```

Each relation witness is Workspace-bound, resource-free, and issued by the
owner that knows the relationship. Navigation validates that every relation
kind permits its source and destination kinds and that every segment belongs
to the same exact Workspace. It does not infer a relationship from names,
namespace prefixes, source labels, registration text, package metadata, or
prior browser history.

Subject identity answers **what is inspected**. Route identity answers **how
the current Workspace reached it**. A route change can preserve the exact
subject and its `NavigationLensIdentity`; activating a different exact subject
cannot preserve identity merely because the new route renders the same text.
If the active route disappears while the subject remains available, Navigation
may retain that subject only with a complete replacement route issued from
current owner evidence. Otherwise reconciliation falls back through the last
valid exact ancestor, ending at Workspace rather than inventing ancestry.

Navigation's acquired Library, Type, and Member identity projections preserve
the exact Metadata registration association without retaining that registration.
An artifact-backed registration has a live-authority backlink, so the
Inspection Graph acquired-identity objects are not suitable retained Navigation
data. `NavigationRegistrationIdentity` is an erasing reference identity, shared
only for the same exact registration. Different registrations remain distinct
even when their metadata and artifact coordinates compare equal.

The implementation uses weak exact-object memoization: the key is the original
owner-issued registration and the value has no backlink. The same rule applies
to erasing producer-exception identity. These associations neither retain
content nor resolve a different generation and are not a new state port or
architecture owner under [Stateless core services](stateless-core-services.md).
Only current owner-issued operation authority can acquire content; a detached
Navigation identity cannot recover it.
`NavigationDetachmentTests.ErasingIdentity_PreservesExactRegistrationEquality`
checks that equal metadata and provenance do not collapse different
registrations, while repeated projection of the same registration preserves
exact identity.

The current coordinate-rooted `StructuralSubjectIdentity` implementation is
replaced in place rather than retained as a parallel identity family.
`StructuralSubjectRoute` is separate state rather than a second subject
identity family. The closed-kind, component-binding, route-validation, and
construction gates must be updated to this Workspace-rooted graph while
preserving their existing exact Type and Member witnesses.

### Current Ecosystem contribution intake

The preparatory intake consumes only owner-issued evidence already produced by
Ecosystem Population Loading:

- the exact historical registration revision and declaration retained by the
  load receipt;
- the exact Workspace Library admission receipt and occurrence; and
- the Focus-only contribution witness that joins them.

`EcosystemPopulationNavigationProjection` compares each inseparable
Focus-witness association with one caller-supplied current Workspace
registration revision. It returns:

- `Available` with one `NavigationEcosystemLibraryContribution` when the
  current revision belongs to the witness's exact Workspace and still contains
  the same declaration object;
- `Unavailable(RegistrationNotCurrent)` when the historical evidence is valid
  but that exact declaration is absent from the current revision; or
- `Rejected(ForeignWorkspace)` when the current revision belongs to another
  Workspace.

An equal ID or equal declaration value is not currentness. An unrelated
registration revision may preserve the contribution only by retaining the same
exact declaration object. The available value retains both historical and
current revisions, the exact declaration, admission receipt, and Library
occurrence. It has no Package field or implied Package ancestry.

`EcosystemPopulationNavigationProjection` is the one-way adapter because
`DotnetInspector.EcosystemLoading` already references
`DotnetInspector.Queries`; reversing that dependency would create a cycle. It
accepts only `EcosystemPopulationAdmissionResult.Contributions`, whose
owner-issued witnesses already bind the exact historical registration,
accepted admission, occurrence, and Focus role. The adapter does not expose a
component-wise evaluator that could substitute support-only or unrelated
evidence; binding-support-only Libraries never enter this intake.

This evidence is route-ready but is not itself a structural relation identity
or route. The target graph still requires a nominal current Ecosystem
occurrence, route construction, activation, and reconciliation in one coherent
replacement of the package-only subject implementation. Hosts must not expose
the available intake value as a supported route before that slice lands.

A navigation lens identity combines one exact structural subject identity with
one view-facet registry identity:

```text
NavigationLensIdentity
  Subject  StructuralSubjectIdentity
  Facet    ViewFacetId
```

The registry owns the stable facet identity; Inspection Subject Navigation
owns the exact subject binding. `type.api` on two exact Types therefore names
two navigation lenses, while Library Metadata and Type Metadata can share a
label without sharing either facet or navigation identity. Consumers treat the
combined identity as opaque and never reconstruct it from kind, display text,
or active UI state. This is gated by
`NavigationLensRecommendationTests.LensIdentity_BindsExactStructuralSubjectAndFacet`.

### Snapshot

One navigation snapshot contains:

| Field | Purpose |
| --- | --- |
| Generation | Identifies an action publication and scopes action IDs and snapshot-relative commands; not a semantic revision |
| Workspace | Binds the session, every subject, descriptor, action, lens, basis, and diagnostic to one exact isolation boundary |
| Active package occurrence | Names exact Package focus or provenance when Package participates; absent for unrelated direct or Ecosystem-to-Library routes |
| Active subject | The one committed Workspace, Ecosystem, Package, Library, Type, or Member |
| Active route | The complete exact Workspace-rooted route to the active subject |
| Type-inventory Library context | Scopes Type navigation independently of the active subject |
| Retained-coordinate descriptors | Owner-ordered exact occurrences available from Workspace |
| Ecosystem descriptors | Owner-ordered exact registered Ecosystem occurrences available from Workspace |
| Hierarchy descriptors | Ordered descriptors for the active exact route and required Library-to-Member definition ancestry |
| Library descriptors | Aggregate, primary, then declaration order |
| Type and Member rows | Producer rows plus product activation state |
| Lens descriptors | Registry order, subject-scoped identity, and availability |
| Lens outcome | Effective identity or non-effective outcome, evaluation basis, and exact Registry evidence |
| Diagnostics | Partial evidence and scoped failures |

A consumer hierarchy descriptor classified as failed from inventory evidence
carries that exact typed evidence. Consumers render the descriptor-owned
evidence directly; they do not infer a slot association from the snapshot's
separate diagnostic inventory.

The semantic snapshot is the state lineage's only committed subject and lens
state. It includes complete descriptors, retained context, diagnostics, and
exact evaluation evidence, but excludes opaque action-publication identity.
Semantic revision advances exactly when that complete semantic value changes.
Renewing consumed actions for retry may instead publish a new generation with
the same semantic revision. The publication receipt is the composite
`NavigationPublication(Revision, Generation)`, not either component alone.

One lineage is bound to one exact Workspace realization for its lifetime.
Workspace binding is carried transitively by every subject identity, and
therefore by every subject-bound lens and evaluation basis. Navigation never
posts or reconciles a subject or accepts an action or restoration payload
from another Workspace. Only the host's current product-issued state supplies
retained prior state; an old state value does not authorize replacing that slot.

A lens outcome retains one evaluation basis:

| Basis | Retained input |
| --- | --- |
| Recommendation | Exact subject, preferred role, and complete target-aware Registry options |
| Exact request | Exact subject-bound navigation lens identity and exact Registry result |

Descriptor-bearing `Available`, `Unavailable`, and `Failed` exact results match
the requested subject kind because the Registry produces them only after
structural applicability succeeds. `Inapplicable` may describe another kind;
retaining that cross-kind descriptor is the exact evidence for rejecting the
request rather than treating it as unknown.

An effective outcome carries the selected exact navigation lens. A
non-effective recommendation outcome carries no invented lens identity; a
non-effective exact-request outcome retains the requested identity without
making it effective. This basis is product state used by reconciliation, not a
host hint. Recommendation posts a recommendation basis whether it selects a
lens or not. An explicit lens command posts an exact-request basis even when
it selects the same lens, recording that subsequent refresh must preserve the
exact request rather than resume automatic fallback.

The implemented basis shapes are gated by
`NavigationLensRecommendationTests.LensOutcome_RetainsRecommendationOrExactRequestBasis`.

### Descriptor states

Available descriptors carry an exact target and either `Current` or an opaque
generation-scoped action ID. Unavailable and failed descriptors carry no
target.

| State | Meaning |
| --- | --- |
| Available | An exact target can be activated |
| Pending | Owner evaluation or realization has not settled |
| Unavailable | Successful settled evaluation proved that no target exists now |
| Failed | Availability could not be established |
| Selection required | Choices exist, but policy forbids an implicit default |

`Selection required` is used for Member context when choices exist but no
Member is committed. It is neither valid-empty nor failure.

Every bounded Type and Member inventory row is preserved in producer order and
wrapped with the same activation classification. Navigation does not create a
second inventory or omit rows because of host filters.

### Action IDs

Interactive consumers receive opaque action IDs for non-current available
Workspace, Ecosystem, Package, Library, Type, and Member descriptors. Action IDs
are scoped to one exact Workspace and generation and are distinct from
structured identities.

Stale, foreign-Workspace, unknown, or duplicated action IDs produce typed
rejection without semantic snapshot change. Ordinary current results still
issue fresh effect authority. A consumed advertised action can be renewed for
retry by a new action generation without changing semantic revision; the old
action remains unusable. Canonical product peers may submit structured
identities through typed seams; browser display text never becomes a command
currency.

Retained-coordinate descriptors separately carry an owner-issued exact
`WorkspacePackageOccurrence`, including its typed Package descriptor, and owner order
from
[Workspace Scope and Expansion](workspace-scope-and-expansion.md), current
realization status from
[Artifact acquisition and workspace
composition](artifact-acquisition-and-workspaces.md), and an optional
Navigation-issued activation action. Navigation resolves the action to the
exact occurrence; the host never submits a package key or display label. An
artifact-owner loading status maps to `Pending` activation, retains the
owner's typed status and evidence, and carries no activation action. A failed
status maps to `Failed` activation and likewise carries no activation action.
Current `Ready` occurrences omit activation.

Activation status is independent of exact occurrence presence in the complete
owner-issued inventory. When the current retained occurrence is being
re-realized without a membership or identity change, `Pending` or `Failed`
keeps its exact logical occurrence, posted Package subject, descendant
subject context, and typed owner evidence but carries no current artifact
realization reference or activation action. Neither status runs
occurrence-first correspondence, fallback, or truncation, and neither pretends
that a retired artifact generation remains consumable. Only absence of that
exact occurrence from the complete inventory enters the
replacement-or-Workspace branch; #5584 owns protected result consumption for
the non-`Ready` occurrence.

Admission and Close remain Artifact Acquisition concerns. Root removal,
replacement, invalidation, and effect disposition are Workspace Scope and
Expansion results. Navigation applies the reconciliation rule below and #5584
owns protected result consumption. This design consumes only a posted
complete inventory and any exact requested occurrence supplied through those
contracts; it does not acquire a separate successor-selection policy.

## Product policy

### Initial subject

The #7301 target selects Workspace when a fresh active Workspace has no exact
subject request, regardless of inventory cardinality. An explicit Ecosystem
activation selects that Ecosystem without implicitly acquiring or selecting a
child. An explicit direct Library activation selects that exact Library.
Package entry may continue to use the Package-local Library recommendation
below after the caller explicitly selects one exact Package occurrence.

When no subject is committed and one exact retained-coordinate occurrence is
already active, recommendation order is:

1. Library, using the recommendation below.
2. The occurrence's exact Package.

Type and Member are never implicit subjects. A retained Type cursor does not
make Type the active subject.

When no retained-coordinate occurrence is active, Workspace is selected,
whether the inventory contains zero, one, or several entries. Navigation never
chooses an inventory entry from cardinality or order. Restoration or an
activation action supplies the exact active occurrence; only that explicit
input establishes it. When the operation supplies no exact subject request,
Navigation applies the recommendation above only inside that occurrence. The
CLI consumer in #5513 and canonical restoration must supply an exact occurrence
before expecting a lower initial subject.

Library choice is independent of Type count, accessibility, UI filters, search
text, display labels, and arrival order. Type inventory retains its producer
order for explicit navigation; it does not choose the initial subject.

Initial recommendation does not rank Types. When retained-context derivation
requires the highest-ranked trustworthy Type for inventory context, it uses
these tiers, not as a replacement for a missing selected Type:

1. Primary Library and default accessibility.
2. Other Library and default accessibility.
3. Primary Library and non-default accessibility.
4. Other Library and non-default accessibility.

Within a tier, Libraries use primary-then-declaration order and Types use the
inventory producer's deterministic navigation order. UI filters, search text,
display labels, and arrival order never participate.

A trustworthy candidate from a successful participant may be selected when
another participant failed; every participant failure remains visible. If no
producer can vouch for a candidate, Type availability is failed rather than
delegated to the consumer.

### Initial aggregate and Package

Library/API recommendation selects:

1. The available Package-scoped `All libraries` aggregate.
2. The exact Package when no aggregate is available.

The aggregate contains every admitted Library from one PackageHouse-selected
compile projection in the exact `PackageLibraries` basis. Its cardinality may
be one or many; cardinality never changes its identity into a one-Library
subject. The former primary and declaration-order Library preferences do not
participate in initial subject selection.

Participant rejection, decode failure, or inspection failure remains attached
to the aggregate result. Healthy participant evidence may remain usable, but
the snapshot and every projection identify the result as partial; Navigation
never turns incomplete evidence into a complete successful aggregate.
Package-only occurrences, including the tools-v2 pointer-package case
implemented by #4829, select Package with the selection explanation visible.

Explicit subject requests take precedence over this recommendation:

- **Exact Library** consumes one owner-issued exact selected-asset resolution
  against the Package's selected projection. An available admitted Library
  identity activates that one-Library subject. A missing, foreign, unavailable,
  or failed resolution returns its typed non-success without choosing the
  aggregate or another Library.
- **Namesake Library** compares the normalized Package ID with each admitted
  Library's owner-issued assembly simple name, ignoring case. One available
  match activates that exact one-Library subject only when identity evidence is
  complete. Zero matches with complete evidence is unavailable; multiple
  matches is ambiguous; an unresolved candidate identity is failed because it
  could change either conclusion. Every non-applied outcome retains the
  ordered candidates and participant evidence and never falls back to
  declaration order, the aggregate, or Package.
- **Package, Type, Member, and restored subject** requests keep their existing
  exact precedence and do not run Library recommendation.

Hosts own syntax such as `--library` and `--namesake-library`, but they submit
the corresponding typed gesture and render the returned outcome. Exact token
resolution remains a shared selected-asset operation; hosts do not turn token
text into subject identity. They also do not repeat namesake matching.
Navigation never derives a namesake from an asset path, file stem, display
label, or package-relative text.

One Navigation evaluation consumes one selected framework projection. A
coordinator requesting multiple frameworks presents separately associated
aggregates; it cannot merge their Library identities or API evidence into one
`All libraries` subject.

The browser adoption in #6098 uses the existing `defaultAssemblyId` projection
for fresh package entry, including opening retained packages through Search.
Explicit Package, Library, Type, Member and inspector destinations, and
restored workspace/history state, take precedence. Package remains explicitly
reachable and retains its full Library inventory. The existing Library Overview
is the browser's entry inspector; this slice does not adopt the separate
Registry-backed lens-recommendation protocol below.
Root-only `NoCompileAssets` and `EmptyCompileGroup` outcomes open Package with
their explanation visible. Failed selection is not treated as an empty package.
Browser entry and restoration are gated by
`inspect-web/browser/library-hierarchy.package-loading.spec.ts`; root-only and
failed selection modeling is gated by `test/package-acquisition.test.ts` in
that host.
This is the pre-#7318 default-entry adoption, not completion of #5510/#5511's
broader snapshot and result-authority migration. The #7428 successor retires
its primary-Library preference after consuming this aggregate recommendation.

### Bounded subject inventory classification

Navigation classifies one bounded API-surface result over one exact Library
inventory basis before snapshot-relative descriptors are composed:

```text
NavigationLibraryInventoryBasis
  = OneLibrary(exact Workspace-bound Library occurrence)
  | PackageLibraries(
      exact Package occurrence and Artifact Root correspondence,
      exact PackageHouse compile realization receipt,
      ordered selected assets and admitted Library participant outcomes)
```

The first arm serves direct and Ecosystem-routed Library subjects without
Package ancestry. The second preserves the existing one-Library and
`All libraries` behavior inside one exact Package. This owner defines no
Workspace-wide or Ecosystem-wide Type aggregate.

The Package-bound basis is one owner-issued association. Its occurrence's
Artifact Root correspondence, House acquisition generation and coordinate,
realization request and requested/selected framework, selected asset IDs, and
admitted Library package associations must all describe the same realization.
Participant outcomes exact-join that admitted Library basis by owner-issued
acquisition registration. A foreign Workspace, occurrence, settlement,
generation, framework, asset, basis, reordered, duplicated, or unexplained
missing outcome is invalid input before recommendation or narrowing rather
than evidence about subject availability. A direct Library basis does not
manufacture Package correspondence.

The generation-free classification follows this table:

| Producer evidence | Type inventory outcome |
| --- | --- |
| One or more returned Types with exact definition identity | `Available`; retain every exact Type and Member row in producer order plus all peer evidence |
| Complete successful production with zero Types and no inspection failures | `Unavailable` |
| No exact Type plus participant rejection, participant failure, inspection failure, missing exact Type or unresolved projected-Member declaration identity, or projection omission | `Failed` with the original typed evidence |
| Exact Types plus any of those failures | `Available` and partial; retain the exact rows and original typed evidence |

Projection truncation never proves that an omitted Library is empty. A returned
Type without exact `MetadataTypeDefinitionName` is retained as identity-failure
evidence and is not reconstructed from display text, metadata token, or list
position. A Member projected onto another Type resolves its
`DeclaringTypeDefinitionName` against exact Type rows in the same Library. One
unique match retains the producer row beneath its containing Type while its
`MemberSubject` binds the declaration Type and the declaration-scoped
`MemberAnchor`, exact-joining the declaration Member by its producer-issued
metadata token. Missing identity, no returned exact declaration, or multiple
matching Type or Member rows retains the complete producer row as typed failure
evidence and emits no Member subject. Classification never parses display or
canonical declaring text as lookup identity. Returned exact rows remain
trustworthy when another row or participant fails; failure does not erase
positive evidence.

Every admitted Library remains an ordered aggregate member and an eligible
target for explicit exact or namesake narrowing. No individual Library is an
implicit initial-recommendation candidate. Exact returned Type rows remain
inventory candidates for retained-context ranking; they are not implicit
subjects. Classification does not commit the recommendation, choose an active
subject, compose `Current` or `Selection required`, mint generation-scoped
actions, or produce a navigation snapshot.

This classification is gated by
`NavigationSubjectInventoryTests.EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity`,
`ProjectedMemberFromRealProducer_BindsDeclarationTypeAndAnchor`,
`ProjectedMemberWithoutTypedDeclaringIdentity_FailsClosed`,
`ProjectedMemberWithUnreturnedDeclaringType_FailsClosed`,
`ProjectedMemberWithAmbiguousDeclaringType_FailsClosed`,
`ProjectedMemberWithoutDeclarationMemberIdentity_FailsClosed`,
`ProjectedMemberWithUnreturnedDeclarationMember_FailsClosed`,
`ProjectedMemberWithAmbiguousDeclarationMember_FailsClosed`,
`ProjectedMemberLookup_DoesNotUseDeclaringText`,
`SuccessfulProducerRows_AreTrustworthyDespitePeerFailure`,
`CompleteSuccessfulEmptyInventory_IsUnavailable`,
`NoCandidateWithIndeterminateProducer_IsFailed`,
`ProjectionTruncation_NeverProvesUnavailability`,
`ProducerEvidence_IsRetainedWithoutTranslation`,
`InitialCandidates_ContainOnlyTrustworthyExactRows`, and
`InventoryJoin_RequiresExactParticipantRegistration`, and
`Inventories_PreserveExactPackageAncestryAndSequenceEquality`.

The current one-Library ranking is gated by
`NavigationInitialSubjectRecommendationTests.InitialRecommendation_PrefersLibraryThenPackage`
and `LibraryRecommendation_UsesPrimaryThenProducerOrderRegardlessOfTypes`.
Those gates describe pre-#7318 behavior and retire with its implementation.
The aggregate-first replacement remains **unverified** and requires
`InitialRecommendation_PrefersAggregateThenPackage`,
`InitialRecommendation_AggregateCardinalityDoesNotSelectOneLibrary`,
`InitialRecommendation_ExcludesIndividualLibraryCandidates`,
`PackageLibrariesBasis_RequiresExactOccurrenceRealizationAndParticipantCorrespondence`,
`PackageLibrariesBasis_RejectsCrossGenerationFrameworkAndSettlementPairingBeforeRecommendation`,
`ExactLibraryNarrowing_UsesOnlyExactSelectedLibraryIdentity`,
`ExactLibraryNarrowing_NonSuccessDoesNotFallback`,
`NamesakeLibraryNarrowing_UsesOwnerIssuedAssemblyIdentity`,
`NamesakeLibraryNarrowing_ZeroOrMultipleMatchesDoNotFallback`,
`NamesakeLibraryNarrowing_UnresolvedIdentityFailsClosed`,
`NamesakeLibraryNarrowing_RetainsCandidatesAndParticipantEvidence`,
`AggregateRecommendation_UsesOneSelectedFrameworkProjection`, and
`SeparateFrameworkSelections_NeverMergeAggregateIdentity`.
`InitialRecommendation_NeverChoosesTypeOrMember` remains a neighboring gate.
Candidate coordinate, Library, Type, primary-role, and accessibility
consistency remains gated by
`CandidateConstruction_RejectsInconsistentOwnerIssuedEvidence`. The bounded
classification above supplies exact Library and Type candidates and retains
availability and failure evidence. These gates apply only after one Package
occurrence is selected; they do not choose among Workspace inventory entries.

### Lens recommendation

Lens recommendation is a pure policy over one exact structural subject and the
target-aware options returned for that subject by one View Facet Registry
snapshot. It runs when an initial snapshot needs a lens and when activation or
reconciliation changes the exact subject without an explicit lens request.
Reactivating the unchanged current subject does not reset an effective lens. A
directly activated Member therefore receives the same owner-issued
recommendation as an initially recommended subject.

The implemented roles after the Registry adoption tracked by #5509 cover
Workspace, Package, Library, Type, and Member. Before any consumer exposes an
Ecosystem subject, a focused Registry adoption under #7301 must add the
Ecosystem overview role and one applicable descriptor backed by an
owner-defined host-neutral inspection. Navigation owns the preferred role, not
the facet's contents, execution, availability, or rendering.

The preferred semantic roles are:

| Subject | Preferred lens role |
| --- | --- |
| Workspace | Workspace overview |
| Ecosystem | Ecosystem overview |
| Package | Package overview |
| Type | Type API |
| Member | Member overview |
| Library | Library references |

The existing Compare descriptors require a separate target-aware applicability
decision before non-Package subjects are exposed. Navigation requires the
Registry result rather than inheriting applicability from the Library, Type,
or Member kind alone. The initial adoption keeps Compare applicable when the
exact subject identity retains an exact Package occurrence association,
including a package-origin Library reached by a direct route. It returns
`Inapplicable` for a source-native subject without Package association.
Supporting non-Package Diff or Clone requires separate Compare-owned execution
and Registry adoption; this design does not infer a baseline from Ecosystem or
Workspace context.

Recommendation applies these rules in order:

1. Find the one applicable option carrying the subject's preferred role.
   Missing preferred-role or empty option input is a typed Navigation policy
   failure; it does not silently turn registry order into policy.
2. If the preferred option is available, select its exact subject-bound
   navigation lens even when another available option appears first.
3. If the preferred option is unavailable or failed, select the first
   available option in registry order. Preserve the preferred option's
   non-success evidence and every returned descriptor.
4. If no option is available and any option is failed, return a failed lens
   outcome with every failed and unavailable result retained.
5. Otherwise return an unavailable lens outcome with every unavailable result
   retained.

Navigation consumes Registry order as returned and never re-sorts by role,
title, ID, or local host preference. Registry `Retired` is one unavailable
reason and follows the same fallback rule. Failure dominates unavailability
when no available fallback exists because Navigation cannot claim that no lens
is available while an applicable option could not be evaluated.

Recommendation never changes the active subject. An unavailable or failed
recommendation leaves that exact subject active and posts the corresponding
lens outcome with no effective lens.

The pure recommendation policy is gated by
`NavigationLensRecommendationTests.LensRecommendation_UsesPreferredRoleBeforeRegistryOrder`,
`LensRecommendation_FallsBackToFirstAvailableInRegistryOrder`,
`LensRecommendation_ConsumesRegistryOrderWithoutResorting`,
`LensRecommendation_RetainsAllRegistryOptionsAndEvidence`,
`LensRecommendation_MissingPreferredRoleFails`,
`LensRecommendation_EmptyOptionsFails`,
`LensRecommendation_FailedDominatesUnavailableWhenNoOptionIsAvailable`,
`LensRecommendation_AllUnavailableReturnsUnavailable`, and
`MemberRecommendation_UsesMemberOverviewRole` for the implemented subject
subset. Workspace and Package recommendation remain unverified until #5509
lands and replacement gates exercise those exact subject kinds.

### Type-inventory Library context

Type navigation has an explicit Library context:

| Active subject | Type-inventory context |
| --- | --- |
| Library | The active Library |
| Type or Member | The defining Library |
| Ecosystem | Defining Library of the deepest retained Type or Member; otherwise the deepest retained Library; otherwise none |
| Package | Defining Library of the deepest retained Type or Member; otherwise the deepest retained Library; otherwise available aggregate, then the highest-ranked trustworthy Type's Library, then primary or first available Library |
| Workspace | Defining Library of the deepest retained Type or Member; otherwise the deepest retained Library; if the retained route ends at Package, apply the Package rule; otherwise none |

If no context can be established, the context is unavailable or failed. The
context does not activate Library or promote Package or Workspace.
Ancestor context is derived from the retained exact route, Library inventory
basis, and applicable realized facts; it is not an independently selectable or
caller-authored Library. Workspace and Ecosystem never rank across sibling
Package or Library inventories to manufacture context.

### Aggregate and single-library capability

`All libraries` is the ordinary Package-backed aggregate Library/API subject,
not a client-side concatenation of independently rendered library pages.
Aggregate evaluation returns one owner-provided result that defines ordering,
identity, deduplication, and partial-failure behavior across the admitted
library set.

Each Library-scoped lens declares explicit aggregate and single-library
capability, together with a visible rejection reason when the current subject
arity is unsupported. This is symmetric: an aggregate-only lens does not
report one-library data, and a single-library-only lens does not report an
aggregate. A lens exposes only the arities it can genuinely support; capability
is never inferred from source family or transport method.

The active Library subject controls every Library-scoped lens:

- `All libraries` requests a coordinate-wide result over the complete admitted
  Library set of the active retained occurrence.
- An individual Library requests the same lens for only that Library.
- The selected Library subject persists when switching among returned Library
  lenses.
- A package-version or TFM change supplies an exact replacement occurrence to
  reconciliation, which decides whether that exact Library subject survives.

Navigation's `All libraries` evaluation never combines Libraries from sibling
coordinate occurrences or another Workspace. No Navigation snapshot combines
inspection evidence from sibling occurrences. The Workspace subject may expose
owner-issued retained-coordinate descriptors, but a future lens that needs
cross-occurrence inspection evidence requires its own focused owner contract
and Navigation adoption rather than an implicit exception here.

Because standalone lens activation requires the request's exact subject to
equal the snapshot's active subject (see
[Explicit activation](#explicit-activation)), standalone activation never
silently changes the Library subject to obtain a supported arity. An
unsupported arity is reported as `Unavailable` for that lens while the current
Library subject remains active and selectable for a supported lens.

A host may expose a compound subject-and-lens gesture when the user moves from
`All libraries` to a single-library-only inspector. Inspect Web defines that
gesture by selecting the first case-insensitive Package-ID namesake in its
alphabetically ordered Library inventory, or the first Library in that
inventory when no namesake exists, and then activating the requested inspector.
The transition runs only for user inspector navigation. Restoration,
rerendering, and asynchronous settlement do not repeat it. A later explicit
`All libraries` gesture remains active and receives the unsupported-arity
result, while moving to an aggregate-capable inspector retains the selected
exact Library rather than automatically returning to the aggregate.

## Activation and reconciliation

### Explicit activation

Subject and lens activation return one of these semantic outcomes:

| Outcome | State effect |
| --- | --- |
| Applied | Posts the exact requested subject or lens in a replacement snapshot |
| Unavailable | Applies no target or fallback; a completed exact lens evaluation posts its non-effective exact-request basis and evidence when either differs, while other operations post a replacement only when evaluation or reconciliation changes the snapshot |
| Rejected | Retains state because the command is stale, foreign, or invalid |
| Failed | A completed Registry or Navigation-policy lens evaluation posts its non-effective basis and evidence when either differs; Navigation preparation failure retains the prior snapshot |
| Superseded | Produces no visible effect because a newer explicit intent owns the session |

`Rejected` is an admitted Navigation result with ordinary result authority. A
protected-membership refusal belongs to the
[Scope-operation consumption boundary](navigation-scope-operation-consumption.md)
rather than this ordinary result algebra.

A subject-activation request carries one exact destination subject and one
complete exact route to it. Navigation rejects a foreign Workspace, invalid
relation kind, absent relation witness, route whose leaf differs from the
destination, or segment bound to another occurrence before lens recommendation
or fallback. Activating the same subject through another current exact route
may apply a route-only snapshot change; it does not manufacture another
subject or another subject-bound lens.

Standalone lens activation first requires the request's exact subject to equal
the snapshot's active subject. A mismatch is `Rejected` with the complete
request identity retained, before Registry resolution or fallback. It cannot
change the active subject. Canonical restoration's separately validated atomic
subject+lens pair remains governed by the fresh Workspace initialization
contract.

After that precondition succeeds, exact lens activation maps the View Facet
Registry result without fallback:

| Registry result | Navigation outcome |
| --- | --- |
| Available | `Applied` with the exact requested subject-bound lens, unless later Navigation preparation fails or the operation is superseded |
| Unavailable | `Unavailable` with the exact registry reason and a non-effective exact-request basis |
| Failed | `Failed` with the registry diagnostic identified as the source and a non-effective exact-request basis |
| Inapplicable | `Rejected` as structurally invalid for the exact subject |
| Unknown | `Rejected` as an unknown facet ID |

Every outcome retains the exact registry result and request identity, including
the absent descriptor in `Unknown`. A Navigation-owned preparation failure
after an available registry result remains distinguishable from a
Registry-owned failed result. Neither failure is rewritten as unavailable.

The pure exact-request boundary is gated by
`StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution`,
`ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback`, and
`ExplicitLensResolution_RetainsExactRegistryEvidence`. Snapshot replacement,
revision advancement, and publication of an exact-request basis remain
unverified until their separately named gates land.

A valid exact request that completes as Registry `Unavailable` or `Failed`
posts its exact-request basis and evidence whenever that replacement differs
from the prior snapshot and its bound subject remains active. It does not
retain an earlier recommendation basis.

An unavailable request never silently activates a sibling, ancestor, or
recommended subject. If the already committed subject became invalid
independently, automatic reconciliation may change it before the unavailable
outcome is returned. When that reconciliation falls back to a different subject,
its structural consistency takes precedence: the replacement snapshot posts
a recommendation basis for the fallback subject, while the operation result
still returns the original exact request's non-success outcome and evidence.
It never posts an exact-request basis bound to the inactive subject.
Coordinate inspector retention below transfers a previously retained request
through a resolved path; it does not retarget an in-flight standalone or
descendant subject-plus-lens command.

Outcome labels do not determine revision behavior. Every semantically changed
snapshot advances the state revision, including an unavailable result with
refreshed descriptors, a reconciled active subject, or a changed lens basis or
evidence. The same rule applies to a completed Registry or policy `Failed`
outcome. A non-success result shares the unchanged-snapshot outcome class only
when the complete snapshot is unchanged.

#### Exact Type selection in another retained Package

Issue [#7243](https://github.com/richlander/dotnet-inspect/issues/7243)
adds one direct-selection action for
[Inspect Web Type Find](inspect-web-type-find.md), whose end-to-end adoption is
tracked by [#6851](https://github.com/richlander/dotnet-inspect/issues/6851).
The action allows a person to choose an exact discovered Type in any ready
Package occurrence already retained by the same Workspace. It does not add,
replace, or correspond Package membership.

The managed consumer supplies one complete
`StructuralSubjectIdentity.TypeSubject`. Its ancestry binds the exact
Workspace, Package occurrence, acquired Library registration, and structured
Metadata Type name. Navigation publishes an opaque action only while that
occurrence is ready in the current complete Scope snapshot. Action publication
is a state transition bound to the current Navigation publication; a stale
publication, foreign Workspace, absent occurrence, or non-ready occurrence
returns a typed non-success and publishes no action.

Submitting the action validates its session, generation, source subject,
Workspace, occurrence, and exact target before gathering destination facts.
Navigation then evaluates the requested occurrence directly and requires the
exact Library and Type to occur once in its trustworthy inventory. Display
text, Package coordinate equality, assembly simple name, and locator result
ordinals are not action identity or fallback inputs.

| Destination result | Navigation result and state |
| --- | --- |
| Exact Library and exactly one Type are available | Publish one complete snapshot with that occurrence and Type active; run recommendation only for that Type |
| Occurrence or Type is absent with complete evidence | `Unavailable`; retain the current snapshot |
| Exact Type identity occurs more than once | `Ambiguous`; retain the current snapshot |
| Library ancestry does not match the destination occurrence | `Rejected`; retain the current snapshot |
| Inventory cannot establish absence | `Failed` with its evidence; retain the current snapshot |
| Action is stale, foreign, duplicated, or source-mismatched | `Rejected` before destination preparation; retain the current snapshot |
| Superseded by a newer explicit intent | `Superseded`; publish no visible effect |

This is direct user selection, not retained-coordinate variation. Navigation
does not inspect the prior subject for correspondence and does not publish a
Package or recommended default-Type snapshot before the selected Type. One
semantically changed successful completion advances the semantic revision once
and uses the existing complete-snapshot posting and acknowledgement
protocol. Selecting the already-active exact Type is an applied semantic no-op:
it preserves the complete current snapshot, including an exact lens basis,
and returns fresh effect authority without advancing the semantic revision,
under the ordinary unchanged-snapshot rule.

#### Atomic descendant subject and lens activation

Issue [#6490](https://github.com/richlander/dotnet-inspect/issues/6490)
adds one product-owned request for a current subject that must activate an
exact descendant with an exact destination lens. Its first retained consumer
is Library-to-Type and Type-to-Member drill-down in
[Inspect Web Compare Experience](inspect-web-compare-experience.md), under the
end-to-end tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213). The
stateless CLI consumer is tracked by #5513 under #5512.

The host-neutral request binds:

```text
DescendantSubjectLensRequest
  Source       exact current structural subject
  Destination  NavigationLensIdentity
```

The destination lens already binds its exact descendant subject and exact
Registry facet. A retained interactive session issues an opaque
generation-scoped action ID bound to the complete request for an available
owner-issued descendant row. A canonical stateless product peer may submit the
structured pair through the typed evaluation seam. Browser display state never
becomes request identity.

The destination must be in the same Workspace as its source and must be an
eligible descendant admitted by that exact row and relation witness. The first
retained consumer uses Library-to-Type and Type-to-Member edges. A Package
occurrence must also match when the source Library basis is Package-bound; a
direct or Ecosystem-routed Library requires no Package. Callers do not
construct or broaden the relationship from metadata, display text, hierarchy
position, or route shape.

For one-Library sources, an eligible Type retains that exact Library as its
defining Library. For `All libraries`, each eligible Type row names one exact
constituent Library from the aggregate's complete admitted Library set; the
Type keeps that concrete defining-Library identity rather than acquiring an
aggregate parent. Applying the action posts the destination Type's defining
Library as hierarchy and Type-inventory context. A Type-to-Member action
requires the Member's exact declaring Type to equal the source Type.

Submitting the opaque action begins one explicit Navigation intent. Navigation
validates the action's session, generation, source subject, destination
relation witness, any applicable Package occurrence, and exact subject-bound
lens before Registry resolution. A stale, foreign-Workspace, foreign-basis,
duplicated, source-mismatched, or non-descendant action is `Rejected` without
Registry evaluation, recommendation, correspondence, or fallback.

Stateless evaluation validates the same exact source, destination, Workspace,
occurrence, and descendant relationship without issuing retained action or
effect authority. It returns the same semantic mapping and complete evaluated
snapshot as data, while retained execution alone may post that snapshot.

After validation, Navigation resolves the destination facet against the exact
destination subject. It never activates the subject first and never runs lens
recommendation for that destination:

| Destination Registry or preparation result | Navigation result and state |
| --- | --- |
| `Available`, with successful Navigation preparation | `Applied`; publish one complete replacement snapshot whose active subject and effective lens equal the exact destination pair |
| `Unavailable` | `Unavailable`; retain the current pair and return the exact request and Registry evidence |
| `Failed` | `Failed`; retain the current pair and return the exact request and Registry diagnostic |
| `Inapplicable` or `Unknown` | `Rejected`; retain the current pair and return the exact Registry evidence |
| Navigation preparation failure | `Failed`; retain the current pair and identify Navigation as the failure source |
| Superseded by a newer explicit intent | `Superseded`; publish no visible effect |

Here, "retain the current pair" means that this action does not post
either requested half. The ordinary retained-session result may still carry a
newer complete snapshot with `Synchronization required` when product state was
committed by another operation; the consumer posts that current snapshot
without presenting this descendant request as applied.

An applied action advances the state revision once and produces one canonical
subject-and-lens transition. The Navigation Consumer applies its existing
explicit-action history rule to that one result, so the Browser pushes one
entry and never records an intermediate recommended lens. The action carries
no host presentation mode, query input, or renderer state.

This action is general Navigation capability, not a Compare-specific command.
Any future consumer must supply one owner-issued descendant row and exact
destination lens under the same rules. Ordinary subject activation without an
exact lens remains recommendation-driven, and standalone lens activation
continues to require the requested subject to be current.

Selecting Workspace changes only the committed active subject. It preserves
the prior exact route as inactive retained context when its owner-issued
subjects and relations remain current, allowing a later exact action to return
without reconstructing ancestry. It never changes membership, registration,
or an occurrence implicitly.

Activating an exact retained occurrence is a coordinate request, not
display-label or tab selection. It restores an explicitly supplied exact
subject when valid; otherwise it runs initial recommendation only within that
occurrence.

Selecting Package directly keeps the same exact occurrence and posts that
Package subject. Selecting a Library does not also select a Type. Selecting a
Type or Member directly returns its complete Workspace-rooted route and
required Library-to-Member definition ancestry. Package appears only when the
exact selected route contains it.

Activating a different exact subject without an explicit lens runs lens
recommendation for that subject. A prior lens is never carried to a different
subject merely because its registry facet ID or structural kind matches.

Every structured subject request and restoration payload carries the session's
exact Workspace transitively through subject identity; every action is scoped
to it explicitly. A foreign-Workspace value is rejected before Registry
resolution, correspondence, or fallback.

### Reconciliation

| Current subject | Reconciled subject |
| --- | --- |
| Workspace | Workspace |
| Ecosystem | Retain while the same exact registration occurrence remains present; otherwise select Workspace |
| Package | Retain while the same exact occurrence remains present; replace a lost route only from complete current relation evidence, otherwise select Workspace |
| All Libraries | Retain when aggregate remains available; otherwise the exact Package |
| One Library | Retain while the same exact admitted occurrence remains available; otherwise select the nearest valid exact ancestor on its route, using Package aggregate only for a Package route |
| Type | Retain when available; otherwise its defining Library, then the nearest valid exact route ancestor |
| Member | Retain when available; otherwise containing Type; if that Type is unavailable, apply the Type rule |

Navigation reconciles one retained subject and route with one route-first
algorithm:

1. **Establish the Workspace.** Every retained subject, relation, replacement,
   and correspondence must belong to the state lineage's exact Workspace. A
   foreign value is rejected rather than considered for fallback.
2. **Resolve the route.** Retain each exact subject and relation witness that is
   still current. Package-bound route replacement uses the existing
   occurrence-first correspondence only when the input supplies an exact
   replacement Package occurrence. Another subject replacement requires its
   owner's exact correspondence and complete current relation witnesses. If a
   route disappears while its leaf remains exact, Navigation may use only a
   complete replacement route supplied by current owner evidence. It never
   chooses a route from labels, inventory order, or prior history.
3. **Resolve definition descendants.** Starting at the resolved exact Library,
   resolve Type and Member in definition order. Unchanged content uses exact
   availability; replacement uses typed correspondence. During Package
   replacement, a paired entry Library is provisional context: an exact
   forwarded Type may require its actual defining Library instead, under
   [Forwarded API ancestry](#forwarded-api-ancestry). This is not permission to
   replace an active Library or retain a Type beneath its forwarding facade.
4. **Apply one fallback.** At the first unresolved subject or route segment,
   select the table's nearest valid exact ancestor and truncate every lower
   node. Package aggregate is eligible only inside a resolved Package route.
   Missing, ambiguous, refused, or failed correspondence follows the same rule
   with its diagnostic. No fallback crosses the exact Workspace.
5. **Complete the snapshot.** Preserve Workspace as active when it was selected.
   Otherwise use the resolved active subject or the single fallback result,
   rebuild its contiguous exact route and hierarchy descriptors, derive
   Type-inventory Library context, and reconcile the active subject's lens
   basis.

For example, `Package -> Library -> Type -> Member` with Package active retains
a correspondable complete path across an exact replacement occurrence. A missing
Member truncates the path to Type while Package remains active. The identical
path with Workspace active produces the same retained result while Workspace
remains active. The active subject no longer controls whether the path receives
same-occurrence or replacement reconciliation.

An unchanged `Workspace -> Library -> Type -> Member` route refreshes without
Package correspondence. If its exact Library remains admitted, Type and Member
use their ordinary exact availability. If the Library occurrence is replaced,
only owner-issued Library correspondence and a complete replacement route can
retain the descendants.

No sibling Type or Member replaces a missing selected Type or Member, even
when it has the same display name or ranks first. This ancestor fallback
applies to same-occurrence refresh and coordinate replacement; incomplete
evaluation remains distinct from confirmed absence. Inventory refresh never
promotes an explicitly selected Workspace, Ecosystem, Package, or Library to
Type. Navigation never chooses a sibling occurrence when the current occurrence
is absent. It consumes only exact replacement and relation evidence supplied by
the evaluation input. Otherwise it falls back through the exact route to
Workspace. This removes the browser's package-key-based replacement choice
tracked by #5510 and #5511.

Lens reconciliation follows the retained evaluation basis:

- a recommendation-basis outcome, effective or non-effective, reruns
  recommendation for the resolved subject against its refreshed complete
  Registry options; a recommended fallback is not promoted to explicit intent;
- an exact-request-basis outcome, effective or non-effective, re-resolves its
  exact subject-bound lens identity when the subject is unchanged;
- when coordinate variation resolves the active subject in the replacement
  occurrence without subject fallback, Navigation reissues the retained exact
  inspector request as specified below; and
- when the active subject changes by fallback, Navigation runs recommendation
  for that fallback subject. An independent subject activation also retains
  its existing recommendation rule unless it supplies an exact lens request.
  Canonical restoration retains its separate atomic exact-pair contract.

This lets a recommendation recover when refreshed facts make a facet available
or replace a fallback with the now-available preferred role, without turning an
explicit request into a different lens. Every replacement outcome retains its
new basis and complete evidence.

### Retained-coordinate variation

The Package-specialized branch of route resolution uses typed owner-issued
correspondence when the retained Package moves between exact occurrences inside
one Workspace:

| Resolution | Result |
| --- | --- |
| Exact subject resolves and is available | Resolved subject |
| Member missing, Type resolves | Resolved Type |
| Type missing, defining Library resolves | Resolved defining Library, never a sibling Type |
| Library missing | Available aggregate, then the new occurrence's exact Package |
| Correspondence missing, ambiguous, refused, or failed | Apply the unresolved node's level fallback inside the already resolved ancestor, truncate lower nodes, and retain the diagnostic |

For a forwarded Type, the resolved ancestor can change from entry Library A'
to defining Library B' only under the policy below. Type resolution alone is
not exact Type correspondence.

Display text, package ID alone, portable coordinate equality, assembly name,
token, and ordinal are not correspondence.

For an unchanged occurrence, failure to evaluate reconciliation retains the
current snapshot and surfaces failure. For a newly activated occurrence with
no prior retained path, Navigation runs independent initial recommendation;
correspondence is not invented. Failed lower levels remain failed.

Correspondence never crosses a Workspace boundary. A different exact Workspace
uses a different retained navigation session and independently selected or
restored state.

Membership-changing effects are outside this structural claim and are owned by
Workspace Scope and Expansion. #5584 owns their stale-work sequencing and
protected Navigation consumption. This design accepts only the exact posted
inventory and active-occurrence inputs. Non-invalidating realization-status
refresh remains ordinary maintenance.

#### Forwarded API ancestry

Within the exact replacement Package, Library pairing can establish A -> A'
while correspondence establishes that retained Type T has its exact available
counterpart T' in B', reached through A'. Navigation keeps those relations
distinct. A' remains entry evidence; B' is T's destination ancestry. There is
one retained path, not competing facade and definition paths.

The input is the Queries-issued correspondence result associated with the
retained source and the exact source/destination observations admitted for this
replacement. Consume its source, entry, terminal and native non-success
evidence under the correspondence owner's contract. Scope-result correlation,
generation/currentness and permission to post remain with #5584 and the
existing Navigation state protocol. Portable coordinates, names, equal MVIDs,
or a result from another replacement cannot supply that association.

Resolve the retained structural Type before retaining a Member beneath it.
Only an exact, available Type counterpart permits replacing the Library
context with B'. A Member additionally needs exact correspondence whose
destination declares that Member in the same exact T'. A successful Member
declaration lookup alone does not establish the retained Type's correspondence.
An inherited inventory row's display containment does not replace the
structural Member's declaring-Type identity.

| Active subject before replacement | Adoption when T' is exact and available in B' |
| --- | --- |
| Type T | Make T' active with B' as its defining-Library ancestry. Reconcile any lower Member beneath T'. |
| Member M in T | Retain M' only when its exact result belongs to T'; otherwise use T' as the Member-level fallback. |
| Package | Keep the exact replacement Package active and retain available lower context through B'. |
| Workspace | Keep Workspace active and retain available lower context through B' in the exact replacement occurrence. |
| One Library A | Keep available paired A' active. Retain lower context only if its exact destination belongs to A'; otherwise truncate Type/Member and explain the containment decision. |

The last row does not turn an exact match in B' into `Absent`, `Refused`, or a
new Library pair. Preserve the exact correspondence and route as evidence for
why that lower context was not retained. Library or aggregate non-availability
continues to use the existing Library-level fallback; a lower match does not
rescue or change an active Library. Aggregate activation and containment retain
their existing rules.

When the retained Type is exact but Member correspondence is absent, ambiguous,
refused, or failed, retain B'.T' and truncate the Member with its native result.
An active Member falls back to T'; an active Type, Package, or Workspace
remains active as above. This is not permission to select another Member.

Without an exact, available retained Type counterpart, the route alone does not
authorize retaining B' as a replacement Library or T' as a counterpart.
Apply Type-level fallback at available paired entry A', then the existing
aggregate/Package fallback if necessary. This includes a resolved terminal
whose strict Type correspondence fails, unresolved forwarding, or destination
availability that prevents adoption. An exact correspondence result remains
exact when a separate availability failure prevents its retention.
Preserve resolution, matching, and stage evidence: a dangling route, missing
binding, ambiguity, refusal, or evaluation failure is not an API-removal
verdict. No same-named Type elsewhere supplies the missing correspondence.

The complete snapshot uses the adopted destination path and its current
descriptors and inventories. Type-inventory context is B' for an active Type
or Member, or for their retained context beneath Package/Workspace; it stays
A' for the preserved active one-Library case. Inspector-request retention
below applies to an exactly retained active Type/Member even when its Library
changes. Member fallback to Type instead receives Type recommendation.
Lower-context truncation never transfers that lower subject's inspector to an
active ancestor or discards the ancestor's own exact request.

This policy changes neither correspondence nor admission of a destination
outside the producer's selected Package population. It does not authorize
dependency traversal, cross-Workspace retention, broader acquisition, or a
Browser ancestry repair. The stateless and retained Navigation consumers apply the
same policy; #5584 integrates it with protected replacement, #5513 exposes the
stateless completed result, and #5510/#5511 adopt descriptors and complete
results in Browser/Wasm. Completed host boundaries retain the existing
`InspectionEnvelope<TContent>` contract.

#### Coordinate inspector-request retention

Only a resolved active subject carries its retained exact inspector request
into a replacement occurrence. This includes an active Package when the
Package-specialized route branch establishes its exact supplied replacement,
and an active Library, Type, or Member when typed correspondence resolves its
complete ancestor path, including the defining-Library adoption above. A
failure below an active resolved ancestor truncates the lower context without
discarding that ancestor's inspector request.
An active Workspace keeps its own subject and lens independently.

The transferable intent is the existing complete opaque `ViewFacetId`, not
the old `NavigationLensIdentity`. Navigation binds that facet to the exact
resolved destination and asks Registry for exact resolution using destination
facts. It does not parse a facet prefix, choose an alias, compare inspector
labels, or reuse the prior target's availability, execution binding, or query
results. The new snapshot's exact-request basis is bound to the destination.

An available result makes that destination-bound lens effective. For
unavailable, retired, failed, unknown, or inapplicable resolution, the resolved
subject stays selected with no effective lens; the exact-request basis and
Registry evidence retain the original outcome distinction. Unknown and
inapplicable remain rejected requests, and failure is not described as
unavailability. No recommendation or neighboring inspector substitutes for
the request. Registry continues to own these classifications and whether
availability evaluation may perform work.

When correspondence does not resolve the active subject, use its structural
fallback and recommend for that fallback subject instead. Do not transfer a
Member inspector to its containing Type, a Type inspector to a Library, or a
request from an unrelated occurrence. No retained explicit request means
ordinary recommendation, not retention of whichever fallback inspector happened
to be effective. This policy does not alter direct exact-lens activation,
atomic descendant activation, canonical restoration, or cross-Workspace
selection.

## Retained navigation session

"Session" names one Navigation state lineage, not a retained service instance.
The immutable, opaque `NavigationState` holds the semantic snapshot, published
actions, acknowledged publication receipt, FIFO request identities, current
intent, active attempts, and effect/posting evidence. Product functions
own all policy. The host retains only the current state slot and executes
operations under the existing host and Workspace owners' authority.

The stateless transition boundary is:

| Operation | Navigation obligation |
| --- | --- |
| `Initialize` | Issue fresh state and a complete initial result for one exact Workspace realization |
| `Begin`, `BeginLens` | Validate an opaque action or exact product-peer lens request, supersede older explicit intent, and issue exact work or a current typed rejection |
| `Evaluate` | Consume the issued ticket and invocation-local prepared facts; return detached semantic and exact-lens evidence, never effect authority |
| `Complete` | Validate the ticket and current attempt; return the next state and operation-correlated result |
| `QueueMaintenance`, `QueueSynchronization`, `Advance` | Retain exact request identities, preserve maintenance FIFO, and issue work or dedicated synchronization when eligible |
| `RecordConsumerPosting`, `Acknowledge`, `Abandon` | Validate current effect authority; keep posting, receipt advancement, and debt-preserving release distinct |
| `Cancel` | Settle only the exact cancelled request; do not manufacture a successful evaluation or discard another queued request |
| `CanCommit(current, transition)` | Accept only the exact current-state object from which that transition was computed |

The host serializes the short current-slot check and replacement, not fact
gathering. It commits `Begin` before executing issued work, evaluates outside
that critical section, then completes against the current slot. A transition
computed from a replaced slot cannot be committed, even when both states have
equal semantic revisions and generations. The host does not merge competing
states or reconstruct the product's admission policy.

Each product-issued work ticket binds the exact session, Workspace, request,
attempt, intent, and snapshot/publication basis. Completion rejects foreign
Workspace/session tickets, an evaluation from a different ticket, and a stale
attempt. A later explicit intent makes an older explicit result `Superseded`
without authority. Stale maintenance completion discards only the attempt:
the same request keeps its FIFO position and must gather again under fresh
host operation authority against the then-current product state.

This boundary consumes admission, execution, and authority from their existing
owners; a ticket is correlation data, not permission to acquire or inspect.
`NavigationScopeOperations` composes that ticket with the existing scoped Root,
Scope, correspondence, and restoration producers for #5584 without introducing
a new architecture owner.

The bounded ordering model is
[`NavigationSession.tla`](models/inspection-subject-navigation/NavigationSession.tla).

The model establishes these design guarantees:

- every admitted explicit subject, lens, retained-coordinate, or restoration
  request receives a product-issued monotonic intent token;
- a newer admitted explicit intent supersedes older explicit results and in-flight
  maintenance results, while each same queued maintenance request survives,
  rebuilds from the replacement revision, re-gathers its facts, and remains in
  its original admission order;
- standalone maintenance is admitted in request order, not completion order;
- every queued maintenance request is retained and its own exact identity is
  eventually admitted;
- maintenance cannot post during unresolved explicit work or unconsumed
  visible effects;
- every admitted result receives exact session, state-revision, intent, and
  effect-epoch authority;
- every semantically changed snapshot advances the state revision regardless
  of its outcome label;
- retry action renewal may advance generation alone; the receipt and
  consumer posting distinguish that publication from its predecessor;
- every current result carries the complete current snapshot and identifies
  whether the retained consumer must synchronize it before acknowledgement;
- consumer posting and product acknowledgement are separate state
  transitions, and each authority must be posted under its exact effect
  epoch before acknowledgement;
- acknowledgement advances the product-owned receipt only after that current
  posting;
- abandonment never advances the receipt, including when the consumer
  posted the snapshot but lost authority before acknowledgement;
- every bounded model synchronization request retains its identity through
  an intervening acknowledgement and settles under dedicated fresh authority,
  without a product-side retry ceiling;
- stale or foreign authority cannot authorize a consumer-visible effect;
- prerequisite failure terminates the explicit operation without inventing a
  navigation result; and
- acknowledgement or abandonment releases queued maintenance.

A retained consumer treats tokens and authority as opaque. It validates
authority against the host's current product state before applying a result and again
before each deferred consumer-visible effect. Earlier validation is not
continuing authority.

The four-part effect authority remains session, semantic revision, intent, and
epoch. Its epoch is bound to exactly one publication, including generation;
generation-only renewal requires fresh authority. It is not a fifth
caller-supplied authority component.

Retained operations read the current snapshot from the explicitly passed
product state, not a UI snapshot. Standalone evaluation has no implicit
cross-command state. `SnapshotAuthority.tla` models this custody distinction;
exact object-identity commit races and full work-ticket validation remain
implementation-gated, not model-proved.

### Consumer synchronization

One retained navigation state records the composite publication of the snapshot
last acknowledged by its retained consumer. This is a product-owned receipt,
not a caller-supplied prior snapshot. The consumer neither orders revisions nor
uses them as command identity.

Consumer posting is separate from that receipt. Applying a result records
the complete snapshot and exact effect epoch posted by the consumer, but
does not advance the product-owned receipt. Acknowledgement requires that the
consumer posted the result under the current authority's exact epoch.

Every current explicit or maintenance result carries the session's complete
current snapshot and one typed disposition:

| Disposition | Consumer obligation |
| --- | --- |
| Current | The product-owned acknowledged consumer receipt already names this result's exact semantic revision and action generation |
| Synchronization required | Post the complete result snapshot before acknowledging its authority |

The disposition is independent of semantic outcome. A rejected, failed,
aborted, or unchanged-unavailable result is still `Synchronization required`
when an earlier applied or maintenance result advanced the session before the
consumer posted it. The consumer presents the current semantic outcome only
after synchronizing the complete snapshot, so descriptors, generation-scoped
actions, diagnostics, and lens state come from one publication. Equal semantic
revisions alone do not establish synchronization. `Current` still requires
posting evidence under this result's fresh epoch before acknowledgement.

Acknowledgement confirms consumption of the result snapshot named by the
current authority and advances the product-owned consumer receipt. The session
rejects acknowledgement while synchronization is required and incomplete.
Abandonment releases the current authority but does not advance the receipt;
the debt survives supersession, destination destruction, and remount, including
when destruction occurs after posting but before acknowledgement.

A retained consumer may request synchronization without submitting a subject,
lens, retained-coordinate, or restoration command. The session returns the
latest complete current snapshot with fresh current authority and no
semantic navigation change. If standalone maintenance is already queued, its
eventual current result may discharge the same debt without changing request
order. An implementation may settle the pending synchronization request on that
acknowledgement, or preserve its exact identity for a dedicated response after
the queue drains. #6113 implements the latter: even when an intervening
maintenance acknowledgement makes the receipt `Current`, the queued request
receives its own fresh authority. The representative model follows that path;
it does not require the alternate acknowledgement-discharge implementation.
Repeated remounts may request fresh authority again after
abandonment; the product contract imposes no retry ceiling.

A newer current result is also a synchronization vehicle. Product-side discard
of older superseded work publishes no authority, but the current result's
disposition is computed from the unchanged consumer receipt. If the consumer
still lags, even a non-posting semantic outcome requires the current complete
snapshot to be posted before acknowledgement.

This owner does not decide how a host renders the synchronization, classifies
browser history, or focuses a remounted surface. It supplies the complete
snapshot, typed disposition, and current authority needed for that owner to act.

`NavigationSession.tla` does not model external Workspace membership effects.
Its opaque `coordinate` intent covers Navigation-local coordinate activation
and variation under ordinary latest-admitted-intent supersession. Workspace
scope-operation results are owned by Workspace Scope and Expansion; their
protected Navigation consumption is #5584.

## Fresh Workspace navigation initialization

The implemented Package-backed initialization is the current subset. After
Definitions constructs a fresh unpublished Workspace and publishes its
complete explicit Package membership, it supplies Navigation with that exact
Workspace, zero or one exact retained occurrence context, and the optional
exact active subject and lens requested inside it. Every supplied identity was
issued within the new Workspace, and Navigation validates only their internal
consistency there.

The retained occurrence context is independent from the active subject. It
contains:

- the exact retained occurrence and its Package;
- one contiguous optional exact retained Library, Type, and Member path beneath
  that occurrence.

An explicitly selected Workspace may therefore retain one complete occurrence
context without making any descendant the active subject. Two Workspace-selected
snapshots with the same occurrence but different retained Type or Member
contexts remain distinct restoration inputs. No active occurrence means no
retained occurrence context.

Inspection Subject Navigation independently retains that requested payload,
requires every identity in the retained context to share one exact occurrence
ancestry and form one contiguous path. An active Workspace may retain that
independent path. Any requested non-Workspace subject must equal one exact node
of the retained path, not merely share its occurrence. Navigation derives the
Type-inventory Library context from that path and current realized occurrence
facts through [Type-inventory Library
context](#type-inventory-library-context); it is not supplied independently by
the canonical-state owner. When active subject is absent, retained context may
contain only the exact occurrence and Package; a lower retained Library, Type,
or Member path is rejected as ambiguous before recommendation. Package-only
context permits initial recommendation inside that exact occurrence; no
retained context selects Workspace. The lens identity's exact subject must
equal the requested subject. A path/subject mismatch, subject-less lower path,
internally inconsistent context, or subject/lens mismatch fails before Registry
resolution and aborts initialization. Navigation publishes one complete
snapshot inside the new Workspace when structural preparation succeeds and the
optional exact Registry request is `Available`, `Unavailable`, or `Failed`.
The latter two retain the exact request basis and Registry evidence with no
effective lens; they remain complete, postable Navigation snapshots.
Registry `Unknown` or `Inapplicable`, incomplete structural evidence, invalid
requested subjects, and absent requested subjects outside the Package-only
recommendation form produce typed non-prepared results with no Navigation state
or effect authority. Definitions closes that unpublished Workspace, and
supersession prevents an older attempt's Workspace from becoming active. The
focused local state machine is
[`AtomicRestoration.tla`](models/inspection-subject-navigation/AtomicRestoration.tla).

The #7301 target generalizes that runtime input from one Package occurrence
path to one complete exact `StructuralSubjectRoute`. A restored Ecosystem,
direct Library, or Package-backed subject must first resolve every subject and
relation witness inside the fresh Workspace. Navigation accepts no serialized
runtime identity and does not infer a missing segment. An explicitly selected
Workspace may retain one independently resolved exact route without making its
leaf active. Workspace Definitions remains the owner of portable fields,
resolution, and complete restoration; this design adds no packet shape or
encoding requirement.

This owner does not install the new Workspace or coordinate its
lifetime. Complete Workspace construction and result classification belong to
[Workspace Definitions](workspace-definitions.md); the retained host owns the
current-authority collection publication and active-identity selection.
Navigation owns only the new Workspace's
internally complete current snapshot.

Selecting an already admitted coordinate, Library, Type, or Member in
Spotlight uses ordinary Navigation inside the active Workspace and never
enters this construction path. The
[Spotlight destination-activation
owner](inspect-web-spotlight-destination-activation.md) separately classifies
registration-covered and uncovered destinations and consumes Navigation only
through owner-issued actions and initialization inputs. This document does not
define that Spotlight orchestration. The current implementation still delegates
source-native framework Library activation to a Browser-owned path. The #7301
target instead admits an exact Workspace-bound Library subject once its owner
supplies the required occurrence and route witnesses; it does not add a
user-facing Platform subject.

The [Workspace Definitions version-2
shape](workspace-definitions.md#complete-committed-views) represents an
explicitly selected Workspace and carries an optional retained occurrence and
descendant context independently from the active subject. #5525 owns the
record, JSON, composition, and portable-resolution implementation.
Section, body, source-target, and other portable state remain outside this
owner.

## Consumer contract

### Retained consumers

A retained consumer submits subject action IDs with their issuing generation
and submits lens identities through Inspection Subject Navigation. It treats
intent tokens and effect authority as opaque, applies no effect without current
authority, consumes the result's typed synchronization disposition, and
performs no subject or lens fallback after a non-applied outcome. It posts
the complete result snapshot before acknowledging `Synchronization required`,
may request fresh synchronization authority while its receipt lags, and
abandons authority it can no longer consume so queued maintenance can proceed.

Inspect Web presentation and accessibility belong to
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md).
Focus, acknowledgement timing, and surface-destruction behavior belong to
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md), with
the migration historically tracked by
[#4917](https://github.com/richlander/dotnet-inspect/issues/4917).

### Canonical state

The canonical-state owner consumes structured subject and lens identities.
Definitions and plans remain detached. Navigation state, action IDs, receipts,
work tickets, and retained-session authority are never serialized or reused in
a new realization. The fresh-initialization contract above is implemented by
`NavigationTransitions.PrepareRestoration`; complete Definitions and host
adoption remain separate scope.

### Other hosts

Another retained host may use the same session model without adopting browser
layout. A stateless CLI may use recommendation and reconciliation without
retaining a navigation session.

## Workspace-rooted graph adoption

Issue [#7301](https://github.com/richlander/dotnet-inspect/issues/7301) is the
overall tracker. The current plan has sixteen focused stages:

1. Lock this Navigation-owned subject, identity, route, policy, and evidence
   contract.
2. Have the Ecosystems catalog define the `.NET Runtime` Ecosystem contribution
   as the .NET runtime platform population plus the `System.` Package Prefix,
   with `.NET Runtime` as its user-facing identity.
3. Have the Workspace registration and ecosystem-handoff owners issue exact
   Workspace-bound Ecosystem occurrences and contribution-relation witnesses.
4. Have the responsible admission owner issue exact Workspace-bound Library
   occurrences and direct, Package, and Ecosystem relation witnesses.
5. Supply one host-neutral Ecosystem Overview inspection over the exact
   registration occurrence and admitted-descendant descriptors through the
   repository's `InspectionEnvelope<T>` boundary.
6. Have View Facet Registry adopt the Ecosystem Overview descriptor, preferred
   role, exact applicability, and execution binding.
7. Have View Facet Registry make an explicit target-aware applicability
   decision for existing Compare descriptors; the initial path keeps
   source-native subjects without Package association inapplicable.
8. **Partially implemented:** first consume accepted Focus witnesses as exact
   point-in-time current contribution evidence without Package ancestry. Then
   replace Navigation's package-only subject implementation with the closed
   subject graph, route state, activation, reconciliation, and remaining
   Release gates.
9. Have Workspace Definitions resolve portable subject intent into exact fresh
   Workspace subjects and routes without serializing runtime identities.
10. Adopt the same Workspace-rooted subjects and routes in the CLI through the
   existing Workspace top-level inventory selection receipt, Markout, and
   structured-output path.
11. Adopt the product-issued routes, actions, and outcomes in the Inspect Web
   Navigation Consumer, resolving Workspace inventory rows through their
   exact top-level inventory selection receipt.
12. Have Inspect Web Workspace Editing expose configuration as an explicit
   action on the singular live Workspace rather than as a structural subject.
13. Have Inspect Web Saved Workspaces expose the plural saved-definition
   collection and lifecycle separately from the active Workspace subject.
14. Have Inspect Web Navigation Presentation make singular Workspace the
   visible inspection root and compose the separately owned Configure
   Workspace and Workspaces actions or surfaces without redefining them.
15. Have Spotlight destination activation add typed Ecosystem destinations and
   explicit registration effects, then retire its Browser-local framework
   Library activation path after shared Navigation covers it.
16. Include the CLI and website behavior in a separately authorized product
   release and production-site deployment.

Each stage owns only its component's adoption decisions. This document does not
define those adjacent internals. Stages may split when an owner demonstrates
more than one independently coherent claim; the tracker must then update the
count rather than hide the additional work.

The package-only `StructuralSubjectIdentity` is an alternative architecture,
not a compatibility contract. Stage 8 replaces it in place once all current
Package behavior has equivalent gates. The Browser-local framework Library
route remains visible migration state until stages 11 through 15 replace and
retire it; no design-only claim presents that path as already shared.

## Verification

### Executable design models

| Model | Checked design properties |
| --- | --- |
| `NavigationSession.tla` | Latest admitted Navigation-local explicit intent wins; semantic revision follows semantic snapshot change; retry publication can renew generation alone; composite receipt and exact-epoch posting govern acknowledgement; maintenance is request ordered; dedicated synchronization preserves exact request identity even after an intervening acknowledgement makes the receipt current |
| `AtomicRestoration.tla` | One exact requested subject+lens pair initializes atomically; failed or superseded initialization is not published |
| `SnapshotAuthority.tla` | Explicit host-current product state supplies retained prior state, never a consumer-supplied snapshot; applied lens results equal the independently retained request; stale or foreign authority is rejected |
| [`NavigationScopeOperationConsumption.tla`](models/navigation-scope-operation-consumption/NavigationScopeOperationConsumption.tla) | Protected acceptance precedes Scope submission; only the exact Scope association can publish and release; complete result membership, cancellation-control distinction, stale-work exclusion, requested-occurrence activation, and forwarded defining-Library context survive composition |

The model README records the TLC commands and scope. Model checking validates
these finite specifications, not the implementation.

Workspace isolation, structural ancestry, lens ranking, Registry-result
classification, and the exact subject-plus-facet identity structure are
intentionally absent from the models: subjects, snapshots, and lenses remain
opaque values there. The pure recommendation, mapping, identity-binding, and
Workspace-containment rules above are enforced by the implementation gates
below rather than claimed as model-checked behavior. Ancestor fallback and
coordinate inspector-request retention are likewise pure policy over those
values, not changes to the modeled ordering protocol; their protected
replacement gates are implemented. Forwarded ancestry is another pure
structural policy, not a new concurrency transition; its Release gates, not the
models, establish the implemented ancestry and correspondence properties.

The Workspace-rooted graph adds no second intent, publication, or
acknowledgement protocol. Subject plus route remains one immutable semantic
snapshot value under the existing ordering models. Before stage 4,
`NavigationSession.tla` must exercise a route-only applied change, stale
relation action rejection, and relation removal that cannot leave an invalid
posted route. Those bounded results will establish model behavior, not
implementation conformance. The #7301 model extension and all implementation
properties remain **unverified**.

### Required implementation gates

The eventual subject-navigation implementation must include named gates for:

- `WorkspaceSubject_BindsOneExactWorkspaceOccurrence`
- `KindVocabulary_IsClosedAndWorkspaceRooted`
- `KindVocabulary_IncludesWorkspaceEcosystemPackageLibraryTypeAndMember`
- `Identities_BindExactOwnerIssuedComponents`
- `SubjectIdentity_ExcludesRouteIdentity`
- `EcosystemSubject_RequiresExactWorkspaceRegistrationOccurrence`
- `LibrarySubject_RequiresExactWorkspaceAdmissionOccurrence`
- `CurrentNavigationContributionPreservesExactFocusAdmission`
- `EqualTextRegistrationReplacementDoesNotReauthorizeContribution`
- `ForeignWorkspaceCannotConsumeContribution`
- `Construction_RejectsAbsentOwnerIssuedComponents`
- `Route_AllowsOnlyClosedTypedRelations`
- `Route_RequiresOneExactWorkspaceAndContiguousWitnesses`
- `Route_RejectsForeignWorkspaceAndMismatchedLeaf`
- `SameSubjectAcrossRoutes_PreservesSubjectAndLensIdentity`
- `EqualLibraryNamesFromDifferentSources_RemainDistinctSubjects`
- `DirectLibraryRoute_PreservesPackageProvenanceWithoutPackageAncestry`
- `EmptyEcosystemSubject_DoesNotAcquireOrInventChildren`
- `RouteLoss_UsesOnlyCurrentOwnerIssuedReplacement`
- `DotNetEcosystem_RoutesRuntimeAndEveryAdmittedSystemPrefixOccurrence`
- `EcosystemGrouping_PreservesTopLevelInventoryEntriesAndIdentity`
- `OneLibraryInventory_DoesNotRequirePackageOccurrence`
- `DirectLibraryDescendantActivation_UsesExactLibraryRelations`
- `NonPackageRefresh_PreservesExactSubjectAndRoute`
- `WorkspaceSubject_RetainsNonPackageRouteContext`
- `TypeInventoryContext_DerivesFromNonPackageRetainedRoute`
- `WorkspaceAndEcosystem_DoNotRankSiblingInventoriesForTypeContext`
- `PortableCoordinateAlone_CannotIdentifyRetainedPackageSubject`
- `WorkspaceSubject_PreservesActiveOccurrenceAndDescendantContext`
- `AncestorTypeInventoryContext_DerivesFromDeepestRetainedNode`
- `WorkspaceSubject_ExposesCoordinatesWithoutNavigationAggregation`
- `PackageSubject_RequiresPackageOccurrence`
- `RetainedCoordinateActivation_UsesExactOccurrenceAction`
- `ForeignWorkspaceSubjectActionAndRestoration_AreRejected`
- `SnapshotComposition_RejectsForeignWorkspaceEvidence`
- `SnapshotComposition_RejectsForeignOccurrenceEvidence`
- `RetainedCoordinateDescriptor_FailureHasEvidenceAndNoActivation`
- `RetainedCoordinateDescriptor_PendingHasEvidenceAndNoActivation`
- `RetainedCoordinatePending_PreservesPostedContextUntilSettled`
- `RetainedCoordinateFailure_PreservesPostedContextWithEvidence`
- `RetainedCoordinateCorrespondingGenerationRefresh_PreservesPackageSubject`
- `ZeroOneOrManyOccurrences_DoNotInventActiveOccurrence`
- `RetainedContextReconciliation_ResolvesOccurrenceThenPathThenActiveSubject`
- `CoordinateVariation_NeverCrossesWorkspaceBoundary`
- `MemberIdentity_BindsExactDeclaringTypeAndAnchor`
- `FreshWorkspaceWithoutExactSubjectRequest_SelectsWorkspace`
- `ExplicitEcosystemActivation_DoesNotSelectAChild`
- `EcosystemRecommendation_UsesEcosystemOverviewRole`
- `EmptyEcosystemOverview_RemainsAvailable`
- `SourceNativeSubjectCompare_IsInapplicableWithoutChangingSubject`
- `PackageOriginDirectRouteCompare_UsesExactPackageAssociation`
- `ExplicitDirectLibraryActivation_SelectsExactLibrary`
- `InitialRecommendation_PrefersAggregateThenPackage`
- `InitialRecommendation_AggregateCardinalityDoesNotSelectOneLibrary`
- `InitialRecommendation_ExcludesIndividualLibraryCandidates`
- `PackageLibrariesBasis_RequiresExactOccurrenceRealizationAndParticipantCorrespondence`
- `PackageLibrariesBasis_RejectsCrossGenerationFrameworkAndSettlementPairingBeforeRecommendation`
- `ExactLibraryNarrowing_UsesOnlyExactSelectedLibraryIdentity`
- `ExactLibraryNarrowing_NonSuccessDoesNotFallback`
- `NamesakeLibraryNarrowing_UsesOwnerIssuedAssemblyIdentity`
- `NamesakeLibraryNarrowing_ZeroOrMultipleMatchesDoNotFallback`
- `NamesakeLibraryNarrowing_UnresolvedIdentityFailsClosed`
- `NamesakeLibraryNarrowing_RetainsCandidatesAndParticipantEvidence`
- `AggregateRecommendation_UsesOneSelectedFrameworkProjection`
- `SeparateFrameworkSelections_NeverMergeAggregateIdentity`
- `TypeRecommendation_UsesPrimaryLibraryAccessibilityAndProducerOrder`
- `InitialRecommendation_NeverChoosesTypeOrMember`
- `EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity`
- `ProjectedMemberWithoutTypedDeclaringIdentity_FailsClosed`
- `SuccessfulProducerRows_AreTrustworthyDespitePeerFailure`
- `CompleteSuccessfulEmptyInventory_IsUnavailable`
- `NoCandidateWithIndeterminateProducer_IsFailed`
- `ProjectionTruncation_NeverProvesUnavailability`
- `ProducerEvidence_IsRetainedWithoutTranslation`
- `InitialCandidates_ContainOnlyTrustworthyExactRows`
- `InventoryJoin_RequiresExactParticipantRegistration`
- `LensIdentity_BindsExactStructuralSubjectAndFacet`
- `LensOutcome_RetainsRecommendationOrExactRequestBasis`
- `LensRecommendation_UsesPreferredRoleBeforeRegistryOrder`
- `LensRecommendation_FallsBackToFirstAvailableInRegistryOrder`
- `LensRecommendation_ConsumesRegistryOrderWithoutResorting`
- `LensRecommendation_RetainsAllRegistryOptionsAndEvidence`
- `LensRecommendation_MissingPreferredRoleFails`
- `LensRecommendation_EmptyOptionsFails`
- `LensRecommendation_FailedDominatesUnavailableWhenNoOptionIsAvailable`
- `LensRecommendation_AllUnavailableReturnsUnavailable`
- `MemberRecommendation_UsesMemberOverviewRole`
- `StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution`
- `ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback`
- `ExplicitLensResolution_RetainsExactRegistryEvidence`
- `DescendantLensAction_BindsExactSourceDestinationAndFacet`
- `DescendantLensAction_RejectsStaleForeignAndNonDescendantBeforeRegistryResolution`
- `DescendantLensResolution_MapsEveryRegistryOutcomeWithoutRecommendation`
- `StatelessAndRetainedDescendantLens_UseSameExactMapping`
- `AppliedDescendantLens_PostsExactPairInOneSnapshot`
- `NonAppliedDescendantLens_PostsNeitherRequestedHalf`
- `SupersededDescendantLens_PublishesNoEffect`
- `ExactNonSuccess_PostsExactRequestBasis`
- `NavigationPreparationFailure_RemainsDistinctFromRegistryFailure`
- `NavigationPreparationFailure_RetainsSnapshotAndRevision`
- `RecommendationBasis_RefreshRerunsRecommendation`
- `ExactNonSuccessLens_RefreshReresolvesExactIdentityWithoutFallback`
- `ExactNonSuccessDuringSubjectReconciliation_PostsReplacementSubjectRecommendationBasis`
- `UnavailableDescriptor_HasNoTargetOrActionId`
- `ExplicitUnavailableTransition_DoesNotApplyFallback`
- `UnavailableReplacement_AdvancesStateRevision`
- `UnavailableUnchangedSnapshot_RetainsStateRevision`
- `UnavailableResult_CurrentRevisionMatchesRecordedResultRevision`
- `FailedReplacement_AdvancesStateRevision`
- `FailedUnchangedSnapshot_RetainsStateRevision`
- `FailedResult_CurrentRevisionMatchesRecordedResultRevision`
- `RetainedCoordinateVariation_UsesTypedCorrespondence`
- `LensReconciliation_PreservesExactSubjectScopedIdentity`
- `Reconciliation_MissingTypeFallsBackToDefiningLibrary`
- `CoordinateVariation_RebindsExactInspectorRequest`
- `CoordinateVariation_NonSuccessInspectorKeepsResolvedSubject`
- `CoordinateVariation_FallbackDoesNotTransferInspectorRequest`
- `CoordinateVariation_RecommendationBasisRemainsRecommendation`
- `CoordinateVariation_ForwardedApiAdoptsDefiningAncestry`
- `CoordinateVariation_ForwardedContextPreservesActiveAncestor`
- `CoordinateVariation_ForwardedMemberNonSuccessKeepsResolvedType`
- `CoordinateVariation_ForwardedTypeNonSuccessKeepsEntryAncestor`
- `RetainedSession_UsesCurrentSnapshotAsOnlyPriorState`
- `RetainedSession_BindsOneExactWorkspaceOccurrence`
- `RetainedSession_RejectsCallerSuppliedPriorSnapshot`
- `RetainedSession_RejectsSuppliedSameSessionSnapshotCustody`
- `NavigationStateTests.SameStateAndInput_ProduceEquivalentResultsWithoutChangingInput`
- `NavigationStateTests.InvocationAvailabilityTarget_IsNotRetainedByStateTicketEvaluationOrResult`
- `NavigationDetachmentTests.ArtifactBackedStateTicketsAndExactResults_DoNotRetainAcquisitionAuthority`
- `NavigationDetachmentTests.DetachedFailure_RefreshPreservesExactFailureIdentityAndSemanticRevision`
- `NavigationDetachmentTests.ErasingIdentity_PreservesExactRegistrationEquality`
- `NavigationStateTests.Complete_DuplicateCannotRepublishOrCommitAgainstReplacedCurrentSlot`
- `NavigationStateTests.Complete_RejectsWrongTicketAttemptSessionAndWorkspaceWithoutChangingState`
- `NavigationStateTests.Maintenance_RegatherPreservesRequestIdentityButRejectsRetiredAttempt`
- `NavigationStateTests.Initialize_EqualFactsSeedIndependentSessionIdentities`
- `NavigationStateTests.IndependentStates_InterleaveWithoutSharingHistoryOrAuthority`
- `NavigationSessionRegressionTests.DescendantNonSuccess_RetainsExactEvidenceUnderActionAuthority`
- `NavigationSessionRegressionTests.SupersededDescendantCompletion_DoesNotReplaceCurrentResolutionEvidence`
- `SuppliedPriorRejection_CorrelatesExactOperation`
- `AppliedResult_EqualsExactRequestedSubjectAndLens`
- `Maintenance_SerializesInRequestOrderAcrossCompletionTiming`
- `Maintenance_EveryQueuedRequestIsAdmittedByExactIdentity`
- `Maintenance_CannotPostDuringUnconsumedEffect`
- `StaleBasisMaintenance_SameRequestRebuildsRegathersAndIsAdmitted`
- `EffectAuthority_RequiresExactCurrentSessionRevisionIntentAndEpoch`
- `ConsumerSynchronization_DispositionComesFromAcknowledgedPublication`
- `NavigationSessionRegressionTests.PreparationNonSuccess_ReturnsFreshRetryWithoutSemanticRevisionChange`
- `NavigationSessionRegressionTests.RetryGenerationDebt_SurvivesAbandonmentWithoutSemanticRevisionChange`
- `ConsumerSynchronization_DispositionIsIndependentOfSemanticOutcome`
- `ConsumerSynchronization_NonReplacingSuccessorCarriesCurrentSnapshot`
- `ConsumerSynchronization_PostingDoesNotAdvanceReceipt`
- `ConsumerSynchronization_AcknowledgementRequiresCurrentEffectPosting`
- `ConsumerSynchronization_AcknowledgementRequiresPostedResult`
- `ConsumerSynchronization_AbandonmentPreservesDebt`
- `ConsumerSynchronization_RequestReturnsLatestSnapshotWithFreshAuthority`
- `NavigationSessionTests.Synchronization_WaitsForExplicitWorkAndQueuedMaintenance`
- `ConsumerSynchronization_RemountCanRequestAgainAfterAbandonment`
- `ConsumerSynchronization_EveryRequestSettlesByCurrentResult`
- `ConsumerSynchronization_MaintenanceOrderAndLivenessArePreserved`
- `ExternalIntentAbort_ReleasesMaintenanceAfterAcknowledgement`
- `CanonicalRestoration_PreparedPairEqualsExactRequest`
- `CanonicalRestoration_ExactRegistryStatusRemainsPrepared`
- `CanonicalRestoration_RejectsUnknownOrInapplicableExactLens`
- `CanonicalRestoration_RejectsMismatchedSubjectBoundLens`
- `CanonicalRestoration_RejectsExactLensWithoutSubject`
- `CanonicalRestoration_RejectsSubjectFromAnotherOccurrence`
- `CanonicalRestoration_RejectsForeignPreparedPackageFacts`
- `CanonicalRestoration_RejectsInconsistentRetainedOccurrenceContext`
- `CanonicalRestoration_RejectsSameOccurrenceSubjectOutsideRetainedPath`
- `CanonicalRestoration_RejectsSubjectlessLowerRetainedPath`
- `CanonicalRestoration_SubjectlessPackageContextRecommends`
- `CanonicalRestoration_ExactPackageRootIsPrepared`
- `CanonicalRestoration_NonReadyPackageFailsWithoutRegistryResolution`
- `CanonicalRestoration_DerivesTypeInventoryContextFromRetainedPathAndFacts`
- `CanonicalRestoration_WorkspaceSubjectPreservesDistinctDescendantContexts`
- `CanonicalRestoration_InventoryMissDistinguishesAbsentFromIncomplete`
- `CanonicalRestoration_IncompleteFailureEvidenceIsDetached`
- `CanonicalRestoration_TrustworthyRequestedRowSurvivesPeerFailure`
- `CanonicalRestoration_EqualInputsIssueIndependentStateAndAuthority`

The closed-kind, component-binding, and construction gates are updated in
place. Initial recommendation and coordinate reconciliation receive the
replacement gate names above. The old four-kind, Package-as-Root, and
same-coordinate expectations are not retained as parallel currencies. Existing
Type, Member, inventory, lens, and typed-correspondence witnesses remain
regression cases inside the new Workspace-rooted gates.

`LensRecommendation_UsesPreferredRoleBeforeRegistryOrder` is the role-policy
non-vacuity gate: its preferred available descriptor is deliberately not first
in Registry order, and replacing role selection with first-available selection
must fail it. `LensRecommendation_RetainsAllRegistryOptionsAndEvidence`
compares the complete result with independently retained input options,
including non-selected unavailable and failed peers that cannot affect the
chosen lens. The exact mapping gate covers all five Registry results, and its
evidence gate likewise compares each result with independently retained input
evidence rather than reconstructing expected evidence from Navigation output.
The cross-subject activation gate uses the same facet ID on two exact subjects
and requires rejection before a throwing Registry-resolution sentinel. It
compares the returned complete request identity with independently retained
input. The canonical mismatch gate independently retains the requested subject,
the differently bound lens, and the restoration operation identity; it requires
the correlated pair to abort before a throwing Registry-resolution sentinel.
The exact-non-success gate begins with a recommendation basis, submits an exact
request returning `Unavailable` and `Failed` in separate cases, and requires
the current replacement basis and evidence to equal the independent request
and Registry result before the refresh gate re-resolves that identity. The
subject-reconciliation gate invalidates the bound subject during those same
non-success cases and instead requires the current snapshot to carry the
fallback subject's independently computed recommendation basis while the
operation result retains the original exact-request evidence.
The preparation-failure retention gate starts with a current snapshot,
forces Navigation preparation to fail after Registry availability, and
requires the complete snapshot and revision to remain unchanged while the
result identifies Navigation as the failure source.

The descendant-action binding gate independently retains the source subject,
destination subject, facet, Workspace, occurrence, defining Library, and
issuing generation. Its rejection gate varies each currency and the eligible
descendant relation before a throwing Registry sentinel. It includes two
identically named Types in different Libraries under `All libraries` and
requires the selected row's exact defining Library to become hierarchy and
Type-inventory context. The exact-pair gate compares the current subject and
effective lens with that independent request after one applied result. The
non-applied gate covers unavailable, failed, inapplicable, unknown, and
Navigation-preparation failure and requires that neither requested half enters
the current snapshot. Existing retained-session authority and consumer
synchronization gates cover supersession, complete-snapshot posting, and
acknowledgement; this action introduces no second operation or partial
publication protocol. The exact pair and descendant relationship remain
**unverified** until these named Release gates land.

The retained-Type publication gate
`RetainedTypeAction_PublicationRequiresCurrentReadyExactOccurrence` varies the
Navigation publication, Workspace, occurrence, and realization status before
asserting that only the exact current basis publishes an action.
`RetainedTypeAction_SelectsExactTypeAcrossOccurrencesWithoutIntermediateState`
starts from one Type in occurrence A, publishes actions for exact Types in A
and B, selects B, and requires one completion whose occurrence, Library, Type,
and single revision advance are B's exact values.
`RetainedTypeAction_SeparatesEqualCoordinateOccurrences` keeps
equal-coordinate occurrences distinct.
`RetainedTypeAction_MapsUnavailableRejectedAmbiguousAndFailed` covers a
mismatched Library, missing Type, duplicate Type identity, and incomplete
inventory, while existing action-authority gates cover stale generation and
supersession.
`RetainedTypeAction_DestinationRealizationChangeIsTyped` covers a destination
that becomes Pending or Failed after publication, and
`RetainedTypeAction_AlreadyActiveTypeIsSemanticNoOp` fixes the unchanged-snapshot
revision behavior. The tests independently retain their input identities and
require the prior complete snapshot for every non-applied result.
`RetainedTypeAction_ActivatesRealSystemTextJsonObservation` exercises the same
transition over Metadata projected from the real `System.Text.Json` assembly.
These gates also use a throwing correspondence sentinel when that seam becomes
injectable; until then, the direct evaluation path and exact outcome assertions
gate the no-correspondence claim.

The implemented protected ancestor-fallback and coordinate-inspector cases for
issue #7061 are mapped to `NavigationCoordinateReplacementTests` in
[the protected-consumer design](navigation-scope-operation-consumption.md#production-implementation-gates).
They independently
retain source and destination subject identities and the requested facet, then
require the destination-bound exact basis and fresh Registry result for
Workspace, Package, Library, Type, and Member. Member loss beneath an exact Type
falls back to that Type without discarding an active ancestor's request.

The remaining planned #7061 gates above are **unverified** where they go beyond
that mapped coverage: ordinary-refresh ancestor fallback, every non-available
Registry arm (including retirement), ambiguous correspondence, and recovery of a
formerly unavailable recommended facet. The implemented replacement Registry
cases cover unavailable and failed exact requests, not those additional arms.
CLI and Browser adoption must preserve the same policy outcomes, fresh content,
and existing Package/Library experience.

The forwarded-ancestry gates use the pinned Avalonia pair and product
correspondence producer, not hand-authored successful correspondence, for the
exact Type/constructor and non-matching Member cases. They independently retain
source and destination identities and assert the complete ancestry, active
subject, Type-inventory context, inspector basis, and associated route/result
evidence.

The active-ancestor gate covers Workspace and Package retaining B'.T', and
active A retaining A' while discarding that same exact lower match with a
containment explanation. The Member non-success gate requires an independently
exact Type result before falling back to B'.T'. The small strict-correspondence gate also covers a resolved Type whose generic
constraints changed, with another available Type in its paired Library: it
falls back to that Library, not a sibling Type, and does not retain a Member.
The remaining Type non-success gates for unavailable destination evaluation
despite an exact match and an unrelated same-named Type remain **unverified**
unless mapped to an implemented Release gate.
Use proportional producer-backed boundary fixtures where the real pair does
not supply a case. Existing exact-inspector gates cover Registry non-success;
the #5584 correlation and supersession gates cover replacement publication,
not a new forwarding-specific scheduling protocol. CLI and Browser adoption
must preserve the same typed outcomes and fresh destination content.

## Acceptance cases

| Case | Expected result |
| --- | --- |
| Workspace selected with Ecosystem, Package, and direct Library inventory | Exact singular Workspace subject and owner-ordered descriptors; no Package is invented as active ancestry |
| Registered Ecosystem has no admitted packages or libraries | Exact Ecosystem subject with effective Ecosystem Overview; valid-empty admitted descendants are content, not unavailability, and no acquisition or child subject is fabricated |
| Framework Library activated directly from Spotlight | Exact `Workspace -> Library` route and source-native Library identity; no Package or user-facing Platform subject |
| Same exact Library activated through its Ecosystem | Exact Library and lens identities retained with an exact `Workspace -> Ecosystem -> Library` route |
| Package-origin Library activated directly | Exact Library occurrence retains Package provenance while the active route may be `Workspace -> Library` |
| Compare requested on a source-native Library | Registry `Inapplicable` is retained with the exact Library still active; no Package or comparison baseline is inferred |
| Compare requested on a direct package-origin Library | Applicability uses the exact Package association in subject identity, not whether Package appears in the active route |
| Two Ecosystems contribute the same exact Package | One Package subject with two exact available routes; Ecosystem labels do not duplicate Package identity |
| Framework and package Libraries share an assembly simple name | Distinct subjects because their owner-issued source occurrences differ |
| `.NET Runtime` contains runtime and two `System.Text.Json` Package versions | Three distinct observations and routes: one source-native Library plus two exact Package occurrences with their own Libraries |
| Workspace inventory reports the same `.NET Runtime` inputs | One Ecosystem entry retains its platform and `System.` populations while both Package occurrences remain separate top-level entries; Navigation grouping changes neither identity nor duplicate policy |
| Active Ecosystem registration is removed while its Library remains admitted | Retain the Library only through a current owner-issued replacement route; otherwise fall back to the last valid exact ancestor |
| Saved definition appears in Workspaces | No structural subject or Navigation action until Open constructs a fresh singular Workspace |
| Workspace selected with an active occurrence | Exact Workspace subject and ordered retained-coordinate descriptors; the active occurrence and its Package, Library, Type, and Member context remain available |
| Workspace selected without an active occurrence, with zero, one, or many retained entries | Exact Workspace subject with no invented coordinate or lower context |
| Package coordinate selected | Exact Workspace-bound Package ancestry; no tab or display identity participates |
| Package subject activated | Exact Package with Package Overview recommendation after #5509 |
| Exact Type selected in another ready retained occurrence | One complete result activates that occurrence, defining Library, and Type; no Package/default-Type intermediate snapshot |
| Equal-coordinate retained occurrences contain the same Type name | Each published action retains its exact occurrence and selecting either activates only that observation |
| Directly selected Type is missing, duplicated, mismatched to its Library, or lacks complete inventory | Typed non-success with the prior complete snapshot; no correspondence or name fallback |
| Active coordinate is absent without a supplied replacement | Workspace with no active occurrence |
| Active coordinate is absent with an exact supplied replacement | Occurrence-first correspondence and level-local fallback only inside that occurrence |
| Current retained coordinate is Pending during non-invalidating re-realization | Exact logical occurrence, posted Package subject, descendant subject context, and typed owner evidence remain without fallback or truncation; no current artifact realization reference or Navigation activation action is exposed |
| Current retained coordinate is Failed while its exact occurrence remains present | Exact logical occurrence, posted Package subject, descendant subject context, and typed owner evidence remain without fallback or truncation; no current artifact realization reference or Navigation activation action is fabricated |
| Foreign-Workspace subject, action, or restoration payload | Rejected before Registry resolution, correspondence, or fallback |
| Restoration occurrence and subject ancestry disagree inside one Workspace | Preparation aborts before Registry resolution |
| Restoration active Type and retained path name different Types in one occurrence | Preparation aborts before Registry resolution |
| Restoration omits active subject but supplies retained Library/Type/Member context | Preparation aborts before initial recommendation |
| Workspace restoration retains Type in Library L2 | Type-inventory context is derived as L2; no independent Library context is decoded |
| Package remains active with retained Type in Library L2 | Type-inventory context is derived as L2 before any Package-only fallback |
| Two Workspace-selected restorations retain different Type contexts | Distinct initialized snapshots preserve the exact independently supplied descendant context |
| Retained Member disappears while Workspace is active | Retained context falls back to the containing Type while Workspace and its lens remain active |
| Retained Member disappears while Package is active | Retained context falls back to the containing Type while Package and its lens remain active |
| Package O1 with retained Type/Member context resolves exactly to replacement Package O2 | Correspondable retained descendants resolve under O2 before invalid descendants are discarded |
| Package content or binding-context generation is replaced with equal logical correspondence | Same exact Package subject and occurrence; projection and actions refresh, retained descendants reconcile, and stale generation-scoped actions are rejected |
| Package coordinate or selection target changes so logical correspondence differs | Membership-changing replacement supplies a new occurrence and Package subject; correspondence and level-local fallback govern retained descendants |
| Coordinate variation within one Workspace | Typed correspondence or independent recommendation confined to the requested occurrence |
| Coordinate variation across Workspaces | No correspondence; separate retained session and independently restored state |
| Active Type/Member moves from A through A' to exact B'.T' | Adopt actual B' ancestry and the exact Type/Member; reissue the active subject's inspector request against its destination |
| Package/Workspace active with the same forwarded lower path | Preserve active ancestor and its lens policy; retain exact lower context through B' |
| Library A active with an exact lower match in B', distinct from paired A' | Keep A' and its inspector active; truncate Type/Member, retaining exact-match evidence and the containment explanation |
| Forwarding and Type correspondence succeed but Member correspondence does not | Retain B'.T'; active Member falls back to Type recommendation, with native Member non-success preserved |
| Forwarding resolves but strict Type correspondence does not | Type-level fallback at paired entry A', not an inferred Library pair with B'; preserve route and strict non-success |
| Forwarding is unresolved or a same-named Type exists without the required route | Entry-ancestor fallback with native resolution evidence; no replacement Type invented |
| Coordinate variation resolves the active Package, Library, Type, or Member with a retained exact inspector request | Same opaque facet is resolved against the exact destination; the old subject-bound lens is not reused |
| Resolved coordinate subject has an unavailable or failed requested inspector | Resolved subject remains active with no effective lens and the exact request/result evidence; no recommended substitute |
| Resolved coordinate subject has an unknown or inapplicable requested inspector | Rejected inspector request remains visible on the resolved subject, with no effective lens or substituted inspector |
| Coordinate variation begins with a recommendation basis | Recommendation runs for the resolved subject; a previously recommended fallback is not explicit intent |
| Coordinate correspondence falls back from Member to Type | Type receives its own recommendation, never the Member's inspector request |
| Ordinary package | Package-scoped `All libraries` subject with the aggregate-capable recommended lens; exact Package remains explicitly reachable |
| Single-Library package | Package-scoped `All libraries` subject containing one Library; cardinality does not implicitly narrow |
| Package occurrence paired with another settlement, generation, or selected TFM | Invalid `PackageLibraries` basis rejected before recommendation, aggregate identity construction, or narrowing |
| Exact Library gesture | Exact selected one-Library subject, or typed non-success with no aggregate, sibling-Library, or Package fallback |
| Unique namesake gesture | Exact one-Library subject whose owner-issued assembly simple name uniquely matches the Package ID ignoring case |
| Missing, ambiguous, or indeterminate namesake | Typed unavailable, ambiguous, or failed result with ordered candidates and participant evidence; no declaration-order, aggregate, or Package fallback |
| Multiple framework selection | One separately associated aggregate per selected framework; no cross-framework subject or API merge |
| Preferred role is not first | Preferred available role, not the earlier available descriptor |
| Preferred lens unavailable | First available registry-ordered fallback with preferred evidence retained |
| No lens available and one evaluation failed | Failed lens outcome with all non-success evidence retained |
| Preferred role missing or options empty | Typed Navigation policy failure, never implicit first-option selection |
| Direct Member activation without a lens | Exact Member with Member Overview recommendation |
| Same facet on two exact Types | Two distinct subject-bound navigation lens identities |
| Lens request bound to another exact Type | Rejected before Registry resolution with active subject unchanged |
| Explicit inapplicable or unknown lens | Rejected with exact Registry evidence and no fallback |
| Library Type row with exact Type Compare lens | One applied snapshot contains that Type and `type.compare`; no Library-to-Type intermediate recommendation |
| `All libraries` has identically named Types in L1 and L2, and the L2 row is activated | Exact L2-bound Type, L2 hierarchy, L2 Type-inventory context, and `type.compare`; aggregate identity does not become Type ancestry |
| Type Member row with exact Member Compare lens | One applied snapshot contains that Member and `member.compare`; no Type-to-Member intermediate recommendation |
| Descendant lens unavailable, failed, inapplicable, unknown, or preparation-failed | Prior current subject and lens remain; the exact destination evidence is returned and neither requested half enters the current snapshot |
| Stale, foreign-occurrence, or non-descendant subject+lens action | Rejected before Registry resolution or recommendation |
| Descendant subject+lens action superseded by a newer intent | No visible effect from the superseded action |
| Stateless CLI evaluates the same exact descendant pair | Same exact Registry mapping and complete snapshot data as retained evaluation; no action ID, effect authority, history, or Compare mode |
| Failed recommendation becomes available on refresh | Recommendation reruns and posts the newly effective exact lens |
| Recommended fallback then preferred role becomes available | Recommendation replaces the fallback with the preferred exact lens |
| Explicit unavailable lens becomes available on refresh | Exact identity is re-resolved without considering a sibling fallback |
| Exact non-success while its subject disappears without correspondence | Result retains the exact request evidence; current snapshot uses the fallback subject's recommendation basis |
| Navigation preparation fails after Registry availability | Failed result identifies Navigation; snapshot and revision remain unchanged |
| Multi-library package | Package-scoped `All libraries` subject containing every admitted Library in the selected compile projection |
| Libraries with no Types | Package-scoped `All libraries` with its aggregate-capable References lens; Type is validly unavailable |
| Tools-v2 pointer package | Package with Package Overview; lower subjects unavailable |
| Primary Library has no default-accessibility Type | Package-scoped `All libraries` remains the recommendation; Type accessibility does not narrow the initial subject |
| Partial Type inventory | Deterministic successful candidate plus retained failures |
| Member disappears | Containing Type, never another Member |
| Type disappears with Library retained and other Types available | Defining Library, never another Type; missing lower context is truncated and its diagnostic retained |
| Coordinate correspondence is ambiguous | Level-local fallback inside the resolved ancestor, lower-path truncation, and retained diagnostic |
| Two lens requests complete out of order | Latest issued lens is final |
| Refresh and reconciliation complete out of order | Maintenance request order determines final snapshot |
| Coordinate acquisition fails | Prior snapshot retained; abort effect visible; maintenance eventually resumes |
| Canonical subject plus non-default lens | One complete initialized snapshot returns the exact requested pair with no partial result |
| Canonical subject plus lens bound to another subject | Preparation aborts before Registry resolution |
| Applied result is abandoned before consumer posting | Product retains the applied snapshot; consumer receipt remains behind |
| Applied result is posted then abandoned before acknowledgement | Consumer-posted state advances, but the product-owned receipt and synchronization debt do not |
| Non-posting successor follows an abandoned applied result | Successor carries the complete current snapshot with `Synchronization required` |
| Maintenance completes while the consumer lags | Current maintenance result carries the complete current snapshot and may discharge the lag without bypassing request order |
| Consumer requests synchronization after abandonment | Latest complete snapshot returns under fresh current authority with no semantic navigation change |
| Queued synchronization follows acknowledged maintenance | The same synchronization request returns a dedicated `Current` result under fresh authority |
| Retryable action completes without semantic snapshot change | Semantic revision is retained, actions renew under a new generation, and the old composite receipt is not current |
| Host supplies current product-issued state explicitly | Product transitions accept its retained basis; consumer snapshots remain inadmissible as retained state |
| Host slot changes after a transition was computed | `CanCommit` rejects the stale transition by exact state identity, independent of publication equality |
| Maintenance attempt becomes stale | Discard attempt evidence, preserve request identity and FIFO position, and re-gather under fresh host authority |
| Same saved plan is realized again | Fresh Workspace-bound Navigation state and authority; no live state restored from the plan |
| Consumer abandons synchronization and remounts | Receipt remains behind and a later request can obtain fresh synchronization authority again |
| Consumer acknowledges while still lagging | Acknowledgement is rejected and the product-owned consumer receipt does not advance |
| Snapshot contents return to an earlier value at a newer revision | `Synchronization required`; equal contents do not make generation-scoped state current |

## Non-goals

This design does not:

- define Workspace or retained-coordinate occurrence identity construction;
- define a universal portable identity for every coordinate or producer;
- make the Workspace subject or `All libraries` combine inspection results
  across retained coordinates or Workspaces;
- make the plural Workspaces collection, a saved definition, or an editor draft
  a structural subject;
- create an `All packages` structural subject;
- create `All ecosystems` or Workspace-wide `All libraries` structural
  subjects;
- define the Workspace owner's close or successor-selection policy;
- require every structural level to be visited;
- make arbitrary Library subsets structural subjects;
- select a default Member;
- make UI filters part of subject identity;
- define view-facet registry membership;
- define portable packet fields or browser-history policy;
- define lens contents or section execution;
- authorize acquisition or expensive inspection work; or
- add implicit session state to stateless commands.
