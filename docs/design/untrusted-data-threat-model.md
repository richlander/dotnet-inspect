# Untrusted data threat model

`dotnet-inspect` reads artifacts that may be malformed or intentionally hostile.
Inspection must not grant those artifacts authority to execute code, choose
network destinations, escape storage boundaries, or turn malformed input into
unbounded work.

This document records the trust boundaries and security rules for product code.
It is a living model: new acquisition paths, parsers, caches, or output features
must update the relevant boundary and verification obligations.

## Scope and priority

State intent; do not make promises. These libraries may eventually ship, and
the patterns in a tool like `mdi` may be reimplemented elsewhere, so the model
should be legible enough to copy — but "we thought about this" is not a
guarantee, and this document does not offer one.

**In scope:** harm to the machine or the user's tooling caused by untrusted
input arriving over the internet — a package from a feed, a PDB from a symbol
server, source fetched from SourceLink. Using a NuGet package is a trust
decision the user makes; that this tool is a no-commitment offer, easy to point
at anything, raises rather than lowers the bar for what it does with what it
finds.

**Out of scope:** deliberately opening artifacts you already know are hostile,
and running the tool elevated. Both are the caller's decision, and neither is a
boundary this tool can defend.

**Target: two to three nines, not five.** That is a real ceiling, and it is
what makes the ordering below meaningful rather than aspirational.

Work is ranked in this order, and the order is not negotiable when they compete
for attention:

1. **Reliability and security on correct, well-formed binaries.** This is first
   because it is the normal case, and because rigor on correct inputs is what
   makes reasoning about malformed ones possible at all. A tool that is wrong
   about ordinary assemblies has no standing to claim anything about hostile
   ones.
2. **Security on malformed binaries.** Everything downstream of a parser that
   accepts what it should have rejected.
3. **Reliability against environmental patterns that require an attacker to
   already have access to the machine.** Not zero — but at most two nines, and
   never ahead of the first two.

A crash on malformed input is a **reliability** defect, not a security one. It
is still worth fixing, and not only for tidiness: a crash means the code is one
step away from the same input producing an effect that does *not* announce
itself.

Local machine weirdness is tier 3. A symbolic link someone placed in a package
cache is a user doing user things, not an elevation of privilege. It would be a
security issue if a *package* could create it during extraction — and that
would be a defect in NuGet's restore, not in this tool.

## Strategy: reject, do not sanitize

When untrusted input violates a contract, the response is a typed rejection.
Sanitizing — accepting the artifact and repairing the offending value — is
rejected as a strategy for two reasons:

- **It is hard to get right, and being wrong is silent.** A sanitizer has to be
  correct about the full set of dangerous forms. A rejecter only has to be
  correct that *this* form is not allowed.
- **Where there is one mouse, there are many.** A field that contains a
  terminal escape sequence is evidence about the artifact, not about the field.
  Repairing it and continuing gives a malformed or hostile package a second
  chance to be interesting somewhere the check does not reach.

The consequence is a stack that must be **resilient to errors**, not resilient
to handling bad input. Those are different engineering problems and only one of
them is defensible: error paths are few, shared, and testable, while
bad-input-tolerance is diffuse and every new consumer re-litigates it.

Push the decision **down**. The best shape is a type whose construction *is*
the check, so choosing the type grants the capability and auditing is a search
rather than an argument. A rule enforced by *calling a function* is a rule a
new path can forget, and `string` is the type of both a checked and an
unchecked value.

`HardenedJson` is the repository's closest existing move in this direction, and
it is worth being precise about how far it actually goes: it is a `static
class` whose `Parse` returns an ordinary `JsonDocument`, so it is a single
named entry point that centralizes the policy — not a type whose construction
enforces it. Choosing it grants the capability; nothing stops a new call site
from reaching for `JsonDocument.Parse` instead, and some already do (see open
work). A centralized entry point is a real improvement over per-call-site
options and is cheap to audit by grep, but it is the weaker of the two shapes,
and new hardening should prefer the stronger one where the value crosses a
layer boundary.

