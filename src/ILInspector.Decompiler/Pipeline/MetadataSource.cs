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

    public void Dispose()
    {
        Pe.Dispose();
        _stream.Dispose();
    }
}
