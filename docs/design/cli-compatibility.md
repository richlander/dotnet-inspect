# CLI compatibility and deprecation

The **CLI compatibility policy** is the normative owner for classifying an
observable `dotnet-inspect` command-line change as compatible, corrective but
breaking, or intentionally breaking. It also owns CLI change disclosure and
the narrow conditions under which an obsolete input remains recognized,
diagnosed, or reserved.

It does not enumerate current commands or define each command's semantics.
Those responsibilities remain with:

- [Development practices](../development-practices.md), which owns the
  repository-wide preference for current agent guidance, simple current
  command shapes, and low carrying cost over retaining obsolete CLI syntax;
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
`dotnet-inspect` applies a stronger process while its `VersionPrefix` remains
below `1.0`: published changes must be classified, current guidance must move
with the product, breaking changes must be disclosed, and the claimed current
behavior must be evidenced.

That stronger rule is about truthful current releases, not preserving
yesterday's CLI. A command, flag, default, or workflow may change before `1.0`
without a compatibility alias or grace period. The release remains coherent
when visible help, the root README, embedded product skills, release notes, and
focused designs describe the same current behavior.

Explicitly owned serialized formats, protocols, library APIs, and other
non-CLI compatibility contracts retain their own versioning rules. Once the
tool declares a `1.0` public API, release versioning must satisfy SemVer's
normal compatibility rules in addition to this policy.

## Current guidance is the CLI compatibility layer

Embedded product skills are the authoritative current agent guides. The root
README is the current human and general-purpose guide. A change to command
syntax, defaults, workflows, or output shapes they teach must update every
affected guide in the same PR and rerun the affected examples against the
changed tool.

An older skill or README invocation is evidence that a release consumer may
need a migration note. It is not a reason to retain the old parser path. A
stale shipped skill is a compatibility failure even when the old invocation
still happens to work.

## Publication boundary

A surface is **published for the current release** when at least one of these
is true:

- a command, argument, option, or alias appears in visible `--help`;
- the current root README documents the invocation as supported;
- a current embedded product skill instructs callers to use the invocation; or
- an owning design explicitly declares a machine-readable or selector
  contract.

Publication creates an obligation to classify and disclose change. It does not
create a requirement to retain obsolete CLI syntax.

The following do not publish a surface by themselves:

- an implementation type, method, parser token, or test fixture;
- a hidden option used only to compose the implicit router or another command;
- a one-off issue, PR, or release-demo invocation; or
- a test that pins behavior no owner has declared as a contract.

A hidden input is current supported syntax only when its owner justifies it as
useful in today's interface. Hidden inputs retained solely because an older
release accepted them are compatibility debt, not precedent.

The README and product skills are publication evidence and versioned
consumers, not owners of producer semantics or structured-output schemas.

## Observable surfaces

Compatibility is assessed at the observable boundary a caller uses, not at the
convenience of the implementation.

| Surface | Change boundary |
| ------- | --------------- |
| Invocation | Command, argument, option, and alias binding; implicit routing; value interpretation; defaults; and authorization. |
| Outcome | Success versus failure, stdout versus stderr, and whether a result is produced. Exact diagnostic prose and numeric non-zero codes are evolvable unless explicitly owned. |
| Typed JSON | Field names, JSON kinds, nullability, envelopes, discriminator values, and meanings follow the command's typed-output owner. A new field is not automatically compatible with strict consumers. |
| Lowered JSON, JSONL, TSV, and tables | Shape and representability follow their focused output owners. Parseability alone does not prove compatibility. |
| Human Markdown and plaintext | Wording, wrapping, spacing, and layout may evolve. Owner-issued section names, selectors, or column identities remain contracts when their designs say so. |
| Help and diagnostics | Descriptions and ordering may evolve. Current spellings, channels, replacement guidance, and success or failure class follow this policy and the command owner. |

Preserving compatibility does not require preserving a bug, unsafe
interpretation, success-shaped failure, or obsolete command shape.

## Change classifications

The classifications are mutually exclusive:

| Classification | Definition | Disclosure and evidence |
| -------------- | ---------- | ----------------------- |
| Compatible | Every current published invocation and owner-issued outcome contract remains valid. Additions have cleared routing, binding, default, vocabulary, and strict-consumer collisions. | Gate the old neighboring case and the new case. A **Breaking** release-note label is not used. |
| Corrective but breaking | A bug, unsafe interpretation, success-shaped failure, or false result is corrected by changing a previously observable contract. | Use a **Breaking** release-note entry that explains the correction and current replacement. Gate the former pathological case to the corrected result, channel, and exit class. |
| Intentionally breaking | A command, spelling, default, operation, or output contract is removed or redesigned for reasons other than correcting false behavior. | Use a **Breaking** release-note entry and update all current guides. Gate the replacement and any old input whose silent reinterpretation remains possible. |

A runtime transition aid does not change the classification. An intentionally
breaking removal remains breaking when the old token is reserved or rejected
with a focused diagnostic.

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

## Current-input states

These states must not be inferred from `Hidden = true`.

