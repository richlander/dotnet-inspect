# Coordinate child command

## Status and authority

This document is the focused owner for placing required subordinate
coordinates under an already selected CLI subject. The operator approved
`coordinate` as the child-command term on 2026-09-16.
Focused and end-to-end CLI adoption is tracked by
[#7307](https://github.com/richlander/dotnet-inspect/issues/7307).

The initial adoption is complete: `library coordinate` is the executable
surface for exact IL points, exact metadata-heap points, and bounded sparse
IL-coordinate files. The former `library --il-offset`,
`library --il-offsets`, and `library --heap` options are removed; rejected
invocations receive replacement guidance and never execute the old behavior.

This document owns command placement and request-shape boundaries. It does not
redefine Library identity or acquisition, metadata roots, IL-coordinate
meaning, heap decoding, method-body facts, section contents, or rendering.
Those remain with their existing owners.

Related documents:

- [Command transition model](command-transition-model.md) owns the broader
  distinction among subjects, operations, coordinates, observations, lenses,
  traversal, and projection.
- [Method body inspection](method-body-inspection.md) owns the shared
  member-selector and IL-coordinate query model.
- [IL coordinate workflows](il-coordinate-workflows.md) owns sparse
  MethodDef-token plus IL-offset explanation.
- [Metadata table projection](metadata-table-projection.md) owns metadata heap
  coordinates and values.
- [ReadyToRun CLI projection](readytorun-cli-projection.md) owns metadata-root
  selection.

## Claim

> A coordinate child command establishes the closed grammar for a required
> subordinate coordinate family within one already selected subject. Its exact
> mode admits one point; a bounded population mode is a multi-coordinate
> operation over that same family. No section, predicate, lens, or renderer is
> responsible for supplying the coordinate input.

The parent command continues to identify and acquire the subject. The child
does not create another subject domain or another implementation architecture.
It creates a closed request grammar for exact points and closely paired
coordinate-population operations whose observations are otherwise unavailable.

This yields four distinct CLI roles:

| Surface | Establishes |
| --- | --- |
| Subject command | Independently navigable subject and source context |
| Operation child | A distinct operation over that subject |
| Coordinate child | A required subordinate address within that subject |
| Section or lens | Evidence or representation at the established request |

`library diff` is an operation child. `library coordinate` is a coordinate
child. `member -S IL` remains a representation lens.

## Admission rule

A subordinate selector should become a coordinate child when all of these are
true:

1. The parent subject and its owner-issued identity remain unchanged.
2. The additional coordinate is required before the relevant request can
   execute.
3. At least one observation is meaningless without that coordinate, and either
   coordinate resolution is itself the useful result or peer observations
   reuse it.
4. The bare child has a meaningful bounded default result rather than existing
   only to make one section syntactically legal.
5. The coordinate can be parsed and resolved as typed request input before its
   evidence producers run.
6. Keeping it as a parent option would leave ordinary subject inspection with
   mutually exclusive section, output, or execution modes.

Conditions 2 and 4 are mandatory. One required predicate for one section is
not enough when the predicate only filters that producer; an exact heap address
qualifies because resolving the address is itself the coordinate result.
Implementation size is not evidence: complex production work may remain behind
an ordinary section when the request shape is unchanged.

The child owns exact-coordinate admission. A population gesture beneath it is
admitted only as an explicit multi-coordinate operation mode with an
owner-declared bound, acquisition plan, result Document, ordering, and
coordinate-local failure topology. Sections own observations after successful
exact admission. Output options continue to own projection of completed
content.

## Library grammar

The shape is:

```console
dotnet-inspect library coordinate 0x06000042+0x2f --library ./My.dll
dotnet-inspect library coordinate "#Strings:0x1a4" --library ./My.dll
dotnet-inspect library coordinate --file coordinates.txt --library ./My.dll
```

The examples lock the `library coordinate` placement and the distinction
between one positional coordinate and one coordinate file. The first positional
value after `coordinate` is always the point to resolve, matching the
focus-first grammar of `type <Type> --library <source>` and
`member <Type> <Member> --library <source>`. It is never the Library location.

Library acquisition uses named source options:

```console
dotnet-inspect library coordinate 0x06000042+0x2f --library ./My.dll
dotnet-inspect library coordinate 0x06000042+0x2f \
  --package System.Text.Json@10.0.0 \
  --library lib/net10.0/System.Text.Json.dll
dotnet-inspect library coordinate 0x06000042+0x2f \
  --platform System.Private.CoreLib
```

The child adopts the existing source-context roles of `--library`, `--package`,
`--platform`, and their applicable selectors. The bare `library` inspection
command may retain its historical positional source, but the child does not
inherit that positional meaning: doing so would make its first value alternate
between the coordinate being sought and the location in which to seek it.

### Exact coordinate mode

One positional coordinate selects one typed subordinate address:

| Coordinate family | Example | Owning meaning |
| --- | --- | --- |
| Method body | `0x06000042+0x2f` | MethodDef token plus IL offset in the selected Library generation |
| Metadata heap | `#Strings:0x1a4` | Heap kind plus offset in the selected metadata root |

The lexical forms are not portable identities by themselves. Admission binds
the parsed coordinate to the selected Library generation and, for metadata, to
the selected metadata root. Output and sharing retain those owner-issued
identities rather than reconstructing them from display text.

The bare child preserves the family-specific bounded default. For an IL
coordinate, it selects every applicable coordinate-scoped section: Source
Location, Member, Instruction, Exception, Callsite, and Return Address. The
last three render only when their owning evidence applies. For a heap
coordinate, the bare child selects the existing heap-coordinate section with
the selected root, heap, offset, and decoded value or typed failure. Explicit
sections narrow those defaults to requested peer observations:

```console
dotnet-inspect library coordinate 0x06000042+0x2f --library ./My.dll \
  -S "Context: Instruction" -S "Context: Callsite"
```

The existing context section contracts remain authoritative. Adoption may
simplify names only through their owning section design; moving them under the
child does not silently rename or merge their evidence.

### Coordinate-file mode

`--file` selects a bounded coordinate population rather than one exact point.
It is a multi-coordinate operation mode beneath the Coordinate child, not
unary Library inspection. The first adoption preserves optional labels,
per-line malformed-input rows, partial useful output, and existing output
capabilities within the admitted 1,024-significant-record population. It
returns an ordered coordinate-explanation Document, not a scalar Library
inspection with several incidental sections.

`library coordinate --file` emits every valid or malformed row in source-file
order; row windows apply after that ordering. This deliberately differs from
the retired batch option, which grouped malformed rows before valid coordinate
rows. A mixed valid/malformed Release fixture proves full order and head/tail
selection.

Exact and file modes are mutually exclusive. File bounds, accepted coordinate
families, normalization, ordering, failures, and output schema remain with
[IL coordinate workflows](il-coordinate-workflows.md) and its adopting query.
That owner counts valid and malformed significant records together, rejects
record 1,025 before Library acquisition without partial rows, and gates the
boundary plus one. The coordinate child does not imply debugger, profiler,
dump, or trace format parsers.

## Completed cutover and gates

The intentionally breaking placement change is complete. Ordinary `library`
help and parsing no longer expose the three parent options, and the
rejection-only guard reports the corresponding `library coordinate`
replacement for separated, `=`, and `:` spellings. The guard follows parser
ownership: a retired option name consumed as another option's required value is
that value, not a retired request. It does not forward, bind, or execute the
retired request.

Release CLI gates cover:

- `LibraryCoordinateCommand_ImplicitlySelectsSections` and
  `LibraryCoordinateCommand_MemberSelectionAllowsNonInstructionBoundary` for
  bounded exact IL defaults and section-specific validity;
- the `LibraryCommand_IlOffset*` context tests, now driven through
  `library coordinate`, for member, instruction, exception, callsite,
  return-address, source, projection, and count behavior;
- `MetadataLens_HeapCoordinate_RendersTheValueAtThatAddress` and neighboring
  heap-coordinate tests for metadata-root binding, spellings, discovery,
  cache bypass, and typed failures;
- `LibraryCoordinateCommand_FilePreservesSourceRecordOrder`,
  `LibraryCoordinateCommand_FileWindowsSourceRecordOrder`, and
  `LibraryCoordinateCommand_FileLimitFailsBeforeLibraryAcquisition` for
  ordering, row windows, and the 1,024-record admission bound; and
- `LibraryCommand_RemovedCoordinateOptionsGiveReplacementGuidance`,
  `LibraryCommand_RemovedInlineCoordinateOptionsGiveReplacementGuidance`,
  `LibraryCoordinateCommand_FileValueMayMatchRetiredOptionName`, and
  `LibraryCoordinateCommand_HelpShowsFocusAndNamedSources` for retirement,
  parser ownership, help, and replacement guidance; and
- `LibraryCoordinateCommand_FileStructuralDiscoveryReadsNeitherInput` and
  `LibraryCoordinateCommand_FileEffectiveDiscoveryReadsCoordinateInput` for
  the input-free structural and materializing effective-discovery boundary.

## Other candidate surfaces

The coordinate rule is intentionally narrow:

| Current or possible surface | Disposition | Reason |
| --- | --- | --- |
| Retired `library --il-offset` | Replaced by `library coordinate` | One required point enables several peer method-body observations. |
| Retired `library --il-offsets` | Replaced by `library coordinate --file` | A bounded coordinate operation has its own ordered document, acquisition plan, and partial-failure semantics. |
| Retired `library --heap` | Replaced by `library coordinate` | One required metadata point enables a coordinate value bound to the selected metadata root. |
| Future `member coordinate` | Valid future adopter, not initial scope | A member-relative IL offset could reuse the same method-body query once a useful peer coordinate view and bare result are defined. |
| `library --metadata-root` | Keep as a selector | It chooses the metadata image in which all metadata sections and coordinates are interpreted; it does not identify one subordinate point. |
| Body Shapes `Kind=...` | Keep as a predicate | It narrows one observation producer and does not establish a reusable coordinate. |
| `--row`, `--value`, `--print`, `--urls`, `--paths`, `--out` | Keep as projections | They select or transfer completed content and do not authorize a new inspection address. |
| Workspace Root reopening and active navigation selectors | Keep with Workspace and navigation | They select Workspace input or structural focus; they are not subordinate coordinates within one inspected subject. |
| Package path or content selection | Keep with Package rows and payload projection | A file row may be selected and printed without creating a peer family of coordinate observations. |

### Resource extraction

`library --extract-resources` is a strong **operation-child** candidate, not a
coordinate child. It writes files, has destination and overwrite policy,
reports an extraction outcome, and is incompatible with ordinary section
rendering. A focused adoption should consider a resource-owned child such as
`library resources` or `library extract`, but this document does not choose its
name, side-effect contract, or content schema.

## Non-goals

This contract does not:

- introduce a universal coordinate CLR type;
- combine IL and metadata coordinate semantics;
- make every required section option into a child command;
- turn output row positions into inspected coordinates;
- move ordinary member inspection beneath Library;
- define resource extraction; or
- retain obsolete option spellings solely for compatibility.
