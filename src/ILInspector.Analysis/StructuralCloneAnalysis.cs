using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

/// <summary>The execution disposition of one structural-clone comparison.</summary>
public enum StructuralCloneDisposition
{
    Completed,
    Unsupported,
    LimitReached,
    Failed,
}

/// <summary>The structural relationship measured by a completed comparison.</summary>
public enum StructuralCloneRelation
{
    Exact,
    Near,
    Different,
}

/// <summary>Whether exact correspondence is unique under normalized graph structure.</summary>
public enum StructuralCloneCorrespondenceKind
{
    Unique,
    Ambiguous,
}

/// <summary>The side of a comparison to which a blocker applies.</summary>
public enum StructuralCloneSide
{
    Left,
    Right,
    Both,
}

/// <summary>Typed reasons why a comparison did not complete.</summary>
public enum StructuralCloneBlockerKind
{
    NoMethodBody,
    UnsupportedMethodImplementation,
    ExceptionHandling,
    UnsupportedMethodSignature,
    UnsupportedLocalSignature,
    ExternalControlFlow,
    IncompleteBody,
    InvalidLocalSlot,
    InvalidArgumentSlot,
    InvalidMetadataOperand,
    TerminalFallThrough,
    BodySizeLimit,
    InstructionLimit,
    BlockLimit,
    EdgeLimit,
    LocalLimit,
    VerificationStepLimit,
    MetadataReadFailure,
}

/// <summary>One visible unsupported, limit, or failure receipt.</summary>
public sealed record StructuralCloneBlocker(
    StructuralCloneBlockerKind Kind,
    StructuralCloneSide Side,
    string Detail);

/// <summary>
/// One left block and the right blocks in its final normalized refinement class.
/// A multi-member class is a conservative over-approximation of an automorphism
/// orbit; its members are not independently selectable correspondences.
/// </summary>
public sealed record StructuralCloneBlockClass(
    int LeftBlock,
    ImmutableArray<int> RightBlocks);

/// <summary>
/// One left local and the right locals in its final normalized refinement class.
/// A multi-member class is a conservative over-approximation of an automorphism
/// orbit; its members are not independently selectable correspondences.
/// </summary>
public sealed record StructuralCloneLocalClass(
    int LeftLocal,
    ImmutableArray<int> RightLocals);

/// <summary>Owner-issued block and local correspondence for an exact body pair.</summary>
public sealed record StructuralCloneCorrespondence(
    StructuralCloneCorrespondenceKind Kind,
    ImmutableArray<StructuralCloneBlockClass> Blocks,
    ImmutableArray<StructuralCloneLocalClass> Locals);

/// <summary>Bounded-work receipt for one comparison.</summary>
public sealed record StructuralCloneVerificationReceipt(
    int LeftBodyBytes,
    int RightBodyBytes,
    int LeftInstructions,
    int RightInstructions,
    int LeftBlocks,
    int RightBlocks,
    int LeftEdges,
    int RightEdges,
    int LeftLocals,
    int RightLocals,
    int RefinementRounds,
    int SearchSteps,
    bool SearchExhausted,
    bool WitnessFound);

/// <summary>Resource limits for exact structural comparison.</summary>
public sealed record StructuralCloneComparisonLimits(
    int MaximumInstructions = 10_000,
    int MaximumBlocks = 128,
    int MaximumEdges = 100_000,
    int MaximumLocals = 256,
    int MaximumVerificationSteps = 100_000,
    int MaximumBodyBytes = 1_000_000);

/// <summary>
/// Product-owned result for one A-vs-A structural body comparison.
/// Disposition and relation are orthogonal: relation is present only when the
/// comparison completed.
/// </summary>
public sealed record StructuralCloneComparison
{
    StructuralCloneComparison(
        MetadataMethodAddress left,
        MetadataMethodAddress right,
        StructuralCloneDisposition disposition,
        StructuralCloneRelation? relation,
        StructuralCloneCorrespondence? correspondence,
        ImmutableArray<StructuralCloneBlocker> blockers,
        StructuralCloneVerificationReceipt receipt)
    {
        if (disposition == StructuralCloneDisposition.Completed
            && relation is null)
        {
            throw new ArgumentException(
                "A completed structural clone comparison requires a relation.",
                nameof(relation));
        }
        if (disposition != StructuralCloneDisposition.Completed
            && relation is not null)
        {
            throw new ArgumentException(
                "A non-completed structural clone comparison cannot carry a relation.",
                nameof(relation));
        }
        if (relation == StructuralCloneRelation.Exact
            && correspondence is null)
        {
            throw new ArgumentException(
                "An exact structural clone comparison requires correspondence.",
                nameof(correspondence));
        }
        if (relation != StructuralCloneRelation.Exact
            && correspondence is not null)
        {
            throw new ArgumentException(
                "Only an exact structural clone comparison carries correspondence.",
                nameof(correspondence));
        }
        if (disposition == StructuralCloneDisposition.Completed
            && !blockers.IsEmpty)
        {
            throw new ArgumentException(
                "A completed structural clone comparison cannot carry blockers.",
                nameof(blockers));
        }
        if (disposition != StructuralCloneDisposition.Completed
            && blockers.IsEmpty)
        {
            throw new ArgumentException(
                "A non-completed structural clone comparison requires a blocker.",
                nameof(blockers));
        }

        Left = left;
        Right = right;
        Disposition = disposition;
        Relation = relation;
        Correspondence = correspondence;
        Blockers = blockers;
        Receipt = receipt;
    }

    public MetadataMethodAddress Left { get; }
    public MetadataMethodAddress Right { get; }
    public StructuralCloneDisposition Disposition { get; }
    public StructuralCloneRelation? Relation { get; }
    public StructuralCloneCorrespondence? Correspondence { get; }
    public ImmutableArray<StructuralCloneBlocker> Blockers { get; }
    public StructuralCloneVerificationReceipt Receipt { get; }

    internal static StructuralCloneComparison Completed(
        MetadataMethodAddress left,
        MetadataMethodAddress right,
        StructuralCloneRelation relation,
        StructuralCloneCorrespondence? correspondence,
        StructuralCloneVerificationReceipt receipt)
        => new(
            left,
            right,
            StructuralCloneDisposition.Completed,
            relation,
            correspondence,
            [],
            receipt);

    internal static StructuralCloneComparison NotCompleted(
        MetadataMethodAddress left,
        MetadataMethodAddress right,
        StructuralCloneDisposition disposition,
        ImmutableArray<StructuralCloneBlocker> blockers,
        StructuralCloneVerificationReceipt receipt)
        => new(left, right, disposition, null, null, blockers, receipt);
}

/// <summary>
/// Exact normalized structural comparison over two method bodies in one
/// retained PE image.
/// </summary>
/// <remarks>
/// <para>
/// The first slice is deliberately A-vs-A. Metadata operands retain their
/// reader-local handle identity; A-vs-B requires a separate cross-reader
/// correspondence owner.
/// </para>
/// <para>
/// Exactness covers normalized opcode encodings, argument positions, explicit
/// local bijection and local types, <c>InitLocals</c> when locals exist,
/// constants, metadata operands, branch roles, and switch-target order. Method
/// parameter/return types and declared <c>MaxStack</c> are outside this body
/// relation. The method-signature calling convention, instance/static shape,
/// generic arity, argument count, and void/value return shape remain exact
/// preconditions.
/// Nops and redundant branches are retained rather than normalized away.
/// </para>
/// </remarks>
public static class StructuralCloneAnalysis
{
    const byte HasThisSignatureFlag = 0x20;
    const byte ExplicitThisSignatureFlag = 0x40;

    /// <summary>Compares two method definitions from one retained managed PE image.</summary>
    public static StructuralCloneComparison Compare(
        PEReader image,
        MethodDefinitionHandle left,
        MethodDefinitionHandle right,
        StructuralCloneComparisonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.HasMetadata)
            throw new ArgumentException(
                "Structural clone comparison requires a managed metadata image.",
                nameof(image));

        MetadataReader reader = image.GetMetadataReader();
        ValidateHandle(reader, left, nameof(left));
        ValidateHandle(reader, right, nameof(right));
        ValidateLimits(limits ??= new StructuralCloneComparisonLimits());

        MetadataMethodAddress leftAddress =
            MetadataMethodAddress.Create(reader, left);
        MetadataMethodAddress rightAddress =
            MetadataMethodAddress.Create(reader, right);
        BodyProduction leftBody =
            Produce(image, reader, leftAddress, StructuralCloneSide.Left, limits);
        BodyProduction rightBody =
            Produce(image, reader, rightAddress, StructuralCloneSide.Right, limits);

        if (leftBody.Disposition != StructuralCloneDisposition.Completed
            || rightBody.Disposition != StructuralCloneDisposition.Completed)
        {
            StructuralCloneDisposition disposition = MoreSevere(
                leftBody.Disposition,
                rightBody.Disposition);
            return StructuralCloneComparison.NotCompleted(
                leftAddress,
                rightAddress,
                disposition,
                [.. leftBody.Blockers, .. rightBody.Blockers],
                Receipt(leftBody, rightBody, 0, 0, false, false));
        }

