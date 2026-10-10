using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Parallel-executor substitution for one complete-profile method packet.
/// The immutable QuerySpace capability plan is shared; retained state is
/// packet-local and discarded after the method result completes.
/// </summary>
internal sealed class CompleteProfileInstructionPacket
{
    static readonly MethodBodyAnalyzerPlan Plan =
        MethodBodyAnalyzerPlanner.Plan(
            MethodBodyAnalyzerDeclarations.DirectCalls,
            MethodBodyAnalyzerDeclarations.CanonicalContext);

    readonly InstructionSequence _sequence;
    readonly ImplementationMetricExecutionRecorder? _recorder;
    readonly MethodImplementationInstructionScan _implementationScan =
        new();
    readonly List<int> _boxOperandTokens = [];
    MethodCallInstructionCounts _callCounts;
    int _newArrayCount;
    int _throwCount;
    bool _scanned;
    bool _materialized;

    internal CompleteProfileInstructionPacket(
        MethodBodyBlock body,
        ImplementationMetricExecutionRecorder? recorder)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (Plan.Source
                != MethodBodyInstructionSourceKind.LazyRetainedSequence
            || Plan.Demand.Access
                != MethodBodyInstructionAccess.RetainedPrefix
            || Plan.Demand.Detail
                != MethodBodyInstructionDetail.SelectiveOperands)
        {
            throw new InvalidOperationException(
                "Complete-profile instruction work requires one retained "
                + "selective-detail Method source.");
        }

        byte[] il = body.GetILBytes() ?? [];
        _sequence = InstructionSequence.CreateResolved(
            ImmutableCollectionsMarshal.AsImmutableArray(il));
        _recorder = recorder;
    }

    internal static MethodBodyAnalyzerPlan InstructionPlan => Plan;

    internal MethodCallAnalysis.DiscoveryCounts Scan()
    {
        if (_scanned)
        {
            return new(
                _callCounts.InvocationCount,
                _callCounts.CallSiteCount);
        }

        try
        {
            InstructionCursor cursor = _sequence.GetCursor();
            int instructionIndex = 0;
            while (cursor.MoveNext())
            {
                InstructionEntry instruction = cursor.Current;
                MethodCallInstructionCounter.Visit(
                    ref _callCounts,
                    instruction.OpCode,
                    instruction.Length);
                _implementationScan.Visit(
                    instruction.OpCode,
                    instruction.Length);
                switch (instruction.OpCode)
                {
                    case ILOpCode.Newarr:
                        _newArrayCount++;
                        break;
                    case ILOpCode.Throw:
                    case ILOpCode.Rethrow:
                        _throwCount++;
                        break;
                    case ILOpCode.Box:
                        _boxOperandTokens.Add(
                            MethodInstructionFacts.OperandInt32(
                                _sequence.Resolve(
                                    instructionIndex)));
                        break;
                }
                instructionIndex++;
            }
            _scanned = true;
            return new(
                _callCounts.InvocationCount,
                _callCounts.CallSiteCount);
        }
        finally
        {
            _recorder?.RecordInstructionSource(
                _sequence.RetainedCount);
        }
    }

    internal MethodInstructions Materialize(
        MethodBodyData body)
    {
        if (!_scanned)
            Scan();
        if (_materialized)
        {
            throw new InvalidOperationException(
                "A complete-profile method packet can materialize only once.");
        }

        MethodInstructions instructions =
            _sequence.Materialize(body);
        _materialized = true;
        _recorder?.RecordInstructionMaterialization();
        return instructions;
    }

    internal MethodImplementationContextMeasurements
        CompleteMeasurements(
            MethodBodyAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_materialized)
        {
            throw new InvalidOperationException(
                "Context measurements require materialized instructions.");
        }
        return _implementationScan.Complete(context);
    }

    internal BodySignals CompleteBodySignals(
        Func<int, bool> isAllocatingBox)
    {
        ArgumentNullException.ThrowIfNull(isAllocatingBox);
        if (!_scanned)
        {
            throw new InvalidOperationException(
                "Body signals require a completed instruction scan.");
        }

        int boxes = 0;
        foreach (int operandToken in _boxOperandTokens)
        {
            if (isAllocatingBox(operandToken))
                boxes++;
        }
        return new(
            _newArrayCount,
            _throwCount,
            Catches: 0,
            Finallys: 0,
            ArrayAllocOffsets: [],
            ThrowOffsets: [],
            Boxes: boxes,
            BoxOffsets: []);
    }
}
