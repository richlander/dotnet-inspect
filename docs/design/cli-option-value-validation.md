# CLI option-value validation

## Owned claim

A zero-arity option does not accept a value. The CLI reports a supplied value
with one diagnostic shape, using the option's name:

```text
Error: --versions does not accept a value.
```

This owner defines that error classification, not commands' positional
cardinality or the meaning of their options. Command definitions retain those
contracts. Row normalization and selection remain owned by
[CLI row selection](cli-row-selection.md).

## Positional ownership

Valid positional arguments remain valid before or after flags. A separate
token is not an option value merely because it follows a zero-arity option.
An attached value, or a surplus positional token directly following that
option after the active command/view's positional capacity is exhausted,
receives the common diagnostic.

| Invocation fragment | Outcome |
| --- | --- |
| `package System.CommandLine --versions 2` | `--versions` does not accept a value |
| `package System.CommandLine --versions=2` | Same diagnostic |
| `package --versions System.CommandLine` | Valid package input |
| `package --versions 2` | Valid numeric package input |
| `package System.CommandLine --versions -n 2 --json` | Valid two-version request |

The user explicitly approved preserving valid positional arguments and
rejecting surplus values. The rule does not recognize former count syntax or
infer a token's role from numeric or Boolean-shaped text.

An unbounded positional declaration is not silently narrowed. A command/view
with a stricter existing capacity supplies that capacity; for example, the
plural package-version views accept one package. Other multi-package views
keep their existing positional inputs.

Required and optional option values retain their parsed ownership. Syntax
after `--` remains positional input, not an option occurrence or an attached
value. Active option identities and arity determine validation; names that
look like flags inside another option's value do not create occurrences.

## Invocation and diagnostics

The rule applies to every zero-arity option, including aliases, rather than
only the package-version selectors. Optional-valued Boolean options are not
converted into zero-arity options by this change.

Validation consumes the authoritative command parse, including supported
row shorthand normalization where applicable. It runs before command actions,
acquisition, disclosure output, or rendered-line writers. A violation uses
the existing `CommandError` boundary, exits nonzero, and emits no stdout.
Other argument and semantic failures retain their owning contracts.

## Consumer and scope

The consumer is the CLI invocation pipeline. Adoption has three steps in this
slice: retain parsed option/argument ownership, apply the common check with
command-owned positional capacity, and publish the result through the existing
CLI error boundary. The semantic-item work is tracked by #4677; issue #6173
is the focused follow-up to #5809.

This is host-specific argv validation, not a new inspection architecture,
acquisition mechanism, or shared algorithm requiring Browser/Wasm adoption.
It does not replace the command parser or the general implicit-routing work
in #5813. Existing command arity is the authority; a new arity framework or
per-option compatibility recognizer is not required.

## Compatibility

This is corrective but breaking under
[CLI change classification](cli-change-classification.md): attached Boolean
values that the parser previously accepted for declared zero-arity options
now fail. Omit the value; use `-n N` for version-row counts. Existing valid
positionals and optional-valued Boolean options are unchanged. No compatibility
recognizer is retained for the former parser behavior.

## Required gates

Release CLI invocation cases must cover the table above and these boundaries:

- Multiple zero-arity options and aliases use the same diagnostic shape.
- Attached numeric, Boolean, empty, and ordinary-text values are rejected.
- Valid positional inputs before/after flags, including numeric and
  Boolean-shaped package IDs, retain their ownership.
- Finite positional slots and existing single-package view limits distinguish
  surplus input from valid arguments; multi-package views remain supported.
- Required/optional values, the `--` marker, and supported row shorthand keep
  their existing ownership.
- Explicit and implicit package invocation share the result, including early
  query-discovery output; invalid input is rejected before an action runs.

`CliOptionValueValidationTests` gates the common invocation contract and its
synthetic finite/unbounded command neighbors. The local-feed
`SourceScopedRoutingTests.PackageVersionListing_ZeroArityValidationPreservesLocalRows`
gate exercises complete selected version rows with valid numeric and
Boolean-shaped package IDs. The package adoption's
`Versions_AdditionalPackageUsesMultiPackageValidation` and
`Versions_ValuedDirectionWithRangeUsesZeroArityDiagnostic` gates pin the
changed diagnostics; `Versions_QueryDiscoveryPreservesJsonFormatContract`
retains the existing format guard's precedence.
