---
name: dotnet-inspect-audit
version: 0.1.0
description: Vet a .NET dependency for trust and quality — the Signals rollup (SourceLink, determinism, trim/AOT, license, vulnerabilities, age, risk), the unsafe/PInvoke surface, and SourceLink provenance.
---

# dotnet-inspect: dependency audit

Use this skill to decide whether to trust and adopt a package or library: its
provenance, build quality, safety surface, and supply-chain risk. These are
observations, not verdicts — `Signals` reports what is true, and you judge.

```bash
dnx dotnet-inspect -y -- <command>
```

## The Signals rollup

`-S Signals` on `package` or `library` is the one-stop audit. It reports
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

Distinct from *fetching* source (the `sourcelink` skill), these audit whether
debuggable source provenance exists and holds up: `SourceLink Availability` (is
it wired up), `SourceLink Integrity` (do the documents validate), and `SourceLink
Missing Files` (gaps).

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink Availability,SourceLink Integrity"
```