        return Compare(leftBody.Facts!, rightBody.Facts!, limits);
    }

    internal static StructuralCloneComparison Compare(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        StructuralCloneComparisonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ValidateLimits(limits ??= new StructuralCloneComparisonLimits());

        ImmutableArray<StructuralCloneBlocker> limitBlockers =
            LimitsFor(left, right, limits);
        if (!limitBlockers.IsEmpty)
        {
            return StructuralCloneComparison.NotCompleted(
                left.Method,
                right.Method,
                StructuralCloneDisposition.LimitReached,
                limitBlockers,
                Receipt(left, right, 0, 0, false, false));
        }

        if (left.Signature != right.Signature
            || left.InitLocals != right.InitLocals
            || left.Locals.Length != right.Locals.Length
            || left.Graph.Blocks.Length != right.Graph.Blocks.Length)
        {
            return StructuralCloneComparison.Completed(
                left.Method,
                right.Method,
                StructuralCloneRelation.Different,
                correspondence: null,
                Receipt(left, right, 0, 0, true, false));
        }

        RefinedColors colors = Refine(left, right);
        WitnessResult witness =
            FindWitness(left, right, colors, limits.MaximumVerificationSteps);
        if (witness.LimitReached)
        {
            return StructuralCloneComparison.NotCompleted(
                left.Method,
                right.Method,
                StructuralCloneDisposition.LimitReached,
                [
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.VerificationStepLimit,
                        StructuralCloneSide.Both,
                        $"Exact witness search exceeded {limits.MaximumVerificationSteps} steps."),
                ],
                Receipt(
                    left,
                    right,
                    colors.Rounds,
                    witness.Steps,
                    false,
                    false));
        }
        if (!witness.Found)
        {
            return StructuralCloneComparison.Completed(
                left.Method,
                right.Method,
                StructuralCloneRelation.Different,
                correspondence: null,
                Receipt(
                    left,
                    right,
                    colors.Rounds,
                    witness.Steps,
                    true,
                    false));
        }

        StructuralCloneCorrespondence correspondence =
            BuildCorrespondence(left, right, colors);
        return StructuralCloneComparison.Completed(
            left.Method,
            right.Method,
            StructuralCloneRelation.Exact,
            correspondence,
            Receipt(
                left,
                right,
                colors.Rounds,
                witness.Steps,
                false,
                true));
    }

    internal static BodyProduction Produce(
        MetadataMethodAddress method,
        MethodInstructions instructions,
        ImmutableArray<TypeRef> locals,
        bool initLocals,
        StructuralCloneMethodSignature signature,
        StructuralCloneComparisonLimits? limits = null,
        StructuralCloneSide side = StructuralCloneSide.Left,
        int? bodyBytes = null)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ValidateLimits(limits ??= new StructuralCloneComparisonLimits());
        BodyMeasurements measurements = BodyMeasurements.From(
            instructions,
            locals.Length,
            bodyBytes);
        if (instructions.Blocks.Blocks.Any(static block =>
            block.Edges.ExternalTargets.Count > 0
                || block.Edges.LeavesRegion))
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Unsupported,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.ExternalControlFlow,
                    side,
                    "The block graph contains external or region-leaving control flow."),
                measurements);
        }
        if (!instructions.IsComplete)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Failed,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.IncompleteBody,
                    side,
                    instructions.Blocks.IncompleteReason
                        ?? "Instruction decode did not complete."),
                measurements);
        }
        if (instructions.Instructions.IsEmpty
            || instructions.Instructions[^1].FallsThrough)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Failed,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.TerminalFallThrough,
                    side,
                    "The method body is empty or its final instruction falls through past the body."),
                measurements);
        }
        if (!instructions.Blocks.Regions.IsEmpty)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Unsupported,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.ExceptionHandling,
                    side,
                    "Exception-handling bodies are outside the first-slice contract."),
                measurements);
        }
        if (locals.Any(static local => !SupportedType(local)))
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Unsupported,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.UnsupportedLocalSignature,
                    side,
                    "The local signature contains an unsupported type shape."),
                measurements);
        }

        ImmutableArray<StructuralCloneBlocker> limitBlockers =
            LimitsFor(instructions, locals.Length, limits, side);
        if (!limitBlockers.IsEmpty)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.LimitReached,
                limitBlockers,
                measurements);
        }
        try
        {
            StructuralCloneGraph graph =
                BuildGraph(instructions, locals, signature, side);
            return BodyProduction.Completed(
                new StructuralCloneBodyFacts(
                    method,
                    measurements.BodyBytes,
                    instructions.Instructions.Length,
                    initLocals && locals.Length > 0,
                    [
                        .. locals.Select(
                            StructuralCloneTypeIdentity.Create),
                    ],
                    signature,
                    graph));
        }
        catch (InvalidLocalSlotException ex)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Failed,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.InvalidLocalSlot,
                    side,
                    ex.Message),
                measurements);
        }
        catch (InvalidArgumentSlotException ex)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Failed,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.InvalidArgumentSlot,
                    side,
                    ex.Message),
                measurements);
        }
        catch (InvalidOperationException ex)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Failed,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.IncompleteBody,
                    side,
                    ex.Message),
                measurements);
        }
    }

    static BodyProduction Produce(
        PEReader image,
        MetadataReader reader,
        MetadataMethodAddress method,
        StructuralCloneSide side,
        StructuralCloneComparisonLimits limits)
    {
        BodyMeasurements measurements = default;
        try
        {
            MethodDefinition definition =
                reader.GetMethodDefinition(method.Handle);
            MethodImplAttributes implementation =
                definition.ImplAttributes;
            MethodAttributes attributes = definition.Attributes;
            if ((implementation & MethodImplAttributes.CodeTypeMask)
                    != MethodImplAttributes.IL
                || (implementation & MethodImplAttributes.ManagedMask)
                    != MethodImplAttributes.Managed
                || (implementation
                    & (MethodImplAttributes.ForwardRef
                        | MethodImplAttributes.InternalCall)) != 0
                || (attributes
                    & (MethodAttributes.Abstract
                        | MethodAttributes.PinvokeImpl)) != 0)
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.Unsupported,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.UnsupportedMethodImplementation,
                        side,
                        $"Method attributes {attributes} and implementation flags "
                        + $"{implementation} do not describe a managed IL body."));
            }
            if (definition.RelativeVirtualAddress == 0)
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.Unsupported,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.NoMethodBody,
                        side,
                        "The method definition has no IL body."));
            }
            BlobReader methodSignatureReader =
                reader.GetBlobReader(definition.Signature);
            SignatureHeader methodSignatureHeader =
                methodSignatureReader.ReadSignatureHeader();
            if (methodSignatureHeader.Kind != SignatureKind.Method
                || methodSignatureHeader.CallingConvention
                    is not (
                        SignatureCallingConvention.Default
                        or SignatureCallingConvention.VarArgs))
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.Failed,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.MetadataReadFailure,
                        side,
                        "A method definition does not have a valid "
                        + "MethodDef signature header."));
            }
            if (!SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    definition.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                bool malformed = IsMalformedUnsafeSignature(
                    reader,
                    definition.Signature,
                    SignatureBlobGuard.Kind.Method);
                return BodyProduction.NotCompleted(
                    malformed
                        ? StructuralCloneDisposition.Failed
                        : StructuralCloneDisposition.Unsupported,
                    new StructuralCloneBlocker(
                        malformed
                            ? StructuralCloneBlockerKind.MetadataReadFailure
                            : StructuralCloneBlockerKind.UnsupportedMethodSignature,
                        side,
                        malformed
                            ? "The method signature grammar is invalid."
                            : "The method signature exceeds the guarded decode policy."));
            }

            MethodSignature<StructuralCloneSignatureType> decodedSignature =
                definition.DecodeSignature(
                    new StructuralCloneSignatureTypeProvider(),
                    GenericScope.Empty);
            if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                    reader,
                    definition.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.Failed,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.MetadataReadFailure,
                        side,
                        "The method signature is incomplete or has trailing data."));
            }
            if (HasInvalidMethodTypePosition(decodedSignature)
                || decodedSignature.RequiredParameterCount
                    != decodedSignature.ParameterTypes.Length)
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.Failed,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.MetadataReadFailure,
                        side,
                        "The method definition signature contains a type or "
                        + "sentinel that is invalid in its position."));
            }
            if (!ValidSignatureTypes(decodedSignature))
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.Unsupported,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.UnsupportedMethodSignature,
                        side,
                        "The method signature contains a nested type shape "
                        + "that exceeds the guarded decode policy."));
            }
            StructuralCloneMethodSignature signature = new(
                decodedSignature.Header.RawValue,
                decodedSignature.GenericParameterCount,
                decodedSignature.RequiredParameterCount,
                decodedSignature.ParameterTypes.Length,
                decodedSignature.ReturnType.IsVoid);

            MethodBodyBlock body =
                image.GetMethodBody(definition.RelativeVirtualAddress);
            if (!body.ExceptionRegions.IsEmpty)
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.Unsupported,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.ExceptionHandling,
                        side,
                        "Exception-handling bodies are outside the first-slice contract."));
            }
            int bodyBytes = body.GetILReader().Length;
            measurements = new BodyMeasurements(
                bodyBytes,
                InstructionCount: 0,
                BlockCount: 0,
                EdgeCount: 0,
                LocalCount: 0);
            if (bodyBytes > limits.MaximumBodyBytes)
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.LimitReached,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.BodySizeLimit,
                        side,
                        $"IL body size {bodyBytes} bytes exceeds "
                        + $"{limits.MaximumBodyBytes}."),
                    measurements);
            }

            ImmutableArray<TypeRef> locals = [];
            if (!body.LocalSignature.IsNil)
            {
                StandaloneSignature localSignature =
                    reader.GetStandaloneSignature(body.LocalSignature);
                int localCount = ReadLocalCount(
                    reader,
                    localSignature.Signature);
                measurements = measurements with
                {
                    LocalCount = localCount,
                };
                if (localCount > limits.MaximumLocals)
                {
                    return BodyProduction.NotCompleted(
                        StructuralCloneDisposition.LimitReached,
                        new StructuralCloneBlocker(
                            StructuralCloneBlockerKind.LocalLimit,
                            side,
                            $"Local count {localCount} exceeds "
                            + $"{limits.MaximumLocals}."),
                        measurements);
                }
                if (!SignatureBlobGuard.IsSafeToDecode(
                        reader,
                        localSignature.Signature,
                        SignatureBlobGuard.Kind.LocalVariables))
                {
                    bool malformed = IsMalformedUnsafeSignature(
                        reader,
                        localSignature.Signature,
                        SignatureBlobGuard.Kind.LocalVariables);
                    return BodyProduction.NotCompleted(
                        malformed
                            ? StructuralCloneDisposition.Failed
                            : StructuralCloneDisposition.Unsupported,
                        new StructuralCloneBlocker(
                            malformed
                                ? StructuralCloneBlockerKind.MetadataReadFailure
                                : StructuralCloneBlockerKind.UnsupportedLocalSignature,
                            side,
                            malformed
                                ? "The local signature grammar is invalid."
                                : "The local signature exceeds the guarded "
                                    + "decode policy."),
                        measurements);
                }
                if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                        reader,
                        localSignature.Signature,
                        SignatureBlobGuard.Kind.LocalVariables))
                {
                    return BodyProduction.NotCompleted(
                        StructuralCloneDisposition.Failed,
                        new StructuralCloneBlocker(
                            StructuralCloneBlockerKind.MetadataReadFailure,
                            side,
                            "The local signature is incomplete or has "
                            + "trailing data."),
                        measurements);
                }
                ImmutableArray<StructuralCloneSignatureType> localShapes =
                    localSignature.DecodeLocalSignature(
                        new StructuralCloneSignatureTypeProvider(),
                        GenericScope.Empty);
                bool hasVoid = false;
                bool hasUnsupportedShape = false;
                foreach (StructuralCloneSignatureType type in localShapes)
                {
                    hasVoid |= type.IsVoid;
                    hasUnsupportedShape |= !type.IsValid;
                }
                if (hasVoid)
                {
                    return BodyProduction.NotCompleted(
                        StructuralCloneDisposition.Failed,
                        new StructuralCloneBlocker(
                            StructuralCloneBlockerKind.MetadataReadFailure,
                            side,
                            "A local variable type cannot be void."),
                        measurements);
                }
                if (hasUnsupportedShape)
                {
                    return BodyProduction.NotCompleted(
                        StructuralCloneDisposition.Unsupported,
                        new StructuralCloneBlocker(
                            StructuralCloneBlockerKind.UnsupportedLocalSignature,
                            side,
                            "The local signature contains a nested type shape "
                            + "that exceeds the guarded decode policy."),
                        measurements);
                }
                // Bind each recursive provider decode to its own prescan.
                if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                        reader,
                        localSignature.Signature,
                        SignatureBlobGuard.Kind.LocalVariables))
                {
                    throw new BadImageFormatException(
                        "The local signature changed between guarded decodes.");
                }
                locals = localSignature.DecodeLocalSignature(
                    TypeRefDecoder.Instance,
                    GenericScope.Empty);
            }

            MethodInstructions instructions = MethodInstructions.Decode(body);
            measurements = BodyMeasurements.From(
                instructions,
                locals.Length,
                bodyBytes);
            if (instructions.Instructions.Length
                > limits.MaximumInstructions)
            {
                return BodyProduction.NotCompleted(
                    StructuralCloneDisposition.LimitReached,
                    InstructionLimitBlocker(
                        instructions.Instructions.Length,
                        limits.MaximumInstructions,
                        side),
                    measurements);
            }
            if (InvalidMetadataOperand(
                    reader,
                    instructions,
                    side)
                is { } operandFailure)
            {
                return BodyProduction.NotCompleted(
                    operandFailure.Disposition,
                    operandFailure.Blocker,
                    measurements);
            }
            return Produce(
                method,
                instructions,
                locals,
                body.LocalVariablesInitialized,
                signature,
                limits,
                side,
                bodyBytes);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return BodyProduction.NotCompleted(
                StructuralCloneDisposition.Failed,
                new StructuralCloneBlocker(
                    StructuralCloneBlockerKind.MetadataReadFailure,
                    side,
                    $"{ex.GetType().Name}: {ex.Message}"),
                measurements);
        }
    }

    static StructuralCloneGraph BuildGraph(
        MethodInstructions body,
        ImmutableArray<TypeRef> locals,
        StructuralCloneMethodSignature signature,
        StructuralCloneSide side)
    {
        ImmutableArray<DecodedInstruction> instructions = body.Instructions;
        var blockOperations =
            new ImmutableArray<StructuralCloneOperation>.Builder[
                body.Blocks.Blocks.Length];
        var terminators =
            new DecodedInstruction?[body.Blocks.Blocks.Length];
        for (int index = 0; index < blockOperations.Length; index++)
        {
            blockOperations[index] =
                ImmutableArray.CreateBuilder<StructuralCloneOperation>();
        }

        foreach (DecodedInstruction instruction in instructions)
        {
            int block = body.BlockIndexAt(instruction.Offset);
            if (block < 0)
            {
                throw new InvalidOperationException(
                    $"Instruction IL_{instruction.Offset:X4} is not owned by a block.");
            }
            StructuralCloneOperation operation =
                NormalizeOperation(instruction);
            if (operation.OperandKind == StructuralCloneOperandKind.Local
                && (uint)operation.Value >= (uint)locals.Length)
            {
                throw new InvalidLocalSlotException(
                    $"The {side.ToString().ToLowerInvariant()} body references local "
                    + $"{operation.Value}, but its local signature has {locals.Length} entries.");
            }
            if (operation.OperandKind == StructuralCloneOperandKind.Argument)
            {
                int argumentSlots =
                    signature.ParameterCount
                    + ((signature.Header & HasThisSignatureFlag) != 0
                            && (signature.Header & ExplicitThisSignatureFlag) == 0
                        ? 1
                        : 0);
                if ((uint)operation.Value >= (uint)argumentSlots)
                {
                    throw new InvalidArgumentSlotException(
                        $"The {side.ToString().ToLowerInvariant()} body references argument "
                        + $"{operation.Value}, but its signature has {argumentSlots} argument slots.");
                }
            }
            blockOperations[block].Add(operation);
            terminators[block] = instruction;
        }

        var blocks = ImmutableArray.CreateBuilder<StructuralCloneBlock>(
            body.Blocks.Blocks.Length);
        foreach (InstructionBlock block in body.Blocks.Blocks)
        {
            var outgoing =
                ImmutableArray.CreateBuilder<StructuralCloneEdge>();
            if (terminators[block.Index] is { } terminator)
            {
                for (int ordinal = 0;
                    ordinal < terminator.BranchTargets.Length;
                    ordinal++)
                {
                    int target =
                        body.BlockIndexAt(terminator.BranchTargets[ordinal]);
                    if (target < 0)
                    {
                        throw new InvalidOperationException(
                            $"Branch target IL_{terminator.BranchTargets[ordinal]:X4} "
                            + "does not resolve to a method block.");
                    }
                    outgoing.Add(new StructuralCloneEdge(
                        new StructuralCloneEdgeRole(
                            StructuralCloneEdgeKind.Branch,
                            ordinal),
                        target));
                }
                if (terminator.FallsThrough
                    && block.Index + 1 < body.Blocks.Blocks.Length)
                {
                    outgoing.Add(new StructuralCloneEdge(
                        new StructuralCloneEdgeRole(
                            StructuralCloneEdgeKind.FallThrough,
                            0),
                        block.Index + 1));
                }
            }

            int[] modeledTargets =
            [
                .. outgoing
                    .Select(static edge => edge.Target)
                    .Distinct()
                    .Order(),
            ];
            if (!modeledTargets.SequenceEqual(block.Edges.Successors))
            {
                throw new InvalidOperationException(
                    $"Block {block.Index} has control-flow successors outside "
                    + "the exact clone edge model.");
            }

            blocks.Add(new StructuralCloneBlock(
                block.Index,
                block.Start,
                block.Edges.ExitsMethod,
                blockOperations[block.Index].ToImmutable(),
                outgoing.ToImmutable(),
                Incoming: []));
        }

        var incoming =
            new ImmutableArray<StructuralCloneEdge>.Builder[blocks.Count];
        for (int index = 0; index < incoming.Length; index++)
            incoming[index] = ImmutableArray.CreateBuilder<StructuralCloneEdge>();
        foreach (StructuralCloneBlock block in blocks)
        {
            foreach (StructuralCloneEdge edge in block.Outgoing)
            {
                incoming[edge.Target].Add(
                    new StructuralCloneEdge(edge.Role, block.Index));
            }
        }

        return new StructuralCloneGraph(
        [
            .. blocks.Select(block => block with
            {
                Incoming = incoming[block.Index].ToImmutable(),
            }),
        ]);
    }

    static MetadataOperandFailure? InvalidMetadataOperand(
        MetadataReader reader,
        MethodInstructions body,
        StructuralCloneSide side)
    {
        var validityByOperand =
            new Dictionary<
                (OperandKind Kind, long Value),
                MetadataOperandValidity>();
        foreach (DecodedInstruction instruction in body.Instructions)
        {
            if (instruction.Operand is not (
                OperandKind.InlineString
                or OperandKind.InlineMethod
                or OperandKind.InlineField
                or OperandKind.InlineType
                or OperandKind.InlineSig
                or OperandKind.InlineTok))
            {
                continue;
            }

            MetadataOperandValidity validity;
            try
            {
                var key =
                    (instruction.Operand, instruction.OperandValue);
                if (!validityByOperand.TryGetValue(key, out validity))
                {
                    validity = instruction.Operand == OperandKind.InlineString
                        ? ValidUserString(reader, instruction.OperandValue)
                            ? MetadataOperandValidity.Valid
                            : MetadataOperandValidity.Invalid
                        : ValidateEntityOperand(
                            reader,
                            instruction.Operand,
                            instruction.OperandValue);
                    validityByOperand.Add(key, validity);
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentException
                    or ArgumentOutOfRangeException
                    or OverflowException)
            {
                return new MetadataOperandFailure(
                    StructuralCloneDisposition.Failed,
                    InvalidOperandBlocker(
                        side,
                        instruction,
                        $"{ex.GetType().Name}: {ex.Message}"));
            }

            if (validity == MetadataOperandValidity.Unsupported)
            {
                return new MetadataOperandFailure(
                    StructuralCloneDisposition.Unsupported,
                    new StructuralCloneBlocker(
                        StructuralCloneBlockerKind.UnsupportedMethodSignature,
                        side,
                        $"Instruction IL_{instruction.Offset:X4} "
                        + "has a call-site signature whose nested type shape "
                        + "exceeds the guarded decode policy."));
            }
            if (validity == MetadataOperandValidity.Invalid)
            {
                return new MetadataOperandFailure(
                    StructuralCloneDisposition.Failed,
                    InvalidOperandBlocker(
                        side,
                        instruction,
                        "the token kind, row, or signature grammar is invalid "
                        + "for the opcode"));
            }
        }

        return null;
    }

    static bool ValidUserString(
        MetadataReader reader,
        long operandValue)
    {
        if (operandValue is <= 0 or > int.MaxValue)
            return false;
        int token = (int)operandValue;
        if ((token & unchecked((int)0xFF000000)) != 0x70000000)
            return false;
        int offset = token & 0x00FFFFFF;
        if (offset <= 0
            || offset >= reader.GetHeapSize(HeapIndex.UserString))
        {
            return false;
        }

        reader.GetUserString(MetadataTokens.UserStringHandle(offset));
        return true;
    }

    static MetadataOperandValidity ValidateEntityOperand(
        MetadataReader reader,
        OperandKind operand,
        long operandValue)
    {
        if (operandValue is <= 0 or > int.MaxValue)
            return MetadataOperandValidity.Invalid;
        EntityHandle handle = MetadataTokens.EntityHandle((int)operandValue);
        if (!ValidEntityRow(reader, handle))
            return MetadataOperandValidity.Invalid;

        if (operand == OperandKind.InlineSig)
        {
            return handle.Kind == HandleKind.StandaloneSignature
                ? ValidateCallSiteSignature(
                    reader,
                    (StandaloneSignatureHandle)handle)
                : MetadataOperandValidity.Invalid;
        }

        bool valid = operand switch
        {
            OperandKind.InlineMethod =>
                handle.Kind is HandleKind.MethodDefinition
                    or HandleKind.MethodSpecification
                || handle.Kind == HandleKind.MemberReference
                    && reader.GetMemberReference(
                        (MemberReferenceHandle)handle).GetKind()
                        == MemberReferenceKind.Method,
            OperandKind.InlineField =>
                handle.Kind == HandleKind.FieldDefinition
                || handle.Kind == HandleKind.MemberReference
                    && reader.GetMemberReference(
                        (MemberReferenceHandle)handle).GetKind()
                        == MemberReferenceKind.Field,
            OperandKind.InlineType =>
                handle.Kind is HandleKind.TypeDefinition
                    or HandleKind.TypeReference
                    or HandleKind.TypeSpecification,
            OperandKind.InlineTok =>
                handle.Kind is HandleKind.TypeDefinition
                    or HandleKind.TypeReference
                    or HandleKind.TypeSpecification
                    or HandleKind.FieldDefinition
                    or HandleKind.MethodDefinition
                    or HandleKind.MemberReference
                    or HandleKind.MethodSpecification,
            _ => false,
        };
        return valid
            ? MetadataOperandValidity.Valid
            : MetadataOperandValidity.Invalid;
    }

    static bool ValidEntityRow(
        MetadataReader reader,
        EntityHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (row <= 0)
            return false;
        TableIndex? table = handle.Kind switch
        {
            HandleKind.TypeReference => TableIndex.TypeRef,
            HandleKind.TypeDefinition => TableIndex.TypeDef,
            HandleKind.FieldDefinition => TableIndex.Field,
            HandleKind.MethodDefinition => TableIndex.MethodDef,
            HandleKind.MemberReference => TableIndex.MemberRef,
            HandleKind.StandaloneSignature => TableIndex.StandAloneSig,
            HandleKind.TypeSpecification => TableIndex.TypeSpec,
            HandleKind.MethodSpecification => TableIndex.MethodSpec,
            _ => null,
        };
        return table is { } value
            && row <= reader.GetTableRowCount(value);
    }

    static MetadataOperandValidity ValidateCallSiteSignature(
        MetadataReader reader,
        StandaloneSignatureHandle handle)
    {
        StandaloneSignature signature =
            reader.GetStandaloneSignature(handle);
        BlobReader headerReader =
            reader.GetBlobReader(signature.Signature);
        if (headerReader.ReadSignatureHeader().Kind != SignatureKind.Method)
            return MetadataOperandValidity.Invalid;
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                signature.Signature,
                SignatureBlobGuard.Kind.StandaloneMethod))
        {
            return IsMalformedUnsafeSignature(
                reader,
                signature.Signature,
                SignatureBlobGuard.Kind.StandaloneMethod)
                ? MetadataOperandValidity.Invalid
                : MetadataOperandValidity.Unsupported;
        }

        try
        {
            MethodSignature<StructuralCloneSignatureType> decoded =
                signature.DecodeMethodSignature(
                    new StructuralCloneSignatureTypeProvider(),
                    GenericScope.Empty);
            if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                    reader,
                    signature.Signature,
                    SignatureBlobGuard.Kind.StandaloneMethod))
            {
                return MetadataOperandValidity.Invalid;
            }
            if (HasInvalidMethodTypePosition(decoded))
                return MetadataOperandValidity.Invalid;
            return ValidSignatureTypes(decoded)
                ? MetadataOperandValidity.Valid
                : MetadataOperandValidity.Unsupported;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return MetadataOperandValidity.Invalid;
        }
    }

    static int ReadLocalCount(
        MetadataReader reader,
        BlobHandle signature)
    {
        BlobReader blob = reader.GetBlobReader(signature);
        if (blob.ReadSignatureHeader().Kind
            != SignatureKind.LocalVariables)
        {
            throw new BadImageFormatException(
                "A method body local signature does not have the local-variable signature kind.");
        }

        int count = blob.ReadCompressedInteger();
        if (count < 0)
        {
            throw new BadImageFormatException(
                "A method body local signature has an invalid local count.");
        }
        return count;
    }

    static StructuralCloneBlocker InvalidOperandBlocker(
        StructuralCloneSide side,
        DecodedInstruction instruction,
        string reason)
        => new(
            StructuralCloneBlockerKind.InvalidMetadataOperand,
            side,
            $"Instruction IL_{instruction.Offset:X4} ({instruction.OpCode}) has invalid "
            + $"operand 0x{instruction.OperandValue:X8}: {reason}.");

    static StructuralCloneOperation NormalizeOperation(
        DecodedInstruction instruction)
    {
        if (TryLocal(instruction, out ILOpCode localOpcode, out int local))
        {
            return new StructuralCloneOperation(
                localOpcode,
                StructuralCloneOperandKind.Local,
                local);
        }
        if (TryArgument(
                instruction,
                out ILOpCode argumentOpcode,
                out int argument))
        {
            return new StructuralCloneOperation(
                argumentOpcode,
                StructuralCloneOperandKind.Argument,
                argument);
        }
        if (TryInt32Constant(instruction, out int int32))
        {
            return new StructuralCloneOperation(
                ILOpCode.Ldc_i4,
                StructuralCloneOperandKind.Immediate,
                int32);
        }

        ILOpCode opcode = NormalizeBranchOpcode(instruction.OpCode);
        if (instruction.Operand is OperandKind.ShortInlineBrTarget
            or OperandKind.InlineBrTarget
            or OperandKind.InlineSwitch)
        {
            return new StructuralCloneOperation(
                opcode,
                StructuralCloneOperandKind.None,
                0);
        }

        StructuralCloneOperandKind operandKind =
            instruction.Operand switch
            {
                OperandKind.ShortInlineI
                    or OperandKind.InlineI
                    or OperandKind.InlineI8
                    or OperandKind.ShortInlineR
                    or OperandKind.InlineR =>
                    StructuralCloneOperandKind.Immediate,
                OperandKind.InlineString =>
                    StructuralCloneOperandKind.UserStringToken,
                OperandKind.InlineMethod
                    or OperandKind.InlineField
                    or OperandKind.InlineType
                    or OperandKind.InlineTok =>
                    StructuralCloneOperandKind.MetadataToken,
                OperandKind.InlineSig =>
                    StructuralCloneOperandKind.SignatureToken,
                _ => StructuralCloneOperandKind.None,
            };
        return new StructuralCloneOperation(
            opcode,
            operandKind,
            operandKind == StructuralCloneOperandKind.None
                ? 0
                : instruction.OperandValue);
    }

    static ILOpCode NormalizeBranchOpcode(ILOpCode opcode)
        => opcode switch
        {
            ILOpCode.Br_s => ILOpCode.Br,
            ILOpCode.Brfalse_s => ILOpCode.Brfalse,
            ILOpCode.Brtrue_s => ILOpCode.Brtrue,
            ILOpCode.Beq_s => ILOpCode.Beq,
            ILOpCode.Bge_s => ILOpCode.Bge,
            ILOpCode.Bgt_s => ILOpCode.Bgt,
            ILOpCode.Ble_s => ILOpCode.Ble,
            ILOpCode.Blt_s => ILOpCode.Blt,
            ILOpCode.Bne_un_s => ILOpCode.Bne_un,
            ILOpCode.Bge_un_s => ILOpCode.Bge_un,
            ILOpCode.Bgt_un_s => ILOpCode.Bgt_un,
            ILOpCode.Ble_un_s => ILOpCode.Ble_un,
            ILOpCode.Blt_un_s => ILOpCode.Blt_un,
            ILOpCode.Leave_s => ILOpCode.Leave,
            _ => opcode,
        };

    static bool TryInt32Constant(
        DecodedInstruction instruction,
        out int value)
    {
        (bool matched, value) = instruction.OpCode switch
        {
            ILOpCode.Ldc_i4_m1 => (true, -1),
            ILOpCode.Ldc_i4_0 => (true, 0),
            ILOpCode.Ldc_i4_1 => (true, 1),
            ILOpCode.Ldc_i4_2 => (true, 2),
            ILOpCode.Ldc_i4_3 => (true, 3),
            ILOpCode.Ldc_i4_4 => (true, 4),
            ILOpCode.Ldc_i4_5 => (true, 5),
            ILOpCode.Ldc_i4_6 => (true, 6),
            ILOpCode.Ldc_i4_7 => (true, 7),
            ILOpCode.Ldc_i4_8 => (true, 8),
            ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 => (
                true,
                checked((int)instruction.OperandValue)),
            _ => (false, 0),
        };
        return matched;
    }

    static bool TryLocal(
        DecodedInstruction instruction,
        out ILOpCode opcode,
        out int slot)
    {
        (opcode, slot) = instruction.OpCode switch
        {
            ILOpCode.Ldloc_0 => (ILOpCode.Ldloc, 0),
            ILOpCode.Ldloc_1 => (ILOpCode.Ldloc, 1),
            ILOpCode.Ldloc_2 => (ILOpCode.Ldloc, 2),
            ILOpCode.Ldloc_3 => (ILOpCode.Ldloc, 3),
            ILOpCode.Ldloc_s or ILOpCode.Ldloc => (
                ILOpCode.Ldloc,
                checked((int)instruction.OperandValue)),
            ILOpCode.Ldloca_s or ILOpCode.Ldloca => (
                ILOpCode.Ldloca,
                checked((int)instruction.OperandValue)),
            ILOpCode.Stloc_0 => (ILOpCode.Stloc, 0),
            ILOpCode.Stloc_1 => (ILOpCode.Stloc, 1),
            ILOpCode.Stloc_2 => (ILOpCode.Stloc, 2),
            ILOpCode.Stloc_3 => (ILOpCode.Stloc, 3),
            ILOpCode.Stloc_s or ILOpCode.Stloc => (
                ILOpCode.Stloc,
                checked((int)instruction.OperandValue)),
            _ => (default, -1),
        };
        return slot >= 0;
    }

    static bool TryArgument(
        DecodedInstruction instruction,
        out ILOpCode opcode,
        out int slot)
    {
        (opcode, slot) = instruction.OpCode switch
        {
            ILOpCode.Ldarg_0 => (ILOpCode.Ldarg, 0),
            ILOpCode.Ldarg_1 => (ILOpCode.Ldarg, 1),
            ILOpCode.Ldarg_2 => (ILOpCode.Ldarg, 2),
            ILOpCode.Ldarg_3 => (ILOpCode.Ldarg, 3),
            ILOpCode.Ldarg_s or ILOpCode.Ldarg => (
                ILOpCode.Ldarg,
                checked((int)instruction.OperandValue)),
            ILOpCode.Ldarga_s or ILOpCode.Ldarga => (
                ILOpCode.Ldarga,
                checked((int)instruction.OperandValue)),
            ILOpCode.Starg_s or ILOpCode.Starg => (
                ILOpCode.Starg,
                checked((int)instruction.OperandValue)),
            _ => (default, -1),
        };
        return slot >= 0;
    }

    static RefinedColors Refine(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right)
    {
        int[] leftBlocks = new int[left.Graph.Blocks.Length];
        int[] rightBlocks = new int[right.Graph.Blocks.Length];
        int[] leftLocals = new int[left.Locals.Length];
        int[] rightLocals = new int[right.Locals.Length];
        int maximumRounds = leftBlocks.Length + rightBlocks.Length
            + leftLocals.Length + rightLocals.Length + 1;
        int rounds = 0;

        for (int ceiling = 0; ceiling < maximumRounds; ceiling++)
        {
            (int[] nextLeftBlocks, int[] nextRightBlocks) = AssignColors(
                BuildBlockKeys(left, leftBlocks, leftLocals),
                BuildBlockKeys(right, rightBlocks, rightLocals));
            (int[] nextLeftLocals, int[] nextRightLocals) = AssignColors(
                BuildLocalKeys(left, nextLeftBlocks, leftLocals),
                BuildLocalKeys(right, nextRightBlocks, rightLocals));
            rounds++;
            bool fixedPoint =
                leftBlocks.AsSpan().SequenceEqual(nextLeftBlocks)
                && rightBlocks.AsSpan().SequenceEqual(nextRightBlocks)
                && leftLocals.AsSpan().SequenceEqual(nextLeftLocals)
                && rightLocals.AsSpan().SequenceEqual(nextRightLocals);
            leftBlocks = nextLeftBlocks;
            rightBlocks = nextRightBlocks;
            leftLocals = nextLeftLocals;
            rightLocals = nextRightLocals;
            if (fixedPoint)
                break;
        }

        return new RefinedColors(
            leftBlocks,
            rightBlocks,
            leftLocals,
            rightLocals,
            rounds);
    }

    static BlockRefinementKey[] BuildBlockKeys(
        StructuralCloneBodyFacts body,
        int[] blockColors,
        int[] localColors)
        =>
        [
            .. body.Graph.Blocks.Select(block =>
                new BlockRefinementKey(
                    blockColors[block.Index],
                    block.Index == 0,
                    block.ExitsMethod,
                    [
                        .. block.Operations.Select(operation =>
                            new OperationRefinementKey(
                                operation.OpCode,
                                operation.OperandKind,
                                operation.OperandKind
                                    == StructuralCloneOperandKind.Local
                                    ? localColors[checked((int)operation.Value)]
                                    : operation.Value)),
                    ],
                    [
                        .. block.Outgoing
                            .Select(edge => new EdgeRefinementKey(
                                edge.Role,
                                blockColors[edge.Target]))
                            .OrderBy(static edge => edge.Role.Kind)
                            .ThenBy(static edge => edge.Role.Ordinal)
                            .ThenBy(static edge => edge.TargetColor),
                    ],
                    [
                        .. block.Incoming
                            .Select(edge => new EdgeRefinementKey(
                                edge.Role,
                                blockColors[edge.Target]))
                            .OrderBy(static edge => edge.Role.Kind)
                            .ThenBy(static edge => edge.Role.Ordinal)
                            .ThenBy(static edge => edge.TargetColor),
                    ])),
        ];

    static LocalRefinementKey[] BuildLocalKeys(
        StructuralCloneBodyFacts body,
        int[] blockColors,
        int[] localColors)
    {
        var uses =
            new ImmutableArray<LocalUseRefinementKey>.Builder[body.Locals.Length];
        for (int local = 0; local < uses.Length; local++)
            uses[local] = ImmutableArray.CreateBuilder<LocalUseRefinementKey>();

        foreach (StructuralCloneBlock block in body.Graph.Blocks)
        {
            for (int ordinal = 0; ordinal < block.Operations.Length; ordinal++)
            {
                StructuralCloneOperation operation = block.Operations[ordinal];
                if (operation.OperandKind != StructuralCloneOperandKind.Local)
                    continue;
                uses[checked((int)operation.Value)].Add(
                    new LocalUseRefinementKey(
                        operation.OpCode,
                        blockColors[block.Index],
                        ordinal));
            }
        }

        return
        [
            .. body.Locals.Select((local, index) =>
                new LocalRefinementKey(
                    localColors[index],
                    local,
                    [
                        .. uses[index]
                            .OrderBy(static use => use.BlockColor)
                            .ThenBy(static use => use.Operation)
                            .ThenBy(static use => use.Ordinal),
                    ])),
        ];
    }

    static (int[] Left, int[] Right) AssignColors<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right)
        where T : notnull
    {
        var colors = new Dictionary<T, int>();
        var leftColors = new int[left.Count];
        var rightColors = new int[right.Count];
        int next = 0;
        for (int index = 0; index < left.Count; index++)
        {
            if (!colors.TryGetValue(left[index], out int color))
                colors.Add(left[index], color = next++);
            leftColors[index] = color;
        }
        for (int index = 0; index < right.Count; index++)
        {
            if (!colors.TryGetValue(right[index], out int color))
                colors.Add(right[index], color = next++);
            rightColors[index] = color;
        }
        return (leftColors, rightColors);
    }

    static WitnessResult FindWitness(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        RefinedColors colors,
        int maximumSteps)
    {
        var blockMap = new int[left.Graph.Blocks.Length];
        var reverseBlocks = new int[right.Graph.Blocks.Length];
        var localMap = new int[left.Locals.Length];
        var reverseLocals = new int[right.Locals.Length];
        var leftEdges = new StructuralCloneEdgeIndex(left.Graph);
        var rightEdges = new StructuralCloneEdgeIndex(right.Graph);
        Array.Fill(blockMap, -1);
        Array.Fill(reverseBlocks, -1);
        Array.Fill(localMap, -1);
        Array.Fill(reverseLocals, -1);
        int steps = 0;
        bool limitReached = false;

        if (colors.LeftBlocks[0] != colors.RightBlocks[0])
            return new WitnessResult(false, false, steps);
        var entryAssignments = new List<(int Left, int Right)>();
        if (!TryMatchBlockOperations(
                left,
                right,
                0,
                0,
                colors,
                localMap,
                reverseLocals,
                entryAssignments))
        {
            return new WitnessResult(false, false, steps);
        }
        blockMap[0] = 0;
        reverseBlocks[0] = 0;

        bool SearchBlocks()
        {
            if (++steps > maximumSteps)
            {
                limitReached = true;
                return false;
            }

            int nextLeft = -1;
            List<int>? candidates = null;
            for (int leftIndex = 0;
                leftIndex < left.Graph.Blocks.Length;
                leftIndex++)
            {
                if (blockMap[leftIndex] >= 0)
                    continue;
                var current = new List<int>();
                for (int rightIndex = 0;
                    rightIndex < right.Graph.Blocks.Length;
                    rightIndex++)
                {
                    if (reverseBlocks[rightIndex] < 0
                        && colors.LeftBlocks[leftIndex]
                            == colors.RightBlocks[rightIndex]
                        && EdgesConsistent(
                            left,
                            right,
                            leftIndex,
                            rightIndex,
                            blockMap,
                            reverseBlocks,
                            leftEdges,
                            rightEdges))
                    {
                        current.Add(rightIndex);
                    }
                }
                if (current.Count == 0)
                    return false;
                if (candidates is null || current.Count < candidates.Count)
                {
                    nextLeft = leftIndex;
                    candidates = current;
                }
            }

            if (candidates is null)
                return CompleteLocals();

            foreach (int rightIndex in candidates)
            {
                var assignments = new List<(int Left, int Right)>();
                if (!TryMatchBlockOperations(
                        left,
                        right,
                        nextLeft,
                        rightIndex,
                        colors,
                        localMap,
                        reverseLocals,
                        assignments))
                {
                    continue;
                }

                blockMap[nextLeft] = rightIndex;
                reverseBlocks[rightIndex] = nextLeft;
                if (SearchBlocks())
                    return true;
                blockMap[nextLeft] = -1;
                reverseBlocks[rightIndex] = -1;
                RollBackLocals(assignments, localMap, reverseLocals);
                if (limitReached)
                    return false;
            }
            return false;
        }

        bool CompleteLocals()
        {
            if (++steps > maximumSteps)
            {
                limitReached = true;
                return false;
            }

            int nextLeft = Array.FindIndex(localMap, static value => value < 0);
            if (nextLeft < 0)
                return true;
            for (int rightIndex = 0; rightIndex < reverseLocals.Length; rightIndex++)
            {
                if (reverseLocals[rightIndex] >= 0
                    || colors.LeftLocals[nextLeft]
                        != colors.RightLocals[rightIndex]
                    || !left.Locals[nextLeft].Equals(right.Locals[rightIndex]))
                {
                    continue;
                }
                localMap[nextLeft] = rightIndex;
                reverseLocals[rightIndex] = nextLeft;
                if (CompleteLocals())
                    return true;
                localMap[nextLeft] = -1;
                reverseLocals[rightIndex] = -1;
                if (limitReached)
                    return false;
            }
            return false;
        }

        bool found = SearchBlocks();
        return new WitnessResult(found, limitReached, steps);
    }

    static bool TryMatchBlockOperations(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        int leftBlock,
        int rightBlock,
        RefinedColors colors,
        int[] localMap,
        int[] reverseLocals,
        List<(int Left, int Right)> assignments)
    {
        StructuralCloneBlock leftValue = left.Graph.Blocks[leftBlock];
        StructuralCloneBlock rightValue = right.Graph.Blocks[rightBlock];
        if (leftValue.ExitsMethod != rightValue.ExitsMethod
            || leftValue.Operations.Length != rightValue.Operations.Length)
        {
            return false;
        }

        for (int index = 0; index < leftValue.Operations.Length; index++)
        {
            StructuralCloneOperation leftOperation =
                leftValue.Operations[index];
            StructuralCloneOperation rightOperation =
                rightValue.Operations[index];
            if (leftOperation.OpCode != rightOperation.OpCode
                || leftOperation.OperandKind != rightOperation.OperandKind)
            {
                RollBackLocals(assignments, localMap, reverseLocals);
                return false;
            }
            if (leftOperation.OperandKind
                != StructuralCloneOperandKind.Local)
            {
                if (leftOperation.Value != rightOperation.Value)
                {
                    RollBackLocals(assignments, localMap, reverseLocals);
                    return false;
                }
                continue;
            }

            int leftLocal = checked((int)leftOperation.Value);
            int rightLocal = checked((int)rightOperation.Value);
            if (colors.LeftLocals[leftLocal]
                    != colors.RightLocals[rightLocal]
                || !left.Locals[leftLocal].Equals(right.Locals[rightLocal]))
            {
                RollBackLocals(assignments, localMap, reverseLocals);
                return false;
            }
            if (localMap[leftLocal] >= 0)
            {
                if (localMap[leftLocal] != rightLocal)
                {
                    RollBackLocals(assignments, localMap, reverseLocals);
                    return false;
                }
                continue;
            }
            if (reverseLocals[rightLocal] >= 0)
            {
                RollBackLocals(assignments, localMap, reverseLocals);
                return false;
            }
            localMap[leftLocal] = rightLocal;
            reverseLocals[rightLocal] = leftLocal;
            assignments.Add((leftLocal, rightLocal));
        }
        return true;
    }

    static bool EdgesConsistent(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        int leftBlock,
        int rightBlock,
        int[] blockMap,
        int[] reverseBlocks,
        StructuralCloneEdgeIndex leftEdges,
        StructuralCloneEdgeIndex rightEdges)
    {
        foreach (StructuralCloneEdge edge
            in left.Graph.Blocks[leftBlock].Outgoing)
        {
            if (blockMap[edge.Target] >= 0
                && !rightEdges.Outgoing[rightBlock].Contains(
                    new StructuralCloneEdge(
                        edge.Role,
                        blockMap[edge.Target])))
            {
                return false;
            }
        }
        foreach (StructuralCloneEdge edge
            in left.Graph.Blocks[leftBlock].Incoming)
        {
            if (blockMap[edge.Target] >= 0
                && !rightEdges.Incoming[rightBlock].Contains(
                    new StructuralCloneEdge(
                        edge.Role,
                        blockMap[edge.Target])))
            {
                return false;
            }
        }
        foreach (StructuralCloneEdge edge
            in right.Graph.Blocks[rightBlock].Outgoing)
        {
            if (reverseBlocks[edge.Target] >= 0
                && !leftEdges.Outgoing[leftBlock].Contains(
                    new StructuralCloneEdge(
                        edge.Role,
                        reverseBlocks[edge.Target])))
            {
                return false;
            }
        }
        foreach (StructuralCloneEdge edge
            in right.Graph.Blocks[rightBlock].Incoming)
        {
            if (reverseBlocks[edge.Target] >= 0
                && !leftEdges.Incoming[leftBlock].Contains(
                    new StructuralCloneEdge(
                        edge.Role,
                        reverseBlocks[edge.Target])))
            {
                return false;
            }
        }
        return true;
    }

    static void RollBackLocals(
        IEnumerable<(int Left, int Right)> assignments,
        int[] localMap,
        int[] reverseLocals)
    {
        foreach ((int left, int right) in assignments)
        {
            localMap[left] = -1;
            reverseLocals[right] = -1;
        }
    }

    static StructuralCloneCorrespondence BuildCorrespondence(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        RefinedColors colors)
    {
        ImmutableArray<StructuralCloneBlockClass> blocks =
        [
            .. Enumerable.Range(0, left.Graph.Blocks.Length)
                .Select(leftBlock => new StructuralCloneBlockClass(
                    leftBlock,
                    [
                        .. Enumerable.Range(0, right.Graph.Blocks.Length)
                            .Where(rightBlock =>
                                colors.LeftBlocks[leftBlock]
                                    == colors.RightBlocks[rightBlock]),
                    ])),
        ];
        ImmutableArray<StructuralCloneLocalClass> locals =
        [
            .. Enumerable.Range(0, left.Locals.Length)
                .Select(leftLocal => new StructuralCloneLocalClass(
                    leftLocal,
                    [
                        .. Enumerable.Range(0, right.Locals.Length)
                            .Where(rightLocal =>
                                colors.LeftLocals[leftLocal]
                                    == colors.RightLocals[rightLocal]),
                    ])),
        ];
        bool unique =
            blocks.All(static block => block.RightBlocks.Length == 1)
            && locals.All(static local => local.RightLocals.Length == 1);
        return new StructuralCloneCorrespondence(
            unique
                ? StructuralCloneCorrespondenceKind.Unique
                : StructuralCloneCorrespondenceKind.Ambiguous,
            blocks,
            locals);
    }

    static ImmutableArray<StructuralCloneBlocker> LimitsFor(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        StructuralCloneComparisonLimits limits)
        =>
        [
            .. LimitsFor(
                left.BodyBytes,
                left.InstructionCount,
                left.Graph.Blocks.Length,
                EdgeCount(left.Graph),
                left.Locals.Length,
                limits,
                StructuralCloneSide.Left),
            .. LimitsFor(
                right.BodyBytes,
                right.InstructionCount,
                right.Graph.Blocks.Length,
                EdgeCount(right.Graph),
                right.Locals.Length,
                limits,
                StructuralCloneSide.Right),
        ];

    static ImmutableArray<StructuralCloneBlocker> LimitsFor(
        MethodInstructions instructions,
        int locals,
        StructuralCloneComparisonLimits limits,
        StructuralCloneSide side)
        => LimitsFor(
            instructions.Instructions.IsEmpty
                ? 0
                : instructions.Instructions[^1].NextOffset,
            instructions.Instructions.Length,
            instructions.Blocks.Blocks.Length,
            -1,
            locals,
            limits,
            side);

    static ImmutableArray<StructuralCloneBlocker> LimitsFor(
        int bodyBytes,
        int instructions,
        int blocks,
        int edges,
        int locals,
        StructuralCloneComparisonLimits limits,
        StructuralCloneSide side)
    {
        var blockers = ImmutableArray.CreateBuilder<StructuralCloneBlocker>();
        if (bodyBytes > limits.MaximumBodyBytes)
        {
            blockers.Add(new StructuralCloneBlocker(
                StructuralCloneBlockerKind.BodySizeLimit,
                side,
                $"IL body size {bodyBytes} bytes exceeds "
                + $"{limits.MaximumBodyBytes}."));
        }
        if (instructions > limits.MaximumInstructions)
        {
            blockers.Add(InstructionLimitBlocker(
                instructions,
                limits.MaximumInstructions,
                side));
        }
        if (blocks > limits.MaximumBlocks)
        {
            blockers.Add(new StructuralCloneBlocker(
                StructuralCloneBlockerKind.BlockLimit,
                side,
                $"Block count {blocks} exceeds {limits.MaximumBlocks}."));
        }
        if (edges > limits.MaximumEdges)
        {
            blockers.Add(new StructuralCloneBlocker(
                StructuralCloneBlockerKind.EdgeLimit,
                side,
                $"Edge count {edges} exceeds {limits.MaximumEdges}."));
        }
        if (locals > limits.MaximumLocals)
        {
            blockers.Add(new StructuralCloneBlocker(
                StructuralCloneBlockerKind.LocalLimit,
                side,
                $"Local count {locals} exceeds {limits.MaximumLocals}."));
        }
        return blockers.ToImmutable();
    }

    static StructuralCloneBlocker InstructionLimitBlocker(
        int instructions,
        int maximumInstructions,
        StructuralCloneSide side)
        => new(
            StructuralCloneBlockerKind.InstructionLimit,
            side,
            $"Instruction count {instructions} exceeds "
            + $"{maximumInstructions}.");

    static StructuralCloneVerificationReceipt Receipt(
        StructuralCloneBodyFacts? left,
        StructuralCloneBodyFacts? right,
        int refinementRounds,
        int steps,
        bool exhausted,
        bool witness)
        => new(
            left?.BodyBytes ?? 0,
            right?.BodyBytes ?? 0,
            left?.InstructionCount ?? 0,
            right?.InstructionCount ?? 0,
            left?.Graph.Blocks.Length ?? 0,
            right?.Graph.Blocks.Length ?? 0,
            left is null ? 0 : EdgeCount(left.Graph),
            right is null ? 0 : EdgeCount(right.Graph),
            left?.Locals.Length ?? 0,
            right?.Locals.Length ?? 0,
            refinementRounds,
            steps,
            exhausted,
            witness);

    static StructuralCloneVerificationReceipt Receipt(
        BodyProduction left,
        BodyProduction right,
        int refinementRounds,
        int steps,
        bool exhausted,
        bool witness)
        => new(
            left.Measurements.BodyBytes,
            right.Measurements.BodyBytes,
            left.Measurements.InstructionCount,
            right.Measurements.InstructionCount,
            left.Measurements.BlockCount,
            right.Measurements.BlockCount,
            left.Measurements.EdgeCount,
            right.Measurements.EdgeCount,
            left.Measurements.LocalCount,
            right.Measurements.LocalCount,
            refinementRounds,
            steps,
            exhausted,
            witness);

    static int EdgeCount(StructuralCloneGraph graph)
        => graph.Blocks.Sum(static block => block.Outgoing.Length);

    static bool ValidSignatureTypes(
        MethodSignature<StructuralCloneSignatureType> signature)
        => signature.ReturnType.IsValid
            && signature.ParameterTypes.All(static type => type.IsValid);

    static bool HasInvalidMethodTypePosition(
        MethodSignature<StructuralCloneSignatureType> signature)
        => signature.ReturnType.IsPinned
            || signature.ParameterTypes.Any(
                static type => type.IsVoid || type.IsPinned);

    internal static bool IsMalformedUnsafeSignature(
        MetadataReader reader,
        BlobHandle signature,
        SignatureBlobGuard.Kind kind)
    {
        if (reader.GetBlobReader(signature).Length
            > MetadataSafetyPolicy.MaxSignatureTypeNodes)
        {
            return false;
        }
        return !SignatureBlobGuard.IsSafeToDecode(
                reader,
                signature,
                kind,
                int.MaxValue);
    }

    static bool SupportedType(TypeRef type)
    {
        if (type.Kind == TypeRefKind.Unsupported)
            return false;
        if (type.ElementType is { } element && !SupportedType(element))
            return false;
        return type.TypeArguments.All(SupportedType);
    }

    static StructuralCloneDisposition MoreSevere(
        StructuralCloneDisposition left,
        StructuralCloneDisposition right)
    {
        static int Rank(StructuralCloneDisposition value)
            => value switch
            {
                StructuralCloneDisposition.Failed => 3,
                StructuralCloneDisposition.LimitReached => 2,
                StructuralCloneDisposition.Unsupported => 1,
                _ => 0,
            };
        return Rank(left) >= Rank(right) ? left : right;
    }

    static void ValidateHandle(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        string parameter)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (handle.IsNil
            || row > reader.GetTableRowCount(TableIndex.MethodDef))
        {
            throw new ArgumentOutOfRangeException(
                parameter,
                "The method handle is outside the image's MethodDef table.");
        }
    }

    static void ValidateLimits(StructuralCloneComparisonLimits limits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumInstructions,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumBlocks,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumEdges,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumLocals,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumVerificationSteps,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumBodyBytes,
            1);
    }
}

