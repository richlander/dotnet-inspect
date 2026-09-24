using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum MetadataRelationFamily
{
    Hierarchy,
    Extensions,
    AssemblyReferences,
    Signatures,
}

public enum MetadataRelationFamilyDisposition
{
    Complete,
    Partial,
    Unavailable,
    Failed,
}

public enum MetadataHierarchyRelationKind
{
    BaseType,
    Interface,
}

public enum MetadataSignatureRelationKind
{
    Accepts,
    Returns,
}

public enum MetadataRelationDiagnosticKind
{
    Limit,
    MalformedMetadata,
    UnsupportedShape,
}

public sealed record MetadataRelationInspectionRequest
{
    public MetadataRelationInspectionRequest(
        IEnumerable<MetadataRelationFamily> families,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        IEnumerable<MetadataTypeDefinitionAddress>? typeScope = null)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(policy);
        MetadataRelationFamily[] familyCopy = [.. families];
        if (familyCopy.Length == 0
            || familyCopy.Any(static family => !Enum.IsDefined(family))
            || familyCopy.Distinct().Count() != familyCopy.Length)
        {
            throw new ArgumentException(
                "A Metadata relation request requires distinct defined families.",
                nameof(families));
        }

        Families = [.. familyCopy.Order()];
        Policy = policy;
        IncludeNonPublic = includeNonPublic;
        MetadataTypeDefinitionAddress[] scopeCopy =
            [.. typeScope ?? []];
        if (scopeCopy.Distinct().Count() != scopeCopy.Length)
        {
            throw new ArgumentException(
                "A Metadata relation type scope requires distinct addresses.",
                nameof(typeScope));
        }
        TypeScope = [.. scopeCopy];
    }

    public ImmutableArray<MetadataRelationFamily> Families { get; }

    public MetadataOperationPolicy Policy { get; }

    public bool IncludeNonPublic { get; }

    public ImmutableArray<MetadataTypeDefinitionAddress> TypeScope
    { get; }

    public bool Includes(MetadataRelationFamily family) =>
        Families.Contains(family);

    internal bool IncludesType(
        MetadataReader reader,
        TypeDefinitionHandle handle) =>
        TypeScope.IsEmpty
        || TypeScope.Contains(
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                handle));
}

public sealed record MetadataRelationInspectionReceipt(
    Guid? ModuleVersionId,
    AssemblyReferenceIdentity? Assembly,
    ImmutableArray<MetadataRelationFamily> Families,
    MetadataOperationCounters Counters);

public sealed record MetadataRelationDiagnostic(
    MetadataRelationFamily Family,
    MetadataRelationDiagnosticKind Kind,
    int? MetadataToken,
    string Detail,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

public sealed record MetadataRelationCoverage
{
    public MetadataRelationCoverage(
        int considered,
        int examined,
        int excluded,
        int unavailable,
        int limited)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(considered);
        ArgumentOutOfRangeException.ThrowIfNegative(examined);
        ArgumentOutOfRangeException.ThrowIfNegative(excluded);
        ArgumentOutOfRangeException.ThrowIfNegative(unavailable);
        ArgumentOutOfRangeException.ThrowIfNegative(limited);
        if (examined + excluded + unavailable + limited != considered)
        {
            throw new ArgumentException(
                "Metadata relation coverage must account for every candidate.");
        }

        Considered = considered;
        Examined = examined;
        Excluded = excluded;
        Unavailable = unavailable;
        Limited = limited;
    }

    public int Considered { get; }

    public int Examined { get; }

    public int Excluded { get; }

    public int Unavailable { get; }

    public int Limited { get; }
}

