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
3. **Info is opt-in.** Facts about the subject are reached through bare `-S`,
   owned by [Bare `-S` default view](info-view.md). This pattern does not
   change Info content.
4. **Summaries are projections.** A presentation such as the CLI tree may group,
   decorate, or collapse children. It never omits a member of an unqualified
   population, and never substitutes a group count for the population's
   Count. Complete formats render the full population.
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
- **Info view:** [Bare `-S` default view](info-view.md) owns subject facts;
  this pattern consumes it unchanged and asks it to retire presets for
  contexts an adoption removes.
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
- type or member name resolution rules or overload addressing; or
- Info content or section names.

Those remain with the owners named in the basis. The next section records
proposals for them.

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
- **Runtime-only Libraries** appear as a count on the identity line, not as
  rows, because they are outside the compile population.
- **Addressing prerequisite:** this adoption requires the package-to-Library
  edge above — a portable row identity that preserves package source,
  version, and target, and an exact `library` gesture that consumes it. A
  candidate spelling is `library P --library <name>`, but the gesture must
  bind to the row identity, not only the name.

### Type and member (owners: the `type` and `member` command designs)

- **Exact resolution:** ambiguous input fails with a candidate list, and each
  candidate is spelled as input that resolves uniquely. Pattern, prefix, and
  missing-name input fails with tips for `find` and `library`.
- **`member T` without a member name** fails with a tip for `type T`.
- **Member tree:** an identity line with the overload count, then one
  signature per overload.

## Evidence

Observed with production dotnet-inspect 0.26.0 on 2026-09-23, unless noted:

| Asset | Observation | Bearing |
| --- | --- | --- |
| System.Text.Json 10.0.12 (NuGet) | Default is Package Info; one `lib/net10.0` Library | Package default is Info, not children |
| System.Text.Json 11.0 (platform) | Default is Library Info. Legacy definition-only public count is 91; the Library owner reports 97 public-surface declarations | Threshold uses declaration Count, not definitions |
| System.Private.CoreLib | Legacy definition-only public count 1,358; the Library owner reports 1,447 declarations | A large library collapses under any reasonable threshold |
| Newtonsoft.Json | 130 public definitions | Collapses at the proposed threshold |
| Microsoft.Data.SqlClient 7.1.0 | `ref`, `lib`, and `runtimes/{unix,win}/lib` copies; the `lib/net9.0` asset is a 93 KB stub next to 1.67 MB runtime copies | Package children count from the compile asset alone |
| SkiaSharp.NativeAssets.Linux | 13 `runtimes/*/native` RIDs, no managed Libraries | Empty compile population must be stated |
| platform `Timer` | `type Timer` silently renders `System.Threading.Timer` | Obligation 1 requires a visible ambiguity failure |
| `member JsonSerializer` | Renders member-group tables duplicating `type` | Two commands render one subject's children |

The 97 and 1,447 figures come from the Library owner's current evidence and
were not reproduced here. Each adoption records its own measurements against
its owner's Count.

## Adoption

Each adoption lands in its owner's document and implementation. It updates
`docs/cli-reference.md` and the shipped product skills for any behavior it
changes and demonstrates the real production-host scenario. Host-neutral
children populations serve both CLI and Browser/Wasm; the CLI tree is host
presentation.

1. **Library children** (presentation only): default and `-v:m` children
   view, tree, and `--namespace`, consuming the Library Type declaration
   population. Current listing paths remain.
2. **Listing retirement:** after the positive CLI and Browser/Wasm gates in
   the Library proposal, retire the `type` listing context and its bare `-S`
   preset.
3. **Package children:** after the package-to-Library edge exists.
4. **Exact `type` and `member`:** exact resolution, removal of `member T`
   without a name, and the member tree.

## Gates

This document's obligations are gated through each adoption, in Release,
against that adoption's motivating assets:

- obligation 1: the platform `Timer` collision fails with candidates;
- obligations 2 and 3: default, `-v:m`, and bare `-S` output for each adopted
  command;
- obligation 4: a tree's grouped or collapsed counts sum to the population
  Count, forwarders included, for System.Text.Json and System.Private.CoreLib;
- obligation 5: an empty compile population for SkiaSharp.NativeAssets.Linux;
  and
- each ladder edge: its row identity resolves through its exact gesture to the
  same child.

Until an adoption lands, every obligation is `unverified` for that command.
