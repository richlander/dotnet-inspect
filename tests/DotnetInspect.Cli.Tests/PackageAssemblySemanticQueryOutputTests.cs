using System.IO.Compression;
using System.Text;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PackageAssemblySemanticQueryOutputTests
{
    private const string Framework = "net11.0";
    private const string Marker = "shared-literal-use-marker";
    private const string Version = "1.0.0";

    [Fact]
    public async Task PathologicalPopulationRendersPackageResultsAndKeepsTypedEvidence()
    {
        string[] packageIds =
        [
            "Contoso.Match.First",
            "Contoso.Miss",
            "Contoso.NoAssets",
            "Contoso.Broken",
            "Contoso.Match.Last",
        ];
        await using var fixture = new SemanticQueryFixture();
        await fixture.CacheAssemblyAsync(packageIds[0], MatchImage());
        await fixture.CacheAssemblyAsync(
            packageIds[1],
            await File.ReadAllBytesAsync(
                typeof(PackageQueryCommand).Assembly.Location,
                TestContext.Current.CancellationToken));
        await fixture.CacheAsync(packageIds[2]);
        await fixture.CacheAssemblyAsync(
            packageIds[3],
            "not an assembly"u8.ToArray());
        await fixture.CacheAssemblyAsync(packageIds[4], MatchImage());
        PackageSourceOperationLease operation = fixture.IssueOperation();
        PackageAcquisitionPopulation population =
            await operation.ResolvePinnedPopulationAsync(
                new FixedAuthorization(fixture.Authorization),
                packageIds.Select(
                    packageId =>
                        PackageSourceCoordinate.Create(
                            packageId,
                            Version))
                    .ToArray());

        InspectionEnvelope<PackageAssemblySemanticQueryDocument> envelope =
            await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken);
        PackageAssemblySemanticQueryDocument document = envelope.Content;

        Assert.Equal(5, document.CandidateCount);
        Assert.Equal(2, document.MatchedPackageCount);
        Assert.Equal(1, document.SemanticMissCount);
        Assert.Equal(1, document.NotApplicableCount);
        Assert.Equal(1, document.FailureCount);
        Assert.Equal([1, 5], document.Results.Select(
            result => result.CandidateOrdinal));
        Assert.All(
            document.Results,
            result => Assert.True(result.Occurrences.Length > 1));

        RowSelectionIntent<string> tail =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Tail(1)]);
        Assert.True(
            CliSemanticRowSelection.TrySelect(
                tail,
                document.Results,
                "package",
                _ => "selection failed",
                out IReadOnlyList<PackageAssemblySemanticQueryResult>
                    selected));
        PackageAssemblySemanticQueryResult result = Assert.Single(selected);
        Assert.Equal(5, result.CandidateOrdinal);

        PackageAssemblySemanticQueryView view =
            PackageAssemblySemanticQuerySections.CreateDocument(
                Marker,
                Framework,
                selected,
                document);
        PackageAssemblySemanticQueryRow row = Assert.Single(view.Results);
        Assert.Equal("contoso.match.last", row.Package);
        Assert.Equal(
            $"lib/{Framework}/Contoso.Match.Last.dll",
            row.Library);
        Assert.Equal(result.Occurrences.Length, row.Occurrences);
        Assert.Contains("0x", row.Evidence, StringComparison.Ordinal);
        Assert.Contains("/IL_", row.Evidence, StringComparison.Ordinal);
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                row.Root,
                out var decoded));
        Assert.Equal(result.RootRequest, decoded);

        PackageQueryOptions options = Options();
        var markdown = await ConsoleCapture.RunAsync(() =>
        {
            PackageQueryCommand.WriteLibraryLiteralOutput(view, options);
            return Task.FromResult(0);
        });
        Assert.Contains("## Packages", markdown.Output, StringComparison.Ordinal);
        Assert.Contains(row.Root, markdown.Output, StringComparison.Ordinal);

        var count = await ConsoleCapture.RunAsync(() =>
        {
            PackageQueryCommand.WriteLibraryLiteralOutput(
                view,
                options with { Count = true });
            return Task.FromResult(0);
        });
        Assert.Equal("1", count.Output.Trim());

        var incompleteCount = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(
                PackageQueryCommand.CompleteLibraryLiteralExecution(
                    options with { Count = true },
                    options.LibraryLiteralPlan!,
                    document)));
        Assert.Equal(1, incompleteCount.ExitCode);
        Assert.Empty(incompleteCount.Output);
        Assert.Contains(
            "Cannot count Package Query rows",
            incompleteCount.Error,
            StringComparison.Ordinal);

        var qualifiedCount = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(
                PackageQueryCommand.CompleteLibraryLiteralExecution(
                    options with
                    {
                        RowSelection = tail,
                        Count = true,
                    },
                    options.LibraryLiteralPlan!,
                    document)));
        Assert.Equal(0, qualifiedCount.ExitCode);
        Assert.Equal("1", qualifiedCount.Output.Trim());
        Assert.Contains(
            "Contoso.Broken",
            qualifiedCount.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    private static PackageQueryOptions Options()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                [],
                nuspecOnly: false,
                take: 5,
                rowSelection: null,
                includePrerelease: false,
                libraryLiteral: Marker,
                targetFramework: Framework,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());
        return options!;
    }

    private static PackageAssemblySemanticFindRequest Request(
        PackageAcquisitionPopulation population) =>
        new(
            population,
            PackageHouseTargetContext.Exact(Framework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                Marker));

    private static byte[] MatchImage() =>
        File.ReadAllBytes(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath());

    private sealed class FixedAuthorization(
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            authorization;
    }

    private sealed class SemanticQueryFixture : IAsyncDisposable
    {
        internal PackageSourceAuthorization Authorization { get; } =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);

        private IPackageSourceClient Source { get; }

        private PackageSourceSettlementLease Settlement { get; }

        private InMemoryPackageStore Store { get; } = new();

        internal PackagePayloadAcquisitionPlan PayloadAcquisition { get; }

        internal SemanticQueryFixture()
        {
            Source = PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                Authorization.Authorities[0].Association,
                factory => new MissingPayloadSource(factory));
            Settlement = PackageSourceSettlementService.IssueLease(
                _ => Source);
            PayloadAcquisition = new PackagePayloadAcquisitionPlan(
                (_, _) => Store);
        }

        internal PackageSourceOperationLease IssueOperation() =>
            Settlement.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);

        internal Task CacheAssemblyAsync(
            string packageId,
            byte[] image) =>
            CacheAsync(
                packageId,
                ($"lib/{Framework}/{packageId}.dll", image));

        internal async Task CacheAsync(
            string packageId,
            params (string Path, byte[] Content)[] entries)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(
                buffer,
                ZipArchiveMode.Create,
                leaveOpen: true))
            {
                Write(
                    archive,
                    $"{packageId}.nuspec",
                    Encoding.UTF8.GetBytes(
                        $"<package><metadata><id>{packageId}</id><version>{Version}</version></metadata></package>"));
                foreach ((string path, byte[] content) in entries)
                    Write(archive, path, content);
            }

            await Store.CommitAsync(
                packageId,
                Version,
                Source.Source.Producer.Key,
                new MemoryStream(
                    buffer.ToArray(),
                    writable: false),
                TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Settlement.DisposeAsync();
            Source.Dispose();
        }

        private static void Write(
            ZipArchive archive,
            string path,
            byte[] content)
        {
            using Stream entry = archive.CreateEntry(path).Open();
            entry.Write(content);
        }
    }

    private sealed class MissingPayloadSource(
        PackageSourceResultFactory results)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.PackagePayload;

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            Task.FromResult(
                results.FailedPackage(
                    PackageSourceCoordinate.Create(packageId, version),
                    PackageSourceFailureKind.NotFound));

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
