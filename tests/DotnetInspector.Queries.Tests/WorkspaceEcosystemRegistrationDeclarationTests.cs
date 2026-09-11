using System.Collections;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.QueriesConsumer;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceEcosystemRegistrationDeclarationTests
{
    private const string RealPackageVersion =
        "11.0.0-preview.7.26381.103";

    [Theory]
    [InlineData("ecosystem.platform")]
    [InlineData("ecosystem.aspnetcore")]
    [InlineData("ecosystem.microsoft-extensions")]
    [InlineData("ecosystem.a1-b2")]
    public void IdentityAcceptsCanonicalExternalSpelling(string value)
    {
        Assert.True(
            WorkspaceEcosystemRegistrationId.TryCreate(
                value,
                out WorkspaceEcosystemRegistrationId? parsed));

        WorkspaceEcosystemRegistrationId created =
            WorkspaceEcosystemRegistrationId.Create(value);
        Assert.Equal(value, parsed.Value);
        Assert.Equal(parsed, created);
        Assert.Equal(value, created.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("platform")]
    [InlineData("Ecosystem.platform")]
    [InlineData("ecosystem.Platform")]
    [InlineData("ecosystem.1platform")]
    [InlineData("ecosystem.-platform")]
    [InlineData("ecosystem.platform-")]
    [InlineData("ecosystem.platform--runtime")]
    [InlineData("ecosystem.platform_runtime")]
    public void IdentityRejectsNonCanonicalExternalSpelling(string? value)
    {
        Assert.False(
            WorkspaceEcosystemRegistrationId.TryCreate(value, out _));
        if (value is not null)
        {
            Assert.Throws<ArgumentException>(
                () => WorkspaceEcosystemRegistrationId.Create(value));
        }
    }

    [Fact]
    public void PublicConsumerRetainsAllContributionsAndImmutableOrder()
    {
        var id = WorkspaceEcosystemRegistrationId.Create(
            "ecosystem.system-text-json");
        string[] namespaceRoots = ["System.Text.Json", "System.Text"];
        var corePackage = new PackageCoordinate("System.Text.Json");
        PackageCoordinate[] corePackages =
        [
            corePackage,
            new("System.Text.Encodings.Web"),
        ];
        ExactLibrarySourceCoordinate exactLibrary =
            RealPackageSystemTextJson();
        var platform = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        var prefix = new PackagePrefixDeclaration("System.Text.");
        WorkspaceEcosystemPopulationDeclaration[] populations =
        [
            new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                exactLibrary),
            new WorkspaceEcosystemPopulationDeclaration.Platform(platform),
            new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(prefix),
        ];
        EcosystemIntegrationScannerBinding scanner =
            EcosystemIntegrationScanner.AspireBinding;

        var declaration = new WorkspaceEcosystemRegistrationDeclaration(
            id,
            namespaceRoots,
            corePackages,
            populations,
            scanner);

        namespaceRoots[0] = "Changed";
        corePackages[0] = new("Changed");
        populations[0] =
            new WorkspaceEcosystemPopulationDeclaration.Platform(
                new(PlatformFamily.AspNetCore));

        Assert.Equal(
            ["System.Text.Json", "System.Text"],
            declaration.NamespaceRoots);
        Assert.Same(corePackage, declaration.CorePackages[0]);
        Assert.Same(
            exactLibrary,
            Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.ExactLibrary>(
                    declaration.Populations[0]).Coordinate);
        Assert.Same(
            platform,
            Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.Platform>(
                    declaration.Populations[1]).Population);
        Assert.Same(
            prefix,
            Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
                    declaration.Populations[2]).Prefix);
        Assert.Same(scanner, declaration.IntegrationScanner);

        WorkspaceEcosystemRegistrationObservation observation =
            WorkspaceEcosystemRegistrationConsumer.Observe(declaration);
        Assert.Same(id, observation.Id);
        Assert.Equal(declaration.NamespaceRoots, observation.NamespaceRoots);
        Assert.Equal(declaration.CorePackages, observation.CorePackages);
        Assert.Same(exactLibrary, Assert.Single(observation.ExactLibraries));
        Assert.Same(platform, Assert.Single(observation.PlatformPopulations));
        Assert.Same(prefix, Assert.Single(observation.PackagePrefixes));
        Assert.Same(scanner, observation.IntegrationScanner);
    }

    [Fact]
    public void DuplicateContributionsAreRejectedByTheirOwnedIdentity()
    {
        Assert.Throws<ArgumentException>(
            () => Declaration(namespaceRoots: ["System", "System"]));
        Assert.Throws<ArgumentException>(
            () => Declaration(
                corePackages:
                [
                    new("System.Text.Json"),
                    new("system.text.json"),
                ]));

        ExactLibrarySourceCoordinate exact = ExactLibrary(
            "Contoso.Json",
            new Version(1, 0, 0, 0));
        ExactLibrarySourceCoordinate equivalentExact = ExactLibrary(
            "contoso.json",
            new Version(1, 0, 0, 0));
        Assert.Throws<ArgumentException>(
            () => Declaration(
                populations:
                [
                    new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                        exact),
                    new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                        equivalentExact),
                ]));

        var platform = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        Assert.Throws<ArgumentException>(
            () => Declaration(
                populations:
                [
                    new WorkspaceEcosystemPopulationDeclaration.Platform(
                        platform),
                    new WorkspaceEcosystemPopulationDeclaration.Platform(
                        new(PlatformFamily.DotNetRuntime)),
                ]));

        var prefix = new PackagePrefixDeclaration("Contoso.");
        Assert.Throws<ArgumentException>(
            () => Declaration(
                populations:
                [
                    new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                        prefix),
                    new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                        new("Contoso.")),
                ]));
    }

    [Fact]
    public void CrossArmOverlapRemainsDistinct()
    {
        var runtime = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        var exactPlatform = new ExactLibrarySourceCoordinate.Platform(
            runtime,
            Assembly("System.Text.Json", new Version(11, 0, 0, 0)));
        ExactLibrarySourceCoordinate exactPackage = ExactLibrary(
            "System.Text.Json",
            new Version(11, 0, 0, 0));

        WorkspaceEcosystemRegistrationDeclaration declaration = Declaration(
            populations:
            [
                new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                    exactPlatform),
                new WorkspaceEcosystemPopulationDeclaration.Platform(runtime),
                new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                    exactPackage),
                new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new("System.Text.")),
            ]);

        Assert.Equal(4, declaration.Populations.Length);
    }

    [Fact]
    public void EmptyDeclarationIsRejectedWhileKnowledgeOrScannerIsSufficient()
    {
        Assert.Throws<ArgumentException>(() => Declaration());

        Assert.Equal(
            "System",
            Assert.Single(Declaration(namespaceRoots: ["System"])
                .NamespaceRoots));
        Assert.Equal(
            "System.Text.Json",
            Assert.Single(
                Declaration(corePackages: [new("System.Text.Json")])
                    .CorePackages)
                .PackageId);
        Assert.Same(
            EcosystemIntegrationScanner.AspireBinding,
            Declaration(
                scanner: EcosystemIntegrationScanner.AspireBinding)
                .IntegrationScanner);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".System")]
    [InlineData("System.")]
    [InlineData("System..Text")]
    [InlineData("System.*")]
    [InlineData("System ?")]
    public void MalformedNamespaceRootIsRejected(string root)
    {
        Assert.Throws<ArgumentException>(
            () => Declaration(namespaceRoots: [root]));
    }

    [Fact]
    public void InvalidOrTargetSpecificCorePackageIsRejected()
    {
        PackageCoordinate[] invalid =
        [
            new("invalid package"),
            new("Contoso", Version: "1.0.0"),
            new("Contoso", Framework: "net10.0"),
            new("Contoso", RuntimeIdentifier: "linux-x64"),
        ];

        Assert.All(
            invalid,
            package => Assert.Throws<ArgumentException>(
                () => Declaration(corePackages: [package])));
    }

    [Fact]
    public void NullSequencesAndElementsAreRejected()
    {
        WorkspaceEcosystemRegistrationId id =
            WorkspaceEcosystemRegistrationId.Create("ecosystem.test");

        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceEcosystemRegistrationDeclaration(
                id,
                null!,
                [],
                []));
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceEcosystemRegistrationDeclaration(
                id,
                [],
                null!,
                []));
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceEcosystemRegistrationDeclaration(
                id,
                [],
                [],
                null!));
        Assert.Throws<ArgumentException>(
            () => Declaration(namespaceRoots: [null!]));
        Assert.Throws<ArgumentException>(
            () => Declaration(corePackages: [null!]));
        Assert.Throws<ArgumentException>(
            () => Declaration(populations: [null!]));
    }

    [Fact]
    public void DefaultImmutableSequencesAreRejectedDeliberately()
    {
        WorkspaceEcosystemRegistrationId id =
            WorkspaceEcosystemRegistrationId.Create("ecosystem.test");
        ImmutableArray<string> roots = default;
        ImmutableArray<PackageCoordinate> packages = default;
        ImmutableArray<WorkspaceEcosystemPopulationDeclaration> populations =
            default;

        Assert.Throws<ArgumentException>(
            () => new WorkspaceEcosystemRegistrationDeclaration(
                id,
                roots,
                [],
                []));
        Assert.Throws<ArgumentException>(
            () => new WorkspaceEcosystemRegistrationDeclaration(
                id,
                [],
                packages,
                []));
        Assert.Throws<ArgumentException>(
            () => new WorkspaceEcosystemRegistrationDeclaration(
                id,
                [],
                [],
                populations));
    }

    [Fact]
    public void ContributionSequencesAreEnumeratedExactlyOnce()
    {
        var roots = new OneShotEnumerable<string>(["System"]);
        var packages = new OneShotEnumerable<PackageCoordinate>(
            [new("System.Text.Json")]);
        var populations =
            new OneShotEnumerable<WorkspaceEcosystemPopulationDeclaration>(
            [
                new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new("System.Text.")),
            ]);

        WorkspaceEcosystemRegistrationDeclaration declaration = new(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.test"),
            roots,
            packages,
            populations);

        Assert.Equal(1, roots.EnumerationCount);
        Assert.Equal(1, packages.EnumerationCount);
        Assert.Equal(1, populations.EnumerationCount);
        Assert.Single(declaration.NamespaceRoots);
        Assert.Single(declaration.CorePackages);
        Assert.Single(declaration.Populations);
    }

    [Fact]
    public void PopulationArmsRejectNullOwnerValues()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                null!));
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceEcosystemPopulationDeclaration.Platform(
                null!));
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                null!));
    }

    private static WorkspaceEcosystemRegistrationDeclaration Declaration(
        IEnumerable<string>? namespaceRoots = null,
        IEnumerable<PackageCoordinate>? corePackages = null,
        IEnumerable<WorkspaceEcosystemPopulationDeclaration>? populations =
            null,
        EcosystemIntegrationScannerBinding? scanner = null) =>
        new(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.test"),
            namespaceRoots ?? [],
            corePackages ?? [],
            populations ?? [],
            scanner);

    private static ExactLibrarySourceCoordinate RealPackageSystemTextJson()
    {
        using var stream = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "BindingComposition",
                "package",
                "System.Text.Json.dll"));
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create(
                "System.Text.Json",
                RealPackageVersion),
            new(
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader)));
    }

    private static ExactLibrarySourceCoordinate ExactLibrary(
        string assemblyName,
        Version assemblyVersion) =>
        new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create("Contoso.Json", "1.0.0"),
            Assembly(assemblyName, assemblyVersion));

    private static ManagedMetadataIdentity.Assembly Assembly(
        string name,
        Version version) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                version,
                Culture: null,
                PublicKeyToken: null));

    private sealed class OneShotEnumerable<T>(
        IEnumerable<T> values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException(
                    "The contribution sequence was enumerated more than once.");
            }

            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
