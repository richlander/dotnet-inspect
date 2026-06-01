# IL-to-C# Decompiler

## Goal

Build an IL-to-C# decompiler as a new component within dotnet-inspect. Given a
.NET assembly (or a type/method within one), produce readable C# source code
from the IL bytecode. This enables developers to understand compiled code,
verify compiler output, and inspect framework internals — all from the CLI.

The decompiler is a peer to the existing IL disassembler (`--index`). Where the
disassembler shows raw IL instructions, the decompiler reconstructs the original
C# intent: control flow, expressions, type information, and language sugar.

## Why build this

- **CLI-native**: No GUI required. Integrate with existing dotnet-inspect
  workflows (package inspection, API surface, assembly audit).
- **Focused scope**: We don't need a full IDE decompiler. We need a tool that
  can show "what does this method do?" in a terminal.
- **Learning platform**: The decompiler pipeline is a well-understood CS problem.
  Building it incrementally creates a foundation we can extend over time.
- **Runtime team alignment**: Porting algorithms from dotnet/runtime gives us
  production-tested IL analysis code that we understand and can maintain.

## Architecture

### Project structure

```text
src/
  DotnetInspector.Decompiler/          # The decompiler library
  DotnetInspector.Decompiler.Tests/    # Test project (xunit v3)
```

### Dependency chain

```text
dotnet-inspect (CLI)
  → DotnetInspector.Decompiler         # NEW
  → DotnetInspector.Metadata           # Existing (SignatureDecoder, TypeResolver)
  → DotnetInspector.Core               # Existing (CoreCache)
```

The decompiler depends on `Metadata` for signature decoding and type name
resolution. It does NOT modify any existing code — purely additive.

### Design principles

1. **Incremental PRs**: Each phase is a self-contained PR that adds capability
   without breaking existing functionality.
2. **BCL types only**: Use `System.Reflection.Metadata` directly. No dependency
   on `Internal.TypeSystem` or other runtime-internal abstractions.
3. **Port, then adapt**: Start from runtime's production-tested algorithms,
   adapt the type surface. Don't reinvent CFG construction or stack analysis.
4. **Test against real assemblies**: Every phase includes stress tests against
   `System.Private.CoreLib` (~39K methods) to catch edge cases early.

## Pipeline

The decompiler is a multi-stage pipeline with pluggable output backends.
The same analysis infrastructure supports both annotated IL and C# emission:

```text
IL bytes
  → Phase 1: Control Flow Graph + Stack Heights
  → Phase 2: Typed Stack Simulation + Variables
  → Phase 3: ILAst (tree of IL operations with variables)
  → Phase 4: Structured Control Flow (if/else, loops, switch, try/catch)
  ├─→ Phase 5: Annotated IL Emitter   (structured IL with types + CFG)
  └─→ Phase 6: C# Emitter            (expressions, statements, sugar)
```

The pipeline supports tapping in at different depths:

| Depth   | Output                          | Use case                             |
| ------- | ------------------------------- | ------------------------------------ |
| Phase 1 | Raw IL with CFG boundaries      | Basic disassembly                    |
| Phase 2 | IL with stack type annotations  | Debugging stack issues, boxing       |
| Phase 4 | Structured IL (indented blocks) | Compiler output analysis             |
| Phase 5 | Full annotated IL               | LLM troubleshooting, codegen diffing |
| Phase 6 | C# source                       | Human-readable decompilation         |

### Why annotated IL matters

Annotated IL is often a better troubleshooting aid than C# — especially for
LLMs and for diagnosing compiler/runtime behavior. Raw IL is nearly opaque:
offsets, tokens, and implicit stack state require mental simulation. Annotated
IL makes the operations, types, and control flow immediately visible:

```il
// Block 0 (entry → Block 1, Block 3)
IL_0000: ldarg.0              // stack: [this: MyService]
IL_0001: ldfld _cache          // stack: [ICache]
IL_0006: ldarg.1              // stack: [ICache, key: string]
IL_0007: callvirt ICache::TryGet(string) → bool  // stack: [bool]
IL_000C: brfalse.s Block 3    // if false → cache miss path

// Block 1 (→ return)  [cache hit]
IL_000E: ldarg.0              // stack: [this: MyService]
...
```

