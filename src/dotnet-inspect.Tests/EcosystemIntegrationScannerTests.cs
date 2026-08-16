using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
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

    private static MemoryStream BuildDependencyInjectionExtensionAssembly()
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
}
