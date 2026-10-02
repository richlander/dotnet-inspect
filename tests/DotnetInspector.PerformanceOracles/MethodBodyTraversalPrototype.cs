using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace DotnetInspector.PerformanceOracles;

/// <summary>The body-scan facts every prototype lane must reproduce exactly.</summary>
public readonly record struct MethodBodyScanSummary(
    int Bodies,
    long Instructions,
    long Calls,
    long Allocations,
    long Throws,
    int IncompleteLayer0 = 0)
{
    public override string ToString() =>
        $"bodies={Bodies};instructions={Instructions};calls={Calls};"
        + $"allocations={Allocations};throws={Throws};"
        + $"incomplete-layer0={IncompleteLayer0}";
}

/// <summary>
/// Prepared managed Method bodies for the traversal scorecard. Image loading,
/// metadata enumeration, body admission, and MethodBodyBlock construction are
/// outside every timed terminal.
/// </summary>
public sealed class PreparedMethodBodies : IDisposable
{
    readonly PEReader _peReader;
    readonly ImmutableArray<MethodBodyBlock> _bodies;
    readonly CallScan _calls = new();
    readonly AllocationScan _allocations = new();
    readonly ThrowScan _throws = new();
    readonly CompositeScan _composite = new();
    readonly HandFusedScan _handFused = new();

    PreparedMethodBodies(PEReader peReader)
    {
        _peReader = peReader;
        if (!peReader.HasMetadata)
            throw new BadImageFormatException("The scorecard asset has no metadata.");

        MetadataReader reader = peReader.GetMetadataReader();
        var bodies = ImmutableArray.CreateBuilder<MethodBodyBlock>();
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0
                || !HasManagedIlBody(method.ImplAttributes))
            {
                continue;
            }

