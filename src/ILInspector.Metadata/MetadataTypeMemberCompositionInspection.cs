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
        => Read(
            reader,
            new MetadataTypeMemberCompositionModule(reader),
            type,
            spelling,
            includeHidden,
            accessibility);

    /// <summary>
    /// Counts one Type against a module's shared state, so a caller counting
    /// many Types indexes the module and its attached extensions once.
    /// </summary>
    public static MetadataTypeMemberCompositionOutcome Read(
        MetadataReader reader,
        MetadataTypeMemberCompositionModule module,
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling,
        bool includeHidden,
        MetadataMethodAccessibilityFilter accessibility)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(module);
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
            MetadataTypeDefinitionIndex index = module.Index;
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
                // The index found typeHandle as the only row named type, so
                // a TypeDef receiver names type exactly when it is that row
                // and the row's name reads back as type.
                bool typeNameReadable =
                    MetadataTypeDefinitionName.Read(reader, typeHandle)
                        is MetadataTypeDefinitionNameReadResult.Read read
                    && read.Name == type;
                if (module.AttachedForRequest() is { } incidence)
                {
                    incidence.AddTo(typeHandle, typeNameReadable, ref counts);
                }
                else
                {
                    // A module's first request, or one whose module pass
                    // failed on some extension, classifies only this Type's
                    // extensions, so only a request they reach fails.
                    var attached = new AttachedCounts(
                        type,
                        typeHandle,
                        typeNameReadable,
                        counts);
                    ApiSurfaceExtractor.ClassifyAttachedExtensions(
                        reader,
                        publicOnly: false,
                        ref attached);
                    counts = attached.Counts;
                }
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

    internal struct CompositionCounts(
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

        /// <summary>Adds n attached extensions of one bucket and hidden status.</summary>
        public void AddAttached(
            MetadataMethodAccessibilityFilter bucket,
            bool isHidden,
            int n)
        {
            if (isHidden && !includeHidden)
                return;

            switch (bucket)
            {
                case MetadataMethodAccessibilityFilter.Public:
                    _public = checked(_public + n);
                    break;
                case MetadataMethodAccessibilityFilter.Protected:
                    _protected = checked(_protected + n);
                    break;
                case MetadataMethodAccessibilityFilter.Internal:
                    _internal = checked(_internal + n);
                    break;
                default:
                    _private = checked(_private + n);
                    break;
            }
            if (accessibility == MetadataMethodAccessibilityFilter.All
                || accessibility == bucket)
            {
                _extension = checked(_extension + n);
            }
        }

        public readonly MetadataTypeMemberComposition ToComposition()
            => new(_public, _protected, _internal, _private, _static, _this, _extension);
    }

    struct AttachedCounts(
        MetadataTypeDefinitionName receiver,
        TypeDefinitionHandle receiverHandle,
        bool receiverNameReadable,
        CompositionCounts counts)
        : IAttachedExtensionSink
    {
        public CompositionCounts Counts = counts;

        public readonly bool Wants(
            in ExtensionReceiver candidate,
            TypeDefinitionHandle declaringType)
            => declaringType != receiverHandle
                && (candidate.TryGetDefinition(out TypeDefinitionHandle definition)
                    ? receiverNameReadable && definition == receiverHandle
                    : candidate.ReadName() == receiver);

        public void Add(in ExtensionReceiver candidate, in ClassifiedMember member)
            => Counts.Add(member);
    }

    internal static bool IsMetadataFailure(Exception exception)
        => exception is BadImageFormatException
            or ArgumentOutOfRangeException
            or OverflowException
            or ApiSurfaceExtractor.MetadataRowRejectedException;

    /// <summary>One receiver's attached extensions per bucket and hidden status.</summary>
    internal sealed class AttachedTally
    {
        // [bucket * 2 + hidden], counted from TypeDef receivers and from
        // receivers matched by name (a same-module TypeRef or a primitive).
        public readonly int[] ByDefinition = new int[8];
        public readonly int[] ByName = new int[8];
    }

    /// <summary>
    /// The module's attached extensions by receiver TypeDef, from one pass
    /// over its extension containers. A receiver named through a TypeRef or a
    /// primitive is matched to the index's only row of that name, and a name
    /// with no single row is dropped, as a request for it counts nothing.
    /// </summary>
    internal sealed class AttachedIncidence(Dictionary<TypeDefinitionHandle, AttachedTally> tallies)
    {
        public static AttachedIncidence Build(
            MetadataReader reader,
            MetadataTypeDefinitionIndex index)
        {
            var sink = new IncidenceSink(index, []);
            ApiSurfaceExtractor.ClassifyAttachedExtensions(reader, publicOnly: false, ref sink);
            return new AttachedIncidence(sink.Tallies);
        }

        public void AddTo(
            TypeDefinitionHandle receiver,
            bool receiverNameReadable,
            ref CompositionCounts counts)
        {
            if (!tallies.TryGetValue(receiver, out AttachedTally? tally))
                return;
            for (int slot = 0; slot < 8; slot++)
            {
                int n = checked(
                    (receiverNameReadable ? tally.ByDefinition[slot] : 0)
                        + tally.ByName[slot]);
                if (n != 0)
                {
                    counts.AddAttached(
                        (MetadataMethodAccessibilityFilter)(slot >> 1),
                        isHidden: (slot & 1) != 0,
                        n);
                }
            }
        }
    }

    struct IncidenceSink(
        MetadataTypeDefinitionIndex index,
        Dictionary<TypeDefinitionHandle, AttachedTally> tallies)
        : IAttachedExtensionSink
    {
        public readonly Dictionary<TypeDefinitionHandle, AttachedTally> Tallies = tallies;
        TypeDefinitionHandle _receiver;
        bool _byDefinition;

        public bool Wants(in ExtensionReceiver candidate, TypeDefinitionHandle declaringType)
        {
            if (candidate.TryGetDefinition(out TypeDefinitionHandle definition))
            {
                _receiver = definition;
                _byDefinition = true;
            }
            else if (candidate.ReadName() is { } name
                && index.TryGetDefinitions(
                    name,
                    out ImmutableArray<TypeDefinitionHandle> definitions,
                    out bool ambiguous)
                && !ambiguous
                && definitions.Length == 1)
            {
                _receiver = definitions[0];
                _byDefinition = false;
            }
            else
            {
                return false;
            }
            return declaringType != _receiver;
        }

        public void Add(in ExtensionReceiver candidate, in ClassifiedMember member)
        {
            if (!Tallies.TryGetValue(_receiver, out AttachedTally? tally))
                Tallies.Add(_receiver, tally = new AttachedTally());
            int slot = (int)ApiSurfaceExtractor.AccessibilityBucket(member.Access) * 2
                + (member.IsHidden ? 1 : 0);
            int[] counts = _byDefinition ? tally.ByDefinition : tally.ByName;
            counts[slot] = checked(counts[slot] + 1);
        }
    }
}

