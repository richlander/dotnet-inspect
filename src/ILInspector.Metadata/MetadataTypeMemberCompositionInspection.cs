using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// One Type's Composition Count
/// (docs/design/type-member-inspection-documents.md#composition-count): the
/// declaration Count of each <c>api.accessibility</c> bucket, and of each
/// receiver form among the declarations the request's accessibility admits.
/// Every Count is in the request's spelling unit and under its hidden
/// admission.
/// </summary>
public sealed record MetadataTypeMemberComposition(
    int Public,
    int Protected,
    int Internal,
    int Private,
    int Static,
    int This,
    int Extension);

public abstract record MetadataTypeMemberCompositionOutcome
{
    private protected MetadataTypeMemberCompositionOutcome()
    {
    }

    public sealed record Counted(
        MetadataTypeDefinitionName Type,
        int TypeDefinitionToken,
        MetadataTypeMemberComposition Composition)
        : MetadataTypeMemberCompositionOutcome;

    public sealed record TypeNotFound
        : MetadataTypeMemberCompositionOutcome;

    public sealed record TypeAmbiguous
        : MetadataTypeMemberCompositionOutcome;

    public sealed record Failed
        : MetadataTypeMemberCompositionOutcome;
}

/// <summary>
/// Counts one Type's Member population in one pass over its member tables,
/// plus one pass over the module's extension containers for attached
/// extensions under C# spelling. It classifies declarations with the same
/// admission rules as extraction and constructs no rows.
/// </summary>
internal static class MetadataTypeMemberCompositionInspection
{
    public static MetadataTypeMemberCompositionOutcome Read(
        MetadataReader reader,
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling,
        bool includeHidden,
        MetadataMethodAccessibilityFilter accessibility)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(spelling))
        {
            throw new ArgumentOutOfRangeException(
                nameof(spelling),
                spelling,
                "Unknown Member spelling.");
        }
        if (!Enum.IsDefined(accessibility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessibility),
                accessibility,
                "Unknown accessibility filter.");
        }

        try
        {
            MetadataTypeDefinitionIndex index =
                MetadataTypeDefinitionIndex.Create(reader);
            if (!index.TryGetDefinitions(
                    type,
                    out ImmutableArray<TypeDefinitionHandle> definitions,
                    out bool ambiguous))
            {
                return new MetadataTypeMemberCompositionOutcome.TypeNotFound();
            }
            if (ambiguous || definitions.Length != 1)
                return new MetadataTypeMemberCompositionOutcome.TypeAmbiguous();

            TypeDefinitionHandle typeHandle = definitions[0];
            TypeDefinition typeDef = reader.GetTypeDefinition(typeHandle);
            var counts = new CompositionCounts(includeHidden, accessibility);
            // The same receiver rule as the MemberGroup receiver filter.
            ApiSurfaceExtractor.ClassifyDeclaredMembers(
                reader,
                typeDef,
                spelling,
                publicOnly: false,
                extensionContainer: AttributeReader.HasExtensionAttribute(
                    reader,
                    typeDef.GetCustomAttributes()),
                ref counts);
            if (spelling == MetadataMemberSpelling.CSharp)
            {
                var attached = new AttachedCounts(type, typeHandle, counts);
                ApiSurfaceExtractor.ClassifyAttachedExtensions(
                    reader,
                    publicOnly: false,
                    ref attached);
                counts = attached.Counts;
            }

            return new MetadataTypeMemberCompositionOutcome.Counted(
                type,
                MetadataTokens.GetToken(typeHandle),
                counts.ToComposition());
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException
                or ApiSurfaceExtractor.MetadataRowRejectedException)
        {
            return new MetadataTypeMemberCompositionOutcome.Failed();
        }
    }

    struct CompositionCounts(
        bool includeHidden,
        MetadataMethodAccessibilityFilter accessibility)
        : IClassifiedMemberSink
    {
        int _public;
        int _protected;
        int _internal;
        int _private;
        int _static;
        int _this;
        int _extension;

        public void Add(in ClassifiedMember member)
        {
            if (member.IsHidden && !includeHidden)
                return;

            MetadataMethodAccessibilityFilter bucket =
                ApiSurfaceExtractor.AccessibilityBucket(member.Access);
            switch (bucket)
            {
                case MetadataMethodAccessibilityFilter.Public:
                    _public = checked(_public + 1);
                    break;
                case MetadataMethodAccessibilityFilter.Protected:
                    _protected = checked(_protected + 1);
                    break;
                case MetadataMethodAccessibilityFilter.Internal:
                    _internal = checked(_internal + 1);
                    break;
                default:
                    _private = checked(_private + 1);
                    break;
            }

            if (accessibility != MetadataMethodAccessibilityFilter.All
                && accessibility != bucket)
            {
                return;
            }
            switch (member.Receiver)
            {
                case MetadataMethodReceiver.Static:
                    _static = checked(_static + 1);
                    break;
                case MetadataMethodReceiver.This:
                    _this = checked(_this + 1);
                    break;
                default:
                    _extension = checked(_extension + 1);
                    break;
            }
        }

        public readonly MetadataTypeMemberComposition ToComposition()
            => new(_public, _protected, _internal, _private, _static, _this, _extension);
    }

    struct AttachedCounts(
        MetadataTypeDefinitionName receiver,
        TypeDefinitionHandle receiverHandle,
        CompositionCounts counts)
        : IAttachedExtensionSink
    {
        public CompositionCounts Counts = counts;

        public readonly bool Wants(
            MetadataTypeDefinitionName candidate,
            TypeDefinitionHandle declaringType)
            => candidate == receiver && declaringType != receiverHandle;

        public void Add(MetadataTypeDefinitionName candidate, in ClassifiedMember member)
            => Counts.Add(member);
    }
}
