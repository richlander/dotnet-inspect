---
name: dotnet-inspect-correctness
version: 0.2.0
description: Investigate .NET exception handling, unsafe operations, interop boundaries and pooled-resource ownership using exact Member and IL evidence; static findings do not establish overall safety.
---

# dotnet-inspect: Investigate .NET Exceptions, Unsafe Code, and Resource Ownership

Use this skill to judge whether code is sound and safe to call: what it can
throw, how it handles errors, and where it steps outside safe, managed
execution. This is single-version analysis; for how these signals *change*
between versions, use the compatibility skill.

```bash
dnx dotnet-inspect -y -- <command>
```

## What can it throw? (exception surface)

There is no dedicated "Exceptions" section; exception behavior comes from
method-body analysis. `Exception Regions` shows the exact catch/filter/finally
layout; graph fields and hidden facts summarize behavior:

```bash
dnx dotnet-inspect -y -- member Type Method:1 -S "Exception Regions"
dnx dotnet-inspect -y -- member Type Method:1 -S "Call Graph" --fields "Throws,ThrowSites,ExceptionTypes,ConstructedExceptions,Catch,Finally"
dnx dotnet-inspect -y -- member Type Method:1 -S "Call Graph" --fields "Throws,Catch,Finally"
dnx dotnet-inspect -y -- member Type Method:1 -S Facts --tsv
```

`Throws`/`ThrowSites` count throw sites; `ExceptionTypes`/`ConstructedExceptions`
name the exception types; `Catch`/`Finally` show handling. `Exception Regions`
retains IL ranges and caught types. `-S Facts` (member, single method) lists the
hidden facts in the body and supports `--tsv`.

## Is it memory-safe? (unsafe operations)

```bash
dnx dotnet-inspect -y -- member Type Method:1 --library MyLib.dll -S "Unsafe Operations,IL"
```

`-S "Unsafe Operations"` shows the unsafe operations in a single method body,
with IL evidence. For the library-wide safety *surface* (unsafe members, P/Invoke
methods) and provenance/supply-chain signals, see the `signals` skill.

For one crash or profiler coordinate, use `library address
0x06000001+0x5 --library Foo.dll` for the default source-location, member,
instruction, exception, callsite, and return-address context. Safety evidence
is opt-in: `library address 0x06000001+0x5 --library Foo.dll -S
"Context: Safety"`.

To confirm whether one definite unsafe operation appeared at an adjacent
version boundary, first correlate caller-selected package cells:

```bash
dnx dotnet-inspect -y -- diff --history --package MyLib@1.0.0..2.0.0 \
  -t MyType -m Method \
  --finding analysis.unsafety --at first --at last
```

Repeat `--at` for sparse probes or use `--at all` for an explicitly bounded
dense traversal. A gap-spanning `Added` row locates a candidate boundary; it
does not claim the exact introduction version. Confirm the adjacent pair:

```bash
dnx dotnet-inspect -y -- diff --package MyLib@1.4.0..1.5.0 \
  -t MyType -m Method \
  --finding analysis.unsafety
```

`PairFinding.Added` is the introduction proof. `Present` and `Removed`
distinguish persistence from disappearance; the command compares only the two
supplied endpoints.

## Review unsafe code and ownership against .NET guidance

