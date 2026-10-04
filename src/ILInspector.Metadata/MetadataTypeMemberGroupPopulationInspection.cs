using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

public enum MetadataTypeMemberGroupCategory
{
    Method,
    Constructor,
    Operator,
    Finalizer,
    ExplicitInterfaceImplementation,
    Property,
    Field,
    Event,
}

[Flags]
public enum MetadataTypeMemberGroupReceiverForms
{
    None = 0,
    Static = 1,
    This = 2,
    Extension = 4,
}

public enum MetadataTypeMemberGroupReceiverFilter
{
    All,
    This,
    Static,
    Extension,
    NonExtension,
}

public sealed record MetadataTypeMemberGroupCountRequest;

public sealed record MetadataTypeMemberGroupRowsRequest
{
    public MetadataTypeMemberGroupRowsRequest(
        int maximumRows,
        int startOrdinal = 0,
        bool includeExactMemberCount = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);

        MaximumRows = maximumRows;
        StartOrdinal = startOrdinal;
        IncludeExactMemberCount = includeExactMemberCount;
    }

    public int MaximumRows { get; }
    public int StartOrdinal { get; }
    public bool IncludeExactMemberCount { get; }
}

public sealed record MetadataTypeMemberGroupPopulationRequest
{
    public MetadataTypeMemberGroupPopulationRequest(
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling,
        bool includeHidden,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataTypeMemberGroupReceiverFilter receiver,
        MetadataTypeMemberGroupCountRequest? count,
        MetadataTypeMemberGroupRowsRequest? rows = null,
        bool includeComposition = false,
        bool includeSelectorCounts = false)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));
        if (!Enum.IsDefined(accessibility))
            throw new ArgumentOutOfRangeException(nameof(accessibility));
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));
        if (count is null
            && rows is null
            && !includeComposition
            && !includeSelectorCounts)
        {
            throw new ArgumentException(
                "A declared Type Member-group population must request Count, Rows, "
                    + "Composition Count, or selector Counts.");
        }

        Spelling = spelling;
        IncludeHidden = includeHidden;
        Accessibility = accessibility;
        Receiver = receiver;
        Count = count;
        Rows = rows;
        IncludeComposition = includeComposition;
        IncludeSelectorCounts = includeSelectorCounts;
    }

    public MetadataTypeDefinitionName Type { get; }
    public MetadataMemberSpelling Spelling { get; }
    public bool IncludeHidden { get; }
    public MetadataMethodAccessibilityFilter Accessibility { get; }
    public MetadataTypeMemberGroupReceiverFilter Receiver { get; }
    public MetadataTypeMemberGroupCountRequest? Count { get; }
    public MetadataTypeMemberGroupRowsRequest? Rows { get; }
    public bool IncludeComposition { get; }
    public bool IncludeSelectorCounts { get; }
}

public sealed record MetadataTypeMemberGroupPopulationBinding(
    Guid ModuleVersionId,
    MetadataTypeDefinitionName Type,
    int TypeDefinitionToken,
    MetadataMemberSpelling Spelling,
    bool IncludeHidden,
    MetadataMethodAccessibilityFilter Accessibility,
    MetadataTypeMemberGroupReceiverFilter Receiver);

public sealed record MetadataTypeMemberGroupRow(
    string Name,
    MetadataTypeMemberGroupCategory Category,
    MetadataTypeMemberGroupReceiverForms Receivers,
    int? ExactMemberCount);

public sealed record MetadataTypeMemberGroupRows(
    ImmutableArray<MetadataTypeMemberGroupRow> Items,
    int? NextOrdinal,
    bool ContinuationOutOfRange = false,
    long? IncompleteRetainedTextCharacters = null);

public sealed record MetadataTypeMemberGroupPopulation(
    MetadataTypeMemberGroupPopulationBinding Binding,
    MetadataTypeMemberComposition? Composition,
    MetadataTypeMemberSelectorCounts? SelectorCounts,
    int? Count,
    MetadataTypeMemberGroupRows? Rows);

public enum MetadataTypeMemberGroupPopulationBound
{
    Members,
    MetadataRows,
}

public abstract record MetadataTypeMemberGroupPopulationOutcome
{
    private protected MetadataTypeMemberGroupPopulationOutcome()
    {
    }

    public sealed record Available(
        MetadataTypeMemberGroupPopulation Population)
        : MetadataTypeMemberGroupPopulationOutcome;

    public sealed record TypeNotFound
        : MetadataTypeMemberGroupPopulationOutcome;

    public sealed record TypeAmbiguous
        : MetadataTypeMemberGroupPopulationOutcome;

