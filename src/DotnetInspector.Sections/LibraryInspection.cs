using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum LibraryTypeAccessibility
{
    Public,
}

public enum LibraryTypeDeclarationSelection
{
    DefinitionsAndForwarders,
    Definitions,
    Forwarders,
}

/// <summary>
/// Request for exact Count over the selected Type population.
/// </summary>
public sealed record LibraryTypePopulationCountRequest;

public enum LibraryTypePopulationOrdering
{
    Metadata,
}

/// <summary>
/// Request for exact Member Count on each returned Type definition row.
/// </summary>
public sealed record LibraryTypeMemberCountRequest;

/// <summary>
/// Opaque source receipt for continuing one exact Type population.
/// </summary>
public sealed record LibraryTypePopulationContinuation
{
    public LibraryTypePopulationContinuation(InertString value)
    {
        if (value.IsEmpty || value.Length > 256)
        {
            throw new ArgumentException(
                "A Library Type continuation must contain a bounded value.",
                nameof(value));
        }

        Value = value;
    }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Value { get; }
}

/// <summary>
/// Request for one bounded ordered segment of the selected Type population.
/// </summary>
public sealed record LibraryTypePopulationRowsRequest
{
    public LibraryTypePopulationRowsRequest(
        int maximumRows,
        LibraryTypePopulationOrdering ordering =
            LibraryTypePopulationOrdering.Metadata,
        LibraryTypeMemberCountRequest? memberCount = null,
        LibraryTypePopulationContinuation? continuation = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        if (!Enum.IsDefined(ordering))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordering),
                ordering,
                "Unknown Library Type ordering.");
        }

        MaximumRows = maximumRows;
        Ordering = ordering;
        MemberCount = memberCount;
        Continuation = continuation;
    }

    public int MaximumRows { get; }
    public LibraryTypePopulationOrdering Ordering { get; }
    public LibraryTypeMemberCountRequest? MemberCount { get; }
    public LibraryTypePopulationContinuation? Continuation { get; }
}

/// <summary>
/// A request for Count over one accessibility-faceted Library Type
/// population.
/// </summary>
public sealed record LibraryTypePopulationRequest
{
    public LibraryTypePopulationRequest(
        LibraryTypeAccessibility accessibility,
        LibraryTypePopulationCountRequest? count,
        LibraryTypePopulationRowsRequest? rows = null,
        LibraryTypeDeclarationSelection declarationSelection =
            LibraryTypeDeclarationSelection.DefinitionsAndForwarders,
        ApiTypeInventoryKinds definitionKinds =
            ApiTypeInventoryKinds.All)
    {
        if (!Enum.IsDefined(accessibility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessibility),
                accessibility,
                "Unknown Library Type accessibility.");
        }
        if (!Enum.IsDefined(declarationSelection))
        {
            throw new ArgumentOutOfRangeException(
                nameof(declarationSelection),
                declarationSelection,
                "Unknown Library Type declaration selection.");
        }
        if ((definitionKinds
                & ~ApiTypeInventoryKinds.All)
            != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitionKinds),
                definitionKinds,
                "Unknown Library Type definition-kind selection.");
        }

        bool includesDefinitions =
            declarationSelection
                is LibraryTypeDeclarationSelection.Definitions
                    or LibraryTypeDeclarationSelection
                        .DefinitionsAndForwarders;
        if (includesDefinitions
            && definitionKinds
                == ApiTypeInventoryKinds.None)
        {
            throw new ArgumentException(
                "A Type population that includes definitions must select at least one definition kind.",
                nameof(definitionKinds));
        }
        if (!includesDefinitions)
        {
            if (definitionKinds
                is not ApiTypeInventoryKinds.All
                    and not ApiTypeInventoryKinds.None)
            {
                throw new ArgumentException(
                    "A forwarder-only Type population cannot select definition kinds.",
                    nameof(definitionKinds));
            }

            definitionKinds =
                ApiTypeInventoryKinds.None;
        }

        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "A Library Type population must request Count, Rows, or both.");
        }

        Accessibility = accessibility;
        DeclarationSelection = declarationSelection;
        DefinitionKinds = definitionKinds;
        Count = count;
        Rows = rows;
    }

    public LibraryTypeAccessibility Accessibility { get; }
    public LibraryTypeDeclarationSelection DeclarationSelection { get; }
    public ApiTypeInventoryKinds DefinitionKinds { get; }
    public LibraryTypePopulationCountRequest? Count { get; }
    public LibraryTypePopulationRowsRequest? Rows { get; }
}

