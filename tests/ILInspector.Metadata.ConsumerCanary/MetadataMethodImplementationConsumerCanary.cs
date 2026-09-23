using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.ConsumerCanary;

public static class MetadataMethodImplementationConsumerCanary
{
    public static MetadataTypeDeclarationResult PostType(
        string assemblyPath,
        MetadataTypeDefinitionAddress type)
    {
        using var assembly = AssemblyInspectionSession.Open(assemblyPath);
        using var operation =
            new MetadataOperationContext(MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.PostTypeDeclaration(type);
    }

    public static bool Consume(MetadataTypeDeclarationResult result) =>
        result switch
        {
            MetadataTypeDeclarationResult.Posted posted =>
                posted.Evidence.Type.Definition.Value != 0
                && posted.Evidence.DefinitionIdentity.Segments.Length > 0
                && posted.Evidence.OpenSelfIdentity is not null,
            MetadataTypeDeclarationResult.Rejected rejected =>
                rejected.Failure.Detail.Length > 0
                && rejected.Counters.MetadataRows >= 0,
            _ => false,
        };

    public static MetadataMethodDeclarationResult PostMethod(
        string assemblyPath,
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress method)
    {
        using var assembly = AssemblyInspectionSession.Open(assemblyPath);
        using var operation =
            new MetadataOperationContext(MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.PostMethodDeclaration(type, method);
    }

    public static bool Consume(MetadataMethodDeclarationResult result) =>
        result switch
        {
            MetadataMethodDeclarationResult.Posted posted =>
                posted.Evidence.Signature.ParameterTypes.Length
                    == posted.Evidence.Parameters.Length
                && posted.Evidence.Method.Handle.IsNil is false
                && posted.Evidence.Type.Definition.Value != 0
                && posted.Evidence.TypeParameters.All(p =>
                    p.Name.Length >= 0
                    && p.Constraints.All(c => c is not null)),
            MetadataMethodDeclarationResult.Rejected rejected =>
                rejected.Failure.Detail.Length > 0
                && rejected.Counters.MetadataRows >= 0,
            _ => false,
        };

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

    public static MetadataInterfaceImplementationResult RelateInterface(
        string assemblyPath,
        MetadataTypeDefinitionAddress type,
        MetadataTypeIdentity interfaceType)
    {
        using var assembly =
            AssemblyInspectionSession.Open(assemblyPath);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.Relate(type, interfaceType);
    }

    public static bool Consume(
        MetadataInterfaceImplementationResult result) =>
        result switch
        {
            MetadataInterfaceImplementationResult.Related related =>
                related.Relationships.All(
                    relationship =>
                        relationship.Relationship.Handle.IsNil is false
                        && relationship.Target.Handle.IsNil is false),
            MetadataInterfaceImplementationResult.Absent absent =>
                absent.Counters.InterfaceImplementationRows >= 0,
            MetadataInterfaceImplementationResult.Rejected rejected =>
                rejected.Failure.Detail.Length > 0
                && rejected.Counters.MetadataRows >= 0,
            _ => false,
        };
}
