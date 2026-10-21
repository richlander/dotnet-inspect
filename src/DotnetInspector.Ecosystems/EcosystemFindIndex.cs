using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Ecosystems;

public enum EcosystemFindIndexSymbolKind
{
    Type,
    Member,
}

public enum EcosystemFindIndexGapKind
{
    PrefixEnumerationBounded,
    PackageRejected,
    LibraryFailed,
    ScannerUnavailable,
    TypeIdentityUnresolved,
    MemberEvidenceIncomplete,
}

public static class EcosystemFindIndexGenerationContract
{
    public const string ScannerIdentity = "dotnet-inspect.integration-observation";
    public const int ScannerVersion = 3;
    public const int SymbolProjectionVersion = 3;
    public const int PrefixEnumerationPolicyVersion = 1;
    public const int PackageVersionSelectionPolicyVersion = 1;
    public const int LibrarySelectionPolicyVersion = 1;
    public const int TypeIdentityFormatVersion = 1;
    public const int MemberAnchorFormatVersion = 1;
}

public sealed record EcosystemFindIndexConceptAssociation(
    string ConceptId,
    string Kind);

public sealed record EcosystemFindIndexSourceCoordinate
{
    public EcosystemFindIndexSourceCoordinate(
        int prefixOrdinal,
        int packageOrdinal,
        int libraryOrdinal,
        PackageCoordinate package,
        string targetFramework,
        string library,
        PackageLibraryIdentity asset,
        AssemblyReferenceIdentity assembly,
        Guid moduleVersionId)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(library);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(assembly);
        if (prefixOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(prefixOrdinal));
        if (packageOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(packageOrdinal));
        if (libraryOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(libraryOrdinal));
        PackageCoordinateResolution.Invalid? invalid =
            PackageCoordinateResolver.Validate(package);
        if (invalid is not null
            || string.IsNullOrWhiteSpace(package.Version)
            || package.Framework is not null
            || package.RuntimeIdentifier is not null)
        {
            throw new ArgumentException(
                invalid?.Message
                    ?? "An indexed package coordinate requires one exact version and no embedded framework or RID.",
                nameof(package));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.AssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.AssemblySimpleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(assembly.Name);
        if (assembly.Version is null)
        {
            throw new ArgumentException(
                "An indexed assembly requires an exact version.",
                nameof(assembly));
        }
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An indexed assembly requires a module version ID.",
                nameof(moduleVersionId));
        }

        PrefixOrdinal = prefixOrdinal;
        PackageOrdinal = packageOrdinal;
        LibraryOrdinal = libraryOrdinal;
        Package = package;
        TargetFramework = targetFramework;
        Library = library;
        Asset = asset;
        Assembly = assembly;
        ModuleVersionId = moduleVersionId;
    }

    public int PrefixOrdinal { get; }
    public int PackageOrdinal { get; }
    public int LibraryOrdinal { get; }
    public PackageCoordinate Package { get; }
    public string TargetFramework { get; }
    public string Library { get; }
    public PackageLibraryIdentity Asset { get; }
    public AssemblyReferenceIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
}

public sealed record EcosystemFindIndexSourceInput
{
    public EcosystemFindIndexSourceInput(
        EcosystemFindIndexSourceCoordinate coordinate,
        IReadOnlyList<EcosystemIntegrationSignalInfo> signals)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(signals);
        Coordinate = coordinate;
        Signals = signals;
    }

    public EcosystemFindIndexSourceCoordinate Coordinate { get; }
    public IReadOnlyList<EcosystemIntegrationSignalInfo> Signals { get; }
}

public sealed record EcosystemFindIndexGapInput(
    EcosystemFindIndexGapKind Kind,
    int PrefixOrdinal,
    PackageCoordinate? Package,
    int? PackageOrdinal,
    PackageLibraryIdentity? Library,
    int? LibraryOrdinal,
    string Detail,
    int? SignalOrdinal = null);

public sealed record EcosystemFindIndexBuildRequest
{
    public EcosystemFindIndexBuildRequest(
        EcosystemPackId ecosystem,
        IReadOnlyList<IntegrationConceptDescriptor> configuredConcepts,
        IReadOnlyList<PackagePrefixDeclaration> prefixes,
        IReadOnlyList<EcosystemFindIndexSourceInput> sources,
        IReadOnlyList<EcosystemFindIndexGapInput> gaps)
    {
        ArgumentNullException.ThrowIfNull(ecosystem);
        ArgumentNullException.ThrowIfNull(configuredConcepts);
        ArgumentNullException.ThrowIfNull(prefixes);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(gaps);

        Ecosystem = ecosystem;
        ConfiguredConcepts = configuredConcepts;
        Prefixes = prefixes;
        Sources = sources;
        Gaps = gaps;
    }

    public EcosystemPackId Ecosystem { get; }
    public IReadOnlyList<IntegrationConceptDescriptor> ConfiguredConcepts { get; }
    public IReadOnlyList<PackagePrefixDeclaration> Prefixes { get; }
    public IReadOnlyList<EcosystemFindIndexSourceInput> Sources { get; }
    public IReadOnlyList<EcosystemFindIndexGapInput> Gaps { get; }
}

public sealed record EcosystemFindIndexGap(
    EcosystemFindIndexGapKind Kind,
    int PrefixOrdinal,
    PackageCoordinate? Package,
    int? PackageOrdinal,
    PackageLibraryIdentity? Library,
    int? LibraryOrdinal,
    InertString Detail,
    int? SignalOrdinal);

public sealed class EcosystemFindIndexSymbol
{
    internal EcosystemFindIndexSymbol(
        int sourceIndex,
        int symbolOrdinal,
        EcosystemFindIndexSymbolKind kind,
        MetadataTypeDefinitionName? type,
        TypeDeclarationDiscoveryAttributes? typeDiscoveryAttributes,
        EcosystemIntegrationApiEvidence? member,
        InertString display,
        ImmutableArray<EcosystemFindIndexConceptAssociation> concepts)
    {
        SourceIndex = sourceIndex;
        SymbolOrdinal = symbolOrdinal;
        Kind = kind;
        Type = type;
        TypeDiscoveryAttributes = typeDiscoveryAttributes;
        Member = member;
        Display = display;
        Concepts = concepts;
    }

