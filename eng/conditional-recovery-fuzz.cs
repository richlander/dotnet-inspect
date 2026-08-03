#:project ../tests/DotnetInspector.CSharpBodySlicer.Tests/DotnetInspector.CSharpBodySlicer.Tests.csproj
#:property EnablePreviewFeatures=true
#:property NoWarn=CA2252

// Deep-run driver for the conditional-recovery differential fuzzer.
//
//     dotnet run eng/conditional-recovery-fuzz.cs -- [seed] [cases]
//
// The generator, the Roslyn oracle and the fair-case rule all live in
// ConditionalRecoveryFuzzTests, which is also what CI runs; this file only supplies a seed, a case
// count and a process exit code, so a deep sweep and the gate can never drift apart. Read that
// file's header before trusting a zero -- in particular what "fair case" excludes, and why a clean
// run is evidence rather than proof.
//
// CI runs five fixed seeds at 5,000 cases as a time budget. Use this to sweep a fresh seed, to
// reproduce a reported flag exactly, or to run the 20,000-case sweeps that justified the round-6
// fix (140,000 cases over seven seeds, 0 flags, against 3,146 flags on seed 12345 before it).
//
// A clean sweep is worth exactly as much as the generator's reach. Rounds 7 and 8 both found
// defects this fuzzer could not spell -- round 7 because every group sat at file scope, round 8
// because both branches of a group always carried the same grammar. Before citing a zero, read
// what the generator can actually emit.

using DotnetInspector.CSharpBodySlicer.Tests;

int seed = args.Length > 0 && int.TryParse(args[0], out var s) ? s : Environment.TickCount;
int cases = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 20000;
string mode = args.Length > 2 ? args[2] : "diff";

Console.WriteLine($"seed={seed} cases={cases} mode={mode}");

var (fair, flagged, report) = ConditionalRecoveryFuzzTests.Run(seed, cases, mode);

Console.WriteLine(report);
Console.WriteLine($"seed={seed} fair={fair} flagged={flagged}");

return flagged == 0 ? 0 : 1;
