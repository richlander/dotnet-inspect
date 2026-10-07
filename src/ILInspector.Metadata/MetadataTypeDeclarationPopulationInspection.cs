using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using CSharpText;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public sealed record MetadataTypeDeclarationPopulationRequest
{
    public MetadataTypeDeclarationPopulationRequest(
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling,
        bool includeHidden,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataTypeMemberGroupReceiverFilter receiver)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));
        if (!Enum.IsDefined(accessibility))
            throw new ArgumentOutOfRangeException(nameof(accessibility));
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));

        Spelling = spelling;
        IncludeHidden = includeHidden;
        Accessibility = accessibility;
        Receiver = receiver;
    }

    public MetadataTypeDefinitionName Type { get; }
    public MetadataMemberSpelling Spelling { get; }
    public bool IncludeHidden { get; }
    public MetadataMethodAccessibilityFilter Accessibility { get; }
    public MetadataTypeMemberGroupReceiverFilter Receiver { get; }
}

public sealed record MetadataTypeMemberDeclaration(
    string Name,
    MetadataTypeMemberGroupCategory Category,
    int MetadataToken,
    MemberAnchor Anchor,
    int BaselineOrdinal,
    string DisplaySignature,
    string CanonicalSignature,
    string DocumentationId,
    string Fingerprint,
    string Accessibility,
    MetadataMethodReceiver Receiver);

public sealed record MetadataTypeDeclarationPopulation(
    MetadataTypeMemberGroupPopulationBinding Binding,
    ImmutableArray<MetadataTypeMemberDeclaration> Members);

public enum MetadataTypeDeclarationPopulationBound
{
    Members,
    RetainedTextCharacters,
}

public abstract record MetadataTypeDeclarationPopulationOutcome
{
    private protected MetadataTypeDeclarationPopulationOutcome()
    {
    }

    public sealed record Available(
        MetadataTypeDeclarationPopulation Population)
        : MetadataTypeDeclarationPopulationOutcome;

    public sealed record Incomplete(
        MetadataTypeDeclarationPopulationBound Bound,
        long Limit,
        long Measured)
        : MetadataTypeDeclarationPopulationOutcome;

    public sealed record Failed
        : MetadataTypeDeclarationPopulationOutcome;
}

internal static class MetadataTypeDeclarationPopulationInspection
{
    internal static MetadataTypeDeclarationPopulationOutcome ReadResolved(
        MetadataReader reader,
        MetadataTypeDeclarationPopulationRequest request,
        ApiSurfaceExtractionBounds bounds,
        MetadataExactTypeDefinitionResolution.Resolved resolved,
        CancellationToken cancellationToken)
    {
        try
        {
            TypeDefinition type =
                reader.GetTypeDefinition(resolved.Handle);
            var sink = new PopulationSink(
                reader,
                request,
                bounds,
                resolved.Handle,
                type,
                cancellationToken);
            ApiSurfaceExtractor.ClassifyDeclaredMembers(
                reader,
                resolved.Handle,
                type,
                request.Spelling,
                publicOnly: false,
                extensionContainer:
                    AttributeReader.HasExtensionAttribute(
                        reader,
                        type.GetCustomAttributes()),
                classifyLogicalMethodKinds: true,
                ref sink);

            return sink.Complete(resolved.ModuleVersionId);
        }
        catch (MemberBoundExceededException exceeded)
        {
            return new MetadataTypeDeclarationPopulationOutcome.Incomplete(
                MetadataTypeDeclarationPopulationBound.Members,
                bounds.MaxMembers,
                exceeded.Measured);
        }
        catch (Exception exception) when (
            MetadataTypeMemberCompositionInspection.IsMetadataFailure(
                exception))
        {
            return new MetadataTypeDeclarationPopulationOutcome.Failed();
        }
    }

    private readonly record struct GroupKey(
        string Name,
        MetadataTypeMemberGroupCategory Category);

    private struct PopulationSink : IClassifiedMemberSink
    {
        private readonly MetadataReader _reader;
        private readonly MetadataTypeDeclarationPopulationRequest _request;
        private readonly ApiSurfaceExtractionBounds _bounds;
        private readonly TypeDefinitionHandle _typeHandle;
        private readonly TypeDefinition _type;
        private readonly GenericContext _genericContext;
        private readonly IReadOnlySet<MethodDefinitionHandle>
            _explicitImplementationBodies;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<GroupKey, int> _groupCounts;
        private readonly ImmutableArray<MetadataTypeMemberDeclaration>.Builder
            _members;
        private int _scannedMembers;
        private int _anchorWorkRemaining;
        private long _retainedTextCharacters;
        private long? _incompleteRetainedTextCharacters;
        private bool _failed;

