using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Detached member spelling and exact navigation identity for one call-graph
/// target.
/// </summary>
public sealed record CallGraphMemberNavigationDocument(
    string? DeclaringAssembly,
    string TypeDisplayName,
    string? TypeMetadataId,
    string? TypeDefinitionId,
    string Name,
    ImmutableArray<string> ParameterTypes,
    string ReturnType,
    int GenericArity,
    int? MetadataToken,
    string SelectorKey,
    AssemblyReferenceIdentity? DefinitionAssemblyIdentity,
    string? DefinitionAssemblyName);

/// <summary>
/// Detaches Analysis-owned member identities for host-neutral navigation and
/// rendering.
/// </summary>
public static class CallGraphMemberNavigationProjection
{
    public static ImmutableArray<CallGraphMemberNavigationDocument> Project(
        InspectionGraphDocument document)
        => ProjectGraph(document);

    public static ImmutableArray<CallGraphMemberNavigationDocument> ProjectGraph(
        InspectionGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [.. document.Nodes.Select(Project)];
    }

    public static CallGraphMemberNavigationDocument Project(
        InspectionGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        MemberRef member = node.Subject
            is InspectionGraphSubject.MemberSubject
            {
                Identity:
                    InspectionGraphMemberIdentity.CallGraph callGraph,
            }
                ? callGraph.Member
                : throw new InvalidOperationException(
                    "The inspection graph contained a non-call-graph member node.");
        return Project(
            member,
            useOpenSignature: false,
            includeCurrentAssemblyIdentity: true);
    }

    public static CallGraphMemberNavigationDocument Project(
        MethodIdentity member)
    {
        ArgumentNullException.ThrowIfNull(member);
        TypeRef? definition =
            DeclaringTypeDefinition(member.DeclaringType);
        CallGraphMemberNavigationIdentity navigation =
            CallGraphMemberResolver.CreateNavigationIdentity(member);
        return new(
            member.DeclaringType.Assembly,
            member.DeclaringType.ToQualifiedDisplayString(),
            navigation.TypeMetadataId,
            navigation.TypeDefinitionId,
            member.Name,
            [
                .. member.ParameterTypes.Select(
                    static type => type.ToQualifiedDisplayString()),
            ],
            member.ReturnType.ToQualifiedDisplayString(),
            member.GenericArity,
            member.MetadataToken,
            navigation.SelectorKey,
            DefinitionAssemblyIdentity(
                definition,
                includeCurrentAssembly: true),
            definition?.Assembly);
    }

    public static CallGraphMemberNavigationDocument ProjectOpenSignature(
        MemberRef member) =>
        Project(
            member,
            useOpenSignature: true,
            includeCurrentAssemblyIdentity: false);

    internal static CallGraphMemberNavigationDocument ProjectClosedSignature(
        MemberRef member) =>
        Project(
            member,
            useOpenSignature: false,
            includeCurrentAssemblyIdentity: false);

    static CallGraphMemberNavigationDocument Project(
        MemberRef member,
        bool useOpenSignature,
        bool includeCurrentAssemblyIdentity)
    {
        ArgumentNullException.ThrowIfNull(member);
        TypeRef? definition =
            DeclaringTypeDefinition(member.DeclaringType);
        CallGraphMemberNavigationIdentity navigation =
            CallGraphMemberResolver.CreateNavigationIdentity(member);
        IEnumerable<TypeRef> parameters = useOpenSignature
            ? member.OpenSignatureParameters
            : member.ParameterTypes;
        TypeRef returnType = useOpenSignature
            ? member.OpenSignatureReturn
            : member.ReturnType;
        return new(
            member.DeclaringType.Assembly,
            member.DeclaringType.ToQualifiedDisplayString(),
            navigation.TypeMetadataId,
            navigation.TypeDefinitionId,
            member.Name,
            [
                .. parameters.Select(
                    static type => type.ToQualifiedDisplayString()),
            ],
            returnType.ToQualifiedDisplayString(),
            member.GenericArity,
            MetadataToken: null,
            navigation.SelectorKey,
            DefinitionAssemblyIdentity(
                definition,
                includeCurrentAssemblyIdentity),
            definition?.Assembly);
    }

    static TypeRef? DeclaringTypeDefinition(TypeRef type)
    {
        while (type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }
        return type.Kind == TypeRefKind.Definition ? type : null;
    }

    static AssemblyReferenceIdentity? DefinitionAssemblyIdentity(
        TypeRef? definition,
        bool includeCurrentAssembly) =>
        definition?.Resolution?.Origin switch
        {
            TypeReferenceOrigin.AssemblyReference reference =>
                reference.Assembly,
            TypeReferenceOrigin.CurrentAssembly current
                when includeCurrentAssembly =>
                current.Assembly,
            _ => null,
        };
}
