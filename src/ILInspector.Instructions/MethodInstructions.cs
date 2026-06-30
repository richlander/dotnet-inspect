using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Entry point for the typed-stack substrate: decode a method body to typed instructions,
/// EH-aware basic blocks, and a per-instruction typed evaluation stack with value provenance.
/// One decode + one block builder, intended to subsume the copies in <c>ReachingDefinitions</c>
/// and the decompiler importer. SRM-only; no IrNode, structuring, C#, Roslyn, or assembly loading.
/// </summary>
public sealed record MethodInstructions(
    ImmutableArray<DecodedInstruction> Instructions,
    BlockGraph Blocks,
    TypedStackResult TypedStack)
{
    /// <summary>True when decode, blocks, and the typed stack all completed without falling closed.</summary>
    public bool IsComplete => Blocks.IsComplete && TypedStack.IsComplete;

    /// <summary>Decode + block + typed-stack from raw IL and exception regions, with an explicit resolver.</summary>
    public static MethodInstructions Create(
        byte[] il,
        int ilLength,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        bool methodReturnsValue,
        IStackTypeResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(il);
        var instructions = InstructionDecoder.Decode(il);
        var blocks = BlockGraph.Build(ilLength, instructions, exceptionRegions);
        var typedStack = StackTypeInterpreter.Interpret(instructions, blocks, methodReturnsValue, resolver);
        return new MethodInstructions(instructions, blocks, typedStack);
    }

    /// <summary>Decode + block + typed-stack from a method body, wiring an SRM-backed resolver from the reader.</summary>
    public static MethodInstructions Create(MetadataReader reader, MethodDefinitionHandle method, MethodBodyBlock body)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(body);
        byte[] il = body.GetILBytes() ?? [];
        var resolver = MetadataStackTypeResolver.Create(reader, method, body);
        return Create(il, il.Length, body.ExceptionRegions, resolver.MethodReturnsValue, resolver);
    }
}