public sealed record MetadataRelationFamilyResult<TEvidence>
{
    public MetadataRelationFamilyResult(
        bool wasRequested,
        MetadataRelationFamilyDisposition? disposition,
        MetadataRelationCoverage? coverage,
        IEnumerable<TEvidence> evidence,
        IEnumerable<MetadataRelationDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        TEvidence[] evidenceCopy = [.. evidence];
        MetadataRelationDiagnostic[] diagnosticCopy =
            [.. diagnostics ?? []];
        if (!wasRequested
            && (disposition is not null
                || coverage is not null
                || evidenceCopy.Length != 0
                || diagnosticCopy.Length != 0))
        {
            throw new ArgumentException(
                "An unrequested relation family cannot publish an outcome.");
        }
        if (wasRequested && disposition is null)
        {
            throw new ArgumentException(
                "A requested relation family requires a disposition.");
        }
        if (wasRequested && coverage is null)
        {
            throw new ArgumentException(
                "A requested relation family requires coverage.",
                nameof(coverage));
        }
        if (disposition is not null
            && !Enum.IsDefined(disposition.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (disposition == MetadataRelationFamilyDisposition.Complete
            && diagnosticCopy.Length != 0)
        {
            throw new ArgumentException(
                "A complete relation family cannot retain diagnostics.");
        }
        if (disposition is MetadataRelationFamilyDisposition.Unavailable
                or MetadataRelationFamilyDisposition.Failed
            && evidenceCopy.Length != 0)
        {
            throw new ArgumentException(
                "An unavailable or failed relation family cannot publish evidence.");
        }

        WasRequested = wasRequested;
        Disposition = disposition;
        Coverage = coverage;
        Evidence = [.. evidenceCopy];
        Diagnostics = [.. diagnosticCopy];
    }

    public bool WasRequested { get; }

    public MetadataRelationFamilyDisposition? Disposition { get; }

    public MetadataRelationCoverage? Coverage { get; }

    public ImmutableArray<TEvidence> Evidence { get; }

    public ImmutableArray<MetadataRelationDiagnostic> Diagnostics { get; }

    internal static MetadataRelationFamilyResult<TEvidence> NotRequested()
        => new(false, null, null, []);
}

public sealed record MetadataHierarchyRelationEvidence(
    MetadataTypeDefinitionAddress Source,
    MetadataTypeDefinitionName SourceType,
    MetadataHierarchyRelationKind Kind,
    MetadataTypeIdentity Target,
    int MetadataToken);

public sealed record MetadataExtensionRelationEvidence(
    MetadataTypeDefinitionAddress DeclaringType,
    MetadataTypeDefinitionName DeclaringTypeName,
    MetadataTypeDefinitionAddress ReceiverContextType,
    int DeclarationMetadataToken,
    MetadataMethodAddress ReceiverDeclarationMethod,
    MemberAnchor Member,
    MetadataTypeIdentity Receiver);

public sealed record MetadataAssemblyReferenceRelationEvidence(
    AssemblyReferenceIdentity Source,
    AssemblyReferenceIdentity Target,
    int MetadataToken);

public sealed record MetadataSignatureRelationEvidence(
    MetadataTypeDefinitionAddress DeclaringType,
    MetadataTypeDefinitionName DeclaringTypeName,
    MetadataMethodAddress Method,
    MemberAnchor Member,
    MetadataSignatureRelationKind Kind,
    int? ParameterIndex,
    MetadataTypeIdentity Shape);

public sealed record MetadataRelationInspectionResult(
    MetadataRelationInspectionReceipt Receipt,
    MetadataRelationFamilyResult<MetadataHierarchyRelationEvidence>
        Hierarchy,
    MetadataRelationFamilyResult<MetadataExtensionRelationEvidence>
        Extensions,
    MetadataRelationFamilyResult<MetadataAssemblyReferenceRelationEvidence>
        AssemblyReferences,
    MetadataRelationFamilyResult<MetadataSignatureRelationEvidence>
        Signatures);

public abstract record MetadataRelationInspectionOutcome
{
    private protected MetadataRelationInspectionOutcome()
    {
    }

    public sealed record Available(MetadataRelationInspectionResult Result)
        : MetadataRelationInspectionOutcome;

    public sealed record Rejected(
        MetadataImageFormatResult Format,
        string Detail)
        : MetadataRelationInspectionOutcome;
}
