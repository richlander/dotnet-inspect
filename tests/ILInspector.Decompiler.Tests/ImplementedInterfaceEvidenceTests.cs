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
    public void ImportImplementedInterfaces_ReturnsSameAssemblyDefinitions()
    {
        using var source = MetadataSource.Open(typeof(InterfaceEvidenceSample).Assembly.Location);
        var reader = source.Reader;

        var interfaces = IrImporter.ImportImplementedInterfaces(reader, FindHandle(reader, nameof(InterfaceEvidenceSample)));

        // Only same-assembly TypeDefinition interfaces are evidence: the marker and
        // the nested interface, but not the generic instantiation (a TypeSpec).
        Assert.Equal(2, interfaces.Length);
        Assert.All(interfaces, type => Assert.Equal(TypeRefKind.Definition, type.Kind));
        Assert.All(interfaces, type => Assert.Equal(source.AssemblyName, type.Assembly));

        Assert.Single(interfaces, type => type.Name.EndsWith("ILocalMarker", StringComparison.Ordinal));
        // Nested interfaces keep their metadata nesting spelling ('+').
        Assert.Single(interfaces, type => type.Name.EndsWith("OuterHost+INested", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportImplementedInterfaces_ExcludesConstructedInterface()
    {
        using var source = MetadataSource.Open(typeof(InterfaceEvidenceSample).Assembly.Location);
        var reader = source.Reader;

        var interfaces = IrImporter.ImportImplementedInterfaces(reader, FindHandle(reader, nameof(InterfaceEvidenceSample)));

        // A generic interface implementation is a TypeSpec, never a local closure
        // root, so it is not part of the evidence set.
        Assert.DoesNotContain(interfaces, type => type.Kind == TypeRefKind.GenericInstance);
        Assert.DoesNotContain(interfaces, type => type.Name.Contains("ILocalGeneric", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportImplementedInterfaces_ExcludesCrossAssemblyInterface()
    {
        using var source = MetadataSource.Open(typeof(DisposableSample).Assembly.Location);
        var reader = source.Reader;

        // A cross-assembly interface is a TypeReference, not a local definition, so
        // it is excluded — target-interface seeding never treated it as a root.
        Assert.Empty(IrImporter.ImportImplementedInterfaces(reader, FindHandle(reader, nameof(DisposableSample))));
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