    public sealed record Incomplete(
        MetadataTypeMemberGroupPopulationBound Bound,
        long Limit,
        long Measured)
        : MetadataTypeMemberGroupPopulationOutcome;

    public sealed record Failed
        : MetadataTypeMemberGroupPopulationOutcome;
}

/// <summary>
/// Executes terminal-directed declared Member-group work for one exact Type
/// without constructing a rich API surface or exact-Member result rows.
/// </summary>
internal static class MetadataTypeMemberGroupPopulationInspection
{
    public static MetadataTypeMemberGroupPopulationOutcome Read(
        MetadataReader reader,
        MetadataTypeMemberGroupPopulationRequest request,
        ApiSurfaceExtractionBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bounds);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            long metadataRows =
                MetadataOperationContext.CountMetadataRows(reader);
            if (metadataRows > bounds.MaxMetadataRows)
            {
                return new MetadataTypeMemberGroupPopulationOutcome.Incomplete(
                    MetadataTypeMemberGroupPopulationBound.MetadataRows,
                    bounds.MaxMetadataRows,
                    metadataRows);
            }

            TypeDefinitionHandle typeHandle = default;
            foreach (TypeDefinitionHandle candidate in reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MetadataTypeDefinitionNameMatchResult match =
                    MetadataTypeDefinitionName.Matches(
                        reader,
                        candidate,
                        request.Type,
                        out _);
                if (match is MetadataTypeDefinitionNameMatchResult.Rejected)
                    return new MetadataTypeMemberGroupPopulationOutcome.Failed();
                if (match is not MetadataTypeDefinitionNameMatchResult.Match)
                    continue;
                if (!typeHandle.IsNil)
                {
                    return new MetadataTypeMemberGroupPopulationOutcome
                        .TypeAmbiguous();
                }
                typeHandle = candidate;
            }
            if (typeHandle.IsNil)
            {
                return new MetadataTypeMemberGroupPopulationOutcome
                    .TypeNotFound();
            }

            Guid moduleVersionId =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (moduleVersionId == Guid.Empty)
                return new MetadataTypeMemberGroupPopulationOutcome.Failed();

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            var sink = new PopulationSink(
                reader,
                request,
                bounds,
                typeHandle,
                cancellationToken);
            ApiSurfaceExtractor.ClassifyDeclaredMembers(
                reader,
                typeHandle,
                type,
                request.Spelling,
                publicOnly: false,
                extensionContainer:
                    AttributeReader.HasExtensionAttribute(
                        reader,
                        type.GetCustomAttributes()),
                classifyLogicalMethodKinds: true,
                ref sink);

