#!/usr/bin/env python3
"""End-to-end NativeAOT base/head timing of `library ... -D` (bare discovery,
which runs unsafe-evidence presence). Round-robin interleaves binaries per
sample so drift affects each equally. Output is hashed for identity."""
import hashlib, statistics, subprocess, sys, time, os

E2E = os.path.dirname(os.path.abspath(__file__))
BINS = sys.argv[1].split(",")          # e.g. base,s1,s2,s3
SAMPLES = int(sys.argv[2]) if len(sys.argv) > 2 else 20
WARMUP = 3
SCENARIOS = [
    ("System.CommandLine", ["library", "--package", "System.CommandLine@2.0.0", "--namesake-library", "-D"]),
    ("Newtonsoft.Json", ["library", "--package", "Newtonsoft.Json@13.0.3", "--namesake-library", "-D"]),
    ("Mono.Cecil", ["library", "--package", "Mono.Cecil@0.11.6", "--namesake-library", "-D"]),
    ("Roslyn C#", ["library", "--package", "Microsoft.CodeAnalysis.CSharp@4.14.0", "--namesake-library", "-D"]),
    ("CoreLib", ["library", "System.Private.CoreLib", "-D"]),
]

def run(b, args):
    exe = os.path.join(E2E, f"bin-{b}", "dotnet-inspect")
    t = time.perf_counter()
    p = subprocess.run([exe, *args], capture_output=True, cwd="/private/tmp")
    ms = (time.perf_counter() - t) * 1000
    out = p.stdout + p.stderr
    # temp extraction paths differ per run; hash with them removed
    norm = b"\n".join(l for l in out.splitlines() if b"inspect-pkg" not in l)
    unsafe = b"Unsafe Members" in out
    err = b"Could not determine Unsafe" in out
    return ms, hashlib.sha256(norm).hexdigest()[:12], p.returncode, ("incomplete" if err else str(unsafe).lower())

print("scenario\tbin\tanswer\texit\thash\tn\tmedian_ms\tp95_ms\tmin_ms")
for name, args in SCENARIOS:
    for _ in range(WARMUP):
        for b in BINS:
            run(b, args)
    samples = {b: [] for b in BINS}
    meta = {}
    for i in range(SAMPLES):
        order = BINS if i % 2 == 0 else list(reversed(BINS))
        for b in order:
            ms, h, rc, ans = run(b, args)
            samples[b].append(ms)
            meta.setdefault(b, set()).add((ans, rc, h))
    for b in BINS:
        s = sorted(samples[b])
        p95 = s[min(len(s) - 1, round(0.95 * (len(s) - 1)))]
        m = sorted(meta[b])
        ans = ";".join(sorted({x[0] for x in m})); rc = ";".join(sorted({str(x[1]) for x in m})); h = ";".join(sorted({x[2] for x in m}))
        print(f"{name}\t{b}\t{ans}\t{rc}\t{h}\t{len(s)}\t{statistics.median(s):.1f}\t{p95:.1f}\t{s[0]:.1f}", flush=True)