internal enum StructuralCloneOperandKind
{
    None,
    Immediate,
    Argument,
    Local,
    MetadataToken,
    UserStringToken,
    SignatureToken,
}

internal enum StructuralCloneEdgeKind
{
    Branch,
    FallThrough,
}

internal readonly record struct StructuralCloneMethodSignature(
    byte Header,
    int GenericArity,
    int RequiredParameterCount,
    int ParameterCount,
    bool ReturnsVoid);

internal readonly record struct StructuralCloneOperation(
    ILOpCode OpCode,
    StructuralCloneOperandKind OperandKind,
    long Value);

internal readonly record struct StructuralCloneEdgeRole(
    StructuralCloneEdgeKind Kind,
    int Ordinal);

internal readonly record struct StructuralCloneEdge(
    StructuralCloneEdgeRole Role,
    int Target);

internal sealed record StructuralCloneBlock(
    int Index,
    int Offset,
    bool ExitsMethod,
    ImmutableArray<StructuralCloneOperation> Operations,
    ImmutableArray<StructuralCloneEdge> Outgoing,
    ImmutableArray<StructuralCloneEdge> Incoming);

internal sealed record StructuralCloneGraph(
    ImmutableArray<StructuralCloneBlock> Blocks);

internal sealed record StructuralCloneBodyFacts(
    MetadataMethodAddress Method,
    int BodyBytes,
    int InstructionCount,
    bool InitLocals,
    ImmutableArray<StructuralCloneTypeIdentity> Locals,
    StructuralCloneMethodSignature Signature,
    StructuralCloneGraph Graph);

