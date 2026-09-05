# Inspect Web Method Body Comparison

## Status and ownership

This is the target design for
[#5963](https://github.com/richlander/dotnet-inspect/issues/5963), the bounded
Browser adoption of local comparison in
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706), step 9.
The feature is **unimplemented and unverified**.

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
| [Direct-member query](direct-member-comparison.md#adapter-contract) | Two exact physical designations, same-method support, and designated rather than strict correspondence |
| [Local comparison publication](local-comparison-publication.md#result-contract) | Original query-origin or Research-terminal evidence associated with one invocation |
| Existing Browser implementation-member resolution and inspection scope | Reference/surface-to-implementation selection, retained participant access, and Metadata-issued method addresses |
| [Operation authority](inspect-web-operation-authority.md) | Current-view publication, cancellation, supersession, disposal, and quiescence |
| [Managed operation bridge](inspect-web-managed-operation-bridge.md) and [Worker runtime](inspect-web-worker-runtime.md) | Generated feature transport, physical execution, cancellation forwarding, and managed release |
| [Shell interaction](inspect-web-shell-interaction.md) | Shared modal accessibility, Escape, and ordinary focus return |
| [Navigation consumer](inspect-web-navigation-consumer.md) | Ordinary member navigation, canonical location, and history |

These are dependencies, not additional normative owners of this feature.
The feature supplies its input and result meaning to their existing boundaries;
it does not change their identity, validity, cleanup, or replacement rules.

## Comparison contract

### Explicit pair interaction

Offer **Compare method bodies** as a contextual action for a selected physical
method or explicitly selected accessor/body. It belongs with the member's
working content, not the Application menu or persistent workspace chrome.
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

Only the resulting exact implementation participant and its Metadata-issued
method address are supplied to the public Queries adapter. Both endpoints must
belong to the selected implementation assembly. Display labels, bare tokens,
or equal signatures in another image cannot substitute for that association.
An asserted physical address is never retargeted to make the request succeed.

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

On narrow screens the same typed Before/After association survives a stacked
layout. Labels, code, and diagnostics use the existing text-rendering
conventions. The shared shell owns modal behavior; this feature only supplies
its accessible title, controls, content, and loading/result announcements.

## Demo and planned gates

**Design mockup**, not a shipped Browser demonstration:

```text
Inspect app.dll -> Left.Compute
Action: Compare method bodies

Method Body Diff
Before: app.dll / Left.Compute()
After:  app.dll / Right.Compute()       [choose method]
[Compare] [Close]

C#  Complete -- native body difference
Before: return value + 1;              After: return value + 2;

IL  Complete -- native operand difference
[Show IL evidence]
```

The neighboring interaction selects `Left.Compute()` on both sides and keeps
two side-local occurrences. A valid bodyless After instead shows that native
endpoint as `NoApplicableInput`, never an added member or an exact body.
Closing a running comparison and opening another cannot display the old pair
under the new headings.

| Existing gate area | Required adoption evidence |
| --- | --- |
| Release `engine.Tests`, paired feature/facade cases | Product-constructed different-name/type and same-method pairs reach the public query with exact implementation addresses; reference and implementation token differences do not substitute a method. |
| Release `engine.Tests`, result projection cases | Original query non-success and Research/native evidence survive generated facade projection, including wrong image, bodyless input, native failure, and cancellation. |
| Inspect Web TypeScript tests, feature coordinator/renderer | Explicit chooser submission, exact result labels, independent C#/IL outcomes, and existing-authority behavior on replacement/dismissal preserve ordinary navigation. |
| Hosted Browser acceptance | Use the actual compiled-fixture assembly, compare the named pair and bodyless neighbor, exercise keyboard/modal return and a narrow viewport, and show the real public-query result rather than a mocked transport. |

These new gates and the hosted demonstration are **unimplemented and
unverified**. Existing bridge, operation-authority, Queries, and Research gates
remain evidence for their own contracts, not proof of this adoption.
Use the [demo hosting runbook](../runbooks/inspect-web-demo-hosting.md);
a local listener alone is not user-visible evidence.

## Delivery and retirement

Tracker #4706 owns counts. The bounded physical-pair route has eight milestones:
1 and 18 are complete; publication 5, adapter 6, CLI 8, Browser 9, and scoped
Queries/Research cleanups 16/17 remain. The Browser path is 1, 18, 5, 6, 9:
five milestones, two complete and three remaining at this design's creation.
This preparation does not add a runtime milestone or complete step 9.

Issue #5925 owns the shared runtime and CLI cutover. Browser implementation here
follows that public boundary immediately, not another unconsumed substrate.
This design can lock while that runtime is in flight; the Browser cannot claim
delivery using a fixture-only query replacement.

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
