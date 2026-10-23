using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// One visited body's detached census fact. Its declared-source outcome is
/// read at completion, where the relation has settled at every breadth, and
/// joins the body to its authenticated owner's identity.
/// </summary>
public readonly struct UnsafeMemberCensusBodyFact
{
    internal UnsafeMemberCensusBodyFact(
        UnsafeMemberBodyFacts facts,
        bool requiresOwnerByShape)
    {
        Facts = facts;
        RequiresOwnerByShape = requiresOwnerByShape;
    }

    internal UnsafeMemberBodyFacts Facts { get; }

    /// <summary>
    /// Whether the body needs an authenticated owner whatever its declared
    /// source: its type is a nested compiler-generated type, or it is an
    /// unconfirmed extension declaration.
    /// </summary>
    internal bool RequiresOwnerByShape { get; }
}

/// <summary>
/// The unsafe member census as one focused value producer over an
/// owner-issued Method Query Source request, as
/// <c>docs/design/unsafe-member-findings.md#execution</c> defines it. Each
/// visit emits one physical body's facts; completion reads each body's
/// declared source, joins its owner, and folds them through
/// <see cref="UnsafeMemberCensusBuilder"/> with the image's public roots and
/// reference-assembly status.
/// </summary>
public sealed class UnsafeMemberCensusProducer
    : MethodDefinitionProducer<
        UnsafeMemberCensusBodyFact,
        List<UnsafeMemberCensusBodyFact>,
        UnsafeMemberCensus>
{
    UnsafeMemberCensusProducer()
        : base(
            "UnsafeMemberCensus",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.Calls
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.PublicMethodRoots
                | MethodDefinitionLayers.ReferenceAssemblyStatus)
    {
    }

    public static UnsafeMemberCensusProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override UnsafeMemberCensusBodyFact Visit(
        scoped MethodDefinitionView view)
    {
        int token = view.Token;
        MetadataReader reader = view.Reader;
        MethodDefinition definition = view.MethodDefinition;
        ExtensionDeclarationShape extensionShape =
            LibraryMethodAnalysisRunner.ClassifyExtensionDeclaration(
                reader,
                view.TypeDefinition,
                definition);
        MethodBodyAvailability availability =
            LibraryMethodAnalysisRunner.ClassifyBodyAvailability(definition);
        MethodIdentity identity;
        bool requiresOwnerByShape;
        try
        {
            switch (view.SemanticIdentity)
            {
                case MethodSemanticIdentityResult.Decoded decoded:
                    identity = Compose(view, decoded.Identity);
                    break;
                case MethodSemanticIdentityResult.SignatureBoundExceeded bounded:
                    identity = Compose(view, bounded.Identity);
                    break;
                case MethodSemanticIdentityResult.Malformed malformed:
                    return Failed(token, body: null, malformed.Reason);
                default:
                    throw new InvalidOperationException(
                        "Unknown semantic-identity outcome.");
            }

            requiresOwnerByShape =
                LibraryMethodAnalysisRunner
                    .IsDeclaredInNestedCompilerGeneratedType(
                        reader,
                        view.TypeDefinition)
                || extensionShape == ExtensionDeclarationShape.Unconfirmed;
        }
        catch (Exception exception)
            when (IsRecoverable(exception))
        {
            return Failed(token, body: null, Describe(exception));
        }

        ImmutableArray<UnsafeMemberUseEvidence> evidence;
        try
        {
            evidence =
                availability == MethodBodyAvailability.Present
                && view.HasManagedBody
                    ? BodyEvidence(view, identity)
                    : UnsafeMemberUseRules
                        .CollectEvidence(
                            identity.CallerUnsafeMode,
                            [],
                            [],
                            []);
        }
        catch (Exception exception)
            when (IsRecoverable(exception))
        {
            return new(
                Facts(
                    token,
                    identity,
                    extensionShape,
                    availability,
                    failureDetail: Describe(exception),
                    evidence: []),
                requiresOwnerByShape);
        }

        return new(
            Facts(
                token,
                identity,
                extensionShape,
                availability,
                failureDetail: null,
                evidence),
            requiresOwnerByShape);
    }

    internal override List<UnsafeMemberCensusBodyFact> Seed() => [];

    internal override List<UnsafeMemberCensusBodyFact> Accumulate(
        List<UnsafeMemberCensusBodyFact> accumulator,
        UnsafeMemberCensusBodyFact fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override UnsafeMemberCensus Complete(
        List<UnsafeMemberCensusBodyFact> accumulator,
        MethodDefinitionCompletionView completion)
    {
        var identities = new Dictionary<int, MethodIdentity>();
        foreach (UnsafeMemberCensusBodyFact fact in accumulator)
        {
            if (fact.Facts.Body is { } identity)
                identities[fact.Facts.Token] = identity;
        }

        // An owner the source did not visit has no identity to join, so its
        // body stays unattributed rather than guessed.
        var bodies = new List<UnsafeMemberBodyFacts>(accumulator.Count);
        foreach (UnsafeMemberCensusBodyFact fact in accumulator)
        {
            if (fact.Facts.Body is not { } identity)
            {
                bodies.Add(fact.Facts with
                {
                    OwnerResolution = DeclaredOwnerResolution.Unresolved,
                });
                continue;
            }

            MethodDefinitionDeclaredSource source =
                completion.DeclaredSourceOf(
                    (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                        fact.Facts.Token));

            // A body needs an authenticated owner when the relation examined
            // it as generated, when its name is a lifted or state-machine
            // body, when its type is a nested compiler-generated type, or
            // when it is an unconfirmed extension declaration. Only the
            // relation supplies the owner.
            bool requiresOwner =
                fact.RequiresOwnerByShape
                || source is not MethodDefinitionDeclaredSource.Direct
                || CompilerGeneratedNames.RequiresDeclaredOwner(
                    identity,
                    source is MethodDefinitionDeclaredSource.Generated
                    {
                        Origin.Kind:
                            MethodDefinitionGeneratedExpansionOriginKind
                                .StateMachineExecutionBody,
                    });
            MethodIdentity? owner =
                source is MethodDefinitionDeclaredSource.Generated generated
                && identities.TryGetValue(
                    MetadataTokens.GetToken(generated.Origin.DeclaredOwner),
                    out MethodIdentity? found)
                    ? found
                    : null;
            bodies.Add(fact.Facts with
            {
                RequiresDeclaredOwner = requiresOwner,
                OwnerResolution = owner is null
                    ? DeclaredOwnerResolution.Unresolved
                    : DeclaredOwnerResolution.Resolved,
                DeclaredOwner = owner,
            });
        }

        return UnsafeMemberCensusBuilder.Build(
            bodies,
            hasFullMethodEvidenceScope: true,
            completion.GetReferenceAssemblyStatus(),
            RootSet(completion.GetPublicMethodRoots()),
            attributionBounded: completion.IsSourceBounded);
    }

    static MethodIdentity Compose(
        scoped MethodDefinitionView view,
        MethodSemanticIdentity identity) =>
        identity.ToMethodIdentity(
            CallerUnsafeModeFacts.FromContract(
                view.GetMemorySafetyContract(view.MethodHandle)));

    // The inventory roles of one present body, read only through the
    // declared Body, Calls, and memory-safety capabilities.
    static ImmutableArray<UnsafeMemberUseEvidence> BodyEvidence(
        scoped MethodDefinitionView view,
        MethodIdentity identity)
    {
        MethodInstructions instructions =
            view.MaterializeInstructions(out MethodBodyData bodyData);
        MethodCallSites calls = view.GetCalls(instructions);
        MethodLocalSignature locals = view.GetLocalSignature();
        MethodBodyAnalysisContext context =
            MethodBodyAnalysisContext.Create(
                identity,
                bodyData,
                instructions,
                locals.Types,
                locals.DeclaredCount,
                locals.IncompleteReason,
                view.GetBody().LocalVariablesInitialized);

        var callees = new Dictionary<int, MemberRef>();
        foreach (MethodCallSite site in calls.Sites)
            callees.TryAdd(site.OperandToken, site.Callee);
        MemberRef ResolveMember(int operandToken) =>
            callees.TryGetValue(operandToken, out MemberRef? callee)
                ? callee
                : throw new BadImageFormatException(
                    $"Operand 0x{operandToken:X8} is not a resolved call site.");

        ImmutableArray<UnsafetyOccurrence> unsafety =
            MethodSafetyAnalysis.CollectOccurrences(
                context,
                operandToken =>
                    callees.TryGetValue(operandToken, out MemberRef? signature)
                        ? signature.ReturnType.ToDisplayString()
                        : null,
                ResolveMember);

        // Field operands are resolved only for the ldsflda sites the
        // constant-data recognizer asks about.
        var rvaFields = new Dictionary<int, bool>();
        foreach (DecodedInstruction instruction in instructions.Instructions)
        {
            if (instruction.OpCode != ILOpCode.Ldsflda)
                continue;
            int operandToken =
                MethodInstructionFacts.OperandInt32(instruction);
            if (rvaFields.ContainsKey(operandToken))
                continue;
            rvaFields[operandToken] =
                (operandToken >> 24) == 0x04
                && view.ResolveFieldOperand(operandToken)
                    is MethodFieldOperand.Resolved
                    {
                        CurrentModuleDefinitionToken: not null,
                        HasRva: true,
                    };
        }
        ImmutableHashSet<int> constantDataSpans =
            SpanStackAllocations.RecognizeConstantDataSpans(
                context,
                ResolveMember,
                operandToken =>
                    rvaFields.TryGetValue(operandToken, out bool hasRva)
                    && hasRva);

        bool updatedRules =
            view.GetMemorySafetyRules()
                is MemorySafetyRulesResult.Available
                {
                    State: MemorySafetyRulesState.Updated,
                };
        var explicitCalls =
            new List<(int ILOffset, MemberRef Callee, UnsafeContractSource Source)>();
        foreach (MethodCallSite site in calls.Sites)
        {
            if (site.Kind is not (
                    CallKind.Call
                    or CallKind.CallVirtual
                    or CallKind.NewObject))
            {
                continue;
            }

            CallerUnsafeMode? targetMode =
                site.Target is MethodCallTarget.CurrentModule target
                    ? CallerUnsafeModeFacts.FromContract(
                        view.GetMemorySafetyContract(target.Method))
                    : null;
            UnsafeContractSource? contractSource =
                UnsafeMemberUseRules.ExplicitCallContractSource(
                    site.Kind,
                    site.Callee,
                    targetMode,
                    identity,
                    updatedRules);
            if (contractSource is not null)
                explicitCalls.Add((site.ILOffset, site.Callee, contractSource));
        }

        return UnsafeMemberUseRules.CollectEvidence(
            identity.CallerUnsafeMode,
            unsafety,
            constantDataSpans,
            explicitCalls);
    }

    static UnsafeMemberRootSet RootSet(PublicMethodRootsResult roots) =>
        roots switch
        {
            PublicMethodRootsResult.Read read => new UnsafeMemberRootSet(
                read.Inventory.Roots
                    .Select(static root => root.Token)
                    .ToHashSet(),
                read.Inventory.IsComplete,
                Failed: false),
            _ => UnsafeMemberRootSet.Unavailable,
        };

    static UnsafeMemberBodyFacts Facts(
        int token,
        MethodIdentity identity,
        ExtensionDeclarationShape extensionShape,
        MethodBodyAvailability availability,
        string? failureDetail,
        ImmutableArray<UnsafeMemberUseEvidence> evidence) =>
        new(
            token,
            identity,
            InScope: true,
            extensionShape == ExtensionDeclarationShape.Skeleton,
            availability,
            AnalysisFailed: failureDetail is not null,
            failureDetail,
            RequiresDeclaredOwner: false,
            DeclaredOwnerResolution.None,
            OwnerResolutionFailed: false,
            DeclaredOwner: null,
            evidence);

    static UnsafeMemberCensusBodyFact Failed(
        int token,
        MethodIdentity? body,
        string detail) =>
        new(
            new UnsafeMemberBodyFacts(
                token,
                body,
                InScope: true,
                IsExtensionDeclarationSkeleton: false,
                MethodBodyAvailability.Present,
                AnalysisFailed: true,
                detail,
                RequiresDeclaredOwner: false,
                DeclaredOwnerResolution.None,
                OwnerResolutionFailed: false,
                DeclaredOwner: null,
                Evidence: []),
            requiresOwnerByShape: false);

    static bool IsRecoverable(Exception exception) =>
        exception is not MethodDefinitionTerminalWorkLimitExceededException
        && LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(exception);

    static string Describe(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";
}

/// <summary>
/// Owner-issued requests for the unsafe member census over Assembly Analysis
/// Operation, and the projection of its detached result.
/// </summary>
public static class UnsafeMemberCensusAnalysis
{
    /// <summary>The census with a Complete terminal.</summary>
    public static ProducerRequest Request { get; } =
        new(UnsafeMemberCensusProducer.Instance);

    /// <summary>The work description, computed without reading any subject.</summary>
    public static WorkDescription Description { get; } =
        ProducerPlanner.Plan([Request]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException(
                "The unsafe member census request must plan.");

    /// <summary>
    /// Type-scope breadth: the selected types' methods with their generated
    /// bodies in every role and the unauthenticated candidates the expansion
    /// reaches, which are visited without an owner.
    /// </summary>
    public static MethodDefinitionSourceBreadth TypeScope(
        IEnumerable<TypeDefinitionHandle> types,
        MethodDefinitionGeneratedExpansionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        return MethodDefinitionSourceBreadth
            .ExactTypes(types.ToImmutableArray())
            .IncludeGeneratedExecutionBodies(
                limits,
                MethodDefinitionGeneratedBodyRoles.All,
                includeUnauthenticatedCandidates: true);
    }

    /// <summary>The census request over the selected types.</summary>
    public static MethodDefinitionSourceRequest<UnsafeMemberCensus>
        ForTypes(
            IEnumerable<TypeDefinitionHandle> types,
            MethodDefinitionGeneratedExpansionLimits? limits = null) =>
        MethodDefinitionSourceRequest<UnsafeMemberCensus>.Create(
            Description,
            UnsafeMemberCensusProducer.Instance,
            TypeScope(types, limits));

    /// <summary>
    /// Library-scope breadth: every method definition, with the declared-source
    /// relation in every role settled over the whole module. Every physical
    /// body is visited, so a generated body no source member claims is still
    /// inspected and needs no unauthenticated candidates.
    /// </summary>
    public static MethodDefinitionSourceBreadth LibraryScope(
        MethodDefinitionGeneratedExpansionLimits? limits = null) =>
        MethodDefinitionSourceBreadth.AllDefinitions
            .IncludeGeneratedExecutionBodies(
                limits,
                MethodDefinitionGeneratedBodyRoles.All);

    /// <summary>The census request over the whole library.</summary>
    public static MethodDefinitionSourceRequest<UnsafeMemberCensus>
        ForLibrary(
            MethodDefinitionGeneratedExpansionLimits? limits = null) =>
        MethodDefinitionSourceRequest<UnsafeMemberCensus>.Create(
            Description,
            UnsafeMemberCensusProducer.Instance,
            LibraryScope(limits));

    /// <summary>Projects one detached census operation with its receipts.</summary>
    public static UnsafeMemberCensusResult Project(
        AssemblyAnalysisExecution<UnsafeMemberCensus> execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ProducerResult<UnsafeMemberCensus> result =
            execution.ResultOf(UnsafeMemberCensusProducer.Instance);
        MethodDefinitionSourceReceipt source =
            execution.MethodDefinitionReceipt();
        WorkReceipt receipt = execution.WorkReceipt;
        // A bounded expansion's census keeps the retained prefix's findings;
        // its attribution-bound limitation makes it incomplete.
        if ((result.HasValue
                || result.Outcome == ProducerOutcome.SourceBounded)
            && result.Value is { } census)
        {
            return new UnsafeMemberCensusResult.Available(census, source, receipt);
        }

        string unit = result.Failure?.Unit ?? result.Critical?.Unit ?? "(unknown)";
        string reason = result.Failure?.Message
            ?? result.Critical?.Message
            ?? result.FailedPrerequisite
            ?? "";
        return new UnsafeMemberCensusResult.Failed(
            new InvalidDataException(
                "The unsafe member census failed because "
                + $"{unit} could not be analyzed: {reason}"),
            result.Outcome,
            source,
            receipt);
    }
}

/// <summary>A detached census, or the inspection failure that prevented it.</summary>
public abstract record UnsafeMemberCensusResult
{
    private UnsafeMemberCensusResult()
    {
    }

    /// <summary>The census, complete or incomplete with its limitations.</summary>
    public sealed record Available(
        UnsafeMemberCensus Census,
        MethodDefinitionSourceReceipt SourceReceipt,
        WorkReceipt Receipt)
        : UnsafeMemberCensusResult;

    /// <summary>An inspection failure, not an observation.</summary>
    public sealed record Failed(
        InvalidDataException Error,
        ProducerOutcome Outcome,
        MethodDefinitionSourceReceipt SourceReceipt,
        WorkReceipt Receipt)
        : UnsafeMemberCensusResult;
}
