using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class EcosystemIntegrationScannerTests
{
    [Fact]
    public void Scan_ProjectsExactOrderedPublicCurrencyAndPresence()
    {
        using var stream = BuildDependencyInjectionExtensionAssembly();
        using var peReader = new PEReader(stream);

        var signals = EcosystemIntegrationScanner.Scan(peReader);
        EcosystemIntegrationSignalInfo[] expected =
        [
            new(
                EcosystemIntegrationNames.DependencyInjection,
                "Service Registration",
                "Microsoft.Extensions.DependencyInjection.TestServiceCollectionExtensions.AddPublicThing(...)",
                IntegrationSignalShape.Api),
            new(
                EcosystemIntegrationNames.DependencyInjection,
                "Dependency Injection",
                "Microsoft.Extensions.DependencyInjection.IServiceCollection"),
        ];

        Assert.Equal(expected, signals);
        Assert.Same(
            IntegrationConceptCatalog.DependencyInjection,
            signals[0].GetConcept());
        Assert.Same(
            IntegrationConceptCatalog.EcosystemObserved,
            signals[0].GetProducerPolicy());
        Assert.Same(signals[0].GetConcept(), signals[1].GetConcept());
        EcosystemIntegrationApiEvidence api = Assert.IsType<
            EcosystemIntegrationApiEvidence>(
                signals[0].GetApiEvidence());
        Assert.Equal("AddPublicThing", api.Member.MemberName);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.TestServiceCollectionExtensions",
            api.DeclaringType.ToMetadataFullName());
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            api.ReceiverType?.Type.ToMetadataFullName());
        Assert.IsType<MetadataTypeReferenceScope.CurrentAssembly>(
            api.ReceiverType?.Scope);
        Assert.Equal(
            "System.Void",
            api.ReturnType?.Type.ToMetadataFullName());
        Assert.IsType<MetadataTypeReferenceScope.IntrinsicCoreLibrary>(
            api.ReturnType?.Scope);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            signals[1].GetTypeDefinition()?.ToMetadataFullName());

        var scannedPresence =
            EcosystemIntegrationScanner.ScanPresence(peReader.GetMetadataReader());
        var summarizedPresence =
            EcosystemIntegrationScanner.SummarizePresence(
                peReader,
                signals,
                hasOpenTelemetrySupport: false);

        Assert.Equal(scannedPresence, summarizedPresence);
        Assert.Equal(1, scannedPresence.IntegrationCount);
        Assert.True(scannedPresence.HasDependencyInjectionSupport);
    }

    [Fact]
    public void OpportunityScan_PreservesCompatibilitySetComparer()
    {
        using var stream = BuildCloudClientAssembly();
        using var peReader = new PEReader(stream);
        IReadOnlySet<string> existingIntegrations = new HashSet<string>(
            [EcosystemIntegrationNames.DependencyInjection.ToLowerInvariant()],
            StringComparer.OrdinalIgnoreCase);

        List<IntegrationOpportunityInfo> opportunities =
            IntegrationOpportunityScanner.Scan(peReader, existingIntegrations);

        Assert.DoesNotContain(
            opportunities,
            opportunity =>
                opportunity.Integration
                == EcosystemIntegrationNames.DependencyInjection);
        Assert.Contains(
            opportunities,
            opportunity =>
                opportunity.Integration == EcosystemIntegrationNames.Aspire);
    }

    [Fact]
    public void OpportunityScan_UsesExactDescriptorIdentityIndependentlyOfSetComparer()
    {
        using var stream = BuildCloudClientAssembly();
        using var peReader = new PEReader(stream);
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations =
            new HashSet<IntegrationConceptDescriptor>(
                AllConceptsEqualComparer.Instance)
            {
                IntegrationConceptCatalog.AI,
            };

        List<IntegrationOpportunityInfo> opportunities =
            IntegrationOpportunityScanner.Scan(peReader, existingIntegrations);

        Assert.Contains(
            opportunities,
            opportunity =>
                opportunity.GetConcept()
                == IntegrationConceptCatalog.DependencyInjection);
        Assert.Contains(
            opportunities,
            opportunity =>
                opportunity.GetConcept() == IntegrationConceptCatalog.Aspire);
    }

    [Fact]
    public void SummarizePresence_PreservesUnknownCompatibilityLabelCount()
    {
        using var stream = BuildDependencyInjectionExtensionAssembly();
        using var peReader = new PEReader(stream);
        EcosystemIntegrationSignalInfo[] signals =
        [
            new(
                "External Integration",
                "External",
                "External.Type"),
        ];

        EcosystemIntegrationPresence presence =
            EcosystemIntegrationScanner.SummarizePresence(
                peReader,
                signals,
                hasOpenTelemetrySupport: false);

        Assert.Equal(1, presence.IntegrationCount);
    }

    [Fact]
    public void CompatibilityRecords_DoNotInventProducerPolicyMembership()
    {
        var ecosystemSignal = new EcosystemIntegrationSignalInfo(
            EcosystemIntegrationNames.OpenTelemetry,
            "Telemetry",
            "OpenTelemetry.Api");
        var opportunity = new IntegrationOpportunityInfo(
            EcosystemIntegrationNames.Logging,
            "Logger",
            "Logging",
            "Add logging");

        Assert.Same(
            IntegrationConceptCatalog.OpenTelemetry,
            ecosystemSignal.GetConcept());
        Assert.Null(ecosystemSignal.GetProducerPolicy());
        Assert.Same(
            IntegrationConceptCatalog.Logging,
            opportunity.GetConcept());
        Assert.Null(opportunity.GetProducerPolicy());
    }

    [Fact]
    public void CompatibilityRecords_WithMutationKeepsConceptIdentitySynchronized()
    {
        var ecosystemSignal = new EcosystemIntegrationSignalInfo(
            EcosystemIntegrationNames.AI,
            "Chat",
            "ChatClient") with
        {
            Integration = EcosystemIntegrationNames.Logging,
        };
        var opportunity = new IntegrationOpportunityInfo(
            EcosystemIntegrationNames.AI,
            "ChatClient",
            "AI",
            "IChatClient") with
        {
            Integration = EcosystemIntegrationNames.Configuration,
        };

        Assert.Equal(
            EcosystemIntegrationNames.Logging,
            ecosystemSignal.Integration);
        Assert.Same(
            IntegrationConceptCatalog.Logging,
            ecosystemSignal.GetConcept());
        Assert.Same(
            IntegrationConceptCatalog.EcosystemObserved,
            ecosystemSignal.GetProducerPolicy());
        Assert.Equal(
            EcosystemIntegrationNames.Configuration,
            opportunity.Integration);
        Assert.Same(
            IntegrationConceptCatalog.Configuration,
            opportunity.GetConcept());
        Assert.Same(
            IntegrationConceptCatalog.Opportunity,
            opportunity.GetProducerPolicy());

        var externalSignal = ecosystemSignal with
        {
            Integration = "External Integration",
        };
        Assert.Null(externalSignal.GetConcept());
        Assert.Null(externalSignal.GetProducerPolicy());
        Assert.Null((opportunity with
        {
            Integration = EcosystemIntegrationNames.Logging,
        }).GetProducerPolicy());
    }

    [Fact]
    public void Scan_SkipsExtensionMethodWithoutReceiver()
    {
        using var stream = BuildDependencyInjectionExtensionAssembly(
            includeMalformedExtension: true);
        using var peReader = new PEReader(stream);

        var signals = EcosystemIntegrationScanner.Scan(peReader);

        Assert.Equal(2, signals.Count);
        Assert.DoesNotContain(
            signals,
            signal => signal.Name.Contains(
                "AddMalformedThing",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_PreservesClassifiedApiWhenStructuredAnchorIsOverBudget()
    {
        byte[] image = BuildAnchorOverBudgetExtensionAssembly();
        using (var extensionReader = new PEReader(
                   new MemoryStream(image, writable: false)))
        {
            Assert.Throws<BadImageFormatException>(
                () => ExtensionMethodScanner
                    .FindAllExtensions(extensionReader)
                    .ToList());
        }
        using var integrationReader = new PEReader(
            new MemoryStream(image, writable: false));

        EcosystemIntegrationSignalInfo signal = Assert.Single(
            EcosystemIntegrationScanner.Scan(integrationReader));

        Assert.Equal(IntegrationSignalShape.Api, signal.Shape);
        Assert.Equal(
            EcosystemIntegrationNames.DependencyInjection,
            signal.Integration);
        Assert.Null(signal.GetApiEvidence());
    }

    private static MemoryStream BuildDependencyInjectionExtensionAssembly(
        bool includeMalformedExtension = false)
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName("IntegrationVisibilityFixture"),
            typeof(object).Assembly);
        var module = assemblyBuilder.DefineDynamicModule("IntegrationVisibilityFixture");

        var serviceCollection = module.DefineType(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var serviceCollectionType = serviceCollection.CreateType();

        var extensions = module.DefineType(
            "Microsoft.Extensions.DependencyInjection.TestServiceCollectionExtensions",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var extensionAttribute = new CustomAttributeBuilder(
            typeof(ExtensionAttribute).GetConstructor(Type.EmptyTypes)!,
            []);
        extensions.SetCustomAttribute(extensionAttribute);

        DefineExtensionMethod(extensions, "AddPublicThing", MethodAttributes.Public);
        DefineExtensionMethod(extensions, "AddInternalThing", MethodAttributes.Assembly);
        if (includeMalformedExtension)
        {
            var malformed = extensions.DefineMethod(
                "AddMalformedThing",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);
            malformed.SetCustomAttribute(extensionAttribute);
            malformed.GetILGenerator().Emit(OpCodes.Ret);
        }

        extensions.CreateType();

        var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        stream.Position = 0;
        return stream;

        void DefineExtensionMethod(TypeBuilder type, string name, MethodAttributes accessibility)
        {
            var method = type.DefineMethod(
                name,
                accessibility | MethodAttributes.Static,
                typeof(void),
                [serviceCollectionType]);
            method.SetCustomAttribute(extensionAttribute);
            method.GetILGenerator().Emit(OpCodes.Ret);
        }
    }

    private static MemoryStream BuildCloudClientAssembly()
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName("IntegrationOpportunityFixture"),
            typeof(object).Assembly);
        var module =
            assemblyBuilder.DefineDynamicModule("IntegrationOpportunityFixture");
        module.DefineType(
                "Azure.Storage.SampleClient",
                TypeAttributes.Public | TypeAttributes.Class)
            .CreateType();

        var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class AllConceptsEqualComparer
        : IEqualityComparer<IntegrationConceptDescriptor>
    {
        public static AllConceptsEqualComparer Instance { get; } = new();

        public bool Equals(
            IntegrationConceptDescriptor? x,
            IntegrationConceptDescriptor? y) => true;

        public int GetHashCode(IntegrationConceptDescriptor obj) => 0;
    }

    private static byte[] BuildAnchorOverBudgetExtensionAssembly()
    {
        const int ParameterCount = 400;
        const int GenericArity = 2030;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("AnchorOverBudget.dll"),
            metadata.GetOrAddGuid(
                new Guid("70635ba7-9b9b-448f-829a-853f8b641594")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("AnchorOverBudget"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        AssemblyReferenceHandle dependencyInjection =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "Microsoft.Extensions.DependencyInjection.Abstractions"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);

        TypeReferenceHandle extensionAttribute =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("ExtensionAttribute"));
        metadata.AddTypeReference(
            dependencyInjection,
            metadata.GetOrAddString(
                "Microsoft.Extensions.DependencyInjection"),
            metadata.GetOrAddString("IServiceCollection"));
        metadata.AddTypeReference(
            dependency,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        metadata.AddTypeReference(
            dependency,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("G"));

        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(0);
        constructorSignature.WriteByte(0x01);
        MemberReferenceHandle extensionConstructor =
            metadata.AddMemberReference(
                extensionAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(constructorSignature));

        var typeSpecificationSignature = new BlobBuilder();
        typeSpecificationSignature.WriteByte(0x15);
        typeSpecificationSignature.WriteByte(0x12);
        typeSpecificationSignature.WriteCompressedInteger(
            (4 << 2) | 1);
        typeSpecificationSignature.WriteCompressedInteger(
            GenericArity);
        for (int index = 0; index < GenericArity; index++)
        {
            typeSpecificationSignature.WriteByte(0x12);
            typeSpecificationSignature.WriteCompressedInteger(
                (3 << 2) | 1);
        }
        TypeSpecificationHandle typeSpecification =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    typeSpecificationSignature));
        int typeSpecificationIndex =
            (MetadataTokens.GetRowNumber(typeSpecification) << 2) | 2;

        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x00);
        methodSignature.WriteCompressedInteger(ParameterCount);
        methodSignature.WriteByte(0x01);
        methodSignature.WriteByte(0x12);
        methodSignature.WriteCompressedInteger((2 << 2) | 1);
        for (int index = 1; index < ParameterCount; index++)
        {
            methodSignature.WriteByte(0x20);
            methodSignature.WriteCompressedInteger(
                typeSpecificationIndex);
            methodSignature.WriteByte(0x08);
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle extensionType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Sealed
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString(
                    "Microsoft.Extensions.DependencyInjection"),
                metadata.GetOrAddString("ProbeExtensions"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(1);
        attributeValue.WriteUInt16(0);
        BlobHandle attributeBlob =
            metadata.GetOrAddBlob(attributeValue);
        metadata.AddCustomAttribute(
            extensionType,
            extensionConstructor,
            attributeBlob);
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("AddProbeThing"),
                metadata.GetOrAddBlob(methodSignature),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddCustomAttribute(
            method,
            extensionConstructor,
            attributeBlob);

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll
                    | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var output = new BlobBuilder();
        pe.Serialize(output);
        return output.ToArray();
    }
}
