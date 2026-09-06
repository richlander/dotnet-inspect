using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>Issues a module-scoped address for a participant's physical MethodDef.</summary>
public static class AssemblyContextMethodAddressQuery
{
    public static InspectionQuery<AssemblyContextEntry<MetadataMethodAddress>> Definition { get; } =
        new("Assembly context method address", InspectionCost.Unbounded);

    public static AssemblyContextEntry<MetadataMethodAddress> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        int methodToken) =>
        AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (_, snapshot) =>
            {
                using var image = new PEReader(snapshot.Content);
                MetadataReader reader = image.GetMetadataReader();
                int row = methodToken & 0x00ffffff;
                if ((methodToken & unchecked((int)0xff000000)) != 0x06000000
                    || row == 0 || row > reader.MethodDefinitions.Count)
                {
                    throw new ArgumentException(
                        $"Token 0x{methodToken:X8} is not a MethodDef in this participant.",
                        nameof(methodToken));
                }
                return MetadataMethodAddress.Create(reader, MetadataTokens.MethodDefinitionHandle(row));
            });
}