| State | Meaning |
| ----- | ------- |
| Published syntax | Current supported syntax exposed through help, README, a product skill, or an explicit owner. |
| Ordinary alias | A co-equal spelling with independent utility in today's interface. It is supported because it improves the current surface, not because an older release had it. |
| Compatibility-only alias or shim | An old spelling retained only so yesterday's invocation continues to work. This is nonconforming debt and a removal candidate. |
| Focused invalid-input guard | A rejected token sequence is recognized because ordinary current use could otherwise bind or route to a different operation. It emits a bounded diagnostic and fails non-zero. |
| Removed and reserved | The old operation is gone, but its command token remains reserved because releasing it would silently reinterpret the input through current implicit routing. |
| Internal hidden input | Parser or router composition state that was never published. It can change without CLI migration treatment, subject to its owning internal tests. |

An owner can keep a former spelling only by justifying it as an ordinary alias
that is useful now. Historical acceptance alone is insufficient.

## Deprecation and removal

Deprecation is release disclosure that a current spelling or behavior is being
replaced. It does not imply a runtime grace period.

When the best current command shape changes:

1. Remove the obsolete spelling rather than add or retain an alias, shim, dual
   parser, or warning solely for compatibility.
2. Update visible help, the root README, and every affected product skill in
   the same change.
3. Add a **Breaking** release-note entry that names the current replacement or
   says plainly that no equivalent operation remains.
4. Add a focused invalid-input guard only when the unrecognized tokens could
   otherwise bind or route to a different current operation. Gate its stderr
   channel and non-zero exit.
5. Reserve a removed command token only when releasing it would create a
   silent implicit-routing reinterpretation. Gate the routing behavior.
6. Remove compatibility-only paths encountered in the changed area unless
   their owner establishes independent current utility.

There is no universal two-minor, time-based, or deprecation-first period. A
longer transition requires an explicit current-product rationale from the
owning design; consumer age by itself is not sufficient.

## Change procedure

For every current published-surface change:

1. Name the focused owner whose contract changes and the exact observable
   surface affected.
2. Classify the change as compatible, corrective but breaking, or
   intentionally breaking.
3. Update current help, README, product skills, and examples rather than
   preserving stale invocations.
4. Exercise the proposed invocation through the product entry point, including
   stdout, stderr, and exit class.
5. Check implicit-router reservations, aliases, optional-value binding, and
   neighboring abbreviations before calling an addition compatible.
6. Exercise the old input when removal could cause silent rebinding or routing;
   otherwise a generic unrecognized-input result is sufficient.
7. Diff each affected machine contract through its owning schema or result
   model; generic JSON validity is not sufficient evidence.
8. Add the smallest owner-aligned gate that proves the claimed current
   behavior and release-note migration.

Silent breakage and stale current guidance are never acceptable substitutes
for classification.

## Current implementation status

Compatibility mechanisms are currently distributed rather than registered in
one manifest:

- hidden `api` is a terminal compatibility shim. It writes replacements for
  `type` and `member` to stderr and returns non-zero without performing the old
  operation. It predates this policy, has only a parse gate, and is a removal
  candidate; removal must decide whether `api` remains reserved;
- `--authored-source` and the `Original Source` selector are hidden
  compatibility-only aliases for `--pdb-source` and `PDB Source`. No
  independent current-interface rationale is recorded, so they are removal
  candidates rather than precedent;
- valued `--head N` and `--tail N` inputs have a focused pre-parse guard because
  the current boolean option would otherwise leave the count to bind as a
  positional target;
- removed `package --readme` receives replacement guidance at the package parse
  boundary. No independent current-input ambiguity is recorded, so the special
  diagnostic's current-policy justification is **unverified**; and
- removed command names including `audit`, `source`, `list`, and `ls` remain
  reserved because releasing them would send the same bare tokens through
  implicit target resolution.

Existing gates prove parts of those behaviors:

- `CommandLineTests.ApiCommand_Deprecated_ParsesCorrectly` proves only that the
  hidden `api` command still parses. Its stderr text and non-zero execution
  outcome are unverified.
- `DiffOptionsParserTests.PdbSourceOption_AndLegacyAlias_EnablePdbSource` and
  command execution tests prove selected compatibility-only aliases still
  reach canonical behavior.
- `CommandExecutionTests.ValuedTailFlag_IsReportedAsAMigration_NotBoundAsAPositional`
  proves the current parser-rebinding guard.
- `CommandExecutionTests.Package_RemovedReadmeFlag_PointsAtItsReplacement`
  proves the package diagnostic behavior, not its independent current-product
  rationale.
- `JsonWireNameGateTests` proves generated serializer contexts follow the
  configured wire-name policy. It does not prove per-command field sets,
  types, optionality, or semantic compatibility.

There is no automated census that reconciles visible help, README syntax,
embedded product skills, hidden aliases, invalid-input guards, removed
spellings, and router reservations. Complete current-surface coverage is
therefore **unverified**. New changes must supply focused evidence rather than
claiming a global compatibility gate.

## Non-claims

This design does not:

- preserve obsolete commands, flags, aliases, defaults, or output bytes;
- promise that pre-`1.0` breaking changes require a major version;
- define producer facts, section semantics, JSON schemas, or presentation
  layouts owned by focused designs;
- make all hidden parser inputs supported syntax;
- require compatibility with undocumented implementation accidents; or
- permit stale product skills or README examples because an old parser path
  still works.