            bodies.Add(peReader.GetMethodBody(method.RelativeVirtualAddress));
        }

        _bodies = bodies.ToImmutable();
    }

    public int BodyCount => _bodies.Length;

    public static IReadOnlyList<ScorecardAsset<PreparedMethodBodies>> LoadAssets(
        IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names = ScorecardAssetNames.FromPaths(paths);
        var assets =
            new List<ScorecardAsset<PreparedMethodBodies>>(paths.Count);
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                var peReader = new PEReader(
                    ImmutableArray.Create(File.ReadAllBytes(paths[i])));
                try
                {
                    assets.Add(
                        new(
                            names[i],
                            new PreparedMethodBodies(peReader)));
                }
                catch
                {
                    peReader.Dispose();
                    throw;
                }
            }

            return assets;
        }
        catch
        {
            foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
                asset.Asset.Dispose();
            throw;
        }
    }

    public MethodBodyScanSummary IndependentStreams()
    {
        _calls.Reset();
        _allocations.Reset();
        _throws.Reset();
        foreach (MethodBodyBlock body in _bodies)
        {
            InstructionDecoder.Visit(body, _calls.Visitor);
            InstructionDecoder.Visit(body, _allocations.Visitor);
            InstructionDecoder.Visit(body, _throws.Visitor);
        }

        return new(
            _bodies.Length,
            _calls.Instructions,
            _calls.Calls,
            _allocations.Allocations,
            _throws.Throws);
    }

    public MethodBodyScanSummary CompositeStream()
    {
        _composite.Reset();
        foreach (MethodBodyBlock body in _bodies)
            InstructionDecoder.Visit(body, _composite.Visitor);
        return _composite.Summary(_bodies.Length);
    }

    public MethodBodyScanSummary HandFusedStream()
    {
        _handFused.Reset();
        foreach (MethodBodyBlock body in _bodies)
            InstructionDecoder.Visit(body, _handFused.Visitor);
        return _handFused.Summary(_bodies.Length);
    }

    public MethodBodyScanSummary MaterializeInstructionsSeparatePasses()
    {
        long instructions = 0;
        long calls = 0;
        long allocations = 0;
        long throws = 0;
        foreach (MethodBodyBlock body in _bodies)
        {
            ImmutableArray<DecodedInstruction> decoded =
                DecodeInstructions(body);
            instructions += decoded.Length;
            ScanSeparatePasses(
                decoded,
                ref calls,
                ref allocations,
                ref throws);
        }

        return new(_bodies.Length, instructions, calls, allocations, throws);
    }

    public MethodBodyScanSummary MaterializeInstructionsFusedPass()
    {
        long instructions = 0;
        long calls = 0;
        long allocations = 0;
        long throws = 0;
        foreach (MethodBodyBlock body in _bodies)
        {
            ImmutableArray<DecodedInstruction> decoded =
                DecodeInstructions(body);
            instructions += decoded.Length;
            ScanFusedPass(
                decoded,
                ref calls,
                ref allocations,
                ref throws);
        }

        return new(_bodies.Length, instructions, calls, allocations, throws);
    }

    public MethodBodyScanSummary BuildLayer0SeparatePasses()
    {
        long instructions = 0;
        long calls = 0;
        long allocations = 0;
        long throws = 0;
        int incompleteLayer0 = 0;
        foreach (MethodBodyBlock body in _bodies)
        {
            MethodInstructions layer0 = MethodInstructions.Decode(body);
            if (!layer0.IsComplete)
                incompleteLayer0++;
            instructions += layer0.Instructions.Length;
            ScanSeparatePasses(
                layer0.Instructions,
                ref calls,
                ref allocations,
                ref throws);
        }

        return new(
            _bodies.Length,
            instructions,
            calls,
            allocations,
            throws,
            incompleteLayer0);
    }

    public MethodBodyScanSummary BuildLayer0FusedPass()
    {
        long instructions = 0;
        long calls = 0;
        long allocations = 0;
        long throws = 0;
        int incompleteLayer0 = 0;
        foreach (MethodBodyBlock body in _bodies)
        {
            MethodInstructions layer0 = MethodInstructions.Decode(body);
            if (!layer0.IsComplete)
                incompleteLayer0++;
            instructions += layer0.Instructions.Length;
            ScanFusedPass(
                layer0.Instructions,
                ref calls,
                ref allocations,
                ref throws);
        }

        return new(
            _bodies.Length,
            instructions,
            calls,
            allocations,
            throws,
            incompleteLayer0);
    }

    public void Dispose() => _peReader.Dispose();

    static ImmutableArray<DecodedInstruction> DecodeInstructions(
        MethodBodyBlock body) =>
        InstructionDecoder.Decode(body.GetILBytes() ?? []);

    static void ScanSeparatePasses(
        ImmutableArray<DecodedInstruction> instructions,
        ref long calls,
        ref long allocations,
        ref long throws)
    {
        foreach (DecodedInstruction instruction in instructions)
        {
            if (CallScan.IsCall(instruction.OpCode))
                calls++;
        }

        foreach (DecodedInstruction instruction in instructions)
        {
            if (AllocationScan.IsAllocation(instruction.OpCode))
                allocations++;
        }

        foreach (DecodedInstruction instruction in instructions)
        {
            if (ThrowScan.IsThrow(instruction.OpCode))
                throws++;
        }
    }

    static void ScanFusedPass(
        ImmutableArray<DecodedInstruction> instructions,
        ref long calls,
        ref long allocations,
        ref long throws)
    {
        foreach (DecodedInstruction instruction in instructions)
        {
            ILOpCode opcode = instruction.OpCode;
            if (CallScan.IsCall(opcode))
                calls++;
            if (AllocationScan.IsAllocation(opcode))
                allocations++;
            if (ThrowScan.IsThrow(opcode))
                throws++;
        }
    }

    static bool HasManagedIlBody(MethodImplAttributes attributes) =>
        (attributes & MethodImplAttributes.CodeTypeMask)
            == MethodImplAttributes.IL
        && (attributes & MethodImplAttributes.ManagedMask)
            == MethodImplAttributes.Managed;

    sealed class CallScan
    {
        internal CallScan() => Visitor = Visit;

        internal Func<ILOpCode, int, int, bool> Visitor { get; }

        internal long Instructions { get; private set; }

        internal long Calls { get; private set; }

        internal void Reset()
        {
            Instructions = 0;
            Calls = 0;
        }

        bool Visit(ILOpCode opcode, int _, int __)
        {
            Instructions++;
            if (IsCall(opcode))
                Calls++;
            return true;
        }

        internal static bool IsCall(ILOpCode opcode) =>
            opcode is ILOpCode.Call
                or ILOpCode.Callvirt
                or ILOpCode.Newobj;
    }

    sealed class AllocationScan
    {
        internal AllocationScan() => Visitor = Visit;

        internal Func<ILOpCode, int, int, bool> Visitor { get; }

        internal long Allocations { get; private set; }

        internal void Reset() => Allocations = 0;

        bool Visit(ILOpCode opcode, int _, int __)
        {
            if (IsAllocation(opcode))
                Allocations++;
            return true;
        }

        internal static bool IsAllocation(ILOpCode opcode) =>
            opcode is ILOpCode.Newobj
                or ILOpCode.Newarr
                or ILOpCode.Box;
    }

    sealed class ThrowScan
    {
        internal ThrowScan() => Visitor = Visit;

        internal Func<ILOpCode, int, int, bool> Visitor { get; }

        internal long Throws { get; private set; }

        internal void Reset() => Throws = 0;

        bool Visit(ILOpCode opcode, int _, int __)
        {
            if (IsThrow(opcode))
                Throws++;
            return true;
        }

        internal static bool IsThrow(ILOpCode opcode) =>
            opcode is ILOpCode.Throw or ILOpCode.Rethrow;
    }

    sealed class CompositeScan
    {
        CallFold _calls;
        AllocationFold _allocations;
        ThrowFold _throws;

        internal CompositeScan() => Visitor = Visit;

        internal Func<ILOpCode, int, int, bool> Visitor { get; }

        internal void Reset()
        {
            _calls = default;
            _allocations = default;
            _throws = default;
        }

        internal MethodBodyScanSummary Summary(int bodies) =>
            new(
                bodies,
                _calls.Instructions,
                _calls.Calls,
                _allocations.Count,
                _throws.Count);

        bool Visit(ILOpCode opcode, int _, int __)
        {
            _calls.Observe(opcode);
            _allocations.Observe(opcode);
            _throws.Observe(opcode);
            return true;
        }
    }

    sealed class HandFusedScan
    {
        long _instructions;
        long _calls;
        long _allocations;
        long _throws;

        internal HandFusedScan() => Visitor = Visit;

        internal Func<ILOpCode, int, int, bool> Visitor { get; }

        internal void Reset()
        {
            _instructions = 0;
            _calls = 0;
            _allocations = 0;
            _throws = 0;
        }

        internal MethodBodyScanSummary Summary(int bodies) =>
            new(bodies, _instructions, _calls, _allocations, _throws);

        bool Visit(ILOpCode opcode, int _, int __)
        {
            _instructions++;
            if (CallScan.IsCall(opcode))
                _calls++;
            if (AllocationScan.IsAllocation(opcode))
                _allocations++;
            if (ThrowScan.IsThrow(opcode))
                _throws++;
            return true;
        }
    }

    struct CallFold
    {
        internal long Instructions;
        internal long Calls;

        internal void Observe(ILOpCode opcode)
        {
            Instructions++;
            if (CallScan.IsCall(opcode))
                Calls++;
        }
    }

    struct AllocationFold
    {
        internal long Count;

        internal void Observe(ILOpCode opcode)
        {
            if (AllocationScan.IsAllocation(opcode))
                Count++;
        }
    }

    struct ThrowFold
    {
        internal long Count;

        internal void Observe(ILOpCode opcode)
        {
            if (ThrowScan.IsThrow(opcode))
                Count++;
        }
    }
}

