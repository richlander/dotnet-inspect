using System;
using System.Linq;
using System.Reflection.Metadata;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ImplementedInterfaceEvidenceTests
{
    interface ILocalMarker;

    interface ILocalGeneric<T>;

    class OuterHost
    {
        public interface INested;
    }

    class InterfaceEvidenceSample : ILocalMarker, ILocalGeneric<int>, OuterHost.INested;

    class NoInterfacesSample;

    class DisposableSample : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Fact]
    public void ImportImplementedInterfaces_DecodesDefinitionGenericAndNestedInterfaces()
    {
        using var source = MetadataSource.Open(typeof(InterfaceEvidenceSample).Assembly.Location);
        var reader = source.Reader;

        var interfaces = IrImporter.ImportImplementedInterfaces(reader, FindHandle(reader, nameof(InterfaceEvidenceSample)));

        Assert.Equal(3, interfaces.Length);

        // Same-assembly non-generic interface: a resolvable Definition ref.
        var marker = Assert.Single(interfaces, type => type.Kind == TypeRefKind.Definition && type.Name.EndsWith("ILocalMarker", StringComparison.Ordinal));
        Assert.Equal(source.AssemblyName, marker.Assembly);

        // Generic interface implementation is a TypeSpec, decoded to a generic instance.
        var generic = Assert.Single(interfaces, type => type.Kind == TypeRefKind.GenericInstance);
        Assert.Contains("ILocalGeneric", generic.ElementType!.Name, StringComparison.Ordinal);
        Assert.Equal("Int32", Assert.Single(generic.TypeArguments).Name);

        // Nested interfaces keep their metadata nesting spelling ('+').
        Assert.Single(interfaces, type => type.Kind == TypeRefKind.Definition && type.Name.EndsWith("OuterHost+INested", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportImplementedInterfaces_CrossAssemblyInterfaceKeepsForeignAssembly()
    {
        using var source = MetadataSource.Open(typeof(DisposableSample).Assembly.Location);
        var reader = source.Reader;

        var iface = Assert.Single(IrImporter.ImportImplementedInterfaces(reader, FindHandle(reader, nameof(DisposableSample))));

        // A cross-assembly interface is a TypeRef decoded to a Definition in the
        // foreign assembly — so target-interface seeding correctly declines it as
        // a local closure root.
        Assert.Equal(TypeRefKind.Definition, iface.Kind);
        Assert.EndsWith("IDisposable", iface.Name, StringComparison.Ordinal);
        Assert.NotEqual(source.AssemblyName, iface.Assembly);
    }

    [Fact]
    public void ImportImplementedInterfaces_NoInterfacesReturnsEmpty()
    {
        using var source = MetadataSource.Open(typeof(NoInterfacesSample).Assembly.Location);
        var reader = source.Reader;

        Assert.Empty(IrImporter.ImportImplementedInterfaces(reader, FindHandle(reader, nameof(NoInterfacesSample))));
    }

    static TypeDefinitionHandle FindHandle(MetadataReader reader, string simpleName)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            if (reader.GetString(reader.GetTypeDefinition(handle).Name) == simpleName)
                return handle;
        }

        throw new InvalidOperationException($"type {simpleName} not found in metadata");
    }
}
