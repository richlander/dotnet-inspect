using System.Text.Json;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

public sealed class InstalledImplementationPlatformSourceTests
{
    [Fact]
    public async Task Realize_DotNetRuntimeUsesManifestMembershipAndSnapshots()
    {
        using var hive = new TestHive();
        string directory = hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            [
                Path.GetFileName(
                    typeof(InstalledImplementationPlatformSourceTests)
                        .Assembly.Location),
                Path.GetFileName(typeof(PlatformFamilyTarget).Assembly.Location),
            ]);
        string first = hive.CopyAssembly(
            directory,
            typeof(InstalledImplementationPlatformSourceTests)
                .Assembly.Location);
        string second = hive.CopyAssembly(
            directory,
            typeof(PlatformFamilyTarget).Assembly.Location);
        hive.CopyAssembly(
            directory,
            typeof(InstalledReferencePackSource).Assembly.Location,
            "Unlisted.dll");

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        Assert.Collection(
            succeeded.Value.Frameworks,
            framework =>
            {
                Assert.Equal(
                    "Microsoft.NETCore.App",
                    framework.Name.Value);
                Assert.Equal("11.0.0", framework.Version.Value);
                Assert.Null(framework.RuntimeConfigurationDigest);
            });
        Assert.Equal(2, succeeded.Value.Libraries.Count);
        Assert.DoesNotContain(
            succeeded.Value.Libraries,
            library => library.ManifestCoordinate.FileName == "Unlisted.dll");