/// <summary>
/// A module's shared Composition Count state: its Type index and its attached
/// extensions by receiver, each built on first use and reused by every later
/// request. It retains no metadata text.
/// </summary>
internal sealed class MetadataTypeMemberCompositionModule
{
    readonly MetadataReader _reader;
    MetadataTypeDefinitionIndex? _index;
    MetadataTypeMemberCompositionInspection.AttachedIncidence? _attached;
    bool _attachedBuilt;
    bool _attachedRequested;

    public MetadataTypeMemberCompositionModule(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    internal MetadataTypeDefinitionIndex Index
        => _index ??= MetadataTypeDefinitionIndex.Create(_reader);

    /// <summary>
    /// The incidence from a module's second C# request on: a single request
    /// costs one filtered pass, and a caller counting many Types builds the
    /// incidence once. A malformed module pass returns null, so each request
    /// falls back to its own pass and fails only when that pass reaches the
    /// malformed extension.
    /// </summary>
    internal MetadataTypeMemberCompositionInspection.AttachedIncidence? AttachedForRequest()
    {
        if (!_attachedRequested)
        {
            _attachedRequested = true;
            return null;
        }
        return Attached;
    }

    internal MetadataTypeMemberCompositionInspection.AttachedIncidence? Attached
    {
        get
        {
            if (!_attachedBuilt)
            {
                MetadataTypeDefinitionIndex index = Index;
                try
                {
                    _attached = MetadataTypeMemberCompositionInspection.AttachedIncidence.Build(
                        _reader,
                        index);
                }
                catch (Exception exception) when (
                    MetadataTypeMemberCompositionInspection.IsMetadataFailure(exception))
                {
                    _attached = null;
                }
                _attachedBuilt = true;
            }
            return _attached;
        }
    }
}
