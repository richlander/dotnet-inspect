# Unsafe Code section

Status: Implemented at Library, Type, and Member scope. This is step 3 of
[unsafe member findings](unsafe-member-findings.md#adoption-and-gates) for
[#5254](https://github.com/richlander/dotnet-inspect/issues/5254), with the
CLI's adoption of step 4's scopes from
[#10881](https://github.com/richlander/dotnet-inspect/issues/10881). It
resolves [#9755](https://github.com/richlander/dotnet-inspect/issues/9755).
[Diff](#diff) participation through `diff --analysis unsafe-code` is
implemented by [#10770](https://github.com/richlander/dotnet-inspect/issues/10770).
Library scope runs [Finding Unsafe Any](#finding-unsafe-any), which lists
Types instead of counting members.

## Owner and claim

**The CLI owns the `Unsafe Code` section.** It discloses the
[unsafe member census](unsafe-member-findings.md#owner-and-claim)
progressively, one scope at a time:

- **Library:** one row per Type that has unsafe code, from
  [Finding Unsafe Any](#finding-unsafe-any), which settles each Type at its
  first finding.
- **Type:** one row per declared member that has a finding.
- **Member:** the member's finding and its evidence items.

Each scope runs its own operation, so no scope computes a wider scope first.
Its discovery uses the census's admission rule, and an incomplete answer
follows the
[incomplete-evaluation rule](section-row-shaping.md#incomplete-evaluation).

The census owns admission, attribution, identity, exposure, the
[safe-boundary state](unsafe-member-findings.md#safe-boundary),
[Type groups and counts](unsafe-member-findings.md#type-groups-and-counts),
and completeness. This document owns only the following:

- the section's name;
- which operation each scope runs;
- Finding Unsafe Any's per-Type answer;
- the row shape at each scope;
- discovery;
- how each outcome renders;
- how [Diff](#diff) compares two censuses.

## Name

The section is `Unsafe Code` at every scope, and its Diff analysis identity is
`unsafe-code`. It was `Unsafe Members` and `unsafe-member`. A Library row is a
Type, not a member, so the old name no longer described every scope. As the
[selection grammar](command-transition-model.md#selection-grammar) owns,
neither old name is kept as an alias: selecting it reports an unknown section
or analysis with its suggestions.

## Three dimensions

Each scope keeps three dimensions apart:

- **Presence:** the member has a finding, because it uses or propagates unsafe
  code.
- **Exposure:** whether the member is public.
- **Safe boundary:** the member uses unsafe code without propagating it. The
  census decides this only under the updated rules.

Presence and safe boundary look alike but differ: a propagating member has a
finding and is never a safe boundary.

## Why the rows change

The section used to show one row per legacy unsafe-evidence item:

- an unsafe signature;
- an unsafe API member;
- a pinned or pointer local;
- an unsafe call;
- an unsafe opcode.

Those rows were keyed by the physical method. Lambda, static lambda, and async
evidence was filtered out at Type scope because it lives in a closure or a
state-machine type, and a local function appeared under its lifted name
(#9755). The two scopes also ordered rows differently.

The section now admits exactly the census's updated memory-safety roles:

- the member's own explicit contract;
- pointer dereference;
- indirect call;
- stack allocation;
- calls to explicit-contract members.

Legacy structural evidence leaves this section, though it still appears
elsewhere:

- `Unsafe Operations` and `Safety Facts` keep the per-site legacy evidence at
  member and Type scope;
- the IL-ordered `analysis.unsafety` family keeps its own unit.

## Execution

| Scope | Command | Census request |
| --- | --- | --- |
| Library | `library` | `FindingUnsafeAny.ForLibrary()`, the census visit settled per Type |
| Type | `type`, and `member` naming only a Type | `UnsafeMemberCensusAnalysis.ForTypes([selected type])` |
| Member | `member` naming a member | `UnsafeMemberCensusQuery.ExecuteExactMethods` over the selected methods |

Each request runs as its own Assembly Analysis Operation over the opened
image. The section reads no Library Body Analysis result. Selecting it
therefore demands no method evidence from the shared body-analysis execution.
Other selected sections that need that execution still run it.

The Member request covers the method definitions the member selector names:

- every overload of a member group, or the one method an overload selector
  names;
- a property's getter and setter.

It reaches their own bodies and the generated bodies they own, as the census's
[Member scope](unsafe-member-findings.md#execution) defines. It does not run
the Type or Library census first, and it loads only the selected Type's
declarations, not the library's whole API surface. A selection that names no
method definition, such as an event, a field, or an accessor's own name, fails
visibly instead of rendering the empty state.

The census request carries the default generated-expansion limits. A reached
bound leaves the census incomplete under its owner's rule.

## Finding Unsafe Any

A Library answer lists Types, never members, so
[no row holds rows](scope-symmetry.md#no-rows-within-rows). The one question
it asks of each Type is whether the Type has unsafe code. Finding Unsafe Any
answers that question the way [Finding Diff Any](finding-diff-any.md) answers
whether a Type changed: with the complete operation's own rule, stopped at
each Type's first finding.

> **Claim.** For one library, Finding Unsafe Any returns as `Unsafe` exactly
> the declaring Types of the Library census's findings. It returns a Type
> with no finding as `Unknown` when a body attributed to one of its members
> could not be inspected. Its limitations are the census's limitations, less
> those that a Type's `Unsafe` answer settles.

- **Source Type.** A method definition's source Type is its declaring Type,
  or, for a method in a nested compiler-generated Type, the nearest enclosing
  Type that isn't one. That Type's members own the method's bodies.
- **Settlement.** A body settles its source Type when the census will
  attribute its roles to itself. That's when it has a census role and, by the
  [presence probe's](#discovery) shape rule, it's its own declared member.
  After that, the Type's later bodies aren't visited, and nor are the bodies
  of the compiler-generated Types nested in it. A generated body never settles
  a Type, because it needs an authenticated owner. Its roles reach the owner's
  Type through the census's attribution.
- **Fold.** The visited bodies fold through the census's attribution and
  admission. Exposure isn't read, so the public-root inventory and its
  limitations aren't part of the answer.
- **Limitations.** A limitation can't change the answer of a Type that's
  `Unsafe`, so the answer drops it. The rest stay: image limitations, and
  limitations of bodies whose source Type has no finding. A Type with no
  finding whose limitation names a declared member is `Unknown`. An
  unattributed generated body names no member, so it leaves no row, but it
  still leaves the answer incomplete. Which bodies settlement skipped
  therefore never changes the answer or its completeness.
- **Fidelity.** The claim holds for Roslyn-produced assemblies, where a
  generated body is nested in its owner's Type. If another compiler's skipped
  body in a settled Type references a generated body in a different Type, the
  declared-source relation might authenticate that body when the census
  wouldn't. That answer is wrong but contained, since it can only make a Type
  `Unsafe`.
- **Cost.** It costs at most the census over the same library. Each Type with
  a finding stops at its first one, and each Type without one is visited in
  full, as the census visits it. The skip uses the producer's
  [source-Type settlement](producer-planning.md#scope-is-declared-on-the-edge-not-tested-inside-the-visit).

## Rows

### Library

One row per Type that Finding Unsafe Any returns:

| Column | Value |
| --- | --- |
| Type | The declaring Type's metadata name, qualified by namespace and enclosing Types, with generic arity, such as ``GCHandle`1``. Two Types that differ only in arity are two rows, and the name is one `type` resolves. |
| State | `unsafe` or `unknown`. The column is shown only when some row is `unknown`. |

Rows are ordered by the rendered Type with ordinal comparison. The Library
row carries no member, so a Library answer is never
[rows within rows](scope-symmetry.md#no-rows-within-rows). The members of a
Type, their counts, and their safe-boundary states are the Type scope's
answer. The Library row no longer carries the census's
[Type counts](unsafe-member-findings.md#type-groups-and-counts), because an
answer settled at a Type's first finding has none. That's an intentional
change from the rows #10966 shipped.

### Type

One row per declared member that has a finding:

| Column | Value |
| --- | --- |
| Member | The declared member, not qualified by its declaring type. |
| Exposure | `Public`, `Non-public`, or `Unknown`, from the finding's exposure. |
| Contract | `explicit` when the member has its own explicit updated-model contract, and so propagates unsafe context; otherwise empty. |
| Safe boundary | `safe boundary`, `not a safe boundary`, `legacy rules`, or `rules unknown`, from the finding's safe-boundary state. |
| Evidence | The finding's roles in inventory order, separated by semicolons. A role with more than one occurrence has a `×N` suffix. An explicit-contract call role lists its distinct callees after a colon. |
| Partial | `partial` when an attributed body could not be inspected; otherwise empty. |

Rows are ordered by the rendered member with ordinal comparison, then by
metadata token, so overloads stay in definition order. Physical bodies, IL
offsets, and contract sources are provenance, which the Member scope shows. A
renumbered closure or state machine therefore renders the same row.

### Member

A selection of several method definitions, such as a member group or a
property with two accessors, renders the Type rows for those methods that have
findings. A selection of one method definition renders one row per evidence
item, in the census's inventory order:

| Column | Value |
| --- | --- |
| Exposure | As at Type scope. |
| Contract | As at Type scope. |
| Safe boundary | As at Type scope. |
| Role | The evidence role. |
| Body | The physical body that holds the evidence: the method itself, or a lambda, local function, or state-machine body it owns. |
| IL | The IL offset, when the role has one. |
| Detail | The role's detail, such as a callee or a pointer type, when it has one. |
| Partial | As at Type scope. |

Each row repeats the method's exposure, contract, and safe-boundary state, so
a row stands alone in TSV and JSONL output.

## Outcomes

| Census outcome | Rendering |
| --- | --- |
| Complete with findings | The rows. |
| Complete with no findings, when explicitly selected | The empty state. For one method it is "No unsafe evidence was attributed to this member."; otherwise it is "No unsafe code under the updated memory-safety rules." |
| Incomplete | The observed rows, a warning on stderr, and exit 1. At Library scope the warning says the search, not the census, has the limitations. |
| Failed | An Inspection Failures entry for `Unsafe Code`, and exit 1. |
| No managed metadata | No section. |

The Member empty state says what was attributed, not that the member is safe:
the Member census does not reach generated bodies whose owner it cannot
authenticate, as the census's [Member scope](unsafe-member-findings.md#execution)
states.

The stderr warning names the number of limitations and points to `--verbose`.
When there are no rows it says that no findings were observed. With
`--verbose`, each limitation is listed with:

- its reason;
- its affected body, when it has one;
- its declared member, when that body's owner was authenticated.

`--count` reports the rows that the same evidence renders: Types at Library
scope, members at Type scope and for a member group, and evidence items for one
method. Under an
incomplete census that count is observed and gets the same disclosure and exit
status. An incomplete census with no rows is an observed zero, never "no unsafe
code".

Library JSON exposes the following:

- `unsafe_code`, one entry per Type row, with `type` and `state`.
- `unsafe_code_complete`.
- `unsafe_code_limitations`, one string per limitation, empty for a complete
  answer and absent when Finding Unsafe Any didn't run. Each string is the limitation's
  reason, then "in" and the affected body when it has one, then "for" and the
  declared member when the body's owner was authenticated, then a colon and
  the detail when there is one, for example
  `BodyAnalysisFailed in Sample.<Run>b__0_0() for Sample.Run(): BadImageFormatException: Malformed IL`.
  `--verbose` writes the same text to stderr.

Library JSON no longer lists members or their counts. That is an
intentional change from the `unsafe_members` field, which carried every member
and its evidence at Library scope, and from the counts that #10966's
`unsafe_code` entries carried.

## Discovery

The presence probe behind `-D` uses the census's per-body rule. It answers
whether any method definition at the census breadth has a positive role, and
settles at the first one. It reads the same declaration contract, body, call,
and contract-source inputs as a census visit, and makes no other admission
decision. The census never confers a generated body's own explicit contract on
its declaring member. So presence counts a body's own contract only when the
body's shape makes it its own declared member: not a lambda, local function,
or state-machine body, not in a nested compiler-generated type, and not an
unconfirmed extension declaration. An
`unsafe` local function that nothing calls is therefore not a role. Library scope probes every definition. Type scope probes the
selected type's census breadth, including unauthenticated generated
candidates. A probe that cannot decide fails discovery visibly, as before.

The probe costs at most the census rule over the bodies it visits before
its first role, plus one-time construction. Each body is decided by the
census's [role scan](unsafe-member-findings.md#role-scan) when it can be, and
materialized only when it cannot. A library with no role is visited in full.
A library with roles usually settles within a few bodies. Its cost is then
mostly one-time: the platform projection load and the memory-safety metadata
index, each paid at the first body that needs it.

Presence and rows can differ in exactly one way. A positive role in a
generated body whose owner cannot be authenticated is a limitation, not a
finding. Discovery then advertises a section whose census is incomplete with
no rows, and that case renders as an observed zero under the incomplete
outcome.

At Member scope the section is explicit-only: discovery runs no probe for it,
and only `-S "Unsafe Code"` runs the Member census.

## Diff

`diff --analysis unsafe-code` compares the census at two endpoints. It's one
Compare participation in the
[analysis catalog](command-transition-model.md#analysis-selection). Its
descriptor is `analysis.unsafe-code`, and it reports at the Library and Type
surfaces. Both surfaces declare the `Transitions` view. As the selection grammar
owns, the identity has no aliases.

- **Census per side.**
  - At Library, each endpoint runs the Library census over each of its Library
    assemblies. Assemblies pair by case-insensitive file name, as
    `string-literals` pairs them. An assembly on only one side compares
    against no members.
  - At Type, each endpoint runs the Type census over the assembly that defines
    the selected Type, found by its exact metadata definition name. A Type on
    only one side compares against no members.
- **Identity.** Findings match as an identity set of declared members, keyed
  by the census's member identity: the shared signature fragment, which leaves
  out module version, metadata token, and caller-unsafe mode.
- **Value.**
  - A matched member is `Changed` when its `UnsafeMemberFindingSummary`
    differs. That summary is the row's value: exposure, explicit contract, and
    each role in inventory order with its occurrence count, plus the distinct
    callees of an explicit-contract call.
  - Physical bodies, generated names, IL offsets, and metadata tokens are
    provenance. A renumbered closure or state machine, or a shifted offset, is
    therefore `Present`, and the row and the comparison can't disagree about a
    change.
  - Each transition row's `Old` and `New` show the summary, or `absent`.
- **Incomplete or failed side.**
  - An incomplete census on either side is never compared as complete. The
    comparison fails with both sides' limitation counts and keeps both sides'
    observed findings, so Diff discloses it and exits 1. The failure detail
    names the incomplete side. The row's `old_inspection` and
    `new_inspection` keep Diff's existing inspection states, which have no
    incomplete state, so they read `complete` for the observed findings.
  - A failed census fails the comparison the same way.
  - A partial finding always carries a census limitation, so this rule covers
    it too.
- **Browser.** Browser Compare doesn't construct method-body inputs. Its
  `unsafe-code` outcome is host-resolved `Unavailable`, as for the other
  body analyses.

Library Diff still reports one transition per member. Reporting only the
Types that changed, as [Finding Diff Any](finding-diff-any.md) does at Library
scope, and whether a change in the safe-boundary state is a change, belong to
[#10965](https://github.com/richlander/dotnet-inspect/issues/10965).

## Non-claims

- **Diff History.** `diff --history` doesn't correlate unsafe member findings
  across more than two versions.
- **Row selection.** The section adds no `--where` keys and no semantic row
  unit. The generic row window applies.
- **Browser.** This document doesn't own the Inspect Web Unsafe tab. That's
  step 4, which [its owner](inspect-web-surface-composition.md#unsafe) defines
  over the same census.
- **Legacy evidence.** This document does not change `Unsafe Operations`,
  `Safety Facts`, `analysis.unsafety`, or `analysis.unsafe-evidence`.
- **Per-origin relation failure.** At Library scope one invalid state-machine
  origin fails the declared-source relation for the whole image, so the
  census and the section fail rather than becoming incomplete. Type scope
  fails only when the selected type owns that origin.
  [#10481](https://github.com/richlander/dotnet-inspect/issues/10481) tracks
  recording such an origin as a per-origin outcome.

## Gates

Focused Release gates in `UnsafeCodeSectionTests` and
`LibraryFindingConsumerTests` cover the following:

- **Library rows.** Library rows are one row per Type in the shape and order
  above, over the unsafe-member fixtures. `--count` reports the Types. The
  State column is shown only for an `unknown` Type.
- **Finding Unsafe Any.** Over the updated-model, legacy, analysis, and
  iterator fixtures, its `Unsafe` Types are exactly the Library census's
  declaring Types, with none `Unknown`. Its limitations are the census's
  limitations outside those Types, and it attempts fewer bodies than the
  census. A focused fold gate keeps a limitation only where it leaves a Type
  unsettled, and makes a Type `Unknown` for a failed body of a known member.
- **Safe boundary.** Type rows carry each finding's safe-boundary text.
- **Member scope.** A member group's rows equal its overloads' Type rows. One
  method's evidence rows carry that member's Type-row values and the
  finding's evidence items. A method with no attributed evidence renders the
  Member empty state. A property selection covers its accessors, and a
  selection with no method definition fails visibly.
- **Generic arity.** Types that differ only in generic arity are separate
  Library rows.
- **Name.** `-S "Unsafe Members"` and `--analysis unsafe-member` reject with
  the new names among their suggestions.
- **#9755 shapes at Type scope.** On the #9755 fixture, the capturing lambda,
  static lambda, async method, and local function each fold into their
  declaring member. The direct member keeps its row.
- **Scope agreement.** The CLI's Library Types are the declaring Types of
  the census's findings on the section fixtures, so each Library row opens to
  Type rows.
- **Legacy evidence.** A member whose only evidence is a legacy structural
  form, such as a pointer signature, a pointer local, or an `Unsafe.*` call
  without an explicit contract, has no row and does not make discovery
  advertise the section.
- **Incomplete answer.** An incomplete census or Library answer renders the
  observed rows, writes the stderr disclosure, gives a `--count` equal to the
  rows, and exits 1. One with no rows is reported as observed zero.
- **Failure.** A failed census or Library answer projects to Inspection
  Failures and exits 1.
- **Discovery.** Discovery agrees with the rows on the census fixtures at both
  scopes. That includes an `unsafe` local function that is never called or is
  only made into a delegate.
- **Diff.** `UnsafeMemberFindingsTests.Comparison` covers:
  - added, removed, changed, and present members;
  - provenance that is never a change;
  - exposure and contract changes;
  - an incomplete or failed side that fails the comparison;
  - the summary text.

  End-to-end gates run over the dedicated `UnsafeMemberDiffPair` fixture,
  which is kept apart from the shared `DiffPair` so that pair's tokens and
  member populations stay stable:
  - `Diff_UnsafeMember_LibraryTransitionsClassifyEachMember` and
    `Diff_UnsafeMember_TypeTransitionsAgreeWithLibrary` cover the same
    transitions at Library and Type scope.
  - `Diff_UnsafeMember_OneSidedTypeIsAllAddedOrRemoved` covers a Type present
    at one endpoint only.
  - `Diff_UnsafeMember_PackageAssemblyRenameReportsBothSides` covers a package
    whose Library assembly is renamed: two subjects, all Removed and all
    Added.
  - `UnsafeMemberComparisonQueryTests` covers case-insensitive assembly
    pairing, a one-sided assembly whose members are all Added, and agreement
    between participant and path endpoints.
  - `LibraryUnsafeMemberAnalysisIsHostUnavailable` covers the Browser's
    host-resolved `Unavailable` outcome.
- **Library empty state.** A complete Library answer with no Types renders
  the empty state in table output. In JSON it gives empty `unsafe_code` and
  `unsafe_code_complete: true`.
- **Role scan.** `UnsafeMemberRoleScanTests` holds the census rule's role
  scan to the full rule, as
  [unsafe member findings](unsafe-member-findings.md#role-scan) defines.

The performance gate is head against base on the perf hosts, with NativeAOT
builds:

- Library `-S "Unsafe Code"` and `--count` on System.Private.CoreLib and
  System.Text.Json, where Finding Unsafe Any must beat the census it
  replaces;
- Type `-S "Unsafe Code"` on the existing Type cases;
- Member `-S "Unsafe Code"` on a method of those Types, which must cost no
  more than the Type case;
- bare `-D` on a library with and without unsafe code.
