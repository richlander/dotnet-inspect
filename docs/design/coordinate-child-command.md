# Coordinate child command

## Status and authority

This document is the focused owner for placing required subordinate
coordinates under an already selected CLI subject. The operator approved
`coordinate` as the child-command term on 2026-09-16.

The initial adoption target is `library coordinate`. It replaces the current
`library --il-offset`, `library --il-offsets`, and `library --heap` gestures
after equivalent behavior, output, discovery, and failure coverage exists.
Until that cutover, those options remain the executable product surface.

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

> A coordinate child command establishes a required subordinate coordinate
> within one already selected subject. Its sections select observations of that
> coordinate; no section, predicate, lens, or renderer is responsible for
> supplying the coordinate.

The parent command continues to identify and acquire the subject. The child
does not create another subject domain or another implementation architecture.
It creates a closed request grammar for a point or bounded coordinate
population whose observations are otherwise unavailable.

This yields four distinct CLI roles:

| Surface | Establishes |
| --- | --- |
| Subject command | Independently navigable subject and source context |
| Operation child | A distinct operation over that subject |
| Coordinate child | A required subordinate address within that subject |
| Section or lens | Evidence or representation at the established request |

`library diff` and `library graph` are operation children. `library coordinate`
is a coordinate child. `member -S IL` remains a representation lens.

## Admission rule

A subordinate selector should become a coordinate child when all of these are
true:

1. The parent subject and its owner-issued identity remain unchanged.
2. The additional coordinate is required before the relevant request can
   execute.
3. At least one observation is meaningless without that coordinate, and either
   coordinate resolution is itself the useful result, peer observations reuse
   it, or a bounded coordinate population has its own result document.
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

The child owns coordinate admission, scalar-versus-batch mode, and
coordinate-level failure topology. Sections own observations after successful
admission. Output options continue to own projection of the completed content.

## Target Library grammar

The target shape is:

```console
dotnet-inspect library coordinate ./My.dll 0x06000042+0x2f
dotnet-inspect library coordinate ./My.dll "#Strings:0x1a4"
dotnet-inspect library coordinate ./My.dll --file coordinates.txt
```

The examples lock the `library coordinate` placement and the distinction
between one positional coordinate and one coordinate file. Exact argument and
source-option binding remains with the CLI adoption, which must preserve local,
Package, Platform, and other admitted Library sources without giving one
positional slot several meanings.

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

The bare child renders one compact coordinate overview. For an IL coordinate,
that overview identifies the resolved member, instruction, and primary
interpretation. For a heap coordinate, it identifies the selected root, heap,
offset, and decoded value or typed failure. Exact sections then select richer
peer observations:

```console
dotnet-inspect library coordinate ./My.dll 0x06000042+0x2f \
  -S "Context: Instruction" -S "Context: Callsite"
```

The existing context section contracts remain authoritative. Adoption may
simplify names only through their owning section design; moving them under the
child does not silently rename or merge their evidence.

### Coordinate-file mode

`--file` selects a bounded coordinate population rather than one exact point.
The first adoption preserves the current sparse IL-coordinate file semantics:
input order, optional labels, per-line malformed-input rows, and partial useful
output. It returns a coordinate-explanation Document, not a scalar Library
inspection with several incidental sections.

Exact and file modes are mutually exclusive. File bounds, accepted coordinate
families, normalization, ordering, failures, and output schema remain with
[IL coordinate workflows](il-coordinate-workflows.md) and its adopting query.
The coordinate child does not imply debugger, profiler, dump, or trace format
parsers.

## Initial cutover

The initial CLI adoption must:

1. add `library coordinate` with exact IL, exact heap, and sparse IL-file modes;
2. route those modes through the existing Metadata, Instructions, Analysis,
   Research, and metadata-rendering owners;
3. provide a useful bounded bare-child result and structural discovery;
4. preserve typed failures, coordinate-file partial results, metadata-root
   binding, and current output capabilities;
5. update help, completion, examples, replay or sharing surfaces, and shipped
   skills; and
6. remove `--il-offset`, `--il-offsets`, and `--heap` from ordinary `library`
   inspection without forwarding aliases or a hidden compatibility route.

This is an intentionally breaking CLI placement change. The implementation
slice must classify it under
[CLI change classification](cli-change-classification.md) and name its exact
Release gates. This specification's target is currently **unverified**.

## Other candidate surfaces

The coordinate rule is intentionally narrow:

| Current or possible surface | Disposition | Reason |
| --- | --- | --- |
| `library --il-offset` | Adopt as `library coordinate` | One required point enables several peer method-body observations. |
| `library --il-offsets` | Adopt as `library coordinate --file` | A bounded coordinate population has its own ordered document and partial-failure semantics. |
| `library --heap` | Adopt as `library coordinate` | One required metadata point enables a coordinate value bound to the selected metadata root. |
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

### Graph placement

Graph follows the operation-child rule, not the coordinate rule. Subject
children provide a local single-seed view:

```text
package graph
library graph
type graph
member graph
```

Top-level `graph` constructs or reopens a Workspace for single-seed,
peer-seed, induced-set, or path questions. The graph-mode owner defines that
split; a graph seed is not a coordinate child merely because it is an input
address.

## Non-goals

This contract does not:

- introduce a universal coordinate CLR type;
- combine IL and metadata coordinate semantics;
- make every required section option into a child command;
- turn output row positions into inspected coordinates;
- move ordinary member inspection beneath Library;
- define resource extraction or graph execution; or
- retain obsolete option spellings solely for compatibility.