This is valuable for:
- **JIT/compiler bug diagnosis** — stack type mismatches visible at a glance
- **Understanding generated code** — LibraryImport stubs, async state machines
- **Performance analysis** — seeing exactly where boxing (`Int32` → `ObjRef`) occurs
- **Compiler output diffing** — structured IL diffs cleanly across Roslyn versions
- **LLM-assisted analysis** — token-efficient AND comprehensible by LLMs

The annotated IL emitter replaces the existing standalone disassembler
(`ILDisassembler`, 493 LOC) conceptually — it produces the same information
but with structure, types, and resolved names from the shared analysis pipeline.

Each stage is independently testable. Early stages produce useful output even
before later ones are complete.

## Phases

### Phase 1: IL Analysis Foundation ✅ (PR #139)

**Port foundational IL analysis algorithms from dotnet/runtime.**

| Component                | Runtime source      | Purpose                                      |
| ------------------------ | ------------------- | -------------------------------------------- |
| `ILOpcodeExtensions`     | `ILOpcodeHelper.cs` | Opcode size, branch classification, validity |
| `ILReaderLite`           | `ILReader.cs`       | `ref struct` IL byte reader                  |
| `ControlFlowGraph`       | `FlowGraph.cs`      | Basic block detection + CFG edges            |
| `StackHeightCalculator`  | `ILStackHelper.cs`  | Max stack depth via simulation               |
| `MethodBodyContext`      | New                 | Wraps `MethodBodyBlock` + `MetadataReader`   |

Key adaptations from runtime to BCL types:
- `Internal.IL.ILOpcode` → `System.Reflection.Metadata.ILOpCode`
- `ILExceptionRegion` → `ExceptionRegion`
- `MethodIL` → `MethodBodyContext`
- `ThrowHelper` → standard exceptions

**Simplification vs runtime.** The runtime's IL analysis code lives inside a
full AOT compiler (`crossgen2`) and carries dependencies on `Internal.TypeSystem`
(23K LOC), `ILProvider`, `MethodIL`, and the compilation pipeline's
`MethodDesc`/`TypeDesc` hierarchy. We replaced all of that with BCL's
`System.Reflection.Metadata` types — `PEReader`, `MetadataReader`,
`MethodBodyBlock`, `ExceptionRegion`, and our existing `SignatureDecoder`.
The algorithmic core of each ported component (CFG construction, stack height
calculation, opcode classification) is preserved intact; only the type surface
and API integration points changed. This took ~3,100 LOC of runtime code down
to ~1,800 LOC while retaining the same correctness properties — verified by
stress-testing against the same `System.Private.CoreLib` assembly the runtime
tests against.

Tests: 32 tests including platform assembly stress tests.

### Phase 2: Stack Type Analysis & Variable Introduction ✅

#### Move from "how high is the stack" to "what types are on the stack"

This phase introduces typed stack simulation — tracking not just stack height
but the kind of value at each stack slot (int32, int64, float, object ref,
by-ref, value type). At basic block boundaries and branch targets, stack slots
become named variables.

| Component        | Reference                   | Purpose                                                  |
| ---------------- | --------------------------- | -------------------------------------------------------- |
| `StackValueKind` | Runtime `StackValueKind.cs` | 8-value enum for stack type categories                   |
| `StackValue`     | Runtime `StackValue.cs`     | Struct with Kind + TypeName, merging per ECMA-335        |
| `StackState`     | New                         | Immutable typed stack with Push/Pop/Merge                |
| `ILVariable`     | ILSpy `ILVariable.cs`       | Named variable (Parameter/Local/StackSlot/ExceptionSlot) |
| `StackSimulator` | Runtime `ILImporter`        | Forward simulation with worklist, variable introduction  |

Key design decisions:
- `StackValue` wraps a string type name (from our `SignatureDecoder`) instead of
  runtime's `TypeDesc`. This avoids the 23K LOC `Internal.TypeSystem` dependency.
  Trade-off: string-based type identity could cause incorrect merges at join
  points when two differently-instantiated generics collapse to the same string
  (e.g., `Span<byte>` and `Span<int>` both rendering as `Span<T>` when generic
  context is unavailable). In practice this is rare — we resolve generic context
  for `MemberReference` and `MethodSpecification` handles, and the merge logic
  widens to `object` on type name mismatch rather than producing incorrect
  results.
- `StackState` is immutable (backed by `ImmutableArray`) for safe propagation
  across the worklist without aliasing bugs.
- `MethodBodyContext` extended with `ParameterTypes` and `ReturnType` properties
  to support argument type resolution during simulation.
