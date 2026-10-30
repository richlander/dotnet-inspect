using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;

namespace DotnetInspector.PerformanceOracles;

public readonly record struct StableGetterSummary(
    int Candidates,
    int Stable)
{
    public override string ToString() =>
        $"candidates={Candidates};stable={Stable}";
}

public readonly record struct FlowProbeSummary(
    int Bodies,
    int NewObjectProbes,
    int NewObjectFeedsThrow,
    int BoxProbes,
    int BoxFeedsThrow)
{
    public override string ToString() =>
        $"bodies={Bodies};newobj-probes={NewObjectProbes};"
        + $"newobj-feeds-throw={NewObjectFeedsThrow};"
        + $"box-probes={BoxProbes};box-feeds-throw={BoxFeedsThrow}";
}

public readonly record struct StableGetterMismatch(
    int MethodToken,
    bool Eager,
    bool LazyShallow);

public readonly record struct FlowProbeMismatch(
    int BodyIndex,
    string Kind,
    int Position,
    bool Eager,
    bool LazyShallow);

public readonly record struct ClassifierQuerySummary(
    int Bodies,
    int WithThrows,
    long Calls,
    long Allocations,
    int StableGetters,
    int NewObjectFeedsThrow,
    int BoxFeedsThrow)
{
    public override string ToString() =>
        $"bodies={Bodies};with-throws={WithThrows};calls={Calls};"
        + $"allocations={Allocations};stable-getters={StableGetters};"
        + $"newobj-feeds-throw={NewObjectFeedsThrow};"
        + $"box-feeds-throw={BoxFeedsThrow}";
}

public readonly record struct ClassifierQueryMismatch(
    int BodyIndex,
    string EagerSeparate,
    string EagerFused,
    string Planned);

readonly record struct MethodBodyFlowProbeInput(
    int BodyIndex,
    ImmutableArray<int> NewObjectPositions,
    ImmutableArray<int> BoxPositions)
{
    internal bool HasProbes =>
        !NewObjectPositions.IsEmpty || !BoxPositions.IsEmpty;

    internal static MethodBodyFlowProbeInput Create(
        int bodyIndex,
        ImmutableArray<byte> il)
    {
        var newObjects = ImmutableArray.CreateBuilder<int>();
        var boxes = ImmutableArray.CreateBuilder<int>();
        int offset = 0;
        InstructionDecoder.Visit(
            il.AsSpan(),
            (opcode, _, length) =>
            {
                int nextOffset = offset + length;
                if (opcode == ILOpCode.Newobj)
                    newObjects.Add(nextOffset);
                else if (opcode == ILOpCode.Box)
                    boxes.Add(nextOffset);
                offset = nextOffset;
                return true;
            });
        return new(
            bodyIndex,
            newObjects.ToImmutable(),
            boxes.ToImmutable());
    }
}

public sealed partial class PreparedMethodBodies
{
    public ClassifierQuerySummary EagerClassifierQuerySeparatePasses() =>
        RunEagerClassifierQuery(fusedPass: false);

    public ClassifierQuerySummary EagerClassifierQueryFusedPass() =>
        RunEagerClassifierQuery(fusedPass: true);

    public ClassifierQuerySummary PlannedClassifierQuery()
    {
        if (MethodBodyAnalyzerPlans.Mixed.Source
            != MethodBodyInstructionSourceKind.LazyRetainedSequence)
        {
            throw new InvalidOperationException(
                "The mixed classifier query requires the retained "
                + "instruction source.");
        }

        return LazyShallowClassifierQuery();
    }

