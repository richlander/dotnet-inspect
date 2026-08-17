using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace ILInspector.Instructions;

public static partial class IlBodyDiff
{
    sealed class MetadataOperandResolver(
        MetadataReader reader,
        IlBodyDiffNormalization normalization,
        CompilerGeneratedOrdinalCorrespondence correspondence)
    {
        // Malformed metadata can make a declaring type or resolution-scope chain cyclic,
        // so the type-name climbs below would recurse until an uncatchable
        // StackOverflowException (which TryResolve's catch cannot intercept). Cap the climb
        // ([ThreadStatic] for thread safety) and degrade to the leaf name past the cap.
        [ThreadStatic]
        static int s_climbDepth;
        const int MaxClimbDepth = 256;

        public bool TryResolve(DecodedInstruction instruction, out IlOperandIdentity? operand, out string? failure)
        {
            try
            {
                string value = instruction.Operand switch
                {
                    OperandKind.InlineString => ResolveString((int)instruction.OperandValue),
                    OperandKind.InlineMethod => ResolveMethod((int)instruction.OperandValue),
                    OperandKind.InlineField => ResolveField((int)instruction.OperandValue),
                    OperandKind.InlineType => ResolveType((int)instruction.OperandValue),
                    OperandKind.InlineTok => ResolveToken((int)instruction.OperandValue),
                    OperandKind.InlineSig => ResolveSignature((int)instruction.OperandValue),
                    _ => throw new InvalidOperationException($"Operand kind {instruction.Operand} is not a metadata token."),
                };
                if (reader.IsAssembly && instruction.Operand != OperandKind.InlineString)
                {
                    string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);
                    value = NormalizeAssemblyScopes(value, assembly);
                }
                operand = new IlOperandIdentity(IlOperandIdentityKind.Token, value);
                failure = null;
                return true;
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                operand = null;
                failure = $"metadata token operand at IL_{instruction.Offset:X4} could not be resolved: {ex.Message}";
                return false;
            }
        }

        string ResolveString(int token)
        {
            var handle = MetadataTokens.UserStringHandle(token);
            return $"string \"{Escape(reader.GetUserString(handle))}\"";
        }

