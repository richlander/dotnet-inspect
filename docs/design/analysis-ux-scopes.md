# Analysis UX Scopes

`dotnet-inspect` should expose one coherent analysis vocabulary across offset,
member, type, and library views. The same underlying facts may be rendered at
different scopes, but they should not acquire unrelated names or meanings just
because the entry point changed.

## Scope model

| Scope | User question | Shape word |
| --- | --- | --- |
| Offset | What is true at this exact IL coordinate? | `Context` |
| Member | What does this method contain? | rows, regions, facts |
| Type | Which members on this type have this? | member-indexed rows |
| Library | Where are the most important instances? | triage or ranking rows |

`Context` is reserved for point-scoped views. Wider scopes should prefer
container names such as `Regions`, `Calls`, `Facts`, or `Triage`.

## Canonical examples from offset context

The offset context sections established by `library --il-offset` are the
reference examples for future semantic analysis work.

### Source

Offset view:

```bash
dotnet-inspect library My.dll --il-offset 0x06000042+0x2A -S "Context: Source Location"
```

```md
## Context: Source Location

| Field | Value |
| ----- | ----- |
| Method | My.Type.Method |
| Token | 0x6000042 |
| IL Offset | 0x2A |
| Matched Offset | 0x28 |
| File | /_/src/Foo.cs |
| Line | 87 |
| Url | https://github.com/org/repo/blob/sha/src/Foo.cs#L87 |
```

Member/type analogs already exist as `Source Locations`, `Original Source`, and
`Source Files`.

### Member identity

Offset view:

```bash
dotnet-inspect library My.dll --il-offset 0x06000042+0x2A -S "Context: Member"
```

```md
## Context: Member

| Field | Value |
| ----- | ----- |
| Assembly | My.Assembly |
| Type | My.Type |
| Type Kind | class |
| Member | My.Type.DoWork |
| Signature | int DoWork(int value) |
| Member Kind | method |
| Visibility | public |
| Static | No |
| Async | State machine |
| Metadata Token | 0x6000042 |
| IL Offset | 0x2A |
```

Wider scopes already expose this through type/member identity and
`Member Index`-style rows.

### Instruction

Offset view:

```bash
dotnet-inspect library My.dll --il-offset 0x06000042+0x2A -S "Context: Instruction"
```

```md
## Context: Instruction

| Field | Value |
| ----- | ----- |
| IL Offset | 0x2A |
| Boundary | Exact |
| Opcode | callvirt |
| Operand Kind | Method |
| Operand | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
| Next Offset | 0x2F |
| Length | 5 |
| Block | 0 |
| Terminates Block | No |
| Falls Through | Yes |
```

The member analog is the existing `IL` section and any future instruction/facts
row view, not a separate offset-only concept.

### Exception

Offset view means "I am inside an exception region":

```bash
dotnet-inspect library My.dll --il-offset 0x06000042+0x1 -S "Context: Exception"
```

```md
## Context: Exception

| Region | Context | Clause | Try Range | Handler Range | Caught Type |
| ------ | ------- | ------ | --------- | ------------- | ----------- |
| 1 | try | catch | IL_0001..IL_0009 | IL_0009..IL_000F | System.DivideByZeroException |
```

Member view should mean "this method has exception regions":

```bash
dotnet-inspect member My.Type DoWork --library My.dll -S "Exception Regions"
```

```md
## Exception Regions

| Region | Clause | Try Range | Handler Range | Caught Type |
| ------ | ------ | --------- | ------------- | ----------- |
| 1 | catch | IL_0001..IL_0009 | IL_0009..IL_000F | System.DivideByZeroException |
| 2 | finally | IL_0010..IL_001A | IL_001A..IL_0024 | |
```

This is the inner/outer distinction: an offset asks **where am I?**, while a
member asks **what does this member contain?**

### Callsite and return address

Offset callsite view:

```bash
dotnet-inspect library My.dll --il-offset 0x06000042+0x2A -S "Context: Callsite"
```

```md
## Context: Callsite

| Field | Value |
| ----- | ----- |
| Call Offset | IL_002A |
| Opcode | callvirt |
| Call Kind | virtual |
| Callee | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
| Return Address | IL_002F |
```

Offset return-address view:

```bash
dotnet-inspect library My.dll --il-offset 0x06000042+0x2F -S "Context: Return Address"
```

```md
## Context: Return Address

| Field | Value |
| ----- | ----- |
| IL Offset | IL_002F |
| Call Offset | IL_002A |
| Opcode | callvirt |
| Call Kind | virtual |
| Callee | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
```

The member analog should converge with existing `Calls` rows:

```bash
dotnet-inspect member My.Type DoWork --library My.dll -S Calls
```

```md
## Calls

| IL Offset | Opcode | Call Kind | Callee |
| --------- | ------ | --------- | ------ |
| IL_002A | callvirt | virtual | MyApp.IWorker::DoWork(int) |
```

## Naming rules for semantic analysis

Future semantic sections should use the same facts and nouns across scopes.

| Concept | Offset | Member | Type | Library |
| --- | --- | --- | --- | --- |
| Exception | `Context: Exception` | `Exception Regions` | `Exception Regions` with member column | optional `Exception Triage` |
| Callsite | `Context: Callsite` / `Context: Return Address` | `Calls` | `Calls` with member column | `Top Leverage` / call graph summaries |
| Allocation | `Context: Allocation` | `Allocation Facts` | `Allocation Facts` with member column | `@Performance` kind sections |
| Resource lifecycle | optional `Resource Context` | optional `Resource Facts` | resource rows with member column | `Resource Triage` |
| Safety | `Context: Safety` | `Safety Facts` | `Safety Facts` with member column | `Safety Triage` or `Correctness Triage` |
| Cost | `Context: Cost` | `Cost Facts` / `Cost Overlay` | cost rows with member column | `@Performance` kind sections |

Rules:

1. Offset point views use `* Context`.
2. Member container views use `* Regions`, `Calls`, or `* Facts`.
3. Type views use the same nouns but include member identity.
4. Library views use `* Triage` only when ranked or curated.
5. New offset semantic sections should have a member/type/library story unless
   intentionally point-only.

At `library` scope the performance findings are decomposed into kind-scoped
sections under the `@Performance` category (`Performance: Boxing`,
`Performance: Arrays`, `Performance: Closures and delegates`,
`Performance: Enumerators`, `Performance: Loop hot paths`,
`Performance: Allocation hotspots`, `Performance: Async`, plus a non-lossy
`Performance: Other`). They follow the il-offset section model: opt-in, absent
when empty, selectable individually or as a group, with a nested `performance`
JSON object (one array per kind with rows) and a per-kind `--count` map. The
kind sections are catalog-hidden (`ListedInCatalog = false`): the top-level
`-D`/`--schema` catalog lists only the `@Performance` category as their
entrypoint, keeping discovery noise low while the sections stay selectable and
drillable (`-D @Performance`). `ListedInCatalog` is a general section-descriptor
flag (orthogonal to `ExplicitOnly` render gating), so any future curated group
can hide its members behind a category with the same mechanism. Flattened
tabular output (`--table`/`--tsv`/`--jsonl`) renders the group as one table with
a leading `Kind` column so each row is self-describing. The
`type`/`member` `Performance Triage` lens is a different, single-member view and
keeps that name.

## Next semantic additions

Before adding `Context: Safety`, `Context: Allocation`, or `Context: Cost`, define
the shared fact/pivot model that each wider scope will use. That model should
reuse the existing Analysis/Research substrate rather than creating offset-only
semantic logic in the CLI.