    ClassifierQuerySummary LazyShallowClassifierQuery()
    {
        int withThrows = 0;
        long calls = 0;
        long allocations = 0;
        int stableGetters = 0;
        int newObjectThrows = 0;
        int boxThrows = 0;
        int stableIndex = 0;
        for (int bodyIndex = 0; bodyIndex < _bodyIl.Length; bodyIndex++)
        {
            bool stableCandidate =
                stableIndex < _stableGetterBodyIndices.Length
                && _stableGetterBodyIndices[stableIndex] == bodyIndex;
            if (stableCandidate)
                stableIndex++;

            var sequence = new InstructionSequence(_bodyIl[bodyIndex]);
            InstructionCursor cursor = sequence.GetCursor();
            var stable = new LazyStableGetterFold(stableCandidate);
            var newObjectPositions = new List<int>();
            var boxPositions = new List<int>();
            bool hasThrow = false;
            int instructionIndex = 0;
            while (cursor.MoveNext())
            {
                InstructionEntry instruction = cursor.Current;
                ILOpCode opcode = instruction.OpCode;
                if (opcode is ILOpCode.Call
                    or ILOpCode.Callvirt
                    or ILOpCode.Newobj)
                {
                    calls++;
                }
                if (opcode is ILOpCode.Newobj
                    or ILOpCode.Newarr
                    or ILOpCode.Box)
                {
                    allocations++;
                }
                if (opcode is ILOpCode.Throw or ILOpCode.Rethrow)
                    hasThrow = true;
                if (opcode == ILOpCode.Newobj)
                    newObjectPositions.Add(instruction.NextOffset);
                else if (opcode == ILOpCode.Box)
                    boxPositions.Add(instruction.NextOffset);

                stable.Observe(instructionIndex, instruction);
                instructionIndex++;
            }

            if (hasThrow)
                withThrows++;
            if (stable.IsStable(this, sequence))
                stableGetters++;
            foreach (int position in newObjectPositions)
            {
                if (NewObjectFeedsThrowSoon(
                    sequence,
                    _bodyIl[bodyIndex].Length,
                    position))
                {
                    newObjectThrows++;
                }
            }
            foreach (int position in boxPositions)
            {
                if (BoxFeedsThrowSoon(sequence, position))
                    boxThrows++;
            }
        }

        return new(
            _bodyIl.Length,
            withThrows,
            calls,
            allocations,
            stableGetters,
            newObjectThrows,
            boxThrows);
    }

    public ImmutableArray<ClassifierQueryMismatch>
        CheckClassifierQueryAgreement()
    {
        var mismatches =
            ImmutableArray.CreateBuilder<ClassifierQueryMismatch>();
        int stableIndex = 0;
        for (int bodyIndex = 0; bodyIndex < _bodyIl.Length; bodyIndex++)
        {
            bool stableCandidate =
                stableIndex < _stableGetterBodyIndices.Length
                && _stableGetterBodyIndices[stableIndex] == bodyIndex;
            if (stableCandidate)
                stableIndex++;

            MethodClassifierResult eagerSeparate =
                EagerClassifierResult(
                    bodyIndex,
                    stableCandidate,
                    fusedPass: false);
            MethodClassifierResult eagerFused =
                EagerClassifierResult(
                    bodyIndex,
                    stableCandidate,
                    fusedPass: true);
            MethodClassifierResult planned =
                PlannedClassifierResult(
                    bodyIndex,
                    stableCandidate);
            if (eagerSeparate != eagerFused
                || eagerFused != planned)
            {
                mismatches.Add(
                    new(
                        bodyIndex,
                        eagerSeparate.ToString(),
                        eagerFused.ToString(),
                        planned.ToString()));
            }
        }

        return mismatches.ToImmutable();
    }

    public StableGetterSummary EagerStableGetters()
    {
        int stable = 0;
        foreach (int bodyIndex in _stableGetterBodyIndices)
        {
            ImmutableArray<DecodedInstruction> instructions =
                InstructionDecoder.Decode(_bodyIl[bodyIndex].AsSpan());
            if (IsStableGetter(instructions))
                stable++;
        }

        return new(_stableGetterBodyIndices.Length, stable);
    }

    public StableGetterSummary LazyShallowStableGetters()
    {
        int stable = 0;
        foreach (int bodyIndex in _stableGetterBodyIndices)
        {
            var sequence = new InstructionSequence(_bodyIl[bodyIndex]);
            if (IsStableGetter(sequence))
                stable++;
        }

        return new(_stableGetterBodyIndices.Length, stable);
    }