        public PopulationSink(
            MetadataReader reader,
            MetadataTypeDeclarationPopulationRequest request,
            ApiSurfaceExtractionBounds bounds,
            TypeDefinitionHandle typeHandle,
            TypeDefinition type,
            CancellationToken cancellationToken)
        {
            _reader = reader;
            _request = request;
            _bounds = bounds;
            _typeHandle = typeHandle;
            _type = type;
            _genericContext = GenericContext.ForType(reader, type);
            _explicitImplementationBodies =
                ApiSurfaceExtractor.GetExplicitImplementationBodies(
                    reader,
                    type);
            _cancellationToken = cancellationToken;
            _groupCounts = [];
            _members =
                ImmutableArray.CreateBuilder<MetadataTypeMemberDeclaration>();
            _anchorWorkRemaining =
                MetadataSafetyPolicy.MaxClassificationScanWorkChars;
        }

        public void Add(in ClassifiedMember member)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            int measured = checked(++_scannedMembers);
            if (measured > _bounds.MaxMembers)
                throw new MemberBoundExceededException(measured);
            if (_failed
                || _incompleteRetainedTextCharacters.HasValue
                || member.IsHidden && !_request.IncludeHidden
                || !MetadataTypeMemberGroupPopulationInspection.Matches(
                    ApiSurfaceExtractor.AccessibilityBucket(
                        member.Access),
                    _request.Accessibility)
                || !MetadataTypeMemberGroupPopulationInspection.Matches(
                    member.Receiver,
                    _request.Receiver))
            {
                return;
            }

            try
            {
                MetadataTypeMemberDeclaration declaration =
                    Project(member);
                long retained = RetainedCharacters(declaration);
                _retainedTextCharacters = checked(
                    _retainedTextCharacters + retained);
                if (_retainedTextCharacters
                    > _bounds.MaxRetainedTextCharacters)
                {
                    _incompleteRetainedTextCharacters =
                        _retainedTextCharacters;
                    return;
                }
                _members.Add(declaration);
            }
            catch (Exception exception) when (
                MetadataTypeMemberCompositionInspection
                    .IsMetadataFailure(exception))
            {
                _failed = true;
            }
        }

        public MetadataTypeDeclarationPopulationOutcome Complete(
            Guid moduleVersionId)
        {
            if (_failed)
            {
                return new MetadataTypeDeclarationPopulationOutcome
                    .Failed();
            }
            if (_incompleteRetainedTextCharacters is { } measured)
            {
                return new MetadataTypeDeclarationPopulationOutcome
                    .Incomplete(
                        MetadataTypeDeclarationPopulationBound
                            .RetainedTextCharacters,
                        _bounds.MaxRetainedTextCharacters,
                        measured);
            }

            return new MetadataTypeDeclarationPopulationOutcome.Available(
                new(
                    new(
                        moduleVersionId,
                        _request.Type,
                        MetadataTokens.GetToken(_typeHandle),
                        _request.Spelling,
                        _request.IncludeHidden,
                        _request.Accessibility,
                        _request.Receiver),
                    _members.ToImmutable()));
        }

        private MetadataTypeMemberDeclaration Project(
            in ClassifiedMember member)
        {
            MetadataTypeMemberGroupCategory category =
                MetadataTypeMemberGroupPopulationInspection.Category(
                    member.Kind);
            string name = Name(member);
            var group = new GroupKey(name, category);
            int baselineOrdinal =
                checked(_groupCounts.GetValueOrDefault(group) + 1);
            _groupCounts[group] = baselineOrdinal;

            return member.Handle.Kind switch
            {
                HandleKind.MethodDefinition =>
                    Method(
                        member,
                        name,
                        category,
                        baselineOrdinal),
                HandleKind.PropertyDefinition =>
                    Property(
                        member,
                        name,
                        category,
                        baselineOrdinal),
                HandleKind.FieldDefinition =>
                    Field(
                        member,
                        name,
                        category,
                        baselineOrdinal),
                HandleKind.EventDefinition =>
                    Event(
                        member,
                        name,
                        category,
                        baselineOrdinal),
                _ => throw new BadImageFormatException(
                    "A classified Member has an unsupported declaration handle."),
            };
        }

