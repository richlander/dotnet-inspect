namespace ILInspector.Metadata;

public readonly record struct ProjectedMethodAddress(
    Guid ModuleVersionId,
    int MetadataToken);