    public ImmutableArray<StableGetterMismatch>
        CheckStableGetterAgreement()
    {
        var mismatches =
            ImmutableArray.CreateBuilder<StableGetterMismatch>();
        foreach (int bodyIndex in _stableGetterBodyIndices)
        {
            bool eager = IsStableGetter(
                InstructionDecoder.Decode(_bodyIl[bodyIndex].AsSpan()));
            bool lazy = IsStableGetter(
                new InstructionSequence(_bodyIl[bodyIndex]));
            if (eager != lazy)
            {
                mismatches.Add(
                    new(
                        MetadataTokens.GetToken(
                            _methodHandles[bodyIndex]),
                        eager,
                        lazy));
            }
        }

        return mismatches.ToImmutable();
    }

    public FlowProbeSummary EagerFlowProbes()
    {
        ImmutableArray<MethodBodyFlowProbeInput> inputs =
            FlowProbeInputs;
        int newObjectProbes = 0;
        int newObjectThrows = 0;
        int boxProbes = 0;
        int boxThrows = 0;
        foreach (MethodBodyFlowProbeInput input in inputs)
        {
            ImmutableArray<DecodedInstruction> instructions =
                InstructionDecoder.Decode(
                    _bodyIl[input.BodyIndex].AsSpan());
            foreach (int position in input.NewObjectPositions)
            {
                newObjectProbes++;
                if (NewObjectFeedsThrowSoon(
                    instructions,
                    _bodyIl[input.BodyIndex].Length,
                    position))
                {
                    newObjectThrows++;
                }
            }
            foreach (int position in input.BoxPositions)
            {
                boxProbes++;
                if (BoxFeedsThrowSoon(instructions, position))
                    boxThrows++;
            }
        }

        return new(
            inputs.Length,
            newObjectProbes,
            newObjectThrows,
            boxProbes,
            boxThrows);
    }

    public FlowProbeSummary LazyShallowFlowProbes()
    {
        ImmutableArray<MethodBodyFlowProbeInput> inputs =
            FlowProbeInputs;
        int newObjectProbes = 0;
        int newObjectThrows = 0;
        int boxProbes = 0;
        int boxThrows = 0;
        foreach (MethodBodyFlowProbeInput input in inputs)
        {
            var sequence =
                new InstructionSequence(_bodyIl[input.BodyIndex]);
            foreach (int position in input.NewObjectPositions)
            {
                newObjectProbes++;
                if (NewObjectFeedsThrowSoon(
                    sequence,
                    _bodyIl[input.BodyIndex].Length,
                    position))
                {
                    newObjectThrows++;
                }
            }
            foreach (int position in input.BoxPositions)
            {
                boxProbes++;
                if (BoxFeedsThrowSoon(sequence, position))
                    boxThrows++;
            }
        }

        return new(
            inputs.Length,
            newObjectProbes,
            newObjectThrows,
            boxProbes,
            boxThrows);
    }

    public ImmutableArray<FlowProbeMismatch>
        CheckFlowProbeAgreement()
    {
        ImmutableArray<MethodBodyFlowProbeInput> inputs =
            FlowProbeInputs;
        var mismatches =
            ImmutableArray.CreateBuilder<FlowProbeMismatch>();
        foreach (MethodBodyFlowProbeInput input in inputs)
        {
            ImmutableArray<byte> il = _bodyIl[input.BodyIndex];
            ImmutableArray<DecodedInstruction> instructions =
                InstructionDecoder.Decode(il.AsSpan());
            var sequence = new InstructionSequence(il);
            foreach (int position in input.NewObjectPositions)
            {
                bool eager = NewObjectFeedsThrowSoon(
                    instructions,
                    il.Length,
                    position);
                bool lazy = NewObjectFeedsThrowSoon(
                    sequence,
                    il.Length,
                    position);
                if (eager != lazy)
                {
                    mismatches.Add(
                        new(
                            input.BodyIndex,
                            "newobj",
                            position,
                            eager,
                            lazy));
                }
            }
            foreach (int position in input.BoxPositions)
            {
                bool eager = BoxFeedsThrowSoon(
                    instructions,
                    position);
                bool lazy = BoxFeedsThrowSoon(
                    sequence,
                    position);
                if (eager != lazy)
                {
                    mismatches.Add(
                        new(
                            input.BodyIndex,
                            "box",
                            position,
                            eager,
                            lazy));
                }
            }
        }

        return mismatches.ToImmutable();
    }

