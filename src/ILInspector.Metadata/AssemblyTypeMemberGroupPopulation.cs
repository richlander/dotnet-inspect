using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using InertText;

namespace ILInspector.Metadata;

public enum AssemblyTypeMemberGroupCategory
{
    Constructor,
    Method,
    Operator,
    ExplicitInterfaceImplementation,
    Property,
    Field,
    Event,
}

public enum AssemblyTypeMemberGroupRole
{
    Declared,
    AttachedExtension,
}

[Flags]
public enum AssemblyTypeMemberReceiverKinds
{
    None = 0,
    Static = 1,
    This = 2,
    Extension = 4,
    All = Static | This | Extension,
}

public enum AssemblyTypeMemberGroupTerminal
{
    Rows,
    Count,
}

public enum AssemblyTypeMemberGroupSelectionStageKind
{
    Head,
    Tail,
    Window,
}

public sealed record AssemblyTypeMemberGroupSelectionStage
{
    private AssemblyTypeMemberGroupSelectionStage(
        AssemblyTypeMemberGroupSelectionStageKind kind,
        int count,
        int? start,
        int? end)
    {
        Kind = kind;
        Count = count;
        Start = start;
        End = end;
    }

    public AssemblyTypeMemberGroupSelectionStageKind Kind { get; }
    public int Count { get; }
    public int? Start { get; }
    public int? End { get; }

    public static AssemblyTypeMemberGroupSelectionStage Head(int count) =>
        new(
            AssemblyTypeMemberGroupSelectionStageKind.Head,
            Positive(count),
            null,
            null);

    public static AssemblyTypeMemberGroupSelectionStage Tail(int count) =>
        new(
            AssemblyTypeMemberGroupSelectionStageKind.Tail,
            Positive(count),
            null,
            null);

    public static AssemblyTypeMemberGroupSelectionStage Window(
        int? start,
        int? end)
    {
        if (start is <= 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (end is <= 0)
            throw new ArgumentOutOfRangeException(nameof(end));
        if (start is not null && end is not null && end < start)
            throw new ArgumentOutOfRangeException(nameof(end));

        return new(
            AssemblyTypeMemberGroupSelectionStageKind.Window,
            0,
            start,
            end);
    }

    private static int Positive(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return value;
    }
}

public sealed class AssemblyTypeMemberGroupPopulationRequest
{
    public AssemblyTypeMemberGroupPopulationRequest(
        MetadataTypeDefinitionAddress type,
        AssemblyTypeMemberGroupCategory category,
        AssemblyTypeMemberReceiverKinds receivers,
        AssemblyTypeMemberGroupTerminal terminal,
        IReadOnlyList<AssemblyTypeMemberGroupSelectionStage> selection,
        bool includeExactMemberCount,
        int maximumRetainedGroups,
        long maximumNameWorkBytes,
        int maximumRetainedTextCharacters)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if ((receivers & ~AssemblyTypeMemberReceiverKinds.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivers));
        }
        if (!Enum.IsDefined(terminal))
            throw new ArgumentOutOfRangeException(nameof(terminal));
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Any(static stage => stage is null))
        {
            throw new ArgumentException(
                "Member-group selection stages cannot contain null.",
                nameof(selection));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedGroups);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumNameWorkBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);
        if (terminal == AssemblyTypeMemberGroupTerminal.Count
            && includeExactMemberCount)
        {
            throw new ArgumentException(
                "A Member-group Count cannot request child Counts.",
                nameof(includeExactMemberCount));
        }

        Type = type;
        Category = category;
        Receivers = receivers;
        Terminal = terminal;
        Selection = selection.ToArray();
        IncludeExactMemberCount = includeExactMemberCount;
        MaximumRetainedGroups = maximumRetainedGroups;
        MaximumNameWorkBytes = maximumNameWorkBytes;
        MaximumRetainedTextCharacters =
            maximumRetainedTextCharacters;
    }

    public MetadataTypeDefinitionAddress Type { get; }
    public AssemblyTypeMemberGroupCategory Category { get; }
    public AssemblyTypeMemberReceiverKinds Receivers { get; }
    public AssemblyTypeMemberGroupTerminal Terminal { get; }
    public IReadOnlyList<AssemblyTypeMemberGroupSelectionStage> Selection
    { get; }
    public bool IncludeExactMemberCount { get; }
    public int MaximumRetainedGroups { get; }
    public long MaximumNameWorkBytes { get; }
    public int MaximumRetainedTextCharacters { get; }
}

