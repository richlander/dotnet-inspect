namespace ILInspector.Metadata;

/// <summary>
/// Materializes safe inert-text renderings for declaration consumers that do
/// not depend on InertText. These strings are not semantic identities.
/// </summary>
public static class MetadataDeclarationText
{
    public static string RenderDeclarationName(
        MetadataMethodImplementationCertificate relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        return relationship.DeclarationName.ToString();
    }

    public static string? RenderParameterName(
        MetadataParameterDeclarationEvidence parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return parameter.Name?.ToString();
    }

    public static string RenderPrimitiveName(
        MetadataTypeIdentity.Primitive primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        return primitive.Name.ToString();
    }

    public static string RenderNamespace(
        MetadataNamedTypeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return identity.Namespace.ToString();
    }

    public static int GetSegmentCount(
        MetadataNamedTypeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return identity.Segments.Length;
    }

    public static string RenderSegment(
        MetadataNamedTypeIdentity identity,
        int index)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return identity.Segments[index].ToString();
    }
}