    bool IsStableGetter(
        ImmutableArray<DecodedInstruction> instructions)
    {
        DecodedInstruction? first = null;
        DecodedInstruction? fieldLoad = null;
        DecodedInstruction? third = null;
        int count = 0;
        foreach (DecodedInstruction instruction in instructions)
        {
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            switch (count++)
            {
                case 0:
                    first = instruction;
                    break;
                case 1:
                    fieldLoad = instruction;
                    break;
                case 2:
                    third = instruction;
                    break;
                default:
                    return false;
            }
        }

        return count == 3
            && first is { OpCode: ILOpCode.Ldarg_0 }
            && fieldLoad is { OpCode: ILOpCode.Ldfld }
            && third is { OpCode: ILOpCode.Ret }
            && IsReadonlyField(fieldLoad);
    }

    bool IsStableGetter(InstructionSequence sequence)
    {
        InstructionCursor cursor = sequence.GetCursor();
        int entryIndex = 0;
        int nonNopCount = 0;
        ILOpCode first = default;
        ILOpCode second = default;
        ILOpCode third = default;
        int fieldLoadIndex = -1;
        while (cursor.MoveNext())
        {
            InstructionEntry instruction = cursor.Current;
            int currentIndex = entryIndex++;
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            switch (nonNopCount++)
            {
                case 0:
                    first = instruction.OpCode;
                    break;
                case 1:
                    second = instruction.OpCode;
                    fieldLoadIndex = currentIndex;
                    break;
                case 2:
                    third = instruction.OpCode;
                    break;
                default:
                    return false;
            }
        }

        return nonNopCount == 3
            && first == ILOpCode.Ldarg_0
            && second == ILOpCode.Ldfld
            && third == ILOpCode.Ret
            && IsReadonlyField(sequence.Resolve(fieldLoadIndex));
    }