    public int SourceIndex { get; }
    public int SymbolOrdinal { get; }
    public EcosystemFindIndexSymbolKind Kind { get; }
    public MetadataTypeDefinitionName? Type { get; }
    public TypeDeclarationDiscoveryAttributes? TypeDiscoveryAttributes
        { get; }
    public EcosystemIntegrationApiEvidence? Member { get; }
    public InertString Display { get; }
    public ImmutableArray<EcosystemFindIndexConceptAssociation> Concepts { get; }
}

public sealed class EcosystemFindIndexArtifact
{
    internal EcosystemFindIndexArtifact(
        int schemaVersion,
        int builderAlgorithmVersion,
        EcosystemPackId ecosystem,
        string scannerIdentity,
        int scannerVersion,
        int integrationCatalogRevision,
        int symbolProjectionVersion,
        int prefixEnumerationPolicyVersion,
        int packageVersionSelectionPolicyVersion,
        int librarySelectionPolicyVersion,
        int typeIdentityFormatVersion,
        int memberAnchorFormatVersion,
        ImmutableArray<string> configuredConcepts,
        ImmutableArray<PackagePrefixDeclaration> prefixes,
        ImmutableArray<EcosystemFindIndexSourceCoordinate> sources,
        ImmutableArray<EcosystemFindIndexSymbol> symbols,
        ImmutableArray<EcosystemFindIndexGap> gaps)
    {
        SchemaVersion = schemaVersion;
        BuilderAlgorithmVersion = builderAlgorithmVersion;
        Ecosystem = ecosystem;
        ScannerIdentity = scannerIdentity;
        ScannerVersion = scannerVersion;
        IntegrationCatalogRevision = integrationCatalogRevision;
        SymbolProjectionVersion = symbolProjectionVersion;
        PrefixEnumerationPolicyVersion = prefixEnumerationPolicyVersion;
        PackageVersionSelectionPolicyVersion = packageVersionSelectionPolicyVersion;
        LibrarySelectionPolicyVersion = librarySelectionPolicyVersion;
        TypeIdentityFormatVersion = typeIdentityFormatVersion;
        MemberAnchorFormatVersion = memberAnchorFormatVersion;
        ConfiguredConcepts = configuredConcepts;
        Prefixes = prefixes;
        Sources = sources;
        Symbols = symbols;
        Gaps = gaps;
    }

    public int SchemaVersion { get; }
    public int BuilderAlgorithmVersion { get; }
    public EcosystemPackId Ecosystem { get; }
    public string ScannerIdentity { get; }
    public int ScannerVersion { get; }
    public int IntegrationCatalogRevision { get; }
    public int SymbolProjectionVersion { get; }
    public int PrefixEnumerationPolicyVersion { get; }
    public int PackageVersionSelectionPolicyVersion { get; }
    public int LibrarySelectionPolicyVersion { get; }
    public int TypeIdentityFormatVersion { get; }
    public int MemberAnchorFormatVersion { get; }
    public ImmutableArray<string> ConfiguredConcepts { get; }
    public ImmutableArray<PackagePrefixDeclaration> Prefixes { get; }
    public ImmutableArray<EcosystemFindIndexSourceCoordinate> Sources { get; }
    public ImmutableArray<EcosystemFindIndexSymbol> Symbols { get; }
    public ImmutableArray<EcosystemFindIndexGap> Gaps { get; }
}

public static class EcosystemFindIndexBuilder
{
    public const int SchemaVersion = 3;
    public const int AlgorithmVersion = 3;

    public static EcosystemFindIndexArtifact Build(
        EcosystemFindIndexBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ImmutableArray<PackagePrefixDeclaration> prefixes =
            SnapshotPrefixes(request.Prefixes);
        ImmutableArray<string> configuredConcepts =
            SnapshotConfiguredConcepts(request.ConfiguredConcepts);
        ImmutableArray<EcosystemFindIndexSourceInput> inputs =
            SnapshotSources(request.Sources, prefixes);

        var symbols = new List<MutableSymbol>();
        var typeSymbols =
            new Dictionary<(int Source, MetadataTypeDefinitionName Type), MutableSymbol>();
        var memberSymbols =
            new Dictionary<(int Source, MemberAnchor Member), MutableSymbol>();
        var generatedGaps = new List<EcosystemFindIndexGap>();

        for (int sourceIndex = 0; sourceIndex < inputs.Length; sourceIndex++)
        {
            EcosystemFindIndexSourceInput input = inputs[sourceIndex];
            ProjectSignals(
                sourceIndex,
                input,
                symbols,
                typeSymbols,
                memberSymbols,
                generatedGaps);
        }

        ImmutableArray<EcosystemFindIndexGap> gaps =
        [
            .. SnapshotGaps(request.Gaps, prefixes),
            .. generatedGaps,
        ];
        gaps = [.. gaps.Order(EcosystemFindIndexGapComparer.Instance)];
        EnsureUniqueGaps(gaps);

        ImmutableArray<EcosystemFindIndexSymbol> projectedSymbols =
        [
            .. symbols.Select(symbol => symbol.Freeze()),
        ];
        ImmutableArray<EcosystemFindIndexSourceCoordinate> sources =
        [
            .. inputs.Select(input => input.Coordinate),
        ];

        var artifact = new EcosystemFindIndexArtifact(
            SchemaVersion,
            AlgorithmVersion,
            request.Ecosystem,
            EcosystemFindIndexGenerationContract.ScannerIdentity,
            EcosystemFindIndexGenerationContract.ScannerVersion,
            IntegrationConceptCatalog.Revision.Number,
            EcosystemFindIndexGenerationContract.SymbolProjectionVersion,
            EcosystemFindIndexGenerationContract.PrefixEnumerationPolicyVersion,
            EcosystemFindIndexGenerationContract.PackageVersionSelectionPolicyVersion,
            EcosystemFindIndexGenerationContract.LibrarySelectionPolicyVersion,
            EcosystemFindIndexGenerationContract.TypeIdentityFormatVersion,
            EcosystemFindIndexGenerationContract.MemberAnchorFormatVersion,
            configuredConcepts,
            prefixes,
            sources,
            projectedSymbols,
            gaps);
        EcosystemFindIndexValidation.Validate(artifact);
        return artifact;
    }

