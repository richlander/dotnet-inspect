#!/usr/bin/env python3
"""Three performance views from a five-column bench-postcard.sh TSV.
Cell = median of round medians; each view is a per-closing geometric mean
across the eight assemblies, with the min-max range.

usage: summarize-perf-views.py <view> <machine> <tsv>
  view: old | wrong | linq | detail | load
"""
import math, statistics, sys
from collections import defaultdict

CLOSINGS = [("exists", "Exists"), ("count", "Count"), ("head", "Head(6)"),
            ("tail", "Tail(6)"), ("rows", "Rows"), ("window", "Rows(100..110)")]
ASMS = {"CommandLine.dll": "CommandLine", "Humanizer.dll": "Humanizer",
        "Mono.Cecil.dll": "Mono.Cecil", "Newtonsoft.Json.dll": "Newtonsoft.Json",
        "System.Text.Json.dll": "System.Text.Json", "NuGet.Packaging.dll": "NuGet.Packaging",
        "Microsoft.CodeAnalysis.CSharp.dll": "Roslyn C#", "System.Private.CoreLib.dll": "CoreLib"}

view, machine, path = sys.argv[1], sys.argv[2], sys.argv[3]
rounds = defaultdict(list)
loads = []
for line in open(path):
    variant, rnd, load, asm, answer, n, med, p10, p90, al = line.rstrip("\n").split("\t")
    _, closing, impl = variant.split(":")
    rounds[(closing, impl, asm)].append(float(med))
    loads.append(float(load))
cell = {k: statistics.median(v) for k, v in rounds.items()}

def geo(xs):
    return math.exp(sum(math.log(x) for x in xs) / len(xs))

def ratio(key, num, den):
    rs = [cell[(key, num, a)] / cell[(key, den, a)] for a in ASMS]
    return f"{geo(rs):.2f}× ({min(rs):.2f}–{max(rs):.2f})"

def fmt(ms):
    return f"{ms * 1000:.1f} µs" if ms < 1 else f"{ms:.2f} ms"

if view == "load":
    print(f"{min(loads):.1f} to {max(loads):.1f}")
elif view == "old":
    print("| Closing | Old | NLinq | After |")
    print("| --- | ---: | ---: | ---: |")
    for k, label in CLOSINGS:
        print(f"| {label} | {ratio(k, 'old', 'nlinq')} | 1.00× | {ratio(k, 'after', 'nlinq')} |")
elif view == "wrong":
    print("| Closing | Materialize ÷ After |")
    print("| --- | ---: |")
    for k, label in CLOSINGS:
        print(f"| {label} | {ratio(k, 'mat', 'after')} |")
elif view == "linq":
    print("| Closing | LINQ | NLinq | After |")
    print("| --- | ---: | ---: | ---: |")
    for k, label in CLOSINGS:
        print(f"| {label} | {ratio(k, 'linq', 'nlinq')} | 1.00× | {ratio(k, 'after', 'nlinq')} |")
elif view == "detail":
    print("| Assembly | Closing | Old | Materialize | LINQ | NLinq | After |")
    print("| --- | --- | ---: | ---: | ---: | ---: | ---: |")
    for a, name in ASMS.items():
        for k, label in CLOSINGS:
            print(f"| {name} | {label} | " + " | ".join(fmt(cell[(k, i, a)]) for i in ["old", "mat", "linq", "nlinq", "after"]) + " |")
