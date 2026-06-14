using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Owner of the PE and metadata readers for one assembly — the explicit
/// lifetime the old pipeline hid (docs/decompiler-ir.md). Everything that
/// resolves tokens borrows from a live source; results that escape its
/// scope must be fully materialized (resolved <see cref="TypeRef"/>s,
/// strings, byte arrays) and never hold metadata handles. The importer's
/// outputs honor that rule by construction.
/// </summary>
public sealed class MetadataSource : IDisposable
{
    readonly FileStream _stream;

    MetadataSource(string path, FileStream stream, PEReader peReader, MetadataReader reader, string assemblyName)
    {
        Path = path;
        _stream = stream;
        Pe = peReader;
        Reader = reader;
        AssemblyName = assemblyName;
    }

    public string Path { get; }

    /// <summary>Simple assembly name (no version/culture).</summary>
    public string AssemblyName { get; }

    internal PEReader Pe { get; }

    internal MetadataReader Reader { get; }

    /// <summary>Opens an assembly. Throws <see cref="BadImageFormatException"/> for files without managed metadata.</summary>
    public static MetadataSource Open(string path)
    {
        var stream = File.OpenRead(path);
        PEReader? peReader = null;
        try
        {
            peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                throw new BadImageFormatException($"No managed metadata: {path}");
            var reader = peReader.GetMetadataReader();
            string assemblyName = reader.IsAssembly
                ? reader.GetString(reader.GetAssemblyDefinition().Name)
                : System.IO.Path.GetFileNameWithoutExtension(path);
            return new MetadataSource(path, stream, peReader, reader, assemblyName);
        }
        catch
        {
            peReader?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    Dictionary<TypeRef, TypeShape>? _shapes;

    /// <summary>
    /// The C# shape of a type defined in THIS assembly — enum, struct, or
    /// reference — read from its base type. Cross-assembly types and non
    /// -definitions return <see cref="TypeShape.Unknown"/>: resolving them
    /// would need an assembly loader the SRM-only pipeline deliberately does
    /// not carry. The same-assembly map covers the whole single-assembly
    /// sweep (every CoreLib type resolves against CoreLib). Built once, lazily.
    /// </summary>
    internal TypeShape ResolveShape(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
            return TypeShape.Unknown;
        _shapes ??= BuildShapeMap();
        return _shapes.GetValueOrDefault(type, TypeShape.Unknown);
    }

    Dictionary<TypeRef, TypeShape> BuildShapeMap()
    {
        var map = new Dictionary<TypeRef, TypeShape>();
        foreach (var handle in Reader.TypeDefinitions)
        {
            // The decoder produces the same nested-aware TypeRef the IR
            // carries, so the map keys match by semantic equality.
            var key = TypeRefDecoder.Instance.GetTypeFromDefinition(Reader, handle, 0);
            map[key] = ClassifyShape(Reader.GetTypeDefinition(handle));
        }
        return map;
    }

    TypeShape ClassifyShape(TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & System.Reflection.TypeAttributes.Interface) != 0)
            return TypeShape.Reference;
        var baseHandle = typeDef.BaseType;
        if (baseHandle.IsNil)
            return TypeShape.Reference;   // System.Object itself
        var (ns, name) = BaseName(baseHandle);
        if (ns == "System" && name == "Enum")
            return TypeShape.Enum;
        if (ns == "System" && name == "ValueType")
            return TypeShape.ValueType;   // a non-enum struct
        return TypeShape.Reference;       // any class base, or a generic (TypeSpec) base
    }

    (string Namespace, string Name) BaseName(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => NameOf(Reader.GetTypeReference((TypeReferenceHandle)handle)),
        HandleKind.TypeDefinition => NameOf(Reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
        _ => ("", ""),   // a TypeSpec base is never System.Enum/ValueType
    };

    (string, string) NameOf(TypeReference reference)
        => (Reader.GetString(reference.Namespace), Reader.GetString(reference.Name));

    (string, string) NameOf(TypeDefinition definition)
        => (Reader.GetString(definition.Namespace), Reader.GetString(definition.Name));

    public void Dispose()
    {
        Pe.Dispose();
        _stream.Dispose();
    }
}
