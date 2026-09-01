# CLI compatibility and deprecation

The **CLI compatibility policy** is the normative owner for deciding whether
an observable `dotnet-inspect` command-line change is compatible, requires a
migration path, or is intentionally breaking. It also owns the states and
evidence required to deprecate or remove published syntax.

It does not enumerate the current command set or define each command's
semantics. Those responsibilities remain with:

- the root [`README.md`](../../README.md), visible `--help`, and embedded
  product skills for current supported invocations;
- [CLI host architecture](../cli-architecture.md) for parsing, routing,
  authorization, request lifetime, presentation, and exit-status mechanics;
- [Command transitions](command-transition-model.md) for deciding whether a
  new gesture should be a command, option, selector, section, or rendering
  choice;
- [Progressive disclosure](progressive-disclosure.md), [output
  shapes](output-shapes.md), and focused output designs for selection and
  presentation contracts; and
- each command's producer and query designs for the meaning of the facts it
  exposes.

This document classifies changes to those owner-issued contracts. It does not
redefine them.

## Versioning baseline

[Semantic Versioning 2.0](https://semver.org/spec/v2.0.0.html) says that a
`0.y.z` public API is in initial development and may change at any time.
`dotnet-inspect` deliberately applies a stronger rule while its
`VersionPrefix` remains below `1.0`: published CLI changes must still be
classified, evidenced, and disclosed. Scripts, embedded product skills, and
copied README invocations are real consumers even when SemVer does not require
a major-version transition.

That stronger process is not a promise that every pre-`1.0` spelling or output
shape remains unchanged until a major release. An intentional breaking change
may ship before `1.0` when its owner approves the new contract, the release
notes label the break, affected product skills and examples move together, and
the PR supplies migration evidence.

Once the tool declares a `1.0` public API, release versioning must satisfy
SemVer's normal compatibility rules in addition to this policy.

## Publication boundary

A surface is **published** when at least one of these is true:

- a command, argument, option, or alias appears in visible `--help`;
- a released root README documents the invocation as supported;
- an embedded product skill instructs callers to use the invocation; or
- an owning design explicitly declares a machine-readable or selector
  contract.

Publication creates an obligation to classify and manage change. It does not
make every byte of output immutable.

The following do not publish a surface by themselves:

- an implementation type, method, parser token, or test fixture;
- a hidden option used only to compose the implicit router or another command;
- a one-off issue, PR, or release-demo invocation; or
- a test that pins behavior no owner has declared as a contract.

A hidden spelling can still be published when it is explicitly retained as a
compatibility alias, deprecation shim, migration diagnostic, or routing
reservation. Hiding a previously published spelling from help does not erase
its history.

The root README and product skills are publication evidence and versioned
consumers, not owners of producer semantics or structured-output schemas.

## Compatibility classes

Compatibility is assessed at the observable boundary a caller uses, not at the
convenience of the implementation.

| Surface | Compatibility obligation |
| ------- | ------------------------- |
| Invocation | A supported token sequence continues to select the same operation and interpret its values the same way. |
| Outcome | Success versus failure, stdout versus stderr, and whether a result is produced remain stable unless the owning contract intentionally changes them. Exact diagnostic prose and numeric non-zero codes are evolvable unless explicitly owned. |
| Typed JSON | Field names, JSON kinds, nullability, envelopes, discriminator values, and meanings follow the command's typed-output owner. A new field is not automatically compatible with strict consumers. |
| Lowered JSON, JSONL, TSV, and tables | Shape and representability follow their focused output owners. Parseability alone does not prove compatibility. |
| Human Markdown and plaintext | Wording, wrapping, spacing, and layout may evolve. Owner-issued section names, selectors, or column identities remain contracts when their designs say so. |
| Help and diagnostics | Descriptions and ordering may evolve. Published spellings, replacement guidance, channels, and success or failure class follow this policy and the command owner. |

Preserving compatibility does not require preserving a bug, an unsafe
interpretation, or a success-shaped failure. A correction can be intentionally
breaking; it must be named as such rather than described as compatible because
the new behavior is preferable.

## Change classifications

Classification and transition state are separate decisions. A change can use a
compatibility alias without being deprecated, or use a terminal deprecation
while making an intentional break.

| Classification | Definition | Disclosure and evidence |
| -------------- | ---------- | ----------------------- |
| Compatible | Every previously published invocation and owner-issued outcome contract remains valid. Additions have cleared routing, binding, default, vocabulary, and strict-consumer collisions. | Gate the old neighboring case and the new case. A **Breaking** release-note label is not used. |
| Migration-preserving | The canonical surface changes, but each old published invocation remains recognized and operational through a compatibility alias or forwarding shim with the same operation and owner-issued result meaning. New guidance or canonical output may identify the replacement. | Release notes name the replacement. Gate old and new invocations, equivalence of the owned result, guidance channel when present, and unchanged success class. |
| Corrective but breaking | A bug, unsafe interpretation, success-shaped failure, or false result is corrected by changing a previously observable contract. | Use a **Breaking** release-note entry that explains the correction and migration. Gate the former pathological case to the corrected result, channel, and exit class. |
| Intentionally breaking | A command, spelling, default, operation, or output contract is removed or redesigned for reasons other than correcting false behavior. | Use a **Breaking** release-note entry and an explicit migration. Deprecate first unless the PR justifies direct removal. Gate the replacement and the old input's final migration or removal behavior. |

A terminal deprecation is corrective or intentionally breaking because the old
invocation no longer succeeds. Recognition and guidance make the break
actionable; they do not make it compatible.

## Additive changes are not automatically compatible

A new command, alias, option, output field, or accepted spelling is only
additive after its effect on existing inputs has been checked.

The required pathological cases are:

- **Implicit target routing:** command names are reserved before a bare token
  can enter the platform-preferred, NuGet-fallback router. Adding a command or
  alias can therefore change an existing invocation from "resolve this
  target" to "run this command."
- **Parser binding:** an option, optional value, alias, or more-lenient parse
  can consume a token that previously belonged to an argument, another option,
  or the `--` literal region.
- **Defaults and authorization:** a new default can change work, network
  access, source acquisition, cost, output volume, or failure behavior for an
  unchanged invocation.
- **Strict machine consumers:** adding a JSON property, enum value, row kind,
  or discriminator can break exhaustive or unknown-member-rejecting consumers.
- **Vocabulary collisions:** a new section, column, field, or selector alias
  can make a formerly unique abbreviation or resolution path ambiguous.

The PR for an apparently additive change must demonstrate the old colliding or
neighboring invocation, not only the new happy path.

## Compatibility and transition states

These labels describe different obligations and must not be inferred from
`Hidden = true`.

| State | Meaning |
| ----- | ------- |
| Published syntax | Current supported syntax exposed through help, README, a product skill, or an explicit owner. |
| Ordinary alias | A co-equal supported spelling with no announced removal. Visible or hidden presentation does not change that status. |
| Compatibility alias | An older spelling retained to perform the same operation while canonical docs and output use the replacement. It has no removal date unless separately deprecated. |
| Deprecated forwarding shim | The old spelling still performs the supported replacement behavior and emits actionable migration guidance. |
| Deprecated terminal shim | The old spelling is recognized but cannot safely preserve its old operation. It emits actionable guidance and fails non-zero. |
| Removed with guidance | The old spelling no longer parses as supported syntax, but a focused pre-parse or parse diagnostic names the replacement. |
| Removed and reserved | The operation is gone, but its command token remains reserved so implicit routing cannot silently reinterpret it as a package or another target. |
| Internal hidden input | Parser or router composition state that was never published. It can change without deprecation, subject to its owning internal tests. |

A compatibility alias is a preservation mechanism, not a deprecation
announcement. If its removal is intended, the owner must either transition it
to an explicit deprecation or approve an intentional breaking removal.

## Deprecation requirements

A deprecation must:

1. Name the canonical replacement for each accepted old operation, give a
   deterministic choice rule when several replacements divide the old
   surface, or state plainly that no equivalent operation remains.
2. Choose forwarding or terminal behavior based on whether continuing the old
   operation would be truthful and safe.
3. Keep migration guidance on stderr and make terminal shims fail non-zero.
4. Remove the deprecated spelling from current README examples and product
   skill instructions while retaining a release-note migration.
5. Gate recognition, replacement guidance, channel, and success or failure
   class. A parse-only test does not prove an execution-time deprecation.
6. Decide separately whether a removed command token must remain reserved
   against implicit routing.

There is no universal two-minor or time-based removal period. Before removal,
the PR must show that the replacement has shipped and is documented, current
product skills no longer generate the old syntax, the routing-reservation
decision is explicit, and the benefit of removal justifies the remaining
consumer cost. A direct breaking removal follows the same evidence except that
the release notes must say that no deprecation period was provided and why.

## Change procedure

For every published-surface change:

1. Name the focused owner whose contract changes and the exact published
   surface affected.
2. Classify the change as compatible, migration-preserving, corrective but
   breaking, or intentionally breaking.
3. Exercise the previous invocation and the proposed invocation through the
   product entry point, including stdout, stderr, and exit class.
4. Check implicit-router reservations, aliases, optional-value binding, and
   neighboring abbreviations before calling an addition compatible.
5. Diff each affected machine contract through its owning schema or result
   model; generic JSON validity is not sufficient evidence.
6. Update visible help, the root README, embedded product skills, release notes,
   and focused designs that actually consume the changed surface.
7. Add the smallest owner-aligned gate that proves the claimed compatibility
   or migration behavior.

The release notes use an explicit **Breaking** label for intentionally
incompatible changes. Silent breakage is never an acceptable substitute for
classification.

## Current implementation status

Compatibility mechanisms are currently distributed rather than registered in
one manifest:

- hidden `api` is the active explicit deprecation. It accepts the old command
  shape, writes replacements for `type` and `member` to stderr, and returns
  non-zero without performing the old operation;
- `--authored-source` and the `Original Source` selector are examples of hidden
  compatibility aliases whose canonical spelling is now `--pdb-source` and
  `PDB Source`;
- valued `--head N` and `--tail N` spellings are removed with pre-parse
  replacement guidance, while removed `package --readme` is diagnosed at the
  package parse boundary; and
- removed command names including `audit`, `source`, `list`, and `ls` remain
  reserved rather than re-entering implicit target resolution.

The `api` terminal shim predates this policy. Its diagnostic gives a task-based
choice between `type` and `member`, but no release-note migration entry was
found and its execution behavior lacks the required gate. It is recorded as
nonconforming transition debt and must satisfy those requirements before the
shim is materially changed or removed.

Existing gates prove parts of those transitions:

- `CommandLineTests.ApiCommand_Deprecated_ParsesCorrectly` proves only that the
  hidden `api` command still parses. Its stderr text and non-zero execution
  outcome are currently unverified.
- `DiffOptionsParserTests.PdbSourceOption_AndLegacyAlias_EnablePdbSource` and
  command execution tests prove selected legacy aliases reach canonical
  behavior and output.
- `CommandExecutionTests.ValuedTailFlag_IsReportedAsAMigration_NotBoundAsAPositional`
  and `Package_RemovedReadmeFlag_PointsAtItsReplacement` prove focused removed
  syntax produces actionable non-zero diagnostics.
- `JsonWireNameGateTests` proves generated serializer contexts follow the
  configured wire-name policy. It does not prove per-command field sets,
  types, optionality, or semantic compatibility.

There is no automated census that reconciles visible help, README syntax,
embedded product skills, compatibility aliases, deprecations, removed
spellings, and router reservations. Complete publication and deprecation
coverage is therefore **unverified**. New changes must supply focused evidence
rather than claiming a global compatibility gate.

## Non-claims

This design does not:

- freeze the current command inventory, defaults, or output bytes;
- promise that pre-`1.0` breaking changes require a major version;
- define producer facts, section semantics, JSON schemas, or presentation
  layouts owned by focused designs;
- make all hidden parser inputs supported syntax;
- require compatibility with undocumented implementation accidents; or
- replace explicit security, failure-visibility, or correctness fixes with
  indefinite legacy behavior.
