---
name: dotnet-inspect-signals
version: 0.1.0
description: Surface a dependency's observable signals — SourceLink, determinism, trim/AOT, license, vulnerabilities, age, dependency risk, and the unsafe/PInvoke surface — to judge how much caution it warrants. Observations, not verdicts.
---

# dotnet-inspect: dependency signals

Use this skill to surface the observable signals about a package or library —
provenance, build quality, safety surface, and supply-chain — so you can judge
how much caution a dependency warrants. dotnet-inspect reports observations, not
verdicts: it can give you concrete reasons for caution, not a stamp of trust.

```bash
dnx dotnet-inspect -y -- <command>
```

## The Signals rollup

`-S Signals` on `package` or `library` is the one-stop view. It reports
SourceLink, determinism, trim/AOT compatibility, memory-safety metadata,
unsafe/PInvoke surface, references, TFMs, manifest/docs, license,
vulnerabilities, package age, and dependency risk.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI -S Signals
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
```

## Safety and interop surface

`-S @Audit` drills the safety surface of a library: `Unsafe Members`, `P/Invoke
Methods`, and `Switches`. (For unsafe operations inside one method, see the
`correctness` skill; for what switches mean across versions, see
`compatibility`.)

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S @Audit
dnx dotnet-inspect -y -- library MyLib.dll -S "Unsafe Members,P/Invoke Methods"
```

## SourceLink provenance

Distinct from *fetching* source (the `sourcelink` skill), these report whether
debuggable source provenance exists and holds up: `SourceLink Availability` (is
it wired up), `SourceLink Integrity` (do the documents validate), and `SourceLink
Missing Files` (gaps).

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink Availability,SourceLink Integrity"
```