/// <summary>
/// Portable execution plan for one Library inspection.
/// </summary>
public sealed record LibraryInspectionPlan
{
    public LibraryInspectionPlan(
        LibraryTypePopulationRequest types,
        ApiSurfaceExtractionBounds bounds)
    {
        Types = types
            ?? throw new ArgumentNullException(nameof(types));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
    }

    public LibraryTypePopulationRequest Types { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
}

/// <summary>
/// An in-process request pairing portable execution intent with exact Library
/// authority.
/// </summary>
public sealed record LibraryInspectionRequest
{
    public LibraryInspectionRequest(
        LibraryReference library,
        LibraryInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public LibraryInspectionPlan Plan { get; }
}

/// <summary>
/// Portable managed identity for the inspected Library API assembly.
/// </summary>
public sealed record LibraryAssemblyIdentity
{
    public LibraryAssemblyIdentity(
        InertString name,
        Version version,
        InertString? culture,
        InertString? publicKeyToken)
    {
        Name = name;
        Version = version
            ?? throw new ArgumentNullException(nameof(version));
        Culture = culture;
        PublicKeyToken = publicKeyToken;
    }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Name { get; }

    public Version Version { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Culture { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? PublicKeyToken { get; }
}

/// <summary>
/// Identity of the exact Type population whose terminals were executed.
/// </summary>
public sealed record LibraryTypePopulationBinding(
    Guid ModuleVersionId,
    LibraryTypeAccessibility Accessibility,
    LibraryTypeDeclarationSelection DeclarationSelection,
    ApiTypeInventoryKinds DefinitionKinds);

public enum LibraryTypePopulationCountUnavailableReason
{
    UnsupportedModuleExport,
}

public enum LibraryTypePopulationCountBound
{
    MetadataRows,
    RetainedDeclarations,
    RetainedTextCharacters,
    Definitions,
    Forwarders,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryTypePopulationCountOutcome.Counted),
    "counted")]
[JsonDerivedType(
    typeof(LibraryTypePopulationCountOutcome.Unavailable),
    "unavailable")]
[JsonDerivedType(
    typeof(LibraryTypePopulationCountOutcome.Incomplete),
    "incomplete")]
public abstract record LibraryTypePopulationCountOutcome
{
    private LibraryTypePopulationCountOutcome()
    {
    }

    public sealed record Counted : LibraryTypePopulationCountOutcome
    {
        public Counted(
            int forwarders,
            int classes,
            int structs,
            int interfaces,
            int enums,
            int delegates)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(forwarders);
            ArgumentOutOfRangeException.ThrowIfNegative(classes);
            ArgumentOutOfRangeException.ThrowIfNegative(structs);
            ArgumentOutOfRangeException.ThrowIfNegative(interfaces);
            ArgumentOutOfRangeException.ThrowIfNegative(enums);
            ArgumentOutOfRangeException.ThrowIfNegative(delegates);

            Forwarders = forwarders;
            Classes = classes;
            Structs = structs;
            Interfaces = interfaces;
            Enums = enums;
            Delegates = delegates;
        }

        public int Total =>
            checked(Definitions + Forwarders);

        public int Definitions =>
            checked(Classes + Structs + Interfaces + Enums + Delegates);
        public int Forwarders { get; }
        public int Classes { get; }
        public int Structs { get; }
        public int Interfaces { get; }
        public int Enums { get; }
        public int Delegates { get; }

        public int Count(ApiTypeInventoryKind kind) =>
            kind switch
            {
                ApiTypeInventoryKind.Class => Classes,
                ApiTypeInventoryKind.Struct => Structs,
                ApiTypeInventoryKind.Interface => Interfaces,
                ApiTypeInventoryKind.Enum => Enums,
                ApiTypeInventoryKind.Delegate => Delegates,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Unknown API Type kind."),
            };
    }

