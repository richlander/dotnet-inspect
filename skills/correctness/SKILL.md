---
name: dotnet-inspect-correctness
version: 0.1.0
description: Judge whether code is sound and safe to call — its exception surface (throws, catches, exception types) and the unsafe operations in a method body.
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

## Is it memory-safe? (unsafe operations)

```bash
dnx dotnet-inspect -y -- member Type Method:1 --library MyLib.dll -S "Unsafe Operations,IL"
```

`-S "Unsafe Operations"` shows the unsafe operations in a single method body,
with IL evidence. For the library-wide safety *surface* (unsafe members, P/Invoke
methods) and provenance/supply-chain signals, see the `signals` skill.