    bool IsReadonlyField(DecodedInstruction fieldLoad)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(
            checked((int)fieldLoad.OperandValue));
        return handle.Kind == HandleKind.FieldDefinition
            && (_reader.GetFieldDefinition(
                    (FieldDefinitionHandle)handle).Attributes
                & FieldAttributes.InitOnly) != 0;
    }

    static bool NewObjectFeedsThrowSoon(
        ImmutableArray<DecodedInstruction> instructions,
        int ilLength,
        int position)
    {
        var visitedOffsets = new HashSet<int>();
        int index = IndexAtOrAfter(instructions, position);
        for (int steps = 0;
            steps < 8 && index < instructions.Length;
            steps++, index++)
        {
            DecodedInstruction instruction = instructions[index];
            if (!visitedOffsets.Add(instruction.Offset))
                return false;
            ILOpCode operation = instruction.OpCode;
            if (operation is ILOpCode.Throw or ILOpCode.Rethrow)
                return true;
            if (operation is ILOpCode.Br or ILOpCode.Br_s)
            {
                if (instruction.BranchTargets.Length != 1)
                    return false;
                int target = instruction.BranchTargets[0];
                if (target < 0 || target >= ilLength)
                    return false;
                index = IndexAtOrAfter(instructions, target) - 1;
                continue;
            }
            if (IsControlFlowDivergent(operation))
                return false;
        }

        return false;
    }

    static bool NewObjectFeedsThrowSoon(
        InstructionSequence sequence,
        int ilLength,
        int position)
    {
        var visitedOffsets = new HashSet<int>();
        int index = sequence.IndexAtOrAfter(position);
        for (int steps = 0; steps < 8; steps++, index++)
        {
            if (!sequence.TryGet(
                index,
                out InstructionEntry instruction))
            {
                return false;
            }
            if (!visitedOffsets.Add(instruction.Offset))
                return false;
            ILOpCode operation = instruction.OpCode;
            if (operation is ILOpCode.Throw or ILOpCode.Rethrow)
                return true;
            if (operation is ILOpCode.Br or ILOpCode.Br_s)
            {
                DecodedInstruction resolved = sequence.Resolve(index);
                if (resolved.BranchTargets.Length != 1)
                    return false;
                int target = resolved.BranchTargets[0];
                if (target < 0 || target >= ilLength)
                    return false;
                index = sequence.IndexAtOrAfter(target) - 1;
                continue;
            }
            if (IsControlFlowDivergent(operation))
                return false;
        }

        return false;
    }

    static bool BoxFeedsThrowSoon(
        ImmutableArray<DecodedInstruction> instructions,
        int position)
    {
        int index = IndexAtOrAfter(instructions, position);
        for (int steps = 0;
            steps < 6 && index < instructions.Length;
            steps++, index++)
        {
            ILOpCode operation = instructions[index].OpCode;
            if (operation is ILOpCode.Throw or ILOpCode.Rethrow)
                return true;
            if (IsControlFlowDivergent(operation))
                return false;
        }

        return false;
    }

    static bool BoxFeedsThrowSoon(
        InstructionSequence sequence,
        int position)
    {
        int index = sequence.IndexAtOrAfter(position);
        for (int steps = 0; steps < 6; steps++, index++)
        {
            if (!sequence.TryGet(
                index,
                out InstructionEntry instruction))
            {
                return false;
            }
            ILOpCode operation = instruction.OpCode;
            if (operation is ILOpCode.Throw or ILOpCode.Rethrow)
                return true;
            if (IsControlFlowDivergent(operation))
                return false;
        }

        return false;
    }

    static int IndexAtOrAfter(
        ImmutableArray<DecodedInstruction> instructions,
        int offset)
    {
        int lo = 0;
        int hi = instructions.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (instructions[mid].Offset < offset)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    static bool IsControlFlowDivergent(ILOpCode operation) =>
        operation is
            ILOpCode.Br
            or ILOpCode.Br_s
            or ILOpCode.Brtrue
            or ILOpCode.Brtrue_s
            or ILOpCode.Brfalse
            or ILOpCode.Brfalse_s
            or ILOpCode.Beq
            or ILOpCode.Beq_s
            or ILOpCode.Bne_un
            or ILOpCode.Bne_un_s
            or ILOpCode.Bge
            or ILOpCode.Bge_s
            or ILOpCode.Bgt
            or ILOpCode.Bgt_s
            or ILOpCode.Ble
            or ILOpCode.Ble_s
            or ILOpCode.Blt
            or ILOpCode.Blt_s
            or ILOpCode.Bge_un
            or ILOpCode.Bge_un_s
            or ILOpCode.Bgt_un
            or ILOpCode.Bgt_un_s
            or ILOpCode.Ble_un
            or ILOpCode.Ble_un_s
            or ILOpCode.Blt_un
            or ILOpCode.Blt_un_s
            or ILOpCode.Switch
            or ILOpCode.Leave
            or ILOpCode.Leave_s
            or ILOpCode.Ret
            or ILOpCode.Throw
            or ILOpCode.Rethrow
            or ILOpCode.Endfinally
            or ILOpCode.Endfilter
            or ILOpCode.Jmp;

    ClassifierQuerySummary RunEagerClassifierQuery(bool fusedPass)
    {
        int withThrows = 0;
        long calls = 0;
        long allocations = 0;
        int stableGetters = 0;
        int newObjectThrows = 0;
        int boxThrows = 0;
        int stableIndex = 0;
        for (int bodyIndex = 0; bodyIndex < _bodyIl.Length; bodyIndex++)
        {
            bool stableCandidate =
                stableIndex < _stableGetterBodyIndices.Length
                && _stableGetterBodyIndices[stableIndex] == bodyIndex;
            if (stableCandidate)
                stableIndex++;

            MethodClassifierResult result =
                EagerClassifierResult(
                    bodyIndex,
                    stableCandidate,
                    fusedPass);
            if (result.HasThrow)
                withThrows++;
            calls += result.Calls;
            allocations += result.Allocations;
            if (result.StableGetter)
                stableGetters++;
            newObjectThrows += result.NewObjectFeedsThrow;
            boxThrows += result.BoxFeedsThrow;
        }

        return new(
            _bodyIl.Length,
            withThrows,
            calls,
            allocations,
            stableGetters,
            newObjectThrows,
            boxThrows);
    }

    MethodClassifierResult EagerClassifierResult(
        int bodyIndex,
        bool stableCandidate,
        bool fusedPass)
    {
        ImmutableArray<DecodedInstruction> instructions =
            InstructionDecoder.Decode(_bodyIl[bodyIndex].AsSpan());
        bool hasThrow = false;
        int calls = 0;
        int allocations = 0;
        var newObjectPositions = new List<int>();
        var boxPositions = new List<int>();
        bool stableGetter;
        if (fusedPass)
        {
            var stable = new EagerStableGetterFold(stableCandidate);
            foreach (DecodedInstruction instruction in instructions)
            {
                ILOpCode opcode = instruction.OpCode;
                if (opcode is ILOpCode.Call
                    or ILOpCode.Callvirt
                    or ILOpCode.Newobj)
                {
                    calls++;
                }
                if (opcode is ILOpCode.Newobj
                    or ILOpCode.Newarr
                    or ILOpCode.Box)
                {
                    allocations++;
                }
                if (opcode is ILOpCode.Throw or ILOpCode.Rethrow)
                    hasThrow = true;
                if (opcode == ILOpCode.Newobj)
                    newObjectPositions.Add(instruction.NextOffset);
                else if (opcode == ILOpCode.Box)
                    boxPositions.Add(instruction.NextOffset);
                stable.Observe(instruction);
            }
            stableGetter = stable.IsStable(this);
        }
        else
        {
            foreach (DecodedInstruction instruction in instructions)
            {
                if (instruction.OpCode
                    is ILOpCode.Throw or ILOpCode.Rethrow)
                {
                    hasThrow = true;
                    break;
                }
            }
            foreach (DecodedInstruction instruction in instructions)
            {
                if (instruction.OpCode is ILOpCode.Call
                    or ILOpCode.Callvirt
                    or ILOpCode.Newobj)
                {
                    calls++;
                }
            }
            foreach (DecodedInstruction instruction in instructions)
            {
                ILOpCode opcode = instruction.OpCode;
                if (opcode is ILOpCode.Newobj
                    or ILOpCode.Newarr
                    or ILOpCode.Box)
                {
                    allocations++;
                }
                if (opcode == ILOpCode.Newobj)
                    newObjectPositions.Add(instruction.NextOffset);
                else if (opcode == ILOpCode.Box)
                    boxPositions.Add(instruction.NextOffset);
            }
            stableGetter =
                stableCandidate && IsStableGetter(instructions);
        }

        int newObjectThrows = 0;
        foreach (int position in newObjectPositions)
        {
            if (NewObjectFeedsThrowSoon(
                instructions,
                _bodyIl[bodyIndex].Length,
                position))
            {
                newObjectThrows++;
            }
        }
        int boxThrows = 0;
        foreach (int position in boxPositions)
        {
            if (BoxFeedsThrowSoon(instructions, position))
                boxThrows++;
        }

        return new(
            hasThrow,
            calls,
            allocations,
            stableGetter,
            newObjectThrows,
            boxThrows);
    }

    MethodClassifierResult LazyClassifierResult(
        int bodyIndex,
        bool stableCandidate)
    {
        var sequence = new InstructionSequence(_bodyIl[bodyIndex]);
        InstructionCursor cursor = sequence.GetCursor();
        var stable = new LazyStableGetterFold(stableCandidate);
        var newObjectPositions = new List<int>();
        var boxPositions = new List<int>();
        bool hasThrow = false;
        int calls = 0;
        int allocations = 0;
        int instructionIndex = 0;
        while (cursor.MoveNext())
        {
            InstructionEntry instruction = cursor.Current;
            ILOpCode opcode = instruction.OpCode;
            if (opcode is ILOpCode.Call
                or ILOpCode.Callvirt
                or ILOpCode.Newobj)
            {
                calls++;
            }
            if (opcode is ILOpCode.Newobj
                or ILOpCode.Newarr
                or ILOpCode.Box)
            {
                allocations++;
            }
            if (opcode is ILOpCode.Throw or ILOpCode.Rethrow)
                hasThrow = true;
            if (opcode == ILOpCode.Newobj)
                newObjectPositions.Add(instruction.NextOffset);
            else if (opcode == ILOpCode.Box)
                boxPositions.Add(instruction.NextOffset);
            stable.Observe(instructionIndex, instruction);
            instructionIndex++;
        }

        int newObjectThrows = 0;
        foreach (int position in newObjectPositions)
        {
            if (NewObjectFeedsThrowSoon(
                sequence,
                _bodyIl[bodyIndex].Length,
                position))
            {
                newObjectThrows++;
            }
        }
        int boxThrows = 0;
        foreach (int position in boxPositions)
        {
            if (BoxFeedsThrowSoon(sequence, position))
                boxThrows++;
        }

        return new(
            hasThrow,
            calls,
            allocations,
            stable.IsStable(this, sequence),
            newObjectThrows,
            boxThrows);
    }

    MethodClassifierResult PlannedClassifierResult(
        int bodyIndex,
        bool stableCandidate)
    {
        if (MethodBodyAnalyzerPlans.Mixed.Source
            != MethodBodyInstructionSourceKind.LazyRetainedSequence)
        {
            throw new InvalidOperationException(
                "The mixed classifier query requires the retained "
                + "instruction source.");
        }

        return LazyClassifierResult(bodyIndex, stableCandidate);
    }

    readonly record struct MethodClassifierResult(
        bool HasThrow,
        int Calls,
        int Allocations,
        bool StableGetter,
        int NewObjectFeedsThrow,
        int BoxFeedsThrow);

    struct EagerStableGetterFold(bool enabled)
    {
        readonly bool _enabled = enabled;
        int _count;
        ILOpCode _first;
        DecodedInstruction? _fieldLoad;
        ILOpCode _third;
        bool _rejected;

        internal void Observe(DecodedInstruction instruction)
        {
            if (!_enabled
                || _rejected
                || instruction.OpCode == ILOpCode.Nop)
            {
                return;
            }
            switch (_count++)
            {
                case 0:
                    _first = instruction.OpCode;
                    break;
                case 1:
                    _fieldLoad = instruction;
                    break;
                case 2:
                    _third = instruction.OpCode;
                    break;
                default:
                    _rejected = true;
                    break;
            }
        }

        internal readonly bool IsStable(
            PreparedMethodBodies bodies) =>
            _enabled
            && !_rejected
            && _count == 3
            && _first == ILOpCode.Ldarg_0
            && _fieldLoad is { OpCode: ILOpCode.Ldfld } fieldLoad
            && _third == ILOpCode.Ret
            && bodies.IsReadonlyField(fieldLoad);
    }

    struct LazyStableGetterFold(bool enabled)
    {
        readonly bool _enabled = enabled;
        int _count;
        ILOpCode _first;
        int _fieldLoadIndex;
        ILOpCode _fieldLoadOpCode;
        ILOpCode _third;
        bool _rejected;

        internal void Observe(
            int index,
            InstructionEntry instruction)
        {
            if (!_enabled
                || _rejected
                || instruction.OpCode == ILOpCode.Nop)
            {
                return;
            }
            switch (_count++)
            {
                case 0:
                    _first = instruction.OpCode;
                    break;
                case 1:
                    _fieldLoadIndex = index;
                    _fieldLoadOpCode = instruction.OpCode;
                    break;
                case 2:
                    _third = instruction.OpCode;
                    break;
                default:
                    _rejected = true;
                    break;
            }
        }

        internal readonly bool IsStable(
            PreparedMethodBodies bodies,
            InstructionSequence sequence) =>
            _enabled
            && !_rejected
            && _count == 3
            && _first == ILOpCode.Ldarg_0
            && _fieldLoadOpCode == ILOpCode.Ldfld
            && _third == ILOpCode.Ret
            && bodies.IsReadonlyField(
                sequence.Resolve(_fieldLoadIndex));
    }
}

