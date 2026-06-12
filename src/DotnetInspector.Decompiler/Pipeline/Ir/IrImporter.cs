using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Builds the typed IR for one method while its <see cref="MetadataSource"/>
/// is alive; the resulting <see cref="IrFunction"/> is fully materialized.
/// Slice scope: straight-line bodies (no branches, no exception regions).
/// IL outside the slice becomes an explicit <see cref="UnsupportedNode"/>
/// with a <see cref="DiagnosticIds.UnsupportedConstruct"/> diagnostic and
/// import stops — fidelity degrades honestly, output never guesses.
/// </summary>
public static class IrImporter
{
    public static IrFunction? Import(MetadataSource source, string typeFullName, string methodName, int overloadIndex = 0)
    {
        var reader = source.Reader;
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            if ((ns.Length == 0 ? name : $"{ns}.{name}") != typeFullName)
                continue;

            int seen = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName || method.RelativeVirtualAddress == 0)
                    continue;
                if (seen++ != overloadIndex)
                    continue;

                var imported = MethodImporter.Import(source, typeDefHandle, methodHandle);
                return Build(source, imported);
            }
            return null;
        }
        return null;
    }

    static IrFunction Build(MetadataSource source, ImportedMethod method)
    {
        var body = new Block();
        var function = new IrFunction(method.Name, method.DeclaringType, method.Signature, method.Body.Locals, body);
        var stack = new Stack<IrExpression>();
        var reader = new ILReaderLite(method.Body.IL.AsSpan());

        if (!method.Body.Handlers.IsEmpty)
        {
            Stop(function, body, stack, 0, "(exception regions)", "exception regions are outside the straight-line slice");
            return function;
        }

        while (reader.HasNext)
        {
            int offset = reader.Offset;
            var opcode = reader.ReadILOpcode();
            switch (opcode)
            {
                case ILOpCode.Nop:
                    break;

                case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
                    stack.Push(MakeLoadArgument(method, opcode - ILOpCode.Ldarg_0));
                    break;
                case ILOpCode.Ldarg_s:
                    stack.Push(MakeLoadArgument(method, reader.ReadILByte()));
                    break;

                case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3:
                    stack.Push(MakeLoadLocal(method, opcode - ILOpCode.Ldloc_0));
                    break;
                case ILOpCode.Ldloc_s:
                    stack.Push(MakeLoadLocal(method, reader.ReadILByte()));
                    break;

                case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3:
                    body.Add(MakeStoreLocal(method, opcode - ILOpCode.Stloc_0, stack.Pop()));
                    break;
                case ILOpCode.Stloc_s:
                    body.Add(MakeStoreLocal(method, reader.ReadILByte(), stack.Pop()));
                    break;

                case >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4_8:
                    stack.Push(new Constant(opcode - ILOpCode.Ldc_i4_0, TypeRef.CoreLib("System", "Int32")));
                    break;
                case ILOpCode.Ldc_i4_s:
                    stack.Push(new Constant((int)(sbyte)reader.ReadILByte(), TypeRef.CoreLib("System", "Int32")));
                    break;
                case ILOpCode.Ldc_i4:
                    stack.Push(new Constant((int)reader.ReadILUInt32(), TypeRef.CoreLib("System", "Int32")));
                    break;
                case ILOpCode.Ldc_i8:
                    stack.Push(new Constant((long)reader.ReadILUInt64(), TypeRef.CoreLib("System", "Int64")));
                    break;
                case ILOpCode.Ldnull:
                    stack.Push(new Constant(null, TypeRef.CoreLib("System", "Object")));
                    break;
                case ILOpCode.Ldstr:
                    stack.Push(new Constant(
                        source.Reader.GetUserString(MetadataTokens.UserStringHandle(reader.ReadILToken())),
                        TypeRef.CoreLib("System", "String")));
                    break;

                case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Rem
                    or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Shl or ILOpCode.Shr
                    or ILOpCode.Add_ovf or ILOpCode.Sub_ovf or ILOpCode.Mul_ovf
                    or ILOpCode.Div_un or ILOpCode.Rem_un or ILOpCode.Shr_un
                    or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf_un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(new Binary(BinaryKindOf(opcode), IsChecked(opcode), IsUnsigned(opcode), left, right));
                    break;
                }

                case ILOpCode.Call or ILOpCode.Callvirt:
                {
                    var callee = ResolveMethod(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()));
                    int argumentCount = callee.ParameterTypes.Length + (callee.HasThis ? 1 : 0);
                    var arguments = new IrExpression[argumentCount];
                    for (int i = argumentCount - 1; i >= 0; i--)
                        arguments[i] = stack.Pop();
                    var call = new Call(callee, opcode == ILOpCode.Callvirt, arguments);
                    if (callee.ReturnType is { Name: "Void", Namespace: "System" })
                        body.Add(new ExpressionStatement(call));
                    else
                        stack.Push(call);
                    break;
                }

                case ILOpCode.Ldfld or ILOpCode.Ldsfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()));
                    stack.Push(new LoadField(field, opcode == ILOpCode.Ldfld ? stack.Pop() : null));
                    break;
                }
                case ILOpCode.Stfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()));
                    var value = stack.Pop();
                    body.Add(new StoreField(field, stack.Pop(), value));
                    break;
                }
                case ILOpCode.Stsfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()));
                    body.Add(new StoreField(field, null, stack.Pop()));
                    break;
                }

                case ILOpCode.Pop:
                    body.Add(new ExpressionStatement(stack.Pop()));
                    break;

                case ILOpCode.Ret:
                    body.Add(new Return(stack.Count > 0 ? stack.Pop() : null));
                    break;

                default:
                    Stop(function, body, stack, offset, opcode.ToString().ToLowerInvariant(),
                        "opcode is outside the straight-line slice");
                    return function;
            }
        }

        function.CheckInvariant();
        return function;
    }

    /// <summary>Records the honest stopping point: spill the pending stack as statements, append the unsupported marker, attach the diagnostic.</summary>
    static void Stop(IrFunction function, Block body, Stack<IrExpression> stack, int offset, string opcode, string reason)
    {
        foreach (var pending in stack.Reverse())
            body.Add(new ExpressionStatement(pending));
        stack.Clear();
        body.Add(new ExpressionStatement(new UnsupportedNode(offset, opcode, reason)));
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct, $"IL_{offset:X4} {opcode}: {reason}"));
        function.CheckInvariant();
    }

    static LoadArgument MakeLoadArgument(ImportedMethod method, int index)
    {
        if (method.Signature.HasThis)
        {
            if (index == 0)
                return new LoadArgument(0, "this", method.DeclaringType);
            var p = method.Signature.Parameters[index - 1];
            return new LoadArgument(index, p.Name, p.Type);
        }
        var parameter = method.Signature.Parameters[index];
        return new LoadArgument(index, parameter.Name, parameter.Type);
    }

    static LoadLocal MakeLoadLocal(ImportedMethod method, int index)
        => new(index, method.Body.Locals[index]);

    static StoreLocal MakeStoreLocal(ImportedMethod method, int index, IrExpression value)
        => new(index, method.Body.Locals[index], value);

    internal static MethodRef ResolveMethod(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var declaring = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, method.GetDeclaringType(), 0);
                var typeScope = new GenericScope(GenericParameterNames(reader, reader.GetTypeDefinition(method.GetDeclaringType()).GetGenericParameters()), []);
                var signature = method.DecodeSignature(TypeRefDecoder.Instance, typeScope);
                return new MethodRef(declaring, reader.GetString(method.Name), signature.ReturnType, signature.ParameterTypes, signature.Header.IsInstance);
            }
            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                var declaring = ResolveParentType(reader, member.Parent);
                var signature = member.DecodeMethodSignature(TypeRefDecoder.Instance, GenericScope.Empty);
                return new MethodRef(declaring, reader.GetString(member.Name), signature.ReturnType, signature.ParameterTypes, signature.Header.IsInstance);
            }
            default:
                return new MethodRef(TypeRef.Unsupported($"callee handle kind {handle.Kind}"), "?", TypeRef.Unsupported("unknown return"), [], false);
        }
    }

    internal static FieldRef ResolveField(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
            {
                var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                var declaring = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, field.GetDeclaringType(), 0);
                var typeScope = new GenericScope(GenericParameterNames(reader, reader.GetTypeDefinition(field.GetDeclaringType()).GetGenericParameters()), []);
                return new FieldRef(declaring, reader.GetString(field.Name), field.DecodeSignature(TypeRefDecoder.Instance, typeScope));
            }
            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                var declaring = ResolveParentType(reader, member.Parent);
                return new FieldRef(declaring, reader.GetString(member.Name), member.DecodeFieldSignature(TypeRefDecoder.Instance, GenericScope.Empty));
            }
            default:
                return new FieldRef(TypeRef.Unsupported($"field handle kind {handle.Kind}"), "?", TypeRef.Unsupported("unknown field type"));
        }
    }

    static TypeRef ResolveParentType(MetadataReader reader, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)parent, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)parent, 0),
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, GenericScope.Empty, (TypeSpecificationHandle)parent, 0),
        _ => TypeRef.Unsupported($"member parent kind {parent.Kind}"),
    };

    static ImmutableArray<string> GenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    static BinaryKind BinaryKindOf(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un => BinaryKind.Add,
        ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un => BinaryKind.Subtract,
        ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un => BinaryKind.Multiply,
        ILOpCode.Div or ILOpCode.Div_un => BinaryKind.Divide,
        ILOpCode.Rem or ILOpCode.Rem_un => BinaryKind.Remainder,
        ILOpCode.And => BinaryKind.And,
        ILOpCode.Or => BinaryKind.Or,
        ILOpCode.Xor => BinaryKind.Xor,
        ILOpCode.Shl => BinaryKind.ShiftLeft,
        _ => BinaryKind.ShiftRight,
    };

    static bool IsChecked(ILOpCode opcode) => opcode is ILOpCode.Add_ovf or ILOpCode.Add_ovf_un
        or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un;

    static bool IsUnsigned(ILOpCode opcode) => opcode is ILOpCode.Div_un or ILOpCode.Rem_un
        or ILOpCode.Shr_un or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf_un;
}

/// <summary>Indented tree dump of the IR — the stage projection for this layer (consumed by the harness's --dump as the pipeline grows).</summary>
public static class IrPrinter
{
    public static string Dump(IrFunction function)
    {
        var sb = new System.Text.StringBuilder();
        Append(sb, function, 0);
        foreach (var diagnostic in function.Diagnostics)
            sb.AppendLine($"// {diagnostic}");
        sb.AppendLine($"// fidelity: {function.Fidelity}");
        return sb.ToString();
    }

    static void Append(System.Text.StringBuilder sb, IrNode node, int indent)
    {
        sb.Append(' ', indent * 2).AppendLine(node.Describe());
        foreach (var child in node.Children)
            Append(sb, child, indent + 1);
    }
}
