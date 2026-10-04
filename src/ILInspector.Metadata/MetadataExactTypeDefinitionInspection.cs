using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal abstract record MetadataExactTypeDefinitionResolution
{
    private protected MetadataExactTypeDefinitionResolution()
    {
    }

    internal sealed record Resolved(
        TypeDefinitionHandle Handle,
        Guid ModuleVersionId)
        : MetadataExactTypeDefinitionResolution
    {
        internal MetadataTypeDefinitionAddress Address(
            MetadataReader reader) =>
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                Handle);
    }

    internal sealed record TypeNotFound
        : MetadataExactTypeDefinitionResolution;

    internal sealed record TypeAmbiguous
        : MetadataExactTypeDefinitionResolution;

    internal sealed record Incomplete(long Limit, long Measured)
        : MetadataExactTypeDefinitionResolution;

    internal sealed record Failed
        : MetadataExactTypeDefinitionResolution;
}

internal static class MetadataExactTypeDefinitionInspection
{
    internal static MetadataExactTypeDefinitionResolution Resolve(
        MetadataReader reader,
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(bounds);
        cancellationToken.ThrowIfCancellationRequested();

        long metadataRows =
            MetadataOperationContext.CountMetadataRows(reader);
        if (metadataRows > bounds.MaxMetadataRows)
        {
            return new MetadataExactTypeDefinitionResolution.Incomplete(
                bounds.MaxMetadataRows,
                metadataRows);
        }

        TypeDefinitionHandle typeHandle = default;
        foreach (TypeDefinitionHandle candidate in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataTypeDefinitionNameMatchResult match =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    candidate,
                    type,
                    out _);
            if (match is MetadataTypeDefinitionNameMatchResult.Rejected)
                return new MetadataExactTypeDefinitionResolution.Failed();
            if (match is not MetadataTypeDefinitionNameMatchResult.Match)
                continue;
            if (!typeHandle.IsNil)
            {
                return new MetadataExactTypeDefinitionResolution
                    .TypeAmbiguous();
            }
            typeHandle = candidate;
        }
        if (typeHandle.IsNil)
        {
            return new MetadataExactTypeDefinitionResolution
                .TypeNotFound();
        }

        Guid moduleVersionId =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        return moduleVersionId == Guid.Empty
            ? new MetadataExactTypeDefinitionResolution.Failed()
            : new MetadataExactTypeDefinitionResolution.Resolved(
                typeHandle,
                moduleVersionId);
    }
}
