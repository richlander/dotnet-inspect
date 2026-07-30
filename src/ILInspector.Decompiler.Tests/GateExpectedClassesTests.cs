using ILInspector.Decompiler.Tests.Gating;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins <c>eng/decompiler-gate-expected-classes.txt</c> to the <c>pre-merge</c> preset.
///
/// <para>The CI checker requires every class in that file to have executed something,
/// which is what stops an incomplete report from passing trivially. That guarantee is
/// only worth as much as the file's accuracy, so the file is not maintained by hand
/// against the preset: this test asserts set equality in both directions, so a class
/// added to the preset without being added to the file fails, and so does a stale
/// entry left behind after a class is renamed or removed.</para>
///
/// <para>This is the gate that enforces the inventory. It is deliberately in the fast
/// lane: it must run on every PR, including PRs that never trigger the slow gates.</para>
/// </summary>
public class GateExpectedClassesTests
{
    [Fact]
    public void ExpectedClassesFile_MatchesThePreMergePresetExactly()
    {
        GatePreset preset = Assert.Single(Program.Presets, p => p.Name == "pre-merge");

        var fromPreset = preset.Args
            .Select((arg, i) => (arg, i))
            .Where(x => x.arg == "-class")
            .Select(x => preset.Args[x.i + 1])
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(fromPreset);

        string path = Path.Combine(RepoRoot(), "eng", "decompiler-gate-expected-classes.txt");
        var fromFile = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(fromPreset, fromFile);
    }

    // Fails rather than skips when the source tree is absent. A skip here would
    // silently retire the inventory's only enforcement, which is exactly the
    // failure mode the inventory exists to prevent.
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "eng", "decompiler-gate-expected-classes.txt")))
                return dir.FullName;

        throw new InvalidOperationException(
            "Could not locate the repository root from "
                + $"'{AppContext.BaseDirectory}'. This test must run from a source checkout.");
    }
}
