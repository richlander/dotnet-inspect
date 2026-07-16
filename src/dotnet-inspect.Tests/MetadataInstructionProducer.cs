using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

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

    public static List<ILInstructionText>? DisassembleMethod(
        PEReader peReader,
        string typeName,
        string methodName,
        int overloadIndex = 0,
        bool publicOnly = false)
    {
        var reader = peReader.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (reader.GetFullTypeName(reader.GetTypeDefinition(typeHandle)) != typeName)
                continue;

            return DisassembleMethod(
                peReader,
                reader,
                typeHandle,
                methodName,
                overloadIndex,
                publicOnly);
        }

        return null;
    }

    public static List<ILInstructionText>? DisassembleMethod(
        PEReader peReader,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string methodName,
        int overloadIndex,
        bool publicOnly = false)
        => InstructionProducer.DisassembleMethod(
            peReader,
            reader,
            typeHandle,
            methodName,
            overloadIndex,
            new MetadataOperandNameResolver(reader),
            publicOnly);

    public static List<ILInstructionText>? DisassembleMethod(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle methodHandle)
        => InstructionProducer.DisassembleMethod(
            peReader,
            reader,
            methodHandle,
            new MetadataOperandNameResolver(reader));
}
