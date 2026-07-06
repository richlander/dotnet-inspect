# Member target resolution

Member target resolution is the typed seam between user selectors, API surface
members, durable member anchors, and physical body evidence.

`MemberTargetResolver` owns semantic selection for a member within an `ApiType`.
It consumes a `MemberTargetSelector` rather than a loose tuple of strings, so
selector details survive past command-line parsing:

- normalized member name
- `Name:N` overload index
- `Name~digest` stable selector prefix
- generic method arity from `M<T>` / `M<TKey,TValue>`
- kind qualifiers: `operator:`, `explicit:`, and `extension:`

The resolver returns `ResolvedMemberTarget`, which carries the API member handle,
its `MemberAnchor`, selector/declaring overload indexes, and a `BodyTarget` when
the selected API member maps to a physical declaring member. Projected extension
methods use this body target to preserve the difference between the API target
and the member that owns IL/native metadata evidence.

Diagnostics are typed (`MemberTargetDiagnosticKind`) and include candidate
anchors for ambiguous or out-of-range selections. CLI commands should render the
diagnostic instead of falling back to partial string matching.

## Identity ownership

Member identity has two related vocabularies:

- **API identity** is owned by `ILInspector.Metadata.ApiMemberIdentity`. It
  creates `MemberAnchor` values, selector prefixes (`operator:`, `explicit:`,
  `extension:`), canonical signatures, and stable selector fingerprints. Product
  producers such as C# body diff should call this layer instead of building
  anchors locally.
- **Body identity** is owned by `ILInspector.Research.ResearchMemberIdentity`.
  It formats `MethodIdentity` subjects and API-derived `ResolvedMemberTarget`
  body aliases through one body canonicalization path. Body identity deliberately
  has a different type-name vocabulary from API identity because it mirrors
  `LibraryBodyIndex`/`MethodIdentity` evidence.

Conversion operators are a special API-identity case: C# overloads
`op_Implicit`, `op_Explicit`, and `op_CheckedExplicit` by return type. Their
API canonical signatures therefore include the DocId-style return suffix
`~ReturnType`, for example
`M:System.Decimal.op_Explicit(System.Decimal)~int`. Without the suffix, all
conversions with the same source parameter collapse to one anchor digest.

## Boundaries

- Lexical command helpers may still identify source/type/member argument slots,
  but semantic member resolution should flow through `MemberTargetResolver`.
- Commands that target API or body changes, such as `diff -m/--member`, should
  resolve selectors against the old/new API surfaces and filter by the resulting
  `MemberAnchor` identities rather than by re-parsing display text.
- Body evidence should flow through `ResearchMemberIdentity`, which formats
  `MethodIdentity` subjects and API-derived `ResolvedMemberTarget` body aliases
  with the same canonical spelling.
- `MemberAnchor` remains the durable user/agent-facing identity; producer-native
  references remain producer evidence and should not be replaced by selectors.
- The resolver lives in `ILInspector.Metadata`, so it stays SRM-only and has no
  decompiler dependency.
- Do not add local selector, canonical-signature, fingerprint, or
  anchor-construction helpers in producers. Add or extend the owning identity
  layer instead, then cover the bridge with a round-trip or alias-vs-subject
  test.
