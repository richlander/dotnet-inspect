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

The intended consumer raises it further. This tool is built to be handed to
**autonomous agents**, so its output is frequently acted on without a human
reading it. Two things follow. A rendering hazard is not bounded by whether
someone is watching a terminal, and output that misstates identity is not
caught by a reader who would have noticed. Trust in the *input* is also
misplaced in a specific way worth naming: a caller who pre-vetted their
dependencies concludes that reading them is safe, but a vetted package can be
hijacked after the fact, and the names entering a build are not spelled
uniformly across projects, transitive edges, and floating versions.

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

### Prefer an allow list wherever the grammar is known

A rejecter still has to decide what to reject, and there are two ways to write
that down. A **deny list** enumerates the bad forms; an **allow list**
enumerates the permitted ones and refuses everything else. Where a field's
grammar is externally defined and small — a package id, a version — use the
allow list.

The difference is not stylistic. A deny list is only ever as current as the
last hazard someone thought of, and it cannot express the attacks that use
*ordinary* characters. Cyrillic `а` (`U+0430`) and Latin `a` (`U+0061`) are the
same glyph, different code points, and both are general category `Ll` — no
hazard classification will ever separate them, because neither is a hazard.
Homoglyph typosquatting is defeated by constraining the grammar and by nothing
else.

An allow list is also the cheapest thing here to audit — one small set checked
against one field — and the fastest to run, needing no Unicode tables. It is
the same reject-over-sanitize rule applied one step earlier: at the point the
value is admitted rather than the point it is printed.

