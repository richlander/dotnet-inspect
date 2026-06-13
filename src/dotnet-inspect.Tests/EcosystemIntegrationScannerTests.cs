using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class EcosystemIntegrationScannerTests
{
    [Fact]
    public void Scan_IgnoresNonPublicStarterExtensionMethods()
    {
        using var stream = BuildDependencyInjectionExtensionAssembly();
        using var peReader = new PEReader(stream);

        var signals = EcosystemIntegrationScanner.Scan(peReader);
        var apis = signals
            .Where(signal => signal.Shape == IntegrationSignalShape.Api)
            .Select(signal => signal.Name)
            .ToArray();

        Assert.Contains("Microsoft.Extensions.DependencyInjection.TestServiceCollectionExtensions.AddPublicThing(...)", apis);
        Assert.DoesNotContain("Microsoft.Extensions.DependencyInjection.TestServiceCollectionExtensions.AddInternalThing(...)", apis);
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