        string ResolveMethod(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodDefinition((MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberReference((MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => FormatMethodSpecification((MethodSpecificationHandle)handle),
                _ => throw new BadImageFormatException($"Expected method token, got {handle.Kind}."),
            };
        }

        string ResolveField(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.FieldDefinition => FormatFieldDefinition((FieldDefinitionHandle)handle),
                HandleKind.MemberReference => FormatFieldMemberReference((MemberReferenceHandle)handle),
                _ => throw new BadImageFormatException($"Expected field token, got {handle.Kind}."),
            };
        }

        string ResolveType(int token)
            => FormatType(MetadataTokens.EntityHandle(token));

        string ResolveToken(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => $"type {FormatType(handle)}",
                HandleKind.MethodDefinition => $"method {FormatMethodDefinition((MethodDefinitionHandle)handle)}",
                HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)handle).GetKind() == MemberReferenceKind.Method
                    => $"method {FormatMemberReference((MemberReferenceHandle)handle)}",
                HandleKind.MemberReference => $"field {FormatFieldMemberReference((MemberReferenceHandle)handle)}",
                HandleKind.MethodSpecification => $"method {FormatMethodSpecification((MethodSpecificationHandle)handle)}",
                HandleKind.FieldDefinition => $"field {FormatFieldDefinition((FieldDefinitionHandle)handle)}",
                _ => throw new BadImageFormatException($"Unsupported ldtoken handle kind {handle.Kind}."),
            };
        }

        string ResolveSignature(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                throw new BadImageFormatException($"Expected standalone signature token, got {handle.Kind}.");

            var signature = reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            return $"signature {Convert.ToHexString(reader.GetBlobBytes(signature.Signature))}";
        }

        string FormatMethodDefinition(MethodDefinitionHandle handle)
        {
            var method = reader.GetMethodDefinition(handle);
            var signature = GuardedProviderDecode.TryMethod(
                reader,
                method,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, method.Signature));
            return FormatCall(signature, FormatType(method.GetDeclaringType()), MethodName(handle, method), genericArgs: null);
        }

        string FormatMemberReference(MemberReferenceHandle handle)
        {
            var member = reader.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Method)
                throw new BadImageFormatException("Expected method member reference.");

            var signature = GuardedProviderDecode.TryMemberRefMethod(
                reader,
                member,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, member.Signature));
            return FormatCall(signature, FormatMemberParent(member.Parent), reader.GetString(member.Name), genericArgs: null);
        }

        string FormatMethodSpecification(MethodSpecificationHandle handle)
        {
            var spec = reader.GetMethodSpecification(handle);
            var typeArguments = GuardedProviderDecode.TryMethodSpec(
                reader,
                spec,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : [GuardedProviderDecode.RejectedIdentity(reader, spec.Signature)];
            string genericArgs = $"<{string.Join(", ", typeArguments)}>";

            return spec.Method.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodSpecificationDefinition((MethodDefinitionHandle)spec.Method, genericArgs),
                HandleKind.MemberReference => FormatMethodSpecificationReference((MemberReferenceHandle)spec.Method, genericArgs),
                _ => throw new BadImageFormatException($"Unsupported method specification target {spec.Method.Kind}."),
            };
        }

        string FormatMethodSpecificationDefinition(MethodDefinitionHandle handle, string genericArgs)
        {
            var method = reader.GetMethodDefinition(handle);
            var signature = GuardedProviderDecode.TryMethod(
                reader,
                method,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, method.Signature));
            return FormatCall(signature, FormatType(method.GetDeclaringType()), MethodName(handle, method), genericArgs);
        }

        string FormatMethodSpecificationReference(MemberReferenceHandle handle, string genericArgs)
        {
            var member = reader.GetMemberReference(handle);

            // A method specification can name a member reference that is actually a
            // field. Reject that malformed shape before decoding it as a method.
            if (member.GetKind() != MemberReferenceKind.Method)
                throw new BadImageFormatException("Expected method member reference.");

            var signature = GuardedProviderDecode.TryMemberRefMethod(
                reader,
                member,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, member.Signature));
            return FormatCall(signature, FormatMemberParent(member.Parent), reader.GetString(member.Name), genericArgs);
        }

        string FormatFieldDefinition(FieldDefinitionHandle handle)
        {
            var field = reader.GetFieldDefinition(handle);
            string fieldType = GuardedProviderDecode.TryField(
                reader,
                field,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, field.Signature);
            return $"{fieldType} {FormatType(field.GetDeclaringType())}::{FieldName(handle, field)}";
        }

        string FormatFieldMemberReference(MemberReferenceHandle handle)
        {
            var member = reader.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Field)
                throw new BadImageFormatException("Expected field member reference.");

            string fieldType = GuardedProviderDecode.TryMemberRefField(
                reader,
                member,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, member.Signature);
            // A MemberReference names a field the definition-handle correspondence never
            // indexed, so it keeps its raw name.
            return $"{fieldType} {FormatMemberParent(member.Parent)}::{reader.GetString(member.Name)}";
        }

        string FormatCall(MethodSignature<string> signature, string parent, string name, string? genericArgs)
        {
            string instance = SignatureThisPrefix(signature.Header);
            string convention = CallingConventionPrefix(signature.Header.CallingConvention);
            string arity = signature.GenericParameterCount > 0
                ? $"`{signature.GenericParameterCount}"
                : "";
            return $"{instance}{convention}{signature.ReturnType} {parent}::{name}{arity}{genericArgs}({FormatParameterList(signature)})";
        }

        string FormatMemberParent(EntityHandle parent)
            => parent.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => FormatType(parent),
                _ => throw new BadImageFormatException($"Unsupported member parent {parent.Kind}."),
            };

        string FormatType(EntityHandle handle)
            => handle.Kind switch
            {
                HandleKind.TypeDefinition => FormatTypeDefinition((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => FormatTypeReference((TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => FormatTypeSpecification((TypeSpecificationHandle)handle),
                _ => throw new BadImageFormatException($"Unsupported type handle {handle.Kind}."),
            };

        string FormatTypeSpecification(TypeSpecificationHandle handle)
        {
            var specification = reader.GetTypeSpecification(handle);
            return GuardedProviderDecode.TryTypeSpec(
                reader,
                handle,
                SignatureIdentityProvider.Instance,
                null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, specification.Signature);
        }

        string FormatTypeDefinition(TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = correspondence.TryGetTypeName(handle, out var elided)
                ? elided
                : reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            string fullName;
            if (!declaring.IsNil && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { fullName = $"{FormatTypeDefinition(declaring)}+{name}"; }
                finally { s_climbDepth--; }
            }
            else
            {
                fullName = Dotted(reader.GetString(type.Namespace), name);
            }
            return $"[{CurrentAssemblyName()}]{fullName}";
        }

        string FormatTypeReference(TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            string fullName = Dotted(reader.GetString(type.Namespace), name);
            if (type.ResolutionScope.Kind == HandleKind.AssemblyReference)
                return $"[{AssemblyReferenceIdentity(reader, (AssemblyReferenceHandle)type.ResolutionScope)}]{fullName}";
            if (type.ResolutionScope.Kind == HandleKind.TypeReference && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { return $"{FormatTypeReference((TypeReferenceHandle)type.ResolutionScope)}+{fullName}"; }
                finally { s_climbDepth--; }
            }
            return $"[{CurrentAssemblyName()}]{fullName}";
        }

        string MethodName(MethodDefinitionHandle handle, MethodDefinition method)
        {
            if (correspondence.TryGetMethodName(handle, out var elided))
                return elided;

            // A recognized name that the correspondence declines keeps its raw spelling.
            // Declining means the ordinal-free key was ambiguous on a side, so the two
            // members are not known to correspond (#3645).
            return reader.GetString(method.Name);
        }

        string FieldName(FieldDefinitionHandle handle, FieldDefinition field)
        {
            if (correspondence.TryGetFieldName(handle, out var elided))
                return elided;

            return reader.GetString(field.Name);
        }

        string CurrentAssemblyName()
            => (normalization & IlBodyDiffNormalization.NormalizeCurrentAssemblyScope) != 0
                ? "<current>"
                : reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";

        static string Dotted(string ns, string name)
            => ns.Length == 0 ? name : $"{ns}.{name}";

        static string Escape(string value)
            => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        string NormalizeAssemblyScopes(string value, string currentAssembly)
        {
            bool normalizeCurrent =
                (normalization & IlBodyDiffNormalization.NormalizeCurrentAssemblyScope) != 0;
            bool normalizePlatform =
                (normalization & IlBodyDiffNormalization.NormalizePlatformAssemblyScope) != 0;
            if (!normalizeCurrent && !normalizePlatform)
                return value;

            StringBuilder? normalized = null;
            int copied = 0;
            int open = value.IndexOf('[', StringComparison.Ordinal);
            while (open >= 0)
            {
                int close = value.IndexOf(']', open + 1);
                if (close < 0)
                    break;

                ReadOnlySpan<char> identity = value.AsSpan(open + 1, close - open - 1);
                int comma = identity.IndexOf(',');
                ReadOnlySpan<char> name = comma >= 0 ? identity[..comma] : identity;
                string? normalizedScope = normalizeCurrent && name.Equals(currentAssembly, StringComparison.Ordinal)
                    ? "<current>"
                    : normalizePlatform && !name.Equals(currentAssembly, StringComparison.Ordinal)
                        && IsPlatformAssembly(name)
                            ? "<platform>"
                            : null;
                if (normalizedScope is not null)
                {
                    normalized ??= new StringBuilder(value.Length);
                    normalized.Append(value, copied, open - copied);
                    normalized.Append('[').Append(normalizedScope).Append(']');
                    copied = close + 1;
                }

                open = value.IndexOf('[', close + 1);
            }

            if (normalized is null)
                return value;

            normalized.Append(value, copied, value.Length - copied);
            return normalized.ToString();
        }

        static bool IsPlatformAssembly(ReadOnlySpan<char> name)
            => name.Equals("mscorlib", StringComparison.Ordinal)
                || name.Equals("netstandard", StringComparison.Ordinal)
                || name.Equals("System", StringComparison.Ordinal)
                || name.StartsWith("System.", StringComparison.Ordinal)
                || name.Equals("Microsoft.CSharp", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.VisualBasic", StringComparison.Ordinal);

    }

    sealed class SignatureIdentityProvider : ISignatureTypeProvider<string, object?>
    {
        public static SignatureIdentityProvider Instance { get; } = new();

        // Malformed metadata can make a declaring type or resolution-scope chain cyclic, so
        // the TypeName climbs below would recurse until an uncatchable StackOverflowException.
        // Cap the climb ([ThreadStatic] so the shared Instance stays thread-safe) and degrade
        // to the leaf name past the cap.
        [ThreadStatic]
        static int s_climbDepth;
        const int MaxClimbDepth = 256;

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode switch
            {
                PrimitiveTypeCode.Void => "void",
                PrimitiveTypeCode.Boolean => "bool",
                PrimitiveTypeCode.Char => "char",
                PrimitiveTypeCode.SByte => "int8",
                PrimitiveTypeCode.Byte => "uint8",
                PrimitiveTypeCode.Int16 => "int16",
                PrimitiveTypeCode.UInt16 => "uint16",
                PrimitiveTypeCode.Int32 => "int32",
                PrimitiveTypeCode.UInt32 => "uint32",
                PrimitiveTypeCode.Int64 => "int64",
                PrimitiveTypeCode.UInt64 => "uint64",
                PrimitiveTypeCode.Single => "float32",
                PrimitiveTypeCode.Double => "float64",
                PrimitiveTypeCode.String => "string",
                PrimitiveTypeCode.Object => "object",
                PrimitiveTypeCode.IntPtr => "native int",
                PrimitiveTypeCode.UIntPtr => "native uint",
                PrimitiveTypeCode.TypedReference => "typedref",
                _ => typeCode.ToString(),
            };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeName(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeName(reader, handle);

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var specification = reader.GetTypeSpecification(handle);
            return GuardedProviderDecode.TryTypeSpec(
                reader,
                handle,
                this,
                genericContext,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, specification.Signature);
        }

        public string GetSZArrayType(string elementType) => $"{elementType}[]";
        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
        public string GetByReferenceType(string elementType) => $"{elementType}&";
        public string GetPointerType(string elementType) => $"{elementType}*";
        public string GetPinnedType(string elementType) => $"{elementType} pinned";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => $"{genericType}<{string.Join(", ", typeArguments)}>";
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
            => $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            string instance = SignatureThisPrefix(signature.Header);
            string convention = CallingConventionPrefix(signature.Header.CallingConvention);
            return $"method {instance}{convention}{signature.ReturnType} *({FormatParameterList(signature)})";
        }

        static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { return $"{TypeName(reader, declaring)}+{name}"; }
                finally { s_climbDepth--; }
            }
            string ns = reader.GetString(type.Namespace);
            string assembly = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";
            return $"[{assembly}]{(ns.Length == 0 ? name : $"{ns}.{name}")}";
        }

        static string TypeName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            string ns = reader.GetString(type.Namespace);
            string fullName = ns.Length == 0 ? name : $"{ns}.{name}";
            if (type.ResolutionScope.Kind == HandleKind.AssemblyReference)
                return $"[{AssemblyReferenceIdentity(reader, (AssemblyReferenceHandle)type.ResolutionScope)}]{fullName}";
            if (type.ResolutionScope.Kind == HandleKind.TypeReference && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { return $"{TypeName(reader, (TypeReferenceHandle)type.ResolutionScope)}+{fullName}"; }
                finally { s_climbDepth--; }
            }
            return fullName;
        }
    }

    static string FormatParameterList(MethodSignature<string> signature)
    {
        if (signature.Header.CallingConvention != SignatureCallingConvention.VarArgs)
            return string.Join(", ", signature.ParameterTypes);

        var builder = ImmutableArray.CreateBuilder<string>(signature.ParameterTypes.Length + 1);
        int requiredCount = Math.Clamp(signature.RequiredParameterCount, 0, signature.ParameterTypes.Length);
        for (int i = 0; i < requiredCount; i++)
            builder.Add(signature.ParameterTypes[i]);
        builder.Add("...");
        for (int i = requiredCount; i < signature.ParameterTypes.Length; i++)
            builder.Add(signature.ParameterTypes[i]);
        return string.Join(", ", builder);
    }

    /// <summary>
    /// Spells the <c>this</c> attributes of a method signature header the way ILAsm
    /// does: <c>instance</c> for <c>HASTHIS</c>, and <c>explicit</c> for
    /// <c>EXPLICITTHIS</c>.
    /// </summary>
    /// <remarks>
    /// Every bit of a signature header that distinguishes two methods has to reach the
    /// rendered operand, because the operand is the whole of what the comparison sees.
    /// <c>EXPLICITTHIS</c> was silently dropped, which made two methods with different
    /// calling conventions render identically. That was latent while names were compared
    /// literally — differing names still differed — and became a masked difference as
    /// soon as <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>
    /// folded the names, since the operand was then the only remaining discriminator.
    /// <para>
    /// Fixing it here rather than by widening the correspondence key is deliberate. A key
    /// term would have to encode the signature, and a signature blob encodes type
    /// references as metadata tokens, which legitimately differ between the two
    /// assemblies being compared for the same logical signature — so keying on it would
    /// suppress real folds. The renderer already decodes to a side-independent spelling,
    /// so the difference belongs there, where it also protects every comparison that does
    /// not fold names at all.
    /// </para>
    /// <para>
    /// The bit is spelled independently of <c>HASTHIS</c>. ECMA-335 II.15.3 only defines
    /// <c>EXPLICITTHIS</c> alongside <c>HASTHIS</c>, but untrusted metadata can set it
    /// alone, and rendering it only in the pair would leave that shape colliding with a
    /// static signature.
    /// </para>
    /// <para>
    /// Gated end-to-end by
    /// <c>IlBodyDiffNormalizationTests.MethodsDifferingOnlyInExplicitThis_DoNotFold</c>
    /// and <c>..._FunctionPointersDifferingOnlyInTheirThisAttributes_AreNotEqual</c>.
    /// </para>
    /// </remarks>
    static string SignatureThisPrefix(SignatureHeader header)
    {
        string instance = header.IsInstance ? "instance " : "";
        string explicitThis = (header.Attributes & SignatureAttributes.ExplicitThis) != 0
            ? "explicit "
            : "";
        return instance + explicitThis;
    }

    static string CallingConventionPrefix(SignatureCallingConvention convention)
    {
        string text = convention switch
        {
            SignatureCallingConvention.Default => "",
            SignatureCallingConvention.VarArgs => "vararg",
            SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
            SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
            SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
            SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
            SignatureCallingConvention.Unmanaged => "unmanaged",
            _ => convention.ToString(),
        };
        return text.Length == 0 ? "" : $"{text} ";
    }

    static string AssemblyReferenceIdentity(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        var reference = reader.GetAssemblyReference(handle);
        string name = reader.GetString(reference.Name);
        string culture = reference.Culture.IsNil ? "neutral" : reader.GetString(reference.Culture);
        string keyOrToken = reference.PublicKeyOrToken.IsNil
            ? "null"
            : Convert.ToHexString(reader.GetBlobBytes(reference.PublicKeyOrToken)).ToLowerInvariant();
        string keyLabel = (reference.Flags & AssemblyFlags.PublicKey) != 0 ? "PublicKey" : "PublicKeyToken";
        string flags = reference.Flags == default ? "" : $", Flags={reference.Flags}";
        return $"{name}, Version={reference.Version}, Culture={culture}, {keyLabel}={keyOrToken}{flags}";
    }
}
