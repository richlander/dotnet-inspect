using System.Collections.Immutable;

namespace ILInspector.Metadata;

public enum MetadataLibrarySignatureUseDisposition
{
    Complete,
    Partial,
}

public enum MetadataLibrarySignatureUseSiteKind
{
    BaseType,
    Interface,
    TypeConstraint,
    MethodConstraint,
    FieldType,
    PropertyType,
    PropertyParameter,
    EventType,
    MethodReturn,
    MethodParameter,
}

[Flags]
public enum MetadataLibraryTypeClassification
{
    None = 0,
    UniversalBase = 1,
    Enum = 2,
    Attribute = 4,
    Exception = 8,
    Delegate = 16,
}

public enum MetadataLibrarySignatureUseDiagnosticKind
{
    Limit,
    MalformedMetadata,
    UnsupportedShape,
}

public enum MetadataLibrarySignatureUseRejectionKind
{
    UnsupportedImage,
    MalformedImage,
    MissingAssemblyIdentity,
    TypeInventory,
    Limit,
}

public sealed record MetadataLibrarySignatureUseRequest
{
    public MetadataLibrarySignatureUseRequest(
        MetadataOperationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Policy = policy;
    }

    public MetadataLibrarySignatureUseRequest(
        MetadataOperationPolicy policy,
        string exactNamespace)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(exactNamespace);
        Policy = policy;
        ExactNamespace = exactNamespace;
    }

    public MetadataOperationPolicy Policy { get; }
    public string? ExactNamespace { get; }
}

public sealed record MetadataLibrarySignatureUseBatchRequest
{
    public MetadataLibrarySignatureUseBatchRequest(
        MetadataOperationPolicy policy,
        IReadOnlyList<string> exactNamespaces)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(exactNamespaces);
        if (exactNamespaces.Count == 0)
        {
            throw new ArgumentException(
                "A signature-use batch requires at least one exact namespace.",
                nameof(exactNamespaces));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var namespaces =
            ImmutableArray.CreateBuilder<string>(exactNamespaces.Count);
        foreach (string? exactNamespace in exactNamespaces)
        {
            if (exactNamespace is null)
            {
                throw new ArgumentException(
                    "A signature-use batch cannot contain a null namespace.",
                    nameof(exactNamespaces));
            }
            if (!seen.Add(exactNamespace))
            {
                throw new ArgumentException(
                    "A signature-use batch requires unique exact namespaces.",
                    nameof(exactNamespaces));
            }
            namespaces.Add(exactNamespace);
        }

        Policy = policy;
        ExactNamespaces = namespaces.MoveToImmutable();
    }

    public MetadataOperationPolicy Policy { get; }
    public ImmutableArray<string> ExactNamespaces { get; }
}

public sealed record MetadataLibrarySignatureUseReceipt(
    Guid ModuleVersionId,
    AssemblyReferenceIdentity Assembly,
    string? ExactNamespace,
    MetadataOperationCounters Counters);

public sealed record MetadataLibrarySignatureUseBatchReceipt(
    Guid ModuleVersionId,
    AssemblyReferenceIdentity Assembly,
    MetadataOperationCounters PhysicalCounters);

public sealed record MetadataLibrarySignatureType(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    AssemblyTypeDefinitionKind DefinitionKind,
    MetadataLibraryTypeClassification Classification);

public sealed record MetadataLibrarySignatureUseOccurrence(
    MetadataTypeDefinitionAddress Source,
    MetadataTypeDefinitionName SourceType,
    MetadataTypeDefinitionAddress Target,
    MetadataTypeDefinitionName TargetType,
    MetadataLibrarySignatureUseSiteKind SiteKind,
    int MetadataToken,
    int OccurrenceOrdinal);

public sealed record MetadataLibrarySignatureUseCoverage
{
    public MetadataLibrarySignatureUseCoverage(
        int considered,
        int examined,
        int unavailable,
        int limited)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(considered);
        ArgumentOutOfRangeException.ThrowIfNegative(examined);
        ArgumentOutOfRangeException.ThrowIfNegative(unavailable);
        ArgumentOutOfRangeException.ThrowIfNegative(limited);
        if (examined + unavailable + limited != considered)
        {
            throw new ArgumentException(
                "Library signature-use coverage must account for every site.");
        }

        Considered = considered;
        Examined = examined;
        Unavailable = unavailable;
        Limited = limited;
    }

    public int Considered { get; }
    public int Examined { get; }
    public int Unavailable { get; }
    public int Limited { get; }
}

public sealed record MetadataLibrarySignatureUseDiagnostic(
    MetadataLibrarySignatureUseDiagnosticKind Kind,
    int? MetadataToken,
    string Detail,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

public sealed record MetadataLibrarySignatureUseResult(
    MetadataLibrarySignatureUseReceipt Receipt,
    MetadataLibrarySignatureUseDisposition Disposition,
    ImmutableArray<MetadataLibrarySignatureType> Types,
    ImmutableArray<MetadataLibrarySignatureUseOccurrence> Occurrences,
    MetadataLibrarySignatureUseCoverage Coverage,
    ImmutableArray<MetadataLibrarySignatureUseDiagnostic> Diagnostics);

public abstract record MetadataLibrarySignatureUseOutcome
{
    private protected MetadataLibrarySignatureUseOutcome()
    {
    }

    public sealed record Available(
        MetadataLibrarySignatureUseResult Result)
        : MetadataLibrarySignatureUseOutcome;

    public sealed record Rejected(
        MetadataLibrarySignatureUseRejectionKind Kind,
        string Detail,
        MetadataImageFormatResult? Format = null,
        MetadataOperationCounters? Counters = null)
        : MetadataLibrarySignatureUseOutcome;
}

public sealed record MetadataLibrarySignatureUseBatchResult(
    MetadataLibrarySignatureUseBatchReceipt Receipt,
    ImmutableArray<MetadataLibrarySignatureUseResult> Results);

public abstract record MetadataLibrarySignatureUseBatchOutcome
{
    private protected MetadataLibrarySignatureUseBatchOutcome()
    {
    }

    public sealed record Available(
        MetadataLibrarySignatureUseBatchResult Result)
        : MetadataLibrarySignatureUseBatchOutcome;

    public sealed record Rejected(
        MetadataLibrarySignatureUseRejectionKind Kind,
        string Detail,
        MetadataImageFormatResult? Format = null,
        MetadataOperationCounters? Counters = null)
        : MetadataLibrarySignatureUseBatchOutcome;
}