internal readonly record struct BodyMeasurements(
    int BodyBytes,
    int InstructionCount,
    int BlockCount,
    int EdgeCount,
    int LocalCount)
{
    public static BodyMeasurements From(
        MethodInstructions instructions,
        int localCount,
        int? bodyBytes = null)
        => new(
            bodyBytes
                ?? (instructions.Instructions.IsEmpty
                    ? 0
                    : instructions.Instructions[^1].NextOffset),
            instructions.Instructions.Length,
            instructions.Blocks.Blocks.Length,
            0,
            localCount);

    public static BodyMeasurements From(
        StructuralCloneBodyFacts facts)
        => new(
            facts.BodyBytes,
            facts.InstructionCount,
            facts.Graph.Blocks.Length,
            facts.Graph.Blocks.Sum(
                static block => block.Outgoing.Length),
            facts.Locals.Length);
}

internal sealed record BodyProduction(
    StructuralCloneDisposition Disposition,
    StructuralCloneBodyFacts? Facts,
    ImmutableArray<StructuralCloneBlocker> Blockers,
    BodyMeasurements Measurements)
{
    public static BodyProduction Completed(StructuralCloneBodyFacts facts)
        => new(
            StructuralCloneDisposition.Completed,
            facts,
            [],
            BodyMeasurements.From(facts));

    public static BodyProduction NotCompleted(
        StructuralCloneDisposition disposition,
        StructuralCloneBlocker blocker,
        BodyMeasurements measurements = default)
        => new(disposition, null, [blocker], measurements);

    public static BodyProduction NotCompleted(
        StructuralCloneDisposition disposition,
        ImmutableArray<StructuralCloneBlocker> blockers,
        BodyMeasurements measurements = default)
        => new(disposition, null, blockers, measurements);
}

