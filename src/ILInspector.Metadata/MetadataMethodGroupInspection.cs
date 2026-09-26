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

public enum MetadataMethodAccessibilityFilter
{
    Public,
    Protected,
    Internal,
    Private,
    All,
}

public enum MetadataMethodReceiverFilter
{
    All,
    This,
    Static,
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
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver,
        int maximumMembers,
        int maximumRetainedTextCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(methodSemantics);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        if (!Enum.IsDefined(accessibility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessibility),
                accessibility,
                "Unknown Method-group accessibility filter.");
        }
        if (!Enum.IsDefined(receiver))
        {
            throw new ArgumentOutOfRangeException(
                nameof(receiver),
                receiver,
                "Unknown Method-group receiver filter.");
        }
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
                    typeHandle,
                    type,
                    methodSemantics,
                    out HashSet<MethodDefinitionHandle> accessors,
                    out MetadataMethodGroupInspectionOutcome? failure))
            {
                return failure!;
            }
            int matchCount = 0;
            List<MethodDefinitionHandle>? rowHandles =
                materializeRows
                    ? []
                    : null;
            bool groupExists = false;
            bool? extensionContainerForFilter = null;
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
                    || !IsOrdinaryMethodName(methodName))
                {
                    continue;
                }
                groupExists = true;
                if (!MatchesAccessibility(
                        method.Attributes,
                        accessibility)
                    || !MatchesReceiver(
                        reader,
                        type,
                        method,
                        receiver,
                        ref extensionContainerForFilter))
                {
                    continue;
                }

                if (rowHandles is not null
                    && matchCount >= startOrdinal
                    && rowHandles.Count < maximumRows)
                {
                    rowHandles.Add(handle);
                }
                matchCount++;
                if (matchCount > maximumMembers)
                {
                    return new MetadataMethodGroupInspectionOutcome.Incomplete(
                        MetadataMethodGroupInspectionBound.Members,
                        maximumMembers,
                        matchCount);
                }
            }

            if (!groupExists)
            {
                return new MetadataMethodGroupInspectionOutcome
                    .MemberGroupNotFound();
            }
            if (startOrdinal > matchCount
                || (startOrdinal == matchCount && matchCount != 0))
            {
                return new MetadataMethodGroupInspectionOutcome.Read(
                    declaringType,
                    MetadataTokens.GetToken(typeHandle),
                    matchCount,
                    [],
                    NextOrdinal: null,
                    ContinuationOutOfRange: true);
            }

            int rowCount = rowHandles?.Count ?? 0;
            var rows =
                ImmutableArray.CreateBuilder<MetadataMethodGroupRow>(
                    rowCount);
            bool extensionContainer;
            try
            {
                extensionContainer =
                    receiver
                        is MetadataMethodReceiverFilter.Static
                            or MetadataMethodReceiverFilter.Extension
                    ? extensionContainerForFilter ?? false
                    : receiver is MetadataMethodReceiverFilter.All
                        && rowCount != 0
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
                    matchCount,
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
                    rowHandles![indexInGroup];
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                MetadataMethodReceiver rowReceiver;
                MetadataMethodDeclaration declaration;
                MemberAnchor anchor;
                try
                {
                    rowReceiver = receiver switch
                    {
                        MetadataMethodReceiverFilter.This =>
                            MetadataMethodReceiver.This,
                        MetadataMethodReceiverFilter.Static =>
                            MetadataMethodReceiver.Static,
                        MetadataMethodReceiverFilter.Extension =>
                            MetadataMethodReceiver.Extension,
                        MetadataMethodReceiverFilter.All =>
                            ClassifyReceiver(
                                reader,
                                method,
                                extensionContainer),
                        _ => throw new InvalidOperationException(
                            "Unknown Method-group receiver filter."),
                    };
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
                            rowReceiver
                                is MetadataMethodReceiver.Extension);
                }
                catch (Exception exception) when (
                    exception is BadImageFormatException
                        or ArgumentOutOfRangeException
                        or OverflowException)
                {
                    return new MetadataMethodGroupInspectionOutcome.Read(
                        declaringType,
                        MetadataTokens.GetToken(typeHandle),
                        matchCount,
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
                        matchCount,
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
                        rowReceiver));
            }

            int nextOrdinal = checked(startOrdinal + rowCount);
            return new MetadataMethodGroupInspectionOutcome.Read(
                declaringType,
                MetadataTokens.GetToken(typeHandle),
                matchCount,
                rows.MoveToImmutable(),
                materializeRows && nextOrdinal < matchCount
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
        TypeDefinitionHandle typeHandle,
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
                reader.GetMethodDefinition(row.Method)
                    .GetDeclaringType() == typeHandle;
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

    private static bool MatchesAccessibility(
        MethodAttributes attributes,
        MetadataMethodAccessibilityFilter filter)
    {
        MethodAttributes accessibility =
            attributes & MethodAttributes.MemberAccessMask;
        MetadataMethodAccessibilityFilter actual =
            accessibility switch
            {
                MethodAttributes.Public =>
                    MetadataMethodAccessibilityFilter.Public,
                MethodAttributes.Family
                    or MethodAttributes.FamANDAssem
                    or MethodAttributes.FamORAssem =>
                    MetadataMethodAccessibilityFilter.Protected,
                MethodAttributes.Assembly =>
                    MetadataMethodAccessibilityFilter.Internal,
                MethodAttributes.Private
                    or MethodAttributes.PrivateScope =>
                    MetadataMethodAccessibilityFilter.Private,
                _ => throw new BadImageFormatException(
                    "The MethodDef accessibility is invalid."),
            };
        return filter is MetadataMethodAccessibilityFilter.All
            || filter == actual;
    }

    private static bool MatchesReceiver(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method,
        MetadataMethodReceiverFilter filter,
        ref bool? extensionContainer)
    {
        if (filter is MetadataMethodReceiverFilter.All)
            return true;

        bool isStatic =
            (method.Attributes & MethodAttributes.Static) != 0;
        if (filter is MetadataMethodReceiverFilter.This)
            return !isStatic;
        if (!isStatic)
            return false;

        extensionContainer ??=
            AttributeReader.HasExtensionAttribute(
                reader,
                type.GetCustomAttributes());
        bool isExtension =
            extensionContainer.Value
            && AttributeReader.HasExtensionAttribute(
                reader,
                method.GetCustomAttributes());
        return filter switch
        {
            MetadataMethodReceiverFilter.Static => !isExtension,
            MetadataMethodReceiverFilter.Extension => isExtension,
            _ => throw new InvalidOperationException(
                "Unknown Method-group receiver filter."),
        };
    }

    private static MetadataMethodReceiver ClassifyReceiver(
        MetadataReader reader,
        MethodDefinition method,
        bool extensionContainer)
    {
        if ((method.Attributes & MethodAttributes.Static) == 0)
            return MetadataMethodReceiver.This;
        return extensionContainer
            && AttributeReader.HasExtensionAttribute(
                reader,
                method.GetCustomAttributes())
            ? MetadataMethodReceiver.Extension
            : MetadataMethodReceiver.Static;
    }

    private static bool IsOrdinaryMethodName(string name) =>
        name is not ".ctor" and not ".cctor"
        && !name.StartsWith("op_", StringComparison.Ordinal)
        && !name.Contains('.', StringComparison.Ordinal);
}
