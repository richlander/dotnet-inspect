# Decompiler PR reference

The [decompiler PR template](decompiler-pr.md) is one page. This reference
holds the evidence lenses, the structural-review mechanics, and the corpus
tables a PR cites only when they apply. Copy a section into the PR when the
change needs it; otherwise cite nothing. Every section keeps the rules the
template used to carry inline.

## Evidence scoping

Every evidence lens names its scope: one method, one fixture assembly, a
fixed corpus population, a source build, or a baseline manifest. When two
lenses inspect the same logical method through different physical artifacts
(a no-PDB and a PDB-bearing build), say so. Do not make the reader infer
whether examples are the same method, neighboring methods, or different
fixtures.

Roll every lens up to **Better**, **Same**, **Worse**, or **Not directional**
before presenting raw output, and label its kind: product quality, evidence
coverage, or population accounting. Structural correspondence explains what
changed and how much was comparable; validity, correctness, and fidelity
decide product quality. `Unsupported` and `Ambiguous` are unordered coverage
reasons, not quality levels.

Directional product A/B comparisons need the same population and sampling
identity: hold input bytes, working directory, normalized paths, method caps,
and sampling policy constant. A build-contract lens may compare different
physical artifacts only when it names what changed, what stayed fixed, and
the contract that decides direction. Report fixture membership or sampling
changes as Not directional population accounting, never as product movement.

Glossary: **IL fidelity** judges whether the rendered C# recompiles to the
original contract body; **fully raised** judges whether that faithful
rendering has reached the preferred source idiom. A `Full`-fidelity render
can still be short of the fully raised endpoint.

## Verdicts under Before and After

Record each verdict beside the code it judges, and add prose only to explain
one:

- Valid: does it compile and bind?
- Correct: does it preserve the original observable behavior?
- Printer exact (when the method is enrolled in a whole-file source oracle):
  does the rendered body match the checksum-pinned authored body?
- IL fidelity: does it recompile to the original opcodes, or is it not
  currently checkable? Judged by the compile-back harness or the `diff`
  command, never by `member`; a render can be Valid and Correct yet not
  opcode-faithful.
- Taste applied: the configurable, opcode-neutral style choices from
  `-S "Applied Taste"`; a byte-divergent lens there is why IL fidelity can be
  False while Valid and Correct are True.
- Commit: the digest the render was acquired at.

Acquire every block from `dotnet-inspect` rather than transcribing: Before
with `-S "Decompiled Source"` at the base commit, After at the head, Applied
Taste at the same commits, and the PDB Source → After lens with
`-S "Source Diff"` (`-v:d` for the full diff) at the head. Only the fully
raised block is authored by hand.

## Evidence map and shared judgment

| Lens | Scope | Compared | Held constant | Answers |
| --- | --- | --- | --- | --- |
| Method validity | **One method**: `{Type::Method}` | Base product → head product | One pinned assembly | Did the rendered C# become more or less legal? |
| Method correctness | **The same method** | Base product → head product | The method's observable behavior contract | Did behavior preservation improve or regress? |
| Method IL fidelity | **The same method** | Base product → head product | The original opcode contract | Did compile-back fidelity improve or regress? |
| Structural review | **One method** | Base product → head product | One pinned assembly | What structure changed? |
| PDB Source → After | **The same logical source method**, through a PDB-bearing build | Authored source → head product | Source document and checksum | How does the head render differ from source? |
| Render A/B | **One fixture or corpus**: {count and identity} | Base product → head product | Input bytes, working directory, paths, sampling policy | Which rendered methods changed? |
| Same-population quality | **Fixed corpus**: {assemblies and method count} | Base product → head product | The same binaries and sampling identity | Did product quality improve or regress? |
| Fixture activation | **Fixture builds**: {count and identity} | Base-built inputs → head-built inputs | One harness and explicit activation contract | Does the fixture exercise its compiler feature? |
| Population accounting | **Fixture manifests** | Baseline manifest → head manifest | Not applicable | Which coverage movement is accepted? |

| Lens | Evidence kind | Direction | What decides the judgment |
| --- | --- | --- | --- |
| Method validity | Product quality | Better/Same/Worse | The legality rule |
| Method correctness | Product quality | Better/Same/Worse | The observable-behavior rule |
| Method IL fidelity | Product quality | Better/Same/Worse | The compile-back rule |
| Structural review | Evidence coverage | Better/Same/Worse | More supported correspondence is better; gap reasons are unordered |
| PDB Source → After | Product quality | Not directional | Textual similarity decides nothing about validity, correctness, or fidelity |
| Render A/B | Product quality | Better/Same/Worse | Classify every changed method; net gains do not offset losses |
| Same-population quality | Product quality | Better/Same/Worse | Each metric's explicit higher- or lower-is-better goal |
| Fixture activation | Evidence coverage | Better/Same/Worse | Fulfillment of the explicit feature-activation contract |
| Population accounting | Population accounting | Not directional | Coverage movement, reported apart from fixed-population quality |

## Correctness-fix contract

For an invalid-`Full` or output-correctness fix, replace the proof-obligation
table with this one and keep the structural review whenever the rendered body
changes.

| Obligation | Contract |
| --- | --- |
| False claim | {invalid `Full`, incorrect behavior, or other false product claim} |
| Root cause | {why the product produced the defective output} |
| Fix shape | {narrow code, predicate, ownership, or rendering change} |
| Scope boundary | {nearby shapes or sibling defects intentionally unchanged} |
| Falsifier | {evidence that would make the fix incorrect or incomplete} |

## Two-lens review

Acquire both exact revisions as root JSON documents and run the structural
review; then acquire the independent SourceLink-backed lens from the head:

