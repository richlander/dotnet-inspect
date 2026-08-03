using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// Gate for <see cref="TestAssemblySourceLink"/>.
/// </summary>
/// <remarks>
/// The helper's value is that it speaks exactly when SourceLink is missing and stays silent
/// otherwise, and both halves fail silently on their own. A probe stuck at "available" never
/// fires, retiring the diagnostic and restoring issue #3658's misdiagnosis; one stuck at
/// "unavailable" smears an irrelevant note across unrelated failures. Nothing else in the
/// suite would notice either way, so asserting only the available branch would leave the
/// always-available mutation alive — which is the one that costs an investigation.
///
/// These tests therefore drive <see cref="TestAssemblySourceLink.DescribeUnavailability"/>
/// over the real available assembly and over two constructed unavailable ones. The remaining
/// state, a PDB that matches but carries no blob, is a property of the build rather than of
/// the run: reproduce it with <c>/p:EnableSourceLink=false</c>, which is how this PR's
/// end-to-end evidence was gathered.
/// </remarks>
public class TestAssemblySourceLinkTests
{
    private static string TestAssemblyPath => typeof(TestAssemblySourceLinkTests).Assembly.Location;

    [Fact]
    public void NormalBuild_StampsSourceLink_SoTheHintStaysSilent()
    {
        Assert.True(
            TestAssemblySourceLink.IsAvailable,
            "The test assembly's PDB should carry SourceLink in a normal build, but the probe "
            + $"reported: {TestAssemblySourceLink.UnavailableReason}."
            + TestAssemblySourceLink.FailureHint);

        Assert.Null(TestAssemblySourceLink.UnavailableReason);
        Assert.Empty(TestAssemblySourceLink.FailureHint);
    }

    [Fact]
    public void AssemblyWithNoAdjacentPdb_IsReportedUnavailable()
    {
        var directory = CreateTempDirectory();
        try
        {
            var copy = Path.Combine(directory, Path.GetFileName(TestAssemblyPath));
            File.Copy(TestAssemblyPath, copy);

            var reason = TestAssemblySourceLink.DescribeUnavailability(copy);

            Assert.NotNull(reason);
            Assert.Contains("no PDB", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AssemblyWithForeignPdb_IsReportedUnavailable_EvenThoughThatPdbHasSourceLink()
    {
        // The regression this pins: a probe that reads the adjacent PDB without validating its
        // identity accepts another assembly's SourceLink map and goes silent, while the product
        // rejects that PDB and produces no rows — the hint disappearing exactly when needed.
        var foreignPdb = Path.ChangeExtension(typeof(ILInspector.Metadata.PdbContext).Assembly.Location, ".pdb");
        Assert.SkipUnless(File.Exists(foreignPdb), $"No foreign PDB available at {foreignPdb}.");

        var directory = CreateTempDirectory();
        try
        {
            var copy = Path.Combine(directory, Path.GetFileName(TestAssemblyPath));
            File.Copy(TestAssemblyPath, copy);
            File.Copy(foreignPdb, Path.ChangeExtension(copy, ".pdb"));

            var reason = TestAssemblySourceLink.DescribeUnavailability(copy);

            Assert.NotNull(reason);
            Assert.Contains("not its own", reason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sourcelink-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
