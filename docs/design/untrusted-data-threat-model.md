# Untrusted data threat model

`dotnet-inspect` reads artifacts that may be malformed or intentionally hostile.
Inspection must not grant those artifacts authority to execute code, choose
network destinations, escape storage boundaries, or turn malformed input into
unbounded work.

This document records the trust boundaries and security rules for product code.
It is a living model: new acquisition paths, parsers, caches, or output features
must update the relevant boundary and verification obligations.

## Security objectives

The product must:

1. Inspect assemblies without loading or executing them.
2. Keep artifact-derived paths inside an explicit caller- or product-owned root.
3. Treat artifact-derived URLs as untrusted network destinations.
4. Bound CPU, memory, network, archive, and recursion work where hostile input
   can amplify it.
5. Keep malformed-input failures visible rather than returning plausible,
   success-shaped output.
6. Treat rendered artifact text as data, not terminal commands, markup
   authority, or agent instructions.

The product path remains SRM-only, NativeAOT-friendly, Roslyn-free, and free of
inspected-assembly loading. Those architectural constraints are security
boundaries as well as deployment choices.

## Trust boundaries

| Boundary | Untrusted input | Trusted side | Primary risks |
| --- | --- | --- | --- |
| Assembly inspection | PE headers, metadata tables, signatures, IL, resources | Metadata, Instructions, Analysis, Decompiler | Parser crashes, recursion or allocation denial of service, path derivation, misleading identities |
| Package acquisition | `.nupkg` / `.snupkg`, nuspec and package file names, feed responses | Package extraction and cache roots | Archive traversal, disk exhaustion, cache poisoning, dependency confusion |
| Symbols and source | Portable/embedded PDBs, SourceLink maps, document names, checksums, source URLs and content | PDB sessions, source cache, source rendering | SSRF, local-file access, cache escape, oversized downloads, spoofed provenance |
| Restored project inputs | `project.assets.json`, `.deps.json`, runtime/tool settings, paths within those files | Project and dependency resolvers | Path confusion, unintended file reads, excessive graph expansion |
| Filesystem output | Resource names, generated file names, user-selected output paths | Explicit output or cache directory | Arbitrary write, overwrite, symlink/reparse escape, partial output |
| Presentation | Names, documentation, source, paths, diagnostics, package metadata | Terminal, Markdown/JSON consumers, agents | Terminal control injection, broken structured output, prompt-like content treated as authority |

User-supplied local paths are trusted as *locations the user chose*, but the
contents found there are not trusted. Product cache directories and
process-created temporary directories are trusted roots; names appended beneath
them are not trusted unless derived from a cryptographic key or validated
component.

## Existing controls

### Assemblies are parsed, never loaded

Assembly, metadata, and method-body paths use
`System.Reflection.PortableExecutable` and `System.Reflection.Metadata`. Product
inspection must not introduce `Assembly.Load`, `AssemblyLoadContext`, reflection
over inspected binaries, module initializers, or dependency resolution that
executes target code.

Reader-backed values remain inside their owning session. Values that cross a
session boundary are copied or reduced to immutable tokens and shapes. This
prevents use-after-dispose and avoids lending privileged readers to higher
layers.

### Package archives use traversal-aware extraction

NuGet package extraction uses `ZipFile.ExtractToDirectory`, which rejects
archive entries that escape the destination directory. Extraction occurs under
process-created temporary directories before the validated content is committed
into product caches (`FileSystemPackageStore.CommitAsync`).

Symbol-package (`.snupkg`) PDB acquisition does not extract the archive to disk.
`SnupkgPdbReader` opens the archive in memory, matches candidate entries by file
name only (never by attacker-controlled directory paths), validates each
candidate's PDB header and debug GUID, and returns the matching bytes. Those
bytes are then persisted through `IPdbStore`; the filesystem implementation
(`FileSystemPdbStore`) maps only store-composed, per-segment-validated keys onto
disk, so no archive-entry name is ever used as an output path.

