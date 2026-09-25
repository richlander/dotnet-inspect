using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum MemberGroupCategory
{
    Method,
}

public enum MemberGroupRole
{
    Declared,
}

public enum MemberReceiver
{
    Static,
    This,
    Extension,
}

public sealed record MemberGroupSubject
{
    public MemberGroupSubject(
        MetadataTypeDefinitionName declaringType,
        string name,
        MemberGroupCategory category = MemberGroupCategory.Method,
        MemberGroupRole role = MemberGroupRole.Declared)
    {
        DeclaringType = declaringType
            ?? throw new ArgumentNullException(nameof(declaringType));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "Unknown Member-group category.");
        }
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Unknown Member-group role.");
        }

        Name = name;
        Category = category;
        Role = role;
    }

    public MetadataTypeDefinitionName DeclaringType { get; }
    public string Name { get; }
    public MemberGroupCategory Category { get; }
    public MemberGroupRole Role { get; }
}

public sealed record MemberOverloadCountRequest;

public enum MemberOverloadOrdering
{
    Metadata,
}

public sealed record MemberOverloadPopulationBinding
{
    public MemberOverloadPopulationBinding(
        Guid moduleVersionId,
        MetadataTypeDefinitionName declaringType,
        int typeDefinitionToken,
        string name,
        MemberGroupCategory category,
        MemberGroupRole role,
        MemberOverloadOrdering ordering)
    {
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An exact-Member population binding requires a module version identifier.",
                nameof(moduleVersionId));
        }
        DeclaringType = declaringType
            ?? throw new ArgumentNullException(nameof(declaringType));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            typeDefinitionToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "Unknown Member-group category.");
        }
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Unknown Member-group role.");
        }
        if (!Enum.IsDefined(ordering))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordering),
                ordering,
                "Unknown exact-Member ordering.");
        }

        ModuleVersionId = moduleVersionId;
        TypeDefinitionToken = typeDefinitionToken;
        Name = name;
        Category = category;
        Role = role;
        Ordering = ordering;
    }

    public Guid ModuleVersionId { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public int TypeDefinitionToken { get; }
    public string Name { get; }
    public MemberGroupCategory Category { get; }
    public MemberGroupRole Role { get; }
    public MemberOverloadOrdering Ordering { get; }
}

public sealed record MemberOverloadContinuation
{
    public MemberOverloadContinuation(
        MemberOverloadPopulationBinding binding,
        int nextOrdinal)
    {
        Binding = binding
            ?? throw new ArgumentNullException(nameof(binding));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextOrdinal);
        NextOrdinal = nextOrdinal;
    }

    public MemberOverloadPopulationBinding Binding { get; }
    public int NextOrdinal { get; }
}

public sealed record MemberOverloadRowsRequest
{
    public MemberOverloadRowsRequest(
        int maximumRows,
        MemberOverloadOrdering ordering =
            MemberOverloadOrdering.Metadata,
        MemberOverloadContinuation? continuation = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        if (!Enum.IsDefined(ordering))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordering),
                ordering,
                "Unknown exact-Member ordering.");
        }

        MaximumRows = maximumRows;
        Ordering = ordering;
        Continuation = continuation;
    }

    public int MaximumRows { get; }
    public MemberOverloadOrdering Ordering { get; }
    public MemberOverloadContinuation? Continuation { get; }
}

public sealed record MemberOverloadPopulationRequest
{
    public MemberOverloadPopulationRequest(
        MemberOverloadCountRequest? count,
        MemberOverloadRowsRequest? rows = null)
    {
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "An exact-Member population must request Count, Rows, or both.");
        }

        Count = count;
        Rows = rows;
    }

    public MemberOverloadCountRequest? Count { get; }
    public MemberOverloadRowsRequest? Rows { get; }
}

public sealed record MemberOverloadPopulationInspectionPlan
{
    public MemberOverloadPopulationInspectionPlan(
        MemberGroupSubject subject,
        MemberOverloadPopulationRequest overloads,
        ApiSurfaceExtractionBounds bounds)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Overloads = overloads
            ?? throw new ArgumentNullException(nameof(overloads));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
    }

    public MemberGroupSubject Subject { get; }
    public MemberOverloadPopulationRequest Overloads { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
}