public sealed record AssemblyTypeMemberGroupRow(
    InertString Name,
    AssemblyTypeMemberGroupCategory Category,
    AssemblyTypeMemberGroupRole Role,
    MetadataTypeDefinitionName? AttachedDeclaringType,
    MetadataTypeDefinitionAddress? AttachedDeclaringTypeAddress,
    AssemblyTypeMemberReceiverKinds ReceiverKinds,
    int? ExactMemberCount);

public sealed record AssemblyTypeMemberGroupPopulationWork(
    int CandidateMembersVisited,
    int GroupsAggregated,
    int RowsMaterialized,
    int ExactMemberRowsMaterialized,
    int SignaturesDecoded,
    int FormattedSignaturesMaterialized);

public enum AssemblyTypeMemberGroupPopulationBound
{
    RetainedGroups,
    NameWorkBytes,
    RetainedTextCharacters,
}

public enum AssemblyTypeMemberGroupPopulationRejection
{
    TypeAddressMismatch,
    SelectionOutOfRange,
}

public sealed record AssemblyTypeMemberGroupSelectionFailure(
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

public abstract record AssemblyTypeMemberGroupPopulationOutcome
{
    private protected AssemblyTypeMemberGroupPopulationOutcome()
    {
    }

    public sealed record Counted(
        int Value,
        AssemblyTypeMemberGroupPopulationWork Work)
        : AssemblyTypeMemberGroupPopulationOutcome;

    public sealed record Read(
        ImmutableArray<AssemblyTypeMemberGroupRow> Rows,
        AssemblyTypeMemberGroupPopulationWork Work)
        : AssemblyTypeMemberGroupPopulationOutcome;

    public sealed record Incomplete(
        AssemblyTypeMemberGroupPopulationBound Bound,
        long Measured)
        : AssemblyTypeMemberGroupPopulationOutcome;

    public sealed record Rejected(
        AssemblyTypeMemberGroupPopulationRejection Reason,
        AssemblyTypeMemberGroupSelectionFailure? SelectionFailure = null)
        : AssemblyTypeMemberGroupPopulationOutcome;

    public sealed record Failed
        : AssemblyTypeMemberGroupPopulationOutcome;
}

internal static class AssemblyTypeMemberGroupPopulationReader
{
    internal static AssemblyTypeMemberGroupPopulationOutcome Read(
        MetadataReader reader,
        AssemblyTypeMemberGroupPopulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.Type.TryResolve(
                reader,
                out TypeDefinitionHandle subjectHandle))
        {
            return new AssemblyTypeMemberGroupPopulationOutcome.Rejected(
                AssemblyTypeMemberGroupPopulationRejection
                    .TypeAddressMismatch);
        }

        try
        {
            MetadataTypeDefinitionName subjectName =
                MetadataTypeDefinitionNameReader.Read(
                    reader,
                    subjectHandle)
                is MetadataTypeDefinitionNameReadResult.Read read
                    ? read.Name
                    : throw new BadImageFormatException(
                        "The selected TypeDef has no structured identity.");
            var groups = new Dictionary<GroupKey, GroupState>(
                new GroupKeyComparer(
                    reader,
                    request.MaximumNameWorkBytes));
            var order = new List<GroupKey>();
            int visited = 0;
            int signaturesDecoded = 0;
            ApiSurfaceExtractor.ApiSummaryMemberKind selectedKind =
                SummaryKind(request.Category);

            TypeDefinition subject =
                reader.GetTypeDefinition(subjectHandle);
            bool subjectIsExtensionClass =
                IsExtensionClass(reader, subject);
            ApiSurfaceExtractor.VisitSummaryMembers(
                reader,
                subjectHandle,
                subjectIsExtensionClass,
                resolveExtensionReceiver: false,
                member =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    visited = checked(visited + 1);
                    Add(
                        member,
                        AssemblyTypeMemberGroupRole.Declared,
                        declaringType: default);
                },
                selectedKind,
                includeExtensionProperties: true);

            if ((request.Receivers
                    & AssemblyTypeMemberReceiverKinds.Extension) != 0
                && request.Category
                    is AssemblyTypeMemberGroupCategory.Method
                        or AssemblyTypeMemberGroupCategory.Operator
                        or AssemblyTypeMemberGroupCategory.Property)
            {
                foreach (TypeDefinitionHandle declaringHandle
                    in reader.TypeDefinitions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TypeDefinition declaring =
                        reader.GetTypeDefinition(declaringHandle);
                    if (!declaring.IsPublic
                        || TypeFilters.IsCompilerGenerated(
                            reader.GetString(declaring.Name))
                        || AttributeReader.HasHiddenAttribute(
                            reader,
                            declaring.GetCustomAttributes())
                        || !IsExtensionClass(reader, declaring))
                    {
                        continue;
                    }

                    MetadataTypeDefinitionName declaringName =
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            declaringHandle)
                        is MetadataTypeDefinitionNameReadResult.Read
                            declaringRead
                            ? declaringRead.Name
                            : throw new BadImageFormatException(
                                "An extension container has no structured identity.");
                    ApiSurfaceExtractor.VisitSummaryMembers(
                        reader,
                        declaringHandle,
                        isExtensionClass: true,
                        resolveExtensionReceiver: true,
                        member =>
                        {
                            cancellationToken
                                .ThrowIfCancellationRequested();
                            visited = checked(visited + 1);
                            if (member.IsExtension)
                            {
                                signaturesDecoded =
                                    checked(signaturesDecoded + 1);
                            }
                            if (!member.IsExtension
                                || member.ExtensionReceiver != subjectName
                                || declaringName == subjectName)
                            {
                                return;
                            }

                            Add(
                                member,
                                AssemblyTypeMemberGroupRole
                                    .AttachedExtension,
                                declaringHandle);
                        },
                        selectedKind,
                        includeExtensionProperties: true);
                }
            }

            if (!TrySelect(
                    groups.Count,
                    request.Selection,
                    out int start,
                    out int length,
                    out AssemblyTypeMemberGroupSelectionFailure? failure))
            {
                return new AssemblyTypeMemberGroupPopulationOutcome.Rejected(
                    AssemblyTypeMemberGroupPopulationRejection
                        .SelectionOutOfRange,
                    failure);
            }

            var work = new AssemblyTypeMemberGroupPopulationWork(
                visited,
                groups.Count,
                RowsMaterialized: 0,
                ExactMemberRowsMaterialized: 0,
                signaturesDecoded,
                FormattedSignaturesMaterialized: 0);
            if (request.Terminal
                == AssemblyTypeMemberGroupTerminal.Count)
            {
                return new AssemblyTypeMemberGroupPopulationOutcome.Counted(
                    length,
                    work);
            }

            var rows =
                ImmutableArray.CreateBuilder<
                    AssemblyTypeMemberGroupRow>(length);
            long retainedTextCharacters = 0;
            for (int index = start; index < start + length; index++)
            {
                GroupKey key = order[index];
                GroupState state = groups[key];
                var name = new InertString(
                    TextPolicy.Field,
                    reader.GetString(key.Name));
                MetadataTypeDefinitionName? declaringType = null;
                MetadataTypeDefinitionAddress? declaringAddress = null;
                if (key.Role
                    == AssemblyTypeMemberGroupRole.AttachedExtension)
                {
                    declaringType =
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            key.DeclaringType)
                        is MetadataTypeDefinitionNameReadResult.Read
                            declaringRead
                            ? declaringRead.Name
                            : throw new BadImageFormatException(
                                "An attached MemberGroup has no declaring Type identity.");
                    declaringAddress =
                        MetadataTypeDefinitionAddress.FromHandle(
                            reader,
                            key.DeclaringType);
                }

                retainedTextCharacters = checked(
                    retainedTextCharacters
                        + name.Length
                        + (declaringType is null
                            ? 0
                            : RetainedCharacters(declaringType)));
                if (retainedTextCharacters
                    > request.MaximumRetainedTextCharacters)
                {
                    return new AssemblyTypeMemberGroupPopulationOutcome
                        .Incomplete(
                            AssemblyTypeMemberGroupPopulationBound
                                .RetainedTextCharacters,
                            retainedTextCharacters);
                }

                rows.Add(
                    new(
                        name,
                        key.Category,
                        key.Role,
                        declaringType,
                        declaringAddress,
                        state.ReceiverKinds,
                        request.IncludeExactMemberCount
                            ? state.ExactMemberCount
                            : null));
            }

            return new AssemblyTypeMemberGroupPopulationOutcome.Read(
                rows.MoveToImmutable(),
                work with
                {
                    RowsMaterialized = length,
                });

            void Add(
                ApiSurfaceExtractor.ApiSummaryMember member,
                AssemblyTypeMemberGroupRole role,
                TypeDefinitionHandle declaringType)
            {
                if (member.Kind
                    == ApiSurfaceExtractor.ApiSummaryMemberKind.Finalizer)
                {
                    return;
                }
                AssemblyTypeMemberGroupCategory category =
                    Category(member.Kind);

                AssemblyTypeMemberReceiverKinds receiver =
                    member.IsExtension
                        ? AssemblyTypeMemberReceiverKinds.Extension
                        : member.IsStatic
                            ? AssemblyTypeMemberReceiverKinds.Static
                            : AssemblyTypeMemberReceiverKinds.This;
                if ((request.Receivers & receiver) == 0)
                    return;

                var key = new GroupKey(
                    member.Name,
                    category,
                    role,
                    declaringType);
                if (!groups.TryGetValue(key, out GroupState? state))
                {
                    if (groups.Count
                        >= request.MaximumRetainedGroups)
                    {
                        throw new RetainedGroupLimitException(
                            checked(groups.Count + 1));
                    }
                    state = new GroupState();
                    groups.Add(key, state);
                    order.Add(key);
                }

                state.ExactMemberCount =
                    checked(state.ExactMemberCount + 1);
                state.ReceiverKinds |= receiver;
            }
        }
        catch (RetainedGroupLimitException exception)
        {
            return new AssemblyTypeMemberGroupPopulationOutcome.Incomplete(
                AssemblyTypeMemberGroupPopulationBound.RetainedGroups,
                exception.Measured);
        }
        catch (NameWorkLimitException exception)
        {
            return new AssemblyTypeMemberGroupPopulationOutcome.Incomplete(
                AssemblyTypeMemberGroupPopulationBound.NameWorkBytes,
                exception.Measured);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new AssemblyTypeMemberGroupPopulationOutcome.Failed();
        }
    }

    private static bool IsExtensionClass(
        MetadataReader reader,
        TypeDefinition definition)
    {
        TypeAttributes attributes = definition.Attributes;
        return (attributes
                & (TypeAttributes.Sealed | TypeAttributes.Abstract))
            == (TypeAttributes.Sealed | TypeAttributes.Abstract)
            && AttributeReader.HasExtensionAttribute(
                reader,
                definition.GetCustomAttributes());
    }

    private static AssemblyTypeMemberGroupCategory Category(
        ApiSurfaceExtractor.ApiSummaryMemberKind kind) =>
        kind switch
        {
            ApiSurfaceExtractor.ApiSummaryMemberKind.Constructor =>
                AssemblyTypeMemberGroupCategory.Constructor,
            ApiSurfaceExtractor.ApiSummaryMemberKind.Method =>
                AssemblyTypeMemberGroupCategory.Method,
            ApiSurfaceExtractor.ApiSummaryMemberKind.Operator =>
                AssemblyTypeMemberGroupCategory.Operator,
            ApiSurfaceExtractor.ApiSummaryMemberKind
                    .ExplicitInterfaceImplementation =>
                AssemblyTypeMemberGroupCategory
                    .ExplicitInterfaceImplementation,
            ApiSurfaceExtractor.ApiSummaryMemberKind.Property =>
                AssemblyTypeMemberGroupCategory.Property,
            ApiSurfaceExtractor.ApiSummaryMemberKind.Field =>
                AssemblyTypeMemberGroupCategory.Field,
            ApiSurfaceExtractor.ApiSummaryMemberKind.Event =>
                AssemblyTypeMemberGroupCategory.Event,
            _ => throw new InvalidOperationException(
                "Unknown summary Member kind."),
        };

    private static ApiSurfaceExtractor.ApiSummaryMemberKind SummaryKind(
        AssemblyTypeMemberGroupCategory category) =>
        category switch
        {
            AssemblyTypeMemberGroupCategory.Constructor =>
                ApiSurfaceExtractor.ApiSummaryMemberKind.Constructor,
            AssemblyTypeMemberGroupCategory.Method =>
                ApiSurfaceExtractor.ApiSummaryMemberKind.Method,
            AssemblyTypeMemberGroupCategory.Operator =>
                ApiSurfaceExtractor.ApiSummaryMemberKind.Operator,
            AssemblyTypeMemberGroupCategory
                    .ExplicitInterfaceImplementation =>
                ApiSurfaceExtractor.ApiSummaryMemberKind
                    .ExplicitInterfaceImplementation,
            AssemblyTypeMemberGroupCategory.Property =>
                ApiSurfaceExtractor.ApiSummaryMemberKind.Property,
            AssemblyTypeMemberGroupCategory.Field =>
                ApiSurfaceExtractor.ApiSummaryMemberKind.Field,
            AssemblyTypeMemberGroupCategory.Event =>
                ApiSurfaceExtractor.ApiSummaryMemberKind.Event,
            _ => throw new InvalidOperationException(
                "Unknown MemberGroup category."),
        };

    private static long RetainedCharacters(
        MetadataTypeDefinitionName name)
    {
        long characters =
            new InertString(
                TextPolicy.Field,
                name.Namespace).Length;
        foreach (string segment in name.Segments)
        {
            characters = checked(
                characters
                    + new InertString(
                        TextPolicy.Field,
                        segment).Length);
        }
        return characters;
    }

    private static bool TrySelect(
        int sourceCount,
        IReadOnlyList<AssemblyTypeMemberGroupSelectionStage> stages,
        out int start,
        out int length,
        out AssemblyTypeMemberGroupSelectionFailure? failure)
    {
        start = 0;
        length = sourceCount;
        failure = null;
        for (int index = 0; index < stages.Count; index++)
        {
            AssemblyTypeMemberGroupSelectionStage stage = stages[index];
            switch (stage.Kind)
            {
                case AssemblyTypeMemberGroupSelectionStageKind.Head:
                    length = Math.Min(length, stage.Count);
                    break;
                case AssemblyTypeMemberGroupSelectionStageKind.Tail:
                {
                    int selected = Math.Min(length, stage.Count);
                    start += length - selected;
                    length = selected;
                    break;
                }
                case AssemblyTypeMemberGroupSelectionStageKind.Window:
                {
                    if (stage.Start is null && stage.End is null)
                        break;
                    int required = stage.End ?? stage.Start!.Value;
                    if (required > length)
                    {
                        failure = new(
                            index + 1,
                            required,
                            length);
                        return false;
                    }
                    int first = (stage.Start ?? 1) - 1;
                    int endExclusive = stage.End ?? length;
                    start += first;
                    length = endExclusive - first;
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        "Unknown Member-group selection stage.");
            }
        }

        return true;
    }

    private readonly record struct GroupKey(
        StringHandle Name,
        AssemblyTypeMemberGroupCategory Category,
        AssemblyTypeMemberGroupRole Role,
        TypeDefinitionHandle DeclaringType);

    private sealed class GroupKeyComparer(
        MetadataReader reader,
        long maximumNameWorkBytes)
        : IEqualityComparer<GroupKey>
    {
        private readonly Dictionary<StringHandle, int> _hashes = [];
        private long _nameWorkBytes;

        public bool Equals(GroupKey left, GroupKey right)
        {
            if (left.Category != right.Category
                || left.Role != right.Role
                || left.DeclaringType != right.DeclaringType)
            {
                return false;
            }
            if (left.Name == right.Name)
                return true;

            BlobReader leftName = reader.GetBlobReader(left.Name);
            BlobReader rightName = reader.GetBlobReader(right.Name);
            if (leftName.Length != rightName.Length)
                return false;
            Charge(checked((long)leftName.Length + rightName.Length));
            while (leftName.RemainingBytes > 0)
            {
                if (leftName.ReadByte() != rightName.ReadByte())
                    return false;
            }
            return true;
        }

        public int GetHashCode(GroupKey key)
        {
            if (_hashes.TryGetValue(key.Name, out int existing))
                return Combine(existing, key);

            var hash = new HashCode();
            BlobReader name = reader.GetBlobReader(key.Name);
            Charge(name.Length);
            while (name.RemainingBytes > 0)
                hash.Add(name.ReadByte());
            int nameHash = hash.ToHashCode();
            _hashes.Add(key.Name, nameHash);
            return Combine(nameHash, key);
        }

        private static int Combine(int nameHash, GroupKey key)
        {
            var hash = new HashCode();
            hash.Add(nameHash);
            hash.Add(key.Category);
            hash.Add(key.Role);
            hash.Add(key.DeclaringType);
            return hash.ToHashCode();
        }

        private void Charge(long bytes)
        {
            _nameWorkBytes = checked(_nameWorkBytes + bytes);
            if (_nameWorkBytes > maximumNameWorkBytes)
                throw new NameWorkLimitException(_nameWorkBytes);
        }
    }

    private sealed class GroupState
    {
        internal int ExactMemberCount;
        internal AssemblyTypeMemberReceiverKinds ReceiverKinds;
    }

    private sealed class RetainedGroupLimitException(long measured)
        : Exception
    {
        internal long Measured { get; } = measured;
    }

    private sealed class NameWorkLimitException(long measured)
        : Exception
    {
        internal long Measured { get; } = measured;
    }
}