Package identifiers and versions used as cache path components pass
`NuGetCache.ValidatePathComponent`, which rejects empty or whitespace values,
traversal (`..`), separators, volume qualifiers (`:`), null characters, and
otherwise rooted values before any cache path is built. Store keys (PDB cache
keys and package entry paths) resolve through the shared `StorePath.ResolveUnderRoot` guard: it splits
on `/`, rejects any segment that is empty, `.`, `..`, separator-bearing,
volume-qualified (`:`), null-character-bearing, or otherwise rooted, then
verifies the composed absolute path stays under the store root with a final
`Path.GetFullPath` containment check. This closes the Windows volume-reset
vector where `Path.Combine(root, "C:..", ...)` would discard the root, while
still permitting the interior dots of a real PDB or assembly file name. A PDB
file name recovered from untrusted PE debug metadata that is not a usable single
segment yields a graceful "no symbols" miss rather than an output path. General
cache entries use SHA-256-derived keys through `CoreCache`.

Archive containment does not itself bound expanded bytes, entry count, or disk
consumption. Resource budgets remain an open requirement below.

### Untrusted JSON rejects duplicate properties

JSON does not define how duplicate object keys resolve, so two readers of one payload can
disagree. `DotnetInspector.Core.HardenedJson` (and its `ILInspector.Metadata.SourceLinkJson`
counterpart, kept separate because Metadata sits below the Core infrastructure layer) parses with
`AllowDuplicateProperties = false`, so such a payload fails visibly instead of binding one of
several possible readings.

This is not hypothetical for SourceLink. `SourceDocumentPathResolver` orders mappings by
descending pattern length and takes the first match, while the repository-URL reader in
`AssemblyInspector` takes the first value naming a known host. Given a repeated key under
`documents`, those rules select different entries, so a hostile PDB could resolve source from one
origin while the tool reported provenance from another. Rejecting the map closes that gap.

Feed responses, package contents, `project.assets.json`, `.deps.json`, and product cache entries
parse through the same guard. Callers that already treated malformed JSON as "no data" now treat
duplicate-bearing JSON the same way; that is fail-closed, but it does not by itself convert those
callers to explicit failure reporting, which remains open work below.

`runfaster` still parses its trace inputs directly and is not yet covered.

### Artifact-derived source URLs use an SSRF-hardened client

SourceLink and other artifact-derived fetches must use
`HttpClientFactory.SharedUntrustedFetch`. It allows only HTTP(S), resolves every
connection including redirects, and rejects loopback, link-local, private,
CGNAT, multicast, unspecified, and reserved destinations. Callers must not
replace it with the general shared client.

Checksums from portable PDB documents authenticate source content when the
workflow claims authored-source integrity. A reachable URL without a matching
checksum is not equivalent to verified source.

## Resource extraction contract

Manifest resource names are attacker-controlled metadata. They are not safe
paths merely because a compiler normally emits dotted logical names.

Resource extraction therefore follows this contract:

- Preserve nested resource paths only after validating every component.
- Treat both `/` and `\` as separators on every platform.
- Reject rooted and drive-qualified names, empty components, `.` and `..`,
  control characters, a fixed portable set of invalid filename characters,
  alternate data stream syntax, trailing dot/space aliases, and Windows device
  names.
- Normalize and case-fold destination identities so extraction has one
  deterministic collision policy across operating systems and filesystems.
- Preflight all resource data ranges and destination paths before creating the
  output directory or writing any resource.
- Reject malformed resource payloads, duplicate destinations, and
  file/directory prefix conflicts.
- Reject existing destination files; extraction never overwrites.
- Reject existing symbolic-link or reparse-point components beneath the output
  root.
- Open destination files with create-new semantics so a concurrent file
  creation cannot become an overwrite.
- Surface the failure to the caller. Do not silently skip an unsafe name.

The caller-selected output directory is the trust anchor. Portable .NET APIs do
not currently provide an atomic cross-platform "open beneath this directory,
never follow links" primitive. Reparse checks and create-new writes narrow the
race window, but a local adversary able to mutate the chosen directory
concurrently remains a residual risk. Security-sensitive automation should use
a fresh directory with permissions restricted to the invoking user.

## Required rules for new code

These are requirements for new or changed paths and audit targets for existing
code. They do not claim that every legacy scanner already distinguishes
malformed input from an ordinary empty or zero-valued result.

### Derived paths

Validate before side effects. Prefer rejection over sanitization: sanitization
can create collisions and hides the artifact's original identity.

For each artifact-derived path:

1. Define the trusted root.
2. Parse the untrusted value into components.
3. Reject rooted, traversing, empty, control, device, and platform-alias forms.
4. Resolve the full destination and prove it remains beneath the root.
5. Preflight collisions across the full operation.
6. Refuse unintended overwrite.
7. Keep failures visible and attributable to the offending artifact.

Do not use `Path.Combine(root, untrustedValue)` as a containment check.

### Parsing and resource consumption

Use existing guarded signature, metadata, IL, and recursion limits. A new
decoder must identify:

- maximum input size;
- maximum nesting or graph depth;
- maximum produced rows or objects;
- cancellation and timeout behavior;
- malformed-input failure shape.

Do not catch `Exception` and return an ordinary empty result for malformed
input. Empty means the producer completed and found no evidence; failure is a
different state.

Metadata relationship and name traversal follows the
[bounded metadata traversal](bounded-metadata-traversal.md) contract. Cycles,
depth, count expansion, and projected text are separate budget dimensions;
exceeding any one produces a visible rejection rather than a partial identity.

### Network and caches

Network capability policy is enforced in the shared HTTP handler after the
attempt is recorded for diagnostics and before it reaches the transport.
Traffic families that require explicit authorization, currently vulnerability
data, must run inside their matching `NetworkTelemetry.Allow` scope. Offline
mode remains the broader prohibition over every traffic family.

Network access derived from inspected content must be explicit in the command
surface, use the untrusted-fetch client, have a timeout, and retain provenance.
Cache paths must be hashed or use validated single components. Downloads should
land in temporary files and become visible atomically after validation.

### Presentation

Artifact text can contain Markdown delimiters, newlines, terminal control
characters, URLs, or prompt-like instructions. Renderers must preserve output
structure and must not interpret inspected text as authority. JSON serializers
provide structural escaping; Markdown, table, plain-text, and stderr paths need
equivalent control-character and delimiter discipline.

## Verification obligations

Security-sensitive parsers and writers require close negative fixtures, not
only ordinary compiler output.

| Surface | Required evidence |
| --- | --- |
| Resource extraction | Traversal and rooted names rejected before writes; valid nested and empty resources retained; malformed ranges rejected; separator/case aliases collide; existing file preserved; device/control names rejected |
| Archive extraction | Zip-slip fixture; expanded-size and entry-count policy tests once budgets exist |
| Metadata and signatures | Malformed table/blob fixtures, depth/size limits, no process crash |
| SourceLink | Private/loopback/redirect targets rejected; allowed public target and checksum path retained; duplicate `documents` entries rejected rather than resolved |
| Untrusted JSON | Duplicate properties rejected at top level, nested, and from UTF-8 bytes; case-distinct and sibling-repeated names still parse |
| Cache paths | Traversal/separator components rejected; content-addressed keys deterministic |
| Structured output | Untrusted delimiters/control characters cannot escape the selected format |

## Open work

1. Extend duplicate-property rejection to `runfaster` trace parsing.
2. Define package, symbol, source-download, and decompressed-archive byte and
   entry-count budgets.
3. Audit every product write against the derived-path rules, including symbol
   server cache path construction.
4. Audit Markdown, plain-text, and stderr rendering for terminal control
   characters and structure injection.
5. Implement the [bounded metadata traversal](bounded-metadata-traversal.md)
   migration and expand malformed PE/PDB product-entry-point coverage around
   graph depth, row count, and allocation limits.
6. Migrate legacy metadata scanners that collapse malformed reads into empty or
   zero-valued results onto explicit failure-bearing outcomes.
7. Revisit filesystem containment if .NET exposes a portable atomic
   no-follow/open-beneath primitive.