- `leave`/`leave.s` propagates empty stack to target per ECMA-335 III.3.42.
- Exception handler entries get their own stack state (exception object for
  catch/filter, empty for finally/fault).

Tests: 33 tests including stack value merging, stack state operations,
per-method simulation verification, and platform assembly stress test.

### Phase 3: ILAst Construction ✅

**Convert flat IL + variables into a tree representation.**

The ILAst is a tree of operations where stack manipulation is replaced by
explicit variable reads/writes. Single-use variables are inlined to build
expression trees.

Example transformation:

```text
// IL (stack-based)          →  // ILAst (variable-based)
ldarg.0                          return this.name;
ldfld string Name
ret

// Intermediate step:
v0 = ldarg.0
v1 = ldfld(v0, Name)
ret v1

// After inlining:
ret ldfld(ldarg.0, Name)
```

| Component             | Reference        | Purpose                                     |
| --------------------- | ---------------- | ------------------------------------------- |
| `ILAstNode` hierarchy | ILSpy `ILAst/`   | Expression/Statement/Block nodes            |
| Stack-to-variable     | ILSpy `ILReader` | Replace push/pop with assignments           |
| Variable inlining     | ILSpy transforms | Build expression trees from single-use vars |

### Phase 4: Control Flow Structuring ✅

**Recover if/else, loops, switch, and exception handling from the CFG.**

This is the hardest phase. The compiler flattens structured control flow into
branches and jumps; we must recover the original structure. Key algorithms:

| Algorithm         | Reference                  | Purpose                               |
| ----------------- | -------------------------- | ------------------------------------- |
| Dominator tree    | Standard (Lengauer-Tarjan) | Determine control dependencies        |
| Loop detection    | ILSpy `LoopDetection`      | Find natural loops via back edges     |
| If/else recovery  | ILSpy `ConditionDetection` | Structured conditionals               |
| Switch recovery   | ILSpy `SwitchDetection`    | Reconstruct switch statements         |
| Exception regions | ECMA-335 spec              | try/catch/finally/filter structuring  |

The runtime's exception region metadata (`ExceptionRegion`) directly encodes
try/catch/finally boundaries, making exception handling recovery more
straightforward than loop/conditional recovery.

**Simplification vs ILSpy.** Because we emit lowered C# rather than recovering
sugar (`using`, `foreach`, `lock`), our Phase 4 requirements are lighter than
ILSpy's. We need structuring for: (1) knowing where to emit `try`/`finally`/
`catch` blocks in lowered C#, (2) recovering `if`/`else` to avoid `goto`-heavy
output, and (3) block structure for annotated IL. We do NOT need ILSpy's
~50 IL transforms for pattern matching, iterator reconstruction, or async state
machine recovery. The `ConditionalDetector` does detect specific IL patterns
like null-conditionals (`dup` + `brtrue` + trivial else), but these are
pattern-specific optimizations in the emitter, not general sugar recovery.

### Phase 5: Annotated IL Emitter ✅

**First output backend: structured IL with type annotations and CFG.**

This is the first user-visible deliverable from the pipeline and replaces the
existing standalone disassembler conceptually. The emitter walks the ILAst and
CFG, producing annotated IL at configurable depth:

| Depth      | What it adds                           | Flag                |
| ---------- | -------------------------------------- | ------------------- |
| Raw        | Flat instruction list (like today)     | `--il`              |
| Typed      | Stack type annotations per instruction | `--il --annotate`   |
| Structured | Indented blocks, branch target names   | `--il --structured` |

| Component            | Purpose                                           |
| -------------------- | ------------------------------------------------- |
| `AnnotatedILEmitter` | Walk ILAst + CFG, emit formatted IL text          |
| CLI integration      | New `--il` flag on `api` command (or standalone)  |
| Depth selector       | Choose raw / typed / structured output            |

### Phase 6: Lowered C# Emitter ✅

**Second output backend: lowered C# source code.**

The emitter produces **lowered C#** — an honest IL-to-C# mapping that shows
what the compiler actually emitted, not the sugar the developer wrote. This is
the most natural output when decompiling *up* from IL and serves the primary
use cases (LLM troubleshooting, codegen analysis, runtime diagnostics) better
than sugar-recovered "idiomatic" C# would.

