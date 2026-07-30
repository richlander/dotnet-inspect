using System.Reflection;
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

    /// <summary>
    /// Forbids <c>[Theory]</c> in the <c>pre-merge</c> gate classes, and proves the preset
    /// names classes that exist.
    ///
    /// <para>The CI completeness check compares the run against a <c>-list methods</c>
    /// discovery listing, and no <c>-list</c> mode enumerates theory cases: a theory with
    /// five cases is listed once, so a run that lost four of them would still satisfy the
    /// check. Method granularity is sufficient only while the gate classes are fact-only.
    /// This test is what makes that true instead of assumed — adding a theory to a gate
    /// class fails here rather than silently weakening the gate.</para>
    ///
    /// <para>Resolving each class by name also catches a preset arm naming a renamed or
    /// deleted class. That matters because a <c>-class</c> filter matching nothing runs
    /// zero tests and exits 0, so the mistake is otherwise invisible.</para>
    /// </summary>
    [Fact]
    public void PreMergeGateClasses_DeclareNoTheories()
    {
        GatePreset preset = Assert.Single(Program.Presets, p => p.Name == "pre-merge");

        var classNames = preset.Args
            .Select((arg, i) => (arg, i))
            .Where(x => x.arg == "-class")
            .Select(x => preset.Args[x.i + 1])
            .ToList();

        Assert.NotEmpty(classNames);

        var theories = new List<string>();
        foreach (string className in classNames)
        {
            Type type = typeof(Program).Assembly.GetType(className)
                ?? throw new InvalidOperationException(
                    $"The pre-merge preset names '{className}', which does not exist in the test "
                        + "assembly. A -class filter matching nothing runs zero tests and exits 0, "
                        + "so this would silently un-gate that class.");

            theories.AddRange(
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttribute<TheoryAttribute>() is not null)
                    .Select(m => $"{className}.{m.Name}"));
        }

        Assert.True(
            theories.Count == 0,
            "The pre-merge gate completeness check is method-granular because xUnit's discovery "
                + "listing does not enumerate theory cases, so a theory that silently lost cases "
                + "would still satisfy it. Either make these facts, or teach "
                + "eng/check-decompiler-gate.cs a case-level expectation before adding them:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, theories.Order(StringComparer.Ordinal).Select(t => "  " + t)));
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
