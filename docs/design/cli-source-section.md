# CLI Source section

## Owner and claim

The CLI owns the explicit `Source` section for exact type and member inspection,
tracked by #8056:

> `type T -S Source` and `member T M -S Source` render the completed shared
> authored-first Source result for the selected target, retaining provider,
> provenance, and unsuccessful-attempt evidence rather than implementing a
> second fallback policy in the host.

The section is explicit-only. Ordinary type trees and member listings do not
acquire source because this section exists. `PDB Source` and `Decompiled Source`
remain independent provider-specific views with their current contracts.

The supporting owners are:

- [Ordinary Source policy](source-finding-producers.md#consumer-boundaries):
  authored preference, permitted fallback, failure classification, and
  cancellation.
- [Type source acquisition](type-source-acquisition.md) and
  [member source acquisition](member-source-acquisition.md): completed envelopes,
  exact target identity, bounded acquisition, native attempt evidence, and
  resource retirement.
- [Native source rendering](rendering-model.md#native-type-and-source-defaults):
  native singleton output and explicit format precedence.
- [Output shapes](output-shapes.md): document rendering and unary payload
  projection.

## Selection and authorization

Type Source consumes `TypeSourceInspection.ExecuteAsync`. Verified authored
output retains its issued primary-document scope, mapping strength, partiality,
and additional-document references. It is not a promise to assemble every part
of a partial type or remove unrelated declarations from the original document.
Decompiled fallback is the complete exact type, independent of member-listing
accessibility.

Member Source consumes `MemberSourceInspection.ExecuteAsync` for the exact
selected method or accessor. Existing overload and property/event accessor
selection applies; this does not broaden the shared operation to unsupported
member kinds or turn a member listing into a source request.

The CLI supplies its existing package authorization, repository paths,
local-source and adjacent-PDB permissions, selected assembly, and binding
context. A selected forwarded definition must retain its supplier identity.
Source Files and Source Locations remain inventories; their whole-document
printing and authored-part selection retain their separate meanings.

## Presentation and failure

A singleton Source payload prints native content by default. Explicit Markdown,
including explicit verbosity and environment format selection, uses the
existing document renderer. Native type-tree state alone does not request
Markdown. Multiple selected sections retain their identities in a document.

Unary `--print` projects the same selected content. Its structured formats use
the existing printable-document contract rather than redefining direct
inspection JSON. Limits, invalid-row failures, and format/projection boundaries
follow the existing code-payload path.

Provider and available provenance remain observable without modifying source
text to insert a host-authored header. Successful decompiler fallback retains
the reason authored source was unavailable or rejected; checksum failure must
not be reported as ordinary absence. The shared owner decides when fallback is
permitted. The CLI does not catch cancellation or arbitrary failures and
substitute decompiled text. If neither provider produces content, the command
fails visibly rather than printing an empty successful Source section.

Markdown uses the existing Markout code section. Native output and printable
documents use the existing CLI payload renderer. Provider, provenance, source
location, and fallback notes go to stderr, including with structured output;
they are evidence rather than tips. Authored provenance describes checksum
verification and available repository association, not an inferred transport.
Source can be acquired from local files, a repository, embedded content, or an
authorized remote source without changing that distinction.

Exact singleton `Source --json` has the focused source-generated shape
`{ provider, provenance, location?, fallback_reason?, content, type_evidence? }`.
Provider is `pdb` or `decompiled`. `type_evidence`, when available, describes the
authored attempt's document mapping: `scope`, `mapping_strength`, `is_partial`,
and `additional_documents` containing `original_path` and optional
`resolved_url`. This evidence remains authored-attempt evidence even when
decompilation supplies the content. It is not the structured C# Type document
introduced by #8163.

Direct Source JSON requires a singleton section; it does not silently discard
explicitly co-selected sections. `--print --json` instead retains the existing
printable-document projection, with provider and attempt context still visible
on stderr. These CLI-specific JSON projections describe the selected source
payload; they do not register a complete `--envelope` transport or serialize
resource-native settlement graphs. The completed managed envelopes retain
their existing non-projectable Share and diagnostics.

## Production adoption and follow-up

Both CLI commands adopt their existing completed host-neutral operation in this
delivery. Browser member Source already consumes the member operation. Browser
type Source uses the owner's PDB-latency-hedged operation; CLI type Source stays
serial. The scheduling distinction does not authorize a host-owned fallback
algorithm.

PR #8163 introduces the structured C# Type document core without changing
production source construction. Integration with that document is a separate
follow-up in #8083, after the relevant producer and SourceHouse adoption.
This slice does not depend on the unmerged PR or introduce an alternative
structured C# document.

The analogous implementations are the existing Browser consumers and the
CLI's explicit PDB/decompiled and unary document paths. No third-party code is
transferred. The motivating assets are this repository's
`CSharpText.MemberSlicing.MemberTextSlicer.ExtractMemberText` at repository
commit `917763586d2ab4a7ee51742c51a27389f5a19869`, in
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs`, with its compiled Portable
PDB and original source. `System.Text.Json@10.0.5`'s `JsonElement.GetArrayLength`
and `JsonNamingPolicy` provide package-level exact-member and full-type demos;
the installed System.Text.Json library provides the offline regression input.

## Evidence

Release `LocalRepoSourceProjectionTests.MemberSource_*` and `TypeSource_*`
cover both commands' authored preference, visible fallback, exact member
selection, native/Markdown/structured printing, and unsupported targets.
`Source_CoSelectedProviderSectionsRetainTheirIdentities` covers the independent
provider views. `DefaultTypeListingDoesNotAcquireSource` gates ordinary listing
output, while section pipeline and planning tests enforce explicit-only Source
selection, capabilities, and the exact-name versus `@Source` distinction.
`SourceForwarderResolutionTests.TypeSourceAcquisition_ReportsSelectedOpenFailure`
gates retained selected-image authority, including a forwarded target.

The full-type `TypeSource_FallsBackToCompleteExactType` and
`TypeSource_PrintTreeStaysNativeUnlessMarkdownIsExplicit` cases are Slow and
run in the focused pre-merge and daily Deep Inspect gates. The first compares
default and `--all` output rather than allowing listing accessibility to change
the complete type. The measured Slow member cases,
`MemberSource_PrintHonorsRowAndRenderedLineBoundaries` and
`MemberSource_EnvironmentMarkdownFramesPrintedDocument`, have the same owners.
Other new bounded offline cases are PR-fast.

Existing shared-query gates continue to own checksum rejection, cancellation,
finite bounds, and retired-resource publication.
`MemberSourceInspection_ChecksumFailureRetainsSymbolsForFallback` supplies
typed checksum-failure evidence; the CLI projects that existing classification
rather than introducing another acquisition policy.
