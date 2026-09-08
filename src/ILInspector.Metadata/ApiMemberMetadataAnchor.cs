using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Reconstructs the metadata-issued identity for a selected API member.
/// </summary>
public static class ApiMemberMetadataAnchor
{
    public static bool TryResolve(
        string assemblyPath,
        ApiType type,
        ApiMember member,
        [NotNullWhen(true)] out MemberAnchor? anchor,
        out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);

        anchor = null;
        error = null;
        if (type.MetadataToken is not { } typeToken)
        {
            error = $"Type '{type.FullName}' has no TypeDef token.";
            return false;
        }
        EntityHandle typeEntity = MetadataTokens.EntityHandle(typeToken);
        if (typeEntity.Kind != HandleKind.TypeDefinition)
        {
            error = $"Type '{type.FullName}' has no TypeDef token.";
            return false;
        }
        TypeDefinitionHandle typeHandle =
            (TypeDefinitionHandle)typeEntity;

        using FileStream stream = File.OpenRead(assemblyPath);
        using var image = new PEReader(stream);
        if (!image.HasMetadata)
        {
            error = $"Member '{member.Name}' has no managed metadata.";
            return false;
        }

        MetadataReader reader = image.GetMetadataReader();
        int anchorWorkRemaining =
            MetadataSafetyPolicy.MaxClassificationScanWorkChars;
        if (member.DeclarationMetadataToken is { } declarationToken)
        {
            EntityHandle declaration =
                MetadataTokens.EntityHandle(declarationToken);
            anchor = declaration.Kind switch
            {
                HandleKind.PropertyDefinition =>
                    ApiMemberIdentity.CreatePropertyAnchor(
                        reader,
                        typeHandle,
                        reader.GetPropertyDefinition(
                            (PropertyDefinitionHandle)declaration),
                        ref anchorWorkRemaining),
                HandleKind.EventDefinition =>
                    ApiMemberIdentity.CreateEventAnchor(
                        reader,
                        typeHandle,
                        reader.GetEventDefinition(
                            (EventDefinitionHandle)declaration),
                        ref anchorWorkRemaining),
                HandleKind.FieldDefinition =>
                    ApiMemberIdentity.CreateFieldAnchor(
                        reader,
                        typeHandle,
                        reader.GetFieldDefinition(
                            (FieldDefinitionHandle)declaration),
                        ref anchorWorkRemaining),
                _ => null,
            };
        }
        else if (member.MetadataToken is { } methodToken)
        {
            EntityHandle methodEntity =
                MetadataTokens.EntityHandle(methodToken);
            if (methodEntity.Kind == HandleKind.MethodDefinition)
            {
                anchor = ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    typeHandle,
                    reader.GetMethodDefinition(
                        (MethodDefinitionHandle)methodEntity),
                    member.Kind == "extension-method");
            }
        }

        if (anchor is not null)
            return true;

        error =
            $"Member '{member.Name}' has no exact metadata declaration identity.";
        return false;
    }
}
