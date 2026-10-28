# Unsafe Members section

Status: Implemented for Library and Type scope. This is step 3 of
[unsafe member findings](unsafe-member-findings.md#adoption-and-gates) for
[#5254](https://github.com/richlander/dotnet-inspect/issues/5254), and it
resolves [#9755](https://github.com/richlander/dotnet-inspect/issues/9755).
[Diff](#diff) participation through `diff --analysis unsafe-member` is
implemented by [#10770](https://github.com/richlander/dotnet-inspect/issues/10770).

## Owner and claim

**The CLI owns the `Unsafe Members` section.** At Library and Type scope the
section projects the
[unsafe member census](unsafe-member-findings.md#owner-and-claim) as one row
per declared member that has a finding. Its discovery uses the census's
admission rule, and an incomplete census follows the
[incomplete-evaluation rule](section-row-shaping.md#incomplete-evaluation).

The census owns admission, attribution, identity, exposure, and
completeness. This document owns only the following:

- which census each scope runs;
- the row shape;
- discovery;
- how each outcome renders;
- how [Diff](#diff) compares two censuses.

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

| Scope | Census request |
| --- | --- |
| Library | `UnsafeMemberCensusAnalysis.ForLibrary()` |
| Type (`type`, and `member` without an overload) | `UnsafeMemberCensusAnalysis.ForTypes([selected type])` |

Each request runs as its own Assembly Analysis Operation over the opened
image. The section reads no Library Body Analysis result. Selecting it
therefore no longer demands method evidence from the shared body-analysis
execution. Other selected sections that need that execution still run it.

The census request carries the default generated-expansion limits. At Type
scope, a reached bound leaves the census incomplete under its owner's rule.

## Rows

Library and Type scope share one row shape:

| Column | Value |
| --- | --- |
| Member | The declared member. At Library scope it is qualified by its declaring type; at Type scope it is not. |
| Exposure | `Public`, `Non-public`, or `Unknown`, from the finding's exposure. |
| Contract | `explicit` when the member has its own explicit updated-model contract, and so propagates unsafe context; otherwise empty. |
| Evidence | The finding's roles in inventory order, separated by semicolons. A role with more than one occurrence has a `×N` suffix. An explicit-contract call role lists its distinct callees after a colon. |
| Partial | `partial` when an attributed body could not be inspected; otherwise empty. |

Rows are ordered by the rendered member with ordinal comparison, then by
metadata token, so overloads stay in definition order. Physical bodies, IL
offsets, and contract sources are provenance. Library JSON carries them for
each evidence item, but the table does not show them. A renumbered closure or
state machine therefore renders the same row.

## Outcomes

| Census outcome | Rendering |
| --- | --- |
| Complete with findings | The rows. |
| Complete with no findings, when explicitly selected | The empty state: no unsafe members under the updated memory-safety rules. |
| Incomplete | The observed rows, a warning on stderr, and exit 1. |
| Failed | An Inspection Failures entry for `Unsafe Members`, and exit 1. |
| No managed metadata | No section. |

The stderr warning names the number of limitations and points to `--verbose`.
When there are no rows it says that no findings were observed. With
`--verbose`, each limitation is listed with:

- its reason;
- its affected body, when it has one;
- its declared member, when that body's owner was authenticated.

`--count` reports the rows that the same evidence renders. Under an incomplete
census that count is observed and gets the same disclosure and exit status.
An incomplete census with no rows is an observed zero, never "no unsafe
members".

Library JSON exposes the following:

- `unsafeMembers`, one entry per finding, with:
  - member;
  - exposure;
  - explicit contract;
  - partial;
  - evidence items, each with role, physical body, IL offset, detail, and
    contract source.
- `unsafeMembersComplete`.
- `unsafeMemberLimitations`, one string per limitation, empty for a complete
  census and absent when no census ran. Each string is the limitation's
  reason, then "in" and the affected body when it has one, then "for" and the
  declared member when the body's owner was authenticated, then a colon and
  the detail when there is one, for example
  `BodyAnalysisFailed in Sample.<Run>b__0_0() for Sample.Run(): BadImageFormatException: Malformed IL`.
  `--verbose` writes the same text to stderr.

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

## Diff

`diff --analysis unsafe-member` compares the census at two endpoints. It's one
Compare participation in the
[analysis catalog](command-transition-model.md#analysis-selection). Its
descriptor is `analysis.unsafe-member`, and it reports at the Library and Type
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
  `unsafe-member` outcome is host-resolved `Unavailable`, as for the other
  body analyses.

## Non-claims

- **Diff History.** `diff --history` doesn't correlate unsafe member findings
  across more than two versions.
- **Row selection.** The section adds no `--where` keys and no semantic row
  unit. The generic row window applies.
- **Browser.** The Inspect Web Unsafe tab is step 4.
- **Legacy evidence.** This document does not change `Unsafe Operations`,
  `Safety Facts`, `analysis.unsafety`, or `analysis.unsafe-evidence`.
- **Per-origin relation failure.** At Library scope one invalid state-machine
  origin fails the declared-source relation for the whole image, so the
  census and the section fail rather than becoming incomplete. Type scope
  fails only when the selected type owns that origin.
  [#10481](https://github.com/richlander/dotnet-inspect/issues/10481) tracks
  recording such an origin as a per-origin outcome.

## Gates

Focused Release gates in `UnsafeMembersSectionTests` cover the following:

- **Library rows.** Library rows are one row per census finding, in the shape
  and order above, over the unsafe-member fixtures.
- **#9755 shapes at Type scope.** On the #9755 fixture, the capturing lambda,
  static lambda, async method, and local function each fold into their
  declaring member. The direct member keeps its row.
- **Scope agreement.** Type rows equal the Library rows for that type, minus
  the declaring-type qualifier.
- **Legacy evidence.** A member whose only evidence is a legacy structural
  form, such as a pointer signature, a pointer local, or an `Unsafe.*` call
  without an explicit contract, has no row and does not make discovery
  advertise the section.
- **Incomplete census.** An incomplete census renders the observed rows,
  writes the stderr disclosure, gives a `--count` equal to the rows, and exits
  1. An incomplete census with no rows is reported as observed zero.
- **Failure.** A failed census projects to Inspection Failures and exits 1.
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
- **Library empty state.** A complete Library census with no findings renders
  the empty state in table output. In JSON it gives empty `unsafeMembers` and
  `unsafeMembersComplete: true`.
- **Role scan.** `UnsafeMemberRoleScanTests` holds the census rule's role
  scan to the full rule, as
  [unsafe member findings](unsafe-member-findings.md#role-scan) defines.

The performance gate is head against base on the perf hosts, with NativeAOT
builds:

- Library `-S "Unsafe Members"` on System.Private.CoreLib and
  System.Text.Json;
- Type `-S "Unsafe Members"` on the existing Type cases;
- bare `-D` on a library with and without unsafe members.
