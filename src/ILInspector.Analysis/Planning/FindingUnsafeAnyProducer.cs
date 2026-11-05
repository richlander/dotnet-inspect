using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>One visited body's census facts and its source Type.</summary>
public readonly struct FindingUnsafeAnyBodyFact
{
    internal FindingUnsafeAnyBodyFact(
        UnsafeMemberCensusBodyFact census,
        TypeDefinitionHandle sourceType)
    {
        Census = census;
        SourceType = sourceType;
    }

    internal UnsafeMemberCensusBodyFact Census { get; }

    internal TypeDefinitionHandle SourceType { get; }
}

/// <summary>
/// Finding Unsafe Any: the unsafe member census's visit with each source Type
/// settled at its first finding, as
/// <c>docs/design/unsafe-code-section.md#finding-unsafe-any</c> defines. A
/// body that is its own declared member and has a census role settles its
/// Type; the Type's later bodies, and the bodies of the compiler-generated
/// Types nested in it, are not visited. The fold attributes the visited
/// bodies as the census does and answers per Type.
/// </summary>
public sealed class FindingUnsafeAnyProducer
    : MethodDefinitionProducer<
        FindingUnsafeAnyBodyFact,
        List<FindingUnsafeAnyBodyFact>,
        UnsafeCodeTypes>
{
    FindingUnsafeAnyProducer()
        : base(
            "FindingUnsafeAny",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.Calls
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.ReferenceAssemblyStatus)
    {
    }

    public static FindingUnsafeAnyProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override bool SettlesTypes => true;

    internal override FindingUnsafeAnyBodyFact Visit(
        scoped MethodDefinitionView view) =>
        new(
            UnsafeMemberCensusProducer.Instance.Visit(view),
            view.SourceType);

    // A body settles its Type only when the census will attribute its roles
    // to itself: the shape rule of the census's presence probe, so a
    // generated body, which needs an authenticated owner, never settles.
    internal override bool Settles(FindingUnsafeAnyBodyFact fact)
    {
        UnsafeMemberBodyFacts facts = fact.Census.Facts;
        return facts.Body is { } identity
            && !facts.IsExtensionDeclarationSkeleton
            && !facts.Evidence.IsEmpty
            && !fact.Census.RequiresOwnerByShape
            && !CompilerGeneratedNames.RequiresDeclaredOwner(identity);
    }

    internal override List<FindingUnsafeAnyBodyFact> Seed() => [];

    internal override List<FindingUnsafeAnyBodyFact> Accumulate(
        List<FindingUnsafeAnyBodyFact> accumulator,
        FindingUnsafeAnyBodyFact fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override UnsafeCodeTypes Complete(
        List<FindingUnsafeAnyBodyFact> accumulator,
        MethodDefinitionCompletionView completion)
    {
        var sourceTypes = new Dictionary<int, TypeDefinitionHandle>(accumulator.Count);
        var census = new List<UnsafeMemberCensusBodyFact>(accumulator.Count);
        foreach (FindingUnsafeAnyBodyFact fact in accumulator)
        {
            sourceTypes[fact.Census.Facts.Token] = fact.SourceType;
            census.Add(fact.Census);
        }

        // The census fold, without exposure, over the bodies visited.
        UnsafeMemberCensus folded = UnsafeMemberCensusBuilder.Build(
            UnsafeMemberCensusProducer.AttributedBodies(census, completion),
            hasFullMethodEvidenceScope: true,
            completion.GetReferenceAssemblyStatus(),
            UnsafeMemberRootSet.NotRead,
            attributionBounded: completion.IsSourceBounded);
        return Answer(folded, sourceTypes);
    }

    /// <summary>
    /// Each Type with a finding is <see cref="UnsafeCodeTypeState.Unsafe"/>.
    /// A limitation stays only where it leaves an answer unsettled: an image
    /// limitation, a body whose source Type has no finding, and so an
    /// <see cref="UnsafeCodeTypeState.Unknown"/> Type when the body's owner
    /// was authenticated. For Roslyn output, which bodies settlement skipped
    /// therefore never changes the answer.
    /// </summary>
    internal static UnsafeCodeTypes Answer(
        UnsafeMemberCensus census,
        IReadOnlyDictionary<int, TypeDefinitionHandle> sourceTypes)
    {
        var settled = new HashSet<TypeDefinitionHandle>();
        var types = ImmutableArray.CreateBuilder<UnsafeCodeType>();
        var named = new HashSet<TypeRef>();
        foreach (UnsafeMemberTypeGroup group in UnsafeMemberTypeGroup.Group(census.Members))
        {
            types.Add(new UnsafeCodeType(group.DeclaringType, UnsafeCodeTypeState.Unsafe));
            named.Add(group.DeclaringType);
            foreach (UnsafeMemberFinding finding in group.Members)
            {
                if (sourceTypes.TryGetValue(finding.Member.MetadataToken, out var source))
                    settled.Add(source);
            }
        }

        var limitations = ImmutableArray.CreateBuilder<UnsafeMemberLimitation>();
        foreach (UnsafeMemberLimitation limitation in census.Limitations)
        {
            int? token = limitation.DeclaredMember?.MetadataToken ?? limitation.BodyToken;
            if (token is { } known
                && sourceTypes.TryGetValue(known, out var source)
                && settled.Contains(source))
            {
                continue;
            }

            limitations.Add(limitation);
            if (limitation.DeclaredMember is { DeclaringType: var type }
                && named.Add(type))
            {
                types.Add(new UnsafeCodeType(type, UnsafeCodeTypeState.Unknown));
            }
        }

        return new UnsafeCodeTypes(types.ToImmutable(), limitations.ToImmutable());
    }
}

/// <summary>The owner-issued Finding Unsafe Any request over a whole library.</summary>
public static class FindingUnsafeAny
{
    /// <summary>The Finding Unsafe Any fold with a Complete terminal.</summary>
    public static ProducerRequest Request { get; } =
        new(FindingUnsafeAnyProducer.Instance);

    /// <summary>The work description, computed without reading any subject.</summary>
    public static WorkDescription Description { get; } =
        ProducerPlanner.Plan([Request]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException(
                "The Finding Unsafe Any request must plan.");

    /// <summary>The request over every method definition, at the census's Library breadth.</summary>
    public static MethodDefinitionSourceRequest<UnsafeCodeTypes> ForLibrary(
        MethodDefinitionGeneratedExpansionLimits? limits = null) =>
        MethodDefinitionSourceRequest<UnsafeCodeTypes>.Create(
            Description,
            FindingUnsafeAnyProducer.Instance,
            UnsafeMemberCensusAnalysis.LibraryScope(limits));

    /// <summary>Projects one detached operation with its receipts.</summary>
    public static FindingUnsafeAnyResult Project(
        AssemblyAnalysisExecution<UnsafeCodeTypes> execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ProducerResult<UnsafeCodeTypes> result =
            execution.ResultOf(FindingUnsafeAnyProducer.Instance);
        MethodDefinitionSourceReceipt source =
            execution.MethodDefinitionReceipt();
        WorkReceipt receipt = execution.WorkReceipt;
        // A bounded expansion keeps the retained prefix's answers; its
        // attribution-bound limitation makes them incomplete.
        if ((result.HasValue
                || result.Outcome == ProducerOutcome.SourceBounded)
            && result.Value is { } types)
        {
            return new FindingUnsafeAnyResult.Available(types, source, receipt);
        }

        string unit = result.Failure?.Unit ?? result.Critical?.Unit ?? "(unknown)";
        string reason = result.Failure?.Message
            ?? result.Critical?.Message
            ?? result.FailedPrerequisite
            ?? "";
        return new FindingUnsafeAnyResult.Failed(
            new InvalidDataException(
                "Finding Unsafe Any failed because "
                + $"{unit} could not be analyzed: {reason}"),
            result.Outcome,
            source,
            receipt);
    }
}

/// <summary>A detached Finding Unsafe Any answer, or the inspection failure that prevented it.</summary>
public abstract record FindingUnsafeAnyResult
{
    private FindingUnsafeAnyResult()
    {
    }

    /// <summary>The answer, complete or incomplete with its limitations.</summary>
    public sealed record Available(
        UnsafeCodeTypes Types,
        MethodDefinitionSourceReceipt SourceReceipt,
        WorkReceipt Receipt)
        : FindingUnsafeAnyResult;

    /// <summary>An inspection failure, not an observation.</summary>
    public sealed record Failed(
        InvalidDataException Error,
        ProducerOutcome Outcome,
        MethodDefinitionSourceReceipt SourceReceipt,
        WorkReceipt Receipt)
        : FindingUnsafeAnyResult;
}
