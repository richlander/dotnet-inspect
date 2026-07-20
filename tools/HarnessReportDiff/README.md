# Harness report diff

`HarnessReportDiff` compares two structured harness measurements and renders a
goal-aware before/after card with Markout. Measurement remains with the owning
harness; this tool reads stored artifacts, validates comparability, derives
metric direction, and presents the change.

Existing DecompilerHarness corpus snapshots are accepted directly:

```bash
dotnet run --project tools/HarnessReportDiff -c Release -- \
  /tmp/before.json /tmp/after.json
```

Use `--format tsv` or `--format jsonl` for structured rows. Add
`--fail-on-regression` to exit 1 when a comparable metric moves opposite its
declared goal. Invalid input or incompatible report kinds/schemas exit 2.

The default Markdown card uses `Metric (goal)` labels:

- `(+)` means higher is better;
- `(−)` means lower is better;
- `(=)` means the value should remain fixed;
- `(context)` means movement is informational.

Counts from different sampled method populations are marked `Incomparable`
even when the sample sizes happen to match. Corpus snapshots supply their
per-method identities for this check.

## Fully raised V1

The initial endpoint signal is intentionally mechanical:

```text
Fully raised = the complete measurement reports zero decompiler residue
```

The card identifies this as `zero decompiler residue (V1 signal)`. Missing or
incomplete residue evidence produces `Not established`, never a positive
claim. A later schema can carry checksum-verified original-source
correspondence as a stronger basis without changing the before/after card.

## Native structured report

The reader also accepts the generic JSON form represented by
`StructuredHarnessReport`. It contains:

- `schemaVersion`, `kind`, and `description`;
- a population identity;
- metrics with stable IDs, labels, goals, typed counts, and metric-population
  identities;
- optional residue evidence.

Metric IDs perform correlation; labels are presentation. Test-kind vocabulary
stays in the metric IDs and labels rather than being reduced to a universal
pass/fail result.