public static class StableGetterPrototype
{
    static readonly IReadOnlyList<ScorecardClosing> s_closings =
        [ScorecardClosing.Rows];

    public static IReadOnlyList<ScorecardClosing> Closings => s_closings;

    public static ScorecardColumn<
        PreparedMethodBodies,
        StableGetterSummary>[] Columns() =>
    [
        new(
            "Eager decoded instructions",
            static (closing, bodies) =>
                Answer(closing, bodies.EagerStableGetters())),
        new(
            "Lazy shallow cursor",
            static (closing, bodies) =>
                Answer(closing, bodies.LazyShallowStableGetters())),
    ];

    public static string RowText(StableGetterSummary summary) =>
        summary.ToString();

    static ScorecardAnswer<StableGetterSummary> Answer(
        ScorecardClosing closing,
        StableGetterSummary summary) =>
        closing == ScorecardClosing.Rows
            ? ScorecardAnswer<StableGetterSummary>.OfRows([summary])
            : throw new ArgumentOutOfRangeException(nameof(closing));
}

public static class FlowProbePrototype
{
    static readonly IReadOnlyList<ScorecardClosing> s_closings =
        [ScorecardClosing.Rows];

    public static IReadOnlyList<ScorecardClosing> Closings => s_closings;

    public static ScorecardColumn<
        PreparedMethodBodies,
        FlowProbeSummary>[] Columns() =>
    [
        new(
            "Eager decoded instructions",
            static (closing, bodies) =>
                Answer(closing, bodies.EagerFlowProbes())),
        new(
            "Lazy shallow indexed",
            static (closing, bodies) =>
                Answer(closing, bodies.LazyShallowFlowProbes())),
    ];

