using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Indexes exact TypeDef identities from one metadata image in one bounded pass.
/// </summary>
public sealed class MetadataTypeDefinitionIndex
{
    readonly IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        IndexedDefinition> definitions;

    MetadataTypeDefinitionIndex(
        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            IndexedDefinition> definitions)
    {
        this.definitions = definitions;
    }

    public static MetadataTypeDefinitionIndex Create(
        MetadataReader reader) =>
        Create(reader, definitionVisited: null);

    internal static MetadataTypeDefinitionIndex Create(
        MetadataReader reader,
        Action<TypeDefinitionHandle>? definitionVisited)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var definitions = new Dictionary<
            MetadataTypeDefinitionName,
            IndexedDefinition>();
        long remainingWork =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            definitionVisited?.Invoke(handle);
            if (MetadataTypeDefinitionNameReader.Read(reader, handle)
                    is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                throw new BadImageFormatException(
                    "A TypeDef name could not be indexed.");
            }

            long charge = Encoding.UTF8.GetByteCount(
                read.Name.Namespace);
            foreach (string segment in read.Name.Segments)
                charge += Encoding.UTF8.GetByteCount(segment);
            remainingWork -= Math.Max(charge, 1);
            if (remainingWork < 0)
            {
                throw new BadImageFormatException(
                    "The TypeDef name index exceeded its structural-name work budget.");
            }

            if (definitions.TryGetValue(
                    read.Name,
                    out IndexedDefinition existing))
            {
                definitions[read.Name] =
                    existing with { Ambiguous = true };
            }
            else
            {
                definitions.Add(
                    read.Name,
                    new(handle, Ambiguous: false));
            }
        }
        return new(definitions);
    }

    public bool TryGetUniqueDefinition(
        MetadataTypeDefinitionName name,
        out TypeDefinitionHandle handle)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (definitions.TryGetValue(
                name,
                out IndexedDefinition definition)
            && !definition.Ambiguous)
        {
            handle = definition.Handle;
            return true;
        }

        handle = default;
        return false;
    }

    readonly record struct IndexedDefinition(
        TypeDefinitionHandle Handle,
        bool Ambiguous);
}
