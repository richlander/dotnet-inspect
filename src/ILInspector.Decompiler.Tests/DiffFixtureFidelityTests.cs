using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
public class DiffFixtureFidelityTests
{
    static readonly string[] DiffFocusedMethods =
    [
        "ConstantValue",
        "MultipleHunks",
        "StringToken",
        "CallToken",
        "BranchTargetOffsetShift",
        "BranchRetarget",
        "AddsUnsafe",
    ];

    [Theory]
    [InlineData("DiffFixtures.V1")]
    [InlineData("DiffFixtures.V2")]
    public void DiffFocusedFixtures_StayCompileBackCheckable(string project)
    {
        var results = FidelityCheck.Evaluate(DiffFixturePath(project));
        foreach (string method in DiffFocusedMethods)
        {
            var matches = results.Where(result => result.Method == method).ToArray();
            Assert.True(matches.Length > 0, $"Expected fidelity check to evaluate {project}.{method}.");
            Assert.All(matches, result =>
                Assert.True(
                    result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
                    $"{project}.{method} regressed to {result.Status}: the paired diff fixture must remain decompiler compile-back checkable.\n"
                        + $"  original : {result.OriginalOpcodes}\n"
                        + $"  recompiled: {result.RecompiledOpcodes}\n"
                        + $"  detail   : {result.Detail}"));
        }
    }

    static string DiffFixturePath(string project)
    {
        var outputDirectory = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName, "..", "..", project, outputDirectory.Name, "DiffFixtureSample.dll"));
        Assert.True(File.Exists(path), $"Expected diff fixture assembly at {path}");
        return path;
    }
}
