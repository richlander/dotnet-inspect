# Type API Declaration Inspection

## Owner and claim

Type API Declaration Inspection owns one declaration-only view of an exact
metadata type: select its requested declaration surface and return the complete
C# representation in one resource-free `InspectionEnvelope<T>`. CLI and
Browser consume that same operation rather than selecting members or composing
declarations independently.

The purpose is API review and exploration, not a compilable reference assembly.
Signatures and accessor declarations have no implementation bodies. Constants,
base types, interfaces, and generic constraints remain declaration facts.
The view does not promise standalone compilation or replace full implementation
source, exact-member source, member listings, or the existing type tree.

Supporting owners remain unchanged:

| Owner | Supporting role |
| --- | --- |
| [Assembly inspection](assembly-inspection-query.md) | Exact metadata identities, acquired participant lifetime, bounded extraction, and visible extraction failures. |
| `ILInspector.CSharp` | Model-based C# spelling and atomic type printing with `CSharpBodyPolicy.Skeleton`. |
| [SourceHouse](source-house.md) | Existing implementation-source settlement; declarations are a separate metadata view. |
| [Inspection layers](inspection-layers.md) | Completed shared operation and thin host adapters. |
| [Progressive disclosure](progressive-disclosure.md) | Explicit section selection and discovery. |
| [Rendering model](rendering-model.md#native-type-and-source-defaults) | Native singleton payloads, explicit formats, and composed documents. |

## Selection and scope

The request names one acquired assembly participant, one exact
`MetadataTypeDefinitionName`, and one of two scopes:

- **API-visible:** public, protected, and protected internal declarations.
  Private protected requires same-assembly access and is not included.
- **All declarations:** all declarations retained by the existing Metadata
  extractor, including internal and private members.

An explicitly selected non-public root is still the requested root. Scope
controls its member and nested-type declarations, not a second lookup that
silently substitutes another type. Metadata-owned accessibility facts select
members and individual property accessors; rendered signatures are not parsed
for visibility. Omitted C# accessibility spelling does not make a private
explicit interface implementation API-visible. All scope does not introduce
compiler-generated implementation artifacts that the declaration extractor
excludes. Receiver-attached extension-method discovery entries are not
declarations of the receiver; extension methods remain declarations of their
actual declaring type.

The selected type includes its nested declaration subtree under the same
visibility rule. Selecting a nested type retains its containing declarations
as context shells, without including their unrelated members or sibling types.
Structured declaration identity, rather than dotted display-name matching,
establishes containment. Referenced types are named, not recursively expanded.
This is not an assembly-wide declaration listing.

This scope is intentionally independent of the strict-public default member
listing and of full-type implementation source, which includes non-public
implementation members.

## Completion and failures

The completed result retains the requested type identity, declaration scope,
declaration text when available, and explicit failure evidence. Envelope
diagnostics and PortableProjection survive host projection. Until a canonical
Workspace Share exists for this view, PortableProjection is explicitly
non-projectable.

Extraction uses the existing acquired participant and explicit caller-supplied
bounds. Browser supplies its existing bounded surface policy. Truncation,
rejected input, or incomplete metadata needed for the selected declaration
cannot become an apparently complete smaller declaration. A conclusive missing
type is distinguishable from unavailable inspection. Failure ownership applies
to the required containing shells and selected subtree, including declarations
rejected before they could enter the successfully extracted type set.

The CSharp printer's unavailable outcome or diagnostic evidence is retained.
Unsupported declarations are not silently deleted, base types are not stripped
to make printing work, and a failed declaration is not replaced with decompiled
or authored source. The shared operation materializes the result before
returning; no reader, lease, callback, or mutable API model escapes.

## Production adoption and rendering

The focused delivery is [#7984](https://github.com/richlander/dotnet-inspect/issues/7984),
within production adoption trackers
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512) and
[#7177](https://github.com/richlander/dotnet-inspect/issues/7177).
There are three delivery steps:

1. Expose the selected declaration surface and existing CSharp printer through
   the completed shared inspection.
2. Adopt it in CLI `type ... -S "API Declarations"`, with API-visible scope by
   default and `--all` for all declarations.
3. Adopt it in Browser as an explicit choice in the existing type source
   viewer, including the all-declarations choice.

The CLI section is discoverable and explicit-only; no automatic verbosity
preset gains it. A singleton result uses native code output. `--markdown`
uses the existing Markout `CodeSection`; structured formats retain the typed
completed result and diagnostics. Mixed selections remain composed documents.

Browser reuses its existing code viewer, operation ownership, cancellation,
and stale-result handling. Selecting declarations changes the request identity,
so an earlier implementation-source result cannot satisfy a declaration view
or overwrite it. The control lives within the type source surface rather than
adding a persistent top-level tab.

The native CLI writer and Browser syntax-highlighted viewer are intentional
host-specific presentation paths over completed text. Neither host performs
declaration filtering or body removal. Markout remains the CLI document
renderer. There is no new rendering engine or retired inspection engine.
The added selection policy exists to make the requested API-review experience
available in both production hosts, not to harden trusted internal callers.

## Terminology and analogous evidence

The [runtime API proposal template][proposal] asks for an API declaration with
no method bodies. [SDK GenAPI][genapi] and the [runtime reference-source
workflow][reference-source] produce compilable stubs, a distinct artifact.
This view adopts the API-review terminology, not GenAPI code or its compilation
promise. Existing CSharp model printing supplies the implementation.

## Evidence

The motivating real asset is `System.Text.Json@10.0.5`, `net10.0`,
`JsonNamingPolicy`: its protected constructor belongs in API-visible output,
whereas its private/static implementation declarations appear only in all
scope. Neither output contains their implementation bodies.

The Release gates are `TypeApiDeclarationInspectionTests`,
`TypeApiDeclarationSectionTests`, the `CommandExecutionTests.Type_ApiDeclarations_*`
cases, and the declaration participant case in `SourceForwarderResolutionTests`.
The shared and section tests are PR-fast. The command cases inherit
`CommandExecutionTests`' slow classification and run in the focused pre-merge
gate and daily Deep Inspect rather than the PR-fast selection.
They cover native/Markdown/structured and mixed output, mixed-accessibility
property accessors, protected internal versus private protected, nested generic
context and subtree scope, enums and delegates, exact non-public roots, cyclic
type identity, and bounded extraction. Constant fields and nonsequential enum
values retain their metadata values, including interface constants and
floating-point signed zero. Receiver projections and private explicit
implementations exercise the declaration-membership boundary. Rejected nested
declarations and incomplete identity projections remain unavailable, not
apparently complete output or a conclusive missing type.

`BrowserTypeSourceOperationTests` compares the exported declaration envelope
with the shared operation and exercises reference-only package selection and
scope release. `ProductionFacadeContextTests` gates its completed-contract
transport. The focused `type-panel`, `type-source-managed-operation`,
`source-inspection`, and `engine-worker-source` TypeScript tests cover view
identity, stale completion, text/copy, unavailable diagnostics, and bounded
envelope transport. The existing published Source-comparison Firefox gate also
exercises both declaration scopes through the generated Wasm facade and the
production viewer's selection/copy controls.

[proposal]: https://github.com/dotnet/runtime/blob/f8546ab27b4eb75894e2574c7da144f039d4c95c/.github/ISSUE_TEMPLATE/02_api_proposal.yml#L19-L35
[genapi]: https://github.com/dotnet/sdk/blob/7d46d29649c386ab78235be51220cdda736ed9b1/src/Compatibility/GenAPI/README.md#L1-L5
[reference-source]: https://github.com/dotnet/runtime/blob/f8546ab27b4eb75894e2574c7da144f039d4c95c/docs/coding-guidelines/updating-ref-source.md#L1-L10