            return new MetadataTypeMemberGroupPopulationOutcome.Available(
                sink.Complete(moduleVersionId));
        }
        catch (MemberBoundExceededException exceeded)
        {
            return new MetadataTypeMemberGroupPopulationOutcome.Incomplete(
                MetadataTypeMemberGroupPopulationBound.Members,
                bounds.MaxMembers,
                exceeded.Measured);
        }
        catch (Exception exception) when (
            MetadataTypeMemberCompositionInspection.IsMetadataFailure(
                exception))
        {
            return new MetadataTypeMemberGroupPopulationOutcome.Failed();
        }
    }

    private readonly record struct GroupKey(
        StringHandle Name,
        ClassifiedMemberKind Kind);

    private sealed class GroupKeyComparer(MetadataReader reader)
        : IEqualityComparer<GroupKey>
    {
        public bool Equals(GroupKey left, GroupKey right)
        {
            if (left.Kind != right.Kind)
                return false;

            BlobReader leftName = reader.GetBlobReader(left.Name);
            BlobReader rightName = reader.GetBlobReader(right.Name);
            if (leftName.RemainingBytes != rightName.RemainingBytes)
                return false;
            while (leftName.RemainingBytes > 0)
            {
                if (leftName.ReadByte() != rightName.ReadByte())
                    return false;
            }
            return true;
        }

        public int GetHashCode(GroupKey key)
        {
            var hash = new HashCode();
            hash.Add(key.Kind);
            BlobReader name = reader.GetBlobReader(key.Name);
            while (name.RemainingBytes > 0)
                hash.Add(name.ReadByte());
            return hash.ToHashCode();
        }
    }

    private sealed class GroupState
    {
        public int Count;
        public MetadataTypeMemberGroupReceiverForms Receivers;
    }

    private struct PopulationSink : IClassifiedMemberSink
    {
        private readonly MetadataReader _reader;
        private readonly MetadataTypeMemberGroupPopulationRequest _request;
        private readonly ApiSurfaceExtractionBounds _bounds;
        private readonly TypeDefinitionHandle _typeHandle;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<GroupKey, GroupState>? _groups;
        private readonly List<GroupKey>? _order;
        private readonly Dictionary<MetadataTypeMemberGroupCategory, int>?
            _kindCounts;
        private MetadataTypeMemberCompositionInspection.CompositionCounts?
            _composition;
        private int _members;
        private int _static;
        private int _instance;
        private int _virtual;
        private int _interface;
        private int _extensions;

        public PopulationSink(
            MetadataReader reader,
            MetadataTypeMemberGroupPopulationRequest request,
            ApiSurfaceExtractionBounds bounds,
            TypeDefinitionHandle typeHandle,
            CancellationToken cancellationToken)
        {
            _reader = reader;
            _request = request;
            _bounds = bounds;
            _typeHandle = typeHandle;
            _cancellationToken = cancellationToken;
            _groups = request.Count is not null || request.Rows is not null
                ? new(new GroupKeyComparer(reader))
                : null;
            _order = request.Rows is not null ? [] : null;
            _kindCounts = request.IncludeSelectorCounts ? [] : null;
            _composition = request.IncludeComposition
                ? new(
                    request.IncludeHidden,
                    request.Accessibility)
                : null;
        }

        public void Add(in ClassifiedMember member)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            int measured = checked(++_members);
            if (measured > _bounds.MaxMembers)
                throw new MemberBoundExceededException(measured);

            if (_composition is { } composition)
            {
                composition.Add(member);
                _composition = composition;
            }
            if (member.IsHidden && !_request.IncludeHidden)
                return;
            if (!Matches(
                    ApiSurfaceExtractor.AccessibilityBucket(member.Access),
                    _request.Accessibility)
                || !Matches(member.Receiver, _request.Receiver))
            {
                return;
            }

            MetadataTypeMemberGroupCategory category =
                Category(member.Kind);
            if (_kindCounts is not null)
            {
                _kindCounts[category] =
                    checked(_kindCounts.GetValueOrDefault(category) + 1);
                switch (member.Receiver)
                {
                    case MetadataMethodReceiver.Static:
                        _static = checked(_static + 1);
                        break;
                    case MetadataMethodReceiver.This:
                        _instance = checked(_instance + 1);
                        break;
                    case MetadataMethodReceiver.Extension:
                        _extensions = checked(_extensions + 1);
                        break;
                }
                if (member.IsVirtual)
                    _virtual = checked(_virtual + 1);
                if (member.IsExplicitInterfaceImplementation)
                    _interface = checked(_interface + 1);
            }
            if (_groups is null)
                return;

            GroupKey key = new(
                Name(member),
                member.Kind);
            if (!_groups.TryGetValue(key, out GroupState? group))
            {
                group = new();
                _groups.Add(key, group);
                _order?.Add(key);
            }
            group.Count = checked(group.Count + 1);
            group.Receivers |= Receiver(member.Receiver);
        }

        public MetadataTypeMemberGroupPopulation Complete(
            Guid moduleVersionId)
        {
            MetadataTypeMemberGroupRows? rows =
                _request.Rows is { } request
                    ? Rows(request)
                    : null;
            MetadataTypeMemberSelectorCounts? selectorCounts =
                _kindCounts is null
                    ? null
                    : new(
                        [
                            .. _kindCounts.Select(pair =>
                                new MetadataTypeMemberFacetCount(
                                    Kind(pair.Key),
                                    pair.Value)),
                        ],
                        new(
                            checked(_static + _instance + _extensions),
                            _static,
                            _instance,
                            _virtual,
                            _interface,
                            _extensions));
            return new(
                new(
                    moduleVersionId,
                    _request.Type,
                    MetadataTokens.GetToken(_typeHandle),
                    _request.Spelling,
                    _request.IncludeHidden,
                    _request.Accessibility,
                    _request.Receiver),
                _composition?.ToComposition(),
                selectorCounts,
                _request.Count is null ? null : _groups!.Count,
                rows);
        }

        private MetadataTypeMemberGroupRows Rows(
            MetadataTypeMemberGroupRowsRequest request)
        {
            List<GroupKey> order = _order!;
            if (request.StartOrdinal > 0
                && request.StartOrdinal >= order.Count)
            {
                return new([], null, ContinuationOutOfRange: true);
            }

            int end = request.StartOrdinal
                + Math.Min(
                    order.Count - request.StartOrdinal,
                    request.MaximumRows);
            var rows = ImmutableArray.CreateBuilder<
                MetadataTypeMemberGroupRow>(
                    end - request.StartOrdinal);
            long retainedTextCharacters = 0;
            for (int index = request.StartOrdinal; index < end; index++)
            {
                GroupKey key = order[index];
                string name = _reader.GetString(key.Name);
                retainedTextCharacters = checked(
                    retainedTextCharacters + name.Length);
                if (retainedTextCharacters
                    > _bounds.MaxRetainedTextCharacters)
                {
                    return new(
                        [],
                        null,
                        IncompleteRetainedTextCharacters:
                            retainedTextCharacters);
                }

                GroupState group = _groups![key];
                rows.Add(new(
                    name,
                    Category(key.Kind),
                    group.Receivers,
                    request.IncludeExactMemberCount
                        ? group.Count
                        : null));
            }

            return new(
                rows.MoveToImmutable(),
                end < order.Count ? end : null);
        }

        private StringHandle Name(in ClassifiedMember member) =>
            member.Kind switch
            {
                ClassifiedMemberKind.Method
                    or ClassifiedMemberKind.Constructor
                    or ClassifiedMemberKind.Operator
                    or ClassifiedMemberKind.Finalizer
                    or ClassifiedMemberKind.ExplicitInterfaceImplementation
                    or ClassifiedMemberKind.ExtensionMethod =>
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)member.Handle).Name,
                ClassifiedMemberKind.Property =>
                    _reader.GetPropertyDefinition(
                        (PropertyDefinitionHandle)member.Handle).Name,
                ClassifiedMemberKind.Field =>
                    _reader.GetFieldDefinition(
                        (FieldDefinitionHandle)member.Handle).Name,
                ClassifiedMemberKind.Event =>
                    _reader.GetEventDefinition(
                        (EventDefinitionHandle)member.Handle).Name,
                _ => throw new InvalidOperationException(
                    "Unknown classified Member kind."),
            };
    }

    private static bool Matches(
        MetadataMethodAccessibilityFilter candidate,
        MetadataMethodAccessibilityFilter request) =>
        request is MetadataMethodAccessibilityFilter.All
        || candidate == request;

    private static bool Matches(
        MetadataMethodReceiver candidate,
        MetadataTypeMemberGroupReceiverFilter request) =>
        request switch
        {
            MetadataTypeMemberGroupReceiverFilter.All => true,
            MetadataTypeMemberGroupReceiverFilter.This =>
                candidate is MetadataMethodReceiver.This,
            MetadataTypeMemberGroupReceiverFilter.Static =>
                candidate is MetadataMethodReceiver.Static,
            MetadataTypeMemberGroupReceiverFilter.Extension =>
                candidate is MetadataMethodReceiver.Extension,
            MetadataTypeMemberGroupReceiverFilter.NonExtension =>
                candidate is not MetadataMethodReceiver.Extension,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static MetadataTypeMemberGroupReceiverForms Receiver(
        MetadataMethodReceiver receiver) =>
        receiver switch
        {
            MetadataMethodReceiver.Static =>
                MetadataTypeMemberGroupReceiverForms.Static,
            MetadataMethodReceiver.This =>
                MetadataTypeMemberGroupReceiverForms.This,
            MetadataMethodReceiver.Extension =>
                MetadataTypeMemberGroupReceiverForms.Extension,
            _ => throw new ArgumentOutOfRangeException(nameof(receiver)),
        };

    private static MetadataTypeMemberGroupCategory Category(
        ClassifiedMemberKind kind) =>
        kind switch
        {
            ClassifiedMemberKind.Method =>
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
            ClassifiedMemberKind.ExtensionMethod =>
                throw new InvalidOperationException(
                    "A declaration population cannot contain an attached extension."),
            ClassifiedMemberKind.Property =>
                MetadataTypeMemberGroupCategory.Property,
            ClassifiedMemberKind.Field =>
                MetadataTypeMemberGroupCategory.Field,
            ClassifiedMemberKind.Event =>
                MetadataTypeMemberGroupCategory.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string Kind(
        MetadataTypeMemberGroupCategory category) =>
        category switch
        {
            MetadataTypeMemberGroupCategory.Method => "method",
            MetadataTypeMemberGroupCategory.Constructor =>
                "constructor",
            MetadataTypeMemberGroupCategory.Operator => "operator",
            MetadataTypeMemberGroupCategory.Finalizer => "finalizer",
            MetadataTypeMemberGroupCategory
                .ExplicitInterfaceImplementation =>
                "explicit-interface-implementation",
            MetadataTypeMemberGroupCategory.Property => "property",
            MetadataTypeMemberGroupCategory.Field => "field",
            MetadataTypeMemberGroupCategory.Event => "event",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private sealed class MemberBoundExceededException(int measured)
        : Exception
    {
        public int Measured { get; } = measured;
    }
}