What "lowered" means:
- `stloc.0` → `V_0 = expr;`, using PDB local names when available (embedded, or an
  external/downloaded portable PDB) and falling back to synthesized `V_n` slots otherwise
- Branches → `goto` / `if (...) goto`, not recovering `foreach`/`using`
- `call get_Property()` stays as a method call, not `obj.Property`
- `box` / `unbox.any` visible as casts with annotations
- Compiler-generated state machines shown as-is (fields + switch/goto)

| Pattern          | IL → Lowered C#                                   |
| ---------------- | ------------------------------------------------- |
| Operator mapping | `add` → `+`, `ceq` → `==`                         |
| Method calls     | `call`/`callvirt` → `TypeName.Method(args)`       |
| Field access     | `ldfld`/`stfld` → `obj.field`                     |
| Casts            | `castclass` → `(Type)x`, `isinst` → `x as Type`   |
| Array access     | `ldelem`/`stelem` → `arr[i]`                      |
| Object creation  | `newobj` → `new Type(args)`                       |
| Control flow     | `br`/`brtrue` → `goto` / `if (...) goto`          |
| Runtime async    | `AsyncHelpers.Await(x)` → `await x` (v2)          |

| Component       | Purpose                                             |
| --------------- | --------------------------------------------------- |
| `CSharpEmitter` | Walk ILAst + StructuredControlFlow, emit lowered C# |
| CLI integration | `--source` flag                                     |

### Phase 7: Advanced Patterns

**Handle compiler-generated code patterns.**

Ordered by impact on LLM consumption — closures appear in virtually all
LINQ-heavy code and are straightforward to detect, while async/await state
machines are harder and their lowered form (fields + switch/goto) is still
reasonably interpretable.

| Pattern                | Priority | Complexity | Approach                                          |
| ---------------------- | -------- | ---------- | ------------------------------------------------- |
| Closures               | High     | Medium     | Detect `<>c__DisplayClass`, inline captured vars  |
| LINQ                   | Medium   | Medium     | Recognize query pattern method chains             |
| Pattern matching       | Medium   | Medium     | Reconstruct from branch patterns                  |
| String interpolation   | Medium   | Low        | Detect `DefaultInterpolatedStringHandler`         |
| Collection expressions | Medium   | Low        | Detect known collection builder patterns          |
| Async/await            | Lower    | High       | Recognize state machine, reconstruct awaits       |
| Iterators              | Lower    | High       | Recognize `IEnumerable<T>` state machine          |

## Error recovery

The pipeline is designed for graceful degradation. Each phase catches exceptions
at the method level and falls back to shallower output:

