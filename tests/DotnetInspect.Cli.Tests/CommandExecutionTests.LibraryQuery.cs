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
            // Count reports the observed rows with the same completion warning
            // and exit status as Rows.
            Assert.Equal(0, boundedCount.Exit);
            Assert.Equal("1" + Environment.NewLine, boundedCount.Output);
            Assert.Contains(
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
            Assert.Equal(0, closedHead.Exit);
            Assert.Equal("1" + Environment.NewLine, closedHead.Output);
            Assert.Contains(
                "candidate limit reached",
                closedHead.Error,
                StringComparison.OrdinalIgnoreCase);
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
