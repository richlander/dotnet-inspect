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
        var fromPreset = PreMergeClassNames().ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(fromPreset);

        string path = Path.Combine(RepoRoot(), "eng", "decompiler-gate-expected-classes.txt");
        var fromFile = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(fromPreset, fromFile);
    }

    /// <summary>
    /// Serializes every workload class selected by <c>--gate pre-merge</c>.
    ///
    /// <para>The runner uses two parallel threads. The original raised and lowered
    /// fidelity gates already shared <see cref="FidelityGateCollection"/>, but newer
    /// compile-back gates did not, so two <c>FidelityCheck</c> workloads over the same
    /// fixture assembly could overlap. Run 30885078644 observed Roslyn throw from
    /// <c>CommonReferenceManager.ResolveReferencedAssembly</c>: the failing Printer test
    /// overlapped Cluster capture for 7m01s, then the lowered gate for its final 33s.
    /// The failed-job rerun passed.</para>
    ///
    /// <para>This test derives the workload set from the preset rather than maintaining
    /// another allow list. The only exception is this class itself: it is the fast
    /// plumbing guard that rides in the preset it validates and performs no compile-back
    /// work. Any future workload class added to the preset must join the collection
    /// explicitly, or this always-run guard fails before the slow lane can become
    /// intermittently concurrent again.</para>
    /// </summary>
    [Fact]
    public void PreMergeWorkloadClasses_ShareFidelityGateCollection()
    {
        string guardClass = typeof(GateExpectedClassesTests).FullName!;
        var workloads = PreMergeClassNames()
            .Where(className => className != guardClass)
            .ToList();

        Assert.NotEmpty(workloads);

        var offenders = new List<string>();
        foreach (string className in workloads)
        {
            Type type = typeof(Program).Assembly.GetType(className)
                ?? throw new InvalidOperationException(
                    $"The pre-merge preset names '{className}', which does not exist in the test assembly.");

            CollectionAttribute? collection = type
                .GetCustomAttributes<CollectionAttribute>(inherit: true)
                .SingleOrDefault();

            if (collection?.Name != FidelityGateCollection.Name)
            {
                string collectionName = collection?.Name is { Length: > 0 } name
                    ? name
                    : "no collection";
                offenders.Add(
                    $"{className} [{collectionName}]");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Every pre-merge workload class must use [Collection({nameof(FidelityGateCollection)}.Name)] "
                + "so compile-back gates cannot overlap under xUnit's parallel runner:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders.Select(o => "  " + o)));
    }

    private static List<string> PreMergeClassNames()
    {
        GatePreset preset = Assert.Single(Program.Presets, p => p.Name == "pre-merge");

        return preset.Args
            .Select((arg, i) => (arg, i))
            .Where(x => x.arg == "-class")
            .Select(x => preset.Args[x.i + 1])
            .ToList();
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
