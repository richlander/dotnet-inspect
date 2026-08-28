# Inspect-web TypeScript semantic facts

## Status

This document defines the architectural owner and implementation target for the
inspect-web TypeScript semantic-facts adapter. The adapter is implemented in
`prototypes/inspect-web/scripts/typescript-semantic-facts.ts`; its snapshot,
identity, failure, import-isolation, and artifact-isolation properties are
enforced by the `inspect-web-typescript-semantic-facts` gate described below.
Actual child-process exit after the upstream close call remains **unverified**
because the pinned API exposes no exit-completion signal.

This is the focused successor selected from
[PR #4825](https://github.com/richlander/dotnet-inspect/pull/4825) and is tracked
by [issue #4936](https://github.com/richlander/dotnet-inspect/issues/4936).

## Decision

`Inspect-web TypeScript semantic facts` is the single owner for opening one
pinned TypeScript project snapshot and exposing its semantic information
through repository-owned types and explicit query results.

The adapter is a compatibility boundary around TypeScript 7's unstable
`typescript/unstable/sync` and `typescript/unstable/ast` APIs. Consumers never
receive TypeScript API objects, numeric enum values, or unchecked `undefined`
results.

The adapter reports facts. It does not decide whether an operation is allowed,
whether a value crossed a boundary, or which product component owns a browser
capability.

This is Node build-time tooling. It does not enter the browser application or
its production dependency graph.

## Ownership and boundaries

The semantic-facts owner defines:

- project opening, snapshot lifetime, and disposal;
- the repository-owned handles used to refer to files, nodes, symbols,
  declarations, types, and signatures within one snapshot;
- alias normalization and declaration provenance;
- on-demand symbol, type, contextual-type, signature, overload, module, and
  source-location queries;
- explicit absent, unavailable, ambiguous, and stale-handle outcomes;
- deterministic ordering and caching within a snapshot; and
- characterization and non-vacuity tests against the pinned TypeScript API and
  real inspect-web project.

The adapter consumes:

- one absolute filesystem path to a `tsconfig.json`;
- the files, compiler options, libraries, and dependencies selected by that
  project; and
- query handles issued by the same open adapter session.

The adapter does not own:

- a boundary descriptor or manifest format;
- product source-language restrictions, which
  [issue #4909](https://github.com/richlander/dotnet-inspect/issues/4909)
  tracks;
- reference transport, data flow, provenance, or authorization, which
  [issue #4911](https://github.com/richlander/dotnet-inspect/issues/4911)
  tracks;
- numeric DOM admission, which
  [issue #4819](https://github.com/richlander/dotnet-inspect/issues/4819)
  tracks;
- selector ownership, which
  [issue #4818](https://github.com/richlander/dotnet-inspect/issues/4818)
  tracks; or
- runtime validation, sanitization, state, events, rendering, or general
  TypeScript linting.

A consumer may combine facts into a policy in its own owner. That policy cannot
claim the adapter proved the consumer's inventory, completeness, or runtime
behavior.

## Session contract

### Opening

The adapter accepts an absolute filesystem path to one `tsconfig.json`.
Relative paths, directory paths, and file URLs are invalid inputs. This
preserves the operational behavior confirmed by the TypeScript 7 spike:
`openProjects` opens the real inspect-web project from its absolute path, while
a file URL does not identify that configured project.

One adapter session owns one TypeScript `API`, one `Snapshot`, and one selected
`Project`. Opening fails unless:

- TypeScript returns exactly the project at the requested normalized path;
- the project has one program and checker in the snapshot;
- configuration, program, syntactic, binding, global, and semantic diagnostics
  required for the strict product graph are empty; and
- every path TypeScript reports in `Project.rootFiles` resolves to a source file
  in that selected program.

The failure retains normalized diagnostics and project candidates. It never
falls back to an inferred project, a neighboring configuration, a partial file
set, or a second TypeScript installation.

This validates fidelity to TypeScript's configured roots. It does not prove that
the `tsconfig.json` names every file the product should contain; product
inventory is outside this owner.

Opening is transactional. If input, project selection, source resolution,
diagnostic validation, or fact initialization fails after the TypeScript API or
snapshot is created, the adapter disposes the partial snapshot and closes the
API before returning failure. The caller never owns resources from a failed
open.

Opening returns one of:

```text
Opened(session)
InvalidInput(RelativePath | DirectoryPath | FileUrl | MissingPath)
ProjectSelectionFailed(NoProject | MultipleProjects |
                       RequestedProjectMismatch, candidates)
DiagnosticsRejected(Configuration | Program | Syntactic |
                    Binding | Global | Semantic, diagnostics)
UnsupportedApi(UnsupportedVersion | UnsupportedApiValue |
               UnsupportedResponseShape, detail)
InfrastructureFailed(ProcessFailure | ProtocolFailure, detail)
```

Every non-`Opened` result carries the ordered cleanup failures observed after
its primary result:

```text
CleanupFailure(SnapshotReleaseFailure, detail)
```

The collection is empty when no resource was created or cleanup succeeded. A
cleanup failure never replaces or hides the primary opening result. Failed-open
cleanup uses the disposal procedure below, including its `finally` guarantee.

### Lifetime

Repository handles are opaque and session-scoped. They contain an adapter
session identity plus a kind-specific identity; consumers cannot construct
them from a path, source span, display name, or TypeScript numeric handle.

Every query validates that its handle belongs to the active session. A handle
from another session returns `InvalidHandle(StaleSession)`; it is not looked up
in the current snapshot.

The snapshot is immutable for the session lifetime. The adapter does not watch
files, update a snapshot in place, or mix facts observed before and after a
source change. A caller that needs current facts disposes the complete session
and opens a new one.

Disposal releases the project snapshot and invokes TypeScript API closure.
The adapter calls `Snapshot.dispose()` and calls `API.close()` in a `finally`
block even when snapshot release fails. It returns and latches one result:

```text
Disposed
DisposeFailed(non-empty CleanupFailure collection)
```

Repeated disposal returns the latched result without issuing another release or
close. Queries validate terminal state before handles: a disposed session
returns `SessionFailure(SessionDisposed)` even if it was previously poisoned;
an active session poisoned by a process or protocol failure returns that
latched `SessionFailure`; only a healthy active session reaches handle
validation.

The pinned sync API exposes neither its child process nor exit completion, and
its channel swallows close and kill failures. `Disposed` therefore means
snapshot release succeeded and `API.close()` was invoked; actual child-process
exit after that invocation is unverified. The adapter does not import private
channel internals to claim a stronger result.

### Source scope

The adapter enumerates the source files selected by the TypeScript program and
returns normalized repository-relative or external-library locations. It does
not perform a second recursive file search.

Each source file fact states whether TypeScript classifies it as:

- a project root or imported project file;
- a default library declaration;
- an external library; or
- another program-selected declaration file.

This is provenance, not policy. Consumers decide which classifications are in
their own analysis scope.

## Fact model

### Handles and locations

The repository model uses these run-scoped handle kinds:

```text
SourceFileHandle
NodeHandle
SymbolHandle
DeclarationHandle
TypeHandle
SignatureHandle
```

Each fact that has source syntax carries a normalized source location:

```text
SourceLocation(file, content, start, length, line, column)
```

`start` and `length` count UTF-16 code units in the exact source text held by
the snapshot. `start` is TypeScript's trivia-excluding `Node.getStart()` and
the half-open end is `Node.end`; `length` is their difference. `line` and
`column` are zero-based and identify that canonical start. CRLF counts as two
UTF-16 code units before line mapping, and a non-BMP scalar counts as its UTF-16
surrogate pair.

A source-file fact exposes a `SourceContentId`: SHA-256 over every UTF-16 code
unit of the exact snapshot-owned source text serialized low byte then high byte,
with no byte-order mark, normalization, or replacement. A caller computes the
same identity over the text it parsed and supplies it with every coordinate
query. This UTF-16LE representation preserves unpaired surrogates. The adapter
compares the identity before matching a span or node kind.

A parser that reports byte offsets must convert them against the same source
text before requesting coordinate correlation. The adapter does not guess the
caller's coordinate encoding. Coordinate lookup requires the canonical start,
length, source-content identity, and expected repository node kind. A content
mismatch returns `InvalidCoordinate(SourceContentMismatch)` without examining
the span; multiple matches return `Ambiguous`.

Locations and handles are identity, not display text. Pretty-printed type and
signature text may accompany a fact for diagnostics, but consumers must not
recover identity from that text.

Node kinds, symbol flags, type categories, signature kinds, and diagnostic
categories are mapped to repository-owned enums or discriminated records.
Unknown values from a newer TypeScript version produce an explicit
`UnsupportedApiValue` result.

### Query results

Queries return one of these closed variants:

```text
Resolved<T>
Absent(reason)
Ambiguous(candidates, reason)
Unavailable(MissingApiFact | UnknownSymbol |
            UnsupportedApiValue | UnsupportedResponseShape, detail)
InvalidCoordinate(SourceContentMismatch | OutOfRange)
InvalidHandle(StaleSession | WrongKind)
InvalidArgument(OutOfRange)
NotApplicable(expectedSubject, actualSubject)
SessionFailure(SessionDisposed | ProcessFailure | ProtocolFailure, detail)
```

`Absent` means the operation is defined and no fact exists, such as a symbol
without a value declaration. `Unavailable` means TypeScript could not provide a
fact required by that query. The adapter does not collapse either outcome into
an empty collection, unknown symbol, error type, or successful `undefined`.

TypeScript error types remain resolved `TypeFact` values with an explicit
`Error` category. The TypeScript unknown-symbol sentinel is not an ordinary
symbol fact; it returns `Unavailable(UnknownSymbol)`. Process and protocol
failures poison the session and every later query returns the same
`SessionFailure` kind.

After terminal-state and handle validation, each query validates coordinates
and arguments, then its subject category, before calling TypeScript. A wrong
category returns `NotApplicable`; an invalid index returns `InvalidArgument`.
For an applicable query, a documented absence returns `Absent`, an empty
semantic collection returns `Resolved([])`, and a missing result for a required
fact returns `Unavailable(MissingApiFact)`.

| Query family | Accepted subject | TypeScript `undefined` or empty result |
| --- | --- | --- |
| Coordinate correlation | Source file, matching content, span, and node kind | No match is `Absent`; many are `Ambiguous` |
| Symbol or type at syntax | Any syntax node | `Absent` |
| Source symbol | Shorthand or export-specifier node | `Absent` |
| Contextual type | Expression node | `Absent` |
| Resolved signature | Call-like node | `Absent` |
| Signature parameter type | Signature and in-range parameter index | `undefined` is `Unavailable`; bad index is `InvalidArgument` |
| Signature target | Any signature | `Absent` |
| Declared symbol type | Any symbol | No optional result |
| Symbol value type | Any symbol | `Absent` |
| Symbol type at location | Symbol and syntax node | No optional result |
| Union or intersection constituents | Matching union or intersection type | Empty is `Resolved([])` |
| Class or interface base types | Class or interface type | Empty is `Resolved([])` |
| Instantiated type arguments | Type-reference type | Empty is `Resolved([])` |
| Apparent, widened, or non-nullable type | Any type | `Unavailable` |
| Literal base type | Literal type | `Unavailable` |
| Base constraint | Any type | `Absent` |
| Properties, indexes, call, or construct signatures | Any type | Empty is `Resolved([])` |
| Alias target | Symbol with the alias flag | Unknown sentinel is `Unavailable` |
| Module exports and named lookup | Module symbol | Empty list is `Resolved([])`; lookup miss is `Absent` |
| Module symbol | Static string-literal module reference | Missing symbol is `Absent` |
| Constant value | Enum member, property access, or element access | `Absent` |

The pinned TypeScript 7.0.2 checker panics when asked for the type of a
type-only import-clause node. That node remains a valid syntax subject, but the
adapter recognizes it before invoking the checker and returns
`Unavailable(MissingApiFact)`. The implementation gate sweeps every node in a
real type-importing source through the symbol and type queries so this upstream
failure cannot poison the session.

Calling a category-specific query with any other subject returns
`NotApplicable` without invoking TypeScript. The implementation gate derives
this table from the facade declarations so missing and stale query mappings
both fail.

Collections preserve TypeScript order when that order is semantic, such as
parameters and type arguments. Otherwise source-backed facts are sorted by
normalized source location and stable repository fields. Run-scoped numeric
handles never determine order. An empty collection is successful only when the
TypeScript operation itself establishes that there are no members,
declarations, signatures, or candidates.

### Nodes

A source file can enumerate a repository node tree in source order. Each node
fact includes its handle, repository node kind, source location, optional
parent, and ordered child handles. Identifier and literal spelling may be
returned as source evidence, never as semantic identity.

The facade accepts a node handle for symbol, type, contextual-type, constant,
and resolved-signature queries. It also resolves a source coordinate plus
expected repository node kind to one exact node or an explicit absent or
ambiguous result. This lets a stable parser correlate its source coordinate
with TypeScript semantics without receiving a TypeScript AST object.

The initial facade does not reproduce every typed AST property. A future
consumer that needs another relationship adds one repository-owned query with
characterization rather than exposing the underlying node.

### Symbols, aliases, and declarations

A symbol fact includes its repository flags, escaped and display names,
declaration handles, optional value declaration, parent, and export symbol when
available.

Alias resolution returns both:

- the immediate alias chain, including each alias declaration; and
- the final original symbol or an explicit unresolved/unknown result.

The adapter never substitutes source spelling for a missing symbol. Two symbols
with the same name remain distinct when TypeScript gives them distinct
identities or declarations.

A declaration fact includes node kind, source location, source-file
classification, and its containing declaration chain. Declaration identity is
the basis for consumers that need to distinguish platform, dependency, and
product definitions.

### Types

A type fact exposes its repository category, symbol and alias symbols when
present, declaration-backed identity, intrinsic or literal value when
applicable, and flags relevant to later queries.

Type structure is queried on demand. The adapter exposes repository results for:

- union and intersection constituents;
- apparent, widened, non-nullable, literal-base, and base-constraint types;
- class and interface base types;
- instantiated type arguments;
- properties and index information;
- call and construct signatures; and
- a symbol's declared type, value type, and narrowed type at a location.

The adapter does not recursively materialize a complete type graph. Consumers
walk only the relations they request and own their traversal bounds. Returned
type handles preserve cycles and sharing by identity.

TypeScript error, any, unknown, and never types remain distinct facts. An
unresolved reference returned as a type is a resolved `Error` type fact, as
reported by TypeScript; it is not a separate type category. An error type is
not a valid substitute for a missing type result.

### Signatures and overloads

A signature fact includes its kind, declaration handle when present, optional
target signature handle, type parameters, `this` parameter, ordered parameters,
rest information, return type, and type predicate.

For a call-like node, the adapter returns the exact resolved signature selected
by TypeScript or an explicit absent/unavailable result. It separately exposes
all call or construct signatures of a queried type.

These are different facts. A property such as `querySelector` may have one
symbol with several declaration and signature candidates, while one call site
selects one instantiated signature. The adapter preserves candidate and
selected identities. An instantiated signature's optional target handle links
it to the original generic signature exposed by TypeScript. The adapter does
not invent an overload selector, minimum arity, or policy anchor.

### Context, modules, and constants

The adapter exposes repository results for:

- a node's direct and contextual types;
- the parameter type at a selected signature position;
- the source symbol of shorthand and export specifiers;
- module symbols, their declarations, exports, and named export lookup; and
- TypeScript constant values when the checker provides one.

A static module specifier without a module symbol is `Absent`; this includes a
side-effect import whose target is a non-module script. Computed and dynamic
module references are `NotApplicable`. A non-constant expression or missing
contextual type is `Absent`. The adapter neither rejects the source construct
nor infers an answer with a second parser or a reimplementation of TypeScript
module resolution.

## TypeScript compatibility boundary

Only one tooling module may import `typescript/unstable/sync` or
`typescript/unstable/ast`. That module translates TypeScript API values before
returning to the repository facade. Tests may import a separate adapter test
seam, but not the unstable packages directly.

The adapter batches equivalent TypeScript queries where the unstable API
supports batching and memoizes translations by session and TypeScript object
identity. A private session-scoped registry maps repository handles to the raw
TypeScript objects required by follow-up queries. It is confined to the one
unstable adapter module, never returned through the facade, and cleared during
disposal. All other caches contain repository facts and handles only and are
also cleared on disposal.

The pinned TypeScript version is part of the owner contract. A version update
must pass the characterization gate before the package pin changes. A changed
or removed unstable API member is an explicit compatibility failure, not a
reason to weaken a repository fact or return a default.

The facade does not expose generic "invoke TypeScript method" or raw flag
escape hatches. A new consumer need adds one reviewed repository query and its
characterization rather than importing the unstable API beside the adapter.

## Failure behavior

Opening or querying fails visibly when the adapter cannot:

- start or communicate with the TypeScript API process;
- select exactly the requested project;
- obtain the strict project diagnostics and source set;
- resolve a source file, node, symbol, declaration, type, signature, project,
  or module fact required by a query;
- translate an API enum, flag, category, or response shape; or
- validate session ownership and lifetime.

Failures include the operation, normalized project or source location when
available, repository result kind, and TypeScript detail safe for deterministic
diagnostics. Process termination and protocol failures remain distinct from
semantic absence.

The adapter never retries against another compiler, reparses source with OXC,
repairs a TypeScript result, or returns a success-shaped placeholder.

## Gate and evidence

The implementation adds an `inspect-web-typescript-semantic-facts` package
script. Its gate contains:

1. **Real project opening:** opens the absolute inspect-web `tsconfig.json`,
   selects exactly that project, enumerates its real program files, and rejects
   relative, directory, file-URL, ambiguous, and missing paths.
2. **Snapshot lifetime:** proves same-session handles resolve, cross-session and
   disposed handles fail, disposal is idempotent, every failed-open path cleans
   up a partial API and snapshot, and a new session observes a new immutable
   snapshot. Fault injection at snapshot release proves API closure still runs,
   cleanup failures remain visible, and repeated disposal returns the same
   latched result. Actual child exit after the upstream close call is explicitly
   unverified.
3. **Fact characterization:** maps every repository node, symbol, type,
   signature, diagnostic, and source-file category used by the facade against
   TypeScript 7.0.2. Removing a mapping or adding an unknown API value fails.
4. **Alias and declaration identity:** distinguishes lexical shadows and
   same-named declarations, preserves immediate alias chains, and resolves an
   imported alias to its original declaration.
5. **Type queries:** covers unions, intersections, literals, constraints,
   generics, base types, properties, indexes, narrowed types, error types, and a
   cyclic shared type graph without flattening identity.
6. **Signature and overload queries:** proves that the real DOM
   `querySelector` symbol has multiple candidates while representative calls
   return their exact selected and instantiated signatures, including target
   signature identity, `this`, rest, generic, and predicate cases. No minimum
   argument count is inferred.
7. **Context, module, and constant queries:** distinguishes direct and
   contextual types, resolves module-symbol, declaration, and export identities
   plus checker constants, returns `Absent` for unresolved static specifiers and
   symbol-less side-effect imports, and returns `NotApplicable` for computed or
   dynamic references.
   Coordinate correlation covers leading trivia, CRLF, non-BMP text, byte-to-
   UTF-16 conversion by a caller, exact matches, ambiguous spans, and a
   same-length source mutation that must return `SourceContentMismatch`. It also
   distinguishes two unpaired-surrogate mutations with the lossless content ID.
8. **Failure results:** mutation-tests unavailable, ambiguous, unknown-symbol,
   error-type, invalid-coordinate, invalid-handle, invalid-argument,
   not-applicable, disposed-session, protocol, and process outcomes against
   their exact closed variants so none become successful empty facts or an
   undifferentiated unavailable result. It also proves disposal takes
   precedence over a prior poisoned state and that handle validation runs only
   for a healthy active session.
9. **Import isolation:** a named non-vacuity test uses the toolchain gate's
   shared, case-insensitive TypeScript and JavaScript source inventory and fails
   if any module other than the one adapter references either unstable
   TypeScript API.
10. **Artifact isolation:** audits the Vite graph, runs the production build,
    requires its shipped chunks to equal the audited chunks, and proves the
    adapter, TypeScript API packages, and semantic fact code are absent.

The implementation gate is:

```bash
cd prototypes/inspect-web
npx --yes node@24 --run inspect-web-typescript-semantic-facts
npx --yes node@24 --run build
```

The focused gate is implemented by
`prototypes/inspect-web/test/typescript-semantic-facts.test.ts` plus the shipped
chunk equivalence owner in `prototypes/inspect-web/test/toolchain.test.ts`. It
opens the real inspect-web project and compiled fixtures, exercises the public
facade and failure seams, scans unstable-package references, audits the Vite
graph, and proves that audit describes the production build.

This gate proves the adapter contract only. It does not prove a semantic
consumer's rules, coverage, or behavior.

## Adoption

1. Add the one unstable-API adapter and repository fact/result types.
2. Establish real-project opening, lifecycle, characterization, import
   isolation, and artifact isolation without adding a boundary policy.
3. Publish the repository facade for focused consumers.
4. Let #4819, #4818, #4909, and #4911 consume or extend the facade only through
   their own independently reviewed changes.

No OXC check or product policy is removed when this adapter lands. The adapter
can ship with no production boundary consumer.

## Alternatives considered

### Keep unstable TypeScript objects behind a file boundary

Rejected. Returning those objects still couples every consumer to unstable
methods, numeric flags, lifetime, and failure behavior.

### Serialize the complete semantic graph

Rejected. Type graphs are cyclic, shared, and potentially large. Run-scoped
handles plus on-demand repository queries preserve identity without eagerly
materializing the graph.

### Use TypeScript display text as identity

Rejected. Display strings are presentation and can collide or change across
compiler versions. Symbols, declarations, signatures, and types retain
separate handles.

### Use the stable TypeScript 6 compiler API

Deferred. It would analyze a graph different from the pinned TypeScript 7 graph
that checks the product. Adopting a second compiler requires a separate design
that owns version skew and disagreement behavior.

### Reconstruct facts with OXC

Rejected. OXC syntax remains useful to syntax-owning checks, but it cannot
replace TypeScript's resolved types, declarations, overloads, and contextual
facts.

## Non-claims

The semantic-facts adapter does not prove:

- that product source obeys a capability boundary;
- that a descriptor, catalog, or policy is complete;
- reference provenance, data flow, transport safety, or authorization;
- that eval, dynamic imports, decorators, protocol hooks, or other source
  constructs are safe for a consumer;
- numeric decoding, selector correctness, runtime validation, or sanitization;
- application state, event, rendering, or interaction behavior; or
- general TypeScript or JavaScript correctness.

A clean adapter gate proves only that repository semantic facts faithfully
represent the configured TypeScript snapshot for the queries the facade owns.
