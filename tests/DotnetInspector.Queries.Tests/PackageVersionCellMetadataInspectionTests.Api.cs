using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageVersionCellMetadataInspectionTests
{
    const string MarkoutType = "Markout.MarkoutWriterOptions";

    [Theory]
    [InlineData(ApiSurfaceScope.Public)]
    [InlineData(ApiSurfaceScope.IncludeAll)]
    [InlineData(ApiSurfaceScope.PublicWithNonPublicTypes)]
    public async Task ApiInspectionReturnsDetachedNativeMarkoutFindings(
        ApiSurfaceScope scope)
    {
        var (fixture, content) = MarkoutCell();
        var executor = new SettlementExecutor(
            execution => fixture.Realize(execution, content));

        var result = Assert.IsType<PackageVersionCellMetadataInspectionOutcome.Available>(
            await PackageVersionCellMetadataInspector.ExecuteAsync(
                fixture.Request(
                    framework: "net10.0",
                    apiInspection: ApiRequest(scope: scope)),
                executor,
                TestContext.Current.CancellationToken));

        Assert.Equal(1, executor.Calls);
        var api = Assert.IsType<PackageVersionCellApiInspectionResult>(result.ApiInspection);
        Assert.Equal(MarkoutType, api.TypeFullName);
        Assert.Equal(scope, api.Scope);
        Assert.True(api.Surfaces.IsComplete);
        Assert.Null(api.Surfaces.Truncation);
        var set = Assert.Single(api.Findings);
        Assert.Same(Assert.Single(api.Surfaces.Assemblies.Assemblies), set.Assembly);
        if (scope != ApiSurfaceScope.Public)
        {
            Assert.Contains(set.Assembly.Value.Surface.Types,
                type => !ApiAccessibility.Classify(type.Accessibility).IsDefault);
        }
        else
        {
            Assert.All(set.Assembly.Value.Surface.Types,
                type => Assert.True(ApiAccessibility.Classify(type.Accessibility).IsDefault));
        }
        var types = Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(set.Type.Value);
        Assert.Equal(MarkoutType, Assert.Single(types.Findings).Key.IdentityKey);
        Assert.NotEmpty(
            Assert.IsType<FindingInspection<ApiMemberHandle>.Complete>(
                set.Members.Value).Findings);
        Assert.IsType<FindingInspection<ApiAttributeHandle>.Complete>(set.Attributes.Value);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task ApiInspectionKeepsParticipantCensusesSeparate()
    {
        CellFixture fixture = CellFixture.Create(packageId: "Markout", version: "0.35.2");
        byte[] markout = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "VersionCell", "Markout.dll"));
        IPackageContent content = fixture.Content(
            ("lib/net10.0/Markout.dll", markout),
            ("lib/net10.0/Neighbor.dll", Image));
        var result = Assert.IsType<PackageVersionCellMetadataInspectionOutcome.Available>(
            await PackageVersionCellMetadataInspector.ExecuteAsync(
                fixture.Request(framework: "net10.0", apiInspection: ApiRequest()),
                new SettlementExecutor(execution => fixture.Realize(execution, content)),
                TestContext.Current.CancellationToken));

        var api = Assert.IsType<PackageVersionCellApiInspectionResult>(result.ApiInspection);
        Assert.True(api.Surfaces.IsComplete);
        Assert.Equal(2, api.Findings.Length);
        for (int index = 0; index < api.Findings.Length; index++)
            Assert.Same(api.Surfaces.Assemblies.Assemblies[index], api.Findings[index].Assembly);
        Assert.Single(api.Findings.Where(set =>
            set.Type.Value is FindingInspection<ApiTypeHandle>.Complete { Findings.Length: 1 }));
        Assert.Single(api.Findings.Where(set =>
            set.Members.Value is FindingInspection<ApiMemberHandle>.Absent));
    }

    [Fact]
    public async Task ApiInspectionPreservesNativeSubjectAbsence()
    {
        var (fixture, content) = MarkoutCell();
        var result = Assert.IsType<PackageVersionCellMetadataInspectionOutcome.Available>(
            await PackageVersionCellMetadataInspector.ExecuteAsync(
                fixture.Request(
                    framework: "net10.0",
                    apiInspection: ApiRequest("Missing.Type")),
                new SettlementExecutor(execution => fixture.Realize(execution, content)),
                TestContext.Current.CancellationToken));

        var api = Assert.IsType<PackageVersionCellApiInspectionResult>(result.ApiInspection);
        Assert.True(api.Surfaces.IsComplete);
        var set = Assert.Single(api.Findings);
        Assert.Empty(
            Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(
                set.Type.Value).Findings);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            Assert.IsType<FindingInspection<ApiMemberHandle>.Absent>(
                set.Members.Value).Kind);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            Assert.IsType<FindingInspection<ApiAttributeHandle>.Absent>(
                set.Attributes.Value).Kind);
    }

    [Theory]
    [InlineData(ApiSurfaceProjectionLimit.Types)]
    [InlineData(ApiSurfaceProjectionLimit.RetainedTextCharacters)]
    public async Task ApiInspectionPreservesTruncationWithoutInventingAbsence(
        ApiSurfaceProjectionLimit bound)
    {
        var (fixture, content) = MarkoutCell();
        var limits = new ApiSurfaceProjectionLimits(
            16,
            bound == ApiSurfaceProjectionLimit.Types ? 1 : 1000,
            10_000,
            100,
            100,
            100_000,
            bound == ApiSurfaceProjectionLimit.RetainedTextCharacters ? 0 : 2_000_000);
        var result = Assert.IsType<PackageVersionCellMetadataInspectionOutcome.Available>(
            await PackageVersionCellMetadataInspector.ExecuteAsync(
                fixture.Request(
                    framework: "net10.0",
                    apiInspection: new(MarkoutType, limits)),
                new SettlementExecutor(execution => fixture.Realize(execution, content)),
                TestContext.Current.CancellationToken));

        Assert.Single(result.Metadata.Assemblies);
        var api = Assert.IsType<PackageVersionCellApiInspectionResult>(result.ApiInspection);
        Assert.False(api.Surfaces.IsComplete);
        var truncation = Assert.IsType<ApiSurfaceProjectionTruncation>(api.Surfaces.Truncation);
        Assert.Equal(bound, truncation.Limit);
        Assert.Equal(1, truncation.OmittedParticipants);
        Assert.Empty(api.Surfaces.Assemblies.Assemblies);
        Assert.Empty(api.Findings);
    }

    [Fact]
    public async Task ApiInspectionPreservesEmptyCompileSelection()
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"ref/{Framework}/_._", []),
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));
        var result = Assert.IsType<PackageVersionCellMetadataInspectionOutcome.Available>(
            await PackageVersionCellMetadataInspector.ExecuteAsync(
                fixture.Request(apiInspection: ApiRequest()),
                new SettlementExecutor(execution => fixture.Realize(execution, content)),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            result.Evidence.CompileRealization!.Selection.Status);
        var api = Assert.IsType<PackageVersionCellApiInspectionResult>(result.ApiInspection);
        Assert.True(api.Surfaces.IsComplete);
        Assert.Empty(api.Surfaces.Assemblies.Assemblies);
        Assert.Empty(api.Findings);
    }

    [Fact]
    public void ApiInspectionRequestRejectsInvalidInput()
    {
        var limits = ApiRequest().Limits;
        Assert.Throws<ArgumentException>(
            () => new PackageVersionCellApiInspectionRequest(" ", limits));
        Assert.Throws<ArgumentNullException>(
            () => new PackageVersionCellApiInspectionRequest(MarkoutType, null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackageVersionCellApiInspectionRequest(
                MarkoutType, limits, (ApiSurfaceScope)(-1)));
    }

    static PackageVersionCellApiInspectionRequest ApiRequest(
        string typeFullName = MarkoutType,
        ApiSurfaceScope scope = ApiSurfaceScope.Public) =>
        new(typeFullName, new ApiSurfaceProjectionLimits(
            16, 1000, 10_000, 100, 100, 100_000, 2_000_000), scope);

    static (CellFixture Fixture, IPackageContent Content) MarkoutCell()
    {
        CellFixture fixture = CellFixture.Create(packageId: "Markout", version: "0.35.2");
        byte[] image = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "VersionCell", "Markout.dll"));
        return (fixture, fixture.Content(("lib/net10.0/Markout.dll", image)));
    }
}