        private MetadataTypeMemberDeclaration Method(
            in ClassifiedMember member,
            string name,
            MetadataTypeMemberGroupCategory category,
            int baselineOrdinal)
        {
            var handle = (MethodDefinitionHandle)member.Handle;
            MethodDefinition method =
                _reader.GetMethodDefinition(handle);
            MetadataMethodDeclaration declaration =
                MetadataDeclarationQuery.GetMethod(
                    _reader,
                    _type,
                    method);
            ApiSignature documentationSignature =
                ApiSurfaceExtractor.GetMethodSignatureForIdentity(
                    _reader,
                    _genericContext,
                    handle,
                    method,
                    typeNullableContext: 0)
                .Model;
            MemberAnchor anchor =
                ApiMemberIdentity.CreateMethodAnchorInfo(
                    _reader,
                    _typeHandle,
                    method,
                    ref _anchorWorkRemaining,
                    member.Receiver
                        is MetadataMethodReceiver.Extension)
                .Anchor;
            string documentationId =
                XmlDocumentationNotation.CreateMemberIdentity(
                    "M",
                    _request.Type.Namespace,
                    _request.Type.Segments,
                    declaration.MetadataName,
                    documentationSignature
                        .XmlDocumentationParameterTypes
                        ?? throw new BadImageFormatException(
                            "The method documentation identity could not be decoded."),
                    documentationSignature.TypeParameters.Count,
                    ApiMemberIdentity.IsConversionOperator(name)
                        ? documentationSignature
                            .XmlDocumentationReturnType
                            ?? throw new BadImageFormatException(
                                "The conversion operator documentation identity has no return type.")
                        : null,
                    documentationSignature
                        .XmlDocumentationIsVararg)
                .Value;
            return Declaration(
                name,
                category,
                MetadataTokens.GetToken(handle),
                anchor,
                baselineOrdinal,
                MetadataDeclarationQuery.GetMethodSignatureText(
                    declaration),
                documentationId,
                declaration.Accessibility,
                member.Receiver);
        }

        private MetadataTypeMemberDeclaration Property(
            in ClassifiedMember member,
            string name,
            MetadataTypeMemberGroupCategory category,
            int baselineOrdinal)
        {
            var handle = (PropertyDefinitionHandle)member.Handle;
            PropertyDefinition property =
                _reader.GetPropertyDefinition(handle);
            MetadataPropertyDeclaration declaration =
                MetadataDeclarationQuery.GetProperty(
                    _reader,
                    _type,
                    property);
            ApiSignature documentationSignature =
                ApiSurfaceExtractor.GetPropertySignatureForIdentity(
                    _reader,
                    _genericContext,
                    property,
                    property.GetAccessors(),
                    _explicitImplementationBodies,
                    typeNullableContext: 0)
                .Model;
            MemberAnchor anchor =
                ApiMemberIdentity.CreatePropertyAnchor(
                    _reader,
                    _typeHandle,
                    property,
                    ref _anchorWorkRemaining);
            string documentationId =
                XmlDocumentationNotation.CreateMemberIdentity(
                    "P",
                    _request.Type.Namespace,
                    _request.Type.Segments,
                    declaration.MetadataName,
                    documentationSignature
                        .XmlDocumentationParameterTypes
                        ?? throw new BadImageFormatException(
                            "The property documentation identity could not be decoded."),
                    methodGenericArity: 0,
                    conversionReturnType: null,
                    documentationSignature
                        .XmlDocumentationIsVararg)
                .Value;
            return Declaration(
                name,
                category,
                MetadataTokens.GetToken(handle),
                anchor,
                baselineOrdinal,
                MetadataDeclarationQuery.GetPropertySignatureText(
                    declaration),
                documentationId,
                declaration.Accessibility,
                member.Receiver);
        }

        private MetadataTypeMemberDeclaration Field(
            in ClassifiedMember member,
            string name,
            MetadataTypeMemberGroupCategory category,
            int baselineOrdinal)
        {
            var handle = (FieldDefinitionHandle)member.Handle;
            FieldDefinition field =
                _reader.GetFieldDefinition(handle);
            MetadataFieldDeclaration declaration =
                MetadataDeclarationQuery.GetField(
                    _reader,
                    _type,
                    field);
            MemberAnchor anchor =
                ApiMemberIdentity.CreateFieldAnchor(
                    _reader,
                    _typeHandle,
                    field,
                    ref _anchorWorkRemaining);
            return Declaration(
                name,
                category,
                MetadataTokens.GetToken(handle),
                anchor,
                baselineOrdinal,
                MetadataDeclarationQuery.GetFieldSignatureText(
                    declaration),
                DocumentationId("F", name, []),
                declaration.Accessibility,
                member.Receiver);
        }

