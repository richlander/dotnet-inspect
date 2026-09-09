# CLI change classification and obsolete inputs

The **CLI change-classification design** is the normative owner for classifying
an observable `dotnet-inspect` command-line change as compatible, corrective
but breaking, or intentionally breaking. It also owns CLI change disclosure
and the narrow conditions under which an obsolete input remains recognized,
diagnosed, or reserved.

[Development practices](../development-practices.md#prefer-current-agent-guidance-over-cli-compatibility)
is the normative owner for the agent-first compatibility policy: current
product guidance moves with the tool, and obsolete CLI paths are not retained
solely for historical callers. This design applies that decision; it does not
restate or replace it.

It does not enumerate current commands or define each command's semantics.
Those responsibilities remain with:

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
useful in today's interface under development practices. Historical acceptance
does not determine its state in this design.

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
| Intentionally breaking | A command, spelling, default, operation, or output contract is removed, redesigned, or expanded in a way that invalidates a supported consumer for reasons other than correcting false behavior. | Use a **Breaking** release-note entry and update all current guides. Gate the replacement and any old input whose silent reinterpretation remains possible. |

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

These states describe the mechanics that result after development practices
decides the current surface. They must not be inferred from `Hidden = true`.

| State | Meaning |
| ----- | ------- |
| Published syntax | Current supported syntax exposed through help, README, a product skill, or an explicit owner. |
| Ordinary alias | A co-equal spelling whose owner has justified independent utility in today's interface under development practices. |
| Compatibility-only alias or shim | A former spelling retained only so an earlier invocation continues to work. Development practices governs its disposition. |
| Focused invalid-input guard | A rejected token sequence is recognized because ordinary current use could otherwise bind or route to a different operation. It emits a bounded diagnostic and fails non-zero. |
| Removed and reserved | The old operation is gone, but its command token remains reserved because releasing it would silently reinterpret the input through current implicit routing. |
| Internal hidden input | Parser or router composition state that was never published. It can change without CLI migration treatment, subject to its owning internal tests. |

## Obsolete-input mechanics

Development practices owns whether a former spelling is removed or justified
as useful in the current interface. Once that decision is made, this design
owns only the command-line boundary mechanics:

1. Classify and disclose the observable change.
2. Add a focused invalid-input guard only when the removed tokens could
   otherwise bind or route to a different current operation. Gate its stderr
   channel and non-zero exit.
3. Reserve a removed command token only when releasing it would create a
   silent implicit-routing reinterpretation. Gate the routing behavior.
4. Otherwise, let the ordinary unrecognized-input result report the removed
   spelling.

This design defines no retention rule or transition duration.

## Change procedure

For every current published-surface change:

1. Name the focused owner whose contract changes and the exact observable
   surface affected.
2. Classify the change as compatible, corrective but breaking, or
   intentionally breaking.
3. Apply the current-guidance update required by development practices.
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

- `--authored-source` and the `Original Source` selector are hidden
  compatibility-only aliases for `--pdb-source` and `PDB Source`. No
  independent current-interface rationale is recorded, so their disposition
  under development practices is unresolved;
- `SelectResolver.LegacySectionAliases` contains a broader set of former
  section spellings. Their entry-by-entry current utility and removal status
  have not been classified, so that inventory is **unverified** under this
  policy;
- visible library `--references` and `--dependencies`, plus package
  `--dependencies`, identify themselves as legacy aliases. Their independent
  current utility and removal status are likewise **unverified**;
- unadopted valued `--head N` and `--tail N` inputs have a focused pre-parse guard because
  the current boolean option would otherwise leave the count to bind as a
  positional target. The `--tail N` outcome is gated; the symmetric `--head N`
  outcome is implemented but **unverified**. Adopted presence-only row
  modifiers use [common option-value validation](cli-option-value-validation.md)
  and its zero-arity diagnostic instead;
- removed `package --readme` receives replacement guidance at the package parse
  boundary. No independent current-input ambiguity is recorded, so the special
  diagnostic's current-policy justification is **unverified**; and
- removed top-level command names `api`, `audit`, and `source` remain reserved
  because releasing them would send the same bare tokens through implicit
  target resolution. The `api` and `source` outcomes are gated; the `audit`
  product-entry reservation outcome is **unverified**. `list` and `ls` are also
  reserved, but no independent current-interface rationale for those bare
  tokens is recorded, so their reservation is **unverified** under this
  policy.

Existing gates prove parts of those behaviors:

- `CommandLineTests.ApiCommand_IsRemovedButReserved` proves that `api` is
  unregistered and remains reserved.
- `CommandExecutionTests.ApiCommand_RemovedFromRoot` proves the removed `api`
  token fails as a command rather than entering implicit target routing.
- `DiffOptionsParserTests.PdbSourceOption_AndLegacyAlias_EnablePdbSource` and
  command execution tests prove selected compatibility-only aliases still
  reach canonical behavior.
- `CommandExecutionTests.ValuedTailFlag_IsReportedAsAMigration_NotBoundAsAPositional`
  proves the `--tail N` parser-rebinding guard. It does not gate `--head N`.
- `CommandExecutionTests.Package_RemovedReadmeFlag_PointsAtItsReplacement`
  proves the package diagnostic behavior, not its independent current-product
  rationale.
- `CommandExecutionTests.SourceCommand_RemovedFromRoot` proves the removed
  `source` token fails as a command rather than entering implicit target
  routing. `CommandLineTests.AuditCommand_IsNoLongerRegistered` proves only
  parser registration, not the `audit` product-entry reservation outcome.
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
- define producer facts, section semantics, JSON schemas, or presentation
  layouts owned by focused designs;
- make all hidden parser inputs supported syntax;
- require compatibility with undocumented implementation accidents; or
- permit stale product skills or README examples because an old parser path
  still works.