One thing this rule does **not** forbid is escaping and encoding. Escaping a
value on the way into a sink — JSON string escapes, `vis(3)`-style visual
encoding of control characters — is a property of the *encoding*, applied
uniformly to all text, lossless and invertible. Sanitization is different: it
inspects a value, judges it dangerous, and alters or drops part of it so the
rest can proceed.

Both have to know which characters the sink interprets; a terminal has no
formal grammar, so encoding for one is not the free lunch that escaping for
JSON is. The distinction is what happens when you are **wrong** about that set:

- Under-encode, and the fix is to widen the set in one place. Nothing was
  lost, the decoder still recovers the original, and every call site inherits
  the correction at once.
- Under-sanitize, and the data is already gone, the judgment is spread across
  every call site that made it, and there is no decoder to appeal to.

Uniformity is the other half. An encoder does not decide *whether* a given
value is hostile, so it cannot be wrong about a value — only about the sink.
That is a much smaller thing to be right about, and it is written down.

### Failure messages carry no artifact data

A rejection message names the **user-supplied** input — the path, coordinate,
or package the caller asked for — plus the rule that fired and the location
within the artifact. It must not quote the offending value.

This is not in tension with keeping failures attributable: attribution is
satisfied by naming what the user wrote and where the problem is. The rejected
value is by construction the most hostile string encountered, and echoing it
into an exception message or onto `stderr` re-opens on the error path the exact
channel the check just closed.