| Failure point            | Fallback                                            |
| ------------------------ | --------------------------------------------------- |
| CFG construction         | Emit flat IL (no block structure)                   |
| Stack simulation         | Emit IL with CFG blocks but no type annotations     |
| ILAst construction       | Emit annotated IL only (skip lowered C# section)    |
| Control flow structuring | Emit flat ILAst as sequential statements + goto     |
| C# emission              | Emit `/* unsupported: opcode */` comment inline     |
| Individual expression    | Emit `/* opcode arg1 arg2 */` and continue          |

In practice, the stress test against CoreLib's ~39K methods exercises the full
pipeline with zero failures. NuGet packages in the wild are messier (obfuscated
assemblies, invalid IL, unsupported patterns), so every pipeline stage wraps
method-level processing in `try/catch` to ensure one bad method doesn't block
the rest of a type's output. The `catch` blocks produce diagnostic comments
in the output rather than propagating exceptions.

## Reference material

### Ported from (MIT)

- **dotnet/runtime** `src/coreclr/tools/Common/TypeSystem/IL/`
  - `FlowGraph.cs`, `ILReader.cs`, `ILOpcodeHelper.cs`, `ILStackHelper.cs`
  - Production-tested against the entire BCL
  - Core IL analysis: ~3,100 LOC

- **dotnet/runtime** `src/coreclr/tools/ILVerification/`
  - `ILImporter.StackValue.cs` — full stack type simulation
  - Stack verification: ~3,550 LOC
  - Reference for Phase 2

### Architecture reference (MIT)

- **ILSpy** (`~/git/ILSpy`)
  - Full decompiler pipeline: ILReader → BlockBuilder → CFG → 50 IL transforms
    → ExpressionBuilder → StatementBuilder → C# AST
  - 240 test case files (104 Pretty, 39 Correctness, 97 ILPretty)
  - Key components: ILReader 2.2K LOC, ExpressionBuilder 5K LOC,
    StatementBuilder 1.6K LOC

- **dotnet/runtime IL interpreter** (`src/coreclr/interpreter/`)
  - C++ interpreter that compiles IL to an intermediate representation with
    typed variables — the same var-per-instruction pattern our ILAst uses.
    The interpreter's `compiler.cpp` (11K LOC) builds basic blocks, tracks
    stack types (`StackType` enum similar to our `StackValueKind`), and creates
    `InterpVar` per stack operation. Memory profiling shows ~40% of allocation
    goes to variables, confirming this is fundamental to IL analysis.
  - Notable: the runtime is creating `src/coreclr/jitshared/` to share IL
    analysis infrastructure between JIT and interpreter (PR #123830). Our
    design follows the same principle — shared pipeline, multiple emitters.
  - The interpreter distinguishes `StackTypeLocalVariableAddress` from
    `StackTypeByRef` for `ldloca`/`ldarga` results. We may want this
    distinction for more precise `ref` semantics in annotated IL.
  - **Test suite** (`src/tests/JIT/interpreter/Interpreter.cs`, 3K LOC):
    covers shared generics, `calli`, `ldftn`, delegates, static virtual
    methods, constrained calls, nested exception handling, nullable boxing.
    This is likely our most important test reference for Phase 7 — these
    tests exercise the IL patterns that appear in real framework code
    (the patterns our tool hits when inspecting NuGet packages), rather
    than decompiler-centric sugar recovery patterns.

### Test sources

| Source                     | License | Usage                                                        |
| -------------------------- | ------- | ------------------------------------------------------------ |
| ILSpy test suite           | MIT     | Test cases, expected output                                  |
| dotnet/runtime IL tests    | MIT     | IL verification test corpus                                  |
| dotnet/runtime interpreter | MIT     | IL pattern coverage (generics, delegates, constrained calls) |
| dnSpyEx                    | GPL     | Test inspiration only (never shipped)                        |
| Hand-written samples       | N/A     | `CfgSampleClass` in test project                             |
| Platform assemblies        | N/A     | Stress tests against System.Private.CoreLib                  |

## Usage workflow

The decompiler integrates with the existing `api` command. The workflow is:
browse type → select member → view decompiled output.

### Step 1: Browse the type

Start with `--show-index` to see available members and the flags to target each one.

```sh
dotnet-inspect member --package Microsoft.Extensions.Options OptionsFactory --show-index
```

This shows a methods table with a `Select` column containing the exact flags
needed. For `OptionsFactory`, the table shows `-m Create` for the single
`Create` method.

### Step 2: Select a member

Use the `-m` flag from the select column. For overloaded methods, use
`--params` or `--index` to disambiguate.

```sh
# Single method — -m is enough
dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory -m Create --index 1

# Overloaded method — use --params from the Select column
dotnet-inspect api --package System.Linq Enumerable -m Where --params "IEnumerable<TSource>,Func<TSource, bool>" --index 1

# Or use --index directly (1-based overload position)
dotnet-inspect api --package System.Linq Enumerable -m Where --index 1
```

The `--index` flag activates the decompiler sections: Lowered C#, IL, and
Annotated IL appear below the methods table.

### Step 3: Read the output

The output has four sections:

| Section | What it shows |
| ------- | ------------- |
| **Source** | Original C# source (when PDB with source link is available) |
| **Lowered C#** | Decompiled C# — faithful to IL semantics, goto-with-labels control flow |
| **IL** | Raw IL disassembly with resolved tokens |
| **Annotated IL** | IL with pre-execution stack state at each instruction |

### Examples

```sh
# Options validation pattern — foreach loops, isinst type checks, throw
dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory -m Create --index 1

# LINQ iterator dispatch — generic methods, isinst chains, newobj
dotnet-inspect api --package System.Linq Enumerable -m Where --index 1

# Logger cache with locking — ConcurrentDictionary, Monitor, try/finally
dotnet-inspect api --package Microsoft.Extensions.Logging LoggerFactory -m CreateLogger --index 1

# DI service resolution — dictionary lookup, null guards
dotnet-inspect api --package Microsoft.Extensions.DependencyInjection ServiceProvider -m GetService --index 1
```

## Attribution

All ported code is attributed in `THIRD-PARTY-NOTICES.TXT` with MIT license
notices and file-level origin comments.
