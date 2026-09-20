using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class RealizedPackageDependencyContextQueryTests
{
    const string PackageId = "example.package";
    const string Version = "1.0.0";

    [Fact]
    public async Task ExecuteAsync_UsesFrozenRootSelectionIntent()
    {
        var cases = new[]
        {
            new SelectionCase(
                "framework-neutral",
                Target: null,
                Compatible: false,
                Assets: Array.Empty<string>(),
                Groups:
                    """
                    <dependency id="Universal.Dependency" version="[1.0.0]" />
                    """,
                ExpectedRequested: null,
                ExpectedSelected: "any",
                PackageDependencyEvidenceSelectionStatus.Selected),
            new SelectionCase(
                "exact-only",
                "net11.0",
                Compatible: false,
                Assets: ["lib/net11.0/Example.Package.dll"],
                Groups:
                    """
                    <group targetFramework="net11.0">
                      <dependency id="Exact.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                ExpectedRequested: "net11.0",
                ExpectedSelected: "net11.0",
                PackageDependencyEvidenceSelectionStatus.Selected),
            new SelectionCase(
                "exact-asset-compatible-group",
                "net11.0",
                Compatible: true,
                Assets: ["lib/net11.0/Example.Package.dll"],
                Groups:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Compatible.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                ExpectedRequested: "net11.0",
                ExpectedSelected: "net8.0",
                PackageDependencyEvidenceSelectionStatus.Selected),
            new SelectionCase(
                "compatible-asset",
                "net11.0",
                Compatible: true,
                Assets: ["lib/net8.0/Example.Package.dll"],
                Groups:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Compatible.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                ExpectedRequested: "net11.0",
                ExpectedSelected: "net8.0",
                PackageDependencyEvidenceSelectionStatus.Selected),
            new SelectionCase(
                "no-match",
                "net11.0",
                Compatible: false,
                Assets: ["lib/net11.0/Example.Package.dll"],
                Groups:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Other.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                ExpectedRequested: "net11.0",
                ExpectedSelected: null,
                PackageDependencyEvidenceSelectionStatus
                    .NoMatchingTargetFramework),
            new SelectionCase(
                "different-nearest-frameworks",
                "net11.0",
                Compatible: true,
                Assets: ["lib/net8.0/Example.Package.dll"],
                Groups:
                    """
                    <group targetFramework="net9.0">
                      <dependency id="Nearest.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                ExpectedRequested: "net11.0",
                ExpectedSelected: "net9.0",
                PackageDependencyEvidenceSelectionStatus.Selected),
        };

        foreach (SelectionCase testCase in cases)
        {
            PackageRootBinding binding = Binding(
                Package(
                    testCase.Groups,
                    testCase.Assets),
                testCase.Target,
                testCase.Compatible);

            RealizedPackageDependencyContextResult.Available result =
                Assert.IsType<
                    RealizedPackageDependencyContextResult.Available>(
                    await RealizedPackageDependencyContextQuery.ExecuteAsync(
                        binding,
                        TestContext.Current.CancellationToken));

            Assert.Same(binding.ContentGenerationIdentity,
                result.Subject.ContentGeneration);
            Assert.Same(binding.SelectionIdentity, result.Subject.Selection);
            Assert.Equal(
                testCase.ExpectedRequested,
                result.Context.Evidence.Selection.RequestedFramework
                    ?.ToString());
            Assert.Equal(
                testCase.ExpectedSelected,
                result.Context.Evidence.Selection.SelectedFramework
                    ?.ToString());
            Assert.Equal(
                testCase.ExpectedStatus,
                result.Context.Evidence.Selection.Status);
            if (testCase.Name == "exact-asset-compatible-group")
            {
                Assert.True(
                    result.Subject.RootRequest
                        .AllowsCompatibleTargetSelection);
                Assert.False(
                    result.Subject.RootRequest
                        .UsesCompatibleImplementationSelection);
            }
            if (testCase.Name == "different-nearest-frameworks")
            {
                Assert.Equal(
                    "net8.0",
                    binding.Root.AssetSelection.TargetFramework);
                Assert.Equal(
                    "net9.0",
                    result.Context.Evidence.Selection.SelectedFramework
                        ?.ToString());
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_PreservesClosedResultAlgebra()
    {
        RealizedPackageDependencyContextResult.Available selectedEmpty =
            await AvailableAsync(
                Package(
                    """<group targetFramework="net8.0" />""",
                    ["lib/net8.0/Example.Package.dll"]),
                "net8.0");
        RealizedPackageDependencyContextResult.Available noGroups =
            await AvailableAsync(
                Package("", ["lib/net8.0/Example.Package.dll"]),
                "net8.0");
        RealizedPackageDependencyContextResult.Available noMatch =
            await AvailableAsync(
                Package(
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Example.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                    ["lib/net11.0/Example.Package.dll"]),
                "net11.0");
        RealizedPackageDependencyContextResult unavailable =
            await RealizedPackageDependencyContextQuery.ExecuteAsync(
                Binding(
                    PackageWithoutManifest("lib/net8.0/Example.Package.dll"),
                    "net8.0"),
                TestContext.Current.CancellationToken);
        RealizedPackageDependencyContextResult failed =
            await RealizedPackageDependencyContextQuery.ExecuteAsync(
                Binding(
                    PackageWithManifest("<not-package />",
                        "lib/net8.0/Example.Package.dll"),
                    "net8.0"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus.Selected,
            selectedEmpty.Context.Evidence.Selection.Status);
        Assert.Empty(SelectedGroup(selectedEmpty).Declarations);
        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus.NoDependencyGroups,
            noGroups.Context.Evidence.Selection.Status);
        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus
                .NoMatchingTargetFramework,
            noMatch.Context.Evidence.Selection.Status);
        Assert.Equal(
            RealizedPackageDependencyUnavailableReason.NoManifest,
            Assert.IsType<
                RealizedPackageDependencyContextResult.Unavailable>(
                    unavailable).Reason);
        Assert.NotNull(
            Assert.IsType<RealizedPackageDependencyContextResult.Failed>(
                failed).Failure.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RetainsIncompleteSelectedEvidence()
    {
        RealizedPackageDependencyContextResult.Available result =
            await AvailableAsync(
                Package(
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Usable.Dependency" version="[1.0.0]" />
                      <dependency id="Conflicting.Dependency" version="[1.0.0]" />
                      <dependency id="conflicting.dependency" version="[2.0.0]" />
                    </group>
                    """,
                    ["lib/net8.0/Example.Package.dll"]),
                "net8.0");
        PackageDependencyEvidenceDeclarationResult.Available declaration =
            Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                    result.Context.Evidence.Declaration);

        Assert.False(declaration.IsComplete);
        Assert.Equal(
            "usable.dependency",
            Assert.Single(SelectedGroup(result).Declarations)
                .CanonicalPackageId);
        Assert.IsType<
            PackageDependencyEvidenceDeclarationFailure
                .ConflictingPackageDeclaration>(
                    Assert.Single(declaration.Failures));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotExchangeEqualCoordinateContexts()
    {
        PackageRootBinding first = Binding(
            Package(
                """
                <group targetFramework="net8.0">
                  <dependency id="First.Dependency" version="[1.0.0]" />
                </group>
                """,
                ["lib/net8.0/Example.Package.dll"]),
            "net8.0");
        PackageRootBinding second = Binding(
            Package(
                """
                <group targetFramework="net8.0">
                  <dependency id="Second.Dependency" version="[2.0.0]" />
                </group>
                """,
                ["lib/net8.0/Example.Package.dll"]),
            "net8.0");

        RealizedPackageDependencyContextResult.Available firstResult =
            await ExecuteAvailableAsync(first);
        RealizedPackageDependencyContextResult.Available secondResult =
            await ExecuteAvailableAsync(second);

        Assert.Equal(
            firstResult.Subject.RootRequest,
            secondResult.Subject.RootRequest);
        Assert.NotSame(
            firstResult.Subject.ContentGeneration,
            secondResult.Subject.ContentGeneration);
        Assert.NotSame(
            firstResult.Subject.Selection,
            secondResult.Subject.Selection);
        Assert.Equal(
            "first.dependency",
            Assert.Single(SelectedGroup(firstResult).Declarations)
                .CanonicalPackageId);
        Assert.Equal(
            "second.dependency",
            Assert.Single(SelectedGroup(secondResult).Declarations)
                .CanonicalPackageId);
    }

    [Fact]
    public async Task ExecuteAsync_ReissuesContextForSameAndReplacementGenerations()
    {
        byte[] package = Package(
            """
            <group targetFramework="net8.0">
              <dependency id="Example.Dependency" version="[1.0.0]" />
            </group>
            """,
            ["lib/net8.0/Example.Package.dll"]);
        var content = new InMemoryPackageContent(
            package,
            fromCache: false,
            producerKey: "tests");
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(PackageId, Version),
            content,
            "tests",
            PackagePayloadOrigin.Download);
        PackageRootBinding first =
            PackageRootBinding.CreateFromSource(payload, "net8.0");
        PackageRootBinding cacheHit =
            PackageRootBinding.CreateFromSource(payload, "net8.0");
        PackageRootBinding replacement = Binding(package, "net8.0");

        RealizedPackageDependencyContextResult.Available firstResult =
            await ExecuteAvailableAsync(first);
        RealizedPackageDependencyContextResult.Available cacheHitResult =
            await ExecuteAvailableAsync(cacheHit);
        RealizedPackageDependencyContextResult.Available replacementResult =
            await ExecuteAvailableAsync(replacement);

        Assert.Equal(
            firstResult.Subject.RootRequest,
            cacheHitResult.Subject.RootRequest);
        Assert.Equal(
            firstResult.Subject.RootRequest,
            replacementResult.Subject.RootRequest);
        Assert.Same(
            firstResult.Subject.ContentGeneration,
            cacheHitResult.Subject.ContentGeneration);
        Assert.NotSame(
            firstResult.Subject.Selection,
            cacheHitResult.Subject.Selection);
        Assert.NotSame(
            firstResult.Subject.ContentGeneration,
            replacementResult.Subject.ContentGeneration);
        Assert.NotSame(
            firstResult.Subject.Selection,
            replacementResult.Subject.Selection);
    }

    [Fact]
    public async Task ExecuteAsync_ExternalConsumerObservesDetachedPublicShape()
    {
        PackageRootBinding binding = Binding(
            Package(
                """
                <group targetFramework="net8.0">
                  <dependency id="Example.Dependency" version="[1.0.0]" />
                </group>
                """,
                ["lib/net8.0/Example.Package.dll"]),
            "net8.0");

        RealizedPackageDependencyObservation observation =
            await RealizedPackageDependencyContextConsumer.ObserveAsync(
                binding,
                TestContext.Current.CancellationToken);

        Assert.Equal("available", observation.Status);
        Assert.Equal(
            binding.CreateReacquisitionRequest(),
            observation.RootRequest);
        Assert.Same(binding.SelectionIdentity, observation.Selection);
        Assert.Same(
            binding.ContentGenerationIdentity,
            observation.ContentGeneration);
        Assert.NotNull(observation.Evidence);
        Assert.DoesNotContain(
            typeof(PackageRootBinding),
            typeof(RealizedPackageDependencyContext)
                .GetProperties()
                .Select(property => property.PropertyType));
    }

    [Fact]
    public async Task PollyCore_RetainsSourceDeclarationsAndCompatibleEmptyGroup()
    {
        byte[] nupkg = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "RealizedPackageDependencyContext",
                "polly.core.8.8.0.nupkg"));
        PackageRootBinding exact = Binding(
            nupkg,
            "netstandard2.0",
            compatible: false,
            packageId: "polly.core",
            version: "8.8.0");
        PackageRootBinding compatible = Binding(
            nupkg,
            "net11.0",
            compatible: true,
            packageId: "polly.core",
            version: "8.8.0");

        RealizedPackageDependencyContextResult.Available exactResult =
            await ExecuteAvailableAsync(exact);
        RealizedPackageDependencyContextResult.Available compatibleResult =
            await ExecuteAvailableAsync(compatible);

        Assert.Equal(
            [
                "microsoft.bcl.asyncinterfaces",
                "microsoft.bcl.timeprovider",
                "system.componentmodel.annotations",
                "system.threading.tasks.extensions",
            ],
            SelectedGroup(exactResult).Declarations
                .Select(declaration => declaration.CanonicalPackageId)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ".NETStandard2.0",
            exactResult.Context.Evidence.Selection.SelectedFramework
                ?.ToString());
        Assert.Equal(
            "net8.0",
            compatibleResult.Context.Evidence.Selection.SelectedFramework
                ?.ToString());
        Assert.Empty(SelectedGroup(compatibleResult).Declarations);

        Assert.True(
            exact.Root.Content.TryOpenEntry(
                "lib/netstandard2.0/Polly.Core.dll",
                out Stream? assembly));
        using (assembly)
        using (var pe = new PEReader(assembly))
        {
            MetadataReader metadata = pe.GetMetadataReader();
            string[] references =
            [
                .. metadata.AssemblyReferences.Select(handle =>
                    AssemblyReferenceIdentity.From(metadata, handle).Name),
            ];
            Assert.All(
                new[]
                {
                    "Microsoft.Bcl.AsyncInterfaces",
                    "Microsoft.Bcl.TimeProvider",
                    "System.ComponentModel.Annotations",
                    "System.Threading.Tasks.Extensions",
                },
                expected => Assert.Contains(expected, references));
        }
    }

    private static async ValueTask<
        RealizedPackageDependencyContextResult.Available> AvailableAsync(
            byte[] nupkg,
            string? target) =>
        await ExecuteAvailableAsync(Binding(nupkg, target));

    private static async ValueTask<
        RealizedPackageDependencyContextResult.Available>
        ExecuteAvailableAsync(PackageRootBinding binding) =>
        Assert.IsType<RealizedPackageDependencyContextResult.Available>(
            await RealizedPackageDependencyContextQuery.ExecuteAsync(
                binding,
                TestContext.Current.CancellationToken));

    private static PackageDependencyEvidenceGroup SelectedGroup(
        RealizedPackageDependencyContextResult.Available result)
    {
        PackageDependencyEvidenceDeclarationResult.Available declaration =
            Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                    result.Context.Evidence.Declaration);
        return Assert.Single(
            declaration.Groups,
            group =>
                group.Identity
                == result.Context.Evidence.Selection.SelectedGroup);
    }

    private static PackageRootBinding Binding(
        byte[] nupkg,
        string? target,
        bool compatible = false,
        string packageId = PackageId,
        string version = Version)
    {
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, version),
            new InMemoryPackageContent(
                nupkg,
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        return compatible
            ? PackageRootBinding.CreateFromSourceWithCompatibleSelection(
                payload,
                target
                    ?? throw new ArgumentNullException(nameof(target)))
            : PackageRootBinding.CreateFromSource(payload, target);
    }

    private static byte[] Package(
        string dependencyXml,
        IReadOnlyCollection<string> assets) =>
        PackageWithManifest(
            $$"""
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <authors>Example</authors>
                <description>Example</description>
                <dependencies>
                  {{dependencyXml}}
                </dependencies>
              </metadata>
            </package>
            """,
            assets.ToArray());

    private static byte[] PackageWithoutManifest(params string[] assets) =>
        Archive(assets.Select(path => (path, new byte[] { 0x01 })));

    private static byte[] PackageWithManifest(
        string manifest,
        params string[] assets) =>
        Archive(
            [
                ("Example.Package.nuspec", Encoding.UTF8.GetBytes(manifest)),
                .. assets.Select(path => (path, new byte[] { 0x01 })),
            ]);

    private static byte[] Archive(
        IEnumerable<(string Path, byte[] Content)> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed record SelectionCase(
        string Name,
        string? Target,
        bool Compatible,
        string[] Assets,
        string Groups,
        string? ExpectedRequested,
        string? ExpectedSelected,
        PackageDependencyEvidenceSelectionStatus ExpectedStatus);
}
