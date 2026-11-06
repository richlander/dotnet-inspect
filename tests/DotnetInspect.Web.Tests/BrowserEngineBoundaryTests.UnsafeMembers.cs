using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using NuGetFetch;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspect.Web.Interop.Analysis;
using ILInspector.Analysis;
using ILInspector.Metadata;
using BrowserAnalysisJsonContext = DotnetInspect.Web.Interop.Analysis.BrowserAnalysisJsonContext;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    // The Library Unsafe tab lists Finding Unsafe Any's Types, by name, each
    // resolved to the exact Type the Library's surface names.
    [Fact]
    public async Task PackageUnsafeCodeTypes_ProjectsFindingUnsafeAny()
    {
        const string packageId = "Browser.Library.UnsafeCodeTypes";
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(File.ReadAllBytes(assemblyPath), $"lib/net11.0/{assemblyName}"));
        string assemblyId = Assert.Single(coordinate.Selection.Assets).Id;

        BrowserUnsafeCodeTypes result = await QueryPackageTypes(packageId, assemblyId);

        UnsafeCodeTypes expected = TypesOf(assemblyPath);
        Assert.Equal(expected.IsComplete ? "available" : "incomplete", result.Outcome);
        Assert.Null(result.InspectionError);
        Assert.Equal(assemblyName, result.Assembly);
        Assert.Equal(
            [.. expected.Types
                .Select(static type => type.DeclaringType.ToQualifiedDisplayString())
                .Order(StringComparer.Ordinal)],
            [.. result.Types.Select(static type => type.Type)]);
        Assert.All(result.Types, static type => Assert.Equal("unsafe", type.State));
        Assert.All(result.Types, type =>
        {
            Assert.NotNull(type.TypeId);
            BrowserPerformanceTypeSurface declaring = Assert.IsType<BrowserPerformanceTypeSurface>(type.DeclaringType);
            Assert.Equal(type.TypeId, declaring.DefinitionId);
            Assert.Equal(result.Assembly, declaring.Assembly);
        });
    }

    // A Type request runs that Type's census: the same members the shared
    // census declares in that Type, in the CLI section's order, with the same
    // evidence text and counts that sum its rows.
    [Fact]
    public async Task PackageUnsafeMembers_TypeScopeProjectsTheSharedCensus()
    {
        const string packageId = "Browser.Library.UnsafeMembers";
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(File.ReadAllBytes(assemblyPath), $"lib/net11.0/{assemblyName}"));
        string assemblyId = Assert.Single(coordinate.Selection.Assets).Id;
        BrowserUnsafeCodeType captureType = await CaptureLocalType(packageId, assemblyId);

        BrowserUnsafeMembers result = await QueryPackageMembers(
            packageId, assemblyId, captureType.TypeId!, "");

        Assert.Equal(assemblyName, result.Assembly);
        Assert.Null(result.InspectionError);
        BrowserUnsafeType type = Assert.Single(result.Types);
        Assert.Equal(captureType.Type, type.Type);
        Assert.Equal(captureType.TypeId, type.TypeId);
        Assert.Equal(
            [.. CensusFindings(CensusOf(assemblyPath))
                .Where(finding => finding.Member.DeclaringType.ToQualifiedDisplayString() == type.Type)
                .Select(static finding => finding.Member.MetadataToken)
                .Order()],
            [.. type.Members.Select(static member => member.MetadataToken).Order()]);
        Assert.Equal(
            [.. type.Members.Select(static member => member.Member).Order(StringComparer.Ordinal)],
            [.. type.Members.Select(static member => member.Member)]);

        // A lambda's dereference folds into its declaring non-public member,
        // which still resolves to an exact navigation identity.
        BrowserUnsafeMember capture = Assert.Single(
            type.Members,
            static member => member.Member.Contains(".CaptureLocal(", StringComparison.Ordinal));
        Assert.Equal("Non-public", capture.Exposure);
        Assert.Equal("pointer dereference", capture.Evidence);
        Assert.Equal(captureType.TypeId, capture.TypeId);
        Assert.NotNull(capture.StableSelector);
        Assert.NotEqual("public", capture.Accessibility);
        // The declaring Type a host projects when its Type list lacks it is
        // the same exact Type, named by the Library's surface assembly.
        BrowserPerformanceTypeSurface declaring = Assert.IsType<BrowserPerformanceTypeSurface>(
            capture.DeclaringType);
        Assert.Equal(capture.TypeId, declaring.DefinitionId);
        Assert.Equal(result.Assembly, declaring.Assembly);
        Assert.Contains(
            capture.Items,
            static item => item.Role == "pointer dereference"
                && item.Body.Contains(">b__", StringComparison.Ordinal));
        Assert.DoesNotContain(type.Members, static member => member.Member.Contains(">b__", StringComparison.Ordinal));

        // The fixture uses the updated rules, so a member with an inner
        // unsafe block and no contract is a safe boundary.
        Assert.Equal("safe boundary", capture.SafeBoundary);

        // The Type's counts are its rows' dimensions.
        Assert.Equal(type.Counts, result.Counts);
        Assert.Equal(type.Members.Length, type.Counts.Members);
        Assert.Equal(
            type.Members.Count(static member => member.SafeBoundary == "safe boundary"),
            type.Counts.SafeBoundaries);
        Assert.Equal(
            type.Members.Count(static member => member.ExplicitContract),
            type.Counts.Propagating);
        Assert.Equal(0, type.Counts.SafeBoundaryUndecided);
    }

    // A member request runs only that member's census, and answers with the
    // rows its Type's census issues for that member.
    [Fact]
    public async Task PackageUnsafeMembers_MemberScopesAgreeWithTheTypeCensus()
    {
        const string packageId = "Browser.Library.UnsafeMembers.Scopes";
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(File.ReadAllBytes(assemblyPath), $"lib/net11.0/{assemblyName}"));
        string assemblyId = Assert.Single(coordinate.Selection.Assets).Id;
        string typeId = (await CaptureLocalType(packageId, assemblyId)).TypeId!;

        BrowserUnsafeMembers typeScope = await QueryPackageMembers(packageId, assemblyId, typeId, "");
        BrowserUnsafeType expectedType = Assert.Single(typeScope.Types);
        foreach (BrowserUnsafeMember expected in expectedType.Members.Where(static member => member.StableSelector is not null))
        {
            BrowserUnsafeMembers memberScope = await QueryPackageMembers(
                packageId, assemblyId, typeId, expected.StableSelector!);
            Assert.Equal("available", memberScope.Outcome);
            BrowserUnsafeMember[] rows = Members(memberScope);
            Assert.Contains(Row(expected), rows.Select(Row));
            Assert.All(rows, row => Assert.Equal(expected.StableSelector, row.StableSelector));
        }
    }

    async Task<BrowserUnsafeCodeType> CaptureLocalType(string packageId, string assemblyId)
    {
        BrowserUnsafeCodeTypes library = await QueryPackageTypes(packageId, assemblyId);
        foreach (BrowserUnsafeCodeType candidate in library.Types)
        {
            BrowserUnsafeMembers census = await QueryPackageMembers(
                packageId, assemblyId, candidate.TypeId!, "");
            if (Members(census).Any(static member => member.Member.Contains(".CaptureLocal(", StringComparison.Ordinal)))
                return candidate;
        }
        throw new InvalidOperationException("No Type declares CaptureLocal.");
    }

    static async Task<BrowserUnsafeCodeTypes> QueryPackageTypes(string packageId, string assemblyId) =>
        Assert.IsType<BrowserUnsafeCodeTypes>(
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPackageUnsafeCodeTypes(
                    packageId, "1.0.0", "net11.0", assemblyId),
                BrowserAnalysisJsonContext.Default.BrowserUnsafeCodeTypes));

    static async Task<BrowserUnsafeMembers> QueryPackageMembers(
        string packageId, string assemblyId, string typeIdentity, string stableSelector) =>
        Assert.IsType<BrowserUnsafeMembers>(
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPackageUnsafeMembers(
                    packageId, "1.0.0", "net11.0", assemblyId, typeIdentity, stableSelector),
                BrowserAnalysisJsonContext.Default.BrowserUnsafeMembers));

    // A member's row value without its evidence-item array, which records
    // compare by reference.
    static string Row(BrowserUnsafeMember member) =>
        string.Join(
            " | ",
            member.Member,
            member.MetadataToken,
            member.Exposure,
            member.ExplicitContract,
            member.SafeBoundary,
            member.Evidence,
            member.Partial,
            member.TypeId,
            member.StableSelector,
            member.Accessibility,
            string.Join("; ", member.Items.Select(static item => $"{item.Role} {item.Body} {item.IlOffset} {item.Detail}")));

    [Fact]
    public async Task PackageUnsafeCodeTypes_WithoutImplementationAssemblyIsUnavailable()
    {
        const string packageId = "Browser.Library.UnsafeMembers.ReferenceOnly";
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(File.ReadAllBytes(assemblyPath), $"ref/net11.0/{assemblyName}"));
        string assemblyId = Assert.Single(coordinate.Selection.Assets).Id;

        BrowserUnsafeCodeTypes result = await QueryPackageTypes(packageId, assemblyId);

        Assert.Equal("unavailable", result.Outcome);
        Assert.Empty(result.Types);
        Assert.NotNull(result.InspectionError);
    }

    [Fact]
    public async Task PlatformUnsafeCodeTypes_UsesFindingUnsafeAny()
    {
        const string packageId = "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.9802";
        const string framework = "net11.0-unsafe-members";
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        byte[] nupkg = PlatformPackage((assemblyName, File.ReadAllBytes(assemblyPath)));
        var handler = new PlatformVersionHandler(packageId, version, nupkg);
        using var client = new HttpClient(handler);
        var authorization = new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                assemblyName,
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        BrowserUnsafeCodeTypes result = Assert.IsType<BrowserUnsafeCodeTypes>(
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPlatformUnsafeCodeTypes(
                    framework, "", assemblyName, "netcore.app"),
                BrowserAnalysisJsonContext.Default.BrowserUnsafeCodeTypes));

        Assert.Contains(result.Outcome, new[] { "available", "incomplete" });
        Assert.Equal(TypesOf(assemblyPath).Types.Length, result.Types.Length);

        // Rows navigate into the Platform surface, whose Types carry the bare
        // identity name rather than the file name.
        BrowserPlatformProjectionInfo projection = BrowserPlatformSurfaceProjection.Project(
            resolution.Scope, resolution.Participant, resolution.Coordinate);
        Assert.Equal(Path.GetFileNameWithoutExtension(assemblyName), result.Assembly);
        BrowserUnsafeCodeType[] navigable =
            [.. result.Types.Where(static type => type.TypeId is not null)];
        Assert.NotEmpty(navigable);
        Assert.All(navigable, type =>
            Assert.Equal(result.Assembly, type.DeclaringType?.Assembly));
        Assert.Contains(navigable, row => projection.Surface.Types.Any(type =>
            type.Assembly == result.Assembly && type.DefinitionId == row.TypeId));
    }

    [Fact]
    public void UnsafeProjections_KeepFailedAndMissingResultsVisible()
    {
        var subject = new AssemblyContextSubject(ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity("Fixture", new Version(1, 0), null, null), null,
            () => new MemoryStream(), AssemblyResolutionProvenance.Local("fixture")));
        BrowserUnsafeMembers failed = AnalysisExports.ProjectUnsafeMembers(
            new AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Available(
                subject,
                new AssemblyUnsafeMemberCensusResult(
                    new UnsafeMemberCensusQueryResult.Failed(new InvalidOperationException("census failed")),
                    System.Collections.Immutable.ImmutableDictionary<int, UnsafeMemberNavigation>.Empty,
                    [])),
            static identity => new BrowserUnsafeMemberSurfaceNaming(identity.Name, identity.Name, null));
        Assert.Equal("failed", failed.Outcome);
        Assert.Equal("census failed", failed.InspectionError);
        Assert.Empty(Members(failed));

        BrowserUnsafeMembers missing = AnalysisExports.ProjectUnsafeMembers(
            new AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Available(
                subject,
                new AssemblyUnsafeMemberCensusResult(
                    new UnsafeMemberCensusQueryResult.NoMetadata(),
                    System.Collections.Immutable.ImmutableDictionary<int, UnsafeMemberNavigation>.Empty,
                    [])),
            static identity => new BrowserUnsafeMemberSurfaceNaming(identity.Name, identity.Name, null));
        Assert.Equal("unavailable", missing.Outcome);
        Assert.Empty(Members(missing));

        BrowserUnsafeCodeTypes failedTypes = AnalysisExports.ProjectUnsafeCodeTypes(
            new AssemblyContextEntry<AssemblyUnsafeCodeTypesResult>.Available(
                subject,
                new AssemblyUnsafeCodeTypesResult(
                    new FindingUnsafeAnyQueryResult.Failed(new InvalidOperationException("search failed")),
                    System.Collections.Immutable.ImmutableDictionary<int, UnsafeCodeTypeNavigation>.Empty,
                    [])),
            static identity => new BrowserUnsafeMemberSurfaceNaming(identity.Name, identity.Name, null));
        Assert.Equal("failed", failedTypes.Outcome);
        Assert.Equal("search failed", failedTypes.InspectionError);
        Assert.Empty(failedTypes.Types);

        BrowserUnsafeCodeTypes missingTypes = AnalysisExports.ProjectUnsafeCodeTypes(
            new AssemblyContextEntry<AssemblyUnsafeCodeTypesResult>.Available(
                subject,
                new AssemblyUnsafeCodeTypesResult(
                    new FindingUnsafeAnyQueryResult.NoMetadata(),
                    System.Collections.Immutable.ImmutableDictionary<int, UnsafeCodeTypeNavigation>.Empty,
                    [])),
            static identity => new BrowserUnsafeMemberSurfaceNaming(identity.Name, identity.Name, null));
        Assert.Equal("unavailable", missingTypes.Outcome);
        Assert.Empty(missingTypes.Types);
    }

    static UnsafeCodeTypes TypesOf(string assemblyPath)
    {
        using PdbContext context = PdbContext.OpenMetadataOnly(assemblyPath);
        var result = Assert.IsType<FindingUnsafeAnyQueryResult.Inspected>(
            FindingUnsafeAnyQuery.Execute(assemblyPath, context));
        return Assert.IsType<ILInspector.Analysis.Planning.FindingUnsafeAnyResult.Available>(result.Answer).Types;
    }

    static BrowserUnsafeMember[] Members(BrowserUnsafeMembers result) =>
        [.. result.Types.SelectMany(static type => type.Members)];

    static UnsafeMemberFindingInspection CensusOf(string assemblyPath)
    {
        using PdbContext context = PdbContext.OpenMetadataOnly(assemblyPath);
        var result = Assert.IsType<UnsafeMemberCensusQueryResult.Inspected>(
            UnsafeMemberCensusQuery.Execute(assemblyPath, context));
        return AnalysisFindings.InspectUnsafeMembers(
            result.Census,
            new Inspector.Findings.FindingSubject(assemblyPath, Path.GetFileName(assemblyPath)));
    }

    static System.Collections.Immutable.ImmutableArray<UnsafeMemberFinding> CensusFindings(
        UnsafeMemberFindingInspection inspection) =>
        inspection switch
        {
            UnsafeMemberFindingInspection.Complete complete =>
                [.. complete.Inspection.Findings.Select(static finding => finding.Payload)],
            UnsafeMemberFindingInspection.Incomplete incomplete =>
                [.. incomplete.Inspection.Findings.Select(static finding => finding.Payload)],
            _ => [],
        };
}
