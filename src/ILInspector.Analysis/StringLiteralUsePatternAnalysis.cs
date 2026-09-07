using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;
using ILInspector.Metadata;
using InertText;

namespace ILInspector.Analysis;

public static class StringLiteralUsePatternAnalysis
{
    /// <summary>Stable product identity for this producer contract.</summary>
    public const string ProducerId =
        "analysis.ldstr.ordinal-substring.v1";

    /// <summary>
    /// Finds every physical <c>ldstr</c> occurrence whose decoded value
    /// contains <paramref name="operand"/> under ordinal comparison.
    /// </summary>
    public static StringLiteralUsePatternResult Inspect(
        AssemblyInspectionSession session,
        StringLiteralUseOperand operand,
        StringLiteralUsePatternBudget budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(operand);
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();

        Guid moduleVersionId = session.ModuleVersionId();
        MethodBodySource bodies = session.MethodBodies;
        int methodCount = bodies.MethodDefinitionCount;
        var occurrences = ImmutableArray.CreateBuilder<StringLiteralUseOccurrence>(
            Math.Min(budget.MaximumOccurrences, 256));

        int methodsVisited = 0;
        int methodBodiesVisited = 0;
        long methodBodyBytesVisited = 0;
        long instructionsVisited = 0;
        long userStringsDecoded = 0;
        long userStringCharactersDecoded = 0;

        StringLiteralUsePatternReceipt Receipt() => new(
            methodsVisited,
            methodBodiesVisited,
            methodBodyBytesVisited,
            instructionsVisited,
            userStringsDecoded,
            userStringCharactersDecoded,
            occurrences.Count);

        StringLiteralUsePatternResult Rejected(
            StringLiteralUseRejectionKind kind,
            StringLiteralUseFailureStage stage,
            int methodToken,
            int? ilOffset = null) =>
            new StringLiteralUsePatternResult.Rejected(
                new StringLiteralUseRejection(
                    kind,
                    stage,
                    new StringLiteralUseFailureSite(
                        moduleVersionId,
                        methodToken,
                        ilOffset)),
                Receipt());

        StringLiteralUsePatternResult Limited(
            StringLiteralUseLimitKind kind) =>
            new StringLiteralUsePatternResult.WorkLimitExceeded(
                kind,
                Receipt());

        if (methodCount > budget.MaximumMethods)
            return Limited(StringLiteralUseLimitKind.Methods);

        if (moduleVersionId == Guid.Empty && methodCount > 0)
        {
            return Rejected(
                StringLiteralUseRejectionKind.UnsupportedInput,
                StringLiteralUseFailureStage.MethodEnumeration,
                0x06000001);
        }

        for (int rowNumber = 1; rowNumber <= methodCount; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            methodsVisited++;
            int expectedMethodToken = 0x06000000 | rowNumber;
            MethodRowDescription method;
            bool described;
            try
            {
                described = bodies.TryDescribeMethod(
                    rowNumber,
                    out method);
            }
            catch (Exception exception) when (
                exception is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                return Rejected(
                    StringLiteralUseRejectionKind.BoundedDecode,
                    StringLiteralUseFailureStage.MethodEnumeration,
                    expectedMethodToken);
            }

            if (!described)
            {
                return Rejected(
                    StringLiteralUseRejectionKind.Incomplete,
                    StringLiteralUseFailureStage.MethodEnumeration,
                    expectedMethodToken);
            }

            if (!method.HasBody)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            long remainingBodyBytes =
                budget.MaximumMethodBodyBytesVisited
                - methodBodyBytesVisited;
            int bodyReadLimit = (int)Math.Min(
                budget.MaximumMethodBodyBytes,
                Math.Min(remainingBodyBytes, int.MaxValue));

            switch (bodies.ReadBounded(method.MetadataToken, bodyReadLimit))
            {
                case BoundedMethodBodyRead.NoBody:
                    return Rejected(
                        StringLiteralUseRejectionKind.Incomplete,
                        StringLiteralUseFailureStage.MethodBody,
                        method.MetadataToken);

                case BoundedMethodBodyRead.ByteLimitExceeded exceeded:
                    return Limited(
                        exceeded.ILByteCount
                            > budget.MaximumMethodBodyBytes
                                ? StringLiteralUseLimitKind.MethodBodyBytes
                                : StringLiteralUseLimitKind.TotalMethodBodyBytes);

                case BoundedMethodBodyRead.Unreadable unreadable:
                    return Rejected(
                        unreadable.Reason
                            == MethodBodyReadFailure.UnsupportedImplementation
                                ? StringLiteralUseRejectionKind.UnsupportedInput
                                : unreadable.Reason
                                    == MethodBodyReadFailure.MalformedBody
                                        ? StringLiteralUseRejectionKind.BoundedDecode
                                        : StringLiteralUseRejectionKind.Incomplete,
                        StringLiteralUseFailureStage.MethodBody,
                        method.MetadataToken);

                case BoundedMethodBodyRead.Available available:
                {
                    methodBodiesVisited++;
                    methodBodyBytesVisited += available.IL.Length;

                    cancellationToken.ThrowIfCancellationRequested();
                    long remainingInstructions =
                        budget.MaximumInstructions - instructionsVisited;
                    int instructionLimit = (int)Math.Min(
                        remainingInstructions,
                        int.MaxValue);
                    int decodedInstructionCount = 0;
                    ImmutableArray<DecodedInstruction> instructions;
                    try
                    {
                        bool completed = InstructionDecoder.TryDecodeBounded(
                            available.IL.AsSpan(),
                            instructionLimit,
                            cancellationToken,
                            out instructions,
                            out decodedInstructionCount);
                        instructionsVisited += decodedInstructionCount;
                        if (!completed)
                            return Limited(StringLiteralUseLimitKind.Instructions);
                    }
                    catch (BadImageFormatException)
                    {
                        instructionsVisited += decodedInstructionCount;
                        return Rejected(
                            StringLiteralUseRejectionKind.BoundedDecode,
                            StringLiteralUseFailureStage.InstructionDecode,
                            method.MetadataToken);
                    }

                    foreach (DecodedInstruction instruction in instructions)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (instruction.OpCode != ILOpCode.Ldstr)
                            continue;

                        int userStringToken =
                            checked((int)instruction.OperandValue);
                        cancellationToken.ThrowIfCancellationRequested();
                        long remainingCharacters =
                            budget.MaximumDecodedUserStringCharacters
                            - userStringCharactersDecoded;
                        int characterLimit = (int)Math.Min(
                            remainingCharacters,
                            int.MaxValue);

                        switch (bodies.ReadBoundedUserString(
                            userStringToken,
                            characterLimit))
                        {
                            case BoundedUserStringRead.CharacterLimitExceeded:
                                return Limited(
                                    StringLiteralUseLimitKind
                                        .DecodedUserStringCharacters);

                            case BoundedUserStringRead.Unreadable:
                                return Rejected(
                                    StringLiteralUseRejectionKind.BoundedDecode,
                                    StringLiteralUseFailureStage.UserString,
                                    method.MetadataToken,
                                    instruction.Offset);

                            case BoundedUserStringRead.Available decoded:
                            {
                                string literal = decoded.Value;
                                userStringsDecoded++;
                                userStringCharactersDecoded += literal.Length;
                                if (!literal.Contains(
                                        operand.RawValue,
                                        StringComparison.Ordinal))
                                {
                                    break;
                                }

                                cancellationToken.ThrowIfCancellationRequested();
                                if (occurrences.Count
                                    == budget.MaximumOccurrences)
                                {
                                    return Limited(
                                        StringLiteralUseLimitKind.Occurrences);
                                }

                                occurrences.Add(new StringLiteralUseOccurrence(
                                    new StringLiteralInstructionAddress(
                                        moduleVersionId,
                                        method.MetadataToken,
                                        instruction.Offset),
                                    userStringToken,
                                    literal.Length,
                                    new InertString(
                                        TextPolicy.Field,
                                        literal)));
                                break;
                            }

                            default:
                                throw new InvalidOperationException(
                                    "Unknown bounded user-string read outcome.");
                        }
                    }

                    break;
                }

                default:
                    throw new InvalidOperationException(
                        "Unknown bounded method-body read outcome.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        StringLiteralUsePatternReceipt receipt = Receipt();
        return occurrences.Count == 0
            ? new StringLiteralUsePatternResult.NoMatch(receipt)
            : new StringLiteralUsePatternResult.Match(
                occurrences.ToImmutable(),
                receipt);
    }
}
