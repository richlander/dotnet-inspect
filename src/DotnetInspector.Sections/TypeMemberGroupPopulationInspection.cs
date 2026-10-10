using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum TypeMemberGroupSpelling
{
    CSharp,
    Metadata,
}

public enum TypeMemberGroupAccessibilityFilter
{
    Public,
    Protected,
    Internal,
    Private,
    All,
}

public enum TypeMemberGroupReceiverFilter
{
    All,
    This,
    Static,
    Extension,
    NonExtension,
}

public enum TypeMemberGroupOrdering
{
    Metadata,
}

public sealed record TypeMemberGroupCountRequest;

public sealed record TypeMemberCompositionCountRequest;

public sealed record TypeMemberSelectorCountsRequest;

public sealed record TypeMemberGroupPopulationBinding
{
    public TypeMemberGroupPopulationBinding(
        LibraryAssemblyIdentity assembly,
        Guid moduleVersionId,
        MetadataTypeDefinitionName type,
        int typeDefinitionToken,
        TypeMemberGroupSpelling spelling,
        bool includeHidden,
        TypeMemberGroupAccessibilityFilter accessibility,
        TypeMemberGroupReceiverFilter receiver,
        TypeMemberGroupOrdering ordering)
    {
        Assembly = assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A Type Member-group population binding requires a module version identifier.",
                nameof(moduleVersionId));
        }
        Type = type ?? throw new ArgumentNullException(nameof(type));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            typeDefinitionToken);
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));
        if (!Enum.IsDefined(accessibility))
            throw new ArgumentOutOfRangeException(nameof(accessibility));
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));
        if (!Enum.IsDefined(ordering))
            throw new ArgumentOutOfRangeException(nameof(ordering));

        ModuleVersionId = moduleVersionId;
        TypeDefinitionToken = typeDefinitionToken;
        Spelling = spelling;
        IncludeHidden = includeHidden;
        Accessibility = accessibility;
        Receiver = receiver;
        Ordering = ordering;
    }

    public LibraryAssemblyIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public MetadataTypeDefinitionName Type { get; }
    public int TypeDefinitionToken { get; }
    public TypeMemberGroupSpelling Spelling { get; }
    public bool IncludeHidden { get; }
    public TypeMemberGroupAccessibilityFilter Accessibility { get; }
    public TypeMemberGroupReceiverFilter Receiver { get; }
    public TypeMemberGroupOrdering Ordering { get; }
}

public sealed record TypeMemberGroupRowBinding(
    TypeMemberGroupPopulationBinding Population,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Name,
    MemberGroupCategory Category,
    MemberGroupRole Role);

public sealed record TypeMemberGroupContinuation
{
    public TypeMemberGroupContinuation(
        TypeMemberGroupPopulationBinding binding,
        int nextOrdinal,
        bool includeExactMemberCount)
    {
        Binding = binding
            ?? throw new ArgumentNullException(nameof(binding));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextOrdinal);
        NextOrdinal = nextOrdinal;
        IncludeExactMemberCount = includeExactMemberCount;
    }

    public TypeMemberGroupPopulationBinding Binding { get; }
    public int NextOrdinal { get; }
    public bool IncludeExactMemberCount { get; }
}

public sealed record TypeMemberGroupRowsRequest
{
    public TypeMemberGroupRowsRequest(
        int maximumRows,
        bool includeExactMemberCount = true,
        TypeMemberGroupOrdering ordering =
            TypeMemberGroupOrdering.Metadata,
        TypeMemberGroupContinuation? continuation = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        if (!Enum.IsDefined(ordering))
            throw new ArgumentOutOfRangeException(nameof(ordering));

        MaximumRows = maximumRows;
        IncludeExactMemberCount = includeExactMemberCount;
        Ordering = ordering;
        Continuation = continuation;
    }

    public int MaximumRows { get; }
    public bool IncludeExactMemberCount { get; }
    public TypeMemberGroupOrdering Ordering { get; }
    public TypeMemberGroupContinuation? Continuation { get; }
}

