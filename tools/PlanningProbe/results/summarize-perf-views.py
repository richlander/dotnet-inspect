#!/usr/bin/env python3
"""Three performance views from a five-column bench-postcard.sh TSV.
Cell = median of round medians; each view is a per-closing geometric mean
across the eight assemblies, with the min-max range.

usage: summarize-perf-views.py <view> <machine> <tsv>
  view: old | naive | linq | detail-dense | detail-async | load
Variants: pc:<closing>:<naive|linq|nlinq|after> (dense population),
          pa:<closing>:<old|nlinq|after> (async population).
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
    pop, closing, impl = variant.split(":")
    rounds[(closing, pop + ":" + impl, asm)].append(float(med))
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
    print("| Closing | Old | NLinq | Planner |")
    print("| --- | ---: | ---: | ---: |")
    for k, label in CLOSINGS:
        print(f"| {label} | {ratio(k, 'pa:old', 'pa:nlinq')} | 1.00× | {ratio(k, 'pa:after', 'pa:nlinq')} |")
elif view == "naive":
    print("| Closing | Naive Planner ÷ Planner |")
    print("| --- | ---: |")
    for k, label in CLOSINGS:
        print(f"| {label} | {ratio(k, 'pc:naive', 'pc:after')} |")
elif view == "linq":
    print("| Closing | LINQ | NLinq | Planner |")
    print("| --- | ---: | ---: | ---: |")
    for k, label in CLOSINGS:
        print(f"| {label} | {ratio(k, 'pc:linq', 'pc:nlinq')} | 1.00× | {ratio(k, 'pc:after', 'pc:nlinq')} |")
elif view in ("detail-dense", "detail-async"):
    cols = [("pc:naive", "Naive Planner"), ("pc:linq", "LINQ"), ("pc:nlinq", "NLinq"), ("pc:after", "Planner")] \
        if view == "detail-dense" else [("pa:old", "Old"), ("pa:nlinq", "NLinq"), ("pa:after", "Planner")]
    print("| Assembly | Closing | " + " | ".join(c[1] for c in cols) + " |")
    print("| --- | --- | " + " | ".join("---:" for _ in cols) + " |")
    for a, name in ASMS.items():
        for k, label in CLOSINGS:
            print(f"| {name} | {label} | " + " | ".join(fmt(cell[(k, c[0], a)]) for c in cols) + " |")
