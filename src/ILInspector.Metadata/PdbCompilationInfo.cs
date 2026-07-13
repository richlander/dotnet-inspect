using System.Reflection.Metadata;

namespace ILInspector.Metadata;

public sealed record CompilationOptionInfo(string Name, string Value);

public enum CompilationReferenceImageKind
{
    Module,
    Assembly,
}

public sealed record CompilationReferenceInfo(
    string Name,
    string Aliases,
    CompilationReferenceImageKind ImageKind,
    bool EmbedInteropTypes,
    uint Timestamp,
    uint ImageSize,
    Guid ModuleVersionId,
    byte AdditionalFlags = 0);

internal static class PdbCompilationInfoReader
{
    private static readonly Guid s_compilationOptions =
        new("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");

    private static readonly Guid s_compilationMetadataReferences =
        new("7E4D4708-096E-4C5C-AEDA-CB10BA6A740D");

    public static IReadOnlyList<CompilationOptionInfo> ReadOptions(MetadataReader pdb)
    {
        ArgumentNullException.ThrowIfNull(pdb);

        List<CompilationOptionInfo> options = [];
        foreach (var handle in pdb.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var info = pdb.GetCustomDebugInformation(handle);
            if (pdb.GetGuid(info.Kind) != s_compilationOptions)
                continue;

            var blob = pdb.GetBlobReader(info.Value);
            while (blob.RemainingBytes > 0)
            {
                string name = ReadNullTerminatedUtf8(ref blob, "compilation option name");
                if (name.Length == 0)
                    throw new BadImageFormatException("A portable-PDB compilation option has an empty name.");

                string value = ReadNullTerminatedUtf8(ref blob, $"value for compilation option '{name}'");
                options.Add(new CompilationOptionInfo(name, value));
            }
        }

        return options;
    }

    public static IReadOnlyList<CompilationReferenceInfo> ReadReferences(MetadataReader pdb)
    {
        ArgumentNullException.ThrowIfNull(pdb);

        List<CompilationReferenceInfo> references = [];
        foreach (var handle in pdb.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var info = pdb.GetCustomDebugInformation(handle);
            if (pdb.GetGuid(info.Kind) != s_compilationMetadataReferences)
                continue;

            var blob = pdb.GetBlobReader(info.Value);
            while (blob.RemainingBytes > 0)
            {
                string name = ReadNullTerminatedUtf8(ref blob, "compilation reference name");
                if (name.Length == 0)
                    throw new BadImageFormatException("A portable-PDB compilation reference has an empty name.");

                string aliases = ReadNullTerminatedUtf8(ref blob, $"aliases for compilation reference '{name}'");
                const int fixedFieldSize = 1 + sizeof(uint) + sizeof(uint) + 16;
                if (blob.RemainingBytes < fixedFieldSize)
                {
                    throw new BadImageFormatException(
                        $"Portable-PDB compilation reference '{name}' is truncated.");
                }

                byte flags = blob.ReadByte();
                references.Add(new CompilationReferenceInfo(
                    name,
                    aliases,
                    (flags & 0b01) != 0
                        ? CompilationReferenceImageKind.Assembly
                        : CompilationReferenceImageKind.Module,
                    EmbedInteropTypes: (flags & 0b10) != 0,
                    blob.ReadUInt32(),
                    blob.ReadUInt32(),
                    blob.ReadGuid(),
                    AdditionalFlags: (byte)(flags & ~0b11)));
            }
        }

        return references;
    }

    private static string ReadNullTerminatedUtf8(ref BlobReader blob, string field)
    {
        int length = blob.IndexOf(0);
        if (length < 0)
            throw new BadImageFormatException($"Portable-PDB {field} is not null-terminated.");

        string value = blob.ReadUTF8(length);
        blob.ReadByte();
        return value;
    }
}
