# Release Notes

## Unreleased

- **Breaking:** Renames `match --implementation` to `match --body`, with
  a `Method Body Diff` view. Body comparison now consumes the shared Queries
  designated-pair path and retains native endpoint and failure outcomes.
  `--body --json` uses a `match`/`body` envelope with typed native results
  instead of the former `match`/`implementation` presentation envelope.
  Plain `match --json` is unchanged (#5925).

## v0.22.0

### Inspection and matching

- Type API inspection now rejects unusable managed assembly identities with a
  path-attributed error instead of falling back to path-only extraction.
  Listing, compact platform summaries, and deferred type routing preserve the
  selected API assembly's provenance; managed netmodules retain their existing
  inspection behavior (#5853).
- Adds the `match` command for identity-agnostic structural comparison of two
  selected methods in one retained assembly. It preserves exact, near,
  different, unsupported, failed, limit-reached, and
  ambiguous-correspondence results instead of collapsing incomplete
  comparisons into matches. Add `--implementation` for side-by-side decompiled
  C# and IL views of both selected methods (#4540, #4555).
- Adds `graph integrations` for inducing typed Integration relationships over
  an explicit, binding-consistent package set. Markdown, tree, Mermaid, count,
  and structured formats project the same logical edges, while missing peer
  packages remain visible as failures instead of silently shortening the graph
  (#4421).
- Adds `demo` for listing and running closed product-home scenarios backed by
  real section output rather than separate demonstration logic (#4463).
- **Breaking:** Removes the hidden deprecated `api` command and the direct
  `ApiCommand.ExecuteAsync(ApiOptions)` compatibility entry point. Use `type`
  to discover or inspect types and `member` to inspect members. The removed
  `api` command token remains reserved so it cannot silently become an
  implicit package lookup.
- Fixes canonical metadata generic-arity handling across API identity,
  relationship traversal, type forwarding, PDB/source mapping, decompilation,
  and selector spelling. Nested and foreign generic types now retain their
  declared arity without trusting unvalidated name suffixes. Extension members
  now attach to their exact target type rather than an approximate same-named
  or enclosing type (#4233, #4539).
- **Breaking:** Canonical member identity now preserves exact metadata
  structure and spelling in declaring-type anchors and repairs represented
  canonical components used by selectors. Corrections include nested `+`
  separators, normalized generic-argument separators, literal-name escaping,
  encoded-arity handling, and malformed generic-instantiation spelling.
  Because `Name~digest` fingerprints that represented canonical signature,
  persisted selectors can change whenever one of those components was
  repaired; refresh affected selectors from `Member Index`. Extension
  selectors can also move between same-named types of different arity or from
  an enclosing type to a nested target; rediscover them on the exact target
  type (#4233).

### Library inspection safety

- Library-inspection references, classified methods, resources, performance,
  source-integrity summaries, failures, and union rows now carry their remaining
  presentation text through typed inert-text boundaries (#3463).
- API inspection identity fields, failures, type summaries, and unified member
  and surface tables now carry presentation text through typed inert-text
  boundaries (#3463).

### Source and implementation evidence

- Type source-file and portable-PDB acquisition now open the selected supplier
  descriptor, preserving its opener as well as its symbol policy. Thrown
  source-context failures are reported as command errors instead of appearing
  as missing source or symbols; genuine absence remains best-effort (#5888).
- Renames `Original Source` to `PDB Source` and the Implementation Diff
  `--authored-source` flag to `--pdb-source`, reflecting that checksum and
  SourceLink-origin evidence do not independently prove build provenance.
  Both old spellings remain accepted as hidden compatibility aliases. The
  Implementation Diff `Mechanism` value likewise changes from `Source` to
  `PDB Source` in Markdown and structured output (#4385).
- **Breaking:** exact `PDB Source` or `Source Diff` acquisition failures now
  return a non-zero exit status in count, tabular, JSONL, and document JSON
  output. Markdown, plaintext, bare, and print projections continue to render
  the explanatory payload. PDB Source can also use checksum-verified local
  documents without requiring a SourceLink map (#4385).
- Bodyless members now report that fact instead of a missing-PDB-mapping reason
  when platform API shape comes from a reference assembly and source lookup
  uses a different runtime image (#4588).

### Package acquisition and audit

- Online metadata-only package version queries now support configured folder
  feeds for pinned verification, latest and range selection, listing status,
  and per-feed rows. Payload inspection and offline local discovery remain
  separate work (#5400).
- **Breaking:** Online bare `--version`, `--latest-version`, and version ranges
  now fail when an eligible source is unreadable instead of selecting from
  incomplete evidence. Fix or exclude the failing source, or use raw
  `--versions` to inspect explicitly partial results. Online version queries
  bypass legacy producer-keyed caches; offline behavior is unchanged (#5400).

- Adds the opt-in `Audit: Findings` package section for bounded scans of
  text-bearing package files and decoded SourceLink maps. Findings identify
  control or bidi text, package-source declarations, cleared restore sources,
  concerning SourceLink text, and literal parent-path references while keeping
  artifact text visually encoded in terminal output (#4408).
- **Breaking:** Package signature inspection now verifies signed archive
  content against its embedded hash and validates NuGet signer, repository,
  certificate, and accepted timestamp profiles before reporting valid
  provenance. Malformed signature entries and invalid profiles fail closed.
  For a valid repository signature without a verified author signature, the
  Signature section now reports `Author Verified: No`, where v0.21.0 omitted
  that field. The `Repository` field now reports the verified repository
  certificate identity instead of a fixed `nuget.org` value, and `Repository
  Verified` is present when a valid repository signature is established,
  either as the primary signature or a valid countersignature (#4408).
- Package Signals now include default `Provenance / Signature` and
  `NuGet / Listing` rows for the verified signature and Gallery listing facts
  already acquired by package inspection (#4408, #4486).
- Uses authoritative Gallery registration metadata for package listing and
  version state. Pagination, malformed responses, deadline exhaustion, and
  rejected results remain explicit rather than being interpreted as complete
  package history (#4486).
- **Breaking:** Listing-aware `package` version output now emits column headers
  by default for every non-JSONL projection, including Markdown/table, TSV,
  plaintext, bare, and the current JSON compatibility path. This applies to
  plain, pinned, and ranged `--versions --include-unlisted` requests and to
  `--latest-version --include-unlisted`; JSONL is unchanged. Use `--no-headers`
  to retain the v0.21.0 headerless shape (#4486).
- Tool v2 RID-companion verification now falls back from direct nuspec lookup
  to authoritative version indexes on the active configured sources. `RID
  Package` availability remains `Unknown` after indeterminate source failures
  rather than being reported as absent, and cache hits with unknown
  source-dependent availability recheck the active sources (#4594).

### Performance analysis

- Moves Performance Triage onto the typed inspection-query path while
  preserving section selection, ranking, candidate identity, and structured
  output behavior. Body-shape predicates continue to intersect with the exact
  source MethodDefs selected by Performance Triage (#4409).
- Aggregate repeated-scan rows can now retain a separate exact supporting call
  coordinate in structured output. The support remains distinct from the
  aggregate Finding and candidate identity, enabling external runtime
  correlation without changing static priority or confidence (#4544).
- Distinguishes unresolved scoped recommendation owners instead of assigning
  async-alternative evidence to an unauthenticated source method (#4488).

### Analysis, decompiler, and trust correctness

- Materializes complete stack-slot copy components and raises the bounded
  structural-clone comparison limit, improving coverage without emitting
  partial clone results (#4407, #4506).
- Hardens scoped analysis diagnostic aggregation and aligns structural-review
  carets with their rendered evidence (#4499, #4374).
- Matches exact conversion callers by return type and rejects malformed trusted
  generic ownership rather than accepting ambiguous metadata identity (#4562,
  #4561).
- Grants core-platform status only to a provenance-backed coherent closure,
  preventing unrelated assemblies from acquiring platform trust by name or
  declaration alone (#4500).

## v0.21.0

### Query and package workflows

- Adds the `vocabulary` command for discovering product-owned query values and
  typed `Body Shapes` queries for library, type, and member scopes. Body-kind
  predicates use ordinary section projection and structured output, resolve
  property and event accessors, and, at library scope, compose with Performance
  Triage predicates without widening the selected source MethodDefs (#4312,
  #4370, #4390, #4410, #4435, #4442).
- **Breaking:** Removes the standalone `body-shape` command without a
  compatibility alias. Use `--where "Kind=<C# Body Kinds ID>"` with `library`,
  one exact `type`, or one exact `member` (#4460).
- Adds package dependency-tree projection and avoids package archive downloads
  when nuspec or dependency-only evidence is sufficient.
  `package --all-libraries` bare selection preserves its fixed overview and
  `--count` returns per-section maps; multi-library output paths and global row
  windows stay aligned across output formats (#4313, #4318, #4329, #4340,
  #4353, #4423).
- Adds a version-matched `package-skills` guide for discovering, validating,
  selecting, and persisting `SKILL.md` files supplied by restored NuGet
  dependencies. `project -S Skills` now validates package skill identity and
  descriptions and fails visibly for invalid metadata or missing restored
  skill files instead of returning tolerant rows (#4404).
- **Breaking:** `Unsafe Members` no longer expands from `@Audit`; select the
  independently selectable library section by name. Its rows are backed by
  typed unsafe evidence (#4319, #4415).
- Carries diff titles, summaries, versions, grouped type names, row values, and
  composed document fields through typed inert-text boundaries while
  preserving Markdown and structured output shapes (#4308).

### Performance analysis and runtime evidence

- Adds opt-in `sync-call-in-async` Performance Triage findings for synchronous
  calls made by async methods when a signature-compatible accessible Async
  sibling can be established. Findings retain exact call-site provenance and
  fail closed for ambiguous receivers, overloads, accessibility, or incomplete
  metadata. This is static structural evidence, not proof of runtime heat or
  behavioral interchangeability (#4091).
- Performance Triage structured JSON now carries declaring assembly, source
  MethodDef, physical evidence MethodDef, module version ID, and IL offset for
  runtime joins. Compact Performance JSONL remains intentionally
  non-correlatable (#4091, #4326, #4347).
- Top Leverage ranking now uses the typed query path, and call-graph selectors
  preserve physical file identity and full signature structure across scoped
  projections (#4335, #4388, #4396).
- Adds the opt-in Call Graph `AsyncAlternatives` field for per-method
  `sync-call-in-async` opportunity counts while Performance Triage retains the
  exact call-site evidence and replacement (#4380).
- Attributes async state-machine and lifted-body calls to their declared source
  methods in logical call output while retaining the physical `Evidence Method`
  and IL offset for each call-site receipt (#4466, #4461).
- Type-targeted body analysis retains the async/lifted physical evidence needed
  by the selected type while filtering performance opportunities and allocation
  fanout by the authenticated ultimate source owner. Ambiguous or unresolved
  ownership fails closed (#4481).

### Acquisition and safety

- Adds standard nuget.org search discovery. Package deadlines, redirects,
  response addresses, and SSRF classification fail visibly at their owning
  source boundary (#4155, #4349).
- Preserves primary assembly acquisition failures and refactors symbol-package
  and method-reference resolution into shared bounded owners (#4310, #4330,
  #4337).
- Rejects hostile symbol-package entry lengths before expansion or allocation
  (#4332).
- Bounds custom-attribute decoding and metadata type-name construction before
  allocation, rejecting amplified element counts, excessive nesting, and
  oversized names (#4234, #4345).
- Under the default policy, grants core-library identity from acquisition
  provenance instead of self-declaration so resolver-discovered sibling
  assemblies cannot mint trusted core definitions (#4428).

### Decompiler and analysis correctness

- Fixes explicit-interface accessor identity and API-surface retention,
  nested-generic and byref selector keys, generated framework type identity,
  async/lifted source ownership, and signature re-lexing that could produce
  malformed parameter declarations (#4359, #4371, #4386, #4402, #4425, #4431,
  #4445).
- Preserves raw reference-equality binding instead of rebinding decompiled
  comparisons to user-defined equality operators (#4250).
- Improves control-flow and dataflow fidelity for sparse switch partitions,
  legal multi-block `do`/`while` loops, retained-merge loop structuring, early
  region exits, and proven cross-block stack-slot live ranges. Unsupported
  lambda parameter types are declined rather than rendered with unspellable
  explicit types (#3971, #4154, #4299, #4301, #4314, #4333, #4363).

## v0.20.0

### Inspection reliability

- **Breaking:** `library` and `package --all-libraries` now return a non-zero
  exit status when an explicitly selected section is empty because its
  inspection failed. Resource Triage reports incomplete method analysis as a
  typed inspection failure instead of returning fewer rows or success-shaped
  empty output. Instruction decoding, method and catch resolution, metadata
  validation, method-body acquisition, and control-flow analysis retain stable
  failure phases across Markdown, JSON, exact-section, count, multi-assembly,
  and `package --all-libraries` projections. Legal native method bodies are no
  longer decoded as IL (#4273).
- Classified-method facts now come from one bounded typed query shared by
  `Library Info`, P/Invoke Methods, Async Methods, and Signals. Existing output
  remains compatible while failures and raw artifact identity stay typed until
  presentation (#4195).
- Internal inspection-graph composition now has a typed package boundary that
  preserves exact ownership and provenance without reconstructing identity
  from assembly names or display labels. Presentation and CLI integration are
  unchanged (#4269).

### NuGet acquisition

- **Breaking:** Package-search timeout failures now use stable tool-owned
  diagnostics instead of runtime-specific message text. `--http-timeout` now
  bounds service-index discovery and response-body consumption for
  `package search` and package-prefix expansion. Each source's search
  pagination has a separate operation ceiling four times that value (#4243).

### Experimental analysis and decompilation

- Adds bounded exact structural clone comparison and same-assembly discovery.
  Exact normalized IL/control-flow witnesses remain distinct from unsupported,
  failed, limited, ambiguous, and different outcomes; incomplete candidate
  buckets never emit partial clusters (#4114, #4280).
- Annotated-source and structural-review JSON now use strict decompiler-owned
  readers and writers. Missing, duplicate, unknown, malformed, or topologically
  invalid input is rejected without echoing artifact-provided values (#4203).
- Structural decompiler review can consume product-issued cross-document node
  correspondence backed by exact physical-method provenance, document
  revisions, and IL-origin evidence. Unsupported and ambiguous nodes remain
  explicit gaps rather than guessed additions or removals (#4254).
- Retry-loop recognition and adjacent control-flow scans now stay inside their
  owning local-function or lambda scope, preventing nested-function facts from
  inventing or reshaping outer loops (#4253).

### Engineering and validation

- IL diff ownership now has a dedicated assembly boundary. Windows CI restores
  `ilasm` and `ildasm` so oracle-backed Windows tests no longer skip
  (#4201, #4186).
- Structural clone comparison and same-assembly discovery gained
  compiler-produced and corpus coverage while preserving bounded work and
  deterministic evidence (#4114, #4280).

## v0.19.0

### Inspection and output

- `find --json` now composes with `--columns` and `--fields`, emitting a
  projected JSON document with the same rows, windows, compactness, and
  snake_case field vocabulary as TSV and JSONL output (#3536).
- **Breaking:** Explicit empty projection values and duplicate
  `--columns`/`--fields` entries are now rejected at parse time for every
  command and format. Overlapping wildcard patterns resolve each source column
  once, while a name matching no column continues to fail closed during
  rendering (#3536).
- Multi-package inspection now renders and counts `Signature`, package fields,
  and file projections consistently across Markdown, count, TSV, and JSONL
  output, including global row windows and empty package rows (#4004).
- Quiet .NET platform type listings use a compact shared-runtime metadata path,
  avoiding full extraction while preserving forwarded type and extension method
  counts. Rich, ASP.NET Core, pinned reference-pack, and structured output paths
  retain full extraction (#4175).
- Static and instance call-graph members now have distinct opaque selectors, so
  close signatures no longer collide during remapping (#4219).
- Call-graph projection now retains physical call-site receipts behind each
  logical edge, preserving exact loop evidence and disclosing incomplete
  occurrence sets instead of fabricating complete aggregates (#4193).

### Safety and acquisition

- Package presentation now carries containment evidence across every package
  text source through a typed boundary. Aggregate projections report whether
  containment was required while explicit document payloads remain
  byte-preserving (#3831).
- Package `Signals` now summarizes the Unicode concern kinds found in
  package-model text. `Audit: Artifact Text` adds content-free field locations
  and concern kinds without echoing the artifact content (#4090).
- Package and library `Signals` now summarize non-ASCII identifiers and
  reserved-prefix homoglyphs. `Audit: Identifier Confusion` adds content-free
  locations, classifications, similarity, and code points (#4090).
- Find-result views now carry titles, descriptions, type and member identities,
  source provenance, and row values through typed inert-text boundaries before
  Markout-backed Markdown, TSV, JSONL, and projected JSON rendering (#3463).
- PDB and SourceLink acquisition now handles pathless and content-shaped
  responses while preserving visible diagnostics for rejected evidence
  (#4138).
- NuGet metadata acquisition now bounds response bodies, attributes failures to
  their source, and reports malformed service indexes and version metadata
  instead of silently treating them as absent (#4134, #4247).
- Signature decoding and classification now enforce cumulative work budgets,
  keeping deeply nested or broadly repeated metadata from multiplying
  inspection cost without bound (#4170, #4188).

### Experimental decompilation

- Adds complete opt-in `var` spelling and a configurable
  explicit-versus-target-typed object-creation style. Both choices are
  byte-neutral (#4220, #4252).
- Expands whole-member compile-back coverage for constructors, properties, and
  events; recovers variable-less `using` statements; preserves local-function
  argument ref kinds; and fixes several control-flow ownership, stack-merge,
  and structuring fidelity failures (#4070, #4198, #4244, #4113, #4204, #4192,
  #4101, #4255).

## v0.18.0

### Inspection correctness

- Fixes fully qualified ASP.NET Core type and member routing when runtime
  catalogs span multiple shared frameworks or a namespace prefix names a
  non-owning assembly, while retaining real ambiguity errors (#4135).
- Uses a bounded declaration index for `PDB Source` and `Source
  Diff` slicing. Accessors now select their enclosing property or event,
  constructors retain their exact identity, and ambiguous or overly complex
  source boundaries fail visibly instead of returning fragments or empty
  output (#3927).
- Uses document-scoped PDB sequence points to select live conditional branches
  in PDB-mapped source, and refuses invalid coordinates or unsafe declaration
  boundaries instead of leaking inactive sibling declarations (#4158).
- Resolves assembly references by metadata identity rather than derived paths,
  preserving sibling/platform precedence, culture and token constraints, and
  visible failure state for unreadable or mismatched candidates (#3928).
- Uses structural keys for generated-member correspondence and exact IL
  coordinate evidence for allocation, safety, and callsite rows (#3857,
  #4107).
- Bounds cumulative API member-anchor signature construction work and memoizes
  repeated metadata type names, preventing long repeated names from amplifying
  allocations before the safety budget rejects them (#4162).

### Source and network safety

- Verifies fetched SourceLink content against its portable-PDB checksum and
  validates the final redirect origin before buffering the response. Source
  reads are bounded, retry-aware, and fail closed when final-origin evidence is
  unavailable (#4041).
- Recognizes commit-pinned Azure SourceLink URLs as immutable and bounds NuGet
  advisory JSON responses while retaining redacted diagnostics (#4104,
  #4117).
- Separates package-source policy by acquisition owner so package, symbol,
  platform-pack, and related fetches retain their intended source boundaries
  (#4136).
- Contains restored dependency-manifest paths beneath their target,
  global-packages, and owning-package roots, rejecting hostile entries without
  echoing them (#4132).

### Experimental analysis and decompilation fixes

- Extends `ArrayPool<T>` ownership analysis across progressively acquired call
  graphs while retaining bounded, exact path evidence and treating indirect
  calls as incomplete rather than safe (#4081).
- Orders named enum labels that share a switch body alphabetically by default;
  `dotnet_inspect_style_enum_case_label_order = value` retains recovered
  numeric order (#4072).
- Improves generated-member and authored-source correspondence, body-inspection
  session reuse, and full-body structural diagnostics used to find
  decompilation fidelity regressions (#3857, #3927, #4037, #4092).
- Raises local functions inside generic types through their generic type
  definitions while continuing to decline unsupported generic iterator imports
  visibly (#4116).

## v0.17.0

### Curated inspection and query model

- Gives `package` and `library` authored base and domain categories. Package
  exposes `@Package`, `@Files`, `@Dependencies`, `@Audit`, and `@SourceLink`;
  library exposes `@Library`, `@Surface`, `@Audit`, `@Performance`,
  `@SourceLink`, `@Integrations`, `@Metadata`, and `@Context` (#3838, #4061).
- Makes discovery distinguish structural membership from effective evidence.
  `-D --schema` reports the static graph, library `--effective` runs full
  probes, and bare `-S` returns high-value, fixed-length, network-free base
  sections. `--count` can summarize that overview, including zero-row
  candidates (#3566, #3754).
- Addresses rows by displayed number. `--row` accepts `N`, `first`, or `last`;
  `--rows` accepts a count, an inclusive range (`2..10`), start plus count
  (`2+10`), or an open range (`10..`). Exact output line limits are also
  honored (#3404, #3415, #3942).
- Routes package, search, integration, diff, metadata, references, resources,
  and custom-attribute evidence through typed inspection queries so selection,
  counts, and row windows share one contract.

### Metadata, source, and code evidence

- Adds the opt-in `@Metadata` library lens for ECMA-335 tables, image facts,
  and heaps. Handles resolve to their target rows, heap offsets resolve to
  values, table indices are addressable, and `--heap` reads one exact
  coordinate (#3301, #3465, #3510).
- Adds SourceLink map diagnostics, spec-correct document matching, encoded-dot
  support, local-repository source lookup, and portable annotated source maps
  (#3969, #3943, #3790).
- Adds `body-shape` for exact rendered-syntax searches in one assembly, with
  stable kinds, containing members, MethodDef tokens, exact ranges, and
  selected text (#4048).
- Makes `Call Graph` one bidirectional evidence section with Markdown edge
  rows, tree, Mermaid, TSV, and JSONL projections; adds bounded cycle findings
  and scoped cross-library traversal (#4001, #4013, #4069, #4065).

### Analysis and decompilation (experimental)

- Decomposes whole-library performance triage into kind-scoped
  `Performance:*` sections under `@Performance`, with per-kind counts and a
  homogeneous flattened row format (#2833).
- Adds resource-lifecycle and allocation-fanout triage, expands the Finding
  spine across Metadata and Analysis producers, and composes findings into
  timelines and implementation diffs.
- Raises more compiler-produced switch, local-function, lambda, range,
  short-circuit, tuple, and flags-enum shapes while continuing to report typed
  `DEC####` fidelity causes rather than plausible-but-wrong source.
- Uses readable synthesized local names by default and exposes stable,
  product-owned annotated-source spans, nodes, regions, facts, and targets.

### Package source fidelity

- Keeps package coordinates source-scoped: installed package directories no
  longer introduce version candidates, cached payloads retain their producer
  identity, and global-packages payloads are used only when their metadata
  names an authorized source.
- Honors layered NuGet `<packageSourceMapping>` across acquisition,
  dependencies, version discovery, search, routing, redirects, symbols, RID
  companions, and platform packs (#3908).
- Resolves latest versions across all active sources, uses configured sources
  for package metadata, separates stable and prerelease evidence, and reports
  unreadable or refusing feeds instead of success-shaped empty results (#3696,
  #3965, #4074).
- Scopes Azure credential-plugin results by organization, binds redirected
  challenges to the caller-selected source, redacts sensitive retry URLs,
  defers plugin discovery, recovers from dying plugins, and makes HTTP request
  timeout configurable (#3968, #4051, #4036, #3847, #3842).

### Safety, output, and packaging

- Carries untrusted artifact text through typed inert-text boundaries and
  contains metadata and package-authored text before rendering. Malformed
  nuspec XML now produces a one-line location diagnostic, and descriptions
  cannot impersonate tool headings or tables (#3679, #3772).
- **Breaking:** removes the hidden `--oneline` compatibility alias and
  `DOTNET_INSPECT_FORMAT=oneline`/`one-line`; use `--table`.
- Builds Native AOT packages with `OptimizationPreference=Speed`, worth a
  measured 6-7% on representative commands for about 9% more binary size
  (#3675).
- Refreshes the embedded skills for curated package/library discovery,
  categories, high-value bare selection, and the current `--row`/`--rows`
  grammar (#3866, #4079).

## v0.16.0

### Research overlay and performance analysis (experimental)

- Adds the `ILInspector.Research` overlay layer over Analysis and the
  Decompiler, with explicit `Cost` and `Semantics` overlay sections and a
  structured `Facts` table (#1920, #1934, #1932, #1927).
- Builds a shared `ControlFlow` dataflow fixpoint and reaching-definitions
  pass, then uses them to gate allocation triage: local-array and span
  `ToArray` copies are now promoted only when the value actually escapes,
  cutting false positives (#1880, #1896, #1905, #1903, #1907).
- Converges allocation and unsafety facts on the Analysis occurrences and
  adds an allocation-parity regression gate (#1910, #1918, #1909).
- Adds Rung 7 `Performance Triage` shapes: `async-state-machine` (reported as
  amortized off-loop) and `materialize-in-loop` (loop-invariant
  `ToArray`/`ToList`), plus nested-type triage drilldown (#1948, #1889).

### Output and projections

- Adds JSON array projection output and scalar URL/path shape projections,
  generalizes print row projection, and aligns library value projection with
  rendered fields (#1955, #1950, #1935, #1963, #1928).

### Decompiler fidelity and unions (experimental)

- Raises discriminated-union switch and declaration-pattern shapes
  (declaration rendering, value-type tests, two-arm/three-case/guarded switch
  expressions, else arms) (#1915, #1925, #1933, #1936, #1942, #1946, #1951,
  #1957, #1962).
- Improves fidelity skeletons: struct auto-properties and object overrides,
  extension methods, `in`/`out` parameters, constructor auto-property
  initializers, and ref-local/ref-slot declaration accuracy (#1947, #1937,
  #1945, #1943, #1929, #1906, #1919, #1911, #1901).
- Hardens fidelity-check infrastructure: zero-signal guard, phase timings,
  NuGet dependency resolution, and corpus-metadata-driven targeted checks
  (#1894, #1895, #1890, #1898, #1953).

### Docs and skills

- Documents the Research overlay architecture and refreshes performance,
  query, and projection skill guidance (#1927, #1958, #1956, #1940, #1952).
- Corrects stale `ci.yml` comments that described the old release-artifact
  model; `release.yml` builds every package fresh at publish time and CI
  never produces release artifacts (#1699).

## v0.14.0

### Grounding and skill workflow

- Renames the `package` README section to `Grounding` and adds a `--print`
  flag for emitting grounding/content payloads directly (#1659, #1672).
- Turns `dotnet-inspect skill` into a router to focused scenario sub-skills
  (`skill list`, `skill source`, `skill performance`, and more), with
  one-line descriptions sourced from each skill's YAML frontmatter
  (#1559, #1577).

### Performance analysis (experimental)

- Renames the `Optimization Opportunities` section to `Performance Triage`
  and ranks rows by triage priority (hot pay-dirt first) (#1530, #1545).
- Adds allocation-hotspot rows to `Performance Triage` and loop-aware
  allocation-regression detection to Analysis Diff (#1558, #1582).

### Decompiler (experimental)

- Large body of method-body raise, structuring, and printer fidelity
  improvements across the C# decompiler, plus honest `DEC####` degradation
  rather than plausible-but-wrong output. These remain experimental and are
  surfaced through `member -S @Source`.

### CLI fixes

- Fixes CLI batch processing bugs (#1679).
- Preserves `ref readonly` return signatures and function-pointer signature
  modifiers in rendered API surfaces (#1678, #1537).

### Usability fixes (#1690)

- `member` now accepts fully-qualified type names such as
  `System.String` and `System.String.Length`; the type/member boundary is
  resolved against real metadata instead of a fragile dotted-name heuristic.
- `library <path>` reports a missing local file as a file error rather than
  misclassifying the path as a NuGet package.
- All commands now report a missing required argument consistently — a concise
  error on stderr with a non-zero exit code — instead of some printing full
  help with a zero exit. Help and discovery remain available via `--help` and
  `-D`.

## v0.13.0

### SourceLink section consolidation

- Renames the undecorated single-section output mode from `--raw` to `--bare`;
  `--raw` now names the default raw/fetchable GitHub URL shape and pairs with
  `--blob`.
- Clarifies that `--bare` is a presentation-only modifier for already-selected
  payloads, while `--count` remains the reduction that collapses a selected
  section/vector to a single row count.
- Generalizes `--bare` beyond code sections to package README/content payloads
  and one-column SourceLink URL output.
- Normalizes GitHub file links in package README/content output from `blob` to
  raw URLs in the default agent-friendly URL mode.
- Removes the standalone `source` command. Use `package`, `library`, and `type`
  `-S "Source Files"` for type-to-SourceLink URL rows, and use `member -S
  "Source Locations"` / `member -S "PDB Source"` for member-level source
  evidence.
- Adds `library --il-offset` for MethodDef token + IL offset source
  symbolication through coordinate-scoped sections such as `Source Location`.
- Adds `--blob` as the GitHub browser URL toggle for SourceLink URL sections.
- Adds `-t` type filtering to `package`/`library -S "Source Files"`.

### SourceLink member locations

- Adds a `Source Locations` section for member groups and selected signatures,
  reporting SourceLink-backed file/line/URL rows without fetching source bodies.
- Resolves SourceLink rows for unpinned NuGet packages whose symbols are only in
  `.snupkg` packages by reusing the resolved package version during PDB
  acquisition.
- Repeats the start line in the `End Line` column for single-line member source
  locations so blank cells only mean the end line is unknown.
- Keeps library SourceLink audit sections discoverable via `-D` when their
  render data is produced only after the section runs.
- Keeps `Member Index` focused on selector/query columns while moving
  source-location evidence to the dedicated source section.

### Package documentation and project grounding

- Adds package file and documentation views for the best package README, Markdown files, explicit file listings, scoped content, and frontmatter/body extraction.
- Adds opt-in `Source Files` sections to `type`, `library`, and `package` for SourceLink type-to-URL rows.
- Verifies portable PDB identity before using SourceLink rows so multi-TFM package symbol PDBs cannot be paired with the wrong assembly.
- Extends package README/content output with JSON/JSONL and frontmatter/body-scoped modes.
- Supports multi-package `package` surveys with package/version provenance and optional `--skip-empty`.
- Adds `project [path] --agents-index` for direct dependency grounding manifests and `project [path] --readme <package-id>` for version-resolved dependency docs.
- Reports selected package README provenance in `--info`.

### Member lookup and source sections

- Adds the `Member Index` section with copyable `Name:N` selectors, stable `Name~digest` selectors, and printed canonical signatures.
- Removes the older `--params` and `-of` overload selector options.
- Keeps `--show-index` as a compatibility alias for `-S "Member Index"`.

### Member source views

- Replaces selected-member `@Audit` with `@Source` for coherent source-view discovery.
- Splits `Decompiled Source` into plain raised C# and `Annotated Source` into the mixed C#+IL view.
- Keeps one readable decompiled C# section and makes `@Source` include `IL`.
- Removes the production `IR (Stages)`/`--dump-stages` decompiler-debugging surface; per-pass IR remains available through `DecompilerHarness`.
- Documents the source-view model in repo docs and the embedded skill guidance.

### Output polish

- Uses alphabetical field ordering for `Package Info`.

### Bare-name routing

- Keeps exact platform libraries such as `System.Text.Json` on the library view while routing exact NuGet-only package IDs such as `System.CommandLine` to package inspection.
- Suggests likely command names for bare-token typos such as `packag` before falling through to NuGet package lookup.

## v0.10.5

### Library workflows

- Adds `Switches` for feature, compatibility, and runtime configuration switch action points.
- Adds focused integration coverage for ASP.NET Core, Authentication, and OpenAPI.
- Broadens integration detection for package-owned starter APIs across DI, Logging, Health Checks, Hosting, OpenTelemetry, and ASP.NET Core middleware/endpoints.

### Type and implementation inspection

- Keeps single-type verbosity in the tree-shaped type view, with `-v:n` and `-v:d` expanding overload leaves.
- Adds whole-type decompiled source output and improves lowered C# readability for common compiler patterns.

## v0.10.4

### AI integration fixes

- Detects `Microsoft.Extensions.AI.OpenAI` AI adapter APIs such as `AsIChatClient`, `AsIEmbeddingGenerator`, and related modality adapters.
- Includes package-owned OpenAI realtime client support types in the AI integration section.
- Renames the `Integrations` roll-up count column to `APIs`.

## v0.10.3

### Library integrations

- Adds library integration discovery for AI, Aspire, Dependency Injection, Logging, Options, Hosting, Health Checks, HTTP Client, and OpenTelemetry.
- Adds `package <id> --library` to inspect the primary DLL in a package when it is unambiguous.
- Adds section categories such as `@Integrations` so agents can discover or render related library sections together.
- Refines focused integration sections to show package-owned starter APIs and user-facing support types instead of raw referenced assemblies.
- Adds OpenTelemetry telemetry-control rows for public `DisableTracing` and `DisableMetrics` APIs.
- Adds HTTP Client sub-kinds such as HTTP Logging, HTTP Latency, and HTTP Diagnostics.

### Decompiled source

- Improves lowered C# rendering for loops, conditional returns, generic element loads, operator sugar, lambdas, local functions, enum cases, and compound assignments.
- Reduces unnecessary goto labels and unsigned casts while preserving clearer control flow.

### Cleanup

- Removes the stale `demo` command.

## v0.10.2

### Full member signatures

- Makes member `Signature` values full single-line C# declarations with accessibility, modifiers, and high-signal attributes such as `[Obsolete]`.
- Improves overload documentation matching for XML docs.
- Warns when requested projection columns are not available and points to `-D` discovery.

## v0.10.1

### Member output

- Splits logical method summaries into `Method Groups`, reserving `Methods` for actual method rows and overload signatures.
- Makes `member Type -m Name` render overload rows by default, with full signatures and optional `--show-index` member selectors.
- Includes method generic parameter lists, such as `Serialize<TValue>(...)`, in rendered signatures.
- Aligns `--table` and `--tsv` selected-section output with Markdown so narrowed `-S Methods` renders overload rows.
- Adds first-class `Operators`, `Explicit Interface Implementations`, and local `Extension Methods` sections to type/member views.

### JSONL output

- Adds `--jsonl` for one JSON object per table row using the same stable projection as `--tsv`.

## v0.10.0

### Table and TSV output

- Adds `--table` for compact pretty-printed rows and `--tsv` for normalized tab-separated rows.
- Treats `--table` and `--tsv` as single-table formats; select one section with `-S` or use Markdown/JSON for multi-section output.
- Keeps `--oneline` as a hidden compatibility alias for `--table`.
- Normalizes Markdown table cell pipe characters to `&#124;` instead of escaped pipes.

### Type shape output

- Collapses overload-heavy default single-type trees by logical member name, while leaving full overload signatures available through `-v:n`, `-v:d`, and targeted member queries.

## v0.9.4

### Cache

- Silently cleans older versioned cache categories in the background when each family registers, while preserving cache contracts created by newer tool versions.
- Cache deletion paths are guarded so cache clearing and cleanup refuse to delete outside the active or legacy dotnet-inspect cache roots.

### Lowered C# output

- Recovers more recent C# lowering patterns, including `lock` statements, null-conditional assignments, and span collection expressions backed by inline-array helpers.
- Renders null-conditional property compound assignments such as `target?.Count += value` when the compiler-lowered shape is safe to fold.

## v0.9.2

### Package resolution

- `--preview`/`--prerelease` now opt latest package resolution into prerelease versions, including `library <dll> --package <package> --preview`.

### Output

- `Signals` no longer includes a SourceLink CR/LF placeholder row; CR/LF diagnostics are reported only by the `SourceLink Integrity` section.
- Library `Signals` now owns the `Async Kind` roll-up (`Runtime`, `State machine`, `Mixed`, or `None`); `Library Info` no longer duplicates it.
- Library output with explicit section selection now keeps a compact context row with key fields such as version and source.
- Symbol lookup misses are cached to avoid repeated network probes; 403 symbol-server misses are cached for 7 days.

## v0.9.1

### Fixes

- `library <dll> --package <tool-package> -S "SourceLink Integrity"` now resolves Tool v2 pointer/RID packages to their inspectable framework-dependent payload package.
- CI smoke tests now write directly to files again after the .NET 11 stdout redirection fix.

## v0.9.0

### Signals

- Replaced the top-level `audit` command with explicit `Signals` section selection for package and library signal reports.
- Added SourceLink availability and CR/LF mismatch diagnostics to library Signals.
- Package Signals now include symbol/source evidence grouped by PDB source, including `msdl.microsoft.com`, `.snupkg`, embedded, and in-package PDBs.

### Package inspection

- Added `Library Files` to list all files under `lib/` across target frameworks.
- Added package manifest version output and removed the redundant manifest schema row.
- Added `-S @All` to select all sections, including opt-in sections.

### SourceLink

- SourceLink Integrity now treats CR/LF-only checksum differences as verified with a diagnostic row.
- Removed duplicate `source --audit`; use `library <target> -S "SourceLink Availability"` for full SourceLink reachability.

## v0.8.1

### Skill guidance

- Embedded `dotnet-inspect skill` guidance now includes a compact Modern .NET / preview workflow for runtime async classification, runtime-pack/platform assemblies, extension properties, and implementation-lowering inspection.

## v0.8.0

### Highlights

- The `source` command maps MethodDef token + IL offset pairs to source file locations, with Markdown, table/TSV, and JSON output.
- `--count` returns a single integer row count when exactly one table section is selected.
- `library -S "Async*"` lists async methods and classifies them as runtime async or classic state-machine async.
- Platform assembly resolution is SemVer/prerelease-aware and resolves runtime-only assemblies such as `System.Private.CoreLib`.
- `type` and `member` discovery now default to effective `-D` output; use `--schema` for the static schema.
- Obsolete members are shown by default with an obsolete marker and message when available.

### Improvements

- `-S`, `--columns`, and `--fields` accept semicolon-separated lists in addition to comma-separated lists.
- Effective discovery and field projection are wired through API type/member routing and markdown output.
- Package-backed `type`, `member`, and related commands preserve package/library context more reliably for multi-library packages.
- NuGet configs containing local folder or `file://` sources no longer block later HTTP feeds during package resolution.
- Assembly public key tokens are computed from the full public key using the ECMA-335 SHA-1 algorithm.

### Documentation

- README.md is now a concise capability inventory.
- SKILL.md is now workflow-oriented for agents: upgrade triage, find-to-member drill-in, source/IL lookup, platform release notes, package/library audit, structured queries, and relationship exploration.