public sealed record TypeMemberGroupPopulationRequest
{
    public TypeMemberGroupPopulationRequest(
        TypeMemberGroupCountRequest? count,
        TypeMemberGroupRowsRequest? rows = null,
        TypeMemberCompositionCountRequest? composition = null,
        TypeMemberSelectorCountsRequest? selectorCounts = null,
        TypeMemberGroupSpelling spelling =
            TypeMemberGroupSpelling.CSharp,
        TypeMemberGroupAccessibilityFilter accessibility =
            TypeMemberGroupAccessibilityFilter.Public,
        TypeMemberGroupReceiverFilter receiver =
            TypeMemberGroupReceiverFilter.All,
        bool includeHidden = false)
    {
        if (count is not null && rows is not null)
        {
            throw new ArgumentException(
                "One Type Member-group QuerySpace execution cannot request both Count and Rows.");
        }
        if (count is null
            && rows is null
            && composition is null
            && selectorCounts is null)
        {
            throw new ArgumentException(
                "A Type Member-group population must request Count, Rows, Composition Count, or selector Counts.");
        }
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));
        if (!Enum.IsDefined(accessibility))
            throw new ArgumentOutOfRangeException(nameof(accessibility));
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));

        Count = count;
        Rows = rows;
        Composition = composition;
        SelectorCounts = selectorCounts;
        Spelling = spelling;
        Accessibility = accessibility;
        Receiver = receiver;
        IncludeHidden = includeHidden;
    }

    public TypeMemberGroupCountRequest? Count { get; }
    public TypeMemberGroupRowsRequest? Rows { get; }
    public TypeMemberCompositionCountRequest? Composition { get; }
    public TypeMemberSelectorCountsRequest? SelectorCounts { get; }
    public TypeMemberGroupSpelling Spelling { get; }
    public TypeMemberGroupAccessibilityFilter Accessibility { get; }
    public TypeMemberGroupReceiverFilter Receiver { get; }
    public bool IncludeHidden { get; }
}

public sealed record TypeMemberGroupPopulationInspectionPlan
{
    public TypeMemberGroupPopulationInspectionPlan(
        MetadataTypeDefinitionName type,
        TypeMemberGroupPopulationRequest members,
        ApiSurfaceExtractionBounds bounds)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Members = members
            ?? throw new ArgumentNullException(nameof(members));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        Query = TypeMemberGroupPopulationQuery.Resolve(members);
    }

    public MetadataTypeDefinitionName Type { get; }
    public TypeMemberGroupPopulationRequest Members { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    internal TypeMemberGroupPopulationExecutionPlan Query { get; }
}

public sealed record TypeMemberGroupPopulationInspectionRequest
{
    public TypeMemberGroupPopulationInspectionRequest(
        LibraryReference library,
        TypeMemberGroupPopulationInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeMemberGroupPopulationInspectionPlan Plan { get; }
}

public sealed record TypeMemberGroupShape(
    TypeMemberGroupRowBinding Binding,
    int BaselineOrdinal,
    MemberGroupReceiverForms Receivers,
    int? ExactMemberCount,
    ImmutableArray<InertString>? SharedGenericParameters = null,
    TypeMemberTraitCounts? Traits = null)
{
    public bool Equals(TypeMemberGroupShape? other)
        => other is not null
            && Binding == other.Binding
            && BaselineOrdinal == other.BaselineOrdinal
            && Receivers == other.Receivers
            && ExactMemberCount == other.ExactMemberCount
            && SharedGenericParameters.HasValue
                == other.SharedGenericParameters.HasValue
            && (!SharedGenericParameters.HasValue
                || SharedGenericParameters.Value.AsSpan().SequenceEqual(
                    other.SharedGenericParameters!.Value.AsSpan()))
            && Traits == other.Traits;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Binding);
        hash.Add(BaselineOrdinal);
        hash.Add(Receivers);
        hash.Add(ExactMemberCount);
        hash.Add(SharedGenericParameters.HasValue);
        if (SharedGenericParameters is { } parameters)
        {
            foreach (InertString parameter in parameters)
                hash.Add(parameter);
        }
        hash.Add(Traits);
        return hash.ToHashCode();
    }
}

public sealed record TypeMemberCompositionCount(
    int Public,
    int Protected,
    int Internal,
    int Private,
    int Static,
    int This,
    int Extension);