/// <summary>The exact-agreement and timing columns for shared body traversal.</summary>
public static class MethodBodyTraversalPrototype
{
    static readonly IReadOnlyList<ScorecardClosing> s_closings =
        [ScorecardClosing.Rows];

    public static IReadOnlyList<ScorecardClosing> Closings => s_closings;

    public static ScorecardColumn<PreparedMethodBodies, MethodBodyScanSummary>[]
        Columns() =>
    [
        new(
            "Independent x3",
            static (closing, bodies) =>
                Answer(closing, bodies.IndependentStreams())),
        new(
            "Composite stream",
            static (closing, bodies) =>
                Answer(closing, bodies.CompositeStream())),
        new(
            "Hand-fused stream",
            static (closing, bodies) =>
                Answer(closing, bodies.HandFusedStream())),
        new(
            "Instructions once x3",
            static (closing, bodies) =>
                Answer(
                    closing,
                    bodies.MaterializeInstructionsSeparatePasses())),
        new(
            "Instructions once fused",
            static (closing, bodies) =>
                Answer(
                    closing,
                    bodies.MaterializeInstructionsFusedPass())),
        new(
            "Layer 0 once x3",
            static (closing, bodies) =>
                Answer(closing, bodies.BuildLayer0SeparatePasses())),
        new(
            "Layer 0 once fused",
            static (closing, bodies) =>
                Answer(closing, bodies.BuildLayer0FusedPass())),
    ];

    public static string RowText(MethodBodyScanSummary summary) =>
        summary.ToString();

    static ScorecardAnswer<MethodBodyScanSummary> Answer(
        ScorecardClosing closing,
        MethodBodyScanSummary summary)
    {
        if (closing != ScorecardClosing.Rows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(closing),
                closing,
                "The method-body traversal prototype has one complete-summary terminal.");
        }

        return ScorecardAnswer<MethodBodyScanSummary>.OfRows([summary]);
    }
}
