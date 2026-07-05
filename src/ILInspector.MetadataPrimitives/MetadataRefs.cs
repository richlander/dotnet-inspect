namespace ILInspector.MetadataPrimitives;

public sealed record MetadataTypeRef(
    string AssemblyName,
    Guid ModuleVersionId,
    int MetadataToken)
{
    public string Token => $"0x{MetadataToken:X8}";
}

public sealed record MetadataMemberRef(
    string AssemblyName,
    Guid ModuleVersionId,
    int MetadataToken)
{
    public string Token => $"0x{MetadataToken:X8}";
}