public sealed record TypeMemberKindCount(
    MemberGroupCategory Kind,
    int Count);

public sealed record TypeMemberTraitCounts(
    int All,
    int BodyBacked,
    int Static,
    int Instance,
    int Virtual,
    int Interface,
    int Extensions);

public sealed record TypeMemberSelectorCounts(
    ImmutableArray<TypeMemberKindCount> Kinds,
    TypeMemberTraitCounts Traits);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(TypeMemberGroupCountOutcome.Counted),
    "counted")]
public abstract record TypeMemberGroupCountOutcome
{
    private protected TypeMemberGroupCountOutcome()
    {
    }

    public sealed record Counted : TypeMemberGroupCountOutcome
    {
        public Counted(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public int Value { get; }
    }
}

public enum TypeMemberGroupRowsRejection
{
    ContinuationOutOfRange,
}

public enum TypeMemberGroupRowsFailure
{
    MalformedMetadata,
}

public enum TypeMemberGroupPopulationBound
{
    MetadataRows,
    Members,
    RetainedTextCharacters,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeMemberGroupRowsOutcome.Read), "read")]
[JsonDerivedType(typeof(TypeMemberGroupRowsOutcome.Rejected), "rejected")]
[JsonDerivedType(typeof(TypeMemberGroupRowsOutcome.Incomplete), "incomplete")]
[JsonDerivedType(typeof(TypeMemberGroupRowsOutcome.Failed), "failed")]
public abstract record TypeMemberGroupRowsOutcome
{
    private protected TypeMemberGroupRowsOutcome()
    {
    }

    public sealed record Read(
        TypeMemberGroupOrdering Ordering,
        ImmutableArray<TypeMemberGroupShape> Items,
        TypeMemberGroupContinuation? Continuation)
        : TypeMemberGroupRowsOutcome
    {
        public bool IsComplete => Continuation is null;
    }

    public sealed record Rejected(TypeMemberGroupRowsRejection Reason)
        : TypeMemberGroupRowsOutcome;

    public sealed record Incomplete(
        TypeMemberGroupPopulationBound Bound,
        long Limit,
        long Measured)
        : TypeMemberGroupRowsOutcome;

    public sealed record Failed(TypeMemberGroupRowsFailure Reason)
        : TypeMemberGroupRowsOutcome;
}

public sealed record TypeMemberGroupPopulationResult(
    TypeMemberGroupPopulationBinding Binding,
    TypeMemberGroupCountOutcome? Count,
    TypeMemberGroupRowsOutcome? Rows,
    TypeMemberCompositionCount? Composition,
    TypeMemberSelectorCounts? SelectorCounts);

public sealed record TypeMemberGroupPopulationContent(
    LibraryAssemblyIdentity Assembly,
    MetadataTypeDefinitionName Type,
    TypeMemberGroupPopulationResult Members,
    int AssemblyBytes);

public enum TypeMemberGroupPopulationInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    IncompatibleContinuation,
    StaleContinuation,
    TypeNotFound,
    TypeAmbiguous,
}

public enum TypeMemberGroupPopulationInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(TypeMemberGroupPopulationInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeMemberGroupPopulationInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeMemberGroupPopulationInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeMemberGroupPopulationInspectionOutcome.Failed),
    "failed")]
public abstract record TypeMemberGroupPopulationInspectionOutcome
{
    private protected TypeMemberGroupPopulationInspectionOutcome()
    {
    }

    public sealed record Available(
        TypeMemberGroupPopulationContent Content)
        : TypeMemberGroupPopulationInspectionOutcome;

    public sealed record Rejected(
        TypeMemberGroupPopulationInspectionRejection Reason)
        : TypeMemberGroupPopulationInspectionOutcome;

    public sealed record Incomplete(
        TypeMemberGroupPopulationBound Bound,
        long Limit,
        long Measured)
        : TypeMemberGroupPopulationInspectionOutcome;

    public sealed record Failed(
        TypeMemberGroupPopulationInspectionFailure Reason)
        : TypeMemberGroupPopulationInspectionOutcome;
}
