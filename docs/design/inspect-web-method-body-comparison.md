# Inspect Web Method Body Comparison

## Status and ownership

This is the target design for
[#5963](https://github.com/richlander/dotnet-inspect/issues/5963), the bounded
Browser adoption of local comparison in
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706), step 9.
The feature is implemented through the existing Source facade and the shared
direct-member query. The focused and published-Wasm gates below cover this
adopter; broader portable comparison remains separate.

Inspect Web Method Body Comparison is one focused feature owner. It owns the
explicit pair interaction, its managed feature projection, and the Method Body
Diff presentation. It does not own comparison algorithms, physical-method
resolution, navigation, transport, or operation lifetime.

The claim is:

> An explicitly requested same-assembly method pair produces a view associated
> with that exact ordered pair and the shared Queries outcome, preserving
> separate native C# and IL evidence and typed non-success.

## Consumer and basis

The consumer is a person inspecting a member who wants to compare its body
with another method in the same already-open implementation assembly.
Different names and declaring types are permitted; selecting the same method
twice is also valid. This is explicit comparison, not candidate discovery or
proof of semantic equivalence.

The corresponding CLI consumer is `match --body`, being implemented with
the shared query and adapter in
[#5925](https://github.com/richlander/dotnet-inspect/issues/5925).
The Browser follows that production cutover rather than introducing another
unused shared substrate.

Existing Browser member resolution, Source media presentation, and the
Type Source operation-authority adoption are local comparative evidence.
Source currently represents one endpoint, not an existing two-method diff.
The familiar explicit two-selection comparison is the interaction baseline.
This first feature deliberately uses a session-local dialog instead of a
new workspace lens or durable comparison route: it makes the bounded scenario
available without changing the navigation or workspace-packet contracts.

One pair-specific request/result view is the sufficient complexity.
Shared Queries and native producers already supply the association and
comparison; existing Browser owners supply execution and presentation lifetime.

## Consumed boundaries

| Owner | Consumed contract |
| --- | --- |
| [Direct-member query](direct-member-comparison.md#adapter-contract) | Two exact physical designations, same-method support, and designated rather than strict correspondence; Queries issues physical addresses through `AssemblyContextMethodAddressQuery` for this Browser consumer |
| [Local comparison publication](local-comparison-publication.md#result-contract) | Original query-origin or Research-terminal evidence associated with one invocation |
| Existing Browser implementation-member resolution and inspection scope | Reference/surface-to-implementation selection, retained participant access, and validated implementation body selection |
| [Operation authority](inspect-web-operation-authority.md) | Current-view publication, cancellation, supersession, disposal, and quiescence |
| [Managed operation bridge](inspect-web-managed-operation-bridge.md) | Generated feature transport, keyed cancellation forwarding, and managed release |
| [Shell interaction](inspect-web-shell-interaction.md) | Shared modal accessibility, Escape, and ordinary focus return |
| [Surface composition](inspect-web-surface-composition.md#contextual-working-surface-actions) | Contextual-action placement and responsive continuity |
| [Navigation consumer](inspect-web-navigation-consumer.md) | Ordinary member navigation, canonical location, and history |

These are dependencies, not additional normative owners of this feature.
The feature supplies its input and result meaning to their existing boundaries;
it does not change their identity, validity, cleanup, or replacement rules.

## Comparison contract

### Explicit pair interaction

Offer **Compare method bodies** as a contextual action for a selected physical
method or explicitly selected accessor/body. Surface Composition owns its
placement and responsive continuity, including the dedicated action region
when launched from full-area Source. It is not an Application menu item.
A selection without an available implementation target exposes its reason;
it must not silently select the first accessor or another overload.

The action opens a **Method Body Diff** dialog. Before is the launching
selection, identified by its full member identity and implementation assembly.
The user chooses After from the existing type/member/body inventory for that
same implementation assembly, with overloads and accessors distinguished.
The chooser does not change the underlying member navigation.

Both sides remain visible before submission and with the result. **Compare**
is explicit; focus movement, filtering, and changing the candidate do not run
decompilation. A same-method pair is not disabled as a presumed no-op.
A physical MethodDef without a body remains selectable for native
classification.

The dialog uses the shell's existing modal behavior, initially focusing the
After chooser and returning to its launch action on ordinary dismissal.
Changing the pair clears the old comparison before a new request can display
results. Closing the dialog disposes its feature operation session through the
existing authority boundary.

The pair and result are session-local dialog state, not a new navigation
subject or workspace packet. Opening or editing the dialog does not rewrite
the canonical location or history. A workspace/member navigation that replaces
the launching context ends the dialog through existing surface disposal.
Refresh and shared links restore ordinary inspection, not this transient pair.
Portable/restorable comparison remains separate broader
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083) work.

### Physical endpoint and query handoff

Managed code consumes both selections within one existing inspection scope
and resolves their implementation methods through the existing Browser
member-resolution boundary. A token from a reference/surface row is selector
input, not an asserted token in the implementation image.

Existing Browser resolution returns a validated implementation body token,
not a `MetadataMethodAddress`. The Browser cannot construct the missing
module association by opening a metadata reader below Queries.
`AssemblyContextMethodAddressQuery` supplies that Queries-owned projection
with this actual Browser consumer, as part of the existing adapter/host
delivery rather than another standalone substrate milestone. It returns the
existing typed assembly-context entry; its construction and failure semantics
remain Queries-owned.

The comparison consumes the resulting exact implementation participant and
owner-issued physical method association through that public Queries boundary.
Both endpoints must belong to the selected implementation assembly. Display
labels, bare tokens, or equal signatures in another image cannot substitute
for that association. An asserted physical address is never retargeted to make
the request succeed.

An unavailable context, unresolved body selection, wrong-image address, or
ambiguous designation stays visible non-success. This feature neither opens a
different assembly to rescue the pair nor fabricates `SubjectAbsent`.
Ordinary reference-to-implementation resolution remains with its existing
owner; new forwarding-root composition is outside this profile.

The managed feature invokes the shared query once for an accepted comparison,
requesting both local C# and IL mechanisms. It consumes
`LocalComparisonQueryResult`; it does not call legacy `CompareMembers`,
reinspect each endpoint, or construct a synthetic `ResearchComparison`.
The context owner retains acquired input lifetime. Queries and Research retain
their own access and stage cleanup responsibilities.

This feature's transport uses an empty `PackageId` to distinguish a retained
platform selection from a NuGet package. The version, framework, and assembly
remain the exact selected coordinates. Unlike existing Source calls that pass
a runtime pseudo-package ID, this request addresses a resident assembly
directly; it does not ask package acquisition to interpret that ID.
The retained lookup supports runtime and ASP.NET Core families only when those
coordinates identify one scope and one assembly coordinate. Ambiguity is
visible `ContextUnavailable`, not an arbitrary family choice or reacquisition.
This is a feature-specific convention, not a change to other Source exports.

### Operation and result association

Each submitted pair is the immutable input of one feature operation. The
operation-authority consumer associates its terminal publication with that
pair; mutable chooser values never supply labels for an older result.
Replacement, dismissal, cancellation, and late completion use the existing
authority and bridge contracts rather than a second request counter or
feature-owned cancellation registry.

Generated facade data is a typed projection of the shared query result.
It retains the ordered endpoint descriptions and physical associations,
query-origin versus Research-terminal category, and each requested mechanism's
endpoint states, comparison verdict, applicable aligned evidence, and failure
causes. The managed projection consumes original query evidence, not a
recreated operation identified by display text.

This first adopter follows the existing Source consumer's physical execution
placement. The current Worker binding is a diagnostic canary, not the host of
production Source operations. This feature does not migrate acquisition or
retained workspaces to another realm. Future placement changes consume the
[Worker runtime](inspect-web-worker-runtime.md) owner without changing pair
meaning or adding feature-owned lifetime machinery.

Inventory and comparison read already-retained inputs, so they use the keyed
managed bridge without entering the Source acquisition coordinator. That
coordinator's supersession is for acquiring Source; opening a local comparison
must not cancel the member's ongoing Source request. The Release
`BothExportsPreserveSourceAcquisitionAndReleaseOwnOperation` cases and real
dialog acceptance gate this coexistence.

Managed/transport failure or cancellation remains distinct from a query
result. Successfully transporting a query outcome does not mean the comparison
is exact or even completed. Research completed accounting can contain native
unavailability or failure; the feature preserves those distinctions.
An empty change list or missing diff is not a substitute for a native verdict.

Cancellation ends current publication under operation authority. No claim of
immediate physical cancellation of synchronous managed work is added here.
If a context becomes unavailable before query entry, show that outcome for the
requested pair rather than borrowing a replacement context.

## Method Body Diff presentation

The view names Before and After with their own identities. C# and IL have
separate status summaries and evidence regions. C# is the primary expanded
region; IL evidence may use ordinary disclosure, with its outcome visible
even while its rows are collapsed. There is no combined `IsExact` verdict,
and this view does not imply that structural matching was requested.

Exact native evidence can be displayed as exact under that mechanism.
`NoApplicableInput` is shown as not applicable, with its native reason;
unavailable, rejected, failed, and cancelled states remain distinguishable.
One non-success mechanism does not erase usable evidence from the other.

Structured C# line/body evidence and IL instruction/operand evidence reach
the facade before display lowering. The Browser feature owns its DOM lowering
for interactive paired columns and disclosure; this is a deliberate
host-specific rendering path, not CLI-output parsing. Reuse existing source
text rendering, highlighting, scrolling, and accessibility conventions.
Do not compute another text diff from displayed bodies or normalize code to
manufacture matching lines. The CLI continues to use shared Markout lowering.

Within Surface Composition's responsive constraints, the dialog preserves the
same typed Before/After association when its paired content stacks. Labels,
code, and diagnostics use the existing text-rendering
conventions. The shared shell owns modal behavior; this feature only supplies
its accessible title, controls, content, and loading/result announcements.

## Demo and gates

The published-Wasm acceptance uses
`Microsoft.Extensions.Primitives` 10.0.0 / `net10.0`:

```text
Inspect Microsoft.Extensions.Primitives -> StringSegment.Trim
Action: Compare method bodies

Method Body Diff
Before: StringSegment.Trim()
After:  StringSegment.TrimStart()      [choose method]
[Compare] [Close]

Research: Completed
C#: ProducedCSharp / NotExact          13 native rows
IL: ProducedIlBody / OpcodeDiff        22 native rows
[Show IL evidence]
```

The neighboring interaction selects `Trim()` on both sides and reports native
`Exact` independently for C# and IL. Selecting
`IChangeToken.RegisterChangeCallback` as After reports
`NoApplicableInput`, never an added member or an exact body.
Closing a running comparison and opening another cannot display the old pair
under the new headings.

| Gate | Adoption evidence |
| --- | --- |
| Release `AssemblyContextMethodAddressQueryTests` | Owner-issued module association, MethodDef validation, and typed context failures. |
| Release `BrowserMethodBodyOperationTests` | Different/same pairs, reference-token drift, explicit accessors, missing/wrong context, original query failures, native body failure, platform retention, and coexistence with Source acquisition. |
| `method-body-comparison.test.ts`, `method-body-diff-view.test.ts` | Explicit submission, immutable pair association, replacement/dismissal, independent native outcomes, text rendering, and native line-operation lowering. |
| `generate-inspect-web-engine-facade.sh --check`, Release `ProductionFacadeContextTests` | Compiler-derived typed transport in the existing seven-root facade set. |
| `browser/method-body-production.spec.ts` against published Wasm | Actual shared-query results for the public package and compiled reference/implementation fixture; bodyless/accessor neighbors; dialog selection, keyboard focus, IL disclosure, narrow layout, unchanged navigation, and completed underlying Source. |

The compiled input is `FixtureCatalog.InspectWebMethodBodies`, including its
`reference` and `package` assets. Browser acceptance substitutes only the
fixture package download, not comparison transport or native evidence.
It is opt-in via `INSPECT_WEB_METHOD_BODY_URL` and
`INSPECT_WEB_METHOD_BODY_FIXTURE`; ordinary CI retains the focused contract
gates. Existing bridge, operation-authority, Queries, and Research gates remain
evidence for their own contracts.
Use the [demo hosting runbook](../runbooks/inspect-web-demo-hosting.md);
a local listener alone is not user-visible evidence.

## Delivery and retirement

Tracker #4706 owns counts. The bounded physical-pair route has eight milestones.
The shared runtime and CLI cutover in #5925 supply publication 5, adapter 6,
and CLI 8; this adopter supplies Browser 9. Together with landed steps 1 and 18,
the route reaches six of eight delivered milestones when both runtime slices
land. The remaining two are scoped Queries and Research cleanups, steps 16/17.
The Browser path, 1/18/5/6/9, is implemented end to end; its delivery depends on
the shared runtime landing first. This feature adds no separate substrate
milestone and does not use a fixture-only comparison replacement.

There is no existing Browser two-method comparison route to migrate in the
inspected baseline. Preserve single-member Source, Annotated Source, Facts,
and navigation. Remove any temporary or superseded wiring introduced during
this adoption; subsequent scoped owner cleanups use the actual caller
inventory. Broader workspace comparison, Source, assembly comparison, and
global Queries/Research retirement remain explicitly incomplete.

## Non-claims

No candidate ranking, two-package/version comparison, Source acquisition,
new comparison algorithm, canonical pair persistence, workspace lens,
global stage catalog, bridge protocol, or worker lifecycle is defined here.
This feature consumes existing operation state machines; it does not introduce
a separate concurrent protocol requiring a new model.
