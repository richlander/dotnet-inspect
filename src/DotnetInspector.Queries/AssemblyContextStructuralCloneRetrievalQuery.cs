using System.Collections.Immutable;
using System.Reflection;
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
        MetadataReader seedReader;
        MethodDefinitionHandle seed;
        try
        {
            seedReader = seedImage.GetMetadataReader();
            SeedResolution resolution =
                ResolveSeed(seedReader, input.Seed);
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
                    ResolvePopulation(seedReader, input.Population);
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
            MetadataReader candidateReader =
                candidateImage.GetMetadataReader();
            PopulationResolution population =
                ResolvePopulation(
                    candidateReader,
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
        MetadataReader reader,
        StructuralCloneQuerySeed seed)
        => seed switch
        {
            StructuralCloneQuerySeed.MethodDefinitionToken token =>
                ResolveSeedToken(reader, token.MetadataToken),
            StructuralCloneQuerySeed.Member member =>
                ResolveSeedMember(reader, member),
            _ => throw new InvalidOperationException(
                $"Unknown structural-clone seed '{seed.GetType().Name}'."),
        };

    static SeedResolution ResolveSeedToken(
        MetadataReader reader,
        int metadataToken)
    {
        int row = MetadataTokens.GetRowNumber(
            MetadataTokens.EntityHandle(metadataToken));
        if (row == 0
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
        Exception? rejected = null;
        TypeDefinition definition =
            reader.GetTypeDefinition(type.Handle);
        foreach (MethodDefinitionHandle methodHandle
            in definition.GetMethods())
        {
            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            MemberAnchor anchor;
            try
            {
                anchor = ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    type.Handle,
                    method,
                    IsExtensionMethod(
                        reader,
                        definition,
                        method));
            }
            catch (Exception ex) when (IsMalformedMetadata(ex))
            {
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

        return matches switch
        {
            0 when rejected is not null =>
                throw new BadImageFormatException(
                    "A MethodDef could not be inspected while resolving "
                        + "the exact seed member.",
                    rejected),
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
        MetadataReader reader,
        StructuralCloneQueryPopulation population)
    {
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
            ValidatedMethods(reader, type.Handle));
    }

    static TypeResolution ResolveType(
        MetadataReader reader,
        MetadataTypeDefinitionName name,
        StructuralCloneQueryParticipantRole role)
    {
        TypeDefinitionHandle match = default;
        int matches = 0;
        MetadataTypeNameFailure? rejected = null;
        foreach (TypeDefinitionHandle candidate
            in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionNameMatchResult result =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    candidate,
                    name,
                    out MetadataTypeNameFailure? failure);
            if (result
                == MetadataTypeDefinitionNameMatchResult.Rejected)
            {
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

    static ImmutableArray<MethodDefinitionHandle> ValidatedMethods(
        MetadataReader reader,
        TypeDefinitionHandle type)
    {
        int methodRows =
            reader.GetTableRowCount(TableIndex.MethodDef);
        var methods =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
        foreach (MethodDefinitionHandle method
            in reader.GetTypeDefinition(type).GetMethods())
        {
            int row = MetadataTokens.GetRowNumber(method);
            if (row == 0 || row > methodRows)
            {
                throw new BadImageFormatException(
                    "The selected TypeDef contains an invalid MethodDef range.");
            }

            methods.Add(method);
        }

        return methods.ToImmutable();
    }

    static bool IsExtensionMethod(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method)
        => type.Attributes.HasFlag(TypeAttributes.Abstract)
            && type.Attributes.HasFlag(TypeAttributes.Sealed)
            && method.Attributes.HasFlag(MethodAttributes.Static)
            && AttributeReader.HasExtensionAttribute(
                reader,
                type.GetCustomAttributes())
            && AttributeReader.HasExtensionAttribute(
                reader,
                method.GetCustomAttributes());

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
}
