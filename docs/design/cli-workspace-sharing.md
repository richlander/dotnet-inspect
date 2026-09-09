# CLI Workspace sharing

The **CLI Workspace Sharing** design is the normative owner for one public
gesture: serializing the resolved semantic invocation of an inspection into a
portable Workspace scenario and emitting that scenario as a canonical packet
or complete Inspect Web URL.

The end-to-end adoption tracker is
[#6150](https://github.com/richlander/dotnet-inspect/issues/6150). The producing
consumer is the CLI command the user already chose; the receiving production
consumer is Inspect Web. The complementary Browser-to-CLI replay direction is
tracked by
[#4647](https://github.com/richlander/dotnet-inspect/issues/4647).

This is the target contract. `member --share[=url|packet]` is the first
production adoption, but its current semantic restrictions are narrower than
the contract below. Package Dependencies adoption through
`depends --package <id>[@<version>] --tfm <tfm> --share[=url|packet]` is gated
by the focused implementation and Browser-restoration tests named in
[Status and gates](#status-and-gates). Further command adoption remains
unverified until its corresponding gates land.

## Ownership and boundaries

CLI Workspace Sharing owns:

- the common `--share` gesture and its packet-versus-URL selection;
- the requirement to serialize the command's resolved semantic invocation
  rather than reconstructing it through another command or serializing the
  inspected content;
- classification of semantic command inputs, local execution policy, and
  terminal CLI presentation when sharing;
- CLI output, refusal, and exit behavior; and
- the command-by-command adoption rule.

[Workspace Definitions](workspace-definitions.md) remains the sole owner of
portable scenario records, packet versions, canonical encoding, projection
validity, capacity, and restoration. Each inspection command remains the owner
of its source, focus, selector, query, traversal, and execution semantics.
Inspect Web remains the owner of URL activation and presentation. This design
composes those owner-issued contracts without redefining them.

The repository convention is that a caller selects source, focus, operation,
and lens on the command that performs the inspection, then chooses output
through an option on that same command. `--share` follows that convention. It
is a terminal projection, like selecting another output representation, not a
new structural focus or operation arity under the
[Command Transition Model](command-transition-model.md).

Canonical packet and URL output deliberately bypass Markout. They are
byte-sensitive interchange scalars owned by Workspace Definitions, not a
human or tabular rendering of inspection results. The typed portable scenario
remains intact until that canonical encoding boundary.

## Decisions

1. **The inspection command constructs the scenario.** The command's existing
   arguments and options are the only CLI grammar for selecting source,
   context, subject, lens, query, and traversal. Appending `--share` projects
   that selection.
2. **There is no `workspace create` command.** A user never restates a working
   `package`, `library`, `type`, `member`, `depends`, `graph`, or other
   invocation through a second Workspace-construction grammar merely to obtain
   a link.
3. **Sharing serializes the resolved semantic invocation, not argv, target
   content, or results.** Commands hand owner-issued coordinates, identities,
   selectors, facets, and query payloads to Workspace Definitions. A packet
   never persists command names, option spellings, a shell command line,
   acquired artifact bytes, live inspection objects, result rows, or rendered
   text.
4. **A bare `--share` emits the complete URL.** URL sharing is the primary
   agent-to-human handoff. `--share packet` explicitly requests only the
   canonical base64url packet; `--share url` is the explicit spelling of the
   default.
5. **Sharing preserves semantic choices or refuses.** A command must carry
   every projectable source, context, subject, facet, query, and traversal
   choice that affects the receiving inspection. It never drops an unsupported
   choice, substitutes a default view, or produces a less-specific link.
6. **Sharing does not execute the selected inspection merely to encode it.**
   The command performs only the normal resolution needed to establish exact
   portable identity and projectability. Query, analysis, traversal, source,
   or rendering work that the receiving host will perform is not run solely
   because `--share` was selected.
7. **The receiving host replays a recipe, not a result.** A packet contains no
   rendered rows, analysis findings, graph edges, acquired payloads,
   credentials, or frozen result snapshot. Inspect Web applies its own source
   authorization and capabilities when opening the URL.

## CLI contract

### The serialized value is the inspection recipe

The **resolved semantic invocation** is the product-owned inspection recipe
that exists after CLI parsing, normalization, source selection, default
resolution, and any exact identity resolution required by the command, but
before observation and rendering:

```text
CLI argv
   |
   v
parse, normalize, and resolve exact portable identity
   |
   v
resolved semantic invocation
   |-- --share --------------------> scenario packet or URL
   `-- execute target observation -> inspection result
```

Sharing takes the upper branch. It serializes what the caller asked the product
to inspect: acquisition coordinates, Workspace context, structural focus,
exact subject selectors, selected facets or queries, traversal choices, and
other portable semantic options. It does not serialize the target being
inspected or the answer produced by inspecting it.

The distinction is semantic rather than temporal. A command may need bounded
acquisition or metadata resolution to establish an exact portable coordinate
or subject identity, but those acquired bytes and live objects remain evidence
used to resolve the recipe. They do not become packet payload.

### Append sharing to the working invocation

The target gesture is:

```console
dotnet-inspect member JsonSerializer SerializeAsync:1 \
  --package System.Text.Json@10.0.0 \
  --tfm net10.0 \
  --share
```

The same invocation without `--share` performs the ordinary CLI inspection.
Appending `--share` changes only the terminal projection. It must not require a
second command such as:

```console
dotnet-inspect workspace create \
  --package System.Text.Json@10.0.0 \
  --tfm net10.0 \
  --type System.Text.Json.JsonSerializer \
  --member SerializeAsync
```

That duplicate construction path is prohibited even if it could be lowered to
the same records. It would make users and agents translate between two
grammars, allow the grammars to drift, and lose command-specific choices that
the original invocation had already validated.

### Output selection

Commands that adopt sharing expose one common option:

```text
--share[=url|packet]
```

- `--share` and `--share url` write exactly one complete
  `https://dotnet-inspect.net/?w=<packet>` URL plus the platform newline.
- `--share packet` writes exactly the canonical packet plus the platform
  newline.
- Successful sharing writes no ordinary inspection report.
- Diagnostics are written to stderr. Failure writes no packet, partial URL, or
  success-shaped fallback.

`--share` is mutually exclusive with terminal CLI representations and reducers
such as Markdown, JSON, table, TSV, JSONL, plaintext, Mermaid, URL-list, path,
print, count, row-window, field, and column output. Those gestures describe how
the CLI presents an executed result; they do not describe the receiving
Workspace.

Source, focus, section, facet, query, filter, relationship, traversal, and
other semantic options are not rejected merely because sharing was selected.
When their owner provides a portable codec, they are part of the projected
scenario. When the packet or receiving host cannot preserve them, the command
returns a visible non-projectable failure naming the unsupported choice.

Local host policy such as offline mode, source authorization, credentials,
timeouts, cache selection, tracing, verbosity, and tips may govern resolution
in the producing process but never enters the packet. The receiving host
applies its own policy.

### Effective state and exact identity

Sharing consumes the command's effective state after ordinary parsing,
normalization, source selection, and the minimum exact resolution needed for
portable identity. This has four consequences:

1. An input need not repeat information that the command already resolves
   unambiguously. If normal command resolution selects one exact type, member,
   library, context, or query payload, sharing uses that owner-issued identity.
   It does not demand a second selector solely for packet production.
2. A floating package or platform input may use the command's normal resolution
   path. The packet records the resulting exact version and target, never the
   floating spelling. If one exact portable coordinate cannot be established,
   sharing fails.
3. Product defaults that affect the receiving inspection are resolved before
   projection and represented through owner-issued scenario state. A later
   change to CLI or Browser defaults must not reinterpret an existing link.
4. A command that intentionally selects a collection or query may share that
   query when the portable contract represents it. Sharing never chooses the
   first row or another arbitrary result to manufacture a singular subject.

The handoff must use typed command requests, plans, or resolution receipts.
Serializing argv and re-parsing it, invoking another CLI command, embedding
acquired target content, recovering identity from rendered output, or
rebuilding state from display strings are all prohibited.

### Work and acquisition

`--share` authorizes the minimum work required to produce an exact portable
scenario; it does not authorize the observation merely because the ordinary
command would have rendered it.

For example, a package dependency invocation may resolve an unversioned package
to one exact version, but it need not acquire the package or walk its dependency
graph when the packet carries the exact coordinate and Dependencies facet. A
member invocation may need package metadata and compile-surface resolution to
mint an exact member identity, but it does not need documentation, PDB, source,
decompiler, caller, or analysis enrichment solely to emit the link.

If exact identity genuinely depends on work that the command normally performs,
that owner may perform the bounded work and must retain its ordinary failure
semantics. Sharing does not create a cheaper identity guess.

### Projectability and refusal

A valid CLI invocation is not necessarily URL-projectable. Workspace
Definitions may refuse because the current packet version cannot preserve the
scenario, the receiving host lacks the required portable query or restoration
binding, the source cannot be reacquired without excluded authority, or the
canonical packet exceeds its capacity.

The command reports that typed refusal as a nonzero outcome with actionable
stderr. It must identify the first unsupported semantic choice or owner-issued
projection path. It must not:

- omit state and emit a URL anyway;
- fall back to a landing page, Overview, or another facet;
- replace an exact subject with a textual search;
- upload local content or credentials;
- execute the query and serialize its rendered result; or
- instruct the user to recreate the same invocation through `workspace`.

Packet and URL output have identical projectability. URL wrapping cannot make a
non-projectable packet valid.

## The `workspace` and codec command boundaries

The `workspace` command may eventually consume a packet or URL and replay it
under the CLI host contract tracked by #4647. It may also expose inspection of
an already-defined Workspace. Neither role makes it a generic construction
proxy for other commands.

`workspace-state encode` and `workspace-state decode` are low-level conversion
utilities over the canonical packet JSON boundary. They are not the authoring
workflow for a shareable inspection, and this design does not require users to
construct their JSON input. Their eventual naming or retirement is a separate
CLI-surface decision after direct command sharing and required diagnostics are
available.

## Adoption

Adoption is command-by-command because each command owner must issue the typed
state that sharing consumes, and Inspect Web must restore the corresponding
facet or query. The common gesture and output contract do not permit one
command adapter to infer another command's semantics.

For each adopting command, #6150's path has three observable steps:

1. the command projects its effective resolved state to canonical scenario
   records without argv translation;
2. Workspace Definitions either emits one canonical packet or returns its
   typed projection refusal; and
3. Inspect Web restores the packet to the same source coordinates, context,
   subject, facet, query, and portable options.

The exact-member adoption completes these steps for its supported package API
Overview subset. The Package Dependencies adoption in #6394 completes them for
one exact NuGet.org package coordinate, target framework, and package-root
Dependencies facet. An unversioned package or `@latest` is resolved and pinned
before projection; no package body or dependency graph is acquired by the CLI.
Type, package, library, call-graph, and later operation/query adoptions remain
separate slices under #6150; a command exposes `--share` only when its first
useful scenario closes through the receiving host.

## Pathological and neighboring cases

The contract-defining pathological case is a rich invocation whose source,
subject, and selected view are already correct:

```console
dotnet-inspect member JsonSerializer DeserializeAsync:1 \
  --package System.Text.Json \
  --tfm net10.0 \
  -S "Call Graph" \
  --depth 3 \
  --share
```

Once the Call Graph query has a portable packet codec and Browser restoration
binding, this invocation resolves the package version and exact member once,
pins them in the packet, preserves the Call Graph selection and depth, and
emits the URL without running the graph in the CLI. Before that complete path
exists, it fails visibly at the unsupported Call Graph projection. It never
silently emits member Overview and never asks the user to restate the package,
type, member, and graph options under `workspace`.

Neighboring cases prove the boundary:

- the equivalent exact package/member Overview invocation succeeds;
- an ambiguous member selection fails without choosing an overload;
- a local or private artifact that the public Browser cannot reacquire fails
  without leaking a path or credential;
- a floating package that resolves uniquely emits the resolved exact version;
- a command with a competing CLI output format fails rather than producing two
  outputs; and
- packet-capacity overflow emits no URL.

## Non-goals

- A new Workspace construction language or serialized CLI grammar.
- Inspected artifact content, live inspection objects, a snapshot of inspection
  results, or a guarantee that remote content remains available.
- Uploading artifacts, opening a browser, hosting shortened URLs, or storing
  scenarios server-side.
- Treating a packet as source authorization or embedding credentials.
- Making every command projectable before its semantic state and Browser
  consumer have a faithful portable contract.
- Expanding packet schemas merely because a CLI option exists; unsupported
  state remains a visible refusal until its owner designs a portable form.

## Status and gates

The common target contract is **partially verified**. Current
`member --share[=url|packet]` proves canonical packet/URL production for one
explicitly selected public NuGet member Overview, but it still requires an
exact overload gesture and rejects all section selection. Package Dependencies
adoption proves that an omitted or `latest` NuGet.org version can be resolved
to an exact coordinate without acquiring the package or traversing its graph,
then restored by Inspect Web as the Dependencies facet. Those restrictions and
completed slices are implementation status, not the general sharing contract.

Each adoption must add focused Release gates proving:

- appending `--share` consumes the same normalized source, context, subject,
  facet, query, and traversal state as the ordinary command plan;
- no argv serialization, synthetic command invocation, acquired target
  content, result payload, rendered-text identity, or duplicate source/focus
  grammar participates in the projection;
- bare `--share`, explicit `url`, and `packet` produce the required scalar and
  no ordinary report;
- a uniquely resolved floating input is pinned to its exact portable
  coordinate;
- semantic selections are preserved when projectable and produce a typed
  refusal when not;
- work unnecessary for exact identity or projectability does not execute;
- competing terminal output, ambiguous identity, unsupported source,
  over-capacity state, and Browser-restoration gaps fail nonzero with empty
  stdout; and
- the real Inspect Web URL restores the same normalized coordinates, context,
  subject, facet, query, and portable options.

The existing `MemberShare_*` tests gate the narrower member subset.
`DependsShare_PacketProjectsExactPackageDependencyView`,
`DependsShare_UrlWrapsCanonicalPacket`,
`DependsShare_FloatingVersionResolvesWithoutPackageAcquisition`,
`DependsShare_RejectsNonProjectableCoordinate`,
`DependsShare_RejectsLocalPackage`,
`DependsShare_RejectsConfiguredSource`,
`DependsShare_RejectsBrowserPlatformPackageId`, and
`DependsShare_RejectsConflictingOutput` gate the CLI package Dependencies
projection. `canonical package dependency views restore the package root
lens`, `canonical package views reject contradictory structural selection`,
`capture projects package Dependencies through the packet lens`, `capture
refuses a non-active package dependency group`, `Share copies canonical
package Dependencies and refuses a non-active group`, and `canonical package
Dependencies restoration clears a resident group override` gate the Browser
adapter.
`WorkspaceSharePacketTransposerTests` and codec vectors remain authoritative
for packet validity. An adoption is not complete with a decode-only test; its
gate must exercise the command-to-Browser handoff described by #6150.