public sealed record MemberOverloadPopulationInspectionRequest
{
    public MemberOverloadPopulationInspectionRequest(
        LibraryReference library,
        MemberOverloadPopulationInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public MemberOverloadPopulationInspectionPlan Plan { get; }
}

public enum MemberOverloadPopulationBound
{
    MetadataRows,
    Members,
    MethodSemanticsAssociations,
    RetainedTextCharacters,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemberOverloadCountOutcome.Counted), "counted")]
public abstract record MemberOverloadCountOutcome
{
    private protected MemberOverloadCountOutcome()
    {
    }

    public sealed record Counted : MemberOverloadCountOutcome
    {
        public Counted(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public int Value { get; }
    }

}

public sealed record MemberOverloadShape(
    int MetadataToken,
    int BaselineOrdinal,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString DisplaySignature,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString CanonicalSignature,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Fingerprint,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Accessibility,
    MemberGroupRole Role,
    MemberReceiver Receiver,
    MemberOverloadPopulationBinding Binding);

public enum MemberOverloadRowsRejection
{
    IncompatibleContinuation,
    StaleContinuation,
    ContinuationOutOfRange,
}

public enum MemberOverloadRowsFailure
{
    MalformedMetadata,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemberOverloadRowsOutcome.Read), "read")]
[JsonDerivedType(typeof(MemberOverloadRowsOutcome.Rejected), "rejected")]
[JsonDerivedType(typeof(MemberOverloadRowsOutcome.Incomplete), "incomplete")]
[JsonDerivedType(typeof(MemberOverloadRowsOutcome.Failed), "failed")]
public abstract record MemberOverloadRowsOutcome
{
    private protected MemberOverloadRowsOutcome()
    {
    }

    public sealed record Read(
        MemberOverloadOrdering Ordering,
        ImmutableArray<MemberOverloadShape> Items,
        MemberOverloadContinuation? Continuation)
        : MemberOverloadRowsOutcome
    {
        public bool IsComplete => Continuation is null;
    }

    public sealed record Rejected(MemberOverloadRowsRejection Reason)
        : MemberOverloadRowsOutcome;

    public sealed record Incomplete(
        MemberOverloadPopulationBound Bound,
        long Limit,
        long Measured)
        : MemberOverloadRowsOutcome;

    public sealed record Failed(MemberOverloadRowsFailure Reason)
        : MemberOverloadRowsOutcome;
}

public sealed record MemberOverloadPopulationResult(
    MemberOverloadPopulationBinding Binding,
    MemberOverloadCountOutcome? Count,
    MemberOverloadRowsOutcome? Rows);

public sealed record MemberOverloadPopulationContent(
    LibraryAssemblyIdentity Assembly,
    MemberGroupSubject Subject,
    MemberOverloadPopulationResult Overloads,
    int AssemblyBytes);

public enum MemberOverloadPopulationInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
    MemberGroupNotFound,
}

public enum MemberOverloadPopulationInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemberOverloadPopulationInspectionOutcome.Available), "available")]
[JsonDerivedType(typeof(MemberOverloadPopulationInspectionOutcome.Rejected), "rejected")]
[JsonDerivedType(typeof(MemberOverloadPopulationInspectionOutcome.Incomplete), "incomplete")]
[JsonDerivedType(typeof(MemberOverloadPopulationInspectionOutcome.Failed), "failed")]
public abstract record MemberOverloadPopulationInspectionOutcome
{
    private protected MemberOverloadPopulationInspectionOutcome()
    {
    }

    public sealed record Available(MemberOverloadPopulationContent Content)
        : MemberOverloadPopulationInspectionOutcome;

    public sealed record Rejected(
        MemberOverloadPopulationInspectionRejection Reason)
        : MemberOverloadPopulationInspectionOutcome;

    public sealed record Incomplete(
        MemberOverloadPopulationBound Bound,
        long Limit,
        long Measured)
        : MemberOverloadPopulationInspectionOutcome;

    public sealed record Failed(
        MemberOverloadPopulationInspectionFailure Reason)
        : MemberOverloadPopulationInspectionOutcome;
}