        File.Delete(first);
        File.Delete(second);
        Assert.All(
            succeeded.Value.Libraries,
            library =>
            {
                Assert.Equal(64, library.ContentDigest.Value.Length);
                using Stream content = library.OpenRead();
                Assert.True(content.Length > 0);
            });
    }

    [Fact]
    public async Task Realize_AspNetCoreIncludesResolvedRuntimeDependency()
    {
        using var hive = new TestHive();
        string core10 = hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            [Path.GetFileName(typeof(PlatformFamilyTarget).Assembly.Location)]);
        hive.CopyAssembly(
            core10,
            typeof(PlatformFamilyTarget).Assembly.Location);
        string core11 = hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.1",
            runtimeConfiguration: null,
            [Path.GetFileName(typeof(PlatformFamilyTarget).Assembly.Location)]);
        hive.CopyAssembly(
            core11,
            typeof(PlatformFamilyTarget).Assembly.Location);
        string aspnet = hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "11.0.0",
            RuntimeConfiguration(
                "Microsoft.NETCore.App",
                "11.0.0",
                "LatestPatch"),
            [
                Path.GetFileName(
                    typeof(InstalledImplementationPlatformSourceTests)
                        .Assembly.Location),
            ]);
        hive.CopyAssembly(
            aspnet,
            typeof(InstalledImplementationPlatformSourceTests)
                .Assembly.Location);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        Assert.Collection(
            succeeded.Value.Frameworks,
            framework =>
            {
                Assert.Equal(
                    "Microsoft.NETCore.App",
                    framework.Name.Value);
                Assert.Equal("11.0.1", framework.Version.Value);
            },
            framework =>
            {
                Assert.Equal(
                    "Microsoft.AspNetCore.App",
                    framework.Name.Value);
                Assert.Equal("11.0.0", framework.Version.Value);
            });
        Assert.Equal(2, succeeded.Value.Libraries.Count);
    }

    [Fact]
    public async Task Realize_ExactRootNeverRollsForward()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.1",
            runtimeConfiguration: null,
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Unavailable>(outcome);
    }

    [Fact]
    public async Task Realize_RejectsSelectedDependencyWithoutManifest()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "11.0.0",
            RuntimeConfiguration("Other.Framework", "11.0.0", "Minor"),
            []);
        Directory.CreateDirectory(
            Path.Combine(hive.Root, "shared", "Other.Framework", "11.0.0"));

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidManifest,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_ReconcilesReferencesAndPropagatesHighestPolicy()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Framework.C",
            "1.0.0",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Framework.C",
            "1.2.0",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Framework.A",
            "2.0.0",
            RuntimeConfiguration(
                "Framework.C",
                "1.0.0",
                "Minor"),
            []);
        hive.CreateFramework(
            "Framework.B",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.C",
                "1.1.0",
                "Minor"),
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            RuntimeConfiguration(
                ("Framework.A", "1.0.0", "LatestMajor"),
                ("Framework.B", "1.0.0", "Minor")),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        InstalledImplementationFramework frameworkC =
            Assert.Single(
                succeeded.Value.Frameworks,
                framework => framework.Name.Value == "Framework.C");
        Assert.Equal("1.2.0", frameworkC.Version.Value);
        Assert.Equal(
            [
                "Framework.C",
                "Framework.A",
                "Framework.B",
                "Microsoft.AspNetCore.App",
            ],
            succeeded.Value.Frameworks.Select(
                static framework => framework.Name.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realize_LateReferenceReplacesPriorExpansion(
        bool reverseRootReferences)
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Framework.Current",
            "1.0.0",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Framework.A",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.Stale",
                "1.0.0",
                "Minor"),
            []);
        hive.CreateFramework(
            "Framework.A",
            "2.0.0",
            RuntimeConfiguration(
                "Framework.Current",
                "1.0.0",
                "Minor"),
            []);
        hive.CreateFramework(
            "Framework.B",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.A",
                "2.0.0",
                "Major"),
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            reverseRootReferences
                ? RuntimeConfiguration(
                    ("Framework.B", "1.0.0", "Minor"),
                    ("Framework.A", "1.0.0", "Major"))
                : RuntimeConfiguration(
                    ("Framework.A", "1.0.0", "Major"),
                    ("Framework.B", "1.0.0", "Minor")),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        Assert.Contains(
            succeeded.Value.Frameworks,
            framework =>
                framework.Name.Value == "Framework.A"
                && framework.Version.Value == "2.0.0");
        Assert.Contains(
            succeeded.Value.Frameworks,
            framework =>
                framework.Name.Value == "Framework.Current");
        Assert.DoesNotContain(
            succeeded.Value.Frameworks,
            framework =>
                framework.Name.Value == "Framework.Stale");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realize_ManifestOrderDoesNotChangeTightByteBudget(
        bool reverseRootReferences)
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Framework.Z",
            "1.0.0",
            runtimeConfiguration: null,
            []);
        string selectedDirectory = hive.CreateFramework(
            "Framework.Z",
            "2.0.0",
            runtimeConfiguration: null,
            []);
        string upgraderDirectory = hive.CreateFramework(
            "Framework.A",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.Z",
                "2.0.0",
                "Major"),
            []);
        string rootDirectory = hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            reverseRootReferences
                ? RuntimeConfiguration(
                    ("Framework.Z", "1.0.0", "Major"),
                    ("Framework.A", "1.0.0", "Minor"))
                : RuntimeConfiguration(
                    ("Framework.A", "1.0.0", "Minor"),
                    ("Framework.Z", "1.0.0", "Major")),
            []);
        long finalManifestBytes =
            ManifestBytes(rootDirectory)
            + ManifestBytes(upgraderDirectory)
            + ManifestBytes(selectedDirectory);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    Work(finalManifestBytes)),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        Assert.DoesNotContain(
            succeeded.Value.Frameworks,
            framework =>
                framework.Name.Value == "Framework.Z"
                && framework.Version.Value == "1.0.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realize_FrameworkBudgetPrecedesMissingDependencyRegardlessOfOrder(
        bool missingFirst)
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Framework.Good",
            "1.0.0",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            missingFirst
                ? RuntimeConfiguration(
                    ("Framework.Missing", "1.0.0", "Minor"),
                    ("Framework.Good", "1.0.0", "Minor"))
                : RuntimeConfiguration(
                    ("Framework.Good", "1.0.0", "Minor"),
                    ("Framework.Missing", "1.0.0", "Minor")),
            []);
        var work = new InstalledImplementationWorkBudget(
            maxFrameworks: 2,
            maxResolutionSteps: 128,
            maxManifestLibraries: 128,
            maxManifestAssets: 512,
            maxAssemblies: 64,
            maxBytes: 64 * 1024 * 1024);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    work),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Incomplete>(outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realize_DuplicateFrameworkPrecedesBudgetRegardlessOfOrder(
        bool duplicateBeforeUnique)
    {
        using var hive = new TestHive();
        hive.CreateFramework(
                "Microsoft.AspNetCore.App",
                "1.0.0",
                duplicateBeforeUnique
                    ? RuntimeConfiguration(
                        ("Framework.A", "1.0.0", "Minor"),
                        ("Framework.A", "1.0.0", "Minor"),
                        ("Framework.B", "1.0.0", "Minor"))
                    : RuntimeConfiguration(
                        ("Framework.A", "1.0.0", "Minor"),
                        ("Framework.B", "1.0.0", "Minor"),
                        ("Framework.A", "1.0.0", "Minor")),
                []);
        var work = new InstalledImplementationWorkBudget(
                maxFrameworks: 2,
                maxResolutionSteps: 128,
                maxManifestLibraries: 128,
                maxManifestAssets: 512,
                maxAssemblies: 64,
                maxBytes: 64 * 1024 * 1024);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
                outcome = await hive.CreateSource().RealizeAsync(
                    new InstalledImplementationRealizationRequest(
                        hive.Coordinate(
                            InstalledPlatformFamily.AspNetCore,
                            "1.0.0"),
                        work),
                    TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
                InstalledPlatformSourceOutcome<
                    InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
                InstalledPlatformSourceDiagnosticKind.InvalidManifest,
                rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_RejectsFrameworkCycle()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Framework.A",
            "1.0.0",
            RuntimeConfiguration(
                "Microsoft.AspNetCore.App",
                "1.0.0",
                "Disable"),
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.A",
                "1.0.0",
                "Disable"),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidFrameworkGraph,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_IgnoresLowerEqualPrecedenceCandidateAmbiguity()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0+one",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0+two",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.1",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "11.0.0",
            RuntimeConfiguration(
                "Microsoft.NETCore.App",
                "11.0.0",
                "LatestPatch"),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        InstalledImplementationFramework runtime =
            Assert.Single(
                succeeded.Value.Frameworks,
                framework =>
                    framework.Name.Value == "Microsoft.NETCore.App");
        Assert.Equal("11.0.1", runtime.Version.Value);
    }

    [Fact]
    public async Task Realize_RejectsEqualPrecedenceReferencesRegardlessOfOrder()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Framework.SourceA",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.Target",
                "1.1.0",
                "Minor"),
            []);
        hive.CreateFramework(
            "Framework.SourceB",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.Target",
                "1.0.0+one",
                "Minor"),
            []);
        hive.CreateFramework(
            "Framework.SourceC",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.Target",
                "1.0.0+two",
                "Minor"),
            []);
        hive.CreateFramework(
            "Framework.Target",
            "1.1.0",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            RuntimeConfiguration(
                ("Framework.SourceA", "1.0.0", "Minor"),
                ("Framework.SourceB", "1.0.0", "Minor"),
                ("Framework.SourceC", "1.0.0", "Minor")),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidFrameworkGraph,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_RejectsCaseVariantRootVersion()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0-preview.7",
            runtimeConfiguration: null,
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0-PREVIEW.7"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidLayout,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_RejectsProjectedCoordinateCollision()
    {
        using var hive = new TestHive();
        string directory = hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            ["first/Collision.dll", "second/Collision.dll"]);
        File.WriteAllBytes(
            Path.Combine(directory, "Collision.dll"),
            [0]);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    Work(ManifestBytes(directory))),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidMember,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_PreflightsAllProjectionCollisionsBeforeMemberIo()
    {
        using var hive = new TestHive();
        string dependencyAssembly = Path.GetFileName(
            typeof(InstalledImplementationPlatformSourceTests)
                .Assembly.Location);
        string dependencyDirectory = hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            [dependencyAssembly]);
        hive.CopyAssembly(
            dependencyDirectory,
            typeof(InstalledImplementationPlatformSourceTests)
                .Assembly.Location);
        string rootDirectory = hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "11.0.0",
            RuntimeConfiguration(
                "Microsoft.NETCore.App",
                "11.0.0",
                "Disable"),
            ["first/Collision.dll", "second/Collision.dll"]);
        File.WriteAllBytes(
            Path.Combine(rootDirectory, "Collision.dll"),
            [0]);
        long manifestBytes =
            ManifestBytes(dependencyDirectory)
            + ManifestBytes(rootDirectory);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "11.0.0"),
                    Work(manifestBytes)),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidMember,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_RejectsMissingManifestMember()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            ["Missing.dll"]);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidMember,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_RejectsRootedManifestMemberCoordinate()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            ["C:/outside/Managed.dll"]);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidMember,
            rejected.Diagnostic.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realize_InvalidMemberPrecedesAssetBudgetRegardlessOfOrder(
        bool invalidFirst)
    {
        using var hive = new TestHive();
        IReadOnlyList<string> assets = invalidFirst
            ? ["C:/outside/Managed.dll", "Good.dll"]
            : ["Good.dll", "C:/outside/Managed.dll"];
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            assets);
        var work = new InstalledImplementationWorkBudget(
            maxFrameworks: 8,
            maxResolutionSteps: 128,
            maxManifestLibraries: 128,
            maxManifestAssets: 1,
            maxAssemblies: 64,
            maxBytes: 64 * 1024 * 1024);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    work),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidMember,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_RejectsDuplicateAssemblyIdentityAcrossFrameworks()
    {
        using var hive = new TestHive();
        string core = hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            ["CoreCopy.dll"]);
        hive.CopyAssembly(
            core,
            typeof(PlatformFamilyTarget).Assembly.Location,
            "CoreCopy.dll");
        string aspnet = hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "11.0.0",
            RuntimeConfiguration(
                "Microsoft.NETCore.App",
                "11.0.0",
                "Disable"),
            ["AspNetCopy.dll"]);
        hive.CopyAssembly(
            aspnet,
            typeof(PlatformFamilyTarget).Assembly.Location,
            "AspNetCopy.dll");

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.DuplicateAssemblyIdentity,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_RejectsEqualPrecedenceDependencyCandidates()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.1+one",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.1+two",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "11.0.0",
            RuntimeConfiguration(
                "Microsoft.NETCore.App",
                "11.0.0",
                "LatestPatch"),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "11.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidFrameworkGraph,
            rejected.Diagnostic.Kind);
    }

    [Theory]
    [InlineData("Disable", "1.0.0", "1.0.0")]
    [InlineData("LatestPatch", "1.0.0", "1.0.2")]
    [InlineData("Minor", "1.0.3", "1.1.1")]
    [InlineData("LatestMinor", "1.0.0", "1.2.0")]
    [InlineData("Major", "1.3.0", "2.0.1")]
    [InlineData("LatestMajor", "1.0.0", "3.0.0")]
    public async Task Realize_AppliesFrameworkRollForward(
        string rollForward,
        string requestedVersion,
        string expectedVersion)
    {
        using var hive = new TestHive();
        foreach (string version in new[]
        {
            "1.0.0",
            "1.0.2",
            "1.1.1",
            "1.2.0",
            "2.0.1",
            "2.1.0",
            "3.0.0",
        })
        {
            hive.CreateFramework(
                "Framework.A",
                version,
                runtimeConfiguration: null,
                []);
        }
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            RuntimeConfiguration(
                "Framework.A",
                requestedVersion,
                rollForward),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        InstalledImplementationFramework selected =
            Assert.Single(
                succeeded.Value.Frameworks,
                framework => framework.Name.Value == "Framework.A");
        Assert.Equal(expectedVersion, selected.Version.Value);
    }

    [Fact]
    public async Task Realize_ApplyPatchesFalseKeepsExactPatch()
    {
        using var hive = new TestHive();
        hive.CreateFramework(
            "Framework.A",
            "1.0.0",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Framework.A",
            "1.0.2",
            runtimeConfiguration: null,
            []);
        hive.CreateFramework(
            "Microsoft.AspNetCore.App",
            "1.0.0",
            LegacyRuntimeConfiguration(
                "Framework.A",
                "1.0.0",
                applyPatches: false),
            []);

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.AspNetCore,
                        "1.0.0"),
                    Work()),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded>(outcome);
        InstalledImplementationFramework selected =
            Assert.Single(
                succeeded.Value.Frameworks,
                framework => framework.Name.Value == "Framework.A");
        Assert.Equal("1.0.0", selected.Version.Value);
    }

    [Fact]
    public async Task Realize_ReturnsIncompleteBeforePublishingPartialClosure()
    {
        using var hive = new TestHive();
        string directory = hive.CreateFramework(
            "Microsoft.NETCore.App",
            "11.0.0",
            runtimeConfiguration: null,
            [
                Path.GetFileName(
                    typeof(InstalledImplementationPlatformSourceTests)
                        .Assembly.Location),
                Path.GetFileName(typeof(PlatformFamilyTarget).Assembly.Location),
            ]);
        hive.CopyAssembly(
            directory,
            typeof(InstalledImplementationPlatformSourceTests)
                .Assembly.Location);
        hive.CopyAssembly(
            directory,
            typeof(PlatformFamilyTarget).Assembly.Location);

        var work = new InstalledImplementationWorkBudget(
            maxFrameworks: 8,
            maxResolutionSteps: 128,
            maxManifestLibraries: 128,
            maxManifestAssets: 512,
            maxAssemblies: 1,
            maxBytes: 64 * 1024 * 1024);
        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    work),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Incomplete>(outcome);
    }

    [Fact]
    public async Task Realize_ObservesFrameworkDirectoryOnce()
    {
        using var hive = new TestHive();
        string assemblyName = Path.GetFileName(
                typeof(InstalledImplementationPlatformSourceTests)
                    .Assembly.Location);
        string directory = hive.CreateFramework(
                "Microsoft.NETCore.App",
                "11.0.0",
                runtimeConfiguration: null,
                [assemblyName]);
        hive.CopyAssembly(
                directory,
                typeof(InstalledImplementationPlatformSourceTests)
                    .Assembly.Location);
        for (int index = 0; index < 4; index++)
        {
                File.WriteAllText(
                    Path.Combine(directory, $"unrelated-{index}.txt"),
                    "");
        }

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
                outcome = await hive.CreateSource(
                        maxObservedEntries: 9)
                    .RealizeAsync(
                        new InstalledImplementationRealizationRequest(
                            hive.Coordinate(
                                InstalledPlatformFamily.DotNetRuntime,
                                "11.0.0"),
                            Work()),
                        TestContext.Current.CancellationToken);

        Assert.IsType<
                InstalledPlatformSourceOutcome<
                    InstalledImplementationRealization>.Succeeded>(outcome);
    }

    [Fact]
    public async Task Realize_PreservesCancellationBeforeZeroBudgetOutcome()
    {
        using var hive = new TestHive();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await hive.CreateSource().RealizeAsync(
                new InstalledImplementationRealizationRequest(
                    hive.Coordinate(
                        InstalledPlatformFamily.DotNetRuntime,
                        "11.0.0"),
                    new InstalledImplementationWorkBudget(
                        maxFrameworks: 0,
                        maxResolutionSteps: 0,
                        maxManifestLibraries: 0,
                        maxManifestAssets: 0,
                        maxAssemblies: 0,
                        maxBytes: 0)),
                cancellation.Token));
    }

    private static InstalledImplementationWorkBudget Work(
        long maxBytes = 64 * 1024 * 1024) =>
        new(
            maxFrameworks: 8,
            maxResolutionSteps: 128,
            maxManifestLibraries: 128,
            maxManifestAssets: 512,
            maxAssemblies: 64,
            maxBytes);

    private static long ManifestBytes(string directory) =>
        Directory.EnumerateFiles(directory, "*.json")
            .Sum(static path => new FileInfo(path).Length);

    private static string RuntimeConfiguration(
        string family,
        string version,
        string rollForward) =>
        RuntimeConfiguration((family, version, rollForward));

    private static string RuntimeConfiguration(
        params (string Family, string Version, string RollForward)[]
            frameworks) =>
        JsonSerializer.Serialize(
            new
            {
                runtimeOptions = new
                {
                    frameworks = frameworks.Select(
                        static framework => new
                        {
                            name = framework.Family,
                            version = framework.Version,
                            rollForward = framework.RollForward,
                        }),
                },
            });

    private static string LegacyRuntimeConfiguration(
        string family,
        string version,
        bool applyPatches) =>
        JsonSerializer.Serialize(
            new
            {
                runtimeOptions = new
                {
                    applyPatches,
                    framework = new
                    {
                        name = family,
                        version,
                    },
                },
            });

    private sealed class TestHive : IDisposable
    {
        internal TestHive()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-inspect-installed-implementation-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Identity = InstalledDotnetHiveIdentity.Create(
                "test-hive-" + Guid.NewGuid().ToString("N"));
        }

        internal string Root { get; }
        internal InstalledDotnetHiveIdentity Identity { get; }

        internal InstalledImplementationPlatformSource CreateSource(
            int maxObservedEntries =
                InstalledImplementationPlatformSource
                    .DefaultMaxObservedEntries) =>
            new(
                Identity,
                Root,
                maxObservedEntries: maxObservedEntries);

        internal InstalledImplementationPlatformCoordinate Coordinate(
            InstalledPlatformFamily family,
            string version) =>
            new(Identity, family, PlatformVersion.Parse(version));

        internal string CreateFramework(
            string family,
            string version,
            string? runtimeConfiguration,
            IReadOnlyList<string> runtimeAssets)
        {
            string directory = Path.Combine(
                Root,
                "shared",
                family,
                version);
            Directory.CreateDirectory(directory);
            if (runtimeConfiguration is not null)
            {
                File.WriteAllText(
                    Path.Combine(
                        directory,
                        family + ".runtimeconfig.json"),
                    runtimeConfiguration);
            }

            var runtime = runtimeAssets.ToDictionary(
                static asset => asset,
                static _ => (object)new { },
                StringComparer.Ordinal);
            var targetLibrary = new Dictionary<string, object>(
                StringComparer.Ordinal)
            {
                ["runtime"] = runtime,
            };
            var selectedTarget = new Dictionary<string, object>(
                StringComparer.Ordinal)
            {
                [family + ".Runtime/" + version] = targetLibrary,
            };
            var targets = new Dictionary<string, object>(
                StringComparer.Ordinal)
            {
                ["target"] = selectedTarget,
            };
            File.WriteAllText(
                Path.Combine(directory, family + ".deps.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        runtimeTarget = new { name = "target" },
                        targets,
                    }));
            return directory;
        }

        internal string CopyAssembly(
            string directory,
            string sourcePath,
            string? fileName = null)
        {
            string path = Path.Combine(
                directory,
                fileName ?? Path.GetFileName(sourcePath));
            File.Copy(sourcePath, path);
            return path;
        }

        public void Dispose() =>
            Directory.Delete(Root, recursive: true);
    }
}
