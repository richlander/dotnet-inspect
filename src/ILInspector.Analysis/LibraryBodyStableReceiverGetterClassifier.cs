using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Classifies primary-image getter calls whose receiver is read from a
/// readonly field and caches each eligible getter's body judgment.
/// </summary>
internal sealed class LibraryBodyStableReceiverGetterClassifier
{
    readonly MetadataReader _reader;
    readonly PEReader _peReader;
    readonly Action<MethodDefinitionHandle>? _getterClassified;
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<bool>> _stableReceiverGetters = new();

    internal LibraryBodyStableReceiverGetterClassifier(
        MetadataReader reader,
        PEReader peReader,
        Action<MethodDefinitionHandle>? getterClassified)
    {
        _reader = reader;
        _peReader = peReader;
        _getterClassified = getterClassified;
    }

    internal bool IsStableReceiverGetter(
        DecodedInstruction instruction)
    {
        try
        {
            EntityHandle methodHandle = MetadataTokens.EntityHandle(
                MethodInstructionFacts.OperandInt32(instruction));
            if (methodHandle.Kind != HandleKind.MethodDefinition)
                return false;

            var definitionHandle =
                (MethodDefinitionHandle)methodHandle;
            var method = _reader.GetMethodDefinition(definitionHandle);
            bool overridableVirtualCall = instruction.OpCode == ILOpCode.Callvirt
                && (method.Attributes & MethodAttributes.Virtual) != 0
                && (method.Attributes & MethodAttributes.Final) == 0
                && (_reader.GetTypeDefinition(method.GetDeclaringType()).Attributes
                    & TypeAttributes.Sealed) == 0;
            if (method.RelativeVirtualAddress == 0
                || overridableVirtualCall
                || !_reader.GetString(method.Name).StartsWith(
                    "get_",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return _stableReceiverGetters.GetOrAdd(
                definitionHandle,
                handle => new Lazy<bool>(
                    () => ClassifyStableReceiverGetter(handle),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or InvalidCastException)
        {
            return false;
        }
    }

    bool ClassifyStableReceiverGetter(
        MethodDefinitionHandle methodHandle)
    {
        _getterClassified?.Invoke(methodHandle);
        MethodDefinition method =
            _reader.GetMethodDefinition(methodHandle);
        var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
        if (body.ExceptionRegions.Length != 0)
            return false;
        DecodedInstruction? first = null;
        DecodedInstruction? fieldLoad = null;
        DecodedInstruction? third = null;
        int count = 0;
        foreach (DecodedInstruction instruction
            in InstructionDecoder.Decode(body.GetILBytes() ?? []))
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
        if (count != 3
            || first is not { OpCode: ILOpCode.Ldarg_0 }
            || fieldLoad is not { OpCode: ILOpCode.Ldfld }
            || third is not { OpCode: ILOpCode.Ret })
        {
            return false;
        }

        EntityHandle fieldHandle = MetadataTokens.EntityHandle(
            MethodInstructionFacts.OperandInt32(fieldLoad));
        return fieldHandle.Kind == HandleKind.FieldDefinition
            && (_reader.GetFieldDefinition(
                    (FieldDefinitionHandle)fieldHandle).Attributes
                & FieldAttributes.InitOnly) != 0;
    }
}