```bash
# At the exact base revision
dotnet-inspect member {Type} {MethodSelector} {scope} \
  -S "Annotated Source Document" --json > /tmp/before.json

# At the exact head revision
dotnet-inspect member {Type} {MethodSelector} {scope} \
  -S "Annotated Source Document" --json > /tmp/after.json

dotnet run --project tools/DecompilerHarness -c Release -- \
  --structural-review /tmp/before.json /tmp/after.json

dotnet-inspect member {Type} {MethodSelector} {scope} \
  -S "Source Diff" -v:d > /tmp/source-diff.txt
```

The structural artifact derives from one product-issued
`CSharpStructuralDiffDocument` bound to physical-method and IL-origin
provenance; never place carets or reconstruct rows by hand. Node ids are
document-local, and equal ids, coordinates, text, labels, or display order
never establish correspondence. Fidelity and retained IL notes are independent
evidence. Running the acquisition and the command is required for every
output-changing PR; do not substitute a hand-written diff or report an
unavailable result without attempting it.

When the claimed change has supported correspondence, paste the generated
output verbatim, gap warning included. When it appears only among unsupported
or ambiguous gaps, or acquisition fails, provenance is missing, or the
physical method identities differ, write
`Attempted — unavailable for the claimed change: {exact result}` and retain
the standalone Before and After bodies. This presentation boundary does not
change the raise verdict.

The PDB Source → After lens is part of the required attempt; record it
beside the structural review as Not directional (retain a generated
unavailable result rather than deleting the lens): its checksum proves agreement with the Portable PDB declaration,
not independent build provenance, and compile-back status stays an
independent oracle. When no matching C# is available, retain the generated
unavailable result and treat raw IL as the compiled-body evidence.

## Evidence table

Report Baseline (base commit) beside Head (this PR) for every check with a
pass/fail or count outcome; a Head-only pass cannot show whether a failure is
pre-existing or new.

| Check | Baseline | Head |
| --- | --- | --- |
| Product build | Pass | Pass |
| Focused tests | `{class}`: {n} passed | `{class}`: {n} passed |
| Compiler-produced positive | `{fixture}` remains flat | `{fixture}` raises |
| Adversarial declines | `{negative fixtures}` remain flat | `{negative fixtures}` remain flat |
| Decompiler fast suite | {total}, {failed} failed | {total}, {failed} failed |
| Reduced fixture validity | Pass / not applicable | Pass / not applicable |
| Reduced fixture fidelity | Pass / not currently checkable | Pass / not currently checkable |
| Structural review | Base document acquired | Generated comparison attached / limit named |
| Real witness | `{Type::Method}` broken / not applicable | `{Type::Method}` fixed / not applicable |

Name any Baseline failures and confirm they are unchanged by the PR. For
render A/B or corpus deltas, list stable changed-method identities and
classify every loss and gain.

## Corpus validity

Required for invalid-`Full` fixes and any change that can alter output
legality. Compare the same input population at Baseline and Head and classify
every changed validity row. Use a real corpus witness when one exists;
otherwise say why the compiler-produced or synthetic fixture is the
authoritative reproducer and name the focused census or gate that bounds the
affected population.

| Metric | Baseline | Head |
| --- | ---: | ---: |
| Full malformed (-) | {count} | {count} |
| Valid to invalid (-) | - | {count} |
| Invalid to valid (+) | - | {count} |
| Invalid to invalid, changed | - | {count} |

List the changed validity rows (direction, method, diagnostic or bucket,
baseline, head), capped at 24; the full local delta follows
[Reproducing decompiler corpus deltas](../decompiler-corpus-delta-repro.md).

## Decompiler quality tables

**Same-population quality judgment.** One fixed method population and
sampling identity; keep Goal and Direction explicit.

| Metric | Goal | Baseline | Head | Direction |
| --- | --- | ---: | ---: | --- |
| Full malformed | Lower | {count} | {count} | Better/Same/Worse |
| Correctness defects | Lower | {count} | {count} | Better/Same/Worse |
| Fidelity exact | Higher | {count} | {count} | Better/Same/Worse |
| Fidelity opcode diffs | Lower | {count} | {count} | Better/Same/Worse |
| Detected lowering residue | Lower | {count/rate} | {count/rate} | Better/Same/Worse |
| Forward-merge stops | Lower | {count/rate} | {count/rate} | Better/Same/Worse |
| Fully raised | Higher | {count/rate} | {count/rate} | Better/Same/Worse |
| Pass bugs | Lower | {count} | {count} | Better/Same/Worse |

**PR quick gate.** The PR quick corpus, hash-stable 100 methods per assembly
over the same population: detected lowering residue, conditional-branch
residue, pass bugs, and fully raised, each with Baseline, PR, and Direction,
closed by a one-line PASS/FAIL verdict.

**Aggregate context.** The full corpus (assemblies, methods, baseline drift)
with the same four metrics and a PASS/ADVISORY/BLOCKED verdict.

**Population accounting.** Only when fixture or corpus membership changes,
and always Not directional: assembly or population, Baseline, Head, and the
classification (accepted growth, accepted removal, unchanged), with a total
line labeled coverage movement. Never reuse its totals as Baseline and Head
values in the quality tables.

## Review comment

Before marking the PR ready, post a comment naming each reviewer and model,
the exact head reviewed, findings with their resolution commits or explicit
non-actions, and each reviewer's final verdict. Append
[the decompiler adversarial-review appendix](decompiler-adversarial-review-appendix.md)
after the canonical review prompt and the completed candidate frame so the
reviewer checks that the harness observed rather than repaired the evidence.
