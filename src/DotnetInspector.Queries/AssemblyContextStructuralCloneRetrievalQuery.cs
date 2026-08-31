using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>An exact, reader-independent seed for structural-clone retrieval.</summary>
public abstract record StructuralCloneQuerySeed
{
    private protected StructuralCloneQuerySeed()
    {
    }

    /// <summary>Selects one MethodDef by its metadata token.</summary>
    public sealed record MethodDefinitionToken : StructuralCloneQuerySeed
    {
        public MethodDefinitionToken(int metadataToken)
        {
            if (MetadataTokens.EntityHandle(metadataToken).Kind
                != HandleKind.MethodDefinition)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(metadataToken),
                    "Structural-clone seeds require a MethodDef token.");
            }

            MetadataToken = metadataToken;
        }

        public int MetadataToken { get; }
    }

    /// <summary>Selects one method by exact declaring type and member identity.</summary>
    public sealed record Member : StructuralCloneQuerySeed
    {
        public Member(
            MetadataTypeDefinitionName type,
            MemberAnchor member)
        {
            Type =
                type
                ?? throw new ArgumentNullException(nameof(type));
            MemberIdentity =
                member
                ?? throw new ArgumentNullException(nameof(member));
        }

        public MetadataTypeDefinitionName Type { get; }
        public MemberAnchor MemberIdentity { get; }
    }
}

/// <summary>
/// Explicit candidate population for one structural-clone retrieval.
/// </summary>
public abstract record StructuralCloneQueryPopulation
{
    private protected StructuralCloneQueryPopulation()
    {
    }

    /// <summary>Enumerates every MethodDef in the candidate assembly.</summary>
    public sealed record WholeAssembly : StructuralCloneQueryPopulation;

    /// <summary>Enumerates every MethodDef declared by one exact type.</summary>
    public sealed record Type : StructuralCloneQueryPopulation
    {
        public Type(MetadataTypeDefinitionName type)
        {
            Definition =
                type
                ?? throw new ArgumentNullException(nameof(type));
        }

        public MetadataTypeDefinitionName Definition { get; }
    }
}

/// <summary>
/// Two retained assembly subjects and the bounded retrieval request joining
/// them.
/// </summary>
public sealed record AssemblyContextStructuralCloneRetrievalInput
{
    public AssemblyContextStructuralCloneRetrievalInput(
        AssemblyContextGroup seedGroup,
        AssemblyContextParticipant seedParticipant,
        AssemblyContextGroup candidateGroup,
        AssemblyContextParticipant candidateParticipant,
        StructuralCloneQuerySeed seed,
        StructuralCloneQueryPopulation population,
        StructuralCloneRetrievalLimits? limits = null)
    {
        SeedGroup =
            seedGroup
            ?? throw new ArgumentNullException(nameof(seedGroup));
        SeedParticipant =
            seedParticipant
            ?? throw new ArgumentNullException(nameof(seedParticipant));
        CandidateGroup =
            candidateGroup
            ?? throw new ArgumentNullException(nameof(candidateGroup));
        CandidateParticipant =
            candidateParticipant
            ?? throw new ArgumentNullException(nameof(candidateParticipant));
        Seed =
            seed
            ?? throw new ArgumentNullException(nameof(seed));
        Population =
            population
            ?? throw new ArgumentNullException(nameof(population));
        Limits = limits;
    }

    public AssemblyContextGroup SeedGroup { get; }
    public AssemblyContextParticipant SeedParticipant { get; }
    public AssemblyContextGroup CandidateGroup { get; }
    public AssemblyContextParticipant CandidateParticipant { get; }
    public StructuralCloneQuerySeed Seed { get; }
    public StructuralCloneQueryPopulation Population { get; }
    public StructuralCloneRetrievalLimits? Limits { get; }
}

/// <summary>The side of the request that could not be inspected.</summary>
public enum StructuralCloneQueryParticipantRole
{
    Seed,
    Candidate,
}

/// <summary>Typed query-layer failures before Analysis retrieval begins.</summary>
public enum StructuralCloneQueryFailureKind
{
    SeedMethodNotFound,
    SeedTypeNotFound,
    SeedTypeAmbiguous,
    SeedMemberNotFound,
    SeedMemberAmbiguous,
    CandidateTypeNotFound,
    CandidateTypeAmbiguous,
    MetadataInspectionFailed,
}

