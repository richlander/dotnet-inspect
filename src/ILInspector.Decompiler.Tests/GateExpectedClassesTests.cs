using System.Reflection;
using ILInspector.Decompiler.Tests.Gating;
using Xunit.v3;

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
    /// Requires every test in the <c>pre-merge</c> gate classes to be a plain <c>[Fact]</c>,
    /// and proves the preset names classes that exist.
    ///
    /// <para>The CI completeness check compares the run against a <c>-list methods/json</c>
    /// discovery listing, which is method-granular: a method that expands to five cases
    /// is listed once, so a run that lost four of them would still satisfy the check.
    /// <c>-preEnumerateTheories -list full/json</c> lists one entry per case with a stable
    /// unique ID, but only for theories whose data is serializable; others collapse to one
    /// delayed-enumeration entry per method, so case IDs are not a free upgrade. Until the
    /// checker is taught a validated case-level expectation, method granularity is
    /// sufficient only while every gate test is exactly one case. This test is what makes
    /// that true instead of assumed.</para>
    ///
    /// <para>It is an allow list, not a deny list. Rejecting only <c>[Theory]</c> would miss
    /// <c>[CulturedFact]</c>, which derives from <see cref="FactAttribute"/> rather than
    /// <c>TheoryAttribute</c> and still yields one case per culture, and would miss any
    /// future multi-case attribute. Requiring the attribute to be exactly
    /// <see cref="FactAttribute"/> fails closed on all of them.</para>
    ///
    /// <para>The scan is anchored on <see cref="IFactAttribute"/> rather than on
    /// <see cref="FactAttribute"/> because that is the abstraction xUnit itself keys on:
    /// <c>ExtensibilityPointFactory.GetMethodFactAttributes</c> returns
    /// <c>IReadOnlyCollection&lt;IFactAttribute&gt;</c>. An attribute may implement that
    /// interface directly, without deriving from <see cref="FactAttribute"/>, and supply a
    /// discoverer that emits several cases. Anchoring on the class would discover such an
    /// attribute in xUnit and miss it here, which is the same mistake as a deny list in a
    /// different disguise.</para>
    ///
    /// <para>The method surface scanned here mirrors what xUnit itself discovers —
    /// inherited and non-public methods, plus interface declarations — because a theory
    /// inherited from a base class is discovered and run exactly like a declared one.
    /// The interface arm is load-bearing and measured, not defensive: a <c>[Fact]</c> on a
    /// default interface method runs on the implementing class, and a <c>[Fact]</c> on an
    /// interface method *declaration* runs in addition to the one on the implementation,
    /// producing two cases from two perfectly ordinary <see cref="FactAttribute"/>s.
    /// That last shape is why multiplicity is counted per signature rather than judged
    /// per attribute: both attributes are plain, and only their number is wrong.</para>
    ///
    /// <para>Resolving each class by name also catches a preset arm naming a renamed or
    /// deleted class. That matters because a <c>-class</c> filter matching nothing runs
    /// zero tests and exits 0, so the mistake is otherwise invisible.</para>
    /// </summary>
    [Fact]
    public void PreMergeGateClasses_ContainOnlyPlainFacts()
    {
        var classNames = PreMergeClassNames();

        Assert.NotEmpty(classNames);

        const BindingFlags Surface =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string className in classNames)
        {
            Type type = typeof(Program).Assembly.GetType(className)
                ?? throw new InvalidOperationException(
                    $"The pre-merge preset names '{className}', which does not exist in the test "
                        + "assembly. A -class filter matching nothing runs zero tests and exits 0, "
                        + "so this would silently un-gate that class.");

            IEnumerable<MethodInfo> methods = type.GetMethods(Surface)
                .Concat(type.GetInterfaces().SelectMany(i => i.GetMethods(Surface)));

            // Keyed by signature, not by MethodInfo: a [Fact] on an interface
            // method declaration and a [Fact] on its implementation are two
            // distinct MethodInfos, and xUnit runs the method twice. Counting
            // per signature is what makes that visible.
            var factsBySignature = new Dictionary<string, List<IFactAttribute>>(StringComparer.Ordinal);

            foreach (MethodInfo method in methods)
            {
                foreach (IFactAttribute fact in method
                    .GetCustomAttributes(inherit: true)
                    .OfType<IFactAttribute>())
                {
                    string signature = method.Name
                        + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName)) + ")";

                    if (!factsBySignature.TryGetValue(signature, out List<IFactAttribute>? facts))
                        factsBySignature[signature] = facts = [];

                    facts.Add(fact);
                }
            }

            foreach ((string signature, List<IFactAttribute> facts) in factsBySignature)
            {
                string name = signature[..signature.IndexOf('(', StringComparison.Ordinal)];

                foreach (IFactAttribute fact in facts.Where(f => f.GetType() != typeof(FactAttribute)))
                    offenders.Add($"{className}.{name} [{fact.GetType().Name}]");

                if (facts.Count > 1)
                    offenders.Add($"{className}.{name} [{facts.Count} fact attributes on one method]");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The pre-merge gate completeness check is method-granular because its discovery "
                + "pass runs '-list methods/json', so a test that expands to several cases and "
                + "silently loses some would still satisfy it. Gate classes must contain only "
                + "plain [Fact] tests. Either make these facts, or teach "
                + "eng/check-decompiler-gate.cs a case-level expectation first -- note that "
                + "'-preEnumerateTheories -list full/json' expands only theories whose data is "
                + "serializable, and collapses the rest to one entry per method, so it must be "
                + "validated against what the run produced rather than trusted:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders.Select(o => "  " + o)));
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
