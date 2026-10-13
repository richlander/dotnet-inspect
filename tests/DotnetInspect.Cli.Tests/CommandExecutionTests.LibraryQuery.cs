using System.Text.Json;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task LibraryQuery_DirectoryUsesConjunctiveReferenceTerms()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "A.dll"),
                "Alpha.Library",
                "System.Runtime",
                "Contoso.Dependency");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "B.dll"),
                "Beta.Library",
                "System.Runtime");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "C.dll"),
                "Gamma.Library",
                "Contoso.Dependency");

            var result = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=system.runtime",
                "--where",
                "references=CONTOSO.DEPENDENCY",
                "--table");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("Alpha.Library", result.Output);
            Assert.Contains("System.Runtime", result.Output);
            Assert.Contains("Contoso.Dependency", result.Output);
            Assert.DoesNotContain("Beta.Library", result.Output);
            Assert.DoesNotContain("Gamma.Library", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryQuery_DefaultCandidateCeilingIsAPartialView()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-ceiling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            for (int index = 0;
                index < LibraryQuery.DefaultMaximumCandidates;
                index++)
            {
                WriteReferenceFixtureAssembly(
                    Path.Combine(tempDir, $"L{index:D3}.dll"),
                    $"Library{index:D3}",
                    "System.Runtime");
            }

            // A population no larger than the default is evaluated
            // completely: the ceiling constrains nothing.
            var atBound = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--count");
            Assert.Equal(0, atBound.Exit);
            Assert.Equal(
                $"{LibraryQuery.DefaultMaximumCandidates}{Environment.NewLine}",
                atBound.Output);
            Assert.Empty(atBound.Error);

            WriteReferenceFixtureAssembly(
                Path.Combine(
                    tempDir,
                    $"L{LibraryQuery.DefaultMaximumCandidates:D3}.dll"),
                $"Library{LibraryQuery.DefaultMaximumCandidates:D3}",
                "System.Runtime");

            // A default ceiling the user did not set is a partial view: Rows
            // and Count print what was observed, warn, and exit nonzero
            // (cli-execution-bounds.md#disclosure).
            var rows = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--json");
            Assert.Equal(1, rows.Exit);
            using (JsonDocument document = JsonDocument.Parse(rows.Output))
            {
                Assert.Equal(
                    LibraryQuery.DefaultMaximumCandidates,
                    document.RootElement
                        .GetProperty("results")
                        .GetArrayLength());
            }
            Assert.Contains(
                "candidate limit reached",
                rows.Error,
                StringComparison.OrdinalIgnoreCase);

            var count = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--count");
            Assert.Equal(1, count.Exit);
            Assert.Equal(
                $"{LibraryQuery.DefaultMaximumCandidates}{Environment.NewLine}",
                count.Output);
            Assert.Contains(
                "candidate limit reached",
                count.Error,
                StringComparison.OrdinalIgnoreCase);

            // A lone Head stops evaluation at its Nth match inside the
            // default bound: the witnessed rows are the requested answer.
            var head = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "-n",
                "2",
                "--count");
            Assert.Equal(0, head.Exit);
            Assert.Equal("2" + Environment.NewLine, head.Output);
            Assert.Empty(head.Error);
            var headSummary = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "-n",
                "2",
                "-S",
                "Query Summary",
                "--json");
            Assert.Equal(0, headSummary.Exit);
            using (JsonDocument document =
                JsonDocument.Parse(headSummary.Output))
            {
                JsonElement summary = document.RootElement
                    .GetProperty("query_summary")[0];
                Assert.Equal("2", summary.GetProperty("scanned").GetString());
                Assert.Equal(
                    "Complete",
                    summary.GetProperty("completion").GetString());
            }

            // The same population under an explicit --take is the request's
            // scope: exact for that population, exit zero.
            var taken = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--take",
                $"{LibraryQuery.DefaultMaximumCandidates}",
                "--count");
            Assert.Equal(0, taken.Exit);
            Assert.Equal(count.Output, taken.Output);
            var takenSummary = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--take",
                $"{LibraryQuery.DefaultMaximumCandidates}",
                "-S",
                "Query Summary",
                "--json");
            Assert.Equal(0, takenSummary.Exit);
            Assert.Contains(
                "Complete (--take scope)",
                takenSummary.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "Library Query scope:",
                taken.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "candidate limit reached",
                taken.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryQuery_CandidateBoundPrecedesResultSelection()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-bound-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "A.dll"),
                "Alpha.Library",
                "System.Runtime");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "B.dll"),
                "Beta.Library",
                "System.Runtime");

            var boundedCount = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--take",
                "1",
                "--count");
            // An explicit --take is the request's scope: Count is exact for
            // that population, names it as the scope, and exits zero.
            Assert.Equal(0, boundedCount.Exit);
            Assert.Equal("1" + Environment.NewLine, boundedCount.Output);
            Assert.Contains(
                "Library Query scope: the first 1 of 2 Libraries (--take)",
                boundedCount.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "candidate limit reached",
                boundedCount.Error,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "Cannot count",
                boundedCount.Error);

            var closedHead = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--take",
                "1",
                "-n",
                "1",
                "--count");
            // The Head is witnessed by the first match, so evaluation stops
            // there and the bound constrains nothing.
            Assert.Equal(0, closedHead.Exit);
            Assert.Equal("1" + Environment.NewLine, closedHead.Output);
            Assert.Empty(closedHead.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryQuery_IncompleteReferencesRemainVisible()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-malformed-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteMalformedAssemblyReferenceNameAssembly(
                Path.Combine(tempDir, "Root.dll"));

            var result = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=Missing.Reference");

            Assert.Equal(1, result.Exit);
            Assert.Contains("Query Summary", result.Output);
            Assert.Contains(
                "IncompleteReferences",
                result.Error);
            Assert.Contains(
                "cannot be treated as absent",
                result.Error);

            var count = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=Missing.Reference",
                "--count");

            // The failed candidate leaves an observed zero, with the same
            // failure warning and exit status as Rows.
            Assert.Equal(1, count.Exit);
            Assert.Equal("0" + Environment.NewLine, count.Output);
            Assert.Contains("IncompleteReferences", count.Error);
            Assert.DoesNotContain("Cannot count", count.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryQuery_DirectoryNativeImagesAreNotPopulationMembers()
    {
        string mixedDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-mixed-test-{Guid.NewGuid():N}");
        string nativeDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-native-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mixedDir);
        Directory.CreateDirectory(nativeDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(mixedDir, "A.dll"),
                "Alpha.Library",
                "System.Runtime");
            WriteNativeImage(
                Path.Combine(mixedDir, "A.dll"),
                Path.Combine(mixedDir, "Native.dll"));
            WriteNativeImage(
                Path.Combine(mixedDir, "A.dll"),
                Path.Combine(nativeDir, "Native.dll"));

            var mixed = await RunAppAsync(
                "library",
                "query",
                mixedDir,
                "--where",
                "references=System.Runtime",
                "--count");
            Assert.Equal(0, mixed.Exit);
            Assert.Equal("1" + Environment.NewLine, mixed.Output);
            Assert.Empty(mixed.Error);

            // A directory with no managed library is a failed source: nothing
            // was evaluated, so Rows report the failure and Count is withheld.
            var native = await RunAppAsync(
                "library",
                "query",
                nativeDir,
                "--where",
                "references=System.Runtime");
            Assert.Equal(1, native.Exit);
            Assert.Contains("contains no managed libraries", native.Error);

            var nativeCount = await RunAppAsync(
                "library",
                "query",
                nativeDir,
                "--where",
                "references=System.Runtime",
                "--count");
            Assert.Equal(1, nativeCount.Exit);
            Assert.Empty(nativeCount.Output);
            Assert.Contains(
                "Cannot count Library Query rows because population formation "
                + "failed before any candidate was evaluated.",
                nativeCount.Error);
        }
        finally
        {
            Directory.Delete(mixedDir, recursive: true);
            Directory.Delete(nativeDir, recursive: true);
        }
    }

    /// <summary>Copies a managed image with its CLI header directory cleared.</summary>
    private static void WriteNativeImage(string managedAssembly, string destination)
    {
        byte[] image = File.ReadAllBytes(managedAssembly);
        int peHeader = BitConverter.ToInt32(image, 0x3C);
        int optionalHeader = peHeader + 4 + 20;
        bool pe32Plus = BitConverter.ToUInt16(image, optionalHeader) == 0x20B;
        int corHeaderDirectory = optionalHeader + (pe32Plus ? 112 : 96) + (14 * 8);
        Array.Clear(image, corHeaderDirectory, 8);
        File.WriteAllBytes(destination, image);
    }

    [Fact]
    public async Task LibraryQuery_DiscoveryRequiresNoPopulation()
    {
        var result = await RunAppAsync(
            "library",
            "query",
            "/missing/library-query",
            "-D",
            "--json");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Contains("\"name\":\"Libraries\"", result.Output);
        Assert.Contains("\"name\":\"Query Summary\"", result.Output);
    }

    [Fact]
    public async Task LibraryQuery_PlatformRuntimeUsesSameReferenceFacet()
    {
        var result = await RunAppAsync(
            "library",
            "query",
            "--platform",
            "runtime",
            "--where",
            "references=System.Runtime",
            "--take",
            "256",
            "-n",
            "1",
            "--json");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Contains("\"library\":", result.Output);
        Assert.Contains("\"System.Runtime\"", result.Output);
    }

    [Fact]
    public async Task LibraryQuery_RejectsParentInspectionOptions()
    {
        var result = await RunAppAsync(
            "library",
            "--package",
            "System.Text.Json",
            "query",
            ".");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--package is not available with library query",
            result.Error);
    }
}
