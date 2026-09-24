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
2. **Children by default.** The default view is `-v:m`, the compact children
   view: the subject's children, drawn from the host-neutral population its
   owner issues, rendered as one or more of that owner's children sections
   (for example a type's per-kind member sections). It may also show context
   the owner attaches to the subject, such as a base type or implemented
   interfaces. That context is labeled as context and is never counted as
   children. Whether a related declaration is a child or context is the
   population owner's decision, not the presentation's.

   Some children stand in a special relationship to the subject: a forwarded
   Type declaration in a Library, or an attached extension Member of a Type
   that another Type declares. These remain children and count toward the
   population, but every presentation and format distinguishes them from
   ordinary children by their owner-issued row kind. The tree marks or groups
   them, and Markdown, JSON, and row formats carry the row kind.
3. **Info is opt-in.** Facts about the subject are one explicitly named
   subject-facts section, selected with `-S <section>`. It answers "what is
   this subject?" and does not re-render the children population. Whether
   `-v:d` also includes the facts section is Progressive disclosure's
   decision.
4. **`-v:m` is compact; `-v:n` and `-v:d` are exhaustive.** At `-v:m`, the
   tree may group, decorate, or collapse the children. That summary is an
   additional projection: it never substitutes a group count for the
   population's Count, and a collapsed tree names the gesture that reaches the
   full inventory. Markdown, JSON, and row formats render the full population
   even at `-v:m`. At `-v:n` and `-v:d`, every format renders the unqualified
   children population in full, with no collapse or omission; higher
   verbosity may add detail to rows but never removes children.
5. **Failure is visible.** An unreadable child, an incomplete population, or an
   empty population renders as such. None becomes a success-shaped empty or
   zero result.

### Containment ladder

The four commands form one containment ladder:

| Command | Subject | Children |
| --- | --- | --- |
| `package` | one package at one selected target | Libraries, or RID packages for a tool pointer package |
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

A tool pointer package's rows lead to `package <id>` for each RID package
rather than to `library`; that edge follows the same rule.

A display name alone is not a sufficient identity. Until both exist for an
edge, that adoption must not present its rows as copyable arguments.

## Design basis

- **Normative owner:** this document — subject cardinality, the default
  children view, and the obligations above.
- **Verbosity and section defaults:**
  [Progressive disclosure](progressive-disclosure.md#verbosity) owns which
  sections each verbosity level renders; it adopts obligations 2 and 4 per
  command, replacing its current rule that omits the children inventories
  from `-v:n` for these commands.
- **Subject-facts sections:** each command's section owner owns its facts
  section: `Package Info`, `Library Info`, `Type Info`, and `Signature` for
  one selected overload. This pattern consumes them unchanged by name. No
  facts section exists yet for an exact member name, so `member` adoption
  requests one.
- **Default renderer:**
  [Rendering model](rendering-model.md#native-type-and-source-defaults) owns
  default presentation. The existing exact-type tree is the reference shape
  for the compact view: per-kind member sections, collapsed overloads,
  labeled context (`Inherits`, `Implements`), and a distinguished group of
  attached extension Members (`Extension Methods`).
- **Library children:**
  [Library inspection documents and populations](library-inspection-document.md)
  owns the Library Type declaration population, including first-class
  forwarders, public-surface selection, and exact Count and Rows.
- **Type and Member children:** the proposed
  [Type and Member inspection documents](https://github.com/richlander/dotnet-inspect/pull/8432)
  own the Type `Members` and Member `Overloads` populations, including
  attached extension rows that keep their receiver attachment separate from
  their declaring identity.
- **Package target:**
  [Traversal target-framework policy](traversal-target-framework-policy.md#traversal-and-selection-are-different-policies)
  separates package-local selection from traversal; package children use the
  Selection default.
- **Package children:** [Package library scope](package-library-scope.md) and
  [Package asset-selection correspondence](package-asset-selection-correspondence.md)
  own the selected compile population and its aggregate scope.
- **Visibility default:** [API population scope](api-population-scope.md) owns
  the public-facing default and explicit widening.
- **Symbol search:** `find` owns pattern search, so non-exact `type` and
  `member` input has a home other than a listing.
- **Tool packages:**
  [Package Info tool measurements](package-info-tool-measurements.md#contract)
  owns the tool Library population and the tool target-framework slices.
- **Motivating assets:** System.Text.Json, Microsoft.Data.SqlClient 7.1.0,
  SkiaSharp.NativeAssets.Linux, System.Private.CoreLib, Newtonsoft.Json,
  Microsoft.TestPlatform.ObjectModel, dotnet-ef, dotnet-inspect and its RID
  packages, and the platform `Timer` name collision. See [Evidence](#evidence).

## Non-claims

This document does not own and does not decide:

- Library or package population membership, Count, or Rows;
- package asset selection, role preference, or library scope;
- the identity-line fields, RID disclosure, or tree layout of any command;
- collapse thresholds;
- type or member name resolution rules or overload addressing; or
- Info content or section names, including the proposed exact-member-name
  facts section.

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
- **Subject resolution:** a `library` source that selects more than one
  Library fails with a candidate list instead of silently picking one, and an
  exact Library selector resolves one Library occurrence, bound to package
  source, version, and target. The selector consumes the Library's asset path
  within the selected slice, not only its file name, because one slice can
  hold same-named Libraries: `dotnet-inspect.any` has
  `System.Security.Cryptography.Pkcs.dll` both at the slice root and under
  `runtimes/win/lib/net10.0/`. A candidate spelling is
  `library P --library <path>`, where a bare file name is accepted only when it
  is unique in the slice. A
  namesake or first-Library recommendation under
  [Package library scope](package-library-scope.md#exact-scope) may appear as
  a tip on that failure, never as the silent subject.
- **Narrowing:** `library L --namespace N`, so that every collapsed row can be
  expanded.
- **Namespace as input:** `library N`, where `N` names a namespace rather
  than a Library, is the same request as `library L --namespace N` when
  exactly one Library `L` in the selected source declares `N`, whether by
  definitions or forwarders. For example, `library System.Text.Json.Nodes`
  equals `library System.Text.Json --namespace System.Text.Json.Nodes`. An
  input that names a Library resolves as that Library. When several Libraries
  declare `N`, the request fails with `library L --namespace N` candidates, as
  obligation 1 requires; for example, the platform declares
  `System.Collections.Generic` in both `System.Collections` and
  `System.Runtime`.
- **Retirement:** the current `type` listing context retires only after the
  Library children view passes positive CLI format and mode gates and
  Browser/Wasm adoption gates. The host-neutral operation and its forwarder
  semantics remain after that presentation context retires.

### Package (owners: Progressive disclosure and Package library scope)

- **Target:** the package children view is a package-local selection
  consumer, like Package Info. Without `--tfm` it uses the Selection default,
  PackageHouse's `HighestAvailable`, not the Traversal default; `--tfm`
  requests an explicit target. See
  [Traversal and selection are different policies](traversal-target-framework-policy.md#traversal-and-selection-are-different-policies).
- **Children:** the selected compile population in aggregate scope, one row
  per Library with its public-surface Type declaration Count. Each count reads
  only the compile asset that compile selection chose.
- **Identity line:** source and selected target, then two groups. The first
  lists the peer asset directories present (`ref`, `lib`, `runtimes`). The
  second adds detail for `runtimes`: its child RID directories, capped to a
  count when long. For example,
  `Microsoft.Data.SqlClient 7.1.0 (NuGet, net9.0; ref, lib, runtimes; runtimes: unix, win)`
  reads as "three peer directories, and `runtimes` contains `unix` and
  `win`".
- **Package-root runtime assets** (`runtimes/` beside `ref` and `lib`) are
  disclosed only on the identity line. A `runtimes/` directory nested inside
  a tool payload is part of the tool Library population instead. Whether a
  `runtimes/*/lib` asset is an implementation overlay of a compile Library or
  a separate Library is decided by package asset selection's correspondence,
  not by this view.
- **Addressing prerequisite:** this adoption reuses the exact Library selector
  from the Library proposal as the package-to-Library edge; each row carries
  the asset-path identity that selector consumes, and a row whose file name is
  not unique displays enough of that path to tell it apart.

#### Tool packages

A tool package's header names its tool settings format as it appears in
`DotnetToolSettings.xml`: the root element and its version, for example
`DotNetCliTool v2`. The nuspec package type (`DotnetTool` or
`DotnetToolRidPackage`) decides which of three shapes applies.

- **Pointer package:** a `DotnetTool` package whose `DotNetCliTool v2`
  settings list `RuntimeIdentifierPackages` and that ships no tool payload.
  Its children are those RID packages, one row per RID with its package ID,
  and each row leads to `package <id>`. It does not render the contents of a
  RID package as its own.
- **Payload package:** a `DotnetTool` or `DotnetToolRidPackage` with a
  `tools/<tfm>/` slice. Its children are the tool Library population that
  [Package Info tool measurements](package-info-tool-measurements.md#contract)
  defines: DLLs in the selected slice, without satellite or
  `runtimes/<rid>/native` entries. The entry-point Library named in the
  settings comes first and is marked. At `-v:m`, the tree may collapse the
  remaining Libraries into one counted dependencies row; `-v:n` and `-v:d`
  list every Library.
- **Native RID package:** a `DotnetToolRidPackage` whose settings name an
  `executable` runner and that has no TFM-like `tools/<tfm>/` segment. It has
  no selected target and no Libraries. It shows its RID and states that it
  has no managed Libraries; it never renders an empty inventory.

Rows use the same public-surface Type declaration Count as other packages, so
a count means the same thing everywhere; `--all` widens it.

```text
dotnet-ef 10.0.12 (NuGet, DotNetCliTool v1, net8.0; tools; command: dotnet-ef)
├─ dotnet-ef (entry point) (n)
└─ ef (n)

dotnet-inspect 0.26.0 (NuGet, DotNetCliTool v2; command: dotnet-inspect)
└─ RID packages (6)
   ├─ win-x64: dotnet-inspect.win-x64
   ├─ …
   └─ any: dotnet-inspect.any

dotnet-inspect.any 0.26.0 (NuGet, DotNetCliTool v1, net10.0; tools; command: dotnet-inspect)
├─ dotnet-inspect (entry point) (n)
└─ Dependencies (72 Libraries)

dotnet-inspect.osx-arm64 0.26.0 (NuGet, DotNetCliTool v2, osx-arm64; command: dotnet-inspect)
└─ No managed Libraries (native executable)
```

### Type and member (owners: the `type` and `member` command designs)

- **Exact resolution:** ambiguous input fails with a candidate list, and each
  candidate is spelled as input that resolves uniquely. Pattern, prefix, and
  missing-name input fails with tips for `find` and `library`.
- **`member T` without a member name** is an error. It does not render the
  Type's Members or forward to `type T`; its message names `type T` as the
  command that shows a Type's Members.
- **Member tree:** an identity line with the overload count, then one
  signature per overload.
- **Member Info:** no named facts section exists for an exact member name.
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
| `library System.Text.Json.Nodes` | Fails trying to acquire a NuGet package with that name | Namespace input needs Library-scoped resolution |
| dotnet-ef 10.0.12 | `DotnetTool`; `DotNetCliTool Version="1"`; `tools/net8.0/any/` with 2 Libraries and `shims/win-*` launchers | Payload package rows come from the tool Library population |
| dotnet-inspect 0.26.0 | `DotnetTool`; `DotNetCliTool Version="2"` listing 6 RID packages; the archive holds only `tools/any/any/DotnetToolSettings.xml`. Today `package dotnet-inspect` reports the 73 Libraries of `dotnet-inspect.any` | A pointer package's children are its RID packages, not another package's payload |
| dotnet-inspect.any 0.26.0 | `DotnetToolRidPackage`; `DotNetCliTool Version="1"`; `tools/net10.0/any/` with 73 Libraries, including two `System.Security.Cryptography.Pkcs.dll` (slice root and `runtimes/win/lib/net10.0/`) | Entry point first; dependencies collapse at `-v:m` |
| dotnet-inspect.osx-arm64 0.26.0 | `DotnetToolRidPackage`; `DotNetCliTool Version="2"` with an `executable` runner; `tools/any/osx-arm64/` holds a 118 MB native executable and no DLLs | No selected target; the missing managed Libraries are stated |
| platform `Timer` | `type Timer` silently renders `System.Threading.Timer` | Obligation 1 requires a visible ambiguity failure |
| `member JsonSerializer` | Renders member-group tables duplicating `type` | Two commands render one subject's children |
| `type JsonElement --platform System.Text.Json` | Tree shows `Inherits`, `Properties`, `Methods`, and `Extension Methods` declared on `JsonSerializer`; `-S "Member Index" --count` is 62, including the five `extension:Deserialize:N` rows | Several children sections plus labeled context; attached extensions are counted children with a distinct row kind |
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
   selector, then the compact `-v:m` default, tree, and `--namespace`, and
   exhaustive `-v:n`/`-v:d` inventories, consuming the Library Type
   declaration population. Current listing paths remain.
2. **Listing retirement:** after the positive CLI and Browser/Wasm gates in
   the Library proposal, retire the `type` listing context.
3. **Package children:** compact and exhaustive views for library and tool
   packages, with Library rows addressed through the step 1 Library selector
   and tool pointer rows through `package <id>`.
4. **Exact `type` and `member`:** exact resolution, removal of `member T`
   without a name, the member tree, and exhaustive `-v:n`/`-v:d` member
   inventories for `type`. The `member` part waits for the
   exact-member-name facts section.

## Gates

This document's obligations are gated through each adoption, in Release,
against that adoption's motivating assets:

- obligation 1: `package dotnet-inspect` renders its RID packages rather
  than the `dotnet-inspect.any` payload; the platform `Timer` collision,
  Microsoft.TestPlatform.ObjectModel's three Libraries, and the platform
  `System.Collections.Generic` namespace each fail with candidates, and
  `library System.Text.Json.Nodes` renders the same output as
  `library System.Text.Json --namespace System.Text.Json.Nodes`;
- obligations 2 and 3: default (`-v:m`) output and the explicitly named
  facts section for each adopted command, with context never counted as
  children, and with forwarder rows (System.Text.Json) and attached extension
  rows (`JsonElement`'s five `JsonSerializer.Deserialize` extensions) counted
  as children and distinguished by row kind in every format;
- obligation 4: at `-v:m`, a tree's grouped or collapsed counts sum to the
  population Count, forwarders included; at `-v:n` and `-v:d`, every format
  lists every child, for System.Text.Json and System.Private.CoreLib;
- obligation 5: an empty compile population for SkiaSharp.NativeAssets.Linux
  and no managed Libraries for dotnet-inspect.osx-arm64; and
- each ladder edge: its row identity resolves through its exact gesture to the
  same child, including both same-named `System.Security.Cryptography.Pkcs.dll`
  rows in `dotnet-inspect.any`.

Until an adoption lands, every obligation is `unverified` for that command.
