using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal static class MetadataDeclaredMemberGroupInspection
{
    public static MetadataMethodGroupInspectionOutcome Read(
        MetadataReader reader,
        MetadataTypeDefinitionName declaringType,
        string memberName,
        MetadataTypeMemberGroupCategory category,
        int startOrdinal,
        int maximumRows,
        bool materializeRows,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver,
        MetadataMemberSpelling spelling,
        bool includeHidden,
        int maximumMembers,
        int maximumRetainedTextCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        if (!Enum.IsDefined(category)
            || category is MetadataTypeMemberGroupCategory.Method)
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "A declared non-method Member category is required.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        if (!Enum.IsDefined(accessibility))
            throw new ArgumentOutOfRangeException(nameof(accessibility));
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMembers);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);

        try
        {
            TypeDefinitionHandle typeHandle = default;
            foreach (TypeDefinitionHandle candidate
                in reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MetadataTypeDefinitionNameMatchResult match =
                    MetadataTypeDefinitionName.Matches(
                        reader,
                        candidate,
                        declaringType,
                        out _);
                if (match is MetadataTypeDefinitionNameMatchResult.Rejected)
                {
                    return new MetadataMethodGroupInspectionOutcome
                        .Failed();
                }
                if (match is not MetadataTypeDefinitionNameMatchResult.Match)
                    continue;
                if (!typeHandle.IsNil)
                {
                    return new MetadataMethodGroupInspectionOutcome
                        .TypeAmbiguous();
                }
                typeHandle = candidate;
            }
            if (typeHandle.IsNil)
            {
                return new MetadataMethodGroupInspectionOutcome
                    .TypeNotFound();
            }

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            var sink = new SelectionSink(
                reader,
                declaringType,
                typeHandle,
                type,
                memberName,
                category,
                startOrdinal,
                maximumRows,
                materializeRows,
                accessibility,
                receiver,
                includeHidden,
                maximumMembers,
                maximumRetainedTextCharacters,
                cancellationToken);
            ApiSurfaceExtractor.ClassifyDeclaredMembers(
                reader,
                typeHandle,
                type,
                spelling,
                publicOnly: false,
                extensionContainer:
                    AttributeReader.HasExtensionAttribute(
                        reader,
                        type.GetCustomAttributes()),
                classifyLogicalMethodKinds: true,
                ref sink);
            return sink.Complete();
        }
        catch (MemberBoundExceededException exceeded)
        {
            return new MetadataMethodGroupInspectionOutcome.Incomplete(
                MetadataMethodGroupInspectionBound.Members,
                maximumMembers,
                exceeded.Measured);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new MetadataMethodGroupInspectionOutcome.Failed();
        }
    }

    private struct SelectionSink : IClassifiedMemberSink
    {
        private readonly MetadataReader _reader;
        private readonly MetadataTypeDefinitionName _declaringType;
        private readonly TypeDefinitionHandle _typeHandle;
        private readonly TypeDefinition _type;
        private readonly string _memberName;
        private readonly MetadataTypeMemberGroupCategory _category;
        private readonly int _startOrdinal;
        private readonly int _maximumRows;
        private readonly MetadataMethodAccessibilityFilter _accessibility;
        private readonly MetadataMethodReceiverFilter _receiver;
        private readonly bool _includeHidden;
        private readonly int _maximumMembers;
        private readonly int _maximumRetainedTextCharacters;
        private readonly CancellationToken _cancellationToken;
        private readonly List<ClassifiedMember>? _rows;
        private bool _groupExists;
        private int _matchCount;

        public SelectionSink(
            MetadataReader reader,
            MetadataTypeDefinitionName declaringType,
            TypeDefinitionHandle typeHandle,
            TypeDefinition type,
            string memberName,
            MetadataTypeMemberGroupCategory category,
            int startOrdinal,
            int maximumRows,
            bool materializeRows,
            MetadataMethodAccessibilityFilter accessibility,
            MetadataMethodReceiverFilter receiver,
            bool includeHidden,
            int maximumMembers,
            int maximumRetainedTextCharacters,
            CancellationToken cancellationToken)
        {
            _reader = reader;
            _declaringType = declaringType;
            _typeHandle = typeHandle;
            _type = type;
            _memberName = memberName;
            _category = category;
            _startOrdinal = startOrdinal;
            _maximumRows = maximumRows;
            _accessibility = accessibility;
            _receiver = receiver;
            _includeHidden = includeHidden;
            _maximumMembers = maximumMembers;
            _maximumRetainedTextCharacters =
                maximumRetainedTextCharacters;
            _cancellationToken = cancellationToken;
            _rows = materializeRows ? [] : null;
        }

        public void Add(in ClassifiedMember member)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (Category(member.Kind) != _category
                || !_reader.StringComparer.Equals(
                    Name(member),
                    _memberName))
            {
                return;
            }

            _groupExists = true;
            if ((!_includeHidden && member.IsHidden)
                || !Matches(
                    ApiSurfaceExtractor.AccessibilityBucket(
                        member.Access),
                    _accessibility)
                || !Matches(member.Receiver, _receiver))
            {
                return;
            }

            if (_rows is not null
                && _matchCount >= _startOrdinal
                && _rows.Count < _maximumRows)
            {
                _rows.Add(member);
            }
            _matchCount = checked(_matchCount + 1);
            if (_matchCount > _maximumMembers)
            {
                throw new MemberBoundExceededException(_matchCount);
            }
        }

        public MetadataMethodGroupInspectionOutcome Complete()
        {
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

            int rowCount = _rows?.Count ?? 0;
            var rows =
                ImmutableArray.CreateBuilder<MetadataMethodGroupRow>(
                    rowCount);
            long retainedTextCharacters = 0;
            int anchorWorkRemaining =
                MetadataSafetyPolicy.MaxClassificationScanWorkChars;
            for (int index = 0; index < rowCount; index++)
            {
                MetadataMethodGroupRow row;
                try
                {
                    row = Project(
                        _reader,
                        _declaringType,
                        _typeHandle,
                        _type,
                        _rows![index],
                        ref anchorWorkRemaining);
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
                        + row.Anchor.StableSelector.Length
                        + row.Anchor.TypeFullName.Length
                        + row.Anchor.MemberName.Length
                        + row.Fingerprint.Length
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
                _rows is not null && nextOrdinal < _matchCount
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
                _declaringType,
                MetadataTokens.GetToken(_typeHandle),
                _matchCount,
                rows,
                nextOrdinal,
                continuationOutOfRange,
                incompleteRetainedTextCharacters,
                rowsFailed);

        private StringHandle Name(in ClassifiedMember member) =>
            member.Handle.Kind switch
            {
                HandleKind.MethodDefinition =>
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)member.Handle).Name,
                HandleKind.PropertyDefinition =>
                    _reader.GetPropertyDefinition(
                        (PropertyDefinitionHandle)member.Handle).Name,
                HandleKind.FieldDefinition =>
                    _reader.GetFieldDefinition(
                        (FieldDefinitionHandle)member.Handle).Name,
                HandleKind.EventDefinition =>
                    _reader.GetEventDefinition(
                        (EventDefinitionHandle)member.Handle).Name,
                _ => throw new InvalidOperationException(
                    "Unknown declared Member handle."),
            };
    }

    private static MetadataMethodGroupRow Project(
        MetadataReader reader,
        MetadataTypeDefinitionName declaringType,
        TypeDefinitionHandle typeHandle,
        TypeDefinition type,
        in ClassifiedMember member,
        ref int anchorWorkRemaining)
    {
        MemberAnchor anchor;
        string displaySignature;
        string accessibility;
        switch (member.Handle.Kind)
        {
            case HandleKind.MethodDefinition:
                MethodDefinition method =
                    reader.GetMethodDefinition(
                        (MethodDefinitionHandle)member.Handle);
                MetadataMethodDeclaration methodDeclaration =
                    MetadataDeclarationQuery.GetMethod(
                        reader,
                        type,
                        method);
                anchor = ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    typeHandle,
                    method,
                    isExtensionMethod: false);
                displaySignature =
                    MetadataDeclarationQuery.GetMethodSignatureText(
                        methodDeclaration);
                accessibility =
                    ApiSurfaceExtractor.AccessibilityBucketName(
                        member.Access);
                break;
            case HandleKind.PropertyDefinition:
                PropertyDefinition property =
                    reader.GetPropertyDefinition(
                        (PropertyDefinitionHandle)member.Handle);
                MetadataPropertyDeclaration propertyDeclaration =
                    MetadataDeclarationQuery.GetProperty(
                        reader,
                        type,
                        property);
                anchor = ApiMemberIdentity.CreatePropertyAnchor(
                    reader,
                    typeHandle,
                    property,
                    ref anchorWorkRemaining);
                displaySignature =
                    MetadataDeclarationQuery.GetPropertySignatureText(
                        propertyDeclaration);
                accessibility =
                    ApiSurfaceExtractor.AccessibilityBucketName(
                        member.Access);
                break;
            case HandleKind.FieldDefinition:
                FieldDefinition field =
                    reader.GetFieldDefinition(
                        (FieldDefinitionHandle)member.Handle);
                MetadataFieldDeclaration fieldDeclaration =
                    MetadataDeclarationQuery.GetField(
                        reader,
                        type,
                        field);
                anchor = ApiMemberIdentity.CreateFieldAnchor(
                    reader,
                    typeHandle,
                    field,
                    ref anchorWorkRemaining);
                displaySignature =
                    MetadataDeclarationQuery.GetFieldSignatureText(
                        fieldDeclaration);
                accessibility =
                    ApiSurfaceExtractor.AccessibilityBucketName(
                        member.Access);
                break;
            case HandleKind.EventDefinition:
                EventDefinition @event =
                    reader.GetEventDefinition(
                        (EventDefinitionHandle)member.Handle);
                anchor = ApiMemberIdentity.CreateEventAnchor(
                    reader,
                    typeHandle,
                    @event,
                    ref anchorWorkRemaining);
                displaySignature =
                    ApiSurfaceExtractor.GetEventSignatureText(
                        reader,
                        type,
                        @event);
                accessibility =
                    ApiSurfaceExtractor.AccessibilityBucketName(
                        member.Access);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown declared Member handle.");
        }

        return new(
            MetadataTokens.GetToken(member.Handle),
            anchor,
            displaySignature,
            anchor.CanonicalSignature,
            anchor.CanonicalSignature,
            anchor.Fingerprint,
            accessibility,
            member.Receiver,
            member.IsVirtual,
            member.IsExplicitInterfaceImplementation);
    }

    private static MetadataTypeMemberGroupCategory Category(
        ClassifiedMemberKind kind) =>
        kind switch
        {
            ClassifiedMemberKind.Method
                or ClassifiedMemberKind.ExtensionMethod =>
                MetadataTypeMemberGroupCategory.Method,
            ClassifiedMemberKind.Constructor =>
                MetadataTypeMemberGroupCategory.Constructor,
            ClassifiedMemberKind.Operator =>
                MetadataTypeMemberGroupCategory.Operator,
            ClassifiedMemberKind.Finalizer =>
                MetadataTypeMemberGroupCategory.Finalizer,
            ClassifiedMemberKind.ExplicitInterfaceImplementation =>
                MetadataTypeMemberGroupCategory
                    .ExplicitInterfaceImplementation,
            ClassifiedMemberKind.Property =>
                MetadataTypeMemberGroupCategory.Property,
            ClassifiedMemberKind.Field =>
                MetadataTypeMemberGroupCategory.Field,
            ClassifiedMemberKind.Event =>
                MetadataTypeMemberGroupCategory.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static bool Matches(
        MetadataMethodAccessibilityFilter candidate,
        MetadataMethodAccessibilityFilter request) =>
        request is MetadataMethodAccessibilityFilter.All
        || candidate == request;

    private static bool Matches(
        MetadataMethodReceiver candidate,
        MetadataMethodReceiverFilter request) =>
        request switch
        {
            MetadataMethodReceiverFilter.All => true,
            MetadataMethodReceiverFilter.This =>
                candidate is MetadataMethodReceiver.This,
            MetadataMethodReceiverFilter.Static =>
                candidate is MetadataMethodReceiver.Static,
            MetadataMethodReceiverFilter.Extension =>
                candidate is MetadataMethodReceiver.Extension,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private sealed class MemberBoundExceededException(int measured)
        : Exception
    {
        public int Measured { get; } = measured;
    }
}
