using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Locks the parallel full-build invariant: <see cref="Analysis.LibraryBodyIndex.Open(string)"/> analyzes
/// method bodies concurrently for assemblies above the parallel threshold, then merges per-method results
/// back in metadata order. The merge must be order-stable and race-free, so repeated opens of the same
/// assembly must produce byte-identical ordered output for every order-sensitive collection (methods,
/// direct calls, unsafe evidence, optimization opportunities, diagnostics). A residual data race would
/// surface here as nondeterministic ordering or counts across repeats.
///
/// Equivalence to the previous sequential build is proven separately by whole-CLI byte-diff in the PR;
/// this test is the ongoing regression guard against concurrency-induced nondeterminism.
/// </summary>
public class ParallelBuildDeterminismTests
{
    // The Analysis assembly itself has thousands of methods, so Open takes the parallel path.
    static string LargeAssemblyPath => typeof(Analysis.LibraryBodyIndex).Assembly.Location;

    static string Signature(Analysis.LibraryBodyIndex idx) => string.Join("\n", new[]
    {
        "M:" + string.Join(";", idx.Methods.Select(m => m.MetadataToken)),
        "C:" + string.Join(";", idx.DirectCalls.Select(c => c.ToString())),
        "U:" + string.Join(";", idx.UnsafeEvidence.Select(e => e.ToString())),
        "O:" + string.Join(";", idx.OptimizationOpportunities.Select(o => o.ToString())),
        "D:" + string.Join(";", idx.Diagnostics.Select(d => d.ToString())),
    });

    [Fact]
    public void ParallelBuild_IsOrderStable_AcrossRepeatedOpens()
    {
        var reference = Signature(Analysis.LibraryBodyIndex.Open(LargeAssemblyPath));
        Assert.Contains("M:", reference);

        // Repeat several times; any race in the concurrent per-method analysis or the ordered merge
        // would produce a differing signature on at least one iteration.
        for (int i = 0; i < 6; i++)
            Assert.Equal(reference, Signature(Analysis.LibraryBodyIndex.Open(LargeAssemblyPath)));
    }
}
