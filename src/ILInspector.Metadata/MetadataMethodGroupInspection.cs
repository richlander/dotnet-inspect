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
    MethodSemanticsAssociations,
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
        long? IncompleteRetainedTextCharacters = null,
        bool RowsFailed = false)
        : MetadataMethodGroupInspectionOutcome;

    public sealed record TypeNotFound
        : MetadataMethodGroupInspectionOutcome;

    public sealed record TypeAmbiguous
        : MetadataMethodGroupInspectionOutcome;

    public sealed record MemberGroupNotFound
        : MetadataMethodGroupInspectionOutcome;

    public sealed record Incomplete(
        MetadataMethodGroupInspectionBound Bound,
        long Limit,
        long Measured)
        : MetadataMethodGroupInspectionOutcome;

    public sealed record Failed
        : MetadataMethodGroupInspectionOutcome;
}

internal static class MetadataMethodGroupInspection
{
    public static MetadataMethodGroupInspectionOutcome Read(
        MetadataReader reader,
        MetadataMethodSemanticsAssociationResult methodSemantics,
        MetadataTypeDefinitionName declaringType,
        string methodName,
        int startOrdinal,
        int maximumRows,
        bool materializeRows,
        int maximumMembers,
        int maximumRetainedTextCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(methodSemantics);
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
            if (!TryGetAccessorMethods(
                    reader,
                    type,
                    methodSemantics,
                    out HashSet<MethodDefinitionHandle> accessors,
                    out MetadataMethodGroupInspectionOutcome? failure))
            {
                return failure!;
            }
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
                    || !IsOrdinaryMethodName(methodName)
                    || !IsPublic(method.Attributes))
                {
                    continue;
                }

                matches.Add(handle);
                if (matches.Count > maximumMembers)
                {
                    return new MetadataMethodGroupInspectionOutcome.Incomplete(
                        MetadataMethodGroupInspectionBound.Members,
                        maximumMembers,
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
            bool extensionContainer;
            try
            {
                extensionContainer =
                    rowCount != 0
                    && AttributeReader.HasExtensionAttribute(
                        reader,
                        type.GetCustomAttributes());
            }
            catch (Exception exception) when (
                exception is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or OverflowException)
            {
                return new MetadataMethodGroupInspectionOutcome.Read(
                    declaringType,
                    MetadataTokens.GetToken(typeHandle),
                    matches.Count,
                    [],
                    NextOrdinal: null,
                    RowsFailed: true);
            }
            long retainedTextCharacters = 0;
            for (int indexInGroup = 0;
                indexInGroup < rowCount;
                indexInGroup++)
            {
                MethodDefinitionHandle handle =
                    matches[startOrdinal + indexInGroup];
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                bool extension;
                MetadataMethodDeclaration declaration;
                MemberAnchor anchor;
                try
                {
                    extension =
                        extensionContainer
                        && (method.Attributes & MethodAttributes.Static) != 0
                        && AttributeReader.HasExtensionAttribute(
                            reader,
                            method.GetCustomAttributes());
                    declaration =
                        MetadataDeclarationQuery.GetMethod(
                            reader,
                            type,
                            method);
                    anchor =
                        ApiMemberIdentity.CreateMethodAnchor(
                            reader,
                            typeHandle,
                            method,
                            extension);
                }
                catch (Exception exception) when (
                    exception is BadImageFormatException
                        or ArgumentOutOfRangeException
                        or OverflowException)
                {
                    return new MetadataMethodGroupInspectionOutcome.Read(
                        declaringType,
                        MetadataTokens.GetToken(typeHandle),
                        matches.Count,
                        [],
                        NextOrdinal: null,
                        RowsFailed: true);
                }
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

    private static bool TryGetAccessorMethods(
        MetadataReader reader,
        TypeDefinition type,
        MetadataMethodSemanticsAssociationResult methodSemantics,
        out HashSet<MethodDefinitionHandle> accessors,
        out MetadataMethodGroupInspectionOutcome? failure)
    {
        accessors = [];
        failure = methodSemantics switch
        {
            MetadataMethodSemanticsAssociationResult.Rejected
                {
                    Failure.Reason:
                        MetadataMethodSemanticsFailureReason.BudgetExceeded,
                    Failure.BudgetLimit: long limit,
                    Failure.RowsVisited: int rowsVisited,
                } =>
                new MetadataMethodGroupInspectionOutcome.Incomplete(
                    MetadataMethodGroupInspectionBound
                        .MethodSemanticsAssociations,
                    limit,
                    rowsVisited),
            MetadataMethodSemanticsAssociationResult.Completed => null,
            _ => new MetadataMethodGroupInspectionOutcome.Failed(),
        };
        if (methodSemantics
            is not MetadataMethodSemanticsAssociationResult.Completed success)
            return false;
        if (!success.AssociationsAreNondecreasing)
        {
            failure =
                new MetadataMethodGroupInspectionOutcome.Failed();
            return false;
        }

        var propertyRows = new HashSet<int>();
        foreach (PropertyDefinitionHandle handle in type.GetProperties())
            propertyRows.Add(MetadataTokens.GetRowNumber(handle));
        var eventRows = new HashSet<int>();
        foreach (EventDefinitionHandle handle in type.GetEvents())
            eventRows.Add(MetadataTokens.GetRowNumber(handle));
        var methods = type.GetMethods().ToHashSet();
        var standardRoles = new HashSet<
            (
                MetadataMethodSemanticsAssociationKind Kind,
                int Row,
                ushort Role)>();
        foreach (MetadataMethodSemanticsAssociation row
            in success.Associations)
        {
            bool associationBelongsToType =
                row.AssociationKind switch
                {
                    MetadataMethodSemanticsAssociationKind.Property =>
                        propertyRows.Contains(
                            row.AssociationRowNumber),
                    MetadataMethodSemanticsAssociationKind.Event =>
                        eventRows.Contains(
                            row.AssociationRowNumber),
                    _ => false,
                };
            bool methodBelongsToType =
                methods.Contains(row.Method);
            if (!associationBelongsToType
                && !methodBelongsToType)
                continue;
            if (!associationBelongsToType
                || !methodBelongsToType
                || !TryValidateRole(row, standardRoles))
            {
                failure =
                    new MetadataMethodGroupInspectionOutcome.Failed();
                return false;
            }

            accessors.Add(row.Method);
        }

        return true;
    }

    private static bool TryValidateRole(
        MetadataMethodSemanticsAssociation row,
        HashSet<(
            MetadataMethodSemanticsAssociationKind Kind,
            int Row,
            ushort Role)> standardRoles)
    {
        const ushort Setter = 0x0001;
        const ushort Getter = 0x0002;
        const ushort Other = 0x0004;
        const ushort Adder = 0x0008;
        const ushort Remover = 0x0010;
        const ushort Raiser = 0x0020;

        ushort role = row.RawSemantics;
        if (role == Other)
            return true;
        bool recognized =
            row.AssociationKind switch
            {
                MetadataMethodSemanticsAssociationKind.Property =>
                    role is Setter or Getter,
                MetadataMethodSemanticsAssociationKind.Event =>
                    role is Adder or Remover or Raiser,
                _ => false,
            };
        return recognized
            && standardRoles.Add(
                (
                    row.AssociationKind,
                    row.AssociationRowNumber,
                    role));
    }

    private static bool IsPublic(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask)
            is MethodAttributes.Public;

    private static bool IsOrdinaryMethodName(string name) =>
        name is not ".ctor" and not ".cctor"
        && !name.StartsWith("op_", StringComparison.Ordinal)
        && !name.Contains('.', StringComparison.Ordinal);
}
