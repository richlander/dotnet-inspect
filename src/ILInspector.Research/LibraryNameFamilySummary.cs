using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.Research;

public enum LibraryNameFamilyKind
{
    OneWordSuffix,
    TwoWordSuffix,
}

public enum LibraryNameFamilyPopulationKind
{
    AllTypes,
    OrdinaryEvidenceOnly,
    GeneratedEvidenceOnly,
    MixedEvidence,
    Unknown,
}

public enum LibraryNameFamilyOneWordResidualReason
{
    EmptyStem,
    SeparatorTerminus,
    UnresolvedTerminus,
}

public enum LibraryNameFamilyTwoWordResidualReason
{
    EmptyStem,
    FewerThanTwoSuffixWords,
    SeparatorTerminus,
    UnresolvedTerminus,
    PrecedingUnresolvedRun,
}

public enum LibraryNameFamilyProvenanceState
{
    NotSupplied,
    Available,
    Unavailable,
    Incomplete,
    Failed,
    Rejected,
}

public enum LibraryNameFamilyProvenanceRejection
{
    ArtifactMismatch,
    AssemblyMismatch,
    ModuleMismatch,
    TypeInventoryMismatch,
}

public enum LibraryNameFamilyUnavailableReason
{
    MissingArtifactBinding,
    IncompleteTypeInventory,
    RejectedTypeInventory,
}

public enum LibraryNameFamilyRejectionReason
{
    ArtifactIdentityMismatch,
    AssemblyIdentityMismatch,
    ModuleIdentityMismatch,
    IdentifierWordGrammarMismatch,
}

public sealed record LibraryNameFamilyLimits
{
    public LibraryNameFamilyLimits(
        int maximumRetainedTypes = 1_000_000,
        int maximumRetainedNameCharacters = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumRetainedTypes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumRetainedNameCharacters);
        MaximumRetainedTypes = maximumRetainedTypes;
        MaximumRetainedNameCharacters = maximumRetainedNameCharacters;
    }

    public int MaximumRetainedTypes { get; }
    public int MaximumRetainedNameCharacters { get; }
}

public sealed record LibraryNameFamilyBinding(
    AssemblyArtifactIdentity Artifact,
    AssemblyReferenceIdentity Assembly,
    Guid ModuleVersionId);

public sealed record LibraryNameFamilyProvenanceQualification(
    LibraryNameFamilyProvenanceState State,
    PdbSourceProvenanceBinding? Binding = null,
    PdbSourceProvenanceUnavailableReason? UnavailableReason = null,
    PdbSourceProvenanceIncompleteReason? IncompleteReason = null,
    LibraryNameFamilyProvenanceRejection? Rejection = null,
    string? Detail = null);

public sealed record LibraryNameFamilyMethodology
{
    public LibraryNameFamilyMethodology(
        string version,
        IdentifierWordOracleReceipt wordOracle)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentNullException.ThrowIfNull(wordOracle);
        Version = version;
        WordOracle = wordOracle;
    }

    public string Version { get; }
    public IdentifierWordOracleReceipt WordOracle { get; }
}

