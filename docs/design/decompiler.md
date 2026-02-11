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

```
src/
  DotnetInspector.Decompiler/          # The decompiler library
  DotnetInspector.Decompiler.Tests/    # Test project (xunit v3)
```

### Dependency chain

```
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

```
IL bytes
  → Phase 1: Control Flow Graph + Stack Heights
  → Phase 2: Typed Stack Simulation + Variables
  → Phase 3: ILAst (tree of IL operations with variables)
  → Phase 4: Structured Control Flow (if/else, loops, switch, try/catch)
  ├─→ Phase 5: Annotated IL Emitter   (structured IL with types + CFG)
  └─→ Phase 6: C# Emitter            (expressions, statements, sugar)
```

The pipeline supports tapping in at different depths:

| Depth | Output | Use case |
|-------|--------|----------|
| Phase 1 | Raw IL with CFG boundaries | Basic disassembly |
| Phase 2 | IL with stack type annotations | Debugging stack issues, boxing |
| Phase 4 | Structured IL (indented blocks) | Compiler output analysis |
| Phase 5 | Full annotated IL | LLM troubleshooting, codegen diffing |
| Phase 6 | C# source | Human-readable decompilation |

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

| Component | Runtime source | Purpose |
|-----------|---------------|---------|
| `ILOpcodeExtensions` | `ILOpcodeHelper.cs` | Opcode size, branch classification, validity |
| `ILReaderLite` | `ILReader.cs` | `ref struct` IL byte reader |
| `ControlFlowGraph` | `FlowGraph.cs` | Basic block detection + CFG edges |
| `StackHeightCalculator` | `ILStackHelper.cs` | Max stack depth via simulation |
| `MethodBodyContext` | New | Wraps `MethodBodyBlock` + `MetadataReader` |

Key adaptations from runtime to BCL types:
- `Internal.IL.ILOpcode` → `System.Reflection.Metadata.ILOpCode`
- `ILExceptionRegion` → `ExceptionRegion`
- `MethodIL` → `MethodBodyContext`
- `ThrowHelper` → standard exceptions

Tests: 32 tests including platform assembly stress tests.

### Phase 2: Stack Type Analysis & Variable Introduction ✅

**Move from "how high is the stack" to "what types are on the stack."**

This phase introduces typed stack simulation — tracking not just stack height
but the kind of value at each stack slot (int32, int64, float, object ref,
by-ref, value type). At basic block boundaries and branch targets, stack slots
become named variables.

| Component | Reference | Purpose |
|-----------|-----------|---------|
| `StackValueKind` | Runtime `StackValueKind.cs` | 8-value enum for stack type categories |
| `StackValue` | Runtime `StackValue.cs` | Struct with Kind + TypeName, merging per ECMA-335 |
| `StackState` | New | Immutable typed stack with Push/Pop/Merge |
| `ILVariable` | ILSpy `ILVariable.cs` | Named variable (Parameter/Local/StackSlot/ExceptionSlot) |
| `StackSimulator` | Runtime `ILImporter` | Forward simulation with worklist, variable introduction |

Key design decisions:
- `StackValue` wraps a string type name (from our `SignatureDecoder`) instead of
  runtime's `TypeDesc`. This avoids the 23K LOC `Internal.TypeSystem` dependency.
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
```
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

| Component | Reference | Purpose |
|-----------|-----------|---------|
| `ILAstNode` hierarchy | ILSpy `ILAst/` | Expression/Statement/Block nodes |
| Stack-to-variable | ILSpy `ILReader` | Replace push/pop with assignments |
| Variable inlining | ILSpy transforms | Build expression trees from single-use vars |

### Phase 4: Control Flow Structuring

**Recover if/else, loops, switch, and exception handling from the CFG.**

This is the hardest phase. The compiler flattens structured control flow into
branches and jumps; we must recover the original structure. Key algorithms:

| Algorithm | Reference | Purpose |
|-----------|-----------|---------|
| Dominator tree | Standard (Lengauer-Tarjan) | Determine control dependencies |
| Loop detection | ILSpy `LoopDetection` | Find natural loops via back edges |
| If/else recovery | ILSpy `ConditionDetection` | Structured conditionals |
| Switch recovery | ILSpy `SwitchDetection` | Reconstruct switch statements |
| Exception regions | ECMA-335 spec | try/catch/finally/filter structuring |

The runtime's exception region metadata (`ExceptionRegion`) directly encodes
try/catch/finally boundaries, making exception handling recovery more
straightforward than loop/conditional recovery.

### Phase 5: Annotated IL Emitter + CLI Integration

**First output backend: structured IL with type annotations and CFG.**

This is the first user-visible deliverable from the pipeline and replaces the
existing standalone disassembler conceptually. The emitter walks the ILAst and
CFG, producing annotated IL at configurable depth:

| Depth | What it adds | Flag |
|-------|-------------|------|
| Raw | Flat instruction list (like today) | `--il` |
| Typed | Stack type annotations per instruction | `--il --annotate` |
| Structured | Indented blocks, branch target names | `--il --structured` |

| Component | Purpose |
|-----------|---------|
| `AnnotatedILEmitter` | Walk ILAst + CFG, emit formatted IL text |
| CLI integration | New `--il` flag on `api` command (or standalone) |
| Depth selector | Choose raw / typed / structured output |

### Phase 6: C# Expression & Statement Building + Emission

**Second output backend: C# source code.**

Combines what were previously Phases 5 and 6 — mapping ILAst nodes to C#
syntax and emitting formatted text.

| Pattern | Example |
|---------|---------|
| Operator mapping | `add` → `+`, `ceq` → `==` |
| Method calls | `call`/`callvirt` → `obj.Method(args)` |
| Field access | `ldfld`/`stfld` → `obj.field` |
| Property sugar | get/set method pairs → `obj.Property` |
| Casts | `castclass`/`isinst` → `(Type)x` / `x as Type` |
| Array access | `ldelem`/`stelem` → `arr[i]` |
| Object creation | `newobj` → `new Type(args)` |

| Component | Purpose |
|-----------|---------|
| `CSharpEmitter` | Walk structured ILAst, emit C# text |
| CLI integration | `--source` flag |
| Roundtrip tests | Compile C# → decompile → verify output compiles |

### Phase 7: Advanced Patterns

**Handle compiler-generated code patterns.**

| Pattern | Complexity | Approach |
|---------|-----------|----------|
| Async/await | High | Recognize state machine, reconstruct awaits |
| Iterators | High | Recognize `IEnumerable<T>` state machine |
| Closures | Medium | Detect display classes, inline captured variables |
| LINQ | Medium | Recognize query pattern method chains |
| Pattern matching | Medium | Reconstruct from branch patterns |
| String interpolation | Low | Detect `DefaultInterpolatedStringHandler` |
| Collection expressions | Low | Detect known collection builder patterns |

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

### Test sources

| Source | License | Usage |
|--------|---------|-------|
| ILSpy test suite | MIT | Test cases, expected output |
| dotnet/runtime IL tests | MIT | IL verification test corpus |
| dnSpyEx | GPL | Test inspiration only (never shipped) |
| Hand-written samples | N/A | `CfgSampleClass` in test project |
| Platform assemblies | N/A | Stress tests against System.Private.CoreLib |

## Attribution

All ported code is attributed in `THIRD-PARTY-NOTICES.TXT` with MIT license
notices and file-level origin comments.