/// <summary>Visible detail for a query-layer target or metadata failure.</summary>
public sealed record StructuralCloneQueryFailure(
    StructuralCloneQueryFailureKind Kind,
    StructuralCloneQueryParticipantRole Role,
    string Detail);

/// <summary>
/// Typed outcome from binding one seed and one candidate population to
/// retained inspection-space content.
/// </summary>
public abstract record AssemblyContextStructuralCloneRetrievalResult
{
    private protected AssemblyContextStructuralCloneRetrievalResult()
    {
    }

    /// <summary>
    /// The selected inputs were bound and the unmodified Analysis result is
    /// available.
    /// </summary>
    public sealed record Available(
        AssemblyContextSubject SeedSubject,
        AssemblyContextSubject CandidateSubject,
        StructuralCloneQueryPopulation CandidatePopulation,
        StructuralCloneRetrievalResult Retrieval)
        : AssemblyContextStructuralCloneRetrievalResult;

    /// <summary>One selected assembly image could not be acquired.</summary>
    public sealed record Rejected(
        StructuralCloneQueryParticipantRole Role,
        AssemblyContextSubject SeedSubject,
        AssemblyContextSubject CandidateSubject,
        CandidateOpenFailure Failure)
        : AssemblyContextStructuralCloneRetrievalResult;

    /// <summary>
    /// Exact target selection or metadata inspection failed before retrieval.
    /// </summary>
    public sealed record Failed(
        AssemblyContextSubject SeedSubject,
        AssemblyContextSubject CandidateSubject,
        StructuralCloneQueryFailure Failure)
        : AssemblyContextStructuralCloneRetrievalResult;
}

/// <summary>
/// Runs one same-image or cross-image seeded structural-clone retrieval over
/// retained inspection-space content.
/// </summary>
/// <remarks>
/// Product-result pass-through is gated by
/// <c>Execute_SameAssemblyExactMemberPreservesProductResult</c> and
/// <c>Execute_CrossAssemblyTokenAndTypeScopePreserveProductResult</c>. The
/// exactly-once Analysis call count is unverified beyond direct inspection of
/// the mutually exclusive same-image and cross-image paths.
/// </remarks>
public static class AssemblyContextStructuralCloneRetrievalQuery
{
    public static InspectionQuery<
        AssemblyContextStructuralCloneRetrievalResult> Definition { get; } =
        new(
            "Assembly context structural-clone retrieval",
            InspectionCost.Unbounded);

    public static AssemblyContextStructuralCloneRetrievalResult Execute(
        AssemblyContextStructuralCloneRetrievalInput input)
        => Execute(input, CancellationToken.None);