public sealed class LibraryNameFamilyIdentity :
    IEquatable<LibraryNameFamilyIdentity>
{
    public LibraryNameFamilyIdentity(
        LibraryNameFamilyMethodology methodology,
        LibraryNameFamilyKind kind,
        ImmutableArray<string> words,
        string? separator)
    {
        ArgumentNullException.ThrowIfNull(methodology);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (words.IsDefault
            || words.Length
                != (kind == LibraryNameFamilyKind.OneWordSuffix ? 1 : 2)
            || words.Any(static word => string.IsNullOrEmpty(word)))
        {
            throw new ArgumentException(
                "A family requires the exact suffix words for its kind.",
                nameof(words));
        }
        if (kind == LibraryNameFamilyKind.OneWordSuffix && separator is not null
            || kind == LibraryNameFamilyKind.TwoWordSuffix
                && separator is null)
        {
            throw new ArgumentException(
                "Only a two-word family carries an exact separator.",
                nameof(separator));
        }

        Methodology = methodology;
        Kind = kind;
        Words = [.. words];
        Separator = separator;
    }

    public LibraryNameFamilyMethodology Methodology { get; }
    public LibraryNameFamilyKind Kind { get; }
    public ImmutableArray<string> Words { get; }
    public string? Separator { get; }

    public bool Equals(LibraryNameFamilyIdentity? other) =>
        other is not null
        && Methodology == other.Methodology
        && Kind == other.Kind
        && Words.SequenceEqual(other.Words, StringComparer.Ordinal)
        && Separator == other.Separator;

    public override bool Equals(object? obj) =>
        obj is LibraryNameFamilyIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Methodology);
        hash.Add(Kind);
        foreach (string word in Words)
            hash.Add(word, StringComparer.Ordinal);
        hash.Add(Separator, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public sealed record LibraryNameFamilyTypeRow(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    string MetadataSimpleName,
    string NameStem,
    ImmutableArray<IdentifierWordSpan> Spans,
    LibraryNameFamilyIdentity? OneWordSuffix,
    LibraryNameFamilyIdentity? TwoWordSuffix,
    LibraryNameFamilyOneWordResidualReason? OneWordResidual,
    LibraryNameFamilyTwoWordResidualReason? TwoWordResidual,
    bool IsDefinitionPublic,
    bool IsPublicSurface,
    AssemblyTypeDefinitionKind DefinitionKind,
    PdbTypeSourceEvidence? SourceEvidence);

public sealed record LibraryNameFamilyDefinitionKindCount(
    AssemblyTypeDefinitionKind Kind,
    int Count);

public sealed record LibraryNameFamilySourceDispositionCount(
    PdbTypeSourceDisposition Disposition,
    int Count);

public sealed record LibraryNameFamilyRow(
    LibraryNameFamilyIdentity Identity,
    int TypeCount,
    int PublicTypeCount,
    int DistinctNamespaceCount,
    ImmutableArray<LibraryNameFamilyDefinitionKindCount> DefinitionKinds,
    ImmutableArray<LibraryNameFamilySourceDispositionCount> SourceDispositions,
    ImmutableArray<MetadataTypeDefinitionAddress> Types);

public static class LibraryNameFamilyOrder
{
    public static IComparer<LibraryNameFamilyRow> Prevalence { get; } =
        Comparer<LibraryNameFamilyRow>.Create(ComparePrevalence);

    public static int CompareIdentity(
        LibraryNameFamilyIdentity left,
        LibraryNameFamilyIdentity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int comparison = left.Kind.CompareTo(right.Kind);
        if (comparison != 0)
            return comparison;

        comparison = string.CompareOrdinal(
            left.Words[0],
            right.Words[0]);
        if (comparison != 0)
            return comparison;

        if (left.Kind == LibraryNameFamilyKind.OneWordSuffix)
            return 0;

        comparison = string.CompareOrdinal(
            left.Words[1],
            right.Words[1]);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(
                left.Separator,
                right.Separator);
    }

    private static int ComparePrevalence(
        LibraryNameFamilyRow? left,
        LibraryNameFamilyRow? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        int comparison = right.TypeCount.CompareTo(left.TypeCount);
        if (comparison != 0)
            return comparison;

        comparison = right.DistinctNamespaceCount.CompareTo(
            left.DistinctNamespaceCount);
        return comparison != 0
            ? comparison
            : CompareIdentity(left.Identity, right.Identity);
    }
}

public sealed record LibraryNameFamilyResidualCount<TReason>(
    TReason Reason,
    int Count)
    where TReason : struct, Enum;

public sealed record LibraryNameFamilyOneWordPartitionReceipt(
    int TotalTypeCount,
    int EligibleTypeCount,
    int ResidualTypeCount,
    ImmutableArray<
        LibraryNameFamilyResidualCount<
            LibraryNameFamilyOneWordResidualReason>> Residuals);

public sealed record LibraryNameFamilyTwoWordPartitionReceipt(
    int TotalTypeCount,
    int EligibleTypeCount,
    int ResidualTypeCount,
    ImmutableArray<
        LibraryNameFamilyResidualCount<
            LibraryNameFamilyTwoWordResidualReason>> Residuals);

public sealed record LibraryNameFamilyPopulation(
    LibraryNameFamilyPopulationKind Kind,
    int TypeCount,
    ImmutableArray<LibraryNameFamilyRow> Families,
    LibraryNameFamilyOneWordPartitionReceipt OneWord,
    LibraryNameFamilyTwoWordPartitionReceipt TwoWord);

public sealed record LibraryNameFamilyReceipt(
    LibraryNameFamilyLimits Limits,
    int TypeCount,
    int TypeNameCharacterCount,
    int TypeWithUnresolvedSpanCount,
    IdentifierWordOracleReceipt Oracle,
    IdentifierPopulationReceipt NumberedFamilyPopulation);

public sealed record LibraryNameFamilyDocument(
    LibraryNameFamilyBinding Binding,
    LibraryNameFamilyMethodology Methodology,
    LibraryNameFamilyReceipt Receipt,
    LibraryNameFamilyProvenanceQualification Provenance,
    ImmutableArray<LibraryNameFamilyTypeRow> Types,
    ImmutableArray<LibraryNameFamilyPopulation> Populations);

public abstract record LibraryNameFamilySummaryOutcome
{
    private LibraryNameFamilySummaryOutcome()
    {
    }

    public sealed record Available(LibraryNameFamilyDocument Document)
        : LibraryNameFamilySummaryOutcome;

    public sealed record Unavailable(
        LibraryNameFamilyUnavailableReason Reason,
        string Detail)
        : LibraryNameFamilySummaryOutcome;

    public sealed record Rejected(
        LibraryNameFamilyRejectionReason Reason,
        string Detail)
        : LibraryNameFamilySummaryOutcome;
}

public static class LibraryNameFamilySummary
{
    public const string MethodologyVersion = "library-name-families.v1";

    public static LibraryNameFamilySummaryOutcome Execute(
        ResolvedAssemblyReference assembly,
        AssemblyInspectionSession session,
        IdentifierWordOracle? oracle = null,
        PdbSourceProvenanceOutcome? provenance = null,
        LibraryNameFamilyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(session);

        if (assembly.Registration.ModuleVersionId is not Guid registeredMvid)
        {
            return new LibraryNameFamilySummaryOutcome.Unavailable(
                LibraryNameFamilyUnavailableReason.MissingArtifactBinding,
                "Library name families require an artifact-backed assembly.");
        }
        if (session.ArtifactIdentity is not { } artifact
            || !session.IsSameArtifact(assembly))
        {
            return new LibraryNameFamilySummaryOutcome.Rejected(
                LibraryNameFamilyRejectionReason.ArtifactIdentityMismatch,
                "The open image and assembly descriptor do not share exact "
                    + "artifact identity.");
        }

        limits ??= new();
        oracle ??= IdentifierWordProductOracle.Instance;
        if (!string.Equals(
                oracle.Receipt.GrammarVersion,
                IdentifierWordBreaker.GrammarVersion,
                StringComparison.Ordinal))
        {
            return new LibraryNameFamilySummaryOutcome.Rejected(
                LibraryNameFamilyRejectionReason
                    .IdentifierWordGrammarMismatch,
                "The identifier-word oracle uses a different grammar.");
        }
        var methodology = new LibraryNameFamilyMethodology(
            MethodologyVersion,
            oracle.Receipt);

        Guid moduleVersionId = session.ModuleVersionId();
        if (registeredMvid != moduleVersionId)
        {
            return new LibraryNameFamilySummaryOutcome.Rejected(
                LibraryNameFamilyRejectionReason.ModuleIdentityMismatch,
                "The assembly descriptor and open image do not share a module generation.");
        }

        AssemblyTypeDeclarationInventoryOutcome inventoryOutcome =
            session.TypeDeclarations(
                limits.MaximumRetainedTypes,
                limits.MaximumRetainedNameCharacters);
        if (inventoryOutcome is
            AssemblyTypeDeclarationInventoryOutcome.Incomplete incomplete)
        {
            return new LibraryNameFamilySummaryOutcome.Unavailable(
                LibraryNameFamilyUnavailableReason.IncompleteTypeInventory,
                $"The complete Type inventory exceeded {incomplete.Bound}.");
        }
        if (inventoryOutcome is
            AssemblyTypeDeclarationInventoryOutcome.Rejected rejected)
        {
            return new LibraryNameFamilySummaryOutcome.Unavailable(
                LibraryNameFamilyUnavailableReason.RejectedTypeInventory,
                rejected.Failure.ToString());
        }
        var inventory =
            ((AssemblyTypeDeclarationInventoryOutcome.Read)inventoryOutcome)
                .Inventory;
        if (inventory.Identity != assembly.Identity)
        {
            return new LibraryNameFamilySummaryOutcome.Rejected(
                LibraryNameFamilyRejectionReason.AssemblyIdentityMismatch,
                "The assembly descriptor and Type inventory do not share identity.");
        }

        AssemblyTypeDeclaration[] definitions =
        [
            .. inventory.Declarations
                .Where(static declaration =>
                    declaration.Kind
                        == AssemblyTypeDeclarationKind.Definition)
                .OrderBy(static declaration =>
                    declaration.DefinitionToken!.Value.Value),
        ];
        string[] stems =
        [
            .. definitions.Select(static declaration =>
                MetadataNameArity.StripFromSegment(
                    declaration.Name.Segments[^1])),
        ];
        IdentifierNumberedFamilyContextConstruction contextConstruction =
            IdentifierNumberedFamilyContext.Build(
                stems,
                oracle.Receipt.GrammarVersion);
        if (contextConstruction is not
            IdentifierNumberedFamilyContextConstruction.Created created)
        {
            return new LibraryNameFamilySummaryOutcome.Rejected(
                LibraryNameFamilyRejectionReason
                    .IdentifierWordGrammarMismatch,
                "The numbered-family context rejected the bound Type population.");
        }

        (
            LibraryNameFamilyProvenanceQualification qualification,
            IReadOnlyDictionary<
                MetadataTypeDefinitionAddress,
                PdbTypeSourceEvidence>? sourceByType) =
            BindProvenance(
                provenance,
                artifact,
                assembly.Identity,
                moduleVersionId,
                definitions);

        var rows = ImmutableArray.CreateBuilder<LibraryNameFamilyTypeRow>(
            definitions.Length);
        for (int index = 0; index < definitions.Length; index++)
        {
            AssemblyTypeDeclaration declaration = definitions[index];
            string metadataSimpleName = declaration.Name.Segments[^1];
            string stem = stems[index];
            IdentifierWordBreakOutcome breakOutcome =
                IdentifierWordBreaker.Break(
                    stem,
                    oracle,
                    created.Context);
            if (breakOutcome is not IdentifierWordBreakOutcome.Succeeded
                succeeded)
            {
                return new LibraryNameFamilySummaryOutcome.Rejected(
                    LibraryNameFamilyRejectionReason
                        .IdentifierWordGrammarMismatch,
                    "Identifier word breaking rejected the bound methodology.");
            }

            MetadataTypeDefinitionAddress address =
                MetadataTypeDefinitionAddress.FromToken(
                    moduleVersionId,
                    declaration.DefinitionToken!.Value.Value);
            FamilyAssignment assignment = AssignFamilies(
                succeeded.Result.Spans,
                methodology);
            PdbTypeSourceEvidence? source = null;
            sourceByType?.TryGetValue(address, out source);
            rows.Add(
                new(
                    address,
                    declaration.Name,
                    metadataSimpleName,
                    stem,
                    succeeded.Result.Spans,
                    assignment.OneWord,
                    assignment.TwoWord,
                    assignment.OneWordResidual,
                    assignment.TwoWordResidual,
                    declaration.IsDefinitionPublic == true,
                    declaration.IsPublicSurface,
                    declaration.DefinitionKind
                        ?? throw new InvalidOperationException(
                            "A Type definition requires its Metadata-owned kind."),
                    source));
        }

        ImmutableArray<LibraryNameFamilyTypeRow> typeRows = rows.MoveToImmutable();
        var populations =
            ImmutableArray.CreateBuilder<LibraryNameFamilyPopulation>(5);
        populations.Add(
            BuildPopulation(
                LibraryNameFamilyPopulationKind.AllTypes,
                typeRows));
        if (qualification.State == LibraryNameFamilyProvenanceState.Available)
        {
            AddSourcePopulation(
                populations,
                typeRows,
                LibraryNameFamilyPopulationKind.OrdinaryEvidenceOnly,
                PdbTypeSourceDisposition.OrdinaryEvidenceOnly);
            AddSourcePopulation(
                populations,
                typeRows,
                LibraryNameFamilyPopulationKind.GeneratedEvidenceOnly,
                PdbTypeSourceDisposition.GeneratedEvidenceOnly);
            AddSourcePopulation(
                populations,
                typeRows,
                LibraryNameFamilyPopulationKind.MixedEvidence,
                PdbTypeSourceDisposition.MixedEvidence);
            AddSourcePopulation(
                populations,
                typeRows,
                LibraryNameFamilyPopulationKind.Unknown,
                PdbTypeSourceDisposition.Unknown);
        }

        return new LibraryNameFamilySummaryOutcome.Available(
            new(
                new(
                    artifact,
                    assembly.Identity,
                    moduleVersionId),
                methodology,
                new(
                    limits,
                    typeRows.Length,
                    typeRows.Sum(static row =>
                        row.MetadataSimpleName.Length),
                    typeRows.Count(static row =>
                        row.Spans.Any(static span =>
                            span.Classification
                                == IdentifierWordSpanClassification
                                    .Unresolved)),
                    oracle.Receipt,
                    created.Context.Receipt),
                qualification,
                typeRows,
                populations.ToImmutable()));
    }

    private static (
        LibraryNameFamilyProvenanceQualification Qualification,
        IReadOnlyDictionary<MetadataTypeDefinitionAddress, PdbTypeSourceEvidence>?
            SourceByType) BindProvenance(
        PdbSourceProvenanceOutcome? provenance,
        AssemblyArtifactIdentity artifact,
        AssemblyReferenceIdentity assembly,
        Guid moduleVersionId,
        IReadOnlyList<AssemblyTypeDeclaration> definitions)
    {
        switch (provenance)
        {
            case null:
                return (
                    new(LibraryNameFamilyProvenanceState.NotSupplied),
                    null);
            case PdbSourceProvenanceOutcome.Unavailable unavailable:
                return (
                    new(
                        LibraryNameFamilyProvenanceState.Unavailable,
                        UnavailableReason: unavailable.Reason,
                        Detail: unavailable.Detail),
                    null);
            case PdbSourceProvenanceOutcome.Incomplete incomplete:
                return (
                    new(
                        LibraryNameFamilyProvenanceState.Incomplete,
                        IncompleteReason: incomplete.Reason,
                        Detail: incomplete.Detail),
                    null);
            case PdbSourceProvenanceOutcome.Failed failed:
                return (
                    new(
                        LibraryNameFamilyProvenanceState.Failed,
                        Detail: failed.Detail),
                    null);
            case PdbSourceProvenanceOutcome.Available available:
                PdbSourceProvenanceResult result = available.Result;
                if (!result.Binding.IsSameArtifact(artifact))
                {
                    return Rejected(
                        LibraryNameFamilyProvenanceRejection
                            .ArtifactMismatch);
                }
                if (result.Binding.Assembly != assembly)
                {
                    return Rejected(
                        LibraryNameFamilyProvenanceRejection
                            .AssemblyMismatch);
                }
                if (result.Binding.ModuleVersionId != moduleVersionId)
                {
                    return Rejected(
                        LibraryNameFamilyProvenanceRejection
                            .ModuleMismatch);
                }

                Dictionary<
                    MetadataTypeDefinitionAddress,
                    PdbTypeSourceEvidence> sourceByType =
                    result.Types.ToDictionary(static type => type.Type);
                if (sourceByType.Count != definitions.Count
                    || definitions.Any(declaration =>
                        !sourceByType.ContainsKey(
                            MetadataTypeDefinitionAddress.FromToken(
                                moduleVersionId,
                                declaration.DefinitionToken!.Value.Value))))
                {
                    return Rejected(
                        LibraryNameFamilyProvenanceRejection
                            .TypeInventoryMismatch);
                }
                return (
                    new(
                        LibraryNameFamilyProvenanceState.Available,
                        result.Binding),
                    sourceByType);
            default:
                throw new InvalidOperationException(
                    "Unknown source-provenance outcome.");
        }

        static (
            LibraryNameFamilyProvenanceQualification,
            IReadOnlyDictionary<
                MetadataTypeDefinitionAddress,
                PdbTypeSourceEvidence>?) Rejected(
            LibraryNameFamilyProvenanceRejection rejection) =>
            (
                new(
                    LibraryNameFamilyProvenanceState.Rejected,
                    Rejection: rejection,
                    Detail:
                        "The optional source provenance does not bind to the "
                        + "complete Type inventory."),
                null);
    }

    private static FamilyAssignment AssignFamilies(
        ImmutableArray<IdentifierWordSpan> spans,
        LibraryNameFamilyMethodology methodology)
    {
        if (spans.IsEmpty)
        {
            return new(
                null,
                null,
                LibraryNameFamilyOneWordResidualReason.EmptyStem,
                LibraryNameFamilyTwoWordResidualReason.EmptyStem);
        }

        int terminusIndex = spans.Length - 1;
        if (spans[terminusIndex].Classification
                == IdentifierWordSpanClassification.Ordinal)
        {
            terminusIndex--;
        }
        if (terminusIndex < 0)
        {
            return new(
                null,
                null,
                LibraryNameFamilyOneWordResidualReason.UnresolvedTerminus,
                LibraryNameFamilyTwoWordResidualReason
                    .FewerThanTwoSuffixWords);
        }

        IdentifierWordSpan terminus = spans[terminusIndex];
        if (terminus.Classification
                == IdentifierWordSpanClassification.Separator)
        {
            return new(
                null,
                null,
                LibraryNameFamilyOneWordResidualReason.SeparatorTerminus,
                LibraryNameFamilyTwoWordResidualReason.SeparatorTerminus);
        }
        if (terminus.Classification
                != IdentifierWordSpanClassification.Word)
        {
            return new(
                null,
                null,
                LibraryNameFamilyOneWordResidualReason.UnresolvedTerminus,
                LibraryNameFamilyTwoWordResidualReason.UnresolvedTerminus);
        }

        var oneWord = new LibraryNameFamilyIdentity(
            methodology,
            LibraryNameFamilyKind.OneWordSuffix,
            [terminus.Text],
            separator: null);
        int precedingIndex = terminusIndex - 1;
        while (precedingIndex >= 0
            && spans[precedingIndex].Classification
                == IdentifierWordSpanClassification.Separator)
        {
            precedingIndex--;
        }
        if (precedingIndex < 0)
        {
            return new(
                oneWord,
                null,
                null,
                LibraryNameFamilyTwoWordResidualReason
                    .FewerThanTwoSuffixWords);
        }

        IdentifierWordSpan preceding = spans[precedingIndex];
        if (preceding.Classification
                == IdentifierWordSpanClassification.Unresolved)
        {
            return new(
                oneWord,
                null,
                null,
                LibraryNameFamilyTwoWordResidualReason
                    .PrecedingUnresolvedRun);
        }
        if (preceding.Classification
                != IdentifierWordSpanClassification.Word)
        {
            return new(
                oneWord,
                null,
                null,
                LibraryNameFamilyTwoWordResidualReason
                    .FewerThanTwoSuffixWords);
        }

        string separator = string.Concat(
            spans[(precedingIndex + 1)..terminusIndex]
                .Select(static span => span.Text));
        var twoWord = new LibraryNameFamilyIdentity(
            methodology,
            LibraryNameFamilyKind.TwoWordSuffix,
            [preceding.Text, terminus.Text],
            separator);
        return new(oneWord, twoWord, null, null);
    }

    private static void AddSourcePopulation(
        ImmutableArray<LibraryNameFamilyPopulation>.Builder populations,
        ImmutableArray<LibraryNameFamilyTypeRow> types,
        LibraryNameFamilyPopulationKind population,
        PdbTypeSourceDisposition disposition) =>
        populations.Add(
            BuildPopulation(
                population,
                [
                    .. types.Where(type =>
                        type.SourceEvidence?.Disposition == disposition),
                ]));

    private static LibraryNameFamilyPopulation BuildPopulation(
        LibraryNameFamilyPopulationKind kind,
        ImmutableArray<LibraryNameFamilyTypeRow> types)
    {
        LibraryNameFamilyRow[] oneWord =
        [
            .. BuildFamilies(
                types,
                LibraryNameFamilyKind.OneWordSuffix),
        ];
        LibraryNameFamilyRow[] twoWord =
        [
            .. BuildFamilies(
                types,
                LibraryNameFamilyKind.TwoWordSuffix),
        ];
        LibraryNameFamilyRow[] families =
        [
            .. oneWord.Concat(twoWord)
                .Order(LibraryNameFamilyOrder.Prevalence),
        ];
        return new(
            kind,
            types.Length,
            [.. families],
            new(
                types.Length,
                oneWord.Sum(static family => family.TypeCount),
                types.Count(static type =>
                    type.OneWordSuffix is null),
                CountResiduals(
                    types
                        .Where(static type =>
                            type.OneWordResidual is not null)
                        .Select(static type =>
                            type.OneWordResidual!.Value))),
            new(
                types.Length,
                twoWord.Sum(static family => family.TypeCount),
                types.Count(static type =>
                    type.TwoWordSuffix is null),
                CountResiduals(
                    types
                        .Where(static type =>
                            type.TwoWordResidual is not null)
                        .Select(static type =>
                            type.TwoWordResidual!.Value))));
    }

    private static IEnumerable<LibraryNameFamilyRow> BuildFamilies(
        ImmutableArray<LibraryNameFamilyTypeRow> types,
        LibraryNameFamilyKind kind)
    {
        return types
            .Select(type => new
            {
                Type = type,
                Family = kind == LibraryNameFamilyKind.OneWordSuffix
                    ? type.OneWordSuffix
                    : type.TwoWordSuffix,
            })
            .Where(static item => item.Family is not null)
            .GroupBy(
                static item => item.Family!)
            .Select(group =>
            {
                LibraryNameFamilyTypeRow[] members =
                [
                    .. group.Select(static item => item.Type)
                        .OrderBy(static type =>
                            type.Type.Definition.Value),
                ];
                return new LibraryNameFamilyRow(
                    members[0].OneWordSuffix is { } one
                        && one.Kind == kind
                            ? one
                            : members[0].TwoWordSuffix!,
                    members.Length,
                    members.Count(static type =>
                        type.IsPublicSurface),
                    members
                        .Select(static type => type.Name.Namespace)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    [
                        .. members
                            .GroupBy(static type =>
                                type.DefinitionKind)
                            .OrderBy(static group => group.Key)
                            .Select(static group =>
                                new LibraryNameFamilyDefinitionKindCount(
                                    group.Key,
                                    group.Count())),
                    ],
                    [
                        .. members
                            .Where(static type =>
                                type.SourceEvidence is not null)
                            .GroupBy(static type =>
                                type.SourceEvidence!.Disposition)
                            .OrderBy(static group => group.Key)
                            .Select(static group =>
                                new LibraryNameFamilySourceDispositionCount(
                                    group.Key,
                                    group.Count())),
                    ],
                    [
                        .. members.Select(static type =>
                            type.Type),
                    ]);
            })
            .Order(LibraryNameFamilyOrder.Prevalence);
    }

    private static ImmutableArray<LibraryNameFamilyResidualCount<TReason>>
        CountResiduals<TReason>(IEnumerable<TReason> source)
        where TReason : struct, Enum =>
    [
        .. source
            .GroupBy(static reason => reason)
            .OrderBy(static group => group.Key)
            .Select(static group =>
                new LibraryNameFamilyResidualCount<TReason>(
                    group.Key,
                    group.Count())),
    ];

    private readonly record struct FamilyAssignment(
        LibraryNameFamilyIdentity? OneWord,
        LibraryNameFamilyIdentity? TwoWord,
        LibraryNameFamilyOneWordResidualReason? OneWordResidual,
        LibraryNameFamilyTwoWordResidualReason? TwoWordResidual);

}