Use the [.NET unsafe-code best-practices guide](https://learn.microsoft.com/dotnet/standard/unsafe-code/best-practices)
as the policy owner. Its
[source document](https://raw.githubusercontent.com/dotnet/docs/refs/heads/main/docs/standard/unsafe-code/best-practices.md)
currently organizes the review into 26 numbered topics.

`dotnet-inspect` supplies compiled evidence, not a monolithic compliance
verdict. Acquire reusable JSONL row sets, use the agent and `jq` to slice and
join them for each question, then drill only consequential Members. The same
rows can answer other safety questions without adding a new aggregate command.

### Acquire focused evidence

Start with `library MyLib.dll -S "Unsafe Code" --jsonl`; request
`P/Invoke Methods`, `Custom Attributes`, `References`, or `Signals` only as the
question requires. Keep row families, exit status, stderr and completion
separate: empty output can be a failed inspection rather than checked absence.
Use `-D` for section fields, `-Q` for supported filters and
`explain "<question>"` for unfamiliar evidence. Follow the returned resource
and CLI binding rather than guessing deeper query rules.

Library `Custom Attributes` covers a filtered set of assembly- and module-level
attributes. It does not discover attributes on Types, fields, or methods, and
does not establish `SkipLocalsInit` coverage. Preserve that scope: use
exact-subject attributes only after another row or source evidence selects the
Type or Member, and report whole-library attribute discovery as `unverified`.

Ownership and unsafe code overlap. A pooled buffer can have no pointer opcode
yet still fail through use-after-return, missing cleanup, or ownership transfer.
Use `library MyLib.dll -S "Resource Triage" --jsonl` beside unsafe evidence.

Resource Triage currently has no positive completion receipt and can emit rows
while some resource roots remain incomplete. Always describe its ownership
coverage as `unverified`; preserve any more specific diagnostic and never turn
zero rows into "no ownership issue."

### Select and drill one candidate

Slice machine rows locally with `jq` where no product predicate is available.
Retain Member, IL, token, Candidate, Finding and boundary coordinates; keep
unsafe, ownership and interop evidence distinct. Display-label equality does
not establish identity. Resolve an exact Type and Member selector before
drilling, and preserve the slice or join used to derive a candidate.

### Drill one candidate

Use separate machine outputs for focused evidence and retain IL for review:

```bash
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll \
  -S "Unsafe Operations" --jsonl
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll \
  -S "Exception Regions" --jsonl
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S IL
```

Load `skill decompiler` only when reconstructed C# helps explain the IL. Use
source evidence when the question depends on lexical scope, an authored guard,
compiler warnings, or repository test practice.

### Route the 26 guidance questions

The routes below name the smallest useful evidence composition. They do not
claim that matching evidence proves a violation or that checked-empty rows
prove safety.

| # | Guidance topic | Evidence route and boundary |
| ---: | --- | --- |
| 1 | Untracked managed pointers | Unsafe-call slice for `AsPointer`; drill pointer provenance, pinning, uses, and escape. |
| 2 | Pointer escape from `fixed` | Unsafe/pinned evidence plus IL stores, returns, and calls; lexical `fixed` scope may require source. |
| 3 | Runtime or library internals | Unsafe calls, attributes, references, and nonpublic call evidence; broad reliance remains a review judgment. |
| 4 | Invalid managed pointers | Slice `Unsafe.Add` and related byref arithmetic; drill range and control-flow evidence. |
| 5 | Reinterpret casts | Slice `Unsafe.As` and `BitCast`; join exact source/destination Type metadata before judging compatibility. |
| 6 | Write barrier and GC-reference atomicity | Slice `cpblk`, `initblk`, indirect stores, and copy APIs; drill destination provenance and field types. |
| 7 | Object lifetime | Calls to `GC.KeepAlive`, `SafeHandle`, finalizers, callbacks, and interop boundaries; absence usually remains unverified. |
| 8 | Cross-thread locals | Call graph, async/state-machine, pointer/byref, and ownership evidence; thread transfer generally needs source or runtime evidence. |
| 9 | Unsafe bounds-check removal | Slice pointer dereferences and `Unsafe.Add`; inspect guards and ranges, then require runtime performance evidence. |
| 10 | Memory access coalescing | Slice `Unsafe`/`MemoryMarshal` calls and stores; compare safe alternatives with runtime evidence. |
| 11 | Unaligned memory access | Slice unaligned APIs and indirect operations; target alignment and atomicity remain platform/context questions. |
| 12 | Binary struct serialization | Slice `MemoryMarshal`/`Unsafe` reads, writes, copies, and exact Type layouts; open generics remain incomplete. |
| 13 | Null managed pointers | Slice `Unsafe.NullRef` and pointer-to-byref conversions; drill uses and discarded dereferences. |
| 14 | `stackalloc` | Slice `localloc`; drill loop membership, length construction, span conversion, and bounds. |
| 15 | Fixed-size buffers | Unsafe declarations can select candidates, but library attributes cannot discover field-level `FixedBufferAttribute`; whole-library discovery is unverified, so use source or exact-Type decompilation for known candidates. |
| 16 | Pointer plus length APIs | Pointer signatures and P/Invoke rows identify candidates; inspect explicit lengths and termination assumptions. |
| 17 | String mutation | Pointer stores plus string-origin evidence; source or decompilation usually establishes the target. |
| 18 | Raw IL generation | References and calls identify Reflection.Emit or Cecil use; generated-IL validity requires separate evidence. |
| 19 | Uninitialized locals | Slice `Unsafe.SkipInit` calls. Library attributes cannot establish `SkipLocalsInit`; that whole-library census is unverified, so use source or inspect exact-Member attributes and IL local initialization, then drill reads, writes, and pooled-buffer initialization. |
| 20 | `ArrayPool<T>` and pooling | Join Resource Triage, unsafe calls, exceptions, and returns; current ownership coverage is always unverified. |
| 21 | `bool`/integer conversion | Slice `Unsafe.As`, `BitCast`, and unaligned reads involving Boolean and integral Types. |
| 22 | Interop | P/Invoke rows, signatures, attributes, unsafe operations, and ownership/lifetime evidence. |
| 23 | Thread safety | Call graph and mutable-resource evidence can select candidates; a general thread-safety verdict is out of scope. |
| 24 | SIMD/vectorization | Calls and unsafe operations identify loads/stores; bounds, alignment, atomicity, and benefit need context/runtime evidence. |
| 25 | Fuzz testing | Assembly evidence cannot establish repository fuzz coverage; inspect tests and CI separately. |
| 26 | Compiler warnings | Warnings are not retained in ordinary assemblies; require build diagnostics or SARIF. |

### Report the review

For each applicable question, report:

1. the guidance topic and exact inspected subject;
2. owner-issued rows and exact coordinates;
3. the `jq` slice or join that produced the bounded candidate set;
4. the agent's labeled interpretation;
5. completion or `unverified` boundaries; and
6. the next focused probe when evidence cannot decide the question.

Do not report all 26 topics as clean because no matching row appeared. Omit
topics that are genuinely inapplicable, and distinguish them from applicable
questions whose evidence is unavailable or incomplete.