    public static AssemblyContextStructuralCloneRetrievalResult Execute(
        AssemblyContextStructuralCloneRetrievalInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureParticipant(
            input.SeedGroup,
            input.SeedParticipant,
            nameof(input.SeedParticipant));
        EnsureParticipant(
            input.CandidateGroup,
            input.CandidateParticipant,
            nameof(input.CandidateParticipant));

        var seedSubject =
            new AssemblyContextSubject(
                input.SeedParticipant.Assembly);
        var candidateSubject =
            new AssemblyContextSubject(
                input.CandidateParticipant.Assembly);
        bool sameImage =
            ReferenceEquals(
                input.SeedGroup,
                input.CandidateGroup)
            && ReferenceEquals(
                input.SeedParticipant,
                input.CandidateParticipant);

        AssemblyImageAccessResult<
            AssemblyContextStructuralCloneRetrievalResult> seedAccess =
            input.SeedGroup.UseSnapshot(
                input.SeedParticipant,
                cancellationToken,
                seedSnapshot => ExecuteWithSeedSnapshot(
                    seedSnapshot,
                    sameImage,
                    input,
                    seedSubject,
                    candidateSubject,
                    cancellationToken));

        return seedAccess switch
        {
            AssemblyImageAccessResult<
                AssemblyContextStructuralCloneRetrievalResult>
                .Available available =>
                    available.Value,
            AssemblyImageAccessResult<
                AssemblyContextStructuralCloneRetrievalResult>
                .Rejected rejected =>
                    new AssemblyContextStructuralCloneRetrievalResult
                        .Rejected(
                            StructuralCloneQueryParticipantRole.Seed,
                            seedSubject,
                            candidateSubject,
                            rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown seed image access result."),
        };
    }

    static AssemblyContextStructuralCloneRetrievalResult
        ExecuteWithSeedSnapshot(
            AssemblyImageSnapshot seedSnapshot,
            bool sameImage,
            AssemblyContextStructuralCloneRetrievalInput input,
            AssemblyContextSubject seedSubject,
            AssemblyContextSubject candidateSubject,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var seedImage = new PEReader(seedSnapshot.Content);
        ValidatedImage validatedSeed;
        MetadataReader seedReader;
        MethodDefinitionHandle seed;
        try
        {
            validatedSeed = ValidatedImage.Create(seedImage);
            seedReader = validatedSeed.Reader;
            SeedResolution resolution =
                ResolveSeed(validatedSeed, input.Seed);
            if (resolution.Failure is { } failure)
            {
                return new AssemblyContextStructuralCloneRetrievalResult
                    .Failed(
                        seedSubject,
                        candidateSubject,
                        failure);
            }

            seed = resolution.Method;
        }
        catch (Exception ex) when (IsMalformedMetadata(ex))
        {
            return MetadataFailure(
                seedSubject,
                candidateSubject,
                StructuralCloneQueryParticipantRole.Seed,
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (sameImage)
        {
            PopulationResolution population;
            try
            {
                population =
                    ResolvePopulation(validatedSeed, input.Population);
            }
            catch (Exception ex) when (IsMalformedMetadata(ex))
            {
                return MetadataFailure(
                    seedSubject,
                    candidateSubject,
                    StructuralCloneQueryParticipantRole.Candidate,
                    ex);
            }

            if (population.Failure is { } failure)
            {
                return new AssemblyContextStructuralCloneRetrievalResult
                    .Failed(
                        seedSubject,
                        candidateSubject,
                        failure);
            }

            StructuralCloneRetrievalResult retrieval =
                StructuralCloneAnalysis.RetrieveSimilar(
                    seedImage,
                    seed,
                    population.Methods,
                    input.Limits);
            return new AssemblyContextStructuralCloneRetrievalResult
                .Available(
                    seedSubject,
                    candidateSubject,
                    input.Population,
                    retrieval);
        }

        AssemblyImageAccessResult<
            AssemblyContextStructuralCloneRetrievalResult>
            candidateAccess =
                input.CandidateGroup.UseSnapshot(
                    input.CandidateParticipant,
                    cancellationToken,
                    candidateSnapshot =>
                        ExecuteCrossImage(
                            seedImage,
                            seed,
                            candidateSnapshot,
                            input,
                            seedSubject,
                            candidateSubject,
                            cancellationToken));
        return candidateAccess switch
        {
            AssemblyImageAccessResult<
                AssemblyContextStructuralCloneRetrievalResult>
                .Available available =>
                    available.Value,
            AssemblyImageAccessResult<
                AssemblyContextStructuralCloneRetrievalResult>
                .Rejected rejected =>
                    new AssemblyContextStructuralCloneRetrievalResult
                        .Rejected(
                            StructuralCloneQueryParticipantRole
                                .Candidate,
                            seedSubject,
                            candidateSubject,
                            rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown candidate image access result."),
        };
    }

    static AssemblyContextStructuralCloneRetrievalResult
        ExecuteCrossImage(
            PEReader seedImage,
            MethodDefinitionHandle seed,
            AssemblyImageSnapshot candidateSnapshot,
            AssemblyContextStructuralCloneRetrievalInput input,
            AssemblyContextSubject seedSubject,
            AssemblyContextSubject candidateSubject,
            CancellationToken cancellationToken)
    {
        using var candidateImage =
            new PEReader(candidateSnapshot.Content);
        ImmutableArray<MethodDefinitionHandle> methods;
        try
        {
            PopulationResolution population =
                ResolvePopulation(
                    ValidatedImage.Create(candidateImage),
                    input.Population);
            if (population.Failure is { } failure)
            {
                return new AssemblyContextStructuralCloneRetrievalResult
                    .Failed(
                        seedSubject,
                        candidateSubject,
                        failure);
            }

            methods = population.Methods;
        }
        catch (Exception ex) when (IsMalformedMetadata(ex))
        {
            return MetadataFailure(
                seedSubject,
                candidateSubject,
                StructuralCloneQueryParticipantRole.Candidate,
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        StructuralCloneRetrievalResult crossImageRetrieval =
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                seed,
                candidateImage,
                methods,
                input.Limits);
        return new AssemblyContextStructuralCloneRetrievalResult
            .Available(
                seedSubject,
                candidateSubject,
                input.Population,
                crossImageRetrieval);
    }

    static SeedResolution ResolveSeed(
        ValidatedImage image,
        StructuralCloneQuerySeed seed)
        => seed switch
        {
            StructuralCloneQuerySeed.MethodDefinitionToken token =>
                ResolveSeedToken(image.Reader, token.MetadataToken),
            StructuralCloneQuerySeed.Member member =>
                ResolveSeedMember(image.Reader, member),
            _ => throw new InvalidOperationException(
                $"Unknown structural-clone seed '{seed.GetType().Name}'."),
        };

    static SeedResolution ResolveSeedToken(
        MetadataReader reader,
        int metadataToken)
    {
        int row = MetadataTokens.GetRowNumber(
            MetadataTokens.EntityHandle(metadataToken));
        if (row <= 0
            || row > reader.GetTableRowCount(TableIndex.MethodDef))
        {
            return SeedResolution.Failed(
                StructuralCloneQueryFailureKind.SeedMethodNotFound,
                $"Seed MethodDef 0x{metadataToken:X8} does not exist.");
        }

        return SeedResolution.Resolved(
            MetadataTokens.MethodDefinitionHandle(row));
    }

    static SeedResolution ResolveSeedMember(
        MetadataReader reader,
        StructuralCloneQuerySeed.Member seed)
    {
        TypeResolution type =
            ResolveType(
                reader,
                seed.Type,
                StructuralCloneQueryParticipantRole.Seed);
        if (type.Failure is { } typeFailure)
        {
            return new SeedResolution(default, typeFailure);
        }

        MethodDefinitionHandle match = default;
        int matches = 0;
        int inspectedMethods = 0;
        int identityDecodeFailures = 0;
        int anchorWorkRemaining =
            MetadataSafetyPolicy.MaxClassificationScanWorkChars;
        Exception? rejected = null;
        TypeDefinition definition =
            reader.GetTypeDefinition(type.Handle);
        var attributeBudget = new AttributeInspectionBudget();
        bool isExtensionContainer =
            definition.Attributes.HasFlag(TypeAttributes.Abstract)
            && definition.Attributes.HasFlag(TypeAttributes.Sealed)
            && HasExtensionAttribute(
                reader,
                definition.GetCustomAttributes(),
                attributeBudget);
        foreach (MethodDefinitionHandle methodHandle
            in definition.GetMethods())
        {
            inspectedMethods++;
            if (inspectedMethods
                > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
            {
                throw new BadImageFormatException(
                    "The exact seed member lookup exceeds the MethodDef "
                        + "row budget.");
            }

            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            MemberAnchor anchor;
            try
            {
                bool isExtensionMethod =
                    isExtensionContainer
                    && method.Attributes.HasFlag(
                        MethodAttributes.Static)
                    && HasExtensionAttribute(
                        reader,
                        method.GetCustomAttributes(),
                        attributeBudget);
                anchor = ApiMemberIdentity.CreateMethodAnchorInfo(
                        reader,
                        type.Handle,
                        method,
                        ref anchorWorkRemaining,
                        isExtensionMethod)
                    .Anchor;
            }
            catch (Exception ex) when (IsMalformedMetadata(ex))
            {
                if (ex is AttributeInspectionBudgetException)
                {
                    throw;
                }
                if (anchorWorkRemaining <= 0)
                {
                    throw new BadImageFormatException(
                        "The exact seed member lookup exceeds the "
                            + "anchor-signature work budget.",
                        ex);
                }
                identityDecodeFailures++;
                if (identityDecodeFailures
                    >= MetadataSafetyPolicy
                        .MaxClassificationIdentityDecodeFailures)
                {
                    throw new BadImageFormatException(
                        "The exact seed member lookup exceeds the "
                            + "method-identity decode failure budget.",
                        ex);
                }

                rejected ??= ex;
                continue;
            }

            if (anchor != seed.MemberIdentity)
            {
                continue;
            }

            match = methodHandle;
            matches++;
        }

        // A rejected sibling cannot be shown to decode to a different
        // anchor, so a single healthy match does not establish
        // uniqueness. Surface the metadata failure rather than return a
        // confident result that a successful decode might have made
        // ambiguous.
        if (rejected is not null)
        {
            throw new BadImageFormatException(
                "A MethodDef could not be inspected while resolving "
                    + "the exact seed member.",
                rejected);
        }

        return matches switch
        {
            0 => SeedResolution.Failed(
                    StructuralCloneQueryFailureKind.SeedMemberNotFound,
                    "The exact seed member does not exist in the selected seed type."),
            1 => SeedResolution.Resolved(match),
            _ => SeedResolution.Failed(
                StructuralCloneQueryFailureKind.SeedMemberAmbiguous,
                "The exact seed member identifies more than one MethodDef."),
        };
    }

    static PopulationResolution ResolvePopulation(
        ValidatedImage image,
        StructuralCloneQueryPopulation population)
    {
        MetadataReader reader = image.Reader;
        if (population
            is StructuralCloneQueryPopulation.WholeAssembly)
        {
            return PopulationResolution.Resolved(
                reader.MethodDefinitions.ToImmutableArray());
        }

        if (population
            is not StructuralCloneQueryPopulation.Type typePopulation)
        {
            throw new InvalidOperationException(
                $"Unknown structural-clone population '{population.GetType().Name}'.");
        }
        TypeResolution type =
            ResolveType(
                reader,
                typePopulation.Definition,
                StructuralCloneQueryParticipantRole.Candidate);
        if (type.Failure is { } failure)
        {
            return new PopulationResolution(default, failure);
        }

        return PopulationResolution.Resolved(
            reader.GetTypeDefinition(type.Handle)
                .GetMethods()
                .ToImmutableArray());
    }

    static TypeResolution ResolveType(
        MetadataReader reader,
        MetadataTypeDefinitionName name,
        StructuralCloneQueryParticipantRole role)
    {
        TypeDefinitionHandle match = default;
        int matches = 0;
        long comparisonWork = Encoding.UTF8.GetByteCount(
            name.Namespace);
        foreach (string segment in name.Segments)
        {
            comparisonWork +=
                Encoding.UTF8.GetByteCount(segment);
        }
        comparisonWork = Math.Max(comparisonWork, 1);
        if (comparisonWork
            > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars)
        {
            throw new BadImageFormatException(
                "The exact TypeDef name exceeds the structural-name "
                    + "work budget.");
        }
        long remainingComparisonWork =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;
        int leafUtf8Length = Encoding.UTF8.GetByteCount(
            name.Segments[^1]);
        int typeNameDecodeFailures = 0;
        MetadataTypeNameFailure? rejected = null;
        foreach (TypeDefinitionHandle candidate
            in reader.TypeDefinitions)
        {
            remainingComparisonWork--;
            if (remainingComparisonWork < 0)
            {
                throw new BadImageFormatException(
                    "The exact TypeDef lookup exceeded its "
                        + "structural-name work budget.");
            }

            int candidateLeafUtf8Length;
            try
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(candidate);
                candidateLeafUtf8Length =
                    reader.GetBlobReader(definition.Name).Length;
            }
            catch (Exception ex) when (IsMalformedMetadata(ex))
            {
                NoteTypeNameDecodeFailure(
                    ref typeNameDecodeFailures,
                    ex);
                rejected ??=
                    MetadataTypeNameFailure.Malformed(
                        candidate,
                        ex.Message);
                continue;
            }

            remainingComparisonWork -=
                Math.Max(candidateLeafUtf8Length - 1, 0);
            if (remainingComparisonWork < 0)
            {
                throw new BadImageFormatException(
                    "The exact TypeDef lookup exceeded its "
                        + "structural-name work budget.");
            }
            if (candidateLeafUtf8Length != leafUtf8Length)
            {
                continue;
            }

            // Matching a leaf walks the whole declaring chain and scans
            // the walked prefix for cycles, so the comparison this is
            // about to perform costs far more than the names involved.
            // Charge that traversal at its ceiling: the leaf length
            // alone would let a deep shared chain amplify bounded
            // budget into unbounded work.
            remainingComparisonWork -=
                comparisonWork
                + MetadataSafetyPolicy.MaxRelationshipNodes;
            if (remainingComparisonWork < 0)
            {
                throw new BadImageFormatException(
                    "The exact TypeDef lookup exceeded its "
                        + "structural-name work budget.");
            }

            MetadataTypeDefinitionNameMatchResult result =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    candidate,
                    name,
                    out MetadataTypeNameFailure? failure);
            if (result
                == MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                NoteTypeNameDecodeFailure(
                    ref typeNameDecodeFailures);
                rejected ??= failure;
                continue;
            }
            if (result
                != MetadataTypeDefinitionNameMatchResult.Match)
            {
                continue;
            }

            match = candidate;
            matches++;
        }

        bool seedSide =
            role == StructuralCloneQueryParticipantRole.Seed;
        if (matches == 0 && rejected is not null)
        {
            throw new BadImageFormatException(
                rejected.Detail);
        }
        return matches switch
        {
            0 => new TypeResolution(
                default,
                new StructuralCloneQueryFailure(
                    seedSide
                        ? StructuralCloneQueryFailureKind.SeedTypeNotFound
                        : StructuralCloneQueryFailureKind
                            .CandidateTypeNotFound,
                    seedSide
                        ? StructuralCloneQueryParticipantRole.Seed
                        : StructuralCloneQueryParticipantRole.Candidate,
                    $"Type '{name.ToEscapedFullName()}' does not exist.")),
            1 => new TypeResolution(match, null),
            _ => new TypeResolution(
                default,
                new StructuralCloneQueryFailure(
                    seedSide
                        ? StructuralCloneQueryFailureKind.SeedTypeAmbiguous
                        : StructuralCloneQueryFailureKind
                            .CandidateTypeAmbiguous,
                    seedSide
                        ? StructuralCloneQueryParticipantRole.Seed
                        : StructuralCloneQueryParticipantRole.Candidate,
                    $"Type '{name.ToEscapedFullName()}' is ambiguous.")),
        };
    }

    /// <summary>
    /// A metadata reader whose TypeDef method ranges are known to
    /// partition the MethodDef table.
    /// </summary>
    /// <remarks>
    /// Seed and population resolution accept only this type, so the
    /// projection guarantee is established once at image entry rather
    /// than re-derived at each projection site. Three review rounds
    /// found holes of exactly that second shape.
    /// </remarks>
    readonly struct ValidatedImage
    {
        ValidatedImage(MetadataReader reader) => Reader = reader;

        public MetadataReader Reader { get; }

        public static ValidatedImage Create(PEReader image)
        {
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(image);
            ValidateMethodOwnership(reader);
            return new ValidatedImage(reader);
        }
    }

    static void NoteTypeNameDecodeFailure(
        ref int typeNameDecodeFailures,
        Exception? inner = null)
    {
        typeNameDecodeFailures++;
        if (typeNameDecodeFailures
            >= MetadataSafetyPolicy
                .MaxClassificationIdentityDecodeFailures)
        {
            throw new BadImageFormatException(
                "The exact TypeDef lookup exceeds the "
                    + "type-name decode failure budget.",
                inner);
        }
    }

    /// <summary>
    /// Verifies that the image's TypeDef method ranges partition the
    /// MethodDef table exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-projection checks alone are not sufficient. A corrupt
    /// <c>MethodList</c> start yields an empty range rather than an
    /// error, which would turn a malformed image into a success-shaped
    /// empty population, and a corrupt <c>MethodPtr</c> table can alias
    /// one MethodDef row into two different types without repeating
    /// within either type's own projection.
    /// </para>
    /// <para>
    /// SRM exposes no raw <c>MethodList</c> column, so the partition is
    /// checked by construction: no range may report a negative length,
    /// every projected row must be in range and claimed exactly once,
    /// and the claimed rows must cover the table. This holds for
    /// optimized and unoptimized images alike, because a valid
    /// <c>MethodPtr</c> table is itself a permutation of the MethodDef
    /// rows.
    /// </para>
    /// <para>
    /// Those requirements bound the raw column jointly, and no one of
    /// them does it alone. A descending start is not silent: SRM reports
    /// that range with a negative <c>Count</c> while enumerating
    /// nothing, so rejecting a negative length is what makes the starts
    /// non-decreasing. Coverage then supplies the rest, because with the
    /// starts rising the enumerated total is the projected row count
    /// less the first non-null start plus one; requiring distinct
    /// in-range rows that total the MethodDef row count forces that
    /// first non-null start to row 1 and holds every later start within
    /// <c>projectionRows + 1</c>. A null start is not part of that
    /// chain: ECMA-335 II.22.37 permits it and SRM reports its range as
    /// length zero rather than as the difference to the next start, so
    /// leading nulls neither rise nor break the ordering. Only a
    /// *leading* null is expressible, though. Because each run is
    /// delimited by the following TypeDef's start, a null after a
    /// populated run would end the preceding run before it began, and
    /// the negative length lands on that preceding row rather than on
    /// the null itself. Such a column is malformed and is rejected.
    /// Neither check
    /// is redundant: a descending column passes coverage, and a column
    /// starting past row 1 passes the length check.
    /// </para>
    /// <para>
    /// The projection alone does not prove that permutation, because a
    /// <c>MethodPtr</c> row that no TypeDef range covers is never
    /// projected and so is never checked. An unreachable row still
    /// changes what SRM reports for a reachable method, because
    /// declaring-type lookup scans <c>MethodPtr</c> for the first row
    /// naming a MethodDef and can land on the uncovered row. Requiring
    /// equal row counts closes that gap: with every projected row
    /// distinct, in range, and covering the MethodDef table, equal
    /// counts leave no <c>MethodPtr</c> row uncovered.
    /// </para>
    /// </remarks>
    static void ValidateMethodOwnership(MetadataReader reader)
    {
        if (reader.GetTableRowCount(TableIndex.TypeDef) == 0)
        {
            throw new BadImageFormatException(
                "The image declares no TypeDef rows, so it lacks the "
                    + "module pseudo-type that owns module-wide "
                    + "methods.");
        }

        int methodRows = reader.GetTableRowCount(TableIndex.MethodDef);
        int methodPtrRows =
            reader.GetTableRowCount(TableIndex.MethodPtr);
        if (methodPtrRows != 0 && methodPtrRows != methodRows)
        {
            throw new BadImageFormatException(
                "The MethodPtr table is not a permutation of the "
                    + "MethodDef table.");
        }

        var owned = new HashSet<MethodDefinitionHandle>();
        foreach (TypeDefinitionHandle type in reader.TypeDefinitions)
        {
            MethodDefinitionHandleCollection methods =
                reader.GetTypeDefinition(type).GetMethods();

            // Checked before enumerating, because a negative range
            // yields no elements rather than an error.
            if (methods.Count < 0)
            {
                throw new BadImageFormatException(
                    "The TypeDef MethodList column is not a "
                        + "non-decreasing range in the projected "
                        + "method table.");
            }

            foreach (MethodDefinitionHandle method in methods)
            {
                ValidateProjectedMethod(method, methodRows, owned);
            }
        }

        if (owned.Count != methodRows)
        {
            throw new BadImageFormatException(
                "The TypeDef method ranges do not cover the MethodDef "
                    + "table exactly once.");
        }
    }

    /// <summary>
    /// Rejects a projected MethodDef row that is out of range or already
    /// seen, so a malformed projection cannot reach Analysis as an
    /// untyped argument error.
    /// </summary>
    static void ValidateProjectedMethod(
        MethodDefinitionHandle method,
        int methodRows,
        HashSet<MethodDefinitionHandle> projected)
    {
        int row = MetadataTokens.GetRowNumber(method);
        if (row == 0 || row > methodRows)
        {
            throw new BadImageFormatException(
                "A projected MethodDef row falls outside the MethodDef "
                    + "table.");
        }

        if (!projected.Add(method))
        {
            throw new BadImageFormatException(
                "The selected method projection repeats a MethodDef "
                    + "row.");
        }
    }

    static bool HasExtensionAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        AttributeInspectionBudget budget)
    {
        budget.Admit(attributes);
        try
        {
            foreach (CustomAttributeHandle attributeHandle
                in attributes)
            {
                CustomAttribute attribute =
                    reader.GetCustomAttribute(attributeHandle);
                string? attributeTypeName =
                    AttributeReader.GetAttributeTypeName(
                        reader,
                        attribute.Constructor,
                        budget.ObserveMaterialization);
                if (attributeTypeName is null)
                {
                    attributeTypeName =
                        ResolveRejectedAttributeType(
                            reader,
                            attribute.Constructor,
                            budget);
                }

                if (attributeTypeName
                    == KnownAttributeNames.ExtensionAttribute)
                {
                    return true;
                }
            }

            return false;
        }
        catch (AttributeInspectionBudgetSignalException ex)
        {
            // Signature decoding converts BadImageFormatException to a
            // rejection, so the callback uses a private signal until it leaves
            // that guarded boundary.
            throw new AttributeInspectionBudgetException(
                ex.Message,
                ex);
        }
    }

    static string? ResolveRejectedAttributeType(
        MetadataReader reader,
        EntityHandle constructor,
        AttributeInspectionBudget budget)
    {
        EntityHandle type = constructor.Kind switch
        {
            HandleKind.MemberReference =>
                reader.GetMemberReference(
                    (MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition =>
                reader.GetMethodDefinition(
                    (MethodDefinitionHandle)constructor)
                    .GetDeclaringType(),
            _ => default,
        };
        if (type.IsNil)
        {
            return null;
        }

        return TypeResolver.ResolveTypeName(reader, type) switch
        {
            MetadataTypeNameResult.Resolved resolved =>
                ChargeResolvedAttributeType(resolved.Value, budget),
            MetadataTypeNameResult.Absent => null,
            MetadataTypeNameResult.Rejected rejected =>
                throw new BadImageFormatException(
                    rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-name result."),
        };
    }

    static string ChargeResolvedAttributeType(
        string value,
        AttributeInspectionBudget budget)
    {
        budget.ObserveMaterialization(
            Encoding.UTF8.GetByteCount(value));
        return value;
    }

    static AssemblyContextStructuralCloneRetrievalResult MetadataFailure(
        AssemblyContextSubject seedSubject,
        AssemblyContextSubject candidateSubject,
        StructuralCloneQueryParticipantRole role,
        Exception error)
        => new AssemblyContextStructuralCloneRetrievalResult.Failed(
            seedSubject,
            candidateSubject,
            new StructuralCloneQueryFailure(
                StructuralCloneQueryFailureKind.MetadataInspectionFailed,
                role,
                error.Message));

    static bool IsMalformedMetadata(Exception exception)
        => exception is BadImageFormatException
            or ArgumentOutOfRangeException
            or OverflowException;

    static void EnsureParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string parameterName)
    {
        if (!group.Participants.Any(
                candidate => ReferenceEquals(
                    candidate,
                    participant)))
        {
            throw new ArgumentException(
                "The selected participant is not a member of its assembly context group.",
                parameterName);
        }
    }

    readonly record struct SeedResolution(
        MethodDefinitionHandle Method,
        StructuralCloneQueryFailure? Failure)
    {
        internal static SeedResolution Resolved(
            MethodDefinitionHandle method) =>
            new(method, null);

        internal static SeedResolution Failed(
            StructuralCloneQueryFailureKind kind,
            string detail) =>
            new(
                default,
                new StructuralCloneQueryFailure(
                    kind,
                    StructuralCloneQueryParticipantRole.Seed,
                    detail));
    }

    readonly record struct PopulationResolution(
        ImmutableArray<MethodDefinitionHandle> Methods,
        StructuralCloneQueryFailure? Failure)
    {
        internal static PopulationResolution Resolved(
            ImmutableArray<MethodDefinitionHandle> methods) =>
            new(methods, null);
    }

    readonly record struct TypeResolution(
        TypeDefinitionHandle Handle,
        StructuralCloneQueryFailure? Failure);

    sealed class AttributeInspectionBudget
    {
        const int MinimumRowCharge = 64;
        const string ExceededMessage =
            "The exact seed member lookup exceeds the custom "
                + "attribute work budget.";
        int remaining =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;

        internal void Admit(
            CustomAttributeHandleCollection attributes)
            => Charge(
                (long)attributes.Count * MinimumRowCharge);

        internal void ObserveMaterialization(int work)
        {
            long charge = Math.Max(work, 1);
            if (charge > remaining)
            {
                throw new AttributeInspectionBudgetSignalException(
                    ExceededMessage);
            }

            remaining -= (int)charge;
        }

        void Charge(long work)
        {
            if (work > remaining)
            {
                throw new AttributeInspectionBudgetException(
                    ExceededMessage);
            }

            remaining -= (int)work;
        }
    }

    sealed class AttributeInspectionBudgetSignalException(string message)
        : Exception(message);

    sealed class AttributeInspectionBudgetException(
        string message,
        Exception? innerException = null)
        : BadImageFormatException(message, innerException);
}