See [metadata-table-projection.md](metadata-table-projection.md#safety) for
this model worked through on one surface, including how a hostile image stays
inspectable without handing over its bytes.

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

This is generic hardening, not a fix for a known divergence. It does **not** close the SourceLink
provenance gap. The repository-URL reader in `AssemblyInspector` stops at the first `documents`
entry, while `SourceDocumentPathResolver` orders mappings by descending pattern length and takes
the first match. A duplicated key keeps document order under a stable sort, so both readers land on
the same entry and duplication alone cannot make them disagree. They diverge on **distinct** keys,
which are well-formed and still accepted: a map whose first entry names a trusted host and whose
longer-matching entry names another origin reports the trusted repository while resolving source
from the other. That gap is open work below.

Feed responses, package contents, `project.assets.json`, `.deps.json`, and product cache entries
are *intended* to parse through the same guard, and most do. Callers that already treated malformed
JSON as "no data" now treat duplicate-bearing JSON the same way; that is fail-closed, but it does
not by itself convert those callers to explicit failure reporting, which remains open work below.

The coverage is not yet complete, and the gaps are on the feed path specifically:
`PackageExtractor` uses `HardenedJson.Parse` at four call sites but plain
`System.Text.Json.JsonDocument.Parse` at two more when reading registration pages, and
`NuGetFetch.NuGetApi` deserializes the service index, version index, and search responses through a
source-generated context that does not reject duplicates. `runfaster` also still parses its trace
inputs directly. Nothing gates the invariant, which is why the gaps persisted; see open work below.

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

That residual risk is **tier 3**: it requires an attacker who already has write
access to a directory the user chose. It is worth the checks already listed
here, and it is not worth trading against correctness on ordinary inputs.

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
7. Keep failures visible, and attributable to the input the **user** supplied.
   Do not quote the rejected artifact value.

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
structure and must not interpret inspected text as authority.

> **Status.** The two axes below are the **target model**, not current
> behavior. Today the metadata projector neutralizes control characters
> unconditionally and continues, with no flags on either axis. See open work
> item 10, and
> [metadata-table-projection.md](metadata-table-projection.md#status).

Presentation is **two orthogonal decisions**, and collapsing them into one flag
is a design error.

**Trust** — what happens when a concerning pattern is found:

| Flag | Behavior |
| --- | --- |
| *(default)* | abort at the first one |
| survey mode | keep going; report location and pattern kind, never content, bounded by the traversal budget |
| a `dangerously`-named skip | keep going and render the values anyway |

**Rendering** — how artifact text is spelled once something is printed:

| Flag | Behavior |
| --- | --- |
| *(default)* | visually encoded into an inert form |
| a `dangerously`-named raw mode | no visual encoding; the output format's own structural escaping still applies |

The axes are independent, and that is the design. Visual encoding is the
default on **every** artifact-text path, including underneath the trust-axis
skip — which is precisely what makes that skip defensible: it means "do not
refuse," not "attack my terminal." Reaching a live control character therefore
requires opting out of both axes, two separately named mistakes.

Rendering is **visual encoding, not neutralization**: control characters are
re-spelled into an inert, lossless, invertible form rather than removed or
replaced. The vocabulary is borrowed rather than coined — see below — and the
three properties together are what let the encoding be the default: it costs
the reader nothing, so there is no case for making it opt-in, and a default
cannot be forgotten by a new path. Nothing passes a flag to make a JSON
serializer escape `\u001B`.

This is established practice for tools that read hostile bytes:

- **BSD `vis(3)`** is where the vocabulary and the contract come from. It
  visually encodes arbitrary input into graphic characters only, and pairs the
  encoder with a decoder (`unvis`) so the transform is unique and invertible.
  Encode-without-a-decoder is not this pattern.
- **Caret notation** — `^[` for `ESC`, `^?` for `DEL` — is the standard
  spelling for C0 and `DEL`, used by `cat -v`, `less`, and `stty`, and dating
  to the PDP-6 up-arrow that the 1967 ASCII revision replaced with `^`. Specify
  it; do not invent a spelling.
- **`grep`** refuses binary content by default and prints `Binary file X
  matches` — location, never content — with `-a`/`--text` as the named opt-in,
  for exactly this threat.
- **`less`** renders control characters in caret notation by default and
  reserves raw output for `-r`.
- **`rustc`** made bidirectional control characters a deny-by-default hard
  error after Trojan Source (CVE-2021-42574) rather than stripping them. Its
  denied set is nine code points — the embeddings and overrides
  `U+202A`–`U+202E` and the isolates `U+2066`–`U+2069`. Unicode's
  `Bidi_Control` property adds the three marks `U+200E`, `U+200F`, and `U+061C`
  for twelve; do not attribute those three to `rustc`. Every one of the twelve
  is `Cf`, and none is anywhere near C1, so a rule written as "control
  characters" excludes all of them — which is why the encoded set is defined by
  Unicode property rather than by a hand-written list.
- **`binutils`** is the cautionary case rather than a model: its parsers are
  continuously fuzzed, and fuzzing has repeatedly found parser defects that
  received CVEs.

Do not copy `less`'s one mistake: its protection is conditional on stdout being
a TTY, and it degrades to `cat` when output is redirected — `-r` makes no
difference there, because nothing is being encoded in the first place. A pipe
is precisely where an agent or a log is.

JSON serializers provide structural escaping. Markdown, table, plain-text, and
stderr paths need equivalent discipline, and stderr especially — see
[failure messages carry no artifact data](#failure-messages-carry-no-artifact-data).

[metadata-table-projection.md](metadata-table-projection.md#safety) works this
through on one surface and specifies a concrete encoding.

## Verification obligations

Security-sensitive parsers and writers require close negative fixtures, not
only ordinary compiler output.

| Surface | Required evidence |
| --- | --- |
| Resource extraction | Traversal and rooted names rejected before writes; valid nested and empty resources retained; malformed ranges rejected; separator/case aliases collide; existing file preserved; device/control names rejected |
| Archive extraction | Zip-slip fixture; expanded-size and entry-count policy tests once budgets exist |
| Metadata and signatures | Malformed table/blob fixtures, depth/size limits, no process crash |
| SourceLink | Private/loopback/redirect targets rejected; allowed public target and checksum path retained; a duplicate `documents` key fails the parse rather than binding one of its values |
| Untrusted JSON | Duplicate properties rejected at top level, nested, and from UTF-8 bytes; case-distinct and sibling-repeated names still parse |
| Cache paths | Traversal/separator components rejected; content-addressed keys deterministic |
| Structured output | Control characters the projector recognizes cannot escape the selected format. `MdiContainmentTests` splices a payload spanning every control range the projector recognizes (a live `ESC [ 3 1 m` sequence, `BEL`, `DEL`, and a C1 control) into both a real `#Strings` entry and the metadata version stamp, then renders that assembly in every format through the three views that carry artifact text — table, heap, and overview — asserting no raw control character survives and every neutralized form is present. The `--references` view carries no artifact text, so it is asserted only against raw controls, as a regression net. Mutation-checked by disabling `MetadataTableProjector.IsControl` and by narrowing it to `ESC` alone. Two limits worth naming: the payload is `Cc` only, so a bidi override would not be noticed, and the assertion deliberately permits raw `CR`/`LF`/`TAB`. Format *delimiters* are not covered by this gate at all |

## Open work

1. Unify SourceLink provenance with source resolution. Today `AssemblyInspector`
   reports the repository from the first `documents` entry while
   `SourceDocumentPathResolver` selects by longest matching pattern, so a
   well-formed map can resolve source from one origin while provenance names
   another.

   State the fix as an invariant rather than a list of blocked tricks, because
   each enumerated mitigation has proven incomplete under review:

   > Reported provenance must describe the origin that source content is
   > actually fetched from, for every document the assembly resolves. When that
   > cannot be established for all of them, report no repository.

   Establish it on the **final resolved URL, after wildcard substitution and
   canonicalization** — not on the mapping text, and not on the mapping prefix
   alone. Four concrete ways the weaker forms fail, all reproduced:

   - Agreement on `owner/repo` ignores the commit, and
     `raw.githubusercontent.com` serves any commit reachable in a repository,
     including the head of an unmerged pull request. Two entries on one
     repository at different commits "agree" while serving different code.
   - `System.Uri` applies RFC 3986 dot-segment removal, so a mapping value
     containing `../` is fetched from the traversed-to path while a regex over
     the raw string reports the literal one.
   - Even a clean mapping is not enough. The wildcard suffix comes from the PDB
     document path, which is equally attacker-controlled, and
     `EscapeSourceLinkPath` leaves `..` intact. A benign
     `.../dotnet/runtime/<commit>/*` resolves
     `/_/../../../attacker/evil/main/Program.cs` to a URL that canonicalizes
     into `attacker/evil`.
   - `System.Uri` preserves percent-encoded separators verbatim: `..%2f` and
     `..%5c` survive canonicalization, so a "canonicalize, then prefix-check"
     step passes while a server that percent-decodes before resolving dot
     segments still traverses out. Reject encoded separators and encoded dot
     segments rather than assuming canonicalization removed them.

   These four are evidence that the weaker forms fail, not a specification of
   what to block; each was found only by attacking a previous formulation of
   this item, and no formulation reviewed so far has survived contact with the
   next reviewer. Treat the invariant as the requirement and this list as a
   regression floor: whatever check is implemented must ship with tests
   covering at least these cases, and passing them is not evidence that the
   invariant holds.

2. Fix GitHub repository provenance. The precondition tests the value for
   `github.com`, which canonical `raw.githubusercontent.com` SourceLink URLs do
   not contain, so GitHub-hosted assemblies report no repository at all. Match
   the URI host instead of a substring.
3. Extend duplicate-property rejection to the readers that still bypass
   `HardenedJson`: the two `JsonDocument.Parse` call sites in
   `PackageExtractor` registration-page reading, `NuGetFetch.NuGetApi`'s
   source-generated feed contexts, and `runfaster` trace parsing. Add a gate
   asserting no product JSON entry point parses outside the guard, so the set
   cannot silently regrow.
4. Define package, symbol, source-download, and decompressed-archive byte and
   entry-count budgets.
5. Audit every product write against the derived-path rules, including symbol
   server cache path construction.
6. Audit Markdown, plain-text, and stderr rendering for terminal control
   characters and structure injection.
7. Implement the [bounded metadata traversal](bounded-metadata-traversal.md)
   migration and expand malformed PE/PDB product-entry-point coverage around
   graph depth, row count, and allocation limits.
8. Migrate legacy metadata scanners that collapse malformed reads into empty or
   zero-valued results onto explicit failure-bearing outcomes.
9. Revisit filesystem containment if .NET exposes a portable atomic
   no-follow/open-beneath primitive. **Tier 3.**
10. Adopt the reject-over-sanitize strategy where the product currently
    neutralizes. `MetadataTableProjector` repairs control characters in
    artifact text and continues, which is the sanitize strategy this document
    now argues against, and it is applied by calling a helper rather than by
    construction — which is how the metadata version stamp reached
    presentation uncontained. Introduce the trust axis (default abort, survey,
    named skip), move rendering into a type a renderer cannot bypass, and
    re-point `MdiContainmentTests` at the new property. Ship the encoder with
    its decoder and the round-trip/injectivity gate described in
    [metadata-table-projection.md](metadata-table-projection.md#safety); a
    caret-introduced spelling is not invertible and must not be used. Note the
    existing escaper has the same defect from the other direction: `EscapeCore`
    renders controls as `\uXXXX` but only escapes `\` when `escapeStructural`
    is set, and `NeutralizeControls` passes `false`, so a literal `\u001B` in
    artifact text and a real `ESC` produce identical output.

    Adopt the general-category rule at the same time. `IsControl` is
    `c < ' ' || c == '\x7f' || (c >= '\x80' && c <= '\x9f')` — `Cc` only — so
    the metadata path does not encode bidi overrides, `U+2028`/`U+2029`, or
    `U+FEFF`. Other paths in this repository already do: `AppliedTasteSection`
    gates `\u202E`, `\u061C`, and `\u2066`, and the IL string-literal printer
    gates `\u2028`/`\u2029`. One product with two containment sets is the same
    inheritance failure as the version stamp, one layer up, and the narrower
    set is the one a reader of this design would have copied.
11. Audit failure messages for artifact data. `NuGetCache.ValidatePathComponent`
    throws `Invalid {name}: '{value}'`, echoing the value it just rejected.
    Printability here is a function of **provenance, not content**: the same
    helper receives user-typed coordinates and artifact-derived ones, so it
    cannot be decided by inspecting the value. Three graph-resolved paths reach
    it, all verified:

    - `ProjectCommand` → `ProjectAssetsParser` package references →
      `PackageExtractor.ExtractPackageAsync`.
    - `NuspecParser` → `PackageDependency.Id`/`.Version` →
      `DependencyResolutionService.ResolveDependencyTreeAsync` →
      `PackageExtractor.TryGetNuspecXmlAsync` → `NuGetCache.TryGetCachedPackage`.
    - A package-authored `DotnetToolSettings.xml` `Id` becoming the current
      package source, then reaching acquisition and cache validation.

    The second path leaks twice and reaches a package the user never named.
    `DependencyResolutionService` logs `dep.Id`/`dep.Version` before any
    validation, then catches `Exception` and logs `ex.Message`, re-emitting the
    rejected value; that same handler returns an empty result, which is the
    success-shaped failure this document forbids elsewhere.
    `ValidatePathComponent` does not reject control characters other than
    `NUL`, so an `ESC` passes it outright. This is the natural first
    application of the hardened-entrypoint pattern, alongside the nuspec input
    contract behind #3394 and #3418.
12. Establish fuzzing over the PE, metadata, PDB, nuspec, and archive entry
    points. The domain-matched precedent is `binutils`, whose parsers are
    continuously fuzzed and have repeatedly yielded CVEs that way. Most of
    those are memory-safety defects that C# denies us, so the realistic harm
    set here is smaller and enumerable — hang or unbounded allocation,
    plausible-but-wrong output, and output-channel injection — but nothing
    currently searches for any of the three. This is the one open item that
    pays into tiers 1 and 2 at the same time.
