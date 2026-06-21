# Reading IR Dumps

A usage guide for the decompiler's per-pass IR dump — the JitDump-style view
that shows *how* the pipeline raised IL into C#, one stage at a time. This is a
**reading** guide; for *why* the pipeline is shaped this way see
[decompiler.md](decompiler.md) and [decompiler-ir.md](decompiler-ir.md), and for
*how we know the output is right* see [decompiler-quality.md](decompiler-quality.md).

## When to reach for it

The decompiled C# (`Decompiled Source`) is a best-effort reconstruction. When it
looks wrong, surprising, or lower-fidelity than expected, the IR dump tells you
**which pass** is responsible. You do not need it for normal lookup — only when
diagnosing the decompiler itself.

Symptoms that warrant a dump:

- The raised C# is correct as IL but reads worse than it should (an idiom that
  *should* have been raised stayed flat — e.g. `T t = x as T; if (t != null)`
  instead of `x is T t`).
- The output contains an explicit unrepresentable marker or a `DEC####`
  diagnostic comment, or the fidelity level is below `Full`.
- The output is semantically wrong (rare, and the kind of bug the corpus oracle
  exists to catch) and you need to localize the defect to a single pass.

## The product surface (ships, no Roslyn)

Agents and end users get exactly one knob, surfaced as a progressively-disclosed
code section. It needs a single selected overload (`--index N` / `Name:N` /
`--params`), because it dumps one method body.

```bash
dnx dotnet-inspect -y -- member string -m IsNullOrEmpty:1 --dump-stages --raw
```

- `--dump-stages` is sugar for `-S "IR (Stages)"`. Both select the same
  `ExplicitOnly` section — it is never auto-rendered.
- `--raw` prints the bare dump with no headings or code fences, suitable for
  redirecting to a file or diffing two runs yourself.