        private MetadataTypeMemberDeclaration Event(
            in ClassifiedMember member,
            string name,
            MetadataTypeMemberGroupCategory category,
            int baselineOrdinal)
        {
            var handle = (EventDefinitionHandle)member.Handle;
            EventDefinition eventDefinition =
                _reader.GetEventDefinition(handle);
            MemberAnchor anchor =
                ApiMemberIdentity.CreateEventAnchor(
                    _reader,
                    _typeHandle,
                    eventDefinition,
                    ref _anchorWorkRemaining);
            return Declaration(
                name,
                category,
                MetadataTokens.GetToken(handle),
                anchor,
                baselineOrdinal,
                MetadataDeclarationQuery.GetEventSignatureText(
                    _reader,
                    _type,
                    eventDefinition),
                DocumentationId("E", name, []),
                Accessibility(member.Access),
                member.Receiver);
        }

        private MetadataTypeMemberDeclaration Declaration(
            string name,
            MetadataTypeMemberGroupCategory category,
            int metadataToken,
            MemberAnchor anchor,
            int baselineOrdinal,
            string displaySignature,
            string documentationId,
            string accessibility,
            MetadataMethodReceiver receiver) =>
            new(
                name,
                category,
                metadataToken,
                anchor,
                baselineOrdinal,
                displaySignature,
                anchor.CanonicalSignature,
                documentationId,
                anchor.Fingerprint,
                accessibility,
                receiver);

        private string DocumentationId(
            string prefix,
            string name,
            IReadOnlyList<string> parameters) =>
            XmlDocumentationNotation.CreateMemberIdentity(
                prefix,
                _request.Type.Namespace,
                _request.Type.Segments,
                name,
                parameters,
                methodGenericArity: 0,
                conversionReturnType: null,
                isVararg: false)
            .Value;

        private string Name(in ClassifiedMember member) =>
            member.Handle.Kind switch
            {
                HandleKind.MethodDefinition =>
                    _reader.GetString(
                        _reader.GetMethodDefinition(
                            (MethodDefinitionHandle)member.Handle)
                            .Name),
                HandleKind.PropertyDefinition =>
                    _reader.GetString(
                        _reader.GetPropertyDefinition(
                            (PropertyDefinitionHandle)member.Handle)
                            .Name),
                HandleKind.FieldDefinition =>
                    _reader.GetString(
                        _reader.GetFieldDefinition(
                            (FieldDefinitionHandle)member.Handle)
                            .Name),
                HandleKind.EventDefinition =>
                    _reader.GetString(
                        _reader.GetEventDefinition(
                            (EventDefinitionHandle)member.Handle)
                            .Name),
                _ => throw new BadImageFormatException(
                    "A classified Member has an unsupported declaration handle."),
            };

        private static string Accessibility(MethodAttributes access) =>
            (access & MethodAttributes.MemberAccessMask) switch
            {
                MethodAttributes.PrivateScope => "private",
                MethodAttributes.Private => "private",
                MethodAttributes.FamANDAssem => "private protected",
                MethodAttributes.Assembly => "internal",
                MethodAttributes.Family => "protected",
                MethodAttributes.FamORAssem => "protected internal",
                MethodAttributes.Public => "public",
                _ => throw new BadImageFormatException(
                    "A Member has an unsupported accessibility."),
            };

        private static long RetainedCharacters(
            MetadataTypeMemberDeclaration declaration) =>
            checked(
                declaration.Name.Length
                + declaration.Anchor.StableSelector.Length
                + declaration.Anchor.CanonicalSignature.Length
                + declaration.Anchor.Fingerprint.Length
                + declaration.Anchor.TypeFullName.Length
                + declaration.Anchor.MemberName.Length
                + declaration.DisplaySignature.Length
                + declaration.DocumentationId.Length
                + declaration.Accessibility.Length);
    }

    private sealed class MemberBoundExceededException(int measured)
        : Exception
    {
        public int Measured { get; } = measured;
    }
}
