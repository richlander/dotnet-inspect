using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypeLocator_VisibilityPinnedReferencePack_PreservesDefinitionAndForwarderEvidence()
    {
        using var http = new HttpClient();
        var nuget = new NuGetClient(http);
        await using var source = await nuget.DownloadAsync(
            "Microsoft.NETCore.App.Ref", "10.0.10",
            cancellationToken: TestContext.Current.CancellationToken);
        using var package = new MemoryStream();
        await source.CopyToAsync(package, TestContext.Current.CancellationToken);
        Assert.Equal(
            "995E22BDC4F70DB27044D152E55CF231A9712338E4E391A2B9B497A756A647C3",
            Convert.ToHexString(SHA256.HashData(package.ToArray())));
        package.Position = 0;
        using var archive = new ZipArchive(package);
        var identities = new List<AssemblyReferenceIdentity>();
        foreach (string library in new[] { "System.Runtime", "netstandard" })
        {
            var entry = archive.GetEntry($"ref/net10.0/{library}.dll");
            Assert.NotNull(entry);
            using var asset = entry.Open();
            using var buffer = new MemoryStream();
            await asset.CopyToAsync(buffer, TestContext.Current.CancellationToken);
            using var pe = new PEReader(new MemoryStream(buffer.ToArray()));
            identities.Add(AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()));
        }

        var authorization = new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.AuthorizeSourcesFor("microsoft.netcore.app.ref").Authorities);
        using IPackageSourceClient client = PackageSourceClientFactory.CreateGallery(authority.Association);
        await using PackageSourceSettlementLease root = PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var platformSource = new PackagePlatformSource(
            authorization, new PackagePayloadAcquisitionPlan((_, _) => store));
        var coordinate = new PackageReferencePackCoordinate(new(PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"), PlatformVersion.Parse("10.0.10")));
        await using var workspace = new InspectionWorkspace();
        foreach (AssemblyReferenceIdentity identity in identities)
        {
            var admitted = await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace, platformSource, coordinate,
                new PackageReferencePopulationDemand.Assembly(identity),
                new PackageReferenceWorkBudget(1, 8 * 1024 * 1024),
                root.IssueOperationLease(TestContext.Current.CancellationToken,
                    TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(3)));
            Assert.True(admitted.Receipt.IsRealized);
        }

        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        var raw = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.ExecuteAsync(
                [
                    new TypeDeclarationLocatorRequest.Exact(LocatorName("System", "Object")),
                    new TypeDeclarationLocatorRequest.Exact(
                        LocatorName("System.Runtime.CompilerServices", "IsExternalInit")),
                    new TypeDeclarationLocatorRequest.Exact(LocatorName("System", "ExecutionEngineException")),
                    new TypeDeclarationLocatorRequest.Exact(LocatorName("System", "Span`1")),
                ], includeAll: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(raw.Answers, answer => Assert.True(answer.IsComplete));
        Assert.Equal(7, raw.Answers.Sum(answer => answer.Candidates.Length));
        Assert.Equal(2, locator.InventoryReadCount);
        await workspace.CloseAsync();

        foreach (var (plan, expectedMatches, expectedExcluded, expectedUnknown) in new[]
        {
            (TypeDeclarationVisibilityPlan.Default, 2, 2, 3),
            (TypeDeclarationVisibilityPlan.All, 7, 0, 0),
            (new TypeDeclarationVisibilityPlan(TypeDeclarationVisibilityPreset.Default,
                [new(TypeDeclarationVisibilityFacet.Obsolete,
                    RowQueryOperator.Equals, TypeDeclarationVisibilityValue.True)]), 1, 3, 3),
            (new TypeDeclarationVisibilityPlan(TypeDeclarationVisibilityPreset.All,
                [new(TypeDeclarationVisibilityFacet.Obsolete,
                    RowQueryOperator.Equals, TypeDeclarationVisibilityValue.Unknown)]), 3, 4, 0),
        })
        {
            var projected = Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(raw, new(RowSelectionIntent<string>.Empty, plan)));
            Assert.True(projected.IsSuccess);
            Assert.Equal(expectedMatches, projected.Answers.Sum(answer => answer.Candidates.Length));
            Assert.Equal(expectedExcluded,
                projected.Answers.Sum(answer =>
                    Assert.IsType<TypeDeclarationVisibilityCoverage>(answer.Visibility).ExcludedCandidateCount));
            Assert.Equal(expectedUnknown,
                projected.Answers.Sum(answer =>
                    Assert.IsType<TypeDeclarationVisibilityCoverage>(answer.Visibility).UnknownCandidates.Length));
            Assert.All(projected.Answers, answer =>
            {
                var visibility = Assert.IsType<TypeDeclarationVisibilityCoverage>(answer.Visibility);
                Assert.True(answer.IsEvaluationComplete);
                Assert.Equal(visibility.UnknownCandidates.IsEmpty, answer.IsComplete);
                Assert.Equal(visibility.InputCandidateCount,
                    answer.AvailableCandidateCount + visibility.ExcludedCandidateCount
                        + visibility.UnknownCandidates.Length);
                Assert.All(visibility.UnknownCandidates, unknown =>
                {
                    Assert.Equal(AssemblyTypeDeclarationKind.Forwarder, unknown.Candidate.DeclarationKind);
                    Assert.Null(unknown.Candidate.DiscoveryAttributes);
                    Assert.Equal("netstandard", unknown.Candidate.Observation.AssemblyIdentity.Name);
                });
            });
            Assert.Equal(2, locator.InventoryReadCount);
        }
    }
}