    public static string RowText(FlowProbeSummary summary) =>
        summary.ToString();

    static ScorecardAnswer<FlowProbeSummary> Answer(
        ScorecardClosing closing,
        FlowProbeSummary summary) =>
        closing == ScorecardClosing.Rows
            ? ScorecardAnswer<FlowProbeSummary>.OfRows([summary])
            : throw new ArgumentOutOfRangeException(nameof(closing));
}

public static class ClassifierQueryPrototype
{
    static readonly IReadOnlyList<ScorecardClosing> s_closings =
        [ScorecardClosing.Rows];

    public static IReadOnlyList<ScorecardClosing> Closings => s_closings;

    public static ScorecardColumn<
        PreparedMethodBodies,
        ClassifierQuerySummary>[] Columns() =>
    [
        new(
            "Eager decoded separate passes",
            static (closing, bodies) =>
                Answer(
                    closing,
                    bodies.EagerClassifierQuerySeparatePasses())),
        new(
            "Eager decoded fused pass",
            static (closing, bodies) =>
                Answer(
                    closing,
                    bodies.EagerClassifierQueryFusedPass())),
        new(
            "Planner-selected lazy shared",
            static (closing, bodies) =>
                Answer(
                    closing,
                    bodies.PlannedClassifierQuery())),
    ];

    public static string RowText(ClassifierQuerySummary summary) =>
        summary.ToString();

    static ScorecardAnswer<ClassifierQuerySummary> Answer(
        ScorecardClosing closing,
        ClassifierQuerySummary summary) =>
        closing == ScorecardClosing.Rows
            ? ScorecardAnswer<ClassifierQuerySummary>.OfRows([summary])
            : throw new ArgumentOutOfRangeException(nameof(closing));
}
