using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Tests;

public partial class EcosystemFindIndexTests
{
    [Fact]
    public void Build_IsDeterministicAcrossSourceArrivalAndRoundTrips()
    {
        IReadOnlyList<EcosystemIntegrationSignalInfo> signals = ScanFixture();
        EcosystemFindIndexSourceInput first = Source(
            prefixOrdinal: 0,
            packageOrdinal: 0,
            packageId: "Fixture.Core",
            assetPath: "lib/net10.0/Fixture.Core.dll",
            assemblyName: "Fixture.Core",
            signals);
        EcosystemFindIndexSourceInput second = Source(
            prefixOrdinal: 1,
            packageOrdinal: 0,
            packageId: "Second.Extensions",
            assetPath: "lib/net10.0/Second.Extensions.dll",
            assemblyName: "Second.Extensions",
            signals);

        byte[] forward = EcosystemFindIndexJson.Serialize(
            EcosystemFindIndexBuilder.Build(Request([first, second])));
        byte[] reversed = EcosystemFindIndexJson.Serialize(
            EcosystemFindIndexBuilder.Build(Request([second, first])));

        Assert.Equal(forward, reversed);
        var available = Assert.IsType<EcosystemFindIndexLoadResult.Available>(
            EcosystemFindIndexJson.Load(forward));
        Assert.Equal(EcosystemPackIds.Aspire, available.Artifact.Ecosystem);
        Assert.Equal(["Fixture.", "Second."],
            available.Artifact.Prefixes.Select(prefix => prefix.Prefix));
        Assert.Equal(2, available.Artifact.Sources.Length);
        Assert.Equal(6, available.Artifact.Symbols.Length);
        Assert.Equal(forward, EcosystemFindIndexJson.Serialize(available.Artifact));
    }

    [Fact]
    public void Build_MergesTypeConceptsAndExpandsEveryMemberOverload()
    {
        EcosystemFindIndexArtifact artifact = EcosystemFindIndexBuilder.Build(
            Request(
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]));