    public sealed record Unavailable(
        LibraryTypePopulationCountUnavailableReason Reason)
        : LibraryTypePopulationCountOutcome;

    public sealed record Incomplete(
        LibraryTypePopulationCountBound Bound,
        long Limit,
        long Measured)
        : LibraryTypePopulationCountOutcome;
}

public enum LibraryTypeDeclarationKind
{
    Definition,
    Forwarder,
}

public enum LibraryTypeDefinitionAccessibility
{
    Public,
    NonPublic,
}

/// <summary>
/// Detached intra-image evidence for one Library-advertised forwarder.
/// </summary>
public sealed record LibraryTypeForwardingEvidence
{
    public LibraryTypeForwardingEvidence(
        Guid sourceModuleVersionId,
        ImmutableArray<ExportedTypeToken> declarations,
        LibraryAssemblyIdentity targetAssembly)
    {
        if (sourceModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Forwarding evidence requires a source MVID.",
                nameof(sourceModuleVersionId));
        }
        if (declarations.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Forwarding evidence requires an ExportedType occurrence chain.",
                nameof(declarations));
        }

        SourceModuleVersionId = sourceModuleVersionId;
        Declarations = declarations;
        TargetAssembly = targetAssembly
            ?? throw new ArgumentNullException(nameof(targetAssembly));
    }

    public Guid SourceModuleVersionId { get; }
    public ImmutableArray<ExportedTypeToken> Declarations { get; }
    public LibraryAssemblyIdentity TargetAssembly { get; }
}

public enum LibraryTypeMemberCountNotApplicableReason
{
    Forwarder,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryTypeMemberCountOutcome.Counted),
    "counted")]
[JsonDerivedType(
    typeof(LibraryTypeMemberCountOutcome.NotApplicable),
    "not-applicable")]
public abstract record LibraryTypeMemberCountOutcome
{
    private protected LibraryTypeMemberCountOutcome()
    {
    }

    public sealed record Counted : LibraryTypeMemberCountOutcome
    {
        public Counted(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public int Value { get; }
    }

    public sealed record NotApplicable(
        LibraryTypeMemberCountNotApplicableReason Reason)
        : LibraryTypeMemberCountOutcome;
}

/// <summary>
/// One lightweight Type declaration shape owned by a Library document.
/// </summary>
public sealed record LibraryTypeShape
{
    public LibraryTypeShape(
        MetadataTypeDefinitionName identity,
        InertString displayName,
        InertString @namespace,
        LibraryTypeDeclarationKind declarationKind,
        ApiTypeInventoryKind? definitionKind,
        LibraryTypeDefinitionAccessibility? definitionAccessibility,
        bool isPublicSurface,
        LibraryTypeForwardingEvidence? forwarding,
        LibraryTypeMemberCountOutcome? memberCount)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (displayName.IsEmpty)
        {
            throw new ArgumentException(
                "A Library Type row requires a display name.",
                nameof(displayName));
        }
        if (!Enum.IsDefined(declarationKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(declarationKind),
                declarationKind,
                "Unknown Library Type declaration kind.");
        }
        if (definitionKind is { } kind
            && !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitionKind),
                definitionKind,
                "Unknown API Type kind.");
        }
        if (definitionAccessibility is { } accessibility
            && !Enum.IsDefined(accessibility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitionAccessibility),
                definitionAccessibility,
                "Unknown Library Type definition accessibility.");
        }

        switch (declarationKind)
        {
            case LibraryTypeDeclarationKind.Definition:
                if (definitionKind is null
                    || definitionAccessibility is null
                    || forwarding is not null
                    || memberCount
                        is LibraryTypeMemberCountOutcome.NotApplicable)
                {
                    throw new ArgumentException(
                        "A Type definition row requires definition facts and cannot carry forwarding evidence.");
                }
                break;
            case LibraryTypeDeclarationKind.Forwarder:
                if (definitionKind is not null
                    || definitionAccessibility is not null
                    || forwarding is null
                    || memberCount
                        is LibraryTypeMemberCountOutcome.Counted)
                {
                    throw new ArgumentException(
                        "A forwarder row requires forwarding evidence and cannot carry definition facts.");
                }
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Library Type declaration kind.");
        }

        Identity = identity;
        DisplayName = displayName;
        Namespace = @namespace;
        DeclarationKind = declarationKind;
        DefinitionKind = definitionKind;
        DefinitionAccessibility = definitionAccessibility;
        IsPublicSurface = isPublicSurface;
        Forwarding = forwarding;
        MemberCount = memberCount;
    }

    public MetadataTypeDefinitionName Identity { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString DisplayName { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Namespace { get; }

    public LibraryTypeDeclarationKind DeclarationKind { get; }
    public ApiTypeInventoryKind? DefinitionKind { get; }
    public LibraryTypeDefinitionAccessibility? DefinitionAccessibility
    {
        get;
    }
    public bool IsPublicSurface { get; }
    public LibraryTypeForwardingEvidence? Forwarding { get; }
    public LibraryTypeMemberCountOutcome? MemberCount { get; }
}

public enum LibraryTypePopulationRowsUnavailableReason
{
    UnsupportedModuleExport,
}

public enum LibraryTypePopulationRowsRejection
{
    InvalidContinuation,
    IncompatibleContinuation,
    StaleContinuation,
    ContinuationOutOfRange,
}

public enum LibraryTypePopulationRowsBound
{
    MetadataRows,
    RetainedDeclarations,
    RetainedTextCharacters,
}

public enum LibraryTypePopulationRowsFailure
{
    MalformedMetadata,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryTypePopulationRowsOutcome.Read),
    "read")]