Free-form fields cannot be treated this way. Assembly-derived type and member
names are legitimately non-ASCII, and prose is legitimately international, so
those fall back to visual encoding. See
[metadata-table-projection.md](metadata-table-projection.md#constrain-the-grammar-first-encode-only-what-cannot-be)
for the sink classes and the encoding rules that follow from them.

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

The stronger shape now exists. `InertText.InertString` (#3636) is a type whose
construction *is* the encoding, so treated text has a different type from
untreated text and survives composition — a site that merely passes a value
along cannot drop the property, and forgetting becomes a compile error rather
than a missing line in a hand-maintained list. It is the primitive this section
argues for; prefer it over a new `HardenedJson`-shaped static entry point when
the thing being contained is text bound for a sink.

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

**Code running in this process is not the boundary these controls defend.**
The untrusted input in every row above is *data* — an artifact, a feed
response, a file. It is not a caller. So a control that a `BindingFlags.NonPublic`
call can undo is not thereby broken, because a party who can execute arbitrary
code in this process does not need to smuggle text through a type to reach a
sink; it can write to the sink. The claim is that narrow, and deliberately so:
not that reflection is harmless in general, but that it is not the entry point
any control here stands in front of. This is also not a self-serving line — no
.NET type meets the other standard. Measured, the same technique that rewrites
a private backing field on `SourceLinkOrigin` rewrites one on `System.Uri` —

```text
Uri backing field: _string
Uri.OriginalString => https://example.com/<LRI>hostile
```

— making `OriginalString` return a live `U+2066` from a `Uri` constructed over
inert text. `Uri` is the type the SourceLink origin readers rely on for
canonicalization, so a rule that treats reflection as in scope would condemn the
control and its substrate together and leave nothing constructible in its place.

What *is* in scope is the ordinary language surface: a public constructor, a
`with` expression, a settable property. Those are reachable by a future
contributor writing normal code, which is how an invariant actually decays, and
`SourceLinkProvenanceTests.ASourceLinkOrigin_CannotBeConstructedOrRewrittenOutsideItsOwnAssembly`
is the gate for them. It deliberately does not claim more.

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
disagree. `DotnetInspector.Core.HardenedJson` and SourceLinkFetch's map parser reject duplicate
properties, while `ILInspector.SourceLink.SourceLinkJsonContext` applies the same rule to its
persistent type-index cache. Such payloads fail visibly instead of binding one of
several possible readings.

This is generic hardening, not a fix for a known divergence. The SourceLink
provenance divergence it does **not** address is closed separately, by the
control below.

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

### SourceLink provenance is read off the URL source is fetched from

Reported provenance must describe the origin that source content is actually
fetched from, for every document the assembly resolves. When that cannot be
established for all of them, report no repository.

`SourceLinkFetch.SourceLinkProvenance` is the single owner of this rule. It
resolves every document the assembly declares through
`SourceLinkFetch.SourceLinkResolver` — the single owner of the mapping rule —
and reads the origin off each **final resolved URL, after wildcard substitution,
percent-encoding, and `System.Uri` canonicalization**. Never off the mapping
text, and never off the mapping prefix alone. Agreement is required on the whole
`(host, organization, repository, revision)` tuple, because
`raw.githubusercontent.com` serves any revision reachable in a repository,
including the head of an unmerged pull request.

Every way a weaker formulation has been found to fail, all reproduced. They are
a regression floor, not a specification of what to block: each was found only by
attacking a previous formulation, so passing them is not evidence that the
invariant holds. The list deliberately carries no count — nothing enforced the
one that was here, and it had gone stale by two.

- Agreement on `owner/repo` ignores the revision, so two entries on one
  repository at different commits "agree" while serving different code.
- `System.Uri` applies RFC 3986 dot-segment removal, so a mapping value
  containing `../` is fetched from the traversed-to path while a regex over the
  raw string reports the literal one.
- Even a clean mapping is not enough. The wildcard suffix comes from the PDB
  document path, which is equally attacker-controlled, so a benign
  `.../dotnet/runtime/<commit>/*` resolves
  `/_/../../../attacker/evil/main/Program.cs` into `attacker/evil`.
- `System.Uri` preserves percent-encoded separators verbatim: `..%2f` and
  `..%5c` survive canonicalization, so a "canonicalize, then prefix-check" step
  passes while a server that percent-decodes before resolving dot segments still
  traverses out. Encoded separators and encoded dot segments are rejected rather
  than assumed resolved.
- `https://raw.githubusercontent.com@evil.example/...` parses with host
  `evil.example` and user info `raw.githubusercontent.com`. The host allow list
  rejects it, since `Uri` takes the authority after the last `@`; user info is
  additionally rejected on its own account, because a credential presented to an
  allowed host makes the response depend on the identity presented rather than
  on the public path the URL names.
- `raw.githubusercontent.com` serves branch names, and a branch may contain `/`,
  so `.../owner/repo/feature/auth/File.cs` reads equally well as revision
  `feature` with path `auth/File.cs` or as revision `feature/auth` with path
  `File.cs`. Nothing in the URL says which. Taking the third path segment made
  `feature/auth` and `feature/login` report one revision and one cache identity.
  The revision must therefore be a full commit hash, which cannot contain `/`,
  or the URL is not attributable.
- Whether a host matches query parameter names case-insensitively is not stated
  by the URL, so `?VERSION=evil&version=legit` has two readings. A case-sensitive
  match reports `legit` while a case-insensitive host may serve `evil`. A
  parameter differing from the expected spelling only by case is not
  attributable.
- Azure's Items API accepts the revision as the flat `version` parameter and as
  `versionDescriptor.version`, and the **descriptor takes precedence**. Reading
  only `version` reported the losing selector, so a URL carrying both named one
  revision while fetching the other. Confirmed against the live API. Both
  spellings are read; disagreeing selectors are not attributable.
- The cache identity must be an unambiguous serialization of the origin tuple.
  Azure DevOps repository names and Git ref names may both contain `/` and `@`
  (`git check-ref-format` accepts `branch@tip`), so a delimiter-joined key let
  repository `repo@branch` at revision `tip` and repository `repo` at revision
  `branch@tip` collide. The identity is length-prefixed. This key selects a
  persistent source index, so a collision serves one repository's source for
  another's assembly.
- A query parameter repeated with *equal* values still has two readings, and the
  host takes neither: measured against the live API,
  `?version=aaaa&version=aaaa` returns 400 "Ambiguous values for version". An
  earlier note here reasoned from `HttpUtility.ParseQueryString`, which joins
  repeats with a comma, and concluded Azure would select the ref `aaaa,aaaa`.
  That is a client decoder's behaviour, not the host's; the refusal was right
  and the stated mechanism was wrong. A repeat is refused however its values
  compare.
- The repeat rule stopped at the revision selectors, and the **content**
  selectors are where it mattered. Azure serves the *first* occurrence of
  `path`, so `path=/fixed.cs&path=/*` substitutes every document into an
  occurrence the host ignores: each document produces a distinct URL — enough
  for the resolver's two-probe check, which sees only text — while every one of
  them fetches `fixed.cs`. Measured: `path=/README.md&path=/nope.txt` returns
  README, and `path=/.gitignore&path=/README.md` returns 404 for the *first*
  path. Names are compared case-insensitively because the host binds them that
  way, also measured. No parameter may be given twice.
- The segments before `_apis` are the host's route, and joining however many
  there were reported an organization that was assembled rather than read. A
  project-less `dev.azure.com/{org}/_apis/...` was attributed to `{org}` at a
  commit, and `dev.azure.com/a/b/c/_apis/...` to the organization `a/b/c` with
  the repository page `https://dev.azure.com/a/b/c/_git/{repo}`, which is not a
  page. Measured, the route is keyed on exactly organization and project: the
  two-segment shape returns 200, while project-less, wrong-project and
  wrong-organization shapes each redirect to a sign-in page on another host and
  an extra segment returns 404. The count is now fixed per host — two on
  `dev.azure.com`, one on `*.visualstudio.com`, where the account is the host
  label — which is exactly what `AzureDevOpsUrlParser` builds. `DefaultCollection`
  is dropped rather than made part of the identity, because the host serves
  byte-identical content with and without it.
- A wildcard confined to the **query** changes the request text without changing
  what the host serves, on a host that ignores the query. `{"*":
  ".../{sha}/fixed.cs?ignored=*"}` gives every document its own URL — so the
  two-probe check, which compares request text, is satisfied — while every one of
  them fetches `fixed.cs`, and the reported origin is genuinely where `fixed.cs`
  is served from, so the agreement check is satisfied too. One file is then shown
  as the source of every document under a clean attribution. Measured against
  `raw.githubusercontent.com`: no query, `?ignored=A.cs`, `?ignored=B.cs` and
  `?path=/other.cs` all return the same 33400 bytes with the same SHA-256. This
  cannot be refused by the host-agnostic matcher, because the identical shape is
  the *generated* Azure Repos form where `path=` does select the file. It is
  refused by the host grammar instead: `raw.githubusercontent.com` URLs may not
  carry a query, which loses nothing, since that generator builds its URL by pure
  path concatenation and never appends one.
- A substitution can land in a component the host does not select on, which
  varies the request text while leaving the served file fixed. The Azure
  spelling is
  `{"*": ".../items?api-version=*&versionType=commit&version={sha}&path=/README.md"}`:
  every document gets its own URL, so the two-probe check passes; `path=` never
  moves, so every one fetches `README.md`; and the origin reported is genuinely
  where `README.md` is served from, so agreement passes too. Measured against
  `dev.azure.com/dnceng-public/public`, repository `dotnet-public-wiki`:
  `api-version` of `1.0`, `7.1`, `1.0-preview` and `5.0` all return the same
  content, SHA-256 `0129277c5fd5e35a…`. The allow list said each parameter was
  *understood*, never that each one *selects*. Provenance now requires the
  substituted text to land in the content-selecting component — the path for
  `raw.githubusercontent.com`, the `path` or `scopePath` value for Azure DevOps
  — which also refuses a substitution in the route, in the repository segment,
  and in `version`. One reviewer cleared `api-version` on the grounds that Azure
  answers a file-like value with 400; another defeated that by naming the PDB's
  documents `1.0` and `7.1`, which the threat model treats as attacker-chosen.
  `scopePath` is on the accept side because it was measured to select, not
  because the allow list already named it: `scopePath=/README.md` returns the
  same 985 bytes and SHA-256 as `path=/README.md`, while `scopePath=/` returns a
  different 425-byte response. Gated by
  `SourceLinkProvenanceTests.ASubstitutionThatSelectsNoContent_IsNotAttributable`.
- The two content selectors are each allow-listed, and their *combination* was
  never considered. `path` names an item and `scopePath` a collection, and the
  host refuses to be asked for both rather than preferring one: measured,
  `scopePath=/&path=/*` and `path=/*&scopePath=/` both return 400, `Cannot
  specify an item "path" as well as "scopePath"`. The pair passes every other
  rule — both names are known, neither is repeated, and each carries its own
  wildcard, so the two-probe check sees two distinct request texts — while
  nothing states which selector governs. This is the repeated-parameter rule
  applied to two spellings of one role: were the host to start preferring one,
  every document would resolve through the selector that does *not* carry the
  wildcard and they would all fetch the same content while attributing cleanly.
  An allow list states that each entry is understood, not that any two of them
  compose. The rule is **ambiguity, not fetchability** — `api-version` is
  allow-listed and unvalidated, and `api-version=bogus` returns 400, which is
  fine: a request that fails serves no content, so nothing is misattributed and
  the failure stays visible.
- Reading the route positionally is not enough on `dev.azure.com`, where a
  leading `e` is the enterprise discovery prefix rather than an account.
  `/e/{org}/_apis/git/repositories/{repo}/items` satisfies the segment count
  exactly and reports the organization `e`. Measured: it returns 404 where the
  same request without the prefix returns 200, so the shape serves nothing for
  the reported origin to describe. `AzureDevOpsUrlParser` refuses it for the
  same reason, so no generated shape is lost by refusing it here.
- A literal `+` in a value decodes to a space under a form decoder and to a plus
  under a percent decoder, so `version=a%2Bb&versionDescriptor.version=a+b`
  presents two agreeing selectors to one reader and two disagreeing ones to
  another. The descriptor wins at the host, so we reported `a+b` while Azure
  served `a b`. A literal `+` is refused; `%2B` is unambiguous and stays accepted.
- Azure reads `version` against `versionType`, which defaults to `branch`, so a
  branch and a tag of one name are two different contents behind one spelling
  and one cache identity. Measured against a live repository: `main` as a branch
  returned 200 and as a tag 404. Only `versionType=commit` with a commit hash is
  attributable, which is exactly what `Microsoft.SourceLink.AzureRepos.Git` and
  `Microsoft.SourceLink.AzureDevOpsServer.Git` generate.
- `versionOptions=previousChange` and `firstParent` serve a different commit's
  content under an unchanged `version`, so the reported revision would not be the
  one fetched. Both are refused.
- The Azure path was matched at `/_apis/git/repositories/{repo}` without
  requiring the `items` endpoint, so endpoints that ignore `version` entirely
  were attributed to an attacker-chosen revision. The repository-metadata
  endpoint returned byte-identical content for every revision supplied. The path
  must now end at `items`.
- Query parameters are allow-listed rather than deny-listed. Azure's Items API
  takes several parameters that change which content is returned, and it grows
  while this reader does not, so an unrecognized name may select content the
  reported origin does not describe.
- Absence and emptiness are different readings. A parameter present with an
  empty value (`versionDescriptor.version=`) or present with no `=` at all was
  treated as absent, so the flat `version` was read as unopposed and its value
  reported — while a host that treats the descriptor as present-and-empty
  selects the default ref instead. Only a genuinely absent parameter counts as
  absent; a present one with nothing to say is refused on its own account.
- Whether a hex string is an object name is a property of the host's object
  format, not of the string. Accepting the 64-character SHA-256 length let
  `raw.githubusercontent.com/owner/repo/<64 hex>/*` report a commit, but GitHub
  stores SHA-1 repositories only and Git will create a branch of that name
  (`git branch` accepts one), so the value could only be a moving ref whose head
  moves under a fixed reported revision and a fixed cache identity. Both hosts
  this reader knows are SHA-1-only; the SHA-256 length needs the same evidence
  as a new host before it is admitted.
- An origin is `(scheme, host, port)`, but the reader identifies a host by name.
  `https://raw.githubusercontent.com:444/owner/repo/<sha>/*` was attributed to
  GitHub and given the same persistent cache identity as port 443, so a
  different service on that machine served content under GitHub's name and into
  GitHub's index. A port other than the scheme's default is refused; an explicit
  `:443` is the same origin and stays accepted. Neither generator emits a port
  for these hosts, so nothing generated is refused.
- The reported origin is itself artifact text, and one component of it is not
  escaped by the parser. `Uri.AbsolutePath` neutralizes a hostile path segment
  by leaving its percent-escape escaped, but `Uri.Host` does not: a raw `U+2066`
  in `a<U+2066>ccount.visualstudio.com` survives into `Uri.Host`, passes the
  `.visualstudio.com` suffix rule, and reached the rendered `RepositoryUrl` as a
  live bidi control — a Trojan Source code point aimed at the reader's terminal
  rather than at the fetch. `TryCheckOriginTextIsInert` now refuses any origin
  component carrying `Cc`, `Cf`, `Cs`, `Zl` or `Zp`. It runs from
  `TryEmitOrigin`, the single point at which an origin becomes visible to a
  caller, so the rule is a property of the value rather than of one code path:
  the first fix placed it in `Determine`, and the re-review found that
  `BrowseUrl` — a rendered product path, reached from
  `SourceLinkResolver.ConvertToGitHubBrowseUrl` and emitted as
  `GitHubBrowseUrl` — reads an origin without going through `Determine`. Gated
  by `SourceLinkProvenanceTests.ALiveFormatCharacterInAHostLabel_IsNotAttributable`
  and `…NoOriginIsEverProducedCarryingAScalarThatCanActOnASink`. The second
  asserts at the construction seam rather than over rendered text on purpose:
  `BrowseUrl`'s own output is inert for an unrelated reason — every hostile
  scalar in a path is percent-escaped by `Uri.AbsolutePath`, and its host must
  equal `raw.githubusercontent.com` exactly — so a test over what it prints
  would pass whether or not the check exists.

Two consequences are deliberate scope, not gaps, and are gated as decisions so
that changing them is visible:

- The host allow list is the set of hosts whose URL grammar this reader knows,
  not a trust boundary. SourceLink's generators also emit `*.vsts.me` and Azure
  DevOps Server URLs on arbitrary hosts and ports; both report no repository.
  Admitting such a host needs its own evidence — who operates the domain, where
  the virtual directory ends, and which port it answers on, none of which the
  URL states.
  Gated by
  `SourceLinkProvenanceTests.AHostWhoseUrlGrammarIsNotKnown_ReportsNoRepositoryRatherThanAGuess`.
- The encoded-separator refusal applies inside Azure's repository segment too.
  Two reviewers read this as over-refusal of a "repository folder", but Azure
  DevOps has no repository folders and forbids `/` in a repository name, so no
  such repository exists. The generator does pass the sequence through when the
  git remote contains it, so the map shape is real even though the repository it
  names cannot be. Accepting it would also undercut the rule that the path must
  end at `items`: that rule is decided by splitting the path, and `%2F` survives
  canonicalization, so our split and the server's need not agree on where the
  repository segment ends. Gated by
  `SourceLinkProvenanceTests.AnEncodedSeparatorInTheAzureRepositorySegment_IsNotAttributable`.

One consequence is a real gap, tracked rather than closed here. Attribution is
decided from the URL's text, offline; the fetch that follows is a separate step
and does not compare where it *landed* with what was attributed.
`CreateUntrustedFetchClient` follows redirects (five hops, SSRF-guarded per hop)
and any 2xx is accepted, so a syntactically valid but nonexistent, private, or
unauthenticated Azure route redirects to a sign-in page on another host and
answers 203:

```text
final=https://spsprodeus27.vssps.visualstudio.com/_signin?realm=dev.azure.com&...
code=203
type=text/html; charset=utf-8
```

The reported repository URL is still `https://dev.azure.com/contoso/widgets/_git/core`,
so "read off the URL source is actually fetched from" is not true after a
cross-host redirect.

Content is **not** protected across the board. The authored-source path verifies:
`AuthoredSourceAcquisition` re-checks the PDB checksum in `FromContent` and
returns `Failed` on a mismatch, so redirected HTML cannot be shown as authored
source there. But five CLI call sites fetch with `SourceFetcher.FetchSourceAsync`
and render the result without any checksum check —
`ApiCommand.cs:648` (the rendered **Original Source** section, whose text goes
straight into `BodySlicer.ExtractMethodBody`), `ApiCommand.cs:1239`,
`LibraryCommand.cs:1243`, and `SourceEnricher.cs:210` and `:333`. In
`ApiCommand`, the *local repository* branch immediately above verifies a
checksum and the network branch does not, so the asymmetry is visible in one
screen. The slicer is a heuristic over line spans the PDB supplies, and the PDB
is attacker-controlled, so it is not an authenticity boundary.

Fixing this means comparing the post-redirect `RequestMessage.RequestUri`
against the attributed origin, and requiring verification — or an explicit
"unverified" label — at every consumer that renders fetched source. Both belong
to the fetch and CLI layers: `Determine` is deliberately offline, and making a
static metadata read depend on a network round trip is a design change. Tracked
by **#3618**.

This entry previously claimed `AuthoredSourceAcquisition` was the only consumer
of fetched source and concluded that redirected HTML could never be rendered.
That was false, and it was written while fixing a finding about ungated claims;
round 18 caught it by enumerating `FetchSourceAsync` rather than the
`…SourceBytesAsync` overloads the original search covered.

Gates. `SourceLinkProvenanceTests` covers all twenty-one as named tests, plus the
cache-identity distinction between forks and the requirement that every
unestablished result carry a reason. Where a refusal has more than one possible
cause, the test asserts the *reason* and not merely that the URL was refused: an
empty selector, for instance, is refused by every downstream rule as well, so a
test asserting only "not established" would pass with the rule it names deleted.
`SourceLinkProvenance.BrowseUrl` makes the same claim as the origin reader, in
the form a user is most likely to click, so it is held to the same rule and
gated by
`SourceLinkProvenanceTests.ABrowseLink_IsOnlyOfferedForAnAttributableGitHubOrigin`.
`SourceLinkProvenanceTests.OnlyTheProvenanceOwner_AndTwoNonAttributingReaders_NameTheGitHubRawHost`
and `SourceLinkMapConformanceTests.OnlyTheSourceLinkOwner_ReadsTheDocumentsMap`
pin the reader sets by set equality, so a second implementation of either rule
fails rather than quietly diverging.

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

> **Status.** Both axes are built in `mdi`, which is the reference consumer:
> the default refuses, `--show-untrusted-text` opts out of the trust axis, and
> `--dangerously-print-raw` additionally opts out of the rendering axis. The
> rendering axis rests on `InertText.InertString` (#3636), extended to Unicode
> general categories in #3628.
>
> Two things remain unbuilt. **Survey mode** is not implemented: refusal stops
> at the first violation rather than reporting every one, though
> `InertString.IsPermitted` already returns a `ScalarViolation` shaped for it.
> And **`dotnet-inspect` has neither flag**; the library default is to encode
> and continue, so its behavior is the middle row of both tables. That default
> is deliberate — containment is a safety property the library owes every
> caller, while refusing is a policy only a caller can choose — but it means the
> trust axis currently exists only where a command line can express it.

Presentation is **two orthogonal decisions**, and collapsing them into one flag
is a design error.

**Trust** — what happens when a concerning pattern is found:

| Flag | Behavior |
| --- | --- |
| *(default)* | abort at the first one |
| survey mode | keep going; report location and pattern kind, never content, bounded by the traversal budget |
| `--show-untrusted-text` | keep going and render the values anyway |

The trust skip is **not** `dangerously`-named, which is a correction to an
earlier draft of this section rather than a departure from it. The argument
below is that the skip is defensible precisely because visual encoding still
applies underneath it — it means "do not refuse," not "it is fine to put my
terminal at risk." A name that called it dangerous would contradict that, and
would spend the word on the safe path, leaving nothing louder for the flag that
genuinely hands over live control characters.

**Rendering** — how artifact text is spelled once something is printed:

| Flag | Behavior |
| --- | --- |
| *(default)* | visually encoded into an inert form |
| `--dangerously-print-raw` | no visual encoding; the output format's own structural escaping still applies |

That last clause is load-bearing and measured: the format keeps itself well
formed regardless of the flag. JSONL escapes scalars below U+0020 per RFC 8259,
which is not containment — those escapes decode back to the original scalar —
and TSV replaces the line and paragraph separators, which it cannot carry in a
record. Markdown carries everything. So the raw mode promises that `mdi` adds
no encoding of its own, not that every scalar reaches the stream.

**Raw output is produced by the decoder, at the sink.** Since #3687 the
projection cannot hold untreated text at all: its text-bearing fields are
`InertString`, and no conversion admits a `string` into one. That closes off the
obvious implementation — keeping a second, raw copy of every value beside the
contained one — and forces the honest one, which is to run the encoding
backwards at the moment of printing. This is the `vis`/`unvis` pairing named
below rather than a workaround for it: the encoding is lossless and invertible
precisely so that a decoder can exist, and having the decoder is what makes raw
output a *rendering* choice instead of a property of the model. A literal
backslash is always rewritten on the way in, which is what keeps the inverse
unique. Refusal is unaffected and still happens upstream, against the raw text,
because the question it asks — does this artifact carry something concerning —
is about the artifact rather than about the spelling.

Placing the decode after the character budget also buys a property the earlier
implementation lacked: **both modes cut the same value at the same point.**
Bounding raw text separately, in its own units, made the rendering axis quietly
a *content* axis too, so that asking for a different spelling changed how much
of the value you saw — a subtler form of exactly the collapse this section
warns about. `MdiUntrustedTextModeTests.RawAndEncodedRenderingsShowTheSamePrefix`
gates it by re-encoding the raw cell and comparing it to the encoded one,
running the encoder forward so the assertion is not a restatement of the
decode. On an artifact carrying nothing that needs containment the three
tiers are byte-identical; measured over a 2,031,325-byte assembly, all three
agree exactly.

The axes are independent, and that is the design. Visual encoding is the
default on **every** artifact-text path, including underneath the trust-axis
skip — which is precisely what makes that skip defensible: it means "do not
refuse," not "it is fine to put my terminal at risk." Reaching a live control
character therefore requires opting out of both axes, two separately named
mistakes. `mdi` enforces exactly that: `--dangerously-print-raw` on its own is
rejected rather than silently ignored, because refusal comes first and the flag
would otherwise change nothing while appearing to.

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

One artifact-derived string on the SourceLink path is rendered today: the
reported `RepositoryUrl`, which `AssemblyInspector` writes to
`audit.RepositoryUrl`. It is built from segments of a URL that came out of a
downloaded package's PDB, so a hostile map can aim `ESC`, `CR`/`LF`, or a bidi
override at it.

The **path** components are inert incidentally. `Uri.AbsolutePath` leaves a
percent-escape escaped, so `%1b` stays the three characters `%`, `1`, `b`, and a
raw `U+202E` comes back as `%E2%80%AE`; measured,
`https://github.com/ow%1bner/repo` and `https://github.com/ow%E2%80%AEner/repo`
are what get reported.

The **host** is not, and assuming otherwise was a real bypass (round 17,
twenty-first entry). `Uri.Host` does not escape the way `Uri.AbsolutePath` does:
a raw `U+2066` in an `account.visualstudio.com` label survives into `Uri.Host`
unchanged, passes the `.visualstudio.com` suffix rule, and reached the reported
URL as a live bidi control — one of the code points `rustc` made a hard error
after Trojan Source. The first gate written for this missed it because every row
it had was a *percent-encoded* escape, and those really are neutralized by the
path reader; nobody had written down the raw form.

So the rule is no longer "the readers happen to escape". `TryCheckOriginTextIsInert`
refuses an origin any of whose components carries a scalar in `Cc`, `Cf`, `Cs`,
`Zl` or `Zp` — by category, not by a list, because `Cf` is what a list misses.
It runs from `TryEmitOrigin`, the one place an origin becomes visible to a
caller, and not from `Determine`: `Determine` is where the round-17 fix put it,
and the re-review pointed out that `BrowseUrl` reads an origin without going
through `Determine` and is rendered as `GitHubBrowseUrl`. A rule enforced by one
consumer is a rule the next consumer does not inherit — the same shape of defect
as the one it was written to close. Refusal rather than encoding
follows the strategy above: no legitimate repository needs a bidi control in its
name. The rejection names the component and the code point and never the value,
so the diagnostic channel does not carry the hazard it is reporting.

Refusal by category was checked against legitimate input rather than assumed
safe: repository names in Japanese, Chinese, Korean, Cyrillic, Greek, Arabic,
Hebrew, Devanagari, Thai and Vietnamese, plus an emoji, a combining sequence and
its precomposed form, all still attribute.

The gates: `ALiveFormatCharacterInAHostLabel_IsNotAttributable` pins the refusal,
`NoOriginIsEverProducedCarryingAScalarThatCanActOnASink` pins the invariant at the
construction seam so it covers `BrowseUrl` and the cache identity as well as the
reported URL, `AnEstablishedRepositoryUrl_CarriesNoScalarThatCanActOnASink` pins
that anything still reported is inert, and
`TheHostileOriginRows_MostlyEstablish_SoTheScalarGateIsNotVacuous` pins that its
rows still establish, because a gate whose every row is refused asserts nothing.
Disabling the check fails nine of them.

`SourceLinkProvenanceResult.Reason` is the *latent* half of the same exposure.
Its messages quote artifact text throughout — the query, the path, the host, a
revision, a rejected map key — and today no caller renders it: all six read
`Origin?.RepositoryUrl` and drop the reason. Issue #3590 exists to report it,
which is exactly the change that turns these into a live path, so #3590 must
adopt visual encoding rather than merely surfacing the strings.

A test framework is a sink too. xUnit builds its row labels from the theory
arguments, so the runner prints a raw `U+202E` from a hostile fixture to the
same terminal — assertion *messages* under our control name the code point
instead (`U+202E (Format) at 2`), and fixtures should assume the label is not
under our control.

## Verification obligations

Security-sensitive parsers and writers require close negative fixtures, not
only ordinary compiler output.

| Surface | Required evidence |
| --- | --- |
| Resource extraction | Traversal and rooted names rejected before writes; valid nested and empty resources retained; malformed ranges rejected; separator/case aliases collide; existing file preserved; device/control names rejected |
| Archive extraction | Zip-slip fixture; expanded-size and entry-count policy tests once budgets exist |
| Metadata and signatures | Malformed table/blob fixtures, depth/size limits, no process crash |
| SourceLink | Private/loopback/redirect targets rejected; allowed public target and checksum path retained; a duplicate `documents` key fails the parse rather than binding one of its values; the mapping rule is pinned against the specification's worked example, and the set of product files reading the map is pinned by set equality |
| Untrusted JSON | Duplicate properties rejected at top level, nested, and from UTF-8 bytes; case-distinct and sibling-repeated names still parse |
| Cache paths | Traversal/separator components rejected; content-addressed keys deterministic |
| Structured output | Untrusted non-graphic scalars cannot escape the selected format. `MdiContainmentTests` splices a payload reaching past any single predicate's notion of "control" (a live `ESC [ 3 1 m` sequence, `BEL`, `DEL`, a C1 control, the bidi override `U+202E`, the line separator `U+2028`, the zero-width space `U+200B`, and the supplementary tag character `U+E0074`) into both a real `#Strings` entry and the metadata version stamp, then renders that assembly in every format through the three views that carry artifact text — table, heap, and overview — asserting no raw non-graphic scalar survives and every contained form is present. The `--references` view carries no artifact text, so it is asserted only against raw scalars, as a regression net. Mutation-checked by restoring the pre-#3628 range predicate (dies naming `U+202E`) and by a category-correct but `char`-based predicate (dies naming `U+E0074`). Until #3628 this row named a payload that was `Cc` only, so a bidi override would not have been noticed; the payload and the assertion helper had both been scoped to the projector's own predicate, which is why the gate stayed green while `U+202E` reached the terminal. Both now classify by Unicode general category over scalars. Two limits remain: the assertion deliberately permits raw `CR`/`LF`/`TAB`, and format *delimiters* are not covered by this gate at all |

## Open work

1. Extend duplicate-property rejection to the readers that still bypass
   `HardenedJson`: the two `JsonDocument.Parse` call sites in
   `PackageExtractor` registration-page reading, `NuGetFetch.NuGetApi`'s
   source-generated feed contexts, and `runfaster` trace parsing. Add a gate
   asserting no product JSON entry point parses outside the guard, so the set
   cannot silently regrow.
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
   no-follow/open-beneath primitive. **Tier 3.**
8. Adopt the reject-over-sanitize strategy where the product currently
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
9. Audit failure messages for artifact data. `NuGetCache.ValidatePathComponent`
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
10. Establish fuzzing over the PE, metadata, PDB, nuspec, and archive entry
    points. The domain-matched precedent is `binutils`, whose parsers are
    continuously fuzzed and have repeatedly yielded CVEs that way. Most of
    those are memory-safety defects that C# denies us, so the realistic harm
    set here is smaller and enumerable — hang or unbounded allocation,
    plausible-but-wrong output, and output-channel injection — but nothing
    currently searches for any of the three. This is the one open item that
    pays into tiers 1 and 2 at the same time.
