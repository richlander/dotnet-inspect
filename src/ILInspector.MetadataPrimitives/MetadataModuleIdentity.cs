using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.MetadataPrimitives;

internal static class MetadataModuleIdentity
{
    internal static Guid ReadVersionId(MetadataReader reader)
    {
        GuidHandle handle = reader.GetModuleDefinition().Mvid;
        int index = MetadataTokens.GetHeapOffset(handle);
        int heapSize = reader.GetHeapSize(HeapIndex.Guid);
        if (handle.IsNil
            || index <= 0
            || (long)index * 16 > heapSize)
        {
            throw new BadImageFormatException(
                "The module MVID does not reference a complete GUID heap entry.");
        }

        return reader.GetGuid(handle);
    }
}
