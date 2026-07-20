# Harness report diff

`HarnessReportDiff` compares two stored harness measurements and renders a
goal-aware before/after card with Markout. Measurement remains with the owning
harness; this tool reads `HarnessReportProtocol` artifacts, validates
comparability, derives metric direction, and presents the change.

Emit a report from a supported DecompilerHarness mode, then compare revisions:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- \
  --source-correspondence-census \
  --return-to-sender-fixtures rts.candidates \
  --emit-harness-report /tmp/source.before.json \
  --cap 100

dotnet run --project tools/HarnessReportDiff -c Release -- \
  /tmp/source.before.json /tmp/source.after.json
```

Existing DecompilerHarness corpus snapshots are also accepted directly.

Use `--format tsv` or `--format jsonl` for structured rows. Add
`--fail-on-regression` to exit 1 when a comparable metric moves opposite its
declared goal. Invalid input or incompatible report kinds/schemas exit 2.
TSV and JSONL use one flat row shape with a `section` discriminator; values
remain strings so a count delta and a count-plus-rate delta do not change type.

The default Markdown card uses `Metric (goal)` labels:

- `(+)` means higher is better;
- `(−)` means lower is better;
- `(=)` means the value should remain fixed;
- `(context)` means movement is informational.

Counts from different sampled method populations are marked `Incomparable`
even when the sample sizes happen to match. Corpus snapshots supply their
per-method identities for this check. Snapshots without method identities fall
back to the aggregate population identity and therefore cannot compare samples
across different aggregate populations.

## Fully raised V1

For decompiler corpus snapshots, the endpoint signal is intentionally
mechanical:

```text
Fully raised = the complete measurement reports zero decompiler residue
```

The card identifies this as `zero decompiler residue (V1 signal)`. Missing or
incomplete residue evidence produces `Not established`, never a positive claim.
Different aggregate populations produce `Incomparable`, and a comparable
residue increase participates in `--fail-on-regression`. Non-corpus reports do
not render this decompiler-specific endpoint.

## Stored report protocol

The shared JSON form is `StoredHarnessReport`. It contains:

- a descriptor with stable report ID and schema version;
- execution disposition, blockers, and artifacts;
- a comparison projection with description and population identity;
- metrics with stable IDs, labels, goals, typed counts, and
  metric-population identities;
- optional decompiler-residue evidence.

Metric IDs perform correlation; labels are presentation. Reports with different
kinds, schemas, or metric sets are rejected rather than partially correlated.
Test-kind vocabulary stays in metric IDs and labels rather than being reduced
to a universal pass/fail result.
