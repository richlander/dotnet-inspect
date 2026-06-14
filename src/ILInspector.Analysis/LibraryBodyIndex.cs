using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis;

/// <summary>Materialized IL body evidence for one assembly.</summary>
public sealed class LibraryBodyIndex
{
    LibraryBodyIndex(
        string path,
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        Path = path;
        Methods = methods;
        DirectCalls = directCalls;
        UnsafeEvidence = unsafeEvidence;
        Diagnostics = diagnostics;
    }

    public string Path { get; }
    public ImmutableArray<MethodIdentity> Methods { get; }
    public ImmutableArray<DirectCall> DirectCalls { get; }
    public ImmutableArray<UnsafeEvidence> UnsafeEvidence { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    public static LibraryBodyIndex Open(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            throw new BadImageFormatException($"No managed metadata: {path}");
        var reader = peReader.GetMetadataReader();
        var builder = new IndexBuilder(path, reader, peReader);
        var index = builder.Build();
        return new LibraryBodyIndex(path, index.Methods, index.DirectCalls, index.UnsafeEvidence, index.Diagnostics);
    }

    public ImmutableArray<DirectCall> FindCalls(MemberPattern pattern)
        => [.. DirectCalls.Where(call => pattern.Matches(call.Callee))];

    sealed class IndexBuilder
    {
        const ILOpCode NoPrefix = (ILOpCode)0xFE19;

        readonly string _path;
        readonly MetadataReader _reader;
        readonly PEReader _peReader;
        readonly string _assemblyName;
        readonly Guid _mvid;

        public IndexBuilder(string path, MetadataReader reader, PEReader peReader)
        {
            _path = path;
            _reader = reader;
            _peReader = peReader;
            _assemblyName = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : System.IO.Path.GetFileNameWithoutExtension(path);
            _mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        }

        public (ImmutableArray<MethodIdentity> Methods, ImmutableArray<DirectCall> DirectCalls, ImmutableArray<UnsafeEvidence> UnsafeEvidence, ImmutableArray<AnalysisDiagnostic> Diagnostics) Build()
        {
            var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
            var calls = ImmutableArray.CreateBuilder<DirectCall>();
            var unsafeEvidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
            var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();

            foreach (var typeHandle in _reader.TypeDefinitions)
            {
                var typeDef = _reader.GetTypeDefinition(typeHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    try
                    {
                        var methodDef = _reader.GetMethodDefinition(methodHandle);
                        var scope = CreateScope(typeDef, methodDef);
                        var caller = CreateMethodIdentity(typeHandle, methodHandle, methodDef, scope);
                        bool hasUnsafeSignature = AddUnsafeSignatureEvidence(caller, unsafeEvidence);
                        if (methodDef.RelativeVirtualAddress == 0)
                            continue;

                        methods.Add(caller);
                        var body = _peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                        var il = body.GetILBytes() ?? [];
                        bool hasUnsafeLocals = ScanLocals(body, caller, scope, unsafeEvidence);
                        ScanBody(il, caller, scope, calls, unsafeEvidence, includeIndirectOpcodes: hasUnsafeSignature || hasUnsafeLocals);
                    }
                    catch (Exception ex) when (IsRecoverableMethodFailure(ex))
                    {
                        diagnostics.Add(new AnalysisDiagnostic(
                            MetadataTokens.GetToken(methodHandle),
                            MethodLabel(typeHandle, methodHandle),
                            $"{ex.GetType().Name}: {ex.Message}"));
                    }
                }
            }

            return (methods.ToImmutable(), calls.ToImmutable(), unsafeEvidence.ToImmutable(), diagnostics.ToImmutable());
        }

        MethodIdentity CreateMethodIdentity(TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle, MethodDefinition methodDef, GenericScope scope)
        {
            var declaringType = TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0);
            var signature = methodDef.DecodeSignature(TypeRefDecoder.Instance, scope);
            return new MethodIdentity(
                _assemblyName,
                _mvid,
                declaringType,
                _reader.GetString(methodDef.Name),
                signature.ParameterTypes,
                signature.ReturnType,
                MetadataTokens.GetToken(methodHandle),
                (methodDef.Attributes & MethodAttributes.Static) != 0);
        }

        bool AddUnsafeSignatureEvidence(MethodIdentity method, ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence)
        {
            var unsafeTypes = method.ParameterTypes
                .Append(method.ReturnType)
                .Where(ContainsUnsafeType)
                .Select(t => t.ToQualifiedDisplayString())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unsafeTypes.Count == 0)
                return false;

            unsafeEvidence.Add(new UnsafeEvidence(
                method,
                "Unsafe signature",
                string.Join(", ", unsafeTypes),
                "signature",
                ILOffset: null,
                OperandToken: null));
            return true;
        }

