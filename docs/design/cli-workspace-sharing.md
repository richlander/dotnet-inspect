# CLI Workspace sharing

The **CLI Workspace Sharing** design is the normative owner for one public
gesture: serializing the resolved semantic invocation of an inspection into a
portable Workspace scenario and emitting that scenario on stderr alongside the
command's ordinary content.

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
- classification of semantic command inputs, local execution policy, content
  presentation, and Share side output;
- CLI output, refusal, and exit behavior; and
- the command-by-command adoption rule.

[Workspace Definitions](workspace-definitions.md) remains the sole owner of
portable scenario records, packet versions, canonical encoding, projection
validity, capacity, and restoration. Each inspection command remains the owner
of its source, focus, selector, query, traversal, and execution semantics.
Inspect Web remains the owner of URL activation and presentation. This design
composes those owner-issued contracts without redefining them.
[Inspection Plan Projections](inspection-plan-projections.md) owns the shared
resolved basis, closed content purpose, and required portable-share projection.

The repository convention is that a caller selects source, focus, operation,
and lens on the command that performs the inspection, then chooses output
through an option on that same command. `--share` follows that convention. It
adds the portable URL or packet to stderr while leaving ordinary content on
stdout. It is an additive presentation gesture, not a new structural focus or
operation arity under the
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
4. **A bare `--share` adds the complete URL.** URL sharing is the primary
   agent-to-human handoff. `--share packet` selects the canonical base64url
   packet; `--share url` is the explicit spelling of the default. The selected
   scalar is written to stderr after ordinary diagnostics.
5. **Sharing preserves semantic choices or refuses.** A command must carry
   every projectable source, context, subject, facet, query, and traversal
   choice that affects the receiving inspection. It never drops an unsupported
   choice, substitutes a default view, or produces a less-specific link.
6. **Sharing adds no second inspection.** The command performs its ordinary
   Execute or Discover content work whether or not `--share` is present. Share
   projection may reuse the resulting resolved basis but does not invoke a
   second query, analysis, traversal, source, or rendering path merely to encode
   the scenario.
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
   |-> execute or discover content -> ordinary stdout
   `-> required share projection
         `-- --share -------------> URL or packet on stderr
```

Every plan includes the Share projection. `--share` renders that projection
without changing the content plan or its stdout. It serializes what the caller
asked the product to inspect: acquisition coordinates, Workspace context,
structural focus, exact subject selectors, selected facets or queries,
traversal choices, and other portable semantic options. It does not serialize
the target being inspected or the answer produced by inspecting it.

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

The same invocation without `--share` produces the same ordinary CLI content.
Appending `--share` adds the URL to stderr. It must not require a second command
such as:

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
  `https://dotnet-inspect.net/?w=<packet>` URL plus the platform newline as the
  final non-empty stderr line.
- `--share packet` writes exactly the canonical packet plus the platform
  newline as the final non-empty stderr line.
- Stdout is the same ordinary inspection content the invocation would produce
  without `--share`.
- Diagnostics, tips, and other normal stderr content retain their existing
  order before the Share scalar.
- If content carries an owner-issued non-success but Share is available, the
  scalar is still written and the command retains the content owner's nonzero
  exit behavior.
- If Share is non-projectable, ordinary content remains on stdout, stderr names
  the projection failure, no packet or partial URL is written, and the command
  exits nonzero because the explicitly requested side output failed.

`--share` may accompany Markdown, JSON, table, TSV, JSONL, plaintext, Mermaid,
URL-list, path, print, count, row-window, field, column, or Discover output.
Those gestures describe the CLI content stream; they do not change the
receiving Workspace scenario.

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

`--share` leaves the command's ordinary content work unchanged. A package
dependency invocation still acquires and inspects the package when that is what
its content path normally does. A member Call Graph invocation still executes
the graph analysis needed for stdout.

Share projection is separately bounded: it reuses the resolved semantic basis
and performs only work required to establish exact portable identity and
projectability. It does not run the ordinary query or analysis a second time,
request Browser-only enrichment, or serialize the content result.

If exact portable identity genuinely requires owner-issued resolution not
otherwise needed by the content plan, that owner may perform the bounded work
and must retain its ordinary failure semantics. Sharing does not create a
cheaper identity guess.

### Projectability and refusal

A valid CLI invocation is not necessarily URL-projectable. Workspace
Definitions may refuse because the current packet version cannot preserve the
scenario, the receiving host lacks the required portable query or restoration
binding, the source cannot be reacquired without excluded authority, or the
canonical packet exceeds its capacity.

When `--share` is present, the command reports that typed refusal as a nonzero
outcome with actionable stderr while preserving ordinary stdout content. It
must identify the first unsupported semantic choice or owner-issued projection
path. It must not:

- omit state and emit a URL anyway;
- fall back to a landing page, Overview, or another facet;
- replace an exact subject with a textual search;
- upload local content or credentials;
- serialize the executed query result or rendered content; or
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

