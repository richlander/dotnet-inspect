using System.Reflection;
using System.Text;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task FrameworkReferences_ExplicitHouseTargetUsesNearestGroup()
    {
        PackageHouseSettlement.Acquired acquired =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net10.0"),
                FrameworkArchive(
                    """
                    <group targetFramework="net8.0">
                      <frameworkReference name="Microsoft.AspNetCore.App" />
                    </group>
                    """,
                    ($"lib/net6.0/{MaterializedPackageId}.dll", [])));

        var compile = Assert.IsType<PackageHouseRealizationReceipt.Compile>(
            acquired.Result.Evidence.Realization);
        Assert.Equal("net6.0", compile.Selection.TargetFramework);
        var selected =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(acquired));
        Assert.Equal(
            "net8.0",
            selected.Evidence.SelectedTargetFramework);
        Assert.Equal(
            "Microsoft.AspNetCore.App",
            Assert.Single(selected.Evidence.References).Name);
    }

    [Fact]
    public async Task FrameworkReferences_OwnerDefaultUsesSelectedCompileTarget()
    {
        PackageHouseSettlement.Acquired acquired =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.OwnerDefault(),
                FrameworkArchive(
                    """
                    <group targetFramework="net8.0">
                      <frameworkReference name="Microsoft.AspNetCore.App" />
                    </group>
                    <group targetFramework="net6.0">
                      <frameworkReference name="Microsoft.WindowsDesktop.App" />
                    </group>
                    """,
                    ($"lib/net8.0/{MaterializedPackageId}.dll", []),
                    ($"lib/net6.0/{MaterializedPackageId}.dll", [])));

        var selected =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(acquired));
        var basis = Assert.IsType<
            PackageHouseFrameworkReferenceTargetBasis.CompileSelection>(
                selected.Association.TargetBasis);
        Assert.Equal("net8.0", basis.TargetFramework);
        Assert.Equal(
            "Microsoft.AspNetCore.App",
            Assert.Single(selected.Evidence.References).Name);
    }

    [Fact]
    public async Task FrameworkOnlyPackage_PreservesHouseFrameworkReferences()
    {
        PackageHouseSettlement.Acquired acquired =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                FrameworkArchive(
                    """
                    <group targetFramework="net8.0">
                      <frameworkReference name="Microsoft.AspNetCore.App" />
                    </group>
                    """));

        Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        var compile = Assert.IsType<PackageHouseRealizationReceipt.Compile>(
            acquired.Result.Evidence.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            compile.Selection.Status);
        var selected =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(acquired));
        Assert.Equal(
            "Microsoft.AspNetCore.App",
            Assert.Single(selected.Evidence.References).Name);
    }

    [Fact]
    public async Task FrameworkReferences_PreserveClosedHouseProjectionOutcomes()
    {
        PackageHouseFrameworkReferenceOutcome noGroups =
            PackageHouseFrameworkReferenceProjection.Project(
                await ExecuteFrameworkReferencesAsync(
                    PackageHouseTargetContext.Exact("net8.0"),
                    FrameworkArchive(
                        "",
                        ($"lib/net8.0/{MaterializedPackageId}.dll", []))));
        PackageHouseFrameworkReferenceOutcome noMatch =
            PackageHouseFrameworkReferenceProjection.Project(
                await ExecuteFrameworkReferencesAsync(
                    PackageHouseTargetContext.Exact("net6.0"),
                    FrameworkArchive(
                        """
                        <group targetFramework="net8.0">
                          <frameworkReference name="Microsoft.AspNetCore.App" />
                        </group>
                        """,
                        ($"lib/net6.0/{MaterializedPackageId}.dll", []))));
        PackageHouseFrameworkReferenceOutcome noTarget =
            PackageHouseFrameworkReferenceProjection.Project(
                await ExecuteFrameworkReferencesAsync(
                    PackageHouseTargetContext.OwnerDefault(),
                    FrameworkArchive(
                        """
                        <group targetFramework="net8.0">
                          <frameworkReference name="Microsoft.AspNetCore.App" />
                        </group>
                        """)));

        Assert.IsType<
            PackageHouseFrameworkReferenceOutcome.NoFrameworkReferenceGroups>(
                noGroups);
        Assert.IsType<
            PackageHouseFrameworkReferenceOutcome.NoMatchingTargetFramework>(
                noMatch);
        Assert.IsType<
            PackageHouseFrameworkReferenceOutcome.TargetUnavailable>(
                noTarget);
    }

    [Fact]
    public async Task FrameworkReferences_NotRequestedDoesNotReadManifest()
    {
        byte[] archive = TestPackageArchive.Create(
            $"lib/net8.0/{MaterializedPackageId}.dll");
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        PackageHouseSettlement.Acquired acquired =
            await ExecuteCompileMeasurementAsync(
                environment,
                content,
                "net8.0");

        Assert.IsType<PackageHouseFrameworkReferenceOutcome.NotRequested>(
            PackageHouseFrameworkReferenceProjection.Project(acquired));
    }

    [Fact]
    public void FrameworkReferences_OutcomeAlgebraIsClosed()
    {
        Assert.Equal(
            [
                "Failed",
                "HouseFailed",
                "ManifestUnavailable",
                "NoFrameworkReferenceGroups",
                "NoMatchingTargetFramework",
                "NotRequested",
                "Selected",
                "TargetUnavailable",
            ],
            typeof(PackageHouseFrameworkReferenceOutcome)
                .GetNestedTypes(BindingFlags.Public)
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FrameworkReferences_RequireExactHouseCompileSettlement()
    {
        byte[] archive = FrameworkArchive(
            "",
            ($"lib/net8.0/{MaterializedPackageId}.dll", []));
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    MaterializedPackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire));
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.CreateHouse(
                    (_, _) => new FixedPackageContentStore(content))
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken)));

        Assert.Throws<ArgumentException>(() =>
            PackageHouseFrameworkReferenceProjection.Project(acquired));
    }

    [Fact]
    public async Task FrameworkReferences_ProjectExactHousePayload()
    {
        PackageHouseSettlement.Acquired acquired =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                FrameworkArchive(
                    """
                    <group targetFramework="net8.0">
                      <frameworkReference name="Microsoft.AspNetCore.App" />
                    </group>
                    """,
                    ($"lib/net8.0/{MaterializedPackageId}.dll", [])));

        var selected =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(acquired));

        Assert.Same(acquired.Result, selected.Association.Result);
        Assert.Same(
            acquired.Result.Evidence.Acquisition,
            selected.Association.Acquisition);
        Assert.Same(
            acquired.Result.Evidence.Realization,
            selected.Association.Realization);
        Assert.Same(
            acquired.Payload.Content.GenerationIdentity,
            selected.Association.Generation);
    }

    [Fact]
    public async Task FrameworkReferences_PreserveNuGetIdentityAndOccurrence()
    {
        PackageHouseSettlement.Acquired acquired =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                FrameworkArchive(
                    """
                    <group targetFramework=".NETCoreApp,Version=v8.0">
                      <frameworkReference name="Microsoft.AspNetCore.App" />
                      <frameworkReference name="microsoft.aspnetcore.app" />
                    </group>
                    """,
                    ($"lib/net8.0/{MaterializedPackageId}.dll", [])));

        var selected =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(acquired));

        Assert.Same(
            selected.Evidence.Groups[0],
            selected.Evidence.SelectedOccurrence);
        Assert.Equal(
            [
                "Microsoft.AspNetCore.App",
                "microsoft.aspnetcore.app",
            ],
            selected.Evidence.Occurrences.Select(
                occurrence => occurrence.SourceName));
        Assert.Single(selected.Evidence.References);
    }

    [Fact]
    public async Task FrameworkReferences_FailureDoesNotRewriteHouseResult()
    {
        PackageHouseSettlement.Acquired missing =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                TestPackageArchive.Create(
                    $"lib/net8.0/{MaterializedPackageId}.dll"));
        PackageHouseResult originalResult = missing.Result;

        var unavailable =
            Assert.IsType<
                PackageHouseFrameworkReferenceOutcome.ManifestUnavailable>(
                PackageHouseFrameworkReferenceProjection.Project(missing));

        Assert.Equal(
            PackageHouseManifestUnavailableReason.MissingRootManifest,
            unavailable.Reason);
        Assert.Same(originalResult, missing.Result);
        Assert.IsType<PackageHouseResult.Settled>(missing.Result);

        PackageHouseSettlement.Acquired malformed =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                TestPackageArchive.CreateWithContent(
                    ($"{MaterializedPackageId}.nuspec", "<package>"u8.ToArray()),
                    ($"lib/net8.0/{MaterializedPackageId}.dll", [])));
        var failed =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Failed>(
                PackageHouseFrameworkReferenceProjection.Project(malformed));
        Assert.IsType<PackageHouseFrameworkReferenceFailure.Manifest>(
            failed.Failure);
        Assert.IsType<PackageHouseResult.Settled>(malformed.Result);

        PackageHouseSettlement.Acquired invalidSection =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                FrameworkArchive(
                    """
                    <group targetFramework="net8.0">
                      <frameworkReference />
                    </group>
                    """,
                    ($"lib/net8.0/{MaterializedPackageId}.dll", [])));
        var sectionFailed =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Failed>(
                PackageHouseFrameworkReferenceProjection.Project(
                    invalidSection));
        Assert.IsType<
            PackageHouseFrameworkReferenceFailure.FrameworkSection>(
                sectionFailed.Failure);
        Assert.IsType<PackageHouseResult.Settled>(invalidSection.Result);

        PackageHouseSettlement.Acquired invalidTarget =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("not-a-framework"),
                FrameworkArchive(
                    """
                    <group targetFramework="net8.0">
                      <frameworkReference name="Microsoft.AspNetCore.App" />
                    </group>
                    """,
                    ($"lib/net8.0/{MaterializedPackageId}.dll", [])));
        var selectionFailed =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Failed>(
                PackageHouseFrameworkReferenceProjection.Project(
                    invalidTarget));
        Assert.IsType<
            PackageHouseFrameworkReferenceFailure.InvalidTargetFramework>(
                selectionFailed.Failure);
        Assert.IsType<PackageHouseResult.NoMatch>(invalidTarget.Result);
    }

    [Fact]
    public async Task FrameworkReferences_HouseFailureDoesNotReadManifest()
    {
        PackageHouseSettlement.Acquired settled =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                TestPackageArchive.Create(
                    $"lib/net8.0/{MaterializedPackageId}.dll"));
        PackageHouseEvidence evidence = settled.Result.Evidence;
        var timeout = new PackageHouseFailure.Timeout(
            settled.Result.Request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            settled.Result.Request.Operation.OperationTimeout);
        var failedResult = new PackageHouseResult.Failed(
            new PackageHouseEvidence(
                settled.Result.Request,
                evidence.Decision,
                evidence.Acquisition,
                evidence.Realization,
                [timeout]),
            new InertText.InertString(
                InertText.TextPolicy.Field,
                "The PackageHouse operation timed out."));
        var failedSettlement = new PackageHouseSettlement.Acquired(
            failedResult,
            settled.Payload,
            settled.SourcePayloadResult!,
            settled.SelectionUsesOriginalSources);

        var outcome =
            PackageHouseFrameworkReferenceProjection.Project(
                failedSettlement);

        Assert.IsType<PackageHouseFrameworkReferenceOutcome.HouseFailed>(
            outcome);
        Assert.Same(failedResult, outcome.Association.Result);
    }

    private static async Task<PackageHouseSettlement.Acquired>
        ExecuteFrameworkReferencesAsync(
            PackageHouseTargetContext target,
            byte[] archive)
    {
        var content = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    MaterializedPackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            target,
            PackageHouseAssetSelectionKind.Compile,
            evidenceDemand:
                PackageHouseEvidenceDemand.FrameworkReferences);

        return Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.CreateHouse(
                    (_, _) => new FixedPackageContentStore(content))
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken)));
    }

    private static byte[] FrameworkArchive(
        string frameworkGroups,
        params (string Path, byte[] Content)[] entries)
    {
        byte[] nuspec = Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{MaterializedPackageId}</id>
                <version>{Version}</version>
                <authors>Framework reference tests</authors>
                <description>Framework reference test fixture</description>
                <frameworkReferences>
                  {frameworkGroups}
                </frameworkReferences>
              </metadata>
            </package>
            """);
        return TestPackageArchive.CreateWithContent(
            [
                ($"{MaterializedPackageId}.nuspec", nuspec),
                .. entries,
            ]);
    }
}
