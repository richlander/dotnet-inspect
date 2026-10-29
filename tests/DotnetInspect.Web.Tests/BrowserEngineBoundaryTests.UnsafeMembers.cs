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
    // The Unsafe tab renders the shared census: the same members, in the CLI
    // section's order, with the same evidence text and the census limitations.
    [Fact]
    public async Task PackageUnsafeMembers_ProjectsTheSharedCensus()
    {
        const string packageId = "Browser.Library.UnsafeMembers";
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(File.ReadAllBytes(assemblyPath), $"lib/net11.0/{assemblyName}"));
        string assemblyId = Assert.Single(coordinate.Selection.Assets).Id;

        BrowserUnsafeMembers result = Assert.IsType<BrowserUnsafeMembers>(
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPackageUnsafeMembers(
                    packageId, "1.0.0", "net11.0", assemblyId),
                BrowserAnalysisJsonContext.Default.BrowserUnsafeMembers));

        UnsafeMemberFindingInspection census = CensusOf(assemblyPath);
        Assert.Equal(
            census is UnsafeMemberFindingInspection.Complete ? "available" : "incomplete",
            result.Outcome);
        Assert.Null(result.InspectionError);
        Assert.Equal(assemblyName, result.Assembly);
        Assert.Equal(
            [.. CensusFindings(census)
                .Select(static finding => finding.Member.MetadataToken)
                .Order()],
            [.. Members(result).Select(static member => member.MetadataToken).Order()]);
        Assert.Equal(
            [.. result.Types.Select(static type => type.Type).Order(StringComparer.Ordinal)],
            [.. result.Types.Select(static type => type.Type)]);
        Assert.All(result.Types, static type => Assert.Equal(
            [.. type.Members.Select(static member => member.Member).Order(StringComparer.Ordinal)],
            [.. type.Members.Select(static member => member.Member)]));

        // A lambda's dereference folds into its declaring non-public member,
        // which still resolves to an exact navigation identity.
        BrowserUnsafeMember capture = Assert.Single(
            Members(result),
            static member => member.Member.Contains(".CaptureLocal(", StringComparison.Ordinal));
        Assert.Equal("Non-public", capture.Exposure);
        Assert.Equal("pointer dereference", capture.Evidence);
        Assert.NotNull(capture.TypeId);
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
        Assert.DoesNotContain(Members(result), static member => member.Member.Contains(">b__", StringComparison.Ordinal));

        // The fixture uses the updated rules, so a member with an inner
        // unsafe block and no contract is a safe boundary.
        Assert.Equal("safe boundary", capture.SafeBoundary);

        // Types group the same rows in the same order, each with the shared
        // counts, and the Library counts are their sum.
        BrowserUnsafeType captureType = Assert.Single(
            result.Types, type => type.Members.Contains(capture));
        Assert.Equal(capture.TypeId, captureType.TypeId);
        Assert.All(result.Types, type => Assert.All(
            type.Members,
            member => Assert.StartsWith(type.Type + ".", member.Member, StringComparison.Ordinal)));
        Assert.Equal(
            result.Types.Length,
            result.Types.Select(static type => type.Type).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Members(result).Length, result.Counts.Members);
        Assert.Equal(result.Types.Sum(static type => type.Counts.SafeBoundaries), result.Counts.SafeBoundaries);
        Assert.Equal(result.Types.Sum(static type => type.Counts.Propagating), result.Counts.Propagating);
        Assert.Equal(result.Types.Sum(static type => type.Counts.Public), result.Counts.Public);
        Assert.Equal(
            Members(result).Count(static member => member.SafeBoundary == "safe boundary"),
            result.Counts.SafeBoundaries);
        Assert.Equal(
            Members(result).Count(static member => member.ExplicitContract),
            result.Counts.Propagating);
        Assert.True(result.Counts.Propagating > 0);
        Assert.Equal(0, result.Counts.SafeBoundaryUndecided);
    }

    [Fact]
    public async Task PackageUnsafeMembers_WithoutImplementationAssemblyIsUnavailable()
    {
        const string packageId = "Browser.Library.UnsafeMembers.ReferenceOnly";
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(File.ReadAllBytes(assemblyPath), $"ref/net11.0/{assemblyName}"));
        string assemblyId = Assert.Single(coordinate.Selection.Assets).Id;

        BrowserUnsafeMembers result = Assert.IsType<BrowserUnsafeMembers>(
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPackageUnsafeMembers(
                    packageId, "1.0.0", "net11.0", assemblyId),
                BrowserAnalysisJsonContext.Default.BrowserUnsafeMembers));

        Assert.Equal("unavailable", result.Outcome);
        Assert.Empty(Members(result));
        Assert.NotNull(result.InspectionError);
    }

    [Fact]
    public async Task PlatformUnsafeMembers_UsesTheSharedCensus()
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
        BrowserUnsafeMembers result = Assert.IsType<BrowserUnsafeMembers>(
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPlatformUnsafeMembers(
                    framework, "", assemblyName, "netcore.app"),
                BrowserAnalysisJsonContext.Default.BrowserUnsafeMembers));

        Assert.Contains(result.Outcome, new[] { "available", "incomplete" });
        Assert.Equal(
            CensusFindings(CensusOf(assemblyPath)).Length,
            Members(result).Length);

        // Rows navigate into the Platform surface, whose Types carry the bare
        // identity name rather than the file name.
        BrowserPlatformProjectionInfo projection = BrowserPlatformSurfaceProjection.Project(
            resolution.Scope, resolution.Participant, resolution.Coordinate);
        Assert.Equal(Path.GetFileNameWithoutExtension(assemblyName), result.Assembly);
        BrowserUnsafeMember[] navigable =
            [.. Members(result).Where(static member => member.TypeId is not null)];
        Assert.NotEmpty(navigable);
        Assert.All(navigable, member =>
            Assert.Equal(result.Assembly, member.DeclaringType?.Assembly));
        Assert.Contains(navigable, member => projection.Surface.Types.Any(type =>
            type.Assembly == result.Assembly && type.DefinitionId == member.TypeId));
    }

    [Fact]
    public void UnsafeMembersProjection_KeepsFailedAndMissingCensusesVisible()
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
