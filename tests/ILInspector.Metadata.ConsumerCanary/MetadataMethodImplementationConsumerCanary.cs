using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.ConsumerCanary;

public static class MetadataMethodImplementationConsumerCanary
{
    public static MetadataMethodImplementationResult Relate(
        string assemblyPath,
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress body)
    {
        using var assembly =
            AssemblyInspectionSession.Open(assemblyPath);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.Relate(type, body);
    }

    public static bool Consume(
        MetadataMethodImplementationResult result) =>
        result switch
        {
            MetadataMethodImplementationResult.Related related =>
                related.Relationships.All(
                    relationship =>
                        relationship.DeclarationName.Length >= 0
                        && relationship.DeclarationSignature
                            .ParameterTypes.Length >= 0),
            MetadataMethodImplementationResult.Absent absent =>
                absent.Counters.MethodImplementationRows >= 0,
            MetadataMethodImplementationResult.Rejected rejected =>
                rejected.Failure.Detail.Length > 0
                && rejected.Counters.MetadataRows >= 0,
            _ => false,
        };
}
