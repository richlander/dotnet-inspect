using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace DotnetInspector.ILRoundtrip.Tests;

internal static class MetadataInstructionProducer
{
    public static List<ILInstructionText>? Disassemble(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinition method,
        ILSyntax syntax = ILSyntax.Display)
        => InstructionProducer.Disassemble(
            peReader,
            method,
            new MetadataOperandNameResolver(reader, syntax));
}
