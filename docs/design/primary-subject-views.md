# Primary subject views

## Status

Proposed. This document is the focused owner of one cross-cutting pattern: the
primary subject view for `package`, `library`, `type`, and `member`. It owns
only that pattern. Each command's owner adopts it in its own slice and owns the
command-specific choices; [Adopter proposals](#adopter-proposals) records
proposed choices for those owners without deciding them. Until a slice lands,
current product behavior remains governed by its existing owner.

## Authority and exact claim

This document owns one claim:

> Each of `package`, `library`, `type`, and `member` names exactly one subject.
> Its default view is that subject's children: one compact identity line
> followed by the child population that the subject's owner issues. Facts
> about the subject itself are the opt-in Info view.

The pattern has five obligations. An adopting command meets all of them:

1. **One subject.** The command resolves exactly one subject or fails visibly.
   It never renders a list of subjects and never picks one candidate silently.
2. **Children by default.** The default view and the `-v:m` view are the
   subject's children, drawn from the host-neutral population its owner issues.
   The children section is the command's single `-v:m` section.
3. **Info is opt-in.** Facts about the subject are one explicitly named
   subject-facts section, selected with `-S <section>`. It answers "what is
   this subject?" and does not re-render the children population. The
   primary behavior is the absence of `-S`, which renders the default
   children section. This pattern never relies on bare `-S`; see
   [Bare `-S` retirement](#bare--s-retirement).
4. **Summaries are projections.** A presentation such as the CLI tree may add
   a grouped, decorated, or collapsed projection of the children. That
   projection is additional: it is never the unqualified inventory, it never
   substitutes a group count for the population's Count, and it names the
   gesture that reaches the full inventory. Complete formats render the
   unqualified population in full.
5. **Failure is visible.** An unreadable child, an incomplete population, or an
   empty population renders as such. None becomes a success-shaped empty or
   zero result.

### Containment ladder

The four commands form one containment ladder:

| Command | Subject | Children |
| --- | --- | --- |
| `package` | one package at one selected target | Libraries |
| `library` | one exact Library | Type declarations |
| `type` | one exact Type | Members |
| `member` | one exact member name | Overload signatures |

The ladder is a target, not a claim that every edge works today. An edge from
one level to the next is supported only after its adopter provides both:

- a portable identity on each child row that preserves the correspondence
  facts of its source (for example package source, version, and target for a
  package's Libraries); and
- an exact next-command gesture that consumes that identity and resolves to
  the same child.

A display name alone is not a sufficient identity. Until both exist for an
edge, that adoption must not present its rows as copyable arguments.

## Design basis

- **Normative owner:** this document — subject cardinality, the default
  children view, and the obligations above.
- **Verbosity and section defaults:**
  [Progressive disclosure](progressive-disclosure.md#verbosity) owns which
  sections each verbosity level renders; it adopts obligation 2 per command.
- **Subject-facts sections:** each command's section owner owns its facts
  section: `Package Info`, `Library Info`, `Type Info`, and `Signature` for
  one selected overload. This pattern consumes them unchanged by name. No
  facts section exists yet for an exact member name, so `member` adoption
  requests one.
- **Default renderer:**
  [Rendering model](rendering-model.md#native-type-and-source-defaults) owns
  default presentation. The existing exact-type tree is the reference shape
  for obligations 2 and 4.
- **Library children:**
  [Library inspection documents and populations](library-inspection-document.md)
  owns the Library Type declaration population, including first-class
  forwarders, public-surface selection, and exact Count and Rows.
- **Package children:** [Package library scope](package-library-scope.md) and
  [Package asset-selection correspondence](package-asset-selection-correspondence.md)
  own the selected compile population and its aggregate scope.
- **Visibility default:** [API population scope](api-population-scope.md) owns
  the public-facing default and explicit widening.
- **Symbol search:** `find` owns pattern search, so non-exact `type` and
  `member` input has a home other than a listing.
- **Motivating assets:** System.Text.Json, Microsoft.Data.SqlClient 7.1.0,
  SkiaSharp.NativeAssets.Linux, System.Private.CoreLib, Newtonsoft.Json, and
  the platform `Timer` name collision. See [Evidence](#evidence).

## Non-claims

This document does not own and does not decide:

- Library or package population membership, Count, or Rows;
- package asset selection, role preference, or library scope;
- the identity-line fields, RID disclosure, or tree layout of any command;
- collapse thresholds;
- type or member name resolution rules or overload addressing;
- Info content or section names, including the proposed exact-member-name
  facts section; or
- bare `-S` behavior for any command, which the retirement effort below owns.

Those remain with the owners named in the basis. The next sections record the
separate bare `-S` retirement and proposals for the other owners.

## Bare `-S` retirement

The operator directed on 2026-09-24 that bare `-S` become illegal. The
primary behavior of every command is the absence of `-S`, which renders that
command's default section; `-S` always names what it selects. Bare `-S`
currently has broader uses than these four commands, including Package
Query's `Packages` preset and `graph libraries`' summary pair, so its
retirement is a separate focused effort owned by
[Progressive disclosure](progressive-disclosure.md#bare--s) and
[Bare `-S` default view](info-view.md). That effort decides the failure
message, the migration of each preset to a default section or a named
section, and the documentation and shipped skills that change.

This pattern is independent of that effort: its obligations use only the
default view and explicitly named sections, so each adoption is correct
whether or not bare `-S` has been retired yet.

## Adopter proposals

These proposals are not normative. Each becomes a contract only when its owner
adopts it, in that owner's document, with its own gates.

### Library (owner: Library inspection documents and populations)

- **Children:** the public-surface Type declaration population, both
  definitions and forwarders.
- **Tree:** namespaces with declaration counts, then declarations by short
  name. A definition shows its kind, modifiers, and member count. A forwarder
  keeps its declared name and namespace and is marked as forwarded (for
  example `SomeType (forwarded)`). It shows no kind, modifiers, or member
  count, because those would come from a target.
- **Namespace counts** include forwarders, and the total equals the
  population's Count.
- **Complete formats** (Markdown, JSON, rows) carry every declaration with its
  declaration kind.
- **Collapse:** the tree lists namespaces only when the public-surface
  declaration Count exceeds a threshold, proposed as 100. Collapsed namespace
  counts still include forwarders, and the tree ends with a tip naming the
  expansion gesture.
- **Subject resolution:** a `library` source that selects more than one
  Library fails with a candidate list instead of silently picking one, and an
  exact Library selector (candidate spelling `library P --library <name>`)
  resolves one Library, bound to package source, version, and target. A
  namesake or first-Library recommendation under
  [Package library scope](package-library-scope.md#exact-scope) may appear as
  a tip on that failure, never as the silent subject.
- **Narrowing:** `library L --namespace N`, so that every collapsed row can be
  expanded.
- **Retirement:** the current `type` listing context retires only after the
  Library children view passes positive CLI format and mode gates and
  Browser/Wasm adoption gates. The host-neutral operation and its forwarder
  semantics remain after that presentation context retires.

### Package (owners: Progressive disclosure and Package library scope)

- **Children:** the selected compile population in aggregate scope, one row
  per Library with its public-surface Type declaration Count. Each count reads
  only the compile asset that compile selection chose.
- **Identity line:** source, selected target, compile roles present, and
  managed and native runtime RIDs, capped to a count when long; for example
  `Microsoft.Data.SqlClient 7.1.0 (NuGet, net9.0; ref, lib; runtimes: unix,
  win)`.
- **Runtime assets** are disclosed only on the identity line. Whether a
  `runtimes/*/lib` asset is an implementation overlay of a compile Library or
  a separate Library is decided by package asset selection's correspondence,
  not by this view.
- **Addressing prerequisite:** this adoption reuses the exact Library selector
  from the Library proposal as the package-to-Library edge; each row carries
  the identity that selector consumes.

### Type and member (owners: the `type` and `member` command designs)

- **Exact resolution:** ambiguous input fails with a candidate list, and each
  candidate is spelled as input that resolves uniquely. Pattern, prefix, and
  missing-name input fails with tips for `find` and `library`.
- **`member T` without a member name** fails with a tip for `type T`.
- **Member tree:** an identity line with the overload count, then one
  signature per overload.
- **Member Info:** no named facts section exists for an exact member name;
  the overload rows that bare `-S` renders today become the children.
  `member` adoption waits for its section owner to issue one (for example
  member kind, declaring type, overload count, and documentation summary).
  Info for one selected overload, `M:<n>`, remains `Signature`.

## Evidence

Observed with production dotnet-inspect 0.26.0 on 2026-09-23, unless noted:

| Asset | Observation | Bearing |
| --- | --- | --- |
| System.Text.Json 10.0.12 (NuGet) | Default is Package Info; one `lib/net10.0` Library | Package default is Info, not children |
| System.Text.Json 11.0.0-rc.1.26425.128 (platform) | Default is Library Info. Legacy definition-only public count is 91; the Library Type Count adoption reports 97 public-surface declarations | Threshold uses declaration Count, not definitions |
| System.Private.CoreLib | Legacy definition-only public count 1,358; the Library Type Count adoption reports 1,447 declarations | A large library collapses under any reasonable threshold |
| Newtonsoft.Json | 130 public definitions | Collapses at the proposed threshold |
| Microsoft.Data.SqlClient 7.1.0 | `ref`, `lib`, and `runtimes/{unix,win}/lib` copies; the `lib/net9.0` asset is a 93 KB stub next to 1.67 MB runtime copies | Package children count from the compile asset alone |
| SkiaSharp.NativeAssets.Linux | 13 `runtimes/*/native` RIDs, no managed Libraries | Empty compile population must be stated |
| platform `Timer` | `type Timer` silently renders `System.Threading.Timer` | Obligation 1 requires a visible ambiguity failure |
| `member JsonSerializer` | Renders member-group tables duplicating `type` | Two commands render one subject's children |
| `member JsonSerializer Serialize -S` | Bare `-S` renders the `Methods` overload rows | No exact-member-name facts section exists yet |
| Microsoft.TestPlatform.ObjectModel 18.10.1 | Three Libraries for net8.0; `library` silently renders `Microsoft.TestPlatform.CoreUtilities.dll` | Obligation 1 requires Library subject resolution |

The 97 and 1,447 figures come from the Library Type Count and Rows work in
[#8385](https://github.com/richlander/dotnet-inspect/pull/8385) and
[#8411](https://github.com/richlander/dotnet-inspect/pull/8411) and were not
reproduced here. Each adoption records its own measurements against
its owner's Count.

## Adoption

Each adoption lands in its owner's document and implementation. It updates
`docs/cli-reference.md` and the shipped product skills for any behavior it
changes and demonstrates the real production-host scenario. Host-neutral
children populations serve both CLI and Browser/Wasm; the CLI tree is host
presentation.

1. **Library subject and children:** exact Library subject resolution and
   selector, then the default and `-v:m` children view, tree, and
   `--namespace`, consuming the Library Type declaration population. Current
   listing paths remain.
2. **Listing retirement:** after the positive CLI and Browser/Wasm gates in
   the Library proposal, retire the `type` listing context.
3. **Package children:** rows addressed through the step 1 Library selector.
4. **Exact `type` and `member`:** exact resolution, removal of `member T`
   without a name, and the member tree. The `member` part waits for the
   exact-member-name facts section.

## Gates

This document's obligations are gated through each adoption, in Release,
against that adoption's motivating assets:

- obligation 1: the platform `Timer` collision and
  Microsoft.TestPlatform.ObjectModel's three Libraries each fail with
  candidates;
- obligations 2 and 3: output with no `-S`, `-v:m` output, and the explicitly
  named facts section for each adopted command;
- obligation 4: a tree's grouped or collapsed counts sum to the population
  Count, forwarders included, and the complete formats list every
  declaration, for System.Text.Json and System.Private.CoreLib;
- obligation 5: an empty compile population for SkiaSharp.NativeAssets.Linux;
  and
- each ladder edge: its row identity resolves through its exact gesture to the
  same child.

Until an adoption lands, every obligation is `unverified` for that command.
