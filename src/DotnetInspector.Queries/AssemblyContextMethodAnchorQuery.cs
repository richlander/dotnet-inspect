using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// Issues the Metadata-owned member anchor for one exact physical MethodDef in
/// a retained assembly-context participant.
/// </summary>
public static class AssemblyContextMethodAnchorQuery
{
    public static InspectionQuery<AssemblyContextEntry<MemberAnchor>>
        Definition { get; } =
            new(
                "Assembly context method anchor",
                InspectionCost.Unbounded);

    public static AssemblyContextEntry<MemberAnchor> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        MetadataTypeDefinitionName declaringType,
        int methodToken,
        bool isExtensionMethod) =>
        AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (_, snapshot) =>
            {
                ArgumentNullException.ThrowIfNull(declaringType);
                using var image = new PEReader(snapshot.Content);
                MetadataReader reader = image.GetMetadataReader();
                StructuralCloneTypeResolution type =
                    StructuralCloneMetadataResolution.ResolveType(
                        reader,
                        declaringType);
                if (type.Status
                    != StructuralCloneTypeResolutionStatus.Resolved)
                {
                    throw new ArgumentException(
                        $"Type '{declaringType.ToEscapedFullName()}' does not "
                            + "resolve uniquely in this participant.",
                        nameof(declaringType));
                }

                int row = methodToken & 0x00ffffff;
                if ((methodToken & unchecked((int)0xff000000))
                        != 0x06000000
                    || row == 0
                    || row > reader.MethodDefinitions.Count)
                {
                    throw new ArgumentException(
                        $"Token 0x{methodToken:X8} is not a MethodDef in this "
                            + "participant.",
                        nameof(methodToken));
                }

                MethodDefinitionHandle method =
                    MetadataTokens.MethodDefinitionHandle(row);
                if (!reader.GetTypeDefinition(type.Handle)
                        .GetMethods()
                        .Contains(method))
                {
                    throw new ArgumentException(
                        $"MethodDef 0x{methodToken:X8} is not declared by "
                            + $"'{declaringType.ToEscapedFullName()}'.",
                        nameof(methodToken));
                }

                return ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    type.Handle,
                    reader.GetMethodDefinition(method),
                    isExtensionMethod);
            });
}