internal sealed class BlockRefinementKey : IEquatable<BlockRefinementKey>
{
    public BlockRefinementKey(
        int previousColor,
        bool entry,
        bool exitsMethod,
        ImmutableArray<OperationRefinementKey> operations,
        ImmutableArray<EdgeRefinementKey> outgoing,
        ImmutableArray<EdgeRefinementKey> incoming)
    {
        PreviousColor = previousColor;
        Entry = entry;
        ExitsMethod = exitsMethod;
        Operations = operations;
        Outgoing = outgoing;
        Incoming = incoming;
    }

    public int PreviousColor { get; }
    public bool Entry { get; }
    public bool ExitsMethod { get; }
    public ImmutableArray<OperationRefinementKey> Operations { get; }
    public ImmutableArray<EdgeRefinementKey> Outgoing { get; }
    public ImmutableArray<EdgeRefinementKey> Incoming { get; }

    public bool Equals(BlockRefinementKey? other)
        => other is not null
            && PreviousColor == other.PreviousColor
            && Entry == other.Entry
            && ExitsMethod == other.ExitsMethod
            && Operations.AsSpan().SequenceEqual(other.Operations.AsSpan())
            && Outgoing.AsSpan().SequenceEqual(other.Outgoing.AsSpan())
            && Incoming.AsSpan().SequenceEqual(other.Incoming.AsSpan());

