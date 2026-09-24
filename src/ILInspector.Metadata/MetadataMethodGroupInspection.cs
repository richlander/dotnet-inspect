using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum MetadataMethodReceiver
{
    Static,
    This,
    Extension,
}

public enum MetadataMethodGroupInspectionBound
{
    Members,
}

public sealed record MetadataMethodGroupRow(
    int MetadataToken,
    string DisplaySignature,
    string CanonicalSignature,
    string Fingerprint,
    string Accessibility,
    MetadataMethodReceiver Receiver);

public abstract record MetadataMethodGroupInspectionOutcome
{
    private protected MetadataMethodGroupInspectionOutcome()
    {
    }

    public sealed record Read(
        MetadataTypeDefinitionName DeclaringType,
        int TypeDefinitionToken,
        int Count,
        ImmutableArray<MetadataMethodGroupRow> Rows,
        int? NextOrdinal,
        bool ContinuationOutOfRange = false,
        long? IncompleteRetainedTextCharacters = null)
        : MetadataMethodGroupInspectionOutcome;

    public sealed record TypeNotFound
        : MetadataMethodGroupInspectionOutcome;

    public sealed record TypeAmbiguous
        : MetadataMethodGroupInspectionOutcome;

    public sealed record MemberGroupNotFound
        : MetadataMethodGroupInspectionOutcome;

    public sealed record Incomplete(
        MetadataMethodGroupInspectionBound Bound,
        long Measured)
        : MetadataMethodGroupInspectionOutcome;

    public sealed record Failed
        : MetadataMethodGroupInspectionOutcome;
}

internal static class MetadataMethodGroupInspection
{
    public static MetadataMethodGroupInspectionOutcome Read(
        MetadataReader reader,
        MetadataTypeDefinitionName declaringType,
        string methodName,
        int startOrdinal,
        int maximumRows,
        bool materializeRows,
        int maximumMembers,
        int maximumRetainedTextCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMembers);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);

        try
        {
            MetadataTypeDefinitionIndex index =
                MetadataTypeDefinitionIndex.Create(reader);
            if (!index.TryGetDefinitions(
                    declaringType,
                    out ImmutableArray<TypeDefinitionHandle> definitions,
                    out bool ambiguous))
            {
                return new MetadataMethodGroupInspectionOutcome.TypeNotFound();
            }
            if (ambiguous || definitions.Length != 1)
            {
                return new MetadataMethodGroupInspectionOutcome.TypeAmbiguous();
            }

            TypeDefinitionHandle typeHandle = definitions[0];
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            bool extensionContainer =
                AttributeReader.HasExtensionAttribute(
                    reader,
                    type.GetCustomAttributes());
            HashSet<MethodDefinitionHandle> accessors =
                GetAccessorMethods(reader, type);
            var matches = new List<MethodDefinitionHandle>();
            foreach (MethodDefinitionHandle handle in type.GetMethods())
            {
                if (accessors.Contains(handle))
                    continue;

                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                if (!string.Equals(
                        reader.GetString(method.Name),
                        methodName,
                        StringComparison.Ordinal)
                    || !IsPublic(method.Attributes))
                {
                    continue;
                }

                matches.Add(handle);
                if (matches.Count > maximumMembers)
                {
                    return new MetadataMethodGroupInspectionOutcome.Incomplete(
                        MetadataMethodGroupInspectionBound.Members,
                        matches.Count);
                }
            }

            if (matches.Count == 0)
            {
                return new MetadataMethodGroupInspectionOutcome
                    .MemberGroupNotFound();
            }
            if (startOrdinal > matches.Count
                || (startOrdinal == matches.Count && matches.Count != 0))
            {
                return new MetadataMethodGroupInspectionOutcome.Read(
                    declaringType,
                    MetadataTokens.GetToken(typeHandle),
                    matches.Count,
                    [],
                    NextOrdinal: null,
                    ContinuationOutOfRange: true);
            }

            int rowCount = materializeRows
                ? Math.Min(maximumRows, matches.Count - startOrdinal)
                : 0;
            var rows =
                ImmutableArray.CreateBuilder<MetadataMethodGroupRow>(
                    rowCount);
            long retainedTextCharacters = 0;
            for (int indexInGroup = 0;
                indexInGroup < rowCount;
                indexInGroup++)
            {
                MethodDefinitionHandle handle =
                    matches[startOrdinal + indexInGroup];
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                bool extension =
                    extensionContainer
                    && (method.Attributes & MethodAttributes.Static) != 0
                    && AttributeReader.HasExtensionAttribute(
                        reader,
                        method.GetCustomAttributes());
                MetadataMethodDeclaration declaration =
                    MetadataDeclarationQuery.GetMethod(
                        reader,
                        type,
                        method);
                MemberAnchor anchor =
                    ApiMemberIdentity.CreateMethodAnchor(
                        reader,
                        typeHandle,
                        method,
                        extension);
                string displaySignature = MetadataDeclarationQuery
                    .GetMethodSignatureText(declaration);
                retainedTextCharacters = checked(
                    retainedTextCharacters
                        + displaySignature.Length
                        + anchor.CanonicalSignature.Length
                        + anchor.Fingerprint.Length
                        + declaration.Accessibility.Length);
                if (retainedTextCharacters
                    > maximumRetainedTextCharacters)
                {
                    return new MetadataMethodGroupInspectionOutcome.Read(
                        declaringType,
                        MetadataTokens.GetToken(typeHandle),
                        matches.Count,
                        [],
                        NextOrdinal: null,
                        IncompleteRetainedTextCharacters:
                            retainedTextCharacters);
                }
                rows.Add(
                    new(
                        MetadataTokens.GetToken(handle),
                        displaySignature,
                        anchor.CanonicalSignature,
                        anchor.Fingerprint,
                        declaration.Accessibility,
                        extension
                            ? MetadataMethodReceiver.Extension
                            : declaration.IsStatic
                                ? MetadataMethodReceiver.Static
                                : MetadataMethodReceiver.This));
            }

            int nextOrdinal = checked(startOrdinal + rowCount);
            return new MetadataMethodGroupInspectionOutcome.Read(
                declaringType,
                MetadataTokens.GetToken(typeHandle),
                matches.Count,
                rows.MoveToImmutable(),
                materializeRows && nextOrdinal < matches.Count
                    ? nextOrdinal
                    : null);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new MetadataMethodGroupInspectionOutcome.Failed();
        }
    }

    private static HashSet<MethodDefinitionHandle> GetAccessorMethods(
        MetadataReader reader,
        TypeDefinition type)
    {
        var accessors = new HashSet<MethodDefinitionHandle>();
        foreach (PropertyDefinitionHandle handle in type.GetProperties())
        {
            PropertyAccessors property =
                reader.GetPropertyDefinition(handle).GetAccessors();
            Add(property.Getter);
            Add(property.Setter);
            foreach (MethodDefinitionHandle other in property.Others)
                Add(other);
        }
        foreach (EventDefinitionHandle handle in type.GetEvents())
        {
            EventAccessors @event =
                reader.GetEventDefinition(handle).GetAccessors();
            Add(@event.Adder);
            Add(@event.Remover);
            Add(@event.Raiser);
            foreach (MethodDefinitionHandle other in @event.Others)
                Add(other);
        }

        return accessors;

        void Add(MethodDefinitionHandle handle)
        {
            if (!handle.IsNil)
                accessors.Add(handle);
        }
    }

    private static bool IsPublic(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask)
            is MethodAttributes.Public;
}
