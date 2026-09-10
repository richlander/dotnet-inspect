using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>One versioned section catalog selected before terminal planning.</summary>
public sealed record InspectionCatalogReference
{
    public InspectionCatalogReference(string identity, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));

        Identity = identity;
        Version = version;
    }

    public string Identity { get; }

    public int Version { get; }
}

/// <summary>The user's verbosity before host-side section promotion.</summary>
public enum InspectionRequestVerbosity
{
    Quiet,
    Minimal,
    Normal,
    Detailed,
}

/// <summary>The discovery gesture retained as capability-request provenance.</summary>
public enum InspectionDiscoveryRequest
{
    None,
    Structural,
    Effective,
}

/// <summary>
/// User-authored inputs that may request capabilities but do not authorize work.
/// </summary>
public sealed record InspectionCapabilityRequestProvenance
{
    public InspectionCapabilityRequestProvenance(
        InspectionRequestVerbosity verbosity,
        IEnumerable<string> explicitSectionSelectors,
        InspectionDiscoveryRequest discovery)
    {
        if (!Enum.IsDefined(verbosity))
            throw new ArgumentOutOfRangeException(nameof(verbosity));
        ArgumentNullException.ThrowIfNull(explicitSectionSelectors);
        if (!Enum.IsDefined(discovery))
            throw new ArgumentOutOfRangeException(nameof(discovery));

        Verbosity = verbosity;
        ExplicitSectionSelectors = Normalize(
            explicitSectionSelectors,
            nameof(explicitSectionSelectors));
        Discovery = discovery;
    }

    public InspectionRequestVerbosity Verbosity { get; }

    public ImmutableArray<string> ExplicitSectionSelectors { get; }

    public InspectionDiscoveryRequest Discovery { get; }

    static ImmutableArray<string> Normalize(
        IEnumerable<string> values,
        string parameterName)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Section selectors cannot be empty.",
                    parameterName);
            }
            if (seen.Add(value))
                result.Add(value);
        }
        return result.ToImmutable();
    }
}

/// <summary>
/// Catalog-scoped semantic section demand retained before terminal policy.
/// </summary>
public sealed record InspectionSemanticDemand
{
    public InspectionSemanticDemand(
        IEnumerable<string> sections,
        IEnumerable<string> exactSections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(exactSections);

        Sections = Normalize(sections, nameof(sections));
        ExactSections = Normalize(exactSections, nameof(exactSections));
        var sectionSet = Sections.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ExactSections.Any(section => !sectionSet.Contains(section)))
        {
            throw new ArgumentException(
                "Every exact section must belong to the resolved section demand.",
                nameof(exactSections));
        }
    }

    public ImmutableArray<string> Sections { get; }

    public ImmutableArray<string> ExactSections { get; }

    static ImmutableArray<string> Normalize(
        IEnumerable<string> values,
        string parameterName)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Resolved section identities cannot be empty.",
                    parameterName);
            }
            if (seen.Add(value))
                result.Add(value);
        }
        return result.ToImmutable();
    }
}

/// <summary>Exact source and assembly context for one resolved inspection.</summary>
public sealed record ResolvedInspectionSource
{
    public ResolvedInspectionSource(
        AssemblyResolutionProvenance provenance,
        AssemblyReferenceIdentity assembly,
        string libraryKey,
        string? framework)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryKey);

        Provenance = provenance;
        Assembly = assembly;
        LibraryKey = libraryKey;
        Framework = framework;
    }

    public AssemblyResolutionProvenance Provenance { get; }

    public AssemblyReferenceIdentity Assembly { get; }

    public string LibraryKey { get; }

    public string? Framework { get; }
}

/// <summary>One exact resolved member target independent of presentation.</summary>
public sealed record ResolvedInspectionMemberTarget
{
    public ResolvedInspectionMemberTarget(
        string typeName,
        MetadataTypeDefinitionName? typeDefinition,
        MemberAnchor member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(member);

        TypeName = typeName;
        TypeDefinition = typeDefinition;
        Member = member;
    }

    public string TypeName { get; }

    public MetadataTypeDefinitionName? TypeDefinition { get; }

    public MemberAnchor Member { get; }
}

/// <summary>
/// Immutable common state retained before exact-member terminal policy.
/// </summary>
public sealed record ResolvedMemberInspectionBasis
{
    public ResolvedMemberInspectionBasis(
        ResolvedInspectionSource source,
        ResolvedInspectionMemberTarget target,
        InspectionCatalogReference catalog,
        InspectionSemanticDemand semanticDemand,
        InspectionCapabilityRequestProvenance capabilityRequest)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        SemanticDemand = semanticDemand
            ?? throw new ArgumentNullException(nameof(semanticDemand));
        CapabilityRequest = capabilityRequest
            ?? throw new ArgumentNullException(nameof(capabilityRequest));
    }

    public ResolvedInspectionSource Source { get; }

    public ResolvedInspectionMemberTarget Target { get; }

    public InspectionCatalogReference Catalog { get; }

    public InspectionSemanticDemand SemanticDemand { get; }

    public InspectionCapabilityRequestProvenance CapabilityRequest { get; }
}

/// <summary>One closed exact-member terminal plan.</summary>
public abstract record MemberInspectionTerminalPlan
{
    private protected MemberInspectionTerminalPlan(
        ResolvedMemberInspectionBasis basis)
    {
        Basis = basis ?? throw new ArgumentNullException(nameof(basis));
    }

    public ResolvedMemberInspectionBasis Basis { get; }
}

/// <summary>Terminal policy for producing ordinary section results.</summary>
public sealed record SectionExecutionPlan : MemberInspectionTerminalPlan
{
    public SectionExecutionPlan(
        ResolvedMemberInspectionBasis basis,
        InspectionRequestVerbosity verbosity)
        : base(basis)
    {
        if (!Enum.IsDefined(verbosity))
            throw new ArgumentOutOfRangeException(nameof(verbosity));
        Verbosity = verbosity;
    }

    public InspectionRequestVerbosity Verbosity { get; }
}

/// <summary>Terminal policy for determining effective sections.</summary>
public sealed record EffectiveDiscoveryPlan : MemberInspectionTerminalPlan
{
    public EffectiveDiscoveryPlan(ResolvedMemberInspectionBasis basis)
        : base(basis)
    {
    }
}

/// <summary>Terminal policy for projecting one portable semantic view.</summary>
public sealed record ShareProjectionPlan : MemberInspectionTerminalPlan
{
    public ShareProjectionPlan(
        ResolvedMemberInspectionBasis basis,
        ViewFacetId facet)
        : base(basis)
    {
        ArgumentNullException.ThrowIfNull(facet);
        if (!ViewFacetId.TryGetKind(
                facet.Value,
                out StructuralSubjectKind kind)
            || kind != StructuralSubjectKind.Member)
        {
            throw new ArgumentException(
                "A member share plan requires a member view facet.",
                nameof(facet));
        }
        Facet = facet;
    }

    public ViewFacetId Facet { get; }
}