Projection eligibility and Inspect Web restoration remain command-by-command
because each command owner must issue the typed source, target, facet, and
query state that sharing consumes. Commands do not independently reconstruct
the common target and selection plan:
[Inspection Plan Projections](inspection-plan-projections.md) supplies that
shared structural basis, content-purpose lowering, and required Share
projection.

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
Dependencies facet. Under the revised target, an unversioned package or
`@latest` is resolved and pinned while the command also performs its ordinary
Dependencies inspection for stdout. The receiving host preserves the requested
framework as the scenario and dependency-selection target. When the package
has no exact compile group but does have a compatible implementation universe,
the host may materialize that compatible compile universe without rewriting
the requested framework in the packet, active coordinate, or Dependencies
query. Type, package, library, call-graph, and later operation/query adoptions
remain separate slices under
[#6150](https://github.com/richlander/dotnet-inspect/issues/6150); a command
exposes `--share` only when its first useful scenario closes
through the receiving host.

### Real asset for Member Call Graph adoption

[#6540](https://github.com/richlander/dotnet-inspect/issues/6540) owns the next
focused adoption: sharing a real package member Call Graph from the CLI to
Inspect Web. Its motivating asset is `System.Text.Json@9.0.4` for `net9.0`,
using the exact public member
`System.Text.Json.Utf8JsonWriter.WriteStringValue(string?)`.

The immutable nuget.org archive is already retained at
`fixtures/services/signatures/system.text.json.9.0.4.nupkg`, with SHA-256
`a083aa7ce2085175d591f1624c223dc302090444d0a85ed970e26fda262eab5b`.
Its nuspec identifies
[`dotnet/runtime` commit
`f57e6dc747158ab7ade4e62a75a6750d16b771e8`](https://github.com/dotnet/runtime/commit/f57e6dc747158ab7ade4e62a75a6750d16b771e8).
The corresponding source is
[`Utf8JsonWriter.WriteValues.String.cs`](https://github.com/dotnet/runtime/blob/f57e6dc747158ab7ade4e62a75a6750d16b771e8/src/libraries/System.Text.Json/src/System/Text/Json/Writer/Utf8JsonWriter.WriteValues.String.cs).
Both source and archive are MIT licensed; the unchanged archive retains
`LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`.

The ordinary CLI invocation selects this overload as `WriteStringValue:7`.
Its resolved member anchor is `7a7f0afab9`. The real package graph has two
inbound serializer-converter callers, `EnumConverter<T>.Write` and
`UriConverter.Write`, and two outbound branches: null values call
`WriteNullValue`, while non-null values flow through `AsSpan`,
`WriteStringValue(ReadOnlySpan<char>)`, value validation, escape detection,
escaped or direct writing, and list-separator state. That bounded graph
demonstrates why preserving Call Graph selection matters; a link that silently
opens member Overview loses the inspection the agent was discussing.

The adoption has three planned steps:

1. This design records the real asset, observed graph, exact invocation,
   compatibility mapping, boundaries, and required gates.
2. [#6555](https://github.com/richlander/dotnet-inspect/issues/6555) implements
   the shared typed content/share model and adopts it in the exact-member CLI
   path for execution, effective discovery, and existing Overview sharing.
3. The final production-adoption PR projects Call Graph through that share
   plan and restores it in Inspect Web, gated against the unchanged committed
   package archive.

Step 2 is direct CLI adoption and step 3 is Browser/Wasm adoption. The feature
is not complete with only a packet writer, decoder test, or host-neutral
projection. Release and deployment follow the existing separately authorized
flow rather than becoming another feature slice.

This first useful scenario carries only the existing Call Graph facet. The CLI
has no `--depth` option, and this slice does not add graph query, caller-scope,
field, tree, Mermaid, or other rendering choices. Packet format 1 represents
the selection with its exact legacy Browser token `call-graph`; Workspace
Definitions owns its lowering to the Registry identity `member.call-graph`.
The CLI never serializes either spelling from display text.

The production slice requires these gates:

- an ordinary CLI invocation over `System.Text.Json@9.0.4`,
  `WriteStringValue:7`, and `net9.0` continues to expose the observed real
  Call Graph neighborhood;
- `--share packet` carries the exact package coordinate, framework, type,
  member anchor, selected library, and packet-v1 section `call-graph`, with no
  graph nodes, edges, or rendered rows;
- adding `--share` executes the same CLI Call Graph and produces the same
  stdout as the ordinary invocation, while writing the Inspect Web URL as the
  final non-empty stderr line;
- the published Browser opens that CLI-produced URL against the exact
  committed package archive, selects Call Graph rather than Overview, and
  renders the matching member graph through the normal Browser engine;
- neighboring member Overview sharing continues to omit the section; and
- unsupported member sections and caller-scope modifiers remain visible
  projection errors, while graph rendering choices continue to affect only the
  stdout content shape.

## Pathological and neighboring cases

The contract-defining pathological case for the Member Call Graph adoption is
a working invocation over the recorded real package whose source, subject, and
selected view are already correct:

```console
dotnet-inspect member Utf8JsonWriter WriteStringValue:7 \
  --package System.Text.Json@9.0.4 \
  --tfm net9.0 \
  -S "Call Graph" \
  --share
```

Once the adoption lands, this invocation resolves the package and exact member
once, runs the ordinary CLI Call Graph for stdout, preserves the Call Graph
facet, and writes the URL to stderr. Inspect Web reacquires the package and
runs its normal Call Graph operation. Before that complete path exists, the
invocation retains its ordinary content and fails visibly at the unsupported
Call Graph projection. It never silently emits member Overview and never asks
the user to restate the package, type, member, and section under `workspace`.

Neighboring cases prove the boundary:

- the equivalent exact package/member Overview invocation succeeds;
- an ambiguous member selection fails without choosing an overload;
- a local or private artifact that the public Browser cannot reacquire fails
  Share projection without leaking a path or credential while retaining
  ordinary CLI content;
- a floating package that resolves uniquely emits the resolved exact version;
- Markdown, JSON, Count, row selection, and graph formats preserve their
  ordinary stdout while the URL remains the final stderr line; and
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
exact overload gesture, rejects all section selection, and replaces ordinary
content with scalar stdout. Package Dependencies similarly proves exact
coordinate projection and Browser restoration through a share-only fast path.
Those implementations verify projection but do not yet satisfy the additive
stdout/stderr contract. The real-package Member Call Graph adoption is designed
and tracked by #6540 but remains unverified until its CLI-to-Browser production
slice lands. Existing-adopter migration is tracked by
[#6725](https://github.com/richlander/dotnet-inspect/issues/6725).

Each adoption must add focused Release gates proving:

- appending `--share` consumes the same normalized source, context, subject,
  facet, query, and traversal state as the ordinary command plan;
- adding `--share` leaves the selected Execute or Discover content and ordinary
  stdout unchanged;
- no argv serialization, synthetic command invocation, acquired target
  content, result payload, rendered-text identity, or duplicate source/focus
  grammar participates in the projection;
- bare `--share`, explicit `url`, and `packet` write the required scalar as the
  final non-empty stderr line after ordinary diagnostics;
- a uniquely resolved floating input is pinned to its exact portable
  coordinate;
- semantic selections are preserved when projectable and produce a typed
  refusal when not;
- Share projection does not cause a second content execution;
- Markdown, JSON, Count, row selection, Discover, and other content modes
  coexist with the Share side output;
- ambiguous identity and content failure retain their ordinary stdout/stderr
  and exit behavior when no envelope can be constructed;
- unsupported source, over-capacity state, and Browser-restoration gaps retain
  ordinary content, write no partial Share scalar, and fail nonzero when
  `--share` was requested; and
- the real Inspect Web URL restores the same normalized coordinates, context,
  subject, facet, query, and portable options.

The existing `MemberShare_*` tests gate the narrower member subset.
`MemberShare_RejectsLegacyLineWindowBeforeScalarOutput` specifically gates
the superseded scalar-only output contract and must be replaced by coexistence
coverage.
`DependsShare_PacketProjectsExactPackageDependencyView`,
`DependsShare_PacketPreservesCompatibleRequestedFramework`,
`DependsShare_UrlWrapsCanonicalPacket`,
`DependsShare_RejectsNonProjectableCoordinate`,
`DependsShare_RejectsLocalPackage`,
`DependsShare_RejectsNonNuGetOrgSource`,
`DependsShare_AcceptsExplicitNuGetOrgSource`,
`DependsShare_RejectsMappedPrivateEffectiveSource`,
and `DependsShare_RejectsBrowserPlatformPackageId` remain projection and
projectability witnesses but need additive stdout-preservation assertions.
`DependsShare_FloatingVersionResolvesWithoutPackageAcquisition` and
`DependsShare_RejectsConflictingOutput` encode the superseded fast-path and
mutual-exclusion behaviors; replace them with ordinary-content execution and
stdout/stderr coexistence coverage.
`PackageDependencies_UsesCompatibleAssetsWithoutChangingRequestedFramework`,
`PackageDependencies_SelectsUngroupedDependenciesWithCompatibleAssets`,
`QueryPackage_ReferenceOnlyCompatibleFrameworkRetainsDependencies`,
`root-only package surfaces remain inspectable without inventing a Library`,
`canonical package dependency views restore the package root lens`,
`canonical package views reject contradictory structural selection`, `capture
projects package Dependencies through the packet lens`, `capture refuses a
non-active package dependency group`, `Share copies canonical package
Dependencies and refuses a non-active group`, and `canonical package
Dependencies restoration clears a resident group override` gate the Browser
adapter.
`WorkspaceSharePacketTransposerTests` and codec vectors remain authoritative
for packet validity. An adoption is not complete with a decode-only test; its
gate must exercise the command-to-Browser handoff described by #6150.