        bool ScanLocals(MethodBodyBlock body, MethodIdentity member, GenericScope scope, ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence)
        {
            if (body.LocalSignature.IsNil)
                return false;

            bool found = false;
            var signature = _reader.GetStandaloneSignature(body.LocalSignature);
            var locals = signature.DecodeLocalSignature(TypeRefDecoder.Instance, scope);
            for (int i = 0; i < locals.Length; i++)
            {
                var local = locals[i];
                if (local.Kind == TypeRefKind.Pinned)
                {
                    unsafeEvidence.Add(new UnsafeEvidence(member, "Pinned local", $"V_{i}: {local.ToQualifiedDisplayString()}", "local", null, null));
                    found = true;
                    continue;
                }
                if (ContainsUnsafeType(local))
                {
                    unsafeEvidence.Add(new UnsafeEvidence(member, "Pointer local", $"V_{i}: {local.ToQualifiedDisplayString()}", "local", null, null));
                    found = true;
                }
            }
            return found;
        }

        void ScanBody(byte[] il, MethodIdentity caller, GenericScope callerScope,
            ImmutableArray<DirectCall>.Builder calls,
            ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence,
            bool includeIndirectOpcodes)
        {
            int position = 0;
            while (position < il.Length)
            {
                int offset = position;
                var opcode = ReadOpcode(il, ref position);
                switch (opcode)
                {
                    case ILOpCode.Call:
                    case ILOpCode.Callvirt:
                    case ILOpCode.Newobj:
                    case ILOpCode.Ldftn:
                    case ILOpCode.Ldvirtftn:
                    {
                        int token = ReadInt32(il, ref position, offset);
                        var callee = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                        calls.Add(new DirectCall(caller, callee, offset, token, ToCallKind(opcode)));
                        if (IsUnsafeCall(callee))
                        {
                            unsafeEvidence.Add(new UnsafeEvidence(
                                caller,
                                "Unsafe call",
                                FormatMember(callee),
                                FormatCallKind(ToCallKind(opcode)),
                                offset,
                                token));
                        }
                        break;
                    }
                    case ILOpCode.Calli:
                    {
                        int token = ReadInt32(il, ref position, offset);
                        calls.Add(new DirectCall(caller, MemberRef.Unsupported($"calli signature token 0x{token:X8}"), offset, token, CallKind.CallIndirect));
                        unsafeEvidence.Add(new UnsafeEvidence(caller, "Unsafe operation", "calli", "calli", offset, token));
                        break;
                    }
                    default:
                        if (UnsafeOpcodeName(opcode, includeIndirectOpcodes) is { } unsafeOpcode)
                            unsafeEvidence.Add(new UnsafeEvidence(caller, "Unsafe operation", unsafeOpcode, "opcode", offset, null));
                        SkipOperand(il, opcode, ref position, offset);
                        break;
                }
            }
        }

        static bool IsUnsafeCall(MemberRef member)
            => IsUnsafeApi(member) || member.ParameterTypes.Append(member.ReturnType).Any(ContainsUnsafeType);

        static bool IsUnsafeApi(MemberRef member)
            => member.DeclaringType is { Namespace: "System.Runtime.CompilerServices", Name: "Unsafe" };

        static bool ContainsUnsafeType(TypeRef type)
        {
            if (type.Kind is TypeRefKind.Pointer or TypeRefKind.Pinned)
                return true;
            if (type.Kind == TypeRefKind.Unsupported
                && type.UnsupportedReason.Contains("function pointer", StringComparison.OrdinalIgnoreCase))
                return true;
            if (type.ElementType is not null && ContainsUnsafeType(type.ElementType))
                return true;
            return type.TypeArguments.Any(ContainsUnsafeType);
        }

        static string FormatMember(MemberRef member)
        {
            if (member.Kind == MemberKind.Unsupported)
                return member.DeclaringType.ToDisplayString();

            string name = member.Name;
            if (member.TypeArguments.Length > 0)
                name += $"<{string.Join(", ", member.TypeArguments.Select(t => t.ToQualifiedDisplayString()))}>";
            return $"{member.DeclaringType.ToQualifiedDisplayString()}.{name}({string.Join(", ", member.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";
        }

        static string FormatCallKind(CallKind kind) => kind switch
        {
            CallKind.Call => "call",
            CallKind.CallVirtual => "callvirt",
            CallKind.NewObject => "newobj",
            CallKind.LoadFunction => "ldftn",
            CallKind.LoadVirtualFunction => "ldvirtftn",
            _ => "calli",
        };

