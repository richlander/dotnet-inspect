using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum LibraryTypeAccessibility
{
    Public,
}

/// <summary>
/// Request for exact Count over the selected Type population.
/// </summary>
public sealed record LibraryTypePopulationCountRequest;

/// <summary>
/// A request for Count over one accessibility-faceted Library Type
/// population.
/// </summary>
public sealed record LibraryTypePopulationRequest
{
    public LibraryTypePopulationRequest(
        LibraryTypeAccessibility accessibility,
        LibraryTypePopulationCountRequest count)
    {
        if (!Enum.IsDefined(accessibility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessibility),
                accessibility,
                "Unknown Library Type accessibility.");
        }

        Accessibility = accessibility;
        Count = count
            ?? throw new ArgumentNullException(nameof(count));
    }

    public LibraryTypeAccessibility Accessibility { get; }
    public LibraryTypePopulationCountRequest Count { get; }
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
    LibraryTypeAccessibility Accessibility);

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

/// <summary>
/// Detached result for one requested Type population.
/// </summary>
public sealed record LibraryTypePopulationResult(
    LibraryTypePopulationBinding Binding,
    LibraryTypePopulationCountOutcome Count);

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
