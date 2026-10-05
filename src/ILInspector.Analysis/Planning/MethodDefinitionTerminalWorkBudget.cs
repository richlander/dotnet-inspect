using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis.Planning;

internal sealed class MethodDefinitionTerminalWorkBudget
{
    readonly MethodDefinitionTerminalWorkLimits _limits;
    readonly MethodDefinitionHandleCoverageBuilder _admittedMethods = new();
    long _encodedIlBytes;
    MethodDefinitionTerminalWorkLimitKind? _reachedLimit;
    int? _reachedAtMethodToken;

    internal MethodDefinitionTerminalWorkBudget(
        MethodDefinitionTerminalWorkLimits limits) =>
        _limits = limits;

    internal void RequireBodyCapacity(
        MethodDefinitionHandle method)
    {
        if (_admittedMethods.Contains(method))
            return;
        if (_admittedMethods.Count >= _limits.MaximumBodies)
        {
            ReachLimit(
                MetadataTokens.GetToken(method),
                MethodDefinitionTerminalWorkLimitKind.Bodies);
        }
    }

    internal void Admit(
        MethodDefinitionHandle method,
        int encodedIlBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedIlBytes);
        if (_admittedMethods.Contains(method))
            return;

        RequireBodyCapacity(method);

        if (encodedIlBytes > _limits.MaximumEncodedIlBytes - _encodedIlBytes)
        {
            ReachLimit(
                MetadataTokens.GetToken(method),
                MethodDefinitionTerminalWorkLimitKind.EncodedIlBytes);
        }

        _admittedMethods.Add(method);
        _encodedIlBytes += encodedIlBytes;
    }

    internal MethodDefinitionTerminalWorkCoverage Build() =>
        new(
            _admittedMethods.Count,
            _encodedIlBytes,
            _reachedLimit,
            _reachedAtMethodToken);

    void ReachLimit(
        int methodToken,
        MethodDefinitionTerminalWorkLimitKind limit)
    {
        _reachedLimit = limit;
        _reachedAtMethodToken = methodToken;
        string dimension = limit switch
        {
            MethodDefinitionTerminalWorkLimitKind.Bodies =>
                "terminal physical-body",
            MethodDefinitionTerminalWorkLimitKind.EncodedIlBytes =>
                "terminal encoded-IL-byte",
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };
        throw new MethodDefinitionTerminalWorkLimitExceededException(
            $"Method source {dimension} limit was exhausted at "
            + $"0x{methodToken:X8}.");
    }
}

internal sealed class MethodDefinitionTerminalWorkLimitExceededException(
    string message)
    : InvalidOperationException(message);