        static string? UnsafeOpcodeName(ILOpCode opcode, bool includeIndirectOpcodes) => opcode switch
        {
            ILOpCode.Localloc => "localloc",
            ILOpCode.Cpblk => "cpblk",
            ILOpCode.Initblk => "initblk",
            ILOpCode.Ldind_i1 when includeIndirectOpcodes => "ldind.i1",
            ILOpCode.Ldind_u1 when includeIndirectOpcodes => "ldind.u1",
            ILOpCode.Ldind_i2 when includeIndirectOpcodes => "ldind.i2",
            ILOpCode.Ldind_u2 when includeIndirectOpcodes => "ldind.u2",
            ILOpCode.Ldind_i4 when includeIndirectOpcodes => "ldind.i4",
            ILOpCode.Ldind_u4 when includeIndirectOpcodes => "ldind.u4",
            ILOpCode.Ldind_i8 when includeIndirectOpcodes => "ldind.i8",
            ILOpCode.Ldind_i when includeIndirectOpcodes => "ldind.i",
            ILOpCode.Ldind_r4 when includeIndirectOpcodes => "ldind.r4",
            ILOpCode.Ldind_r8 when includeIndirectOpcodes => "ldind.r8",
            ILOpCode.Ldind_ref when includeIndirectOpcodes => "ldind.ref",
            ILOpCode.Stind_ref when includeIndirectOpcodes => "stind.ref",
            ILOpCode.Stind_i1 when includeIndirectOpcodes => "stind.i1",
            ILOpCode.Stind_i2 when includeIndirectOpcodes => "stind.i2",
            ILOpCode.Stind_i4 when includeIndirectOpcodes => "stind.i4",
            ILOpCode.Stind_i8 when includeIndirectOpcodes => "stind.i8",
            ILOpCode.Stind_i when includeIndirectOpcodes => "stind.i",
            ILOpCode.Stind_r4 when includeIndirectOpcodes => "stind.r4",
            ILOpCode.Stind_r8 when includeIndirectOpcodes => "stind.r8",
            _ => null,
        };

        static CallKind ToCallKind(ILOpCode opcode) => opcode switch
        {
            ILOpCode.Call => CallKind.Call,
            ILOpCode.Callvirt => CallKind.CallVirtual,
            ILOpCode.Newobj => CallKind.NewObject,
            ILOpCode.Ldftn => CallKind.LoadFunction,
            _ => CallKind.LoadVirtualFunction,
        };

        GenericScope CreateScope(TypeDefinition typeDef, MethodDefinition methodDef)
            => new(GenericParameterNames(typeDef.GetGenericParameters()), GenericParameterNames(methodDef.GetGenericParameters()));

        ImmutableArray<string> GenericParameterNames(GenericParameterHandleCollection handles)
        {
            if (handles.Count == 0)
                return [];
            var names = ImmutableArray.CreateBuilder<string>(handles.Count);
            foreach (var handle in handles)
                names.Add(_reader.GetString(_reader.GetGenericParameter(handle).Name));
            return names.MoveToImmutable();
        }

        static ILOpCode ReadOpcode(byte[] il, ref int position)
        {
            byte first = ReadByte(il, ref position, position);
            if (first != 0xFE)
                return (ILOpCode)first;
            byte second = ReadByte(il, ref position, position - 1);
            return (ILOpCode)(0xFE00 | second);
        }

        void SkipOperand(byte[] il, ILOpCode opcode, ref int position, int offset)
        {
            switch (opcode)
            {
                case ILOpCode.Switch:
                {
                    int count = ReadInt32(il, ref position, offset);
                    if (count < 0 || position + checked(count * 4) > il.Length)
                        throw new BadImageFormatException($"Malformed switch at IL_{offset:X4} in method body from {_path}");
                    position += count * 4;
                    break;
                }
                case ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s
                    or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
                    or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
                    or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s
                    or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
                    or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
                    or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned:
                    Advance(il, ref position, 1, offset);
                    break;
                case ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
                    or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc:
                    Advance(il, ref position, 2, offset);
                    break;
                case ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq
                    or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                    or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
                    or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave
                    or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
                    or ILOpCode.Jmp or ILOpCode.Ldstr
                    or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
                    or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
                    or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Castclass
                    or ILOpCode.Isinst or ILOpCode.Unbox or ILOpCode.Stobj
                    or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Ldelema
                    or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Unbox_any
                    or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Initobj
                    or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Ldtoken:
                    Advance(il, ref position, 4, offset);
                    break;
                case ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8:
                    Advance(il, ref position, 8, offset);
                    break;
                case NoPrefix:
                    Advance(il, ref position, 1, offset);
                    break;
            }
        }

        static void Advance(byte[] il, ref int position, int bytes, int offset)
        {
            if (position + bytes > il.Length)
                throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
            position += bytes;
        }

        static byte ReadByte(byte[] il, ref int position, int offset)
        {
            if (position >= il.Length)
                throw new BadImageFormatException($"Malformed IL at IL_{offset:X4}");
            return il[position++];
        }

        static int ReadInt32(byte[] il, ref int position, int offset)
        {
            if (position + 4 > il.Length)
                throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
            int value = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position));
            position += 4;
            return value;
        }

        string MethodLabel(TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle)
        {
            try
            {
                var typeDef = _reader.GetTypeDefinition(typeHandle);
                string ns = _reader.GetString(typeDef.Namespace);
                string typeName = _reader.GetString(typeDef.Name);
                string methodName = _reader.GetString(_reader.GetMethodDefinition(methodHandle).Name);
                string fullTypeName = ns.Length == 0 ? typeName : $"{ns}.{typeName}";
                return $"{fullTypeName}::{methodName}";
            }
            catch (Exception ex) when (IsRecoverableMethodFailure(ex))
            {
                return $"0x{MetadataTokens.GetToken(methodHandle):X8}";
            }
        }

        static bool IsRecoverableMethodFailure(Exception ex)
            => ex is BadImageFormatException or InvalidOperationException or ArgumentException
                or ArgumentOutOfRangeException or IndexOutOfRangeException;
    }
}