        EcosystemFindIndexSymbol type = Assert.Single(
            artifact.Symbols,
            symbol => symbol.Kind == EcosystemFindIndexSymbolKind.Type);
        Assert.Equal(
            [
                IntegrationConceptCatalog.Configuration.Id.Value,
                IntegrationConceptCatalog.DependencyInjection.Id.Value,
            ],
            type.Concepts.Select(concept => concept.ConceptId));
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            type.Type?.ToMetadataFullName());
        Assert.False(type.TypeDiscoveryAttributes!.IsEditorBrowsableNever);
        Assert.False(type.TypeDiscoveryAttributes.IsObsolete);

        EcosystemFindIndexSymbol[] members =
        [
            .. artifact.Symbols.Where(
                symbol => symbol.Kind == EcosystemFindIndexSymbolKind.Member),
        ];
        Assert.Equal(2, members.Length);
        Assert.Equal(
            2,
            members.Select(symbol => symbol.Member!.Member)
                .Distinct()
                .Count());
        Assert.All(
            members,
            symbol =>
            {
                Assert.Equal(
                    IntegrationConceptCatalog.DependencyInjection.Id.Value,
                    Assert.Single(symbol.Concepts).ConceptId);
                Assert.False(string.IsNullOrWhiteSpace(
                    symbol.Member!.DisplaySignature));
                Assert.NotEqual(
                    symbol.Member.Member.CanonicalSignature,
                    symbol.Member.DisplaySignature);
                Assert.Equal("method", symbol.Member.DisplayKind);
                Assert.False(
                    symbol.Member.DeclaringTypeDiscoveryAttributes
                        .IsEditorBrowsableNever);
                Assert.False(
                    symbol.Member.DeclaringTypeDiscoveryAttributes
                        .IsObsolete);
                Assert.False(symbol.Member.IsEditorBrowsableNever);
            });
    }

    [Fact]
    public void Build_PreservesOrderedCoverageIncludingIndexedEmptySources()
    {
        EcosystemFindIndexArtifact artifact = EcosystemFindIndexBuilder.Build(
            Request(
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Identity",
                    assetPath: "lib/net10.0/Fixture.Identity.dll",
                    assemblyName: "Fixture.Identity",
                    MissingIdentitySignals()),
                Source(
                    prefixOrdinal: 1,
                    packageOrdinal: 0,
                    packageId: "Second.Empty",
                    assetPath: "lib/net10.0/Second.Empty.dll",
                    assemblyName: "Second.Empty",
                    []),
            ],
            gaps: CoverageGaps()));

        Assert.Empty(artifact.Symbols);
        Assert.Equal(2, artifact.Sources.Length);
        Assert.Equal(
            Enum.GetValues<EcosystemFindIndexGapKind>(),
            artifact.Gaps.Select(gap => gap.Kind)
                .Distinct()
                .Order());

        var available = Assert.IsType<EcosystemFindIndexLoadResult.Available>(
            EcosystemFindIndexJson.Load(EcosystemFindIndexJson.Serialize(artifact)));
        Assert.Empty(available.Artifact.Symbols);
        Assert.Equal(2, available.Artifact.Sources.Length);
        Assert.Equal(6, available.Artifact.Gaps.Length);
    }

    [Fact]
    public void Build_RetainsRejectedPackageCandidateOrder()
    {
        var request = new EcosystemFindIndexBuildRequest(
            EcosystemPackIds.Aspire,
            [IntegrationConceptCatalog.DependencyInjection],
            [new PackagePrefixDeclaration("Fixture.")],
            [],
            [
                new(
                    EcosystemFindIndexGapKind.PackageRejected,
                    PrefixOrdinal: 0,
                    Package: new PackageCoordinate("Fixture.B", "1.0.0"),
                    PackageOrdinal: 0,
                    Library: null,
                    LibraryOrdinal: null,
                    Detail: "B was enumerated first."),
                new(
                    EcosystemFindIndexGapKind.PackageRejected,
                    PrefixOrdinal: 0,
                    Package: new PackageCoordinate("Fixture.A", "1.0.0"),
                    PackageOrdinal: 1,
                    Library: null,
                    LibraryOrdinal: null,
                    Detail: "A was enumerated second."),
            ]);

        EcosystemFindIndexArtifact artifact =
            EcosystemFindIndexBuilder.Build(request);

        Assert.Equal(
            ["Fixture.B", "Fixture.A"],
            artifact.Gaps.Select(gap => gap.Package!.PackageId));
        var available = Assert.IsType<EcosystemFindIndexLoadResult.Available>(
            EcosystemFindIndexJson.Load(EcosystemFindIndexJson.Serialize(artifact)));
        Assert.Equal(
            [0, 1],
            available.Artifact.Gaps.Select(gap => gap.PackageOrdinal));
    }

    [Fact]
    public void Build_BindsSharedInputsToCanonicalEcosystem()
    {
        EcosystemFindIndexSourceInput source = Source(
            prefixOrdinal: 0,
            packageOrdinal: 0,
            packageId: "Fixture.Empty",
            assetPath: "lib/net10.0/Fixture.Empty.dll",
            assemblyName: "Fixture.Empty",
            []);

        EcosystemFindIndexArtifact aspire =
            EcosystemFindIndexBuilder.Build(Request([source], EcosystemPackIds.Aspire));
        EcosystemFindIndexArtifact ai =
            EcosystemFindIndexBuilder.Build(Request([source], EcosystemPackIds.AI));

        Assert.NotEqual(
            EcosystemFindIndexJson.Serialize(aspire),
            EcosystemFindIndexJson.Serialize(ai));
        Assert.Equal(
            EcosystemPackIds.Aspire,
            Assert.IsType<EcosystemFindIndexLoadResult.Available>(
                EcosystemFindIndexJson.Load(EcosystemFindIndexJson.Serialize(aspire)))
                .Artifact.Ecosystem);
        Assert.Equal(
            EcosystemPackIds.AI,
            Assert.IsType<EcosystemFindIndexLoadResult.Available>(
                EcosystemFindIndexJson.Load(EcosystemFindIndexJson.Serialize(ai)))
                .Artifact.Ecosystem);
    }

    [Fact]
    public void Load_RejectsDuplicateProperties()
    {
        byte[] valid = EcosystemFindIndexJson.Serialize(
            EcosystemFindIndexBuilder.Build(Request([])));
        string duplicate = Encoding.UTF8.GetString(valid).Replace(
            "{\"schemaVersion\":3",
            "{\"schemaVersion\":3,\"schemaVersion\":3",
            StringComparison.Ordinal);

        Assert.IsType<EcosystemFindIndexLoadResult.Invalid>(
            EcosystemFindIndexJson.Load(Encoding.UTF8.GetBytes(duplicate)));
    }

    [Fact]
    public void Build_RejectsDuplicateOrdinalsAndUnconfiguredConcepts()
    {
        EcosystemFindIndexSourceInput source = Source(
            prefixOrdinal: 0,
            packageOrdinal: 0,
            packageId: "Fixture.Core",
            assetPath: "lib/net10.0/Fixture.Core.dll",
            assemblyName: "Fixture.Core",
            ScanFixture());

        Assert.Throws<ArgumentException>(() =>
            EcosystemFindIndexBuilder.Build(Request([source, source])));

        var request = new EcosystemFindIndexBuildRequest(
            EcosystemPackIds.Aspire,
            [IntegrationConceptCatalog.DependencyInjection],
            [new PackagePrefixDeclaration("Fixture.")],
            [source],
            []);
        Assert.Throws<ArgumentException>(() =>
            EcosystemFindIndexBuilder.Build(request));
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void Load_RejectsMalformedOrInconsistentArtifact(
        Func<byte[], byte[]> corrupt)
    {
        byte[] valid = EcosystemFindIndexJson.Serialize(
            EcosystemFindIndexBuilder.Build(
                Request(
                [
                    Source(
                        prefixOrdinal: 0,
                        packageOrdinal: 0,
                        packageId: "Fixture.Core",
                        assetPath: "lib/net10.0/Fixture.Core.dll",
                        assemblyName: "Fixture.Core",
                        ScanFixture()),
                ])));

        Assert.IsType<EcosystemFindIndexLoadResult.Invalid>(
            EcosystemFindIndexJson.Load(corrupt(valid)));
    }

    public static TheoryData<Func<byte[], byte[]>> InvalidDocuments =>
        new()
        {
            bytes => Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(bytes).Replace(
                    "{\"schemaVersion\":3,",
                    "{\"schemaVersion\":3,\"schemaVersion\":3,",
                    StringComparison.Ordinal)),
            bytes => Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(bytes).Replace(
                    "{\"schemaVersion\":3,",
                    "{\"unexpected\":true,\"schemaVersion\":3,",
                    StringComparison.Ordinal)),
            bytes => Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(bytes).Replace(
                    "\"sourceIndex\":0",
                    "\"sourceIndex\":999",
                    StringComparison.Ordinal)),
            bytes => Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(bytes).Replace(
                    "\"concepts\":[{",
                    "\"concepts\":[null,{",
                    StringComparison.Ordinal)),
            bytes => Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(bytes).Replace(
                    "\"integrationCatalogRevision\":"
                        + IntegrationConceptCatalog.Revision.Number,
                    "\"integrationCatalogRevision\":999999",
                    StringComparison.Ordinal)),
            bytes => RemoveFirstProperty(
                bytes,
                "typeDiscoveryAttributes"),
            bytes => RemoveFirstProperty(
                bytes,
                "displayKind"),
            bytes => RemoveFirstProperty(
                bytes,
                "displaySignature"),
            bytes => RemoveFirstProperty(
                bytes,
                "declaringTypeDiscoveryAttributes"),
        };

    private static byte[] RemoveFirstProperty(
        byte[] document,
        string propertyName)
    {
        JsonNode root = JsonNode.Parse(document)!;
        Assert.True(RemoveFirstProperty(root, propertyName));
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static bool RemoveFirstProperty(
        JsonNode node,
        string propertyName)
    {
        if (node is JsonObject value)
        {
            if (value.Remove(propertyName))
                return true;
            foreach ((_, JsonNode? child) in value)
            {
                if (child is not null
                    && RemoveFirstProperty(child, propertyName))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child is not null
                    && RemoveFirstProperty(child, propertyName))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static EcosystemFindIndexBuildRequest Request(
        IReadOnlyList<EcosystemFindIndexSourceInput> sources,
        EcosystemPackId? ecosystem = null,
        IReadOnlyList<EcosystemFindIndexGapInput>? gaps = null) =>
        new(
            ecosystem ?? EcosystemPackIds.Aspire,
            [
                IntegrationConceptCatalog.Configuration,
                IntegrationConceptCatalog.DependencyInjection,
            ],
            [
                new PackagePrefixDeclaration("Fixture."),
                new PackagePrefixDeclaration("Second."),
            ],
            sources,
            gaps ?? []);

    private static IReadOnlyList<EcosystemFindIndexGapInput> CoverageGaps() =>
        [
            new(
                EcosystemFindIndexGapKind.PrefixEnumerationBounded,
                PrefixOrdinal: 0,
                Package: null,
                PackageOrdinal: null,
                Library: null,
                LibraryOrdinal: null,
                Detail: "Package enumeration reached its configured bound."),
            new(
                EcosystemFindIndexGapKind.PackageRejected,
                PrefixOrdinal: 0,
                Package: new PackageCoordinate("Fixture.Rejected", "1.0.0"),
                PackageOrdinal: 1,
                Library: null,
                LibraryOrdinal: null,
                Detail: "The package did not contain an admitted Library."),
            new(
                EcosystemFindIndexGapKind.LibraryFailed,
                PrefixOrdinal: 0,
                Package: new PackageCoordinate("Fixture.LibraryFailure", "1.0.0"),
                PackageOrdinal: 2,
                Library: new PackageLibraryIdentity(
                    "lib/net10.0/Fixture.LibraryFailure.dll",
                    "Fixture.LibraryFailure"),
                LibraryOrdinal: 0,
                Detail: "The selected Library could not be inspected."),
            new(
                EcosystemFindIndexGapKind.ScannerUnavailable,
                PrefixOrdinal: 1,
                Package: new PackageCoordinate("Second.Unavailable", "1.0.0"),
                PackageOrdinal: 1,
                Library: new PackageLibraryIdentity(
                    "lib/net10.0/Second.Unavailable.dll",
                    "Second.Unavailable"),
                LibraryOrdinal: 0,
                Detail: "The configured scanner was unavailable."),
        ];

    private static IReadOnlyList<EcosystemIntegrationSignalInfo>
        MissingIdentitySignals() =>
        [
            new(
                EcosystemIntegrationNames.DependencyInjection,
                "Dependency Injection",
                "Missing.Type"),
            new(
                EcosystemIntegrationNames.DependencyInjection,
                "Service Registration",
                "Missing.Member(...)",
                IntegrationSignalShape.Api),
        ];

    private static EcosystemFindIndexSourceInput Source(
        int prefixOrdinal,
        int packageOrdinal,
        string packageId,
        string assetPath,
        string assemblyName,
        IReadOnlyList<EcosystemIntegrationSignalInfo> signals) =>
        new(
            new EcosystemFindIndexSourceCoordinate(
                prefixOrdinal,
                packageOrdinal,
                libraryOrdinal: 0,
                new PackageCoordinate(packageId, "1.0.0"),
                "net10.0",
                assemblyName,
                new PackageLibraryIdentity(assetPath, assemblyName),
                new AssemblyReferenceIdentity(
                    assemblyName,
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null),
                Guid.Parse("01234567-89ab-cdef-0123-456789abcdef")),
            signals);

    private static IReadOnlyList<EcosystemIntegrationSignalInfo> ScanFixture(
        bool hideType = false,
        bool hideDeclaringType = false,
        bool hideMethod = false)
    {
        using MemoryStream stream = BuildFixtureAssembly(
            hideType,
            hideDeclaringType,
            hideMethod);
        using var session = AssemblyInspectionSession.OpenPrefetched(stream);
        var binding = EcosystemIntegrationScannerBinding.Create(Classify);
        return session.EcosystemIntegrations(binding);
    }

    private static ImmutableArray<EcosystemIntegrationClassification> Classify(
        EcosystemIntegrationObservationContext context) =>
        [
            .. context.StarterMethods.Select(method => method.Classify(
                IntegrationConceptCatalog.DependencyInjection,
                "Service Registration")),
            .. context.Types
                .Where(type => type.MetadataName
                    == "Microsoft.Extensions.DependencyInjection.IServiceCollection")
                .SelectMany(type => new[]
                {
                    type.Classify(
                        IntegrationConceptCatalog.DependencyInjection,
                        "Dependency Injection"),
                    type.Classify(
                        IntegrationConceptCatalog.Configuration,
                        "Configuration"),
                }),
        ];

    private static MemoryStream BuildFixtureAssembly(
        bool hideType = false,
        bool hideDeclaringType = false,
        bool hideMethod = false)
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName("EcosystemFindIndexFixture")
            {
                Version = new Version(1, 0, 0, 0),
            },
            typeof(object).Assembly);
        ModuleBuilder module =
            assemblyBuilder.DefineDynamicModule("EcosystemFindIndexFixture");
        TypeBuilder serviceCollectionBuilder = module.DefineType(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        if (hideType)
        {
            serviceCollectionBuilder.SetCustomAttribute(
                EditorBrowsableNever());
            serviceCollectionBuilder.SetCustomAttribute(
                Obsolete());
        }
        Type serviceCollection = serviceCollectionBuilder.CreateType();
        TypeBuilder extensions = module.DefineType(
            "Microsoft.Extensions.DependencyInjection.TestServiceCollectionExtensions",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        var extensionAttribute = new CustomAttributeBuilder(
            typeof(ExtensionAttribute).GetConstructor(Type.EmptyTypes)!,
            []);
        extensions.SetCustomAttribute(extensionAttribute);
        if (hideDeclaringType)
        {
            extensions.SetCustomAttribute(EditorBrowsableNever());
            extensions.SetCustomAttribute(Obsolete());
        }
        AddExtension([serviceCollection]);
        AddExtension([serviceCollection, typeof(string)]);
        extensions.CreateType();

        var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        stream.Position = 0;
        return stream;

        void AddExtension(Type[] parameters)
        {
            MethodBuilder method = extensions.DefineMethod(
                "AddIndexedThing",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                parameters);
            method.SetCustomAttribute(extensionAttribute);
            if (hideMethod)
                method.SetCustomAttribute(EditorBrowsableNever());
            method.GetILGenerator().Emit(OpCodes.Ret);
        }

        static CustomAttributeBuilder EditorBrowsableNever() =>
            new(
                typeof(EditorBrowsableAttribute).GetConstructor(
                    [typeof(EditorBrowsableState)])!,
                [EditorBrowsableState.Never]);

        static CustomAttributeBuilder Obsolete() =>
            new(
                typeof(ObsoleteAttribute).GetConstructor(Type.EmptyTypes)!,
                []);
    }
}
