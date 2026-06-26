---
name: dotnet-inspect-correctness
version: 0.1.0
description: Judge whether a single version of .NET code is sound and safe to call — its exception surface, unsafe/PInvoke operations, and memory/trim/AOT safety.
---

# dotnet-inspect: correctness and safety

Use this skill to judge whether code is sound and safe to call: what it can
throw, how it handles errors, and where it steps outside safe, managed
execution. This is single-version analysis; for how these signals *change*
between versions, use the compatibility skill.

```bash
dnx dotnet-inspect -y -- <command>
```

## What can it throw? (exception surface)

There is no dedicated "Exceptions" section; exception behavior comes from
method-body analysis. Project the exception signals as graph fields, or read the
method's hidden facts:

```bash
dnx dotnet-inspect -y -- member Type Method:1 -S "Call Graph" --fields "Throws,ThrowSites,ExceptionTypes,ConstructedExceptions,Catch,Finally"
dnx dotnet-inspect -y -- member Type Method:1 -S "Caller Graph" --fields "Throws,Catch,Finally"
dnx dotnet-inspect -y -- member Type Method:1 -S Facts --tsv
```

`Throws`/`ThrowSites` count throw sites; `ExceptionTypes`/`ConstructedExceptions`
name the exception types; `Catch`/`Finally` show handling. `-S Facts` (member,
single method) lists the hidden facts in the body and supports `--tsv`.

## Is it memory-safe? (unsafe, PInvoke)

```bash
dnx dotnet-inspect -y -- member Type Method:1 --library MyLib.dll -S "Unsafe Operations,IL"
dnx dotnet-inspect -y -- library MyLib.dll -S @Audit
```

`-S "Unsafe Operations"` shows unsafe operations in a member body. `-S @Audit`
rolls up the audit sections: `Unsafe Members` for a member, and `Unsafe
Members`, `P/Invoke Methods`, and `Switches` for a library.

## Safety signals (memory model, trim/AOT)

`library Foo -S Signals` reports observations, including the memory-safety model,
`RequiresUnsafe` members, disable-runtime-marshalling, trim/AOT compatibility
(`IsTrimmable`, `IsAotCompatible`, `RequiresUnreferencedCode`), and
determinism/SourceLink provenance.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
```
