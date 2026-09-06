using System.Net;
using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class SelectedSourceDiffTests
{
    public SelectedSourceDiffTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SelectedSourceOnlyChange_IsVisibleWithoutLocalChanges(
        bool json,
        bool multipleSections)
    {
        var options = Options("Value") with
        {
            JsonOutput = json,
            TypeFilter = [],
            MemberFilter = ["SourceDiffFixture.Counter.Value"],
            Select = multipleSections
                ? ["Analysis Diff", "Implementation Diff"]
                : ["Implementation Diff"],
        };
        var local = DiffCommand.BuildImplementationDiff(
            [FixtureCatalog.SourceDiffPair.OldAssemblyPath()],
            [FixtureCatalog.SourceDiffPair.NewAssemblyPath()],
            options);
        Assert.True(local.IsEmpty);

        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DiffCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("PDB Source", output);
        Assert.DoesNotContain("No implementation differences detected", output);
        Assert.DoesNotContain("changed member", output);
        if (json)
        {
            using var document = JsonDocument.Parse(output);
            var rows = document.RootElement.GetProperty("implementation_diff").EnumerateArray().ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.Equal("PDB Source", row.GetProperty("mechanism").GetString()));
            Assert.Contains(rows, row => row.GetProperty("evidence").GetString()!.Contains("1 + 2", StringComparison.Ordinal));
            Assert.Contains(rows, row => row.GetProperty("evidence").GetString()!.Contains("=> 3", StringComparison.Ordinal));
        }
        else
        {
            string text = WebUtility.HtmlDecode(output);
            Assert.Contains("1 + 2", text);
            Assert.Contains("=> 3", text);
        }
    }

    [Theory]
    [InlineData("Unchanged", false)]
    [InlineData("Unchanged", true)]
    [InlineData("SameSource", false)]
    [InlineData("SameSource", true)]
    public async Task SelectedUnchangedSource_IsExplicitAndPreservesLocalEvidence(
        string member,
        bool json)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DiffCommand.ExecuteAsync(Options(member) with { JsonOutput = json }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("PDB Source", output);
        Assert.Contains("unchanged", output);
        Assert.DoesNotContain("changed member", output);
        if (member == "SameSource")
        {
            Assert.Contains("C#", output);
            Assert.Contains("IL", output);
        }
        if (json)
        {
            using var document = JsonDocument.Parse(output);
            var rows = document.RootElement.GetProperty("implementation_diff").EnumerateArray().ToArray();
            var source = Assert.Single(rows, row => row.GetProperty("mechanism").GetString() == "PDB Source");
            Assert.Equal("unchanged", source.GetProperty("change").GetString());
            Assert.Equal("Exact", source.GetProperty("difference").GetString());
            Assert.Equal(member == "SameSource", rows.Any(row => row.GetProperty("mechanism").GetString() == "C#"));
            Assert.Equal(member == "SameSource", rows.Any(row => row.GetProperty("mechanism").GetString() == "IL"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedReorderedSource_IsChangedRatherThanUnchanged(bool json)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DiffCommand.ExecuteAsync(Options("Reordered") with { JsonOutput = json }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("PDB Source", output);
        Assert.DoesNotContain("unchanged", output);
        Assert.Contains("removed", output);
        Assert.Contains("added", output);
        if (json)
        {
            using var document = JsonDocument.Parse(output);
            var sourceRows = document.RootElement.GetProperty("implementation_diff").EnumerateArray()
                .Where(row => row.GetProperty("mechanism").GetString() == "PDB Source").ToArray();
            Assert.Contains(sourceRows, row => row.GetProperty("change").GetString() == "removed");
            Assert.Contains(sourceRows, row => row.GetProperty("change").GetString() == "added");
            Assert.DoesNotContain(sourceRows, row => row.GetProperty("change").GetString() == "unchanged");
        }
    }

    [Theory]
    [InlineData("MovedBlock", false, false)]
    [InlineData("MovedBlock", true, false)]
    [InlineData("MovedBlock", true, true)]
    [InlineData("MovedBlockAndEdit", false, false)]
    [InlineData("MovedBlockAndEdit", true, false)]
    public async Task SelectedMovedBlock_RetainsSourceEvidence(
        string member,
        bool json,
        bool multipleSections)
    {
        var options = Options(member) with
        {
            JsonOutput = json,
            Select = multipleSections
                ? ["Analysis Diff", "Implementation Diff"]
                : ["Implementation Diff"],
        };
        var local = DiffCommand.BuildImplementationDiff(
            [FixtureCatalog.SourceDiffPair.OldAssemblyPath()],
            [FixtureCatalog.SourceDiffPair.NewAssemblyPath()],
            options);
        Assert.Equal(member == "MovedBlock", local.IsEmpty);

        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DiffCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain("unchanged", output);
        if (json)
        {
            using var document = JsonDocument.Parse(output);
            var sourceRows = document.RootElement.GetProperty("implementation_diff").EnumerateArray()
                .Where(row => row.GetProperty("mechanism").GetString() == "PDB Source").ToArray();
            var moved = sourceRows.Where(row => row.GetProperty("change").GetString() == "moved").ToArray();
            Assert.Equal(2, moved.Length);
            Assert.Equal("declaration line 3 -> 5:     // First annotation.",
                moved[0].GetProperty("evidence").GetString());
            Assert.Equal("declaration line 4 -> 6:     // Second annotation.",
                moved[1].GetProperty("evidence").GetString());
            Assert.All(moved, row =>
            {
                Assert.Equal("Moved", row.GetProperty("difference").GetString());
            });
            if (member == "MovedBlock")
                Assert.Equal(moved.Length, sourceRows.Length);
            else
            {
                Assert.Contains(sourceRows, row => row.GetProperty("change").GetString() == "removed");
                Assert.Contains(sourceRows, row => row.GetProperty("change").GetString() == "added");
            }
        }
        else
        {
            string text = WebUtility.HtmlDecode(output);
            Assert.Contains("PDB Source", text);
            Assert.Contains("Moved", text);
            Assert.Contains("moved", text);
            Assert.Contains("declaration line", text);
            Assert.Contains("->", text);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSelectedEndpoint_IsUnavailableNotSourceRemoval(bool json)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DiffCommand.ExecuteAsync(Options("BeforeOnly") with { JsonOutput = json }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("TargetNotFound", output);
        Assert.Contains("unavailable", output);
        if (json)
        {
            using var document = JsonDocument.Parse(output);
            var source = Assert.Single(
                document.RootElement.GetProperty("implementation_diff").EnumerateArray(),
                row => row.GetProperty("mechanism").GetString() == "PDB Source");
            Assert.Equal("unavailable", source.GetProperty("change").GetString());
            Assert.Contains("old: complete", source.GetProperty("evidence").GetString());
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "unavailable", "SourceAcquisitionUnavailable")]
    [InlineData(HttpStatusCode.BadRequest, "failed", "SourceAcquisitionFailed")]
    public async Task AcquisitionNonSuccess_RendersTypedEndpointReasonWithoutLineEdits(
        HttpStatusCode status,
        string change,
        string outcome)
    {
        string beforePath = FixtureCatalog.SourceDiffPair.OldAssemblyPath();
        string afterPath = FixtureCatalog.SourceDiffPair.NewAssemblyPath();
        using var workspace = new InspectionWorkspace();
        var before = Participant(beforePath, "1.0.0");
        var after = Participant(afterPath, "2.0.0");
        using var beforeGroup = workspace.CreateAssemblyContextGroup([before]);
        using var afterGroup = workspace.CreateAssemblyContextGroup([after]);
        using var client = new HttpClient(new SourceHandler(
            status,
            File.ReadAllBytes(Path.Combine(FixtureCatalog.SourceDiffV1.ProjectDirectory(), "Counter.cs")),
            SymbolPackage(beforePath),
            SymbolPackage(afterPath)));
        var context = new AssemblyContextSourceQueryContext(
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization([NuGetFetch.PackageSource.NuGetOrg]),
            new SourceFetcher(client, new InMemorySourceContentStore()));
        var surface = AssemblySetSurfaceBuilder.Build([beforePath])!;
        var type = Assert.Single(surface.Types, type => type.FullName == "SourceDiffFixture.Counter");
        var member = Assert.Single(type.Members, member => member.Name == "Value");

        var pair = await AssemblyContextMemberSourcePairQuery.ExecuteAsync(
            beforeGroup, before, afterGroup, after,
            AssemblyMemberSourcePairRequest.From(type, member),
            context,
            TestContext.Current.CancellationToken);
        var view = DiffOutputFormatter.BuildImplementationDiffView(
            "SourceDiffFixture",
            new ImplementationDiffResult([], new ResearchComparison([])),
            "v1", "v2", pair);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, pair.Status);
        Assert.Null(pair.Comparison);
        var row = Assert.Single(view.Rows!);
        Assert.Equal("PDB Source", row.Mechanism);
        Assert.Equal(change, row.Change);
        Assert.Equal("Unavailable", row.Difference);
        Assert.Contains(outcome, row.Evidence);
        Assert.Contains("old: complete", row.Evidence);
        Assert.Contains("new:", row.Evidence);
        Assert.DoesNotContain("changed member", view.Summary);
        Assert.DoesNotContain("No implementation differences", view.Summary);
    }

    [Fact]
    public async Task SelectedPackageSource_UsesResolvedPackageCoordinatesForSymbolAcquisition()
    {
        string originalBeforePath = FixtureCatalog.SourceDiffPair.OldAssemblyPath();
        string originalAfterPath = FixtureCatalog.SourceDiffPair.NewAssemblyPath();
        string cachePath = Path.GetFullPath(Path.Combine(
            "artifacts", "source-diff-test-cache", Guid.NewGuid().ToString("N")));
        NuGetCache.Initialize("dotnet-inspect", cachePath, skipNuGetCache: true);
        try
        {
            string beforePath = Path.Combine(cachePath, "before", "SourceDiffFixture.dll");
            string afterPath = Path.Combine(cachePath, "after", "SourceDiffFixture.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(beforePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(afterPath)!);
            File.Copy(originalBeforePath, beforePath);
            File.Copy(originalAfterPath, afterPath);
            Assert.False(File.Exists(Path.ChangeExtension(beforePath, ".pdb")));
            Assert.False(File.Exists(Path.ChangeExtension(afterPath, ".pdb")));
            var handler = new SourceHandler(
                HttpStatusCode.NotFound, [], SymbolPackage(originalBeforePath), SymbolPackage(originalAfterPath));
            using var client = new HttpClient(handler);
            var options = Options("Value") with
            {
                LibraryVersionRange = null,
                PackageVersionRange = "SourceDiff.Package@0.1.0..0.2.0",
                SourceOptions = new NuGetSourceOptions
                {
                    Sources = ["https://api.nuget.org/v3/index.json"]
                }
            };
            var local = DiffCommand.BuildImplementationDiff([beforePath], [afterPath], options);
            var result = await DiffCommand.BuildImplementationDiffWithSourceAsync(
                local, [beforePath], [afterPath], options, client, new VerboseLogger(false),
                fromEntry: new AssemblySetEntry(
                    beforePath, "SourceDiff.Package", "1.0.0", AssemblySetSourceKind.Package, "net11.0"),
                toEntry: new AssemblySetEntry(
                    afterPath, "SourceDiff.Package", "2.0.0", AssemblySetSourceKind.Package, "net11.0"));

            Assert.True(result.Local.IsEmpty);
            var pair = Assert.IsType<AssemblyMemberSourcePairResult>(result.SelectedSource);
            Assert.Equal(AssemblyMemberSourcePairStatus.Compared, pair.Status);
            Assert.False(pair.IsExact);
            var before = Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(pair.Before.Subject.Provenance);
            var after = Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(pair.After.Subject.Provenance);
            Assert.Equal("SourceDiff.Package", before.PackageId);
            Assert.Equal("SourceDiff.Package", after.PackageId);
            Assert.Equal("1.0.0", before.PackageVersion);
            Assert.Equal("2.0.0", after.PackageVersion);
            Assert.Equal("net11.0", before.Tfm);
            Assert.Equal("net11.0", after.Tfm);
            var requests = handler.Requests.Where(uri => uri.AbsolutePath.EndsWith(".snupkg")).ToArray();
            Assert.Contains(requests, uri => uri.AbsolutePath.EndsWith("/sourcediff.package.1.0.0.snupkg", StringComparison.Ordinal));
            Assert.Contains(requests, uri => uri.AbsolutePath.EndsWith("/sourcediff.package.2.0.0.snupkg", StringComparison.Ordinal));
            Assert.DoesNotContain(requests, uri => uri.AbsolutePath.Contains("sourcediff.package.0.", StringComparison.Ordinal));
            var view = DiffOutputFormatter.BuildImplementationDiffView(
                "SourceDiff.Package", result.Local, "1.0.0", "2.0.0", pair);
            Assert.Contains(view.Rows!, row => row.Mechanism == "PDB Source" && row.Evidence.Contains("1 + 2"));
        }
        finally
        {
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, recursive: true);
        }
    }

    [Fact]
    public async Task SelectedSource_RetainsIndependentlyResolvedPhysicalTokens()
    {
        using var client = new HttpClient(new SourceHandler(HttpStatusCode.NotFound, []));
        var result = await DiffCommand.BuildImplementationDiffWithSourceAsync(
            [FixtureCatalog.SourceDiffPair.OldAssemblyPath()],
            [FixtureCatalog.SourceDiffPair.NewAssemblyPath()],
            Options("Value") with { TypeFilter = ["SourceDiffFixture.MovedCounter"] },
            client,
            new VerboseLogger(false));

        Assert.True(result.Local.IsEmpty);
        var pair = Assert.IsType<AssemblyMemberSourcePairResult>(result.SelectedSource);
        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, pair.Status);
        Assert.False(pair.IsExact);
        var before = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(pair.Before);
        var after = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(pair.After);
        Assert.NotEqual(before.Request.MetadataToken, after.Request.MetadataToken);
        Assert.NotEqual(before.Subject.Registration, after.Subject.Registration);
    }

    [Fact]
    public async Task WithoutPdbSource_SameLocalImplementationStillHasNoRows()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DiffCommand.ExecuteAsync(Options("Value") with { IncludePdbSource = false }));
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("No implementation differences detected", output);
        Assert.DoesNotContain("PDB Source", output);
    }

    [Theory]
    [InlineData("property", "Value")]
    [InlineData("property", "Value:1")]
    [InlineData("property", "Value:2")]
    [InlineData("event", "Value:1")]
    [InlineData("event", "Value:2")]
    [InlineData("field", "Value")]
    public async Task BoundedSource_NonMethodAndAccessorSelectionsRetainLegacyRoute(
        string kind,
        string selector)
    {
        var surface = SelectionSurface(new ApiMember
        {
            Name = "Value",
            Kind = kind,
            Signature = "int Value",
            GetterToken = kind == "property" ? 0x06000001 : null,
            SetterToken = kind == "property" ? 0x06000002 : null,
            AdderToken = kind == "event" ? 0x06000001 : null,
            RemoverToken = kind == "event" ? 0x06000002 : null,
        });
        var local = new ImplementationDiffResult([], new ResearchComparison([]));
        var handler = new SourceHandler(HttpStatusCode.NotFound, []);
        using var client = new HttpClient(handler);

        var result = await DiffCommand.BuildImplementationDiffWithSourceAsync(
            local,
            [FixtureCatalog.SourceDiffPair.OldAssemblyPath()],
            [FixtureCatalog.SourceDiffPair.NewAssemblyPath()],
            Options(selector), client, new VerboseLogger(false), surface, surface);

        Assert.Same(local, result.Local);
        Assert.Null(result.SelectedSource);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BoundedSource_UnsupportedOldTargetDoesNotHideNewAmbiguity()
    {
        var before = SelectionSurface(new ApiMember { Name = "Value", Kind = "property" });
        var after = SelectionSurface(
            new ApiMember { Name = "Value", Kind = "method", Signature = "int Value()" },
            new ApiMember { Name = "Value", Kind = "method", Signature = "int Value(int value)" });
        using var client = new HttpClient(new SourceHandler(HttpStatusCode.NotFound, []));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DiffCommand.BuildImplementationDiffWithSourceAsync(
                new ImplementationDiffResult([], new ResearchComparison([])),
                [FixtureCatalog.SourceDiffPair.OldAssemblyPath()],
                [FixtureCatalog.SourceDiffPair.NewAssemblyPath()],
                Options("Value"), client, new VerboseLogger(false), before, after));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundedSource_EndpointReadFailureIsVisible(bool oldSide)
    {
        string before = FixtureCatalog.SourceDiffPair.OldAssemblyPath();
        string after = FixtureCatalog.SourceDiffPair.NewAssemblyPath();
        string missing = Path.Combine(FixtureCatalog.SourceDiffV1.ProjectDirectory(), "missing-source.dll");
        using var client = new HttpClient(new SourceHandler(HttpStatusCode.NotFound, []));

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            DiffCommand.BuildImplementationDiffWithSourceAsync(
                new ImplementationDiffResult([], new ResearchComparison([])),
                [oldSide ? missing : before],
                [oldSide ? after : missing],
                Options("Value"), client, new VerboseLogger(false)));

        Assert.Equal(missing, error.FileName);
    }

    static ApiSurface SelectionSurface(params ApiMember[] members) => new()
    {
        Types =
        [
            new ApiType
            {
                Namespace = "SourceDiffFixture",
                Name = "Counter",
                Kind = "class",
                Members = [.. members],
            }
        ]
    };

    static DiffOptions Options(string member) => new()
    {
        LibraryVersionRange =
            $"{FixtureCatalog.SourceDiffPair.OldAssemblyPath()}..{FixtureCatalog.SourceDiffPair.NewAssemblyPath()}",
        Select = ["Implementation Diff"],
        TypeFilter = ["SourceDiffFixture.Counter"],
        MemberFilter = [member],
        IncludePdbSource = true,
    };

    static AssemblyContextParticipant Participant(string path, string version)
        => new(
            ResolvedAssemblyReference.CreateFromPath(
                path, AssemblyResolutionProvenance.Package("SourceDiff.Package", version, "net11.0", rid: null)),
            new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path)));

    static byte[] SymbolPackage(string assemblyPath)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("lib/net11.0/SourceDiffFixture.pdb").Open();
            stream.Write(File.ReadAllBytes(Path.ChangeExtension(assemblyPath, ".pdb")));
        }
        return buffer.ToArray();
    }

    sealed class SourceHandler(
        HttpStatusCode status,
        byte[] beforeSource,
        byte[]? beforeSymbols = null,
        byte[]? afterSymbols = null) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            if (uri.AbsolutePath.EndsWith(".snupkg", StringComparison.Ordinal))
            {
                byte[]? symbols = uri.AbsolutePath.Contains("2.0.0", StringComparison.Ordinal)
                    ? afterSymbols
                    : beforeSymbols;
                if (symbols is not null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(symbols), RequestMessage = request
                    });
            }
            return Task.FromResult(uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(beforeSource), RequestMessage = request
                }
                : new HttpResponseMessage(status) { RequestMessage = request });
        }
    }
}
