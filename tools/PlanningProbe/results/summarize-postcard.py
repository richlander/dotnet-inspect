#!/usr/bin/env python3
"""Summarize a bench-postcard.sh TSV: cell = median of round medians; ratios
divide by NLinq measured by the same binary in the same run.

usage: summarize-postcard.py <machine> <tsv> [<tsv> ...]  (prints markdown)
"""
import math, statistics, sys
from collections import defaultdict

CLOSINGS = [("exists", "Exists"), ("count", "Count"), ("head", "Head(6)"),
            ("tail", "Tail(6)"), ("rows", "Rows"), ("window", "Rows(100..110)")]
IMPLS = ["before", "nlinq", "after"]
ASMS = {"CommandLine.dll": "CommandLine", "Humanizer.dll": "Humanizer",
        "Mono.Cecil.dll": "Mono.Cecil", "Newtonsoft.Json.dll": "Newtonsoft.Json",
        "System.Text.Json.dll": "System.Text.Json", "NuGet.Packaging.dll": "NuGet.Packaging",
        "Microsoft.CodeAnalysis.CSharp.dll": "Roslyn C#", "System.Private.CoreLib.dll": "CoreLib"}

machine = sys.argv[1]
rounds = defaultdict(list)   # (closing, impl, asm) -> [median]
answers = defaultdict(set)   # (closing, asm) -> answers
alloc = defaultdict(list)
loads = []
for path in sys.argv[2:]:
    for line in open(path):
        f = line.rstrip("\n").split("\t")
        variant, rnd, load, asm, answer, n, med, p10, p90, al = f
        _, closing, impl = variant.split(":")
        rounds[(closing, impl, asm)].append(float(med))
        answers[(closing, asm)].add(answer)
        alloc[(closing, impl, asm)].append(int(al))
        loads.append(float(load))

cell = {k: statistics.median(v) for k, v in rounds.items()}
disagree = [(c, a, s) for (c, a), s in answers.items() if len(s) != 1]

def fmt_ms(ms):
    return f"{ms * 1000:.1f} µs" if ms < 1 else f"{ms:.2f} ms"

def geo(xs):
    return math.exp(sum(math.log(x) for x in xs) / len(xs))

print(f"**{machine}** (load average {min(loads):.1f} to {max(loads):.1f} during the run)\n")
print("| Closing | Before | NLinq | After |")
print("| --- | ---: | ---: | ---: |")
for key, label in CLOSINGS:
    parts = []
    for impl in IMPLS:
        rs = [cell[(key, impl, a)] / cell[(key, "nlinq", a)] for a in ASMS]
        parts.append("1.00×" if impl == "nlinq" else f"{geo(rs):.2f}× ({min(rs):.2f}–{max(rs):.2f})")
    print(f"| {label} | " + " | ".join(parts) + " |")
print()
print(f"<!-- disagreements: {len(disagree)} {disagree} -->")

# Detail table: absolute medians
print()
print("| Assembly | Closing | Before | NLinq | After |")
print("| --- | --- | ---: | ---: | ---: |")
for a, name in ASMS.items():
    for key, label in CLOSINGS:
        print(f"| {name} | {label} | " + " | ".join(fmt_ms(cell[(key, i, a)]) for i in IMPLS) + " |")