    public override bool Equals(object? obj)
        => obj is BlockRefinementKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PreviousColor);
        hash.Add(Entry);
        hash.Add(ExitsMethod);
        foreach (OperationRefinementKey operation in Operations)
            hash.Add(operation);
        foreach (EdgeRefinementKey edge in Outgoing)
            hash.Add(edge);
        foreach (EdgeRefinementKey edge in Incoming)
            hash.Add(edge);
        return hash.ToHashCode();
    }
}

internal sealed class LocalRefinementKey : IEquatable<LocalRefinementKey>
{
    public LocalRefinementKey(
        int previousColor,
        StructuralCloneTypeIdentity type,
        ImmutableArray<LocalUseRefinementKey> uses)
    {
        PreviousColor = previousColor;
        Type = type;
        Uses = uses;
    }

    public int PreviousColor { get; }
    public StructuralCloneTypeIdentity Type { get; }
    public ImmutableArray<LocalUseRefinementKey> Uses { get; }

    public bool Equals(LocalRefinementKey? other)
        => other is not null
            && PreviousColor == other.PreviousColor
            && Type.Equals(other.Type)
            && Uses.AsSpan().SequenceEqual(other.Uses.AsSpan());

    public override bool Equals(object? obj)
        => obj is LocalRefinementKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PreviousColor);
        hash.Add(Type);
        foreach (LocalUseRefinementKey use in Uses)
            hash.Add(use);
        return hash.ToHashCode();
    }
}

