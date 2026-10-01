using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using CSharpText;
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
    string DocumentationId,
    string Fingerprint,
    MemberAnchor Anchor,
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
    internal enum CandidateKind
    {
        OutsideGroup,
        Filtered,
        Selected,
    }

    internal abstract record PreparationResult
    {
        private PreparationResult()
        {
        }

        internal sealed record Prepared(Analysis Model)
            : PreparationResult;

        internal sealed record Rejected(
            MetadataMethodGroupInspectionOutcome Outcome)
            : PreparationResult;
    }

    internal sealed class Analysis
    {
        private readonly HashSet<MethodDefinitionHandle> _accessors;
        private readonly HashSet<MethodDefinitionHandle>
            _explicitImplementationBodies;
        private readonly Dictionary<
            MethodDefinitionHandle,
            ApiSurfaceExtractor.InterfaceImplementationAccess>
            _interfaceImplementations;

        internal Analysis(
            MetadataReader reader,
            MetadataTypeDefinitionName declaringType,
            TypeDefinitionHandle typeHandle,
            TypeDefinition type,
            string methodName,
            HashSet<MethodDefinitionHandle> accessors,
            HashSet<MethodDefinitionHandle> explicitImplementationBodies,
            Dictionary<
                MethodDefinitionHandle,
                ApiSurfaceExtractor.InterfaceImplementationAccess>
                interfaceImplementations)
        {
            Reader = reader;
            DeclaringType = declaringType;
            TypeHandle = typeHandle;
            Type = type;
            MethodName = methodName;
            _accessors = accessors;
            _explicitImplementationBodies =
                explicitImplementationBodies;
            _interfaceImplementations = interfaceImplementations;
        }

        internal MetadataReader Reader { get; }
        internal MetadataTypeDefinitionName DeclaringType { get; }
        internal TypeDefinitionHandle TypeHandle { get; }
        internal TypeDefinition Type { get; }
        internal string MethodName { get; }

        internal MethodDefinitionHandleCollection Methods =>
            Type.GetMethods();

        internal MethodDefinition GetMethod(
            MethodDefinitionHandle handle) =>
            Reader.GetMethodDefinition(handle);

        internal CandidateKind Classify(
            MethodDefinitionHandle handle,
            MethodDefinition method,
            MetadataMethodAccessibilityFilter accessibility,
            MetadataMethodReceiverFilter receiver,
            bool includeHidden,
            ref bool? extensionContainer)
        {
            if (!Reader.StringComparer.Equals(
                    method.Name,
                    MethodName)
                || !IsOrdinaryMethodName(MethodName)
                || _accessors.Contains(handle))
            {
                return CandidateKind.OutsideGroup;
            }

            if (!MatchesAccessibility(
                    ApiSurfaceExtractor.MethodEffectiveAccess(
                        method.Attributes
                            & MethodAttributes.MemberAccessMask,
                        handle,
                        _interfaceImplementations),
                    accessibility)
                || !MatchesReceiver(
                    Reader,
                    Type,
                    method,
                    receiver,
                    ref extensionContainer)
                || (!includeHidden
                    && ApiSurfaceExtractor.IsHiddenMethod(
                        Reader,
                        method.GetCustomAttributes(),
                        _explicitImplementationBodies.Contains(
                            handle))))
            {
                return CandidateKind.Filtered;
            }

            return CandidateKind.Selected;
        }

        internal bool ExtensionContainer() =>
            AttributeReader.HasExtensionAttribute(
                Reader,
                Type.GetCustomAttributes());

        internal MetadataMethodGroupRow Project(
            MethodDefinitionHandle handle,
            MetadataMethodReceiver receiver)
        {
            MethodDefinition method =
                Reader.GetMethodDefinition(handle);
            MetadataMethodDeclaration declaration =
                MetadataDeclarationQuery.GetMethod(
                    Reader,
                    Type,
                    method);
            MemberAnchor anchor =
                ApiMemberIdentity.CreateMethodAnchor(
                    Reader,
                    TypeHandle,
                    method,
                    receiver
                        is MetadataMethodReceiver.Extension);
            ApiSignature documentationSignature =
                ApiSurfaceExtractor.GetMethodSignatureForIdentity(
                    Reader,
                    GenericContext.ForType(Reader, Type),
                    handle,
                    method,
                    typeNullableContext: 0).Model;
            XmlDocMemberIdentity documentationIdentity =
                XmlDocumentationNotation.CreateMemberIdentity(
                    "M",
                    DeclaringType.Namespace,
                    DeclaringType.Segments,
                    declaration.MetadataName,
                    documentationSignature.XmlDocumentationParameterTypes
                        ?? throw new BadImageFormatException(
                            "The method documentation identity could not be decoded."),
                    documentationSignature.TypeParameters.Count,
                    conversionReturnType: null,
                    documentationSignature.XmlDocumentationIsVararg);
            return new(
                MetadataTokens.GetToken(handle),
                MetadataDeclarationQuery.GetMethodSignatureText(
                    declaration),
                anchor.CanonicalSignature,
                documentationIdentity.Value,
                anchor.Fingerprint,
                anchor,
                declaration.Accessibility,
                receiver);
        }
    }

    internal sealed class Selection
    {
        private bool? _extensionContainer;

        internal Selection(
            Analysis model,
            MetadataMethodAccessibilityFilter accessibility,
            MetadataMethodReceiverFilter receiver,
            bool includeHidden)
        {
            Model = model;
            Accessibility = accessibility;
            Receiver = receiver;
            IncludeHidden = includeHidden;
        }

        internal Analysis Model { get; }
        internal MetadataMethodAccessibilityFilter Accessibility { get; }
        internal MetadataMethodReceiverFilter Receiver { get; }
        internal bool IncludeHidden { get; }

        internal CandidateKind Classify(
            MethodDefinitionHandle handle,
            MethodDefinition method) =>
            Model.Classify(
                handle,
                method,
                Accessibility,
                Receiver,
                IncludeHidden,
                ref _extensionContainer);

        internal MetadataMethodReceiver ReceiverFor(
            MethodDefinition method)
        {
            if (Receiver is MetadataMethodReceiverFilter.This)
                return MetadataMethodReceiver.This;
            if (Receiver is MetadataMethodReceiverFilter.Static)
                return MetadataMethodReceiver.Static;
            if (Receiver is MetadataMethodReceiverFilter.Extension)
                return MetadataMethodReceiver.Extension;
            _extensionContainer ??= Model.ExtensionContainer();
            return ClassifyReceiver(
                Model.Reader,
                method,
                _extensionContainer.Value);
        }
    }

    internal sealed class Fold
    {
        private readonly Analysis _model;
        private readonly Selection _selection;
        private readonly int _startOrdinal;
        private readonly int _maximumRows;
        private readonly int _maximumMembers;
        private readonly int _maximumRetainedTextCharacters;
        private readonly List<MethodDefinitionHandle>? _rowHandles;
        private bool _groupExists;
        private int _matchCount;
        private MetadataMethodGroupInspectionOutcome? _terminal;

        internal Fold(
            Analysis model,
            Selection selection,
            int startOrdinal,
            int maximumRows,
            bool materializeRows,
            int maximumMembers,
            int maximumRetainedTextCharacters)
        {
            _model = model;
            _selection = selection;
            _startOrdinal = startOrdinal;
            _maximumRows = maximumRows;
            _maximumMembers = maximumMembers;
            _maximumRetainedTextCharacters =
                maximumRetainedTextCharacters;
            _rowHandles = materializeRows
                ? []
                : null;
        }

        internal bool Accept(MethodDefinitionHandle handle)
        {
            if (_terminal is not null)
                return false;

            MethodDefinition method =
                _model.GetMethod(handle);
            CandidateKind kind =
                _selection.Classify(handle, method);
            if (kind is CandidateKind.OutsideGroup)
                return true;
            _groupExists = true;
            if (kind is CandidateKind.Filtered)
                return true;

            if (_rowHandles is not null
                && _matchCount >= _startOrdinal
                && _rowHandles.Count < _maximumRows)
            {
                _rowHandles.Add(handle);
            }
            _matchCount++;
            if (_matchCount > _maximumMembers)
            {
                _terminal =
                    new MetadataMethodGroupInspectionOutcome.Incomplete(
                        MetadataMethodGroupInspectionBound.Members,
                        _maximumMembers,
                        _matchCount);
                return false;
            }

            return true;
        }

        internal MetadataMethodGroupInspectionOutcome Complete()
        {
            if (_terminal is not null)
                return _terminal;
            if (!_groupExists)
            {
                return new MetadataMethodGroupInspectionOutcome
                    .MemberGroupNotFound();
            }
            if (_startOrdinal > _matchCount
                || (_startOrdinal == _matchCount
                    && _matchCount != 0))
            {
                return Read(
                    [],
                    nextOrdinal: null,
                    continuationOutOfRange: true);
            }

            int rowCount = _rowHandles?.Count ?? 0;
            var rows =
                ImmutableArray.CreateBuilder<MetadataMethodGroupRow>(
                    rowCount);
            long retainedTextCharacters = 0;
            for (int index = 0; index < rowCount; index++)
            {
                MethodDefinitionHandle handle =
                    _rowHandles![index];
                MethodDefinition method =
                    _model.GetMethod(handle);
                MetadataMethodGroupRow row;
                try
                {
                    row =
                        _model.Project(
                            handle,
                            _selection.ReceiverFor(method));
                }
                catch (Exception exception) when (
                    exception is BadImageFormatException
                        or ArgumentOutOfRangeException
                        or OverflowException)
                {
                    return Read(
                        [],
                        nextOrdinal: null,
                        rowsFailed: true);
                }
                retainedTextCharacters = checked(
                    retainedTextCharacters
                        + row.DisplaySignature.Length
                        + row.CanonicalSignature.Length
                        + row.Fingerprint.Length
                        + row.Anchor.StableSelector.Length
                        + row.Anchor.TypeFullName.Length
                        + row.Anchor.MemberName.Length
                        + row.Accessibility.Length);
                if (retainedTextCharacters
                    > _maximumRetainedTextCharacters)
                {
                    return Read(
                        [],
                        nextOrdinal: null,
                        incompleteRetainedTextCharacters:
                            retainedTextCharacters);
                }
                rows.Add(row);
            }

            int nextOrdinal =
                checked(_startOrdinal + rowCount);
            return Read(
                rows.MoveToImmutable(),
                _rowHandles is not null
                    && nextOrdinal < _matchCount
                        ? nextOrdinal
                        : null);
        }

        private MetadataMethodGroupInspectionOutcome.Read Read(
            ImmutableArray<MetadataMethodGroupRow> rows,
            int? nextOrdinal,
            bool continuationOutOfRange = false,
            long? incompleteRetainedTextCharacters = null,
            bool rowsFailed = false) =>
            new(
                _model.DeclaringType,
                MetadataTokens.GetToken(_model.TypeHandle),
                _matchCount,
                rows,
                nextOrdinal,
                continuationOutOfRange,
                incompleteRetainedTextCharacters,
                rowsFailed);
    }

    internal static PreparationResult Prepare(
        MetadataReader reader,
        MetadataMethodSemanticsAssociationResult methodSemantics,
        MetadataTypeDefinitionName declaringType,
        string methodName)
    {
        TypeDefinitionHandle typeHandle = default;
        foreach (TypeDefinitionHandle candidate
            in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionNameMatchResult match =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    candidate,
                    declaringType,
                    out _);
            if (match is MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                return new PreparationResult.Rejected(
                    new MetadataMethodGroupInspectionOutcome.Failed());
            }
            if (match is not MetadataTypeDefinitionNameMatchResult.Match)
                continue;
            if (!typeHandle.IsNil)
            {
                return new PreparationResult.Rejected(
                    new MetadataMethodGroupInspectionOutcome
                        .TypeAmbiguous());
            }
            typeHandle = candidate;
        }
        if (typeHandle.IsNil)
        {
            return new PreparationResult.Rejected(
                new MetadataMethodGroupInspectionOutcome.TypeNotFound());
        }

        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        if (!TryGetAccessorMethods(
                reader,
                typeHandle,
                type,
                methodName,
                methodSemantics,
                out HashSet<MethodDefinitionHandle> accessors,
                out MetadataMethodGroupInspectionOutcome? failure))
        {
            return new PreparationResult.Rejected(failure!);
        }

        return new PreparationResult.Prepared(
            new(
                reader,
                declaringType,
                typeHandle,
                type,
                methodName,
                accessors,
                ApiSurfaceExtractor.GetExplicitImplementationBodies(
                    reader,
                    type),
                ApiSurfaceExtractor.GetInterfaceImplementations(
                    reader,
                    type)));
    }

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
        bool includeHidden,
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
            PreparationResult preparation =
                Prepare(
                    reader,
                    methodSemantics,
                    declaringType,
                    methodName);
            if (preparation
                is PreparationResult.Rejected rejected)
            {
                return rejected.Outcome;
            }
            Analysis model =
                ((PreparationResult.Prepared)preparation).Model;
            return Execute(
                model,
                new(
                    model,
                    accessibility,
                    receiver,
                    includeHidden),
                startOrdinal,
                maximumRows,
                materializeRows,
                maximumMembers,
                maximumRetainedTextCharacters);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new MetadataMethodGroupInspectionOutcome.Failed();
        }
    }

    internal static MetadataMethodGroupInspectionOutcome Execute(
        Analysis model,
        Selection selection,
        int startOrdinal,
        int maximumRows,
        bool materializeRows,
        int maximumMembers,
        int maximumRetainedTextCharacters)
    {
        if (!materializeRows)
        {
            return ExecuteCount(
                model,
                selection,
                startOrdinal,
                maximumMembers);
        }

        var fold =
            new Fold(
                model,
                selection,
                startOrdinal,
                maximumRows,
                materializeRows,
                maximumMembers,
                maximumRetainedTextCharacters);
        foreach (MethodDefinitionHandle handle in model.Methods)
        {
            if (!fold.Accept(handle))
                break;
        }
        return fold.Complete();
    }

    private static MetadataMethodGroupInspectionOutcome ExecuteCount(
        Analysis model,
        Selection selection,
        int startOrdinal,
        int maximumMembers)
    {
        bool groupExists = false;
        int matchCount = 0;
        foreach (MethodDefinitionHandle handle in model.Methods)
        {
            MethodDefinition method =
                model.GetMethod(handle);
            CandidateKind kind =
                selection.Classify(handle, method);
            if (kind is CandidateKind.OutsideGroup)
                continue;
            groupExists = true;
            if (kind is CandidateKind.Filtered)
                continue;

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

        bool continuationOutOfRange =
            startOrdinal > matchCount
                || (startOrdinal == matchCount && matchCount != 0);
        return new MetadataMethodGroupInspectionOutcome.Read(
            model.DeclaringType,
            MetadataTokens.GetToken(model.TypeHandle),
            matchCount,
            [],
            NextOrdinal: null,
            ContinuationOutOfRange: continuationOutOfRange);
    }

    private static bool TryGetAccessorMethods(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        TypeDefinition type,
        string methodName,
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

        int firstPropertyRow = 0;
        int lastPropertyRow = 0;
        foreach (PropertyDefinitionHandle handle in type.GetProperties())
        {
            int row = MetadataTokens.GetRowNumber(handle);
            if (firstPropertyRow == 0)
                firstPropertyRow = row;
            lastPropertyRow = row;
        }
        int firstEventRow = 0;
        int lastEventRow = 0;
        foreach (EventDefinitionHandle handle in type.GetEvents())
        {
            int row = MetadataTokens.GetRowNumber(handle);
            if (firstEventRow == 0)
                firstEventRow = row;
            lastEventRow = row;
        }
        MetadataMethodSemanticsAssociationKind? standardRoleKind =
            null;
        int standardRoleAssociationRow = 0;
        ushort standardRoles = 0;
        foreach (MetadataMethodSemanticsAssociation row
            in success.Associations)
        {
            bool associationBelongsToType =
                row.AssociationKind switch
                {
                    MetadataMethodSemanticsAssociationKind.Property =>
                        IsInRange(
                            row.AssociationRowNumber,
                            firstPropertyRow,
                            lastPropertyRow),
                    MetadataMethodSemanticsAssociationKind.Event =>
                        IsInRange(
                            row.AssociationRowNumber,
                            firstEventRow,
                            lastEventRow),
                    _ => false,
                };
            MethodDefinition method =
                reader.GetMethodDefinition(row.Method);
            bool methodBelongsToType =
                method.GetDeclaringType() == typeHandle;
            if (!associationBelongsToType
                && !methodBelongsToType)
                continue;
            if (!associationBelongsToType
                || !methodBelongsToType
                || !TryValidateRole(
                    row,
                    ref standardRoleKind,
                    ref standardRoleAssociationRow,
                    ref standardRoles))
            {
                failure =
                    new MetadataMethodGroupInspectionOutcome.Failed();
                return false;
            }

            if (reader.StringComparer.Equals(
                    method.Name,
                    methodName))
            {
                accessors.Add(row.Method);
            }
        }

        return true;
    }

    private static bool IsInRange(
        int row,
        int firstRow,
        int lastRow) =>
        firstRow != 0
        && row >= firstRow
        && row <= lastRow;

    private static bool TryValidateRole(
        MetadataMethodSemanticsAssociation row,
        ref MetadataMethodSemanticsAssociationKind? standardRoleKind,
        ref int standardRoleAssociationRow,
        ref ushort standardRoles)
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
        if (!recognized)
            return false;
        if (standardRoleKind != row.AssociationKind
            || standardRoleAssociationRow
                != row.AssociationRowNumber)
        {
            standardRoleKind = row.AssociationKind;
            standardRoleAssociationRow =
                row.AssociationRowNumber;
            standardRoles = 0;
        }
        if ((standardRoles & role) != 0)
            return false;

        standardRoles |= role;
        return true;
    }

    /// <summary>
    /// Whether a method's effective access, as the shared member admission
    /// defines it, falls in the filter's <c>api.accessibility</c> bucket.
    /// </summary>
    private static bool MatchesAccessibility(
        MethodAttributes effectiveAccess,
        MetadataMethodAccessibilityFilter filter)
    {
        MetadataMethodAccessibilityFilter actual =
            ApiSurfaceExtractor.AccessibilityBucket(effectiveAccess);
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
