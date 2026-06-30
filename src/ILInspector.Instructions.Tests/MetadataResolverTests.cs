using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Compiled fixtures whose bodies the typed-stack interpreter reads back through SRM, exercising
/// the metadata-backed resolver end to end (receiver types, return types, EH entry stacks).
/// </summary>
internal static class Fixtures
{
    internal static int StringLengthPlusOne(string s) => s.Length + 1;

    internal static int DivideWithCatch(int x)
    {
        try { return 10 / x; }
        catch (DivideByZeroException) { return -1; }
    }
}

public class MetadataResolverTests
{
    static (MetadataReader Reader, MethodDefinitionHandle Handle, MethodBodyBlock Body) Open(string methodName)
    {
        string location = typeof(Fixtures).Assembly.Location;
        var peReader = new PEReader(File.OpenRead(location));
        var reader = peReader.GetMetadataReader();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name) != methodName)
                continue;
            var declaring = reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name);
            if (declaring != nameof(Fixtures))
                continue;
            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            return (reader, handle, body);
        }
        throw new InvalidOperationException($"Method {methodName} not found.");
    }

    [Fact]
    public void Resolves_call_receiver_type_and_provenance()
    {
        var (reader, handle, body) = Open(nameof(Fixtures.StringLengthPlusOne));
        var mi = MethodInstructions.Decode(body);
        var ts = mi.InterpretStack(reader, handle, body);

        Assert.True(ts.IsComplete, ts.IncompleteReason);

        // The string.Length call (get_Length, zero arguments): its receiver is the argument string,
        // produced by the ldarg.0 at the method entry — exactly the typed-stack witness.
        var call = mi.Instructions.Single(i => i.OpCode is ILOpCode.Call or ILOpCode.Callvirt);
        var receiver = ts.ReceiverAt(call.Offset, argumentsBelow: 0);
        Assert.NotNull(receiver);
        Assert.Equal(StackType.ObjectReference, receiver!.Value.Type);

        var ldarg = mi.Instructions.First(i => i.OpCode is ILOpCode.Ldarg_0 or ILOpCode.Ldarg or ILOpCode.Ldarg_s);
        Assert.Equal(ldarg.Offset, receiver.Value.ProducerOffset);

        // get_Length returns int32; the next instruction sees an int32 on top.
        var afterCall = ts.StackBeforeOffset(call.NextOffset);
        Assert.Equal(StackType.Int32, afterCall[^1].Type);

        // Offset→instruction/block lookup (the join-key surface): the call resolves to itself,
        // a mid-instruction offset resolves to no boundary, and every offset maps to a block.
        Assert.Equal(call, mi.InstructionAt(call.Offset));
        Assert.Null(mi.InstructionAt(call.Offset + 1));
        Assert.True(mi.BlockIndexAt(call.Offset) >= 0);
    }

    [Fact]
    public void Seeds_catch_handler_entry_with_the_exception_object()
    {
        var (reader, handle, body) = Open(nameof(Fixtures.DivideWithCatch));
        var mi = MethodInstructions.Decode(body);
        var ts = mi.InterpretStack(reader, handle, body);

        // A catch handler that discards its exception begins by popping it; the interpreter
        // stays complete only because the handler entry is seeded with the in-flight exception.
        Assert.True(ts.IsComplete, ts.IncompleteReason);

        var catchRegion = mi.Blocks.Regions.Single(r => r.Kind == HandlerKind.Catch);
        var handlerEntry = ts.StackBeforeOffset(catchRegion.HandlerStart);
        var exception = Assert.Single(handlerEntry);
        Assert.Equal(StackType.ObjectReference, exception.Type);
        Assert.Equal(StackValue.NoProducer, exception.ProducerOffset);
    }
}