internal readonly record struct OperationRefinementKey(
    ILOpCode Operation,
    StructuralCloneOperandKind OperandKind,
    long Value);

internal readonly record struct EdgeRefinementKey(
    StructuralCloneEdgeRole Role,
    int TargetColor);

internal readonly record struct LocalUseRefinementKey(
    ILOpCode Operation,
    int BlockColor,
    int Ordinal);

internal sealed record RefinedColors(
    int[] LeftBlocks,
    int[] RightBlocks,
    int[] LeftLocals,
    int[] RightLocals,
    int Rounds);

internal readonly record struct WitnessResult(
    bool Found,
    bool LimitReached,
    int Steps);

enum MetadataOperandValidity
{
    Valid,
    Unsupported,
    Invalid,
}

readonly record struct MetadataOperandFailure(
    StructuralCloneDisposition Disposition,
    StructuralCloneBlocker Blocker);

internal sealed class StructuralCloneEdgeIndex
{
    public StructuralCloneEdgeIndex(StructuralCloneGraph graph)
    {
        Outgoing =
        [
            .. graph.Blocks.Select(
                static block => block.Outgoing.ToHashSet()),
        ];
        Incoming =
        [
            .. graph.Blocks.Select(
                static block => block.Incoming.ToHashSet()),
        ];
    }

