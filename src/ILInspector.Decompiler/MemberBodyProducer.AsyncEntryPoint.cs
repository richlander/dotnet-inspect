using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler;

public static partial class MemberBodyProducer
{
    // RC3 emits precisely `call Main; call AsyncHelpers.HandleAsyncEntryPoint;
    // ret` for Task Main(). Decline other entrypoint shapes rather than hiding
    // an arbitrary method whose metadata name happens to be <Main>.
    static (int Wrapper, int Main)? TryGetAsyncEntryPoint(
        MetadataSource source, TypeDefinitionHandle typeHandle)
    {
        var reader = source.Reader;
        var header = source.Pe.PEHeaders.CorHeader;
        if (header is null || (header.Flags & CorFlags.NativeEntryPoint) != 0)
            return null;
        int token = header.EntryPointTokenOrRelativeVirtualAddress;
        if ((token >> 24) != 0x06)
            return null;
        var handle = MetadataTokens.MethodDefinitionHandle(token & 0xFFFFFF);
        var wrapper = reader.GetMethodDefinition(handle);
        const MethodAttributes flags = MethodAttributes.Private
            | MethodAttributes.Static | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName;
        if (wrapper.GetDeclaringType() != typeHandle
            || wrapper.Attributes != flags
            || wrapper.ImplAttributes != MethodImplAttributes.IL
            || !reader.StringComparer.Equals(wrapper.Name, "<Main>")
            || wrapper.RelativeVirtualAddress == 0)
            return null;
        var signature = GuardedDecode.MethodSignature(reader, wrapper, GenericScope.Empty);
        if (signature.Header.CallingConvention != SignatureCallingConvention.Default
            || signature.Header.IsInstance || signature.GenericParameterCount != 0
            || signature.ParameterTypes.Length != 0 || !signature.ReturnType.Equals(TypeRef.CoreLib("System", "Void")))
            return null;
        var body = source.Pe.GetMethodBody(wrapper.RelativeVirtualAddress);
        var ilReader = body.GetILReader();
        if (ilReader.Length != 11 || !body.LocalSignature.IsNil
            || body.ExceptionRegions.Length != 0)
            return null;
        byte[] il = ilReader.ReadBytes(11);
        if (il[0] != 0x28 || il[5] != 0x28 || il[10] != 0x2A)
            return null;
        int mainToken = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(1));
        int helperToken = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(6));
        if ((mainToken >> 24) != 0x06 || (helperToken >> 24) != 0x0A)
            return null;
        var main = reader.GetMethodDefinition(
            MetadataTokens.MethodDefinitionHandle(mainToken & 0xFFFFFF));
        if (main.GetDeclaringType() != typeHandle
            || !reader.StringComparer.Equals(main.Name, "Main")
            || (main.Attributes & MethodAttributes.Static) == 0
            || MethodClassificationScanner.ClassifyAsyncMethod(reader, main)
                is not (MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync))
            return null;
        var mainSignature = GuardedDecode.MethodSignature(reader, main, GenericScope.Empty);
        var task = TypeRef.CoreLib("System.Threading.Tasks", "Task");
        if (mainSignature.Header.CallingConvention != SignatureCallingConvention.Default
            || mainSignature.Header.IsInstance || mainSignature.GenericParameterCount != 0
            || mainSignature.ParameterTypes.Length != 0 || !mainSignature.ReturnType.Equals(task))
            return null;
        var helper = reader.GetMemberReference(
            MetadataTokens.MemberReferenceHandle(helperToken & 0xFFFFFF));
        if (!reader.StringComparer.Equals(helper.Name, "HandleAsyncEntryPoint")
            || helper.Parent.Kind != HandleKind.TypeReference)
            return null;
        var helperType = reader.GetTypeReference((TypeReferenceHandle)helper.Parent);
        if (!reader.StringComparer.Equals(helperType.Namespace, "System.Runtime.CompilerServices")
            || !reader.StringComparer.Equals(helperType.Name, "AsyncHelpers")
            || helperType.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return null;
        var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)helperType.ResolutionScope);
        if (!reader.StringComparer.Equals(assembly.Name, "System.Runtime"))
            return null;
        var key = reader.GetBlobReader(assembly.PublicKeyOrToken);
        if ((assembly.Flags & AssemblyFlags.PublicKey) != 0 || key.Length != 8
            || !PlatformKeys.IsPlatform(System.Convert.ToHexString(key.ReadBytes(8))))
            return null;
        var helperSignature = GuardedDecode.MethodSignature(reader, helper, GenericScope.Empty);
        if (helperSignature.Header.CallingConvention != SignatureCallingConvention.Default
            || helperSignature.Header.IsInstance || helperSignature.GenericParameterCount != 0
            || !helperSignature.ReturnType.Equals(TypeRef.CoreLib("System", "Void"))
            || helperSignature.ParameterTypes.Length != 1
            || !helperSignature.ParameterTypes[0].Equals(task))
            return null;
        return (token, mainToken);
    }
}