[JsonDerivedType(
    typeof(LibraryTypePopulationRowsOutcome.Unavailable),
    "unavailable")]
[JsonDerivedType(
    typeof(LibraryTypePopulationRowsOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(LibraryTypePopulationRowsOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(LibraryTypePopulationRowsOutcome.Failed),
    "failed")]
public abstract record LibraryTypePopulationRowsOutcome
{
    private protected LibraryTypePopulationRowsOutcome()
    {
    }

    public sealed record Read(
        LibraryTypePopulationOrdering Ordering,
        ImmutableArray<LibraryTypeShape> Items,
        LibraryTypePopulationContinuation? Continuation)
        : LibraryTypePopulationRowsOutcome
    {
        public bool IsComplete => Continuation is null;
    }

    public sealed record Unavailable(
        LibraryTypePopulationRowsUnavailableReason Reason)
        : LibraryTypePopulationRowsOutcome;

    public sealed record Rejected(
        LibraryTypePopulationRowsRejection Reason)
        : LibraryTypePopulationRowsOutcome;

    public sealed record Incomplete(
        LibraryTypePopulationRowsBound Bound,
        long Limit,
        long Measured)
        : LibraryTypePopulationRowsOutcome;

    public sealed record Failed(
        LibraryTypePopulationRowsFailure Reason)
        : LibraryTypePopulationRowsOutcome;
}

/// <summary>
/// Detached result for one requested Type population.
/// </summary>
public sealed record LibraryTypePopulationResult(
    LibraryTypePopulationBinding Binding,
    LibraryTypePopulationCountOutcome? Count,
    LibraryTypePopulationRowsOutcome? Rows = null);

/// <summary>
/// Measured work retained for one Library inspection.
/// </summary>
public sealed record LibraryInspectionWork(
    int AssemblyBytes,
    long MetadataRows,
    long RetainedDeclarations,
    long RetainedTextCharacters);

/// <summary>
/// Resource-free, request-shaped content for one exact Library.
/// </summary>
public sealed record LibraryDocument(
    LibraryAssemblyIdentity Assembly,
    Guid ModuleVersionId,
    LibraryTypePopulationResult Types,
    LibraryInspectionWork Work,
    ApiSurfaceExtractionBounds Bounds);

public enum LibraryInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

/// <summary>
/// The closed terminal outcome for one Library inspection request.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(LibraryInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(LibraryInspectionOutcome.Failed),
    "failed")]
public abstract record LibraryInspectionOutcome
{
    private LibraryInspectionOutcome()
    {
    }

    public sealed record Available(LibraryDocument Document)
        : LibraryInspectionOutcome;

    public sealed record Rejected(LibraryInspectionRejection Reason)
        : LibraryInspectionOutcome;

    public sealed record Failed(LibraryInspectionFailure Reason)
        : LibraryInspectionOutcome;
}