    public HashSet<StructuralCloneEdge>[] Outgoing { get; }
    public HashSet<StructuralCloneEdge>[] Incoming { get; }
}

readonly record struct StructuralCloneSignatureType(
    bool IsValid,
    bool IsVoid,
    bool IsPinned = false)
{
    public static StructuralCloneSignatureType Valid(bool isVoid = false)
        => new(true, isVoid);

    public static StructuralCloneSignatureType Combine(
        params ReadOnlySpan<StructuralCloneSignatureType> types)
    {
        foreach (StructuralCloneSignatureType type in types)
        {
            if (!type.IsValid)
                return default;
        }
        return Valid();
    }
}

sealed class StructuralCloneSignatureTypeProvider
    : ISignatureTypeProvider<StructuralCloneSignatureType, GenericScope>
{
    public StructuralCloneSignatureType GetPrimitiveType(
        PrimitiveTypeCode typeCode)
        => StructuralCloneSignatureType.Valid(
            typeCode == PrimitiveTypeCode.Void);

    public StructuralCloneSignatureType GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        _ = reader.GetTypeDefinition(handle);
        return StructuralCloneSignatureType.Valid();
    }

    public StructuralCloneSignatureType GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        _ = reader.GetTypeReference(handle);
        return StructuralCloneSignatureType.Valid();
    }

    public StructuralCloneSignatureType GetTypeFromSpecification(
        MetadataReader reader,
        GenericScope genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        TypeSpecification specification =
            reader.GetTypeSpecification(handle);
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                specification.Signature,
                SignatureBlobGuard.Kind.TypeSpecification))
        {
            if (StructuralCloneAnalysis.IsMalformedUnsafeSignature(
                    reader,
                    specification.Signature,
                    SignatureBlobGuard.Kind.TypeSpecification))
            {
                throw new BadImageFormatException(
                    "A type specification has invalid signature grammar.");
            }
            return default;
        }
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                specification.Signature,
                SignatureBlobGuard.Kind.TypeSpecification))
        {
            throw new BadImageFormatException(
                "A type specification is incomplete or has trailing data.");
        }
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            return default;
        using (scope)
        {
            return reader.GetTypeSpecification(handle)
                .DecodeSignature(this, genericContext);
        }
    }

    public StructuralCloneSignatureType GetSZArrayType(
        StructuralCloneSignatureType elementType)
        => RequireNonVoid(
            elementType,
            "An array element cannot be void or pinned.");

    public StructuralCloneSignatureType GetArrayType(
        StructuralCloneSignatureType elementType,
        ArrayShape shape)
        => RequireNonVoid(
            elementType,
            "An array element cannot be void or pinned.");

    public StructuralCloneSignatureType GetByReferenceType(
        StructuralCloneSignatureType elementType)
        => RequireNonVoid(
            elementType,
            "A by-reference element cannot be void or pinned.");

    public StructuralCloneSignatureType GetPointerType(
        StructuralCloneSignatureType elementType)
    {
        if (elementType.IsPinned)
        {
            throw new BadImageFormatException(
                "A pointer element cannot be pinned.");
        }
        return StructuralCloneSignatureType.Combine(elementType);
    }

    public StructuralCloneSignatureType GetPinnedType(
        StructuralCloneSignatureType elementType)
    {
        if (elementType.IsVoid || elementType.IsPinned)
        {
            throw new BadImageFormatException(
                "A pinned local has an invalid element type.");
        }
        return new(elementType.IsValid, false, true);
    }

    public StructuralCloneSignatureType GetGenericInstantiation(
        StructuralCloneSignatureType genericType,
        ImmutableArray<StructuralCloneSignatureType> typeArguments)
    {
        if (genericType.IsVoid
            || genericType.IsPinned
            || typeArguments.Any(
                static type => type.IsVoid || type.IsPinned))
        {
            throw new BadImageFormatException(
                "A generic instantiation type cannot be void or pinned.");
        }
        return new(
            genericType.IsValid
                && typeArguments.All(static type => type.IsValid),
            false);
    }

    public StructuralCloneSignatureType GetGenericTypeParameter(
        GenericScope genericContext,
        int index)
        => StructuralCloneSignatureType.Valid();

    public StructuralCloneSignatureType GetGenericMethodParameter(
        GenericScope genericContext,
        int index)
        => StructuralCloneSignatureType.Valid();

    public StructuralCloneSignatureType GetFunctionPointerType(
        MethodSignature<StructuralCloneSignatureType> signature)
    {
        if (signature.Header.Kind != SignatureKind.Method)
        {
            throw new BadImageFormatException(
                "A function pointer does not have a method signature.");
        }
        if (signature.ReturnType.IsPinned
            || signature.ParameterTypes.Any(
                static type => type.IsVoid || type.IsPinned))
        {
            throw new BadImageFormatException(
                "A function-pointer signature contains an invalid type position.");
        }
        return new(
            signature.ReturnType.IsValid
                && signature.ParameterTypes.All(
                    static type => type.IsValid),
            false);
    }

    public StructuralCloneSignatureType GetModifiedType(
        StructuralCloneSignatureType modifier,
        StructuralCloneSignatureType unmodifiedType,
        bool isRequired)
    {
        if (modifier.IsVoid || modifier.IsPinned)
        {
            throw new BadImageFormatException(
                "A custom modifier has an invalid type.");
        }
        return new(
            modifier.IsValid && unmodifiedType.IsValid,
            unmodifiedType.IsVoid,
            unmodifiedType.IsPinned);
    }

    static StructuralCloneSignatureType RequireNonVoid(
        StructuralCloneSignatureType type,
        string message)
    {
        if (type.IsVoid || type.IsPinned)
            throw new BadImageFormatException(message);
        return StructuralCloneSignatureType.Combine(type);
    }
}

sealed class InvalidLocalSlotException(string message)
    : InvalidOperationException(message);

sealed class InvalidArgumentSlotException(string message)
    : InvalidOperationException(message);

internal sealed class StructuralCloneTypeIdentity
    : IEquatable<StructuralCloneTypeIdentity>
{
    StructuralCloneTypeIdentity(
        TypeRefKind kind,
        string assembly,
        string @namespace,
        string name,
        ResolvableTypeReference? resolution,
        StructuralCloneTypeIdentity? elementType,
        ImmutableArray<StructuralCloneTypeIdentity> typeArguments,
        int rank,
        ImmutableArray<int> arraySizes,
        ImmutableArray<int> arrayLowerBounds,
        int genericParameterIndex,
        byte rawTypeKind)
    {
        Kind = kind;
        Assembly = assembly;
        Namespace = @namespace;
        Name = name;
        Resolution = resolution;
        ElementType = elementType;
        TypeArguments = typeArguments;
        Rank = rank;
        ArraySizes = arraySizes;
        ArrayLowerBounds = arrayLowerBounds;
        GenericParameterIndex = genericParameterIndex;
        RawTypeKind = rawTypeKind;
    }

    public TypeRefKind Kind { get; }
    public string Assembly { get; }
    public string Namespace { get; }
    public string Name { get; }
    public ResolvableTypeReference? Resolution { get; }
    public StructuralCloneTypeIdentity? ElementType { get; }
    public ImmutableArray<StructuralCloneTypeIdentity> TypeArguments { get; }
    public int Rank { get; }
    public ImmutableArray<int> ArraySizes { get; }
    public ImmutableArray<int> ArrayLowerBounds { get; }
    public int GenericParameterIndex { get; }
    public byte RawTypeKind { get; }

    public static StructuralCloneTypeIdentity Create(TypeRef type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new StructuralCloneTypeIdentity(
            type.Kind,
            type.Assembly,
            type.Namespace,
            type.Name,
            type.Resolution,
            type.ElementType is { } element ? Create(element) : null,
            [.. type.TypeArguments.Select(Create)],
            type.Rank,
            type.ArraySizes,
            type.ArrayLowerBounds,
            type.GenericParameterIndex,
            type.RawTypeKind);
    }

    public bool Equals(StructuralCloneTypeIdentity? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (Kind != other.Kind
            || Assembly != other.Assembly
            || Namespace != other.Namespace
            || Name != other.Name
            || !Equals(Resolution, other.Resolution)
            || !Equals(ElementType, other.ElementType)
            || Rank != other.Rank
            || !ArraySizes.AsSpan().SequenceEqual(other.ArraySizes.AsSpan())
            || !ArrayLowerBounds.AsSpan().SequenceEqual(
                other.ArrayLowerBounds.AsSpan())
            || GenericParameterIndex != other.GenericParameterIndex
            || RawTypeKind != other.RawTypeKind
            || TypeArguments.Length != other.TypeArguments.Length)
        {
            return false;
        }

        for (int index = 0; index < TypeArguments.Length; index++)
        {
            if (!TypeArguments[index].Equals(other.TypeArguments[index]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj)
        => obj is StructuralCloneTypeIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Assembly);
        hash.Add(Namespace);
        hash.Add(Name);
        hash.Add(Resolution);
        hash.Add(ElementType);
        hash.Add(Rank);
        foreach (int size in ArraySizes)
            hash.Add(size);
        foreach (int lowerBound in ArrayLowerBounds)
            hash.Add(lowerBound);
        hash.Add(GenericParameterIndex);
        hash.Add(RawTypeKind);
        foreach (StructuralCloneTypeIdentity argument in TypeArguments)
            hash.Add(argument);
        return hash.ToHashCode();
    }
}
