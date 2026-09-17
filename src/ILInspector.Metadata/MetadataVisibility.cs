using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

internal readonly record struct MetadataVisibilityClassification(
    bool[] ExternallyVisibleTypes,
    int VisitedTypeDefinitions,
    bool IsComplete)
{
    internal bool IsExternallyVisible(
        TypeDefinitionHandle handle) =>
        ExternallyVisibleTypes[
            MetadataTokens.GetRowNumber(handle)];
}

internal static class MetadataVisibility
{
    internal static MetadataVisibilityClassification Classify(
        MetadataReader reader,
        int maximumTypeDefinitions)
    {
        int typeCount =
            reader.GetTableRowCount(TableIndex.TypeDef);
        int visitedTypes =
            Math.Min(typeCount, maximumTypeDefinitions);
        var visibility =
            new TypeAttributes[visitedTypes + 1];
        var declaringTypes = new int[visitedTypes + 1];

        for (int row = 1; row <= visitedTypes; row++)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(
                    MetadataTokens.TypeDefinitionHandle(row));
            TypeAttributes access =
                definition.Attributes
                & TypeAttributes.VisibilityMask;
            TypeDefinitionHandle declaringType =
                definition.GetDeclaringType();
            bool nested = access is not
                (TypeAttributes.NotPublic
                    or TypeAttributes.Public);
            if (nested == declaringType.IsNil)
            {
                throw new BadImageFormatException(
                    nested
                        ? "A nested type has no declaring type."
                        : "A top-level type has a declaring type.");
            }

            int declaringRow =
                MetadataTokens.GetRowNumber(declaringType);
            if (declaringRow > typeCount)
            {
                throw new BadImageFormatException(
                    "A nested type has an invalid declaring type.");
            }

            visibility[row] = access;
            declaringTypes[row] = declaringRow;
        }

        if (visitedTypes != typeCount)
        {
            return new(
                Array.Empty<bool>(),
                visitedTypes,
                IsComplete: false);
        }

        var externallyVisible = new bool[typeCount + 1];
        var states = new byte[typeCount + 1];
        var path = new int[typeCount];
        for (int start = 1; start <= typeCount; start++)
        {
            if (states[start] == 2)
            {
                continue;
            }

            int depth = 0;
            int current = start;
            while (current != 0 && states[current] == 0)
            {
                states[current] = 1;
                path[depth++] = current;
                current = declaringTypes[current];
            }

            if (current != 0 && states[current] == 1)
            {
                throw new BadImageFormatException(
                    "The nested type graph contains a cycle.");
            }

            while (depth != 0)
            {
                int row = path[--depth];
                int declaringRow = declaringTypes[row];
                externallyVisible[row] =
                    visibility[row] switch
                    {
                        TypeAttributes.Public => true,
                        TypeAttributes.NestedPublic =>
                            externallyVisible[declaringRow],
                        _ => false,
                    };
                states[row] = 2;
            }
        }

        return new(
            externallyVisible,
            visitedTypes,
            IsComplete: true);
    }

    internal static MetadataVisibilityClassification ClassifyAll(
        MetadataReader reader) =>
        Classify(reader, int.MaxValue);
}