That is the whole agent-facing surface. The recompile-and-compare *oracle* (the
fidelity/validity checks) needs Roslyn and therefore lives only in the
developer/CI harness — see [Contributor surface](#contributor-surface-the-harness)
and [decompiler-inspection-oracle.md](design/decompiler-inspection-oracle.md)
for the product-vs-tool boundary.

## How to read a dump

Each stage is a block framed by a `====` header, containing the typed IR tree at
that point, ending with a `// fidelity:` footer. The dump opens with the tree
**after import** (IL turned into typed nodes, nothing raised yet) and prints the
tree again **after every pass that ran**, terminating on the raised C# — which is
**byte-identical to the product's `Decompiled Source`**. The last stage you read
is exactly the artifact the quality gates grade; there is no drift between what
you inspect and what is measured.

A trimmed dump of `string.IsNullOrEmpty`:

```text
==== IR (typed tree after import) ====
Function bool IsNullOrEmpty(string value)
  BlockContainer
    Block IL_0000
      ConditionalBranch IL_000D
        LogicalNot
          LoadArgument 0 (string value)
    Block IL_0003
      Return
        Comparison.Equal
          CallVirt string.get_Length
            LoadArgument 0 (string value)
          Constant 0 (int)
    Block IL_000D
      Return
        Constant 1 (int)
// fidelity: Full

==== IR (after typed-constants) ====
... Constant 1 (int)  becomes  Constant True (bool) ...
// fidelity: Full

  ... one block per pass: identity-convert, redundant-branch-elimination,
      eh-structuring, expression-inlining, ... structuring, boolean-folding,
      is-pattern, ... (passes that change nothing still print, unchanged) ...

==== C# (raised — the shipped product output) ====
return value is null || value.Length == 0;
```

### The reading workflow

1. **Read the header.** `IR (typed tree after import)` is the ground-truth
   starting point. `IR (after <pass>)` is the tree *immediately after* that named
   pass ran. The final `C# (raised — …)` block is the shipped output.
2. **Find the first stage that is wrong.** Scan top-down. Every stage is a valid
   snapshot, so the **first** header after which the tree (or fidelity) goes bad
   names the culprit pass. Everything upstream of it is fine by construction.
3. **Diff against the previous stage.** A pass that "did nothing" prints a tree
   identical to the stage above it — so a real change is visible as the delta
   between two adjacent blocks. With `--raw` you can capture two runs (or two
   methods) and diff them with your own tooling.
4. **Check the fidelity footer.** `// fidelity: Full` means every construct was
   raised to representable C#. Anything lower means the tree carries an explicit
   unrepresentable node; the stage where fidelity first drops is where
   representability was lost.

### Node vocabulary (cheat-sheet)

The tree is the decompiler's typed IR (see [decompiler-ir.md](decompiler-ir.md));
every expression node carries its result type, so there is no opcode-guessing.
Common nodes you will see:

| Node | Meaning |
| --- | --- |
| `Function` | the method, with signature and parameters |
| `BlockContainer` / `Block IL_xxxx` | the block graph; labels are IL offsets |
| `LoadArgument` / `LoadLocal` / `LoadField` | reads (the `LoadArgumentAddress` etc. variants are by-ref) |
| `StoreLocal` / `StoreField` | writes |
| `Call` / `CallVirt` / `NewObject` | method/ctor invocation |
| `Comparison.Equal` / `Comparison.LessThan` … | relational ops |
| `LogicalBinary` (`And`/`Or`) / `LogicalNot` | boolean logic (prints as `LogicalAnd` etc.) |
| `ConditionalBranch` / `Branch` / `Return` | control flow |
| `IsInstance` | the `as` operator (renders `as` for ref targets, `is` for value targets) |
| `IsPattern` | a raised `value is T t` type pattern |
| `Constant` | a literal, with its type |
| `UnsupportedNode` | IL with no C# spelling — caps fidelity at `Partial`, rendered honestly |

When in doubt, the authoritative node set is `IrNodes.cs` and the pass order is
`IrPass.cs` in `src/ILInspector.Decompiler/Pipeline/`.

### Fidelity levels

The footer reports one of (ordered, the product routes on them):

- `Full` — every construct raised; representable C#.
- `Partial` — valid C# containing explicit unrepresentable node(s).
- `StructuredOnly` — structured control flow over low-level expressions.
- `IlOnly` — no C# rendering; IL projections still available.
- `Failed`.

The decompiler degrades honestly: IL with no C# spelling becomes an explicit node
rather than plausible-but-wrong text. See
[decompiler-quality.md](decompiler-quality.md) for the floor this guarantees.

## Contributor surface (the harness)

Maintainers working *on* the decompiler get the same Layer-0 projection plus the
recompile-and-compare oracle, through `tools/DecompilerHarness` (not shipped — it
depends on Roslyn). The harness reads the **same** per-pass capture the product
uses, so its dump is byte-identical; it just adds back-half modes the product
cannot have. The single-method dump modes (one body, no Roslyn):

```bash
# per-pass IR (the product's --dump-stages); dll arg defaults to the running CoreLib
dotnet run --project tools/DecompilerHarness -c Release -- --dump 'System.String::IsNullOrEmpty'

dotnet run --project tools/DecompilerHarness -c Release -- --dump 'Type::Method' lib.dll --diff        # each pass as a +/- hunk over the prior stage
dotnet run --project tools/DecompilerHarness -c Release -- --dump 'Type::Method' lib.dll --steps       # fine-grained per-rewrite step log
dotnet run --project tools/DecompilerHarness -c Release -- --dump 'Type::Method' lib.dll --step-limit N # replay deterministically and stop at step N
dotnet run --project tools/DecompilerHarness -c Release -- --dump 'Type::Method' lib.dll --cfg         # per-block predecessor/successor edges (add --mermaid for a flowchart)
dotnet run --project tools/DecompilerHarness -c Release -- --dump 'Type::Method' lib.dll --lowered     # render at the lower altitude (minus cosmetic sugar passes)
dotnet run --project tools/DecompilerHarness -c Release -- --pass-impact <pass> lib.dll                # corpus-wide inverse: which methods a pass changes
dotnet run --project tools/DecompilerHarness -c Release -- --remarks 'Type::Method' lib.dll            # the IR sites that cap fidelity, with DEC#### codes
```

`--diff` is the fastest way to localize a defect by hand: it collapses each stage
to just its delta, so a misbehaving pass shows up as the one hunk that is wrong.
`--step-limit N` answers "show me the tree right before this rewrite went wrong"
in one command.

The corpus-wide modes are the "find a target, prove a fix" half — they sweep a
whole assembly to pick the next gap and to prove a change regressed nothing
(harness-only: the product dumps one method, and the grading modes also need
Roslyn):

```bash
# pick a target: completeness docket, and the why-not companion
dotnet run --project tools/DecompilerHarness -c Release -- lib.dll --gaps              # methods whose raised tree still holds a goto/unsupported node
dotnet run --project tools/DecompilerHarness -c Release -- lib.dll --structuring-stops # tally why StructuringPass left containers flat

# grade a change: does it compile / still mean the same thing
dotnet run --project tools/DecompilerHarness -c Release -- lib.dll --validity-check    # rendered C# parses, is statement-legal, and binds (CS#### docket)
dotnet run --project tools/DecompilerHarness -c Release -- lib.dll --fidelity-check    # decompile -> recompile -> compare canonical opcodes

# prove zero regressions: emit a per-method defect baseline, change, then diff it
dotnet run --project tools/DecompilerHarness -c Release -- lib.dll --validity-check --emit-validity-defects /tmp/defects.txt
dotnet run --project tools/DecompilerHarness -c Release -- lib.dll --validity-check --diff-validity-defects /tmp/defects.txt  # REGRESSED must be empty
```

`--gaps` and `--structuring-stops` read the raised tree alone (no compiler), so
they are the cheap way to find and rank the next slice; `--validity-check` and
`--fidelity-check` grade the output and are the per-method dockets a fix works
down. The defect-diff loop (`--emit-validity-defects` then
`--diff-validity-defects`) is what backs a "N→M occurrences, 0 regressions"
claim — a raw bucket count cannot tell a real fix from one that also broke
something. The full harness reference is
[tools/DecompilerHarness/README.md](../tools/DecompilerHarness/README.md); the
strategy these modes serve — which check proves what, what gates CI — is
[decompiler-quality.md](decompiler-quality.md); the
architecture and the product-vs-tool layering are in
[decompiler.md](decompiler.md) and
[decompiler-inspection-oracle.md](design/decompiler-inspection-oracle.md).

## See also

- [decompiler.md](decompiler.md) — pipeline architecture and observability.
- [decompiler-ir.md](decompiler-ir.md) — the IR and importer contracts.
- [decompiler-quality.md](decompiler-quality.md) — checks, floors, fidelity.
- [decompiler-taste.md](decompiler-taste.md) — what the raised C# should look like.
