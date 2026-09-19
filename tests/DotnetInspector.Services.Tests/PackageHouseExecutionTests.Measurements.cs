using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task PackageInfoEnvelopeRetainsMeasuredSelectionCorrespondence()
    {
        const string UnsafeFolder = "HOSTILE\u202EMARKER";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ($"lib/net10.0/{MaterializedPackageId}.dll", new byte[13]),
            ("build/net10.0/Package.targets", new byte[3]),
            ($"{UnsafeFolder}/net10.0/data.bin", new byte[5]),
            ("lib/net8.0/Legacy.dll", new byte[17]));
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");

        InspectionEnvelope<PackageInfoMeasurements> envelope =
            PackageInfoMeasurementInspection.Project(settlement);
        PackageInfoMeasurements measurements = envelope.Content;

        Assert.Equal(
            PackageInfoMeasurementStatus.Measured,
            measurements.Status);
        Assert.Equal(archive.LongLength, measurements.CompressedPackageBytes);
        Assert.Equal("net10.0", measurements.SelectedTargetFramework);
        Assert.Equal(
            ["net10.0", "net8.0"],
            measurements.AvailableTargetFrameworks!
                .Select(static framework => framework.ToString()));
        Assert.Equal(
            ["build", @"HOSTILE\u202EMARKER", "lib"],
            measurements.SelectedTargetFrameworkFolders!
                .Select(static folder => folder.ToString()));
        Assert.Equal(13, measurements.SelectedLibraryPayloadBytes);
        Assert.Equal(1, measurements.SelectedLibraryCount);
        Assert.Same(
            settlement.Payload.Content.GenerationIdentity,
            measurements.Generation);
        Assert.Same(
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                settlement.Result.Evidence.Realization).Receipt,
            measurements.SelectionReceipt);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
        string json = JsonSerializer.Serialize(
            envelope,
            PackageInfoMeasurementJsonContext.Default
                .InspectionEnvelopePackageInfoMeasurements);
        Assert.Contains(
            "\"availableTargetFrameworks\":[\"net10.0\",\"net8.0\"]",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"selectedTargetFrameworkFolders\":"
                + "[\"build\",\"HOSTILE\\\\u202EMARKER\",\"lib\"]",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', json);
        InspectionEnvelope<PackageInfoMeasurements> roundTripped =
            JsonSerializer.Deserialize(
                json,
                PackageInfoMeasurementJsonContext.Default
                    .InspectionEnvelopePackageInfoMeasurements)!;
        Assert.Equal(measurements.Status, roundTripped.Content.Status);
        Assert.Equal(
            measurements.SelectedLibraryPayloadBytes,
            roundTripped.Content.SelectedLibraryPayloadBytes);
        Assert.Equal(
            measurements.SelectedTargetFrameworkFolders,
            roundTripped.Content.SelectedTargetFrameworkFolders);
        Assert.Equal(
            measurements.AvailableTargetFrameworks,
            roundTripped.Content.AvailableTargetFrameworks);
        Assert.Null(roundTripped.Content.Evidence);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task PackageInfoEnvelopeKeepsNoCompileSlicesTyped()
    {
        byte[] archive = TestPackageArchive.Create("package/readme.txt");
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");

        InspectionEnvelope<PackageInfoMeasurements> envelope =
            PackageInfoMeasurementInspection.Project(settlement);
        PackageInfoMeasurements measurements = envelope.Content;

        Assert.Equal(
            PackageInfoMeasurementStatus.NoCompileSlices,
            measurements.Status);
        Assert.Equal(archive.LongLength, measurements.CompressedPackageBytes);
        Assert.Empty(measurements.AvailableTargetFrameworks!);
        Assert.Null(measurements.SelectedTargetFramework);
        Assert.Null(measurements.SelectedTargetFrameworkFolders);
        Assert.Null(measurements.SelectedLibraryPayloadBytes);
        Assert.Null(measurements.SelectedLibraryCount);
        Assert.NotNull(measurements.Detail);
        InspectionDiagnostic diagnostic = Assert.Single(envelope.Diagnostics);
        Assert.Equal(
            "package-info-measurements.no-compile-slices",
            diagnostic.Code);
        Assert.Equal(
            InspectionDiagnosticSeverity.Warning,
            diagnostic.Severity);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task PackageInfoEnvelopeContainsAvailableFrameworkIdentities()
    {
        const string UnsafeFramework = "net8.0\u202EHOSTILE";
        byte[] archive = TestPackageArchive.Create(
            $"lib/{UnsafeFramework}/{MaterializedPackageId}.dll");
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");

        InspectionEnvelope<PackageInfoMeasurements> envelope =
            PackageInfoMeasurementInspection.Project(settlement);
        PackageInfoMeasurements measurements = envelope.Content;

        Assert.Equal(
            PackageInfoMeasurementStatus.NoApplicableSlice,
            measurements.Status);
        Assert.Equal(
            [@"net8.0\u202EHOSTILE"],
            measurements.AvailableTargetFrameworks!
                .Select(static framework => framework.ToString()));
        string json = JsonSerializer.Serialize(
            envelope,
            PackageInfoMeasurementJsonContext.Default
                .InspectionEnvelopePackageInfoMeasurements);
        Assert.Contains(
            "\"availableTargetFrameworks\":[\"net8.0\\\\u202EHOSTILE\"]",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', json);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CompileSliceMeasurementsUseOneSelectedPayloadPerLibrary()
    {
        byte[] realImplementation =
            ReadRealAsset("System.Text.Json.dll");
        byte[] archive = TestPackageArchive.CreateWithContent(
            ($"ref/net10.0/{MaterializedPackageId}.dll", new byte[3]),
            ("ref/net10.0/Companion.dll", new byte[5]),
            ($"lib/net10.0/{MaterializedPackageId}.dll", new byte[7]),
            ("lib/net10.0/Companion.dll", new byte[11]),
            (
                $"runtimes/linux-x64/lib/net10.0/{MaterializedPackageId}.dll",
                realImplementation),
            ("build/net10.0/Package.targets", new byte[2]),
            ("contentFiles/any/net10.0/readme.txt", new byte[3]),
            ("custom/net10.0/data.bin", new byte[4]),
            ("tools/net8.0/tool.dll", new byte[5]),
            ("lib/net8.0/Legacy.dll", new byte[17]));
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));

        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0",
                "linux-x64");

        PackageHouseCompileSliceMeasurementOutcome.Measured measured =
            Assert.IsType<
                PackageHouseCompileSliceMeasurementOutcome.Measured>(
                PackageHouseCompileSliceMeasurementProjection.Project(
                    settlement));
        PackageHouseCompileSliceMeasurements measurements =
            measured.Measurements;
        Assert.Equal(archive.LongLength, measurements.CompressedPackageBytes);
        Assert.Equal("net10.0", measurements.SelectedTargetFramework);
        Assert.Equal(
            ["net10.0", "net8.0"],
            measurements.AvailableTargetFrameworks);
        Assert.Equal(2, measurements.SelectedLibraryCount);
        Assert.Equal(
            ["build", "contentFiles", "custom", "lib", "ref", "runtimes"],
            measurements.SelectedTargetFrameworkFolders);
        Assert.Equal(
            realImplementation.LongLength + 11,
            measurements.SelectedLibraryPayloadBytes);
        PackageHouseCompileLibraryMeasurement primary =
            Assert.Single(
                measurements.Libraries,
                library => library.CompileAsset.AssemblyName.Equals(
                    $"{MaterializedPackageId}.dll",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            $"runtimes/linux-x64/lib/net10.0/{MaterializedPackageId}.dll",
            primary.PayloadAsset.Path);
        Assert.Equal(realImplementation.LongLength, primary.UncompressedBytes);
        PackageHouseCompileLibraryMeasurement companion =
            Assert.Single(
                measurements.Libraries,
                library => library.CompileAsset.AssemblyName.Equals(
                    "Companion.dll",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Equal("lib/net10.0/Companion.dll", companion.PayloadAsset.Path);
        Assert.Equal(11, companion.UncompressedBytes);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                settlement.Result.Evidence.Realization);
        Assert.Same(realization, measurements.Realization);
        Assert.Same(realization.Receipt, measurements.SelectionReceipt);
        Assert.Same(realization.Acquisition, measurements.Acquisition);
        Assert.Same(
            settlement.Payload.Content.GenerationIdentity,
            measurements.Generation);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ExplicitEmptySliceRemainsDistinctMeasurementOutcome()
    {
        byte[] archive = TestPackageArchive.Create(
            $"lib/net8.0/{MaterializedPackageId}.dll",
            "ref/net10.0/_._");
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");

        PackageHouseCompileSliceMeasurementOutcome.SelectedEmpty empty =
            Assert.IsType<
                PackageHouseCompileSliceMeasurementOutcome.SelectedEmpty>(
                PackageHouseCompileSliceMeasurementProjection.Project(
                    settlement));

        Assert.Equal(archive.LongLength, empty.Measurements.CompressedPackageBytes);
        Assert.Equal("net10.0", empty.Measurements.SelectedTargetFramework);
        Assert.Equal(
            ["net10.0", "net8.0"],
            empty.Measurements.AvailableTargetFrameworks);
        Assert.Equal(0, empty.Measurements.SelectedLibraryCount);
        Assert.Equal(0, empty.Measurements.SelectedLibraryPayloadBytes);
        Assert.Empty(empty.Measurements.SelectedTargetFrameworkFolders);
        Assert.Empty(empty.Measurements.Libraries);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task NoCompileSlicesRemainTyped()
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create("package/readme.txt"),
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");

        PackageHouseCompileSliceMeasurementOutcome.NoCompileSlices outcome =
            Assert.IsType<
                PackageHouseCompileSliceMeasurementOutcome.NoCompileSlices>(
                PackageHouseCompileSliceMeasurementProjection.Project(
                    settlement));

        Assert.IsType<PackageHouseResult.NoMatch>(outcome.Result);
        Assert.Equal(
            content.NupkgBytes.Length,
            outcome.Measurements.CompressedPackageBytes);
        Assert.Empty(outcome.Measurements.AvailableTargetFrameworks);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task NoApplicableSliceRemainsTyped()
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create(
                $"ref/net11.0/{MaterializedPackageId}.dll"),
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");

        PackageHouseCompileSliceMeasurementOutcome.NoApplicableSlice outcome =
            Assert.IsType<
                PackageHouseCompileSliceMeasurementOutcome.NoApplicableSlice>(
                PackageHouseCompileSliceMeasurementProjection.Project(
                    settlement));

        Assert.IsType<PackageHouseResult.NoMatch>(outcome.Result);
        Assert.Equal(
            content.NupkgBytes.Length,
            outcome.Measurements.CompressedPackageBytes);
        Assert.Equal(
            ["net11.0"],
            outcome.Measurements.AvailableTargetFrameworks);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MissingEntryManifestIsVisible()
    {
        var inner = new InMemoryPackageContent(
            TestPackageArchive.Create(
                $"lib/net10.0/{MaterializedPackageId}.dll"),
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        var content = new ArchiveOnlyPackageContent(inner);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settlement =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");

        PackageHouseCompileSliceMeasurementOutcome.Unavailable unavailable =
            Assert.IsType<
                PackageHouseCompileSliceMeasurementOutcome.Unavailable>(
                PackageHouseCompileSliceMeasurementProjection.Project(
                    settlement));

        Assert.Equal(
            PackageHouseCompileSliceMeasurementUnavailableReason
                .EntryManifestUnavailable,
            unavailable.Reason);
        Assert.NotNull(unavailable.PackageMeasurements);
        Assert.Null(unavailable.PayloadAsset);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task OperationFailureDoesNotProduceMeasurements()
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create(
                $"lib/net10.0/{MaterializedPackageId}.dll"),
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired settled =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net10.0");
        PackageHouseEvidence settledEvidence = settled.Result.Evidence;
        var timeout = new PackageHouseFailure.Timeout(
            settled.Result.Request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            settled.Result.Request.Operation.OperationTimeout);
        var failedEvidence = new PackageHouseEvidence(
            settled.Result.Request,
            settledEvidence.Decision,
            settledEvidence.Acquisition,
            settledEvidence.Realization,
            [timeout]);
        var failedResult = new PackageHouseResult.Failed(
            failedEvidence,
            new InertString(
                TextPolicy.Field,
                "The PackageHouse operation timed out."));
        var failedSettlement = new PackageHouseSettlement.Acquired(
            failedResult,
            settled.Payload,
            settled.SourcePayloadResult!,
            settled.SelectionUsesOriginalSources);

        var outcome =
            PackageHouseCompileSliceMeasurementProjection.Project(
                failedSettlement);

        Assert.IsType<
            PackageHouseCompileSliceMeasurementOutcome.HouseFailure>(
                outcome);
        Assert.Same(failedResult, outcome.Result);
        await environment.AssertRootSettledAsync();
    }

    private static async Task<PackageHouseSettlement.Acquired>
        ExecuteCompileMeasurementAsync(
        HouseEnvironment environment,
        IPackageContent content,
        string targetFramework,
        string? runtimeIdentifier = null)
    {
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    MaterializedPackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(
                targetFramework,
                runtimeIdentifier),
            PackageHouseAssetSelectionKind.Compile);

        return Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.CreateHouse(
                    (_, _) => new FixedPackageContentStore(content))
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken)));
    }

    private sealed class ArchiveOnlyPackageContent(
        InMemoryPackageContent inner) : IPackageContent
    {
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public PackageContentGenerationIdentity GenerationIdentity =>
            inner.GenerationIdentity;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenEntry(relativePath, out stream);

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();
    }
}
