using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace DotnetInspector.PerformanceOracles;

/// <summary>
/// The number of admitted Method bodies containing an explicit throw opcode.
/// </summary>
public readonly record struct MethodThrowPresenceSummary(
    int Bodies,
    int WithThrows)
{
    public override string ToString() =>
        $"bodies={Bodies};with-throws={WithThrows}";
}

public readonly record struct MethodThrowPresenceMismatch(
    int BodyIndex,
    bool Streaming,
    bool LazyShallow,
    bool EagerDecoded);

public sealed partial class PreparedMethodBodies
{
    readonly ThrowPresenceScan _throwPresence = new();

    public MethodThrowPresenceSummary StreamingThrowPresence()
    {
        int throws = 0;
        foreach (ImmutableArray<byte> il in _bodyIl)
        {
            if (StreamingContainsThrow(il))
                throws++;
        }

        return new(_bodyIl.Length, throws);
    }

    public MethodThrowPresenceSummary PlannedThrowPresence() =>
        MethodBodyAnalyzerPlans.ThrowPresence.Source switch
        {
            MethodBodyInstructionSourceKind.NoRetentionStream =>
                StreamingThrowPresence(),
            MethodBodyInstructionSourceKind.LazyRetainedSequence =>
                LazyShallowThrowPresence(),
            _ => throw new InvalidOperationException(
                "Unknown Method-body instruction source."),
        };

    public MethodThrowPresenceSummary LazyShallowThrowPresence()
    {
        int throws = 0;
        foreach (ImmutableArray<byte> il in _bodyIl)
        {
            if (LazyShallowContainsThrow(il))
                throws++;
        }

        return new(_bodyIl.Length, throws);
    }

    public MethodThrowPresenceSummary EagerDecodedThrowPresence()
    {
        int throws = 0;
        foreach (ImmutableArray<byte> il in _bodyIl)
        {
            if (EagerDecodedContainsThrow(il))
                throws++;
        }

        return new(_bodyIl.Length, throws);
    }

    public ImmutableArray<MethodThrowPresenceMismatch>
        CheckThrowPresenceAgreement()
    {
        var mismatches =
            ImmutableArray.CreateBuilder<MethodThrowPresenceMismatch>();
        for (int i = 0; i < _bodyIl.Length; i++)
        {
            ImmutableArray<byte> il = _bodyIl[i];
            bool streaming = StreamingContainsThrow(il);
            bool lazyShallow = LazyShallowContainsThrow(il);
            bool eagerDecoded = EagerDecodedContainsThrow(il);
            if (lazyShallow != streaming || eagerDecoded != streaming)
            {
                mismatches.Add(
                    new(i, streaming, lazyShallow, eagerDecoded));
            }
        }

        return mismatches.ToImmutable();
    }

    static bool IsThrow(ILOpCode opcode) =>
        opcode is ILOpCode.Throw or ILOpCode.Rethrow;

    bool StreamingContainsThrow(ImmutableArray<byte> il)
    {
        _throwPresence.Reset();
        InstructionDecoder.Visit(il.AsSpan(), _throwPresence.Visitor);
        return _throwPresence.Found;
    }

    static bool LazyShallowContainsThrow(ImmutableArray<byte> il)
    {
        var sequence = new InstructionSequence(il);
        InstructionCursor cursor = sequence.GetCursor();
        while (cursor.MoveNext())
        {
            if (IsThrow(cursor.Current.OpCode))
                return true;
        }

        return false;
    }

    static bool EagerDecodedContainsThrow(ImmutableArray<byte> il)
    {
        ImmutableArray<DecodedInstruction> instructions =
            InstructionDecoder.Decode(il.AsSpan());
        foreach (DecodedInstruction instruction in instructions)
        {
            if (IsThrow(instruction.OpCode))
                return true;
        }

        return false;
    }

    sealed class ThrowPresenceScan
    {
        internal ThrowPresenceScan() => Visitor = Visit;

        internal Func<ILOpCode, int, int, bool> Visitor { get; }

        internal bool Found { get; private set; }

        internal void Reset() => Found = false;

        bool Visit(ILOpCode opcode, int _, int __)
        {
            Found = IsThrow(opcode);
            return !Found;
        }
    }
}

/// <summary>
/// Exact-agreement and timing columns for demand-driven throw presence.
/// </summary>
public static class MethodBodyDemandPrototype
{
    static readonly IReadOnlyList<ScorecardClosing> s_closings =
        [ScorecardClosing.Rows];

    public static IReadOnlyList<ScorecardClosing> Closings => s_closings;

    public static ScorecardColumn<
        PreparedMethodBodies,
        MethodThrowPresenceSummary>[] Columns() =>
    [
        new(
            "Planner-selected stream",
            static (closing, bodies) =>
                Answer(closing, bodies.PlannedThrowPresence())),
        new(
            "Lazy shallow cursor",
            static (closing, bodies) =>
                Answer(closing, bodies.LazyShallowThrowPresence())),
        new(
            "Eager decoded instructions",
            static (closing, bodies) =>
                Answer(closing, bodies.EagerDecodedThrowPresence())),
    ];

    public static string RowText(MethodThrowPresenceSummary summary) =>
        summary.ToString();

    static ScorecardAnswer<MethodThrowPresenceSummary> Answer(
        ScorecardClosing closing,
        MethodThrowPresenceSummary summary)
    {
        if (closing != ScorecardClosing.Rows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(closing),
                closing,
                "The method-body demand prototype has one complete-summary terminal.");
        }

        return ScorecardAnswer<MethodThrowPresenceSummary>.OfRows([summary]);
    }
}