    private static ImmutableArray<string> SnapshotConfiguredConcepts(
        IReadOnlyList<IntegrationConceptDescriptor> concepts)
    {
        if (concepts.Count == 0)
        {
            throw new ArgumentException(
                "An index requires at least one configured Integration concept.",
                nameof(concepts));
        }
        if (concepts.Any(concept => concept is null)
            || concepts.Any(concept => !IntegrationConceptCatalog.Concepts.Contains(
                concept,
                ReferenceEqualityComparer.Instance)))
        {
            throw new ArgumentException(
                "Configured concepts must be Integration catalog concepts.",
                nameof(concepts));
        }
        ImmutableArray<string> snapshot =
        [
            .. concepts.Select(concept => concept.Id.Value),
        ];
        if (snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Configured Integration concepts must be unique.",
                nameof(concepts));
        }
        EnsureConceptOrder(snapshot, nameof(concepts));
        return snapshot;
    }

    private static ImmutableArray<PackagePrefixDeclaration> SnapshotPrefixes(
        IReadOnlyList<PackagePrefixDeclaration> prefixes)
    {
        if (prefixes.Count == 0)
            throw new ArgumentException("An index requires at least one package prefix.", nameof(prefixes));

        ImmutableArray<PackagePrefixDeclaration> snapshot = [.. prefixes];
        if (snapshot.Any(prefix => prefix is null))
            throw new ArgumentException("Package prefixes cannot contain null.", nameof(prefixes));
        if (snapshot.Select(prefix => prefix.Prefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != snapshot.Length)
        {
            throw new ArgumentException("Package prefixes must be unique.", nameof(prefixes));
        }
        return snapshot;
    }

    private static ImmutableArray<EcosystemFindIndexSourceInput> SnapshotSources(
        IReadOnlyList<EcosystemFindIndexSourceInput> sources,
        ImmutableArray<PackagePrefixDeclaration> prefixes)
    {
        if (sources.Any(source => source is null))
            throw new ArgumentException("Indexed sources cannot contain null.", nameof(sources));

        ImmutableArray<EcosystemFindIndexSourceInput> snapshot =
        [
            .. sources.Order(EcosystemFindIndexSourceInputComparer.Instance),
        ];
        var ordinals = new HashSet<(int Prefix, int Package, int Library)>();
        var coordinates = new HashSet<EcosystemFindIndexSourceCoordinate>();
        foreach (EcosystemFindIndexSourceInput source in snapshot)
        {
            EcosystemFindIndexSourceCoordinate coordinate = source.Coordinate;
            if ((uint)coordinate.PrefixOrdinal >= (uint)prefixes.Length)
            {
                throw new ArgumentException(
                    "An indexed source names an unknown prefix ordinal.",
                    nameof(sources));
            }
            if (!prefixes[coordinate.PrefixOrdinal]
                .MatchesPackageId(coordinate.Package.PackageId))
            {
                throw new ArgumentException(
                    "An indexed package does not belong to its prefix source.",
                    nameof(sources));
            }
            if (!ordinals.Add((
                    coordinate.PrefixOrdinal,
                    coordinate.PackageOrdinal,
                    coordinate.LibraryOrdinal)))
            {
                throw new ArgumentException(
                    "Indexed source ordinals must be unique.",
                    nameof(sources));
            }
            if (!coordinates.Add(coordinate))
            {
                throw new ArgumentException(
                    "Exact indexed source coordinates must be unique.",
                    nameof(sources));
            }
            if (source.Signals.Any(signal => signal is null))
            {
                throw new ArgumentException(
                    "Integration signals cannot contain null.",
                    nameof(sources));
            }
        }
        return snapshot;
    }

    private static ImmutableArray<EcosystemFindIndexGap> SnapshotGaps(
        IReadOnlyList<EcosystemFindIndexGapInput> gaps,
        ImmutableArray<PackagePrefixDeclaration> prefixes)
    {
        if (gaps.Any(gap => gap is null))
            throw new ArgumentException("Coverage gaps cannot contain null.", nameof(gaps));

        var snapshot = ImmutableArray.CreateBuilder<EcosystemFindIndexGap>(gaps.Count);
        foreach (EcosystemFindIndexGapInput gap in gaps)
        {
            ValidateGap(gap, prefixes, nameof(gaps));
            snapshot.Add(new(
                gap.Kind,
                gap.PrefixOrdinal,
                gap.Package,
                gap.PackageOrdinal,
                gap.Library,
                gap.LibraryOrdinal,
                new InertString(TextPolicy.Field, gap.Detail),
                gap.SignalOrdinal));
        }
        return snapshot.ToImmutable();
    }

    private static void ValidateGap(
        EcosystemFindIndexGapInput gap,
        ImmutableArray<PackagePrefixDeclaration> prefixes,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gap.Detail);
        if ((uint)gap.PrefixOrdinal >= (uint)prefixes.Length)
        {
            throw new ArgumentException(
                "A coverage gap names an unknown prefix ordinal.",
                parameterName);
        }
        if (gap.Package is { } package)
        {
            PackageCoordinateResolution.Invalid? invalid =
                PackageCoordinateResolver.Validate(package);
            if (invalid is not null
                || string.IsNullOrWhiteSpace(package.Version))
            {
                throw new ArgumentException(
                    invalid?.Message ?? "A coverage package requires an exact version.",
                    parameterName);
            }
            if (!prefixes[gap.PrefixOrdinal].MatchesPackageId(package.PackageId))
            {
                throw new ArgumentException(
                    "A coverage package does not belong to its prefix source.",
                    parameterName);
            }
        }
        if (gap.Library is { } library)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(library.AssetPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(library.AssemblySimpleName);
        }

        bool validShape = gap.Kind switch
        {
            EcosystemFindIndexGapKind.PrefixEnumerationBounded
                =>
                gap.Package is null
                    && gap.PackageOrdinal is null
                    && gap.Library is null
                    && gap.LibraryOrdinal is null
                    && gap.SignalOrdinal is null,
            EcosystemFindIndexGapKind.PackageRejected =>
                gap.Package is not null
                    && gap.PackageOrdinal >= 0
                    && gap.Library is null
                    && gap.LibraryOrdinal is null
                    && gap.SignalOrdinal is null,
            EcosystemFindIndexGapKind.LibraryFailed
                or EcosystemFindIndexGapKind.ScannerUnavailable =>
                gap.Package is not null
                    && gap.PackageOrdinal >= 0
                    && gap.Library is not null
                    && gap.LibraryOrdinal >= 0
                    && gap.SignalOrdinal is null,
            EcosystemFindIndexGapKind.TypeIdentityUnresolved
                or EcosystemFindIndexGapKind.MemberEvidenceIncomplete =>
                gap.Package is not null
                    && gap.PackageOrdinal >= 0
                    && gap.Library is not null
                    && gap.LibraryOrdinal >= 0
                    && gap.SignalOrdinal >= 0,
            _ => false,
        };
        if (!validShape)
        {
            throw new ArgumentException(
                "A coverage gap has coordinates inconsistent with its kind.",
                parameterName);
        }
    }

    private static void ProjectSignals(
        int sourceIndex,
        EcosystemFindIndexSourceInput input,
        List<MutableSymbol> symbols,
        Dictionary<(int Source, MetadataTypeDefinitionName Type), MutableSymbol> typeSymbols,
        Dictionary<(int Source, MemberAnchor Member), MutableSymbol> memberSymbols,
        List<EcosystemFindIndexGap> gaps)
    {
        int nextSymbolOrdinal = 0;
        for (int signalOrdinal = 0;
             signalOrdinal < input.Signals.Count;
             signalOrdinal++)
        {
            EcosystemIntegrationSignalInfo signal = input.Signals[signalOrdinal];
            IntegrationConceptDescriptor concept =
                signal.GetConcept()
                ?? throw new ArgumentException(
                    $"Unknown Integration concept '{signal.Integration}'.",
                    nameof(input));
            var association = new EcosystemFindIndexConceptAssociation(
                concept.Id.Value,
                signal.Kind);
            var display = new InertString(TextPolicy.Field, signal.Name);

            if (signal.Shape == IntegrationSignalShape.Type)
            {
                MetadataTypeDefinitionName? type = signal.GetTypeDefinition();
                if (type is null)
                {
                    gaps.Add(UnresolvedGap(
                        EcosystemFindIndexGapKind.TypeIdentityUnresolved,
                        input.Coordinate,
                        signalOrdinal,
                        "Type identity is unavailable."));
                    continue;
                }
                if (!typeSymbols.TryGetValue((sourceIndex, type), out MutableSymbol? symbol))
                {
                    TypeDeclarationDiscoveryAttributes attributes =
                        signal.GetTypeDiscoveryAttributes()
                        ?? throw new ArgumentException(
                            "Type discovery attributes are unavailable.",
                            nameof(input));
                    symbol = MutableSymbol.CreateType(
                        sourceIndex,
                        nextSymbolOrdinal++,
                        type,
                        attributes,
                        display);
                    typeSymbols.Add((sourceIndex, type), symbol);
                    symbols.Add(symbol);
                }
                symbol.Add(association, display);
                continue;
            }

            if (signal.Shape != IntegrationSignalShape.Api)
            {
                throw new ArgumentException(
                    $"Unknown Integration signal shape '{signal.Shape}'.",
                    nameof(input));
            }

            ImmutableArray<EcosystemIntegrationApiEvidence> evidence =
                signal.GetApiEvidenceSet();
            bool incomplete = signal.IsApiEvidenceIncomplete();
            if (incomplete || evidence.IsEmpty)
            {
                gaps.Add(UnresolvedGap(
                    EcosystemFindIndexGapKind.MemberEvidenceIncomplete,
                    input.Coordinate,
                    signalOrdinal,
                    "One or more Member identities are unavailable."));
            }
            if (evidence.IsEmpty)
            {
                continue;
            }
            foreach (EcosystemIntegrationApiEvidence member in evidence)
            {
                ValidateMemberEvidence(member);
                if (!memberSymbols.TryGetValue(
                        (sourceIndex, member.Member),
                        out MutableSymbol? symbol))
                {
                    symbol = MutableSymbol.CreateMember(
                        sourceIndex,
                        nextSymbolOrdinal++,
                        member,
                        display);
                    memberSymbols.Add((sourceIndex, member.Member), symbol);
                    symbols.Add(symbol);
                }
                else if (symbol.Member != member)
                {
                    throw new ArgumentException(
                        "One MemberAnchor has inconsistent Integration evidence.",
                        nameof(input));
                }
                symbol.Add(association, display);
            }
        }
    }

    private static void ValidateMemberEvidence(
        EcosystemIntegrationApiEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        MemberAnchor member = evidence.Member;
        ArgumentException.ThrowIfNullOrWhiteSpace(member.StableSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(member.CanonicalSignature);
        ArgumentException.ThrowIfNullOrWhiteSpace(member.Fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(member.TypeFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(member.MemberName);
        if (!string.Equals(
                member.Fingerprint,
                MemberAnchor.ComputeFingerprint(member.CanonicalSignature),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A MemberAnchor fingerprint does not match its canonical signature.",
                nameof(evidence));
        }
        if (!string.Equals(
                member.TypeFullName,
                evidence.DeclaringType.ToMetadataFullName(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A MemberAnchor declaring Type does not match its structured evidence.",
                nameof(evidence));
        }
    }

    private static EcosystemFindIndexGap UnresolvedGap(
        EcosystemFindIndexGapKind kind,
        EcosystemFindIndexSourceCoordinate coordinate,
        int signalOrdinal,
        string detail) =>
        new(
            kind,
            coordinate.PrefixOrdinal,
            coordinate.Package,
            coordinate.PackageOrdinal,
            coordinate.Asset,
            coordinate.LibraryOrdinal,
            new InertString(TextPolicy.Field, detail),
            signalOrdinal);

    private static void EnsureUniqueGaps(
        ImmutableArray<EcosystemFindIndexGap> gaps)
    {
        var unique = new HashSet<EcosystemFindIndexGap>();
        foreach (EcosystemFindIndexGap gap in gaps)
        {
            if (!unique.Add(gap))
                throw new ArgumentException("Coverage gaps must be unique.");
        }
    }

    internal static void EnsureConceptOrder(
        ImmutableArray<string> concepts,
        string? parameterName = null)
    {
        int previousRank = -1;
        foreach (string concept in concepts)
        {
            int rank = -1;
            for (int index = 0;
                 index < IntegrationConceptCatalog.Concepts.Length;
                 index++)
            {
                if (IntegrationConceptCatalog.Concepts[index].Id.Value == concept)
                {
                    rank = index;
                    break;
                }
            }
            if (rank <= previousRank)
            {
                throw new ArgumentException(
                    "Integration concepts must follow catalog order.",
                    parameterName);
            }
            previousRank = rank;
        }
    }

    private sealed class MutableSymbol
    {
        readonly List<EcosystemFindIndexConceptAssociation> concepts = [];

        MutableSymbol(
            int sourceIndex,
            int symbolOrdinal,
            EcosystemFindIndexSymbolKind kind,
            MetadataTypeDefinitionName? type,
            TypeDeclarationDiscoveryAttributes? typeDiscoveryAttributes,
            EcosystemIntegrationApiEvidence? member,
            InertString display)
        {
            SourceIndex = sourceIndex;
            SymbolOrdinal = symbolOrdinal;
            Kind = kind;
            Type = type;
            TypeDiscoveryAttributes = typeDiscoveryAttributes;
            Member = member;
            Display = display;
        }

        public int SourceIndex { get; }
        public int SymbolOrdinal { get; }
        public EcosystemFindIndexSymbolKind Kind { get; }
        public MetadataTypeDefinitionName? Type { get; }
        public TypeDeclarationDiscoveryAttributes? TypeDiscoveryAttributes
            { get; }
        public EcosystemIntegrationApiEvidence? Member { get; }
        public InertString Display { get; }

        public static MutableSymbol CreateType(
            int sourceIndex,
            int symbolOrdinal,
            MetadataTypeDefinitionName type,
            TypeDeclarationDiscoveryAttributes typeDiscoveryAttributes,
            InertString display) =>
            new(
                sourceIndex,
                symbolOrdinal,
                EcosystemFindIndexSymbolKind.Type,
                type,
                typeDiscoveryAttributes,
                null,
                display);

        public static MutableSymbol CreateMember(
            int sourceIndex,
            int symbolOrdinal,
            EcosystemIntegrationApiEvidence member,
            InertString display) =>
            new(
                sourceIndex,
                symbolOrdinal,
                EcosystemFindIndexSymbolKind.Member,
                null,
                null,
                member,
                display);

        public void Add(
            EcosystemFindIndexConceptAssociation association,
            InertString display)
        {
            if (Display != display)
            {
                throw new ArgumentException(
                    "One exact symbol has inconsistent display text.");
            }
            if (!concepts.Contains(association))
                concepts.Add(association);
        }

        public EcosystemFindIndexSymbol Freeze() =>
            new(
                SourceIndex,
                SymbolOrdinal,
                Kind,
                Type,
                TypeDiscoveryAttributes,
                Member,
                Display,
                [
                    .. concepts.Order(EcosystemFindIndexConceptComparer.Instance),
                ]);
    }

    private sealed class EcosystemFindIndexSourceInputComparer
        : IComparer<EcosystemFindIndexSourceInput>
    {
        internal static EcosystemFindIndexSourceInputComparer Instance { get; } = new();

        public int Compare(
            EcosystemFindIndexSourceInput? x,
            EcosystemFindIndexSourceInput? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            return CompareCoordinates(x.Coordinate, y.Coordinate);
        }
    }

    internal static int CompareCoordinates(
        EcosystemFindIndexSourceCoordinate x,
        EcosystemFindIndexSourceCoordinate y)
    {
        int order = x.PrefixOrdinal.CompareTo(y.PrefixOrdinal);
        if (order == 0)
            order = x.PackageOrdinal.CompareTo(y.PackageOrdinal);
        if (order == 0)
            order = x.LibraryOrdinal.CompareTo(y.LibraryOrdinal);
        return order;
    }

    private sealed class EcosystemFindIndexConceptComparer
        : IComparer<EcosystemFindIndexConceptAssociation>
    {
        internal static EcosystemFindIndexConceptComparer Instance { get; } = new();
        static readonly Dictionary<string, int> Ranks =
            IntegrationConceptCatalog.Concepts
                .Select((concept, index) => (concept.Id.Value, index))
                .ToDictionary(pair => pair.Value, pair => pair.index, StringComparer.Ordinal);

        public int Compare(
            EcosystemFindIndexConceptAssociation? x,
            EcosystemFindIndexConceptAssociation? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            int order = Ranks.GetValueOrDefault(x.ConceptId, int.MaxValue)
                .CompareTo(Ranks.GetValueOrDefault(y.ConceptId, int.MaxValue));
            if (order == 0)
                order = string.CompareOrdinal(x.ConceptId, y.ConceptId);
            if (order == 0)
                order = string.CompareOrdinal(x.Kind, y.Kind);
            return order;
        }
    }

    private sealed class EcosystemFindIndexGapComparer
        : IComparer<EcosystemFindIndexGap>
    {
        internal static EcosystemFindIndexGapComparer Instance { get; } = new();

        public int Compare(EcosystemFindIndexGap? x, EcosystemFindIndexGap? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            int order = x.PrefixOrdinal.CompareTo(y.PrefixOrdinal);
            if (order == 0)
                order = Nullable.Compare(x.PackageOrdinal, y.PackageOrdinal);
            if (order == 0)
                order = Nullable.Compare(x.LibraryOrdinal, y.LibraryOrdinal);
            if (order == 0)
                order = Nullable.Compare(x.SignalOrdinal, y.SignalOrdinal);
            if (order == 0)
                order = string.CompareOrdinal(x.Package?.PackageId, y.Package?.PackageId);
            if (order == 0)
                order = string.CompareOrdinal(x.Package?.Version, y.Package?.Version);
            if (order == 0)
                order = string.CompareOrdinal(x.Library?.AssetPath, y.Library?.AssetPath);
            if (order == 0)
                order = x.Kind.CompareTo(y.Kind);
            if (order == 0)
                order = string.CompareOrdinal(x.Detail.ToString(), y.Detail.ToString());
            return order;
        }
    }
}

internal static class EcosystemFindIndexValidation
{
    internal static void Validate(EcosystemFindIndexArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.SchemaVersion != EcosystemFindIndexBuilder.SchemaVersion
            || artifact.BuilderAlgorithmVersion != EcosystemFindIndexBuilder.AlgorithmVersion)
        {
            throw new ArgumentException("The Ecosystem Find Index schema or builder version is unsupported.");
        }
        if (artifact.IntegrationCatalogRevision != IntegrationConceptCatalog.Revision.Number)
            throw new ArgumentException("The Integration catalog revision does not match this product.");
        if (artifact.ScannerIdentity != EcosystemFindIndexGenerationContract.ScannerIdentity
            || artifact.ScannerVersion != EcosystemFindIndexGenerationContract.ScannerVersion)
        {
            throw new ArgumentException(
                "The scanner contract identity or version is unsupported.");
        }
        if (artifact.SymbolProjectionVersion
                != EcosystemFindIndexGenerationContract.SymbolProjectionVersion
            || artifact.PrefixEnumerationPolicyVersion
                != EcosystemFindIndexGenerationContract.PrefixEnumerationPolicyVersion
            || artifact.PackageVersionSelectionPolicyVersion
                != EcosystemFindIndexGenerationContract.PackageVersionSelectionPolicyVersion
            || artifact.LibrarySelectionPolicyVersion
                != EcosystemFindIndexGenerationContract.LibrarySelectionPolicyVersion
            || artifact.TypeIdentityFormatVersion
                != EcosystemFindIndexGenerationContract.TypeIdentityFormatVersion
            || artifact.MemberAnchorFormatVersion
                != EcosystemFindIndexGenerationContract.MemberAnchorFormatVersion)
        {
            throw new ArgumentException(
                "One or more generation policy or identity-format versions are unsupported.");
        }
        if (artifact.ConfiguredConcepts.IsDefaultOrEmpty
            || artifact.ConfiguredConcepts.Distinct(StringComparer.Ordinal).Count()
                != artifact.ConfiguredConcepts.Length
            || artifact.ConfiguredConcepts.Any(id =>
                !IntegrationConceptCatalog.Concepts.Any(
                    concept => concept.Id.Value == id)))
        {
            throw new ArgumentException(
                "The configured Integration concept sequence is invalid.");
        }
        EcosystemFindIndexBuilder.EnsureConceptOrder(
            artifact.ConfiguredConcepts);
        if (artifact.Prefixes.IsDefaultOrEmpty)
            throw new ArgumentException("The artifact requires at least one prefix.");
        if (artifact.Prefixes.Any(prefix => prefix is null)
            || artifact.Prefixes.Select(prefix => prefix.Prefix)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != artifact.Prefixes.Length)
        {
            throw new ArgumentException("Artifact prefixes are invalid or duplicated.");
        }

        EcosystemFindIndexSourceCoordinate? previous = null;
        var sourceCoordinates = new HashSet<EcosystemFindIndexSourceCoordinate>();
        foreach (EcosystemFindIndexSourceCoordinate source in artifact.Sources)
        {
            if ((uint)source.PrefixOrdinal >= (uint)artifact.Prefixes.Length
                || !artifact.Prefixes[source.PrefixOrdinal]
                    .MatchesPackageId(source.Package.PackageId))
            {
                throw new ArgumentException("An artifact source does not belong to its prefix.");
            }
            if (previous is not null
                && EcosystemFindIndexBuilder.CompareCoordinates(previous, source) >= 0)
            {
                throw new ArgumentException("Artifact source ordinals are not strictly ordered.");
            }
            if (!sourceCoordinates.Add(source))
                throw new ArgumentException("Artifact source coordinates are duplicated.");
            previous = source;
        }
        var symbols = new HashSet<(int Source, EcosystemFindIndexSymbolKind Kind, object Identity)>();
        int previousSource = -1;
        int expectedSymbolOrdinal = 0;
        foreach (EcosystemFindIndexSymbol symbol in artifact.Symbols)
        {
            if ((uint)symbol.SourceIndex >= (uint)artifact.Sources.Length
                || symbol.SourceIndex < previousSource)
            {
                throw new ArgumentException("Artifact symbols name an invalid or disordered source.");
            }
            if (symbol.SourceIndex != previousSource)
                expectedSymbolOrdinal = 0;
            if (symbol.SymbolOrdinal != expectedSymbolOrdinal++)
            {
                throw new ArgumentException(
                    "Artifact symbol ordinals are not contiguous in source order.");
            }
            previousSource = symbol.SourceIndex;
            object identity = symbol.Kind switch
            {
                EcosystemFindIndexSymbolKind.Type
                    when symbol.Type is not null
                        && symbol.TypeDiscoveryAttributes is not null
                        && symbol.Member is null =>
                    symbol.Type,
                EcosystemFindIndexSymbolKind.Member
                    when symbol.Member is not null
                        && symbol.Type is null
                        && symbol.TypeDiscoveryAttributes is null =>
                    symbol.Member.Member,
                _ => throw new ArgumentException(
                    "An artifact symbol has an inconsistent identity shape."),
            };
            if (!symbols.Add((symbol.SourceIndex, symbol.Kind, identity)))
                throw new ArgumentException("Artifact symbols contain an exact duplicate.");
            if (symbol.Concepts.IsDefaultOrEmpty
                || symbol.Concepts.Distinct().Count() != symbol.Concepts.Length)
            {
                throw new ArgumentException("Artifact concept associations are empty or duplicated.");
            }
            foreach (EcosystemFindIndexConceptAssociation association in symbol.Concepts)
            {
                if (!IntegrationConceptCatalog.Concepts.Any(
                        concept => concept.Id.Value == association.ConceptId))
                {
                    throw new ArgumentException(
                        $"Unknown Integration concept '{association.ConceptId}'.");
                }
                if (!artifact.ConfiguredConcepts.Contains(
                        association.ConceptId,
                        StringComparer.Ordinal))
                {
                    throw new ArgumentException(
                        $"Integration concept '{association.ConceptId}' is not configured for this artifact.");
                }
                ArgumentException.ThrowIfNullOrWhiteSpace(association.Kind);
            }
            EnsureAssociationOrder(symbol.Concepts);
            if (symbol.Member is not null)
                ValidateMember(symbol.Member);
        }

        var gaps = new HashSet<EcosystemFindIndexGap>();
        EcosystemFindIndexGap? previousGap = null;
        foreach (EcosystemFindIndexGap gap in artifact.Gaps)
        {
            if ((uint)gap.PrefixOrdinal >= (uint)artifact.Prefixes.Length)
                throw new ArgumentException("An artifact gap names an unknown prefix.");
            ValidateGap(artifact, gap);
            if (!gaps.Add(gap))
                throw new ArgumentException("Artifact gaps contain an exact duplicate.");
            if (previousGap is not null && CompareGaps(previousGap, gap) >= 0)
                throw new ArgumentException("Artifact gaps are not strictly ordered.");
            previousGap = gap;
        }
        ValidateCoordinateClosure(artifact);
    }

    private static void ValidateCoordinateClosure(
        EcosystemFindIndexArtifact artifact)
    {
        var packages =
            new Dictionary<(int Prefix, int Package), PackageCoordinate>();
        var libraries =
            new Dictionary<(int Prefix, int Package, int Library), PackageLibraryIdentity>();
        var sourceLibraries =
            new HashSet<(int Prefix, int Package, int Library)>();
        var rejectedPackages =
            new HashSet<(int Prefix, int Package)>();
        var failedLibraries =
            new HashSet<(int Prefix, int Package, int Library)>();
        var identityGaps =
            new HashSet<(int Prefix, int Package, int Library)>();

        foreach (EcosystemFindIndexSourceCoordinate source in artifact.Sources)
        {
            AddPackage(
                (source.PrefixOrdinal, source.PackageOrdinal),
                source.Package);
            var libraryKey = (
                source.PrefixOrdinal,
                source.PackageOrdinal,
                source.LibraryOrdinal);
            AddLibrary(libraryKey, source.Asset);
            sourceLibraries.Add(libraryKey);
        }

        foreach (EcosystemFindIndexGap gap in artifact.Gaps)
        {
            if (gap.Package is { } package
                && gap.PackageOrdinal is int packageOrdinal)
            {
                AddPackage((gap.PrefixOrdinal, packageOrdinal), package);
            }
            if (gap.Library is { } library
                && gap.PackageOrdinal is int libraryPackageOrdinal
                && gap.LibraryOrdinal is int libraryOrdinal)
            {
                var libraryKey = (
                    gap.PrefixOrdinal,
                    libraryPackageOrdinal,
                    libraryOrdinal);
                AddLibrary(libraryKey, library);
                if (gap.Kind == EcosystemFindIndexGapKind.PackageRejected)
                    throw new ArgumentException("A rejected package cannot name a Library.");
                if (gap.Kind is EcosystemFindIndexGapKind.LibraryFailed
                    or EcosystemFindIndexGapKind.ScannerUnavailable)
                {
                    failedLibraries.Add(libraryKey);
                }
                else if (gap.Kind is EcosystemFindIndexGapKind.TypeIdentityUnresolved
                    or EcosystemFindIndexGapKind.MemberEvidenceIncomplete)
                {
                    identityGaps.Add(libraryKey);
                }
            }
            if (gap.Kind == EcosystemFindIndexGapKind.PackageRejected
                && gap.PackageOrdinal is int rejectedOrdinal)
            {
                rejectedPackages.Add((gap.PrefixOrdinal, rejectedOrdinal));
            }
        }

        if (rejectedPackages.Any(key =>
                sourceLibraries.Any(library =>
                    library.Prefix == key.Prefix
                    && library.Package == key.Package)))
        {
            throw new ArgumentException(
                "A rejected package ordinal also has an indexed Library.");
        }
        if (failedLibraries.Overlaps(sourceLibraries))
        {
            throw new ArgumentException(
                "A failed Library ordinal also has an indexed source.");
        }
        if (identityGaps.Any(key => !sourceLibraries.Contains(key)))
        {
            throw new ArgumentException(
                "A symbol-identity gap does not belong to an indexed source.");
        }

        for (int prefixOrdinal = 0;
             prefixOrdinal < artifact.Prefixes.Length;
             prefixOrdinal++)
        {
            int[] packageOrdinals =
            [
                .. packages.Keys
                    .Where(key => key.Prefix == prefixOrdinal)
                    .Select(key => key.Package)
                    .Order(),
            ];
            EnsureContiguous(packageOrdinals, "package");
            foreach (int packageOrdinal in packageOrdinals)
            {
                int[] libraryOrdinals =
                [
                    .. libraries.Keys
                        .Where(key => key.Prefix == prefixOrdinal
                            && key.Package == packageOrdinal)
                        .Select(key => key.Library)
                        .Order(),
                ];
                EnsureContiguous(libraryOrdinals, "Library");
            }
        }

        void AddPackage(
            (int Prefix, int Package) key,
            PackageCoordinate package)
        {
            if (packages.TryGetValue(key, out PackageCoordinate? existing)
                && existing != package)
            {
                throw new ArgumentException(
                    "One package ordinal names inconsistent exact coordinates.");
            }
            packages[key] = package;
        }

        void AddLibrary(
            (int Prefix, int Package, int Library) key,
            PackageLibraryIdentity library)
        {
            if (libraries.TryGetValue(key, out PackageLibraryIdentity? existing)
                && existing != library)
            {
                throw new ArgumentException(
                    "One Library ordinal names inconsistent exact coordinates.");
            }
            libraries[key] = library;
        }

        static void EnsureContiguous(
            IReadOnlyList<int> ordinals,
            string coordinate)
        {
            for (int index = 0; index < ordinals.Count; index++)
            {
                if (ordinals[index] != index)
                {
                    throw new ArgumentException(
                        $"Artifact {coordinate} ordinals are not contiguous.");
                }
            }
        }
    }

    private static void ValidateGap(
        EcosystemFindIndexArtifact artifact,
        EcosystemFindIndexGap gap)
    {
        if (gap.Package is { } package)
        {
            if (PackageCoordinateResolver.Validate(package) is not null
                || string.IsNullOrWhiteSpace(package.Version)
                || package.Framework is not null
                || package.RuntimeIdentifier is not null
                || !artifact.Prefixes[gap.PrefixOrdinal]
                    .MatchesPackageId(package.PackageId))
            {
                throw new ArgumentException(
                    "An artifact gap contains an invalid package coordinate.");
            }
        }
        if (gap.Library is { } library
            && (string.IsNullOrWhiteSpace(library.AssetPath)
                || string.IsNullOrWhiteSpace(library.AssemblySimpleName)))
        {
            throw new ArgumentException(
                "An artifact gap contains an invalid Library coordinate.");
        }
        bool validShape = gap.Kind switch
        {
            EcosystemFindIndexGapKind.PrefixEnumerationBounded
                =>
                gap.Package is null
                    && gap.PackageOrdinal is null
                    && gap.Library is null
                    && gap.LibraryOrdinal is null
                    && gap.SignalOrdinal is null,
            EcosystemFindIndexGapKind.PackageRejected =>
                gap.Package is not null
                    && gap.PackageOrdinal >= 0
                    && gap.Library is null
                    && gap.LibraryOrdinal is null
                    && gap.SignalOrdinal is null,
            EcosystemFindIndexGapKind.LibraryFailed
                or EcosystemFindIndexGapKind.ScannerUnavailable =>
                gap.Package is not null
                    && gap.PackageOrdinal >= 0
                    && gap.Library is not null
                    && gap.LibraryOrdinal >= 0
                    && gap.SignalOrdinal is null,
            EcosystemFindIndexGapKind.TypeIdentityUnresolved
                or EcosystemFindIndexGapKind.MemberEvidenceIncomplete =>
                gap.Package is not null
                    && gap.PackageOrdinal >= 0
                    && gap.Library is not null
                    && gap.LibraryOrdinal >= 0
                    && gap.SignalOrdinal >= 0,
            _ => false,
        };
        if (!validShape)
        {
            throw new ArgumentException(
                "An artifact gap has coordinates inconsistent with its kind.");
        }
    }

    private static int CompareGaps(
        EcosystemFindIndexGap x,
        EcosystemFindIndexGap y)
    {
        int order = x.PrefixOrdinal.CompareTo(y.PrefixOrdinal);
        if (order == 0)
            order = Nullable.Compare(x.PackageOrdinal, y.PackageOrdinal);
        if (order == 0)
            order = Nullable.Compare(x.LibraryOrdinal, y.LibraryOrdinal);
        if (order == 0)
            order = Nullable.Compare(x.SignalOrdinal, y.SignalOrdinal);
        if (order == 0)
            order = string.CompareOrdinal(x.Package?.PackageId, y.Package?.PackageId);
        if (order == 0)
            order = string.CompareOrdinal(x.Package?.Version, y.Package?.Version);
        if (order == 0)
            order = string.CompareOrdinal(x.Library?.AssetPath, y.Library?.AssetPath);
        if (order == 0)
            order = x.Kind.CompareTo(y.Kind);
        if (order == 0)
            order = string.CompareOrdinal(x.Detail.ToString(), y.Detail.ToString());
        return order;
    }

    private static void ValidateMember(EcosystemIntegrationApiEvidence evidence)
    {
        MemberAnchor member = evidence.Member;
        if (string.IsNullOrWhiteSpace(member.StableSelector)
            || string.IsNullOrWhiteSpace(member.CanonicalSignature)
            || string.IsNullOrWhiteSpace(member.Fingerprint)
            || string.IsNullOrWhiteSpace(member.TypeFullName)
            || string.IsNullOrWhiteSpace(member.MemberName)
            || !string.Equals(
                member.Fingerprint,
                MemberAnchor.ComputeFingerprint(member.CanonicalSignature),
                StringComparison.Ordinal)
            || !string.Equals(
                member.TypeFullName,
                evidence.DeclaringType.ToMetadataFullName(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("An artifact contains an invalid MemberAnchor.");
        }
    }

    private static void EnsureAssociationOrder(
        ImmutableArray<EcosystemFindIndexConceptAssociation> associations)
    {
        int previousRank = -1;
        string? previousKind = null;
        foreach (EcosystemFindIndexConceptAssociation association in associations)
        {
            int rank = -1;
            for (int index = 0;
                 index < IntegrationConceptCatalog.Concepts.Length;
                 index++)
            {
                if (IntegrationConceptCatalog.Concepts[index].Id.Value
                    == association.ConceptId)
                {
                    rank = index;
                    break;
                }
            }
            if (rank < previousRank
                || rank == previousRank
                    && string.CompareOrdinal(previousKind, association.Kind) >= 0)
            {
                throw new ArgumentException(
                    "Artifact concept associations are not in owner order.");
            }
            previousRank = rank;
            previousKind = association.Kind;
        }
    }
}
