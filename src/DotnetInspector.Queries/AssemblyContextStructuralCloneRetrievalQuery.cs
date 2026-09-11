using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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
        StructuralCloneValidatedImage validatedSeed;
        MetadataReader seedReader;
        MethodDefinitionHandle seed;
        try
        {
            validatedSeed = StructuralCloneValidatedImage.Create(seedImage);
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
        catch (Exception ex)
            when (StructuralCloneMetadataResolution
                .IsMalformedMetadata(ex))
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
            catch (Exception ex)
                when (StructuralCloneMetadataResolution
                    .IsMalformedMetadata(ex))
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
                    StructuralCloneValidatedImage.Create(candidateImage),
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
        catch (Exception ex)
            when (StructuralCloneMetadataResolution
                .IsMalformedMetadata(ex))
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
        StructuralCloneValidatedImage image,
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
        StructuralCloneMemberResolution member =
            StructuralCloneMetadataResolution.ResolveMember(
                reader,
                seed.Type,
                seed.MemberIdentity);
        return member.Status switch
        {
            StructuralCloneMemberResolutionStatus.Resolved =>
                SeedResolution.Resolved(member.Method),
            StructuralCloneMemberResolutionStatus.TypeNotFound =>
                SeedResolution.Failed(
                    StructuralCloneQueryFailureKind.SeedTypeNotFound,
                    $"Type '{seed.Type.ToEscapedFullName()}' does not exist."),
            StructuralCloneMemberResolutionStatus.TypeAmbiguous =>
                SeedResolution.Failed(
                    StructuralCloneQueryFailureKind.SeedTypeAmbiguous,
                    $"Type '{seed.Type.ToEscapedFullName()}' is ambiguous."),
            StructuralCloneMemberResolutionStatus.MemberNotFound =>
                SeedResolution.Failed(
                    StructuralCloneQueryFailureKind.SeedMemberNotFound,
                    "The exact seed member does not exist in the selected seed type."),
            _ => SeedResolution.Failed(
                StructuralCloneQueryFailureKind.SeedMemberAmbiguous,
                "The exact seed member identifies more than one MethodDef."),
        };
    }

    static PopulationResolution ResolvePopulation(
        StructuralCloneValidatedImage image,
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
        StructuralCloneTypeResolution type =
            StructuralCloneMetadataResolution.ResolveType(reader, name);
        bool seedSide =
            role == StructuralCloneQueryParticipantRole.Seed;
        return type.Status switch
        {
            StructuralCloneTypeResolutionStatus.Resolved =>
                new TypeResolution(type.Handle, null),
            StructuralCloneTypeResolutionStatus.NotFound =>
                new TypeResolution(
                    default,
                    new StructuralCloneQueryFailure(
                        seedSide
                            ? StructuralCloneQueryFailureKind.SeedTypeNotFound
                            : StructuralCloneQueryFailureKind
                                .CandidateTypeNotFound,
                        role,
                        $"Type '{name.ToEscapedFullName()}' does not exist.")),
            _ => new TypeResolution(
                default,
                new StructuralCloneQueryFailure(
                    seedSide
                        ? StructuralCloneQueryFailureKind.SeedTypeAmbiguous
                        : StructuralCloneQueryFailureKind
                            .CandidateTypeAmbiguous,
                    role,
                    $"Type '{name.ToEscapedFullName()}' is ambiguous.")),
        };
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
