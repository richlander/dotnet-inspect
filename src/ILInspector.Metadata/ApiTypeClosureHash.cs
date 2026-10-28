using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// The closure hash of the API producer's unchanged prefilter
/// (docs/design/finding-producers.md#unchanged-prefilter): a digest of a
/// canonical encoding of one Type's metadata closure, the rows the production
/// API comparer's view of that Type is read from.
/// </summary>
/// <remarks>
/// <para>
/// The encoding interprets nothing. It writes raw row facts (flags, names,
/// constants, attribute blobs, marshalling blobs, layout) and resolves every
/// token to a canonical identity: a Type definition to its escaped definition
/// name, a Type reference to its resolution scope's assembly name and its
/// escaped name, and a Type specification or signature by a lossless decode
/// that keeps custom modifiers and <c>pinned</c>. Member, attribute,
/// interface, constraint, and MethodImpl sets are sorted, so row order does
/// not enter. Every variable-length string is length-prefixed, so the
/// encoding is injective over the facts it writes.
/// </para>
/// <para>
/// Two Types with equal hashes and equal <see cref="Context"/> hashes have
/// equal closures. A Type whose closure cannot be read yields <c>null</c>,
/// which a consumer treats as changed: a malformed row never yields a hash.
/// </para>
/// <para>
/// Slice 1 has no product consumer. <c>ApiTypeClosureHashTests</c>
/// is the gate that no Type with an equal hash has a row in the production
/// comparison.
/// </para>
/// </remarks>
internal sealed class ApiTypeClosureHash
{
    readonly MetadataReader _reader;
    readonly Encoder _encoder;

    public ApiTypeClosureHash(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _encoder = new Encoder(reader);
    }

    /// <summary>
    /// The closure hash of <paramref name="handle"/> under one comparison view,
    /// or <c>null</c> when its closure cannot be read.
    /// </summary>
    /// <param name="view">
    /// <see cref="ApiSurfaceExtractionScope.ApiAxis"/>, the default
    /// <c>diff --library</c> view, or <see cref="ApiSurfaceExtractionScope.IncludeAll"/>
    /// for <c>--all</c>.
    /// </param>
    public UInt128? Type(TypeDefinitionHandle handle, ApiSurfaceExtractionScope view)
        => Encode(handle, view) is { } text ? Digest(text) : null;

    /// <summary>
    /// The canonical text <see cref="Type"/> hashes, for diagnosing a false
    /// change; <c>null</c> when the closure cannot be read.
    /// </summary>
    public string? Encode(TypeDefinitionHandle handle, ApiSurfaceExtractionScope view)
    {
        RequireView(view);
        try
        {
            return _encoder.Type(handle, view == ApiSurfaceExtractionScope.IncludeAll);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The hash of the narrow context outside every Type that the production
    /// view also reads: the assembly's name, culture, and public key (not its
    /// version), and every module-level attribute, which carry the compiler's
    /// module context such as <c>NullablePublicOnly</c>, <c>RefSafetyRules</c>,
    /// and <c>MemorySafetyRules</c>. When two images' context hashes differ,
    /// no Type is skippable. <c>null</c> when the context cannot be read.
    /// </summary>
    public UInt128? Context()
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append("assembly|").Append(_reader.IsAssembly ? '1' : '0');
            if (_reader.IsAssembly)
            {
                AssemblyDefinition assembly = _reader.GetAssemblyDefinition();
                builder.Append('|').Append(Text(_reader.GetString(assembly.Name)))
                    .Append('|').Append(Text(_reader.GetString(assembly.Culture)))
                    .Append('|').Append(Convert.ToHexString(_reader.GetBlobBytes(assembly.PublicKey)));
            }
            builder.Append("\nmodule|")
                .Append(_encoder.Attributes(_reader.GetModuleDefinition().GetCustomAttributes()));
            return Digest(builder.ToString());
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    static void RequireView(ApiSurfaceExtractionScope view)
    {
        if (view is not (ApiSurfaceExtractionScope.ApiAxis or ApiSurfaceExtractionScope.IncludeAll))
        {
            throw new ArgumentOutOfRangeException(
                nameof(view),
                view,
                "A closure hash serves the API axis or the include-all view.");
        }
    }

    static bool IsUnreadable(Exception ex)
        => ex is BadImageFormatException or ArgumentException or UnreadableClosureException;

    static UInt128 Digest(string text)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), digest);
        return BinaryPrimitives.ReadUInt128LittleEndian(digest);
    }

    static string Text(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;

    sealed class UnreadableClosureException(string message) : Exception(message);

    /// <summary>
    /// Writes closure facts. As a signature provider it spells a decoded Type
    /// losslessly; it records the Type definitions one member signature
    /// names while <see cref="_referenced"/> is set.
    /// </summary>
    sealed class Encoder(MetadataReader reader) : ISignatureTypeProvider<string, object?>
    {
        readonly MetadataReader _md = reader;
        readonly Dictionary<TypeDefinitionHandle, string> _definitions = [];
        readonly Dictionary<TypeReferenceHandle, string> _references = [];
        List<TypeDefinitionHandle>? _referenced;

        public string Type(TypeDefinitionHandle handle, bool includeAll)
        {
            TypeDefinition type = _md.GetTypeDefinition(handle);
            var builder = new StringBuilder();
            builder.Append("T|").Append(Flags((int)type.Attributes)).Append('|')
                .Append(Text(DefinitionName(handle))).Append('\n');
            builder.Append("base|").Append(type.BaseType.IsNil ? "-" : Entity(type.BaseType)).Append('\n');

            var interfaces = new List<string>();
            foreach (InterfaceImplementationHandle interfaceHandle in type.GetInterfaceImplementations())
            {
                InterfaceImplementation implementation = _md.GetInterfaceImplementation(interfaceHandle);
                interfaces.Add(Entity(implementation.Interface) + "|" + Attributes(implementation.GetCustomAttributes()));
            }
            AppendSorted(builder, "interface|", interfaces);

            builder.Append("generic|").Append(GenericParameters(type.GetGenericParameters())).Append('\n');
            builder.Append("attributes|").Append(Attributes(type.GetCustomAttributes())).Append('\n');
            TypeLayout layout = type.GetLayout();
            builder.Append("layout|").Append(layout.PackingSize).Append('|').Append(layout.Size).Append('\n');

            // Enclosing Types' flags decide admission and their attributes carry
            // inherited nullability context. The name read above has bounded
            // the declaring chain.
            int remaining = _md.GetTableRowCount(TableIndex.TypeDef);
            for (TypeDefinitionHandle enclosing = type.GetDeclaringType(); !enclosing.IsNil;)
            {
                if (remaining-- == 0)
                    throw new UnreadableClosureException("The nested Type graph contains a cycle.");
                TypeDefinition definition = _md.GetTypeDefinition(enclosing);
                builder.Append("enclosing|").Append(Flags((int)definition.Attributes)).Append('|')
                    .Append(Attributes(definition.GetCustomAttributes())).Append('\n');
                enclosing = definition.GetDeclaringType();
            }

            var implementationBodies = new HashSet<MethodDefinitionHandle>();
            var implementationsByBody = new Dictionary<MethodDefinitionHandle, List<string>>();
            var foreignImplementations = new List<string>();
            foreach (MethodImplementationHandle implementationHandle in type.GetMethodImplementations())
            {
                MethodImplementation implementation = _md.GetMethodImplementation(implementationHandle);
                string declaration = ImplementedDeclaration(implementation.MethodDeclaration);
                if (implementation.MethodBody.Kind == HandleKind.MethodDefinition)
                {
                    var body = (MethodDefinitionHandle)implementation.MethodBody;
                    implementationBodies.Add(body);
                    if (!implementationsByBody.TryGetValue(body, out List<string>? declarations))
                    {
                        declarations = [];
                        implementationsByBody.Add(body, declarations);
                    }
                    declarations.Add(declaration);
                }
                else
                {
                    foreignImplementations.Add(MethodReference(implementation.MethodBody) + "|" + declaration);
                }
            }
            AppendSorted(builder, "implementation|", foreignImplementations);

            // Member admission is the API axis's own rule, widened by every
            // MethodImpl body: a superset of the comparer's population only
            // costs a false change.
            Dictionary<MethodDefinitionHandle, ApiSurfaceExtractor.InterfaceImplementationAccess>
                interfaceImplementations = ApiSurfaceExtractor.GetInterfaceImplementations(_md, type);
            bool AdmitsMethod(MethodDefinitionHandle methodHandle)
                => includeAll
                    || implementationBodies.Contains(methodHandle)
                    || ApiSurfaceExtractor.AdmitsMethodAccess(
                        ApiSurfaceExtractor.MethodEffectiveAccess(
                            _md.GetMethodDefinition(methodHandle).Attributes & MethodAttributes.MemberAccessMask,
                            methodHandle,
                            interfaceImplementations),
                        includeAll: false,
                        apiAxis: true);

            var members = new List<string>();
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (AdmitsMethod(methodHandle))
                    members.Add(Method(methodHandle, implementationsByBody));
            }

            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = _md.GetFieldDefinition(fieldHandle);
                if (includeAll
                    || ApiSurfaceExtractor.AdmitsFieldAccess(
                        field.Attributes & FieldAttributes.FieldAccessMask,
                        includeAll: false,
                        apiAxis: true))
                {
                    members.Add(Field(field));
                }
            }

            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = _md.GetPropertyDefinition(propertyHandle);
                PropertyAccessors accessors = property.GetAccessors();
                var roles = new List<(string Role, MethodDefinitionHandle Method)>();
                if (!accessors.Getter.IsNil)
                    roles.Add(("get", accessors.Getter));
                if (!accessors.Setter.IsNil)
                    roles.Add(("set", accessors.Setter));
                foreach (MethodDefinitionHandle other in accessors.Others)
                    roles.Add(("other", other));
                if (!includeAll
                    && !roles.Exists(role => implementationBodies.Contains(role.Method))
                    && !ApiSurfaceExtractor.AdmitsMethodAccess(
                        ApiSurfaceExtractor.PropertyAccess(_md, accessors, interfaceImplementations),
                        includeAll: false,
                        apiAxis: true))
                {
                    continue;
                }

                var member = new StringBuilder();
                member.Append("P|").Append(Flags((int)property.Attributes)).Append('|')
                    .Append(Text(_md.GetString(property.Name))).Append('|')
                    .Append(Signature(Decoded(GuardedProviderDecode.PropertyResult(
                        _md, property, this, null, string.Empty))))
                    .Append('|').Append(Constant(property.GetDefaultValue()))
                    .Append("|attributes:").Append(Attributes(property.GetCustomAttributes()));
                AppendAccessors(member, roles, implementationsByBody);
                members.Add(member.ToString());
            }

            foreach (EventDefinitionHandle eventHandle in type.GetEvents())
            {
                EventDefinition @event = _md.GetEventDefinition(eventHandle);
                EventAccessors accessors = @event.GetAccessors();
                var roles = new List<(string Role, MethodDefinitionHandle Method)>();
                if (!accessors.Adder.IsNil)
                    roles.Add(("add", accessors.Adder));
                if (!accessors.Remover.IsNil)
                    roles.Add(("remove", accessors.Remover));
                if (!accessors.Raiser.IsNil)
                    roles.Add(("raise", accessors.Raiser));
                foreach (MethodDefinitionHandle other in accessors.Others)
                    roles.Add(("other", other));
                if (!includeAll
                    && !roles.Exists(role => implementationBodies.Contains(role.Method))
                    && !(ApiSurfaceExtractor.EventAccess(_md, accessors, interfaceImplementations) is { } access
                        && ApiSurfaceExtractor.AdmitsMethodAccess(access, includeAll: false, apiAxis: true)))
                {
                    continue;
                }

                var member = new StringBuilder();
                member.Append("E|").Append(Flags((int)@event.Attributes)).Append('|')
                    .Append(Text(_md.GetString(@event.Name))).Append('|')
                    .Append(Entity(@event.Type))
                    .Append("|attributes:").Append(Attributes(@event.GetCustomAttributes()));
                AppendAccessors(member, roles, implementationsByBody);
                members.Add(member.ToString());
            }

            AppendSorted(builder, string.Empty, members);
            return builder.ToString();
        }

        void AppendAccessors(
            StringBuilder member,
            List<(string Role, MethodDefinitionHandle Method)> roles,
            Dictionary<MethodDefinitionHandle, List<string>> implementationsByBody)
        {
            var accessors = new List<string>(roles.Count);
            foreach ((string role, MethodDefinitionHandle method) in roles)
                accessors.Add(role + ":" + Method(method, implementationsByBody).Replace("\n", "\n  ", StringComparison.Ordinal));
            accessors.Sort(StringComparer.Ordinal);
            foreach (string accessor in accessors)
                member.Append("\n  accessor|").Append(accessor);
        }

        static void AppendSorted(StringBuilder builder, string prefix, List<string> lines)
        {
            lines.Sort(StringComparer.Ordinal);
            foreach (string line in lines)
                builder.Append(prefix).Append(line).Append('\n');
        }

        string Method(
            MethodDefinitionHandle handle,
            Dictionary<MethodDefinitionHandle, List<string>> implementationsByBody)
        {
            MethodDefinition method = _md.GetMethodDefinition(handle);
            var referenced = new List<TypeDefinitionHandle>();
            _referenced = referenced;
            MethodSignature<string> signature;
            try
            {
                signature = Decoded(GuardedProviderDecode.MethodResult(_md, method, this, null, string.Empty));
            }
            finally
            {
                _referenced = null;
            }

            var builder = new StringBuilder();
            builder.Append("M|").Append(Flags((int)method.Attributes)).Append('|')
                .Append(Flags((int)method.ImplAttributes)).Append('|')
                .Append(Text(_md.GetString(method.Name))).Append('|')
                .Append(Signature(signature));

            var parameters = new List<(int Sequence, string Text)>();
            bool hasConstant = false;
            foreach (ParameterHandle parameterHandle in method.GetParameters())
            {
                Parameter parameter = _md.GetParameter(parameterHandle);
                ConstantHandle constant = parameter.GetDefaultValue();
                hasConstant |= !constant.IsNil;
                var text = new StringBuilder();
                text.Append("\n  parameter|").Append(parameter.SequenceNumber).Append('|')
                    .Append(Text(_md.GetString(parameter.Name))).Append('|')
                    .Append(Flags((int)parameter.Attributes)).Append('|')
                    .Append(Constant(constant))
                    .Append("|attributes:").Append(Attributes(parameter.GetCustomAttributes()));
                BlobHandle marshalling = parameter.GetMarshallingDescriptor();
                if (!marshalling.IsNil)
                    text.Append("|marshal:").Append(Convert.ToHexString(_md.GetBlobBytes(marshalling)));
                parameters.Add((parameter.SequenceNumber, text.ToString()));
            }
            parameters.Sort(static (left, right) =>
                left.Sequence != right.Sequence
                    ? left.Sequence.CompareTo(right.Sequence)
                    : string.CompareOrdinal(left.Text, right.Text));
            foreach ((_, string text) in parameters)
                builder.Append(text);

            GenericParameterHandleCollection generic = method.GetGenericParameters();
            if (generic.Count > 0)
                builder.Append("\n  generic|").Append(GenericParameters(generic));
            builder.Append("\n  attributes|").Append(Attributes(method.GetCustomAttributes()));

            if (implementationsByBody.TryGetValue(handle, out List<string>? implemented))
            {
                var declarations = new List<string>(implemented);
                declarations.Sort(StringComparer.Ordinal);
                foreach (string declaration in declarations)
                    builder.Append("\n  implements|").Append(declaration);
            }

            MethodImport import = method.GetImport();
            if (!import.Module.IsNil)
            {
                builder.Append("\n  import|").Append(Flags((int)import.Attributes)).Append('|')
                    .Append(Text(_md.GetString(import.Name))).Append('|')
                    .Append(Text(_md.GetString(_md.GetModuleReference(import.Module).Name)));
            }

            // A constant default of an enum-typed parameter is spelled with the
            // name of the enum member that holds its value, which is a row of
            // the enum's definition. Include the literal fields of every
            // definition this signature names.
            if (hasConstant)
            {
                foreach (TypeDefinitionHandle definition in referenced.Distinct())
                    builder.Append("\n  literals|").Append(Literals(definition));
            }

            return builder.ToString();
        }

        string Literals(TypeDefinitionHandle handle)
        {
            var literals = new List<string>();
            foreach (FieldDefinitionHandle fieldHandle in _md.GetTypeDefinition(handle).GetFields())
            {
                FieldDefinition field = _md.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Literal) != 0)
                    literals.Add(Text(_md.GetString(field.Name)) + "=" + Constant(field.GetDefaultValue()));
            }
            literals.Sort(StringComparer.Ordinal);
            return Text(DefinitionName(handle)) + "|" + string.Join(";", literals);
        }

        string Field(FieldDefinition field)
        {
            var builder = new StringBuilder();
            builder.Append("F|").Append(Flags((int)field.Attributes)).Append('|')
                .Append(Text(_md.GetString(field.Name))).Append('|')
                .Append(Decoded(GuardedProviderDecode.FieldResult(_md, field, this, null, string.Empty)))
                .Append('|').Append(Constant(field.GetDefaultValue()))
                .Append("|attributes:").Append(Attributes(field.GetCustomAttributes()));
            BlobHandle marshalling = field.GetMarshallingDescriptor();
            if (!marshalling.IsNil)
                builder.Append("|marshal:").Append(Convert.ToHexString(_md.GetBlobBytes(marshalling)));
            int offset = field.GetOffset();
            if (offset >= 0)
                builder.Append("|offset:").Append(offset);
            return builder.ToString();
        }

        /// <summary>
        /// An implemented declaration and, when it is declared in this image,
        /// its declaring Type's flags: a private MethodImpl body takes the
        /// implemented interface's access.
        /// </summary>
        string ImplementedDeclaration(EntityHandle declaration)
        {
            TypeDefinitionHandle? declaring = declaration.Kind switch
            {
                HandleKind.MethodDefinition =>
                    _md.GetMethodDefinition((MethodDefinitionHandle)declaration).GetDeclaringType(),
                HandleKind.MemberReference =>
                    ApiSurfaceExtractor.DeclaringTypeDefinition(
                        _md,
                        _md.GetMemberReference((MemberReferenceHandle)declaration).Parent),
                _ => throw new UnreadableClosureException("A MethodImpl declaration is not a method."),
            };
            return MethodReference(declaration) + "|declaring:"
                + (declaring is { } local ? Flags((int)_md.GetTypeDefinition(local).Attributes) : "-");
        }

        public string Attributes(CustomAttributeHandleCollection attributes)
        {
            if (attributes.Count == 0)
                return string.Empty;
            var encoded = new List<string>(attributes.Count);
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = _md.GetCustomAttribute(handle);
                encoded.Add("@" + MethodReference(attribute.Constructor) + "="
                    + Convert.ToHexString(_md.GetBlobBytes(attribute.Value)));
            }
            encoded.Sort(StringComparer.Ordinal);
            return string.Join(";", encoded);
        }

        string GenericParameters(GenericParameterHandleCollection parameters)
        {
            var builder = new StringBuilder();
            foreach (GenericParameterHandle handle in parameters)
            {
                GenericParameter parameter = _md.GetGenericParameter(handle);
                var constraints = new List<string>();
                foreach (GenericParameterConstraintHandle constraintHandle in parameter.GetConstraints())
                {
                    GenericParameterConstraint constraint = _md.GetGenericParameterConstraint(constraintHandle);
                    constraints.Add(Entity(constraint.Type) + " " + Attributes(constraint.GetCustomAttributes()));
                }
                constraints.Sort(StringComparer.Ordinal);
                builder.Append('<').Append(parameter.Index).Append(':')
                    .Append(Text(_md.GetString(parameter.Name))).Append(':')
                    .Append(Flags((int)parameter.Attributes))
                    .Append(":constraints[").Append(string.Join(",", constraints))
                    .Append("]:attributes[").Append(Attributes(parameter.GetCustomAttributes())).Append("]>");
            }
            return builder.ToString();
        }

        string Constant(ConstantHandle handle)
        {
            if (handle.IsNil)
                return string.Empty;
            System.Reflection.Metadata.Constant constant = _md.GetConstant(handle);
            return "constant:" + ((int)constant.TypeCode).ToString(CultureInfo.InvariantCulture) + ":"
                + Convert.ToHexString(_md.GetBlobBytes(constant.Value));
        }

        string MethodReference(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    MethodDefinition method = _md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    return "method:" + Text(DefinitionName(method.GetDeclaringType())) + "::"
                        + Text(_md.GetString(method.Name)) + Signature(Decoded(
                            GuardedProviderDecode.MethodResult(_md, method, this, null, string.Empty)));
                }
                case HandleKind.MemberReference:
                {
                    MemberReference member = _md.GetMemberReference((MemberReferenceHandle)handle);
                    string parent = member.Parent.Kind switch
                    {
                        HandleKind.MethodDefinition => MethodReference(member.Parent),
                        HandleKind.ModuleReference => "module:" + Text(_md.GetString(
                            _md.GetModuleReference((ModuleReferenceHandle)member.Parent).Name)),
                        _ => Entity(member.Parent),
                    };
                    string signature = member.GetKind() switch
                    {
                        MemberReferenceKind.Method when SignatureBlobGuard.IsSafeToDecode(
                            _md, member.Signature, SignatureBlobGuard.Kind.Method)
                            => Signature(member.DecodeMethodSignature(this, null)),
                        MemberReferenceKind.Field when SignatureBlobGuard.IsSafeToDecode(
                            _md, member.Signature, SignatureBlobGuard.Kind.Field)
                            => "field:" + member.DecodeFieldSignature(this, null),
                        _ => throw new UnreadableClosureException("A member reference signature cannot be decoded."),
                    };
                    return "member:" + parent + "::" + Text(_md.GetString(member.Name)) + signature;
                }
                default:
                    throw new UnreadableClosureException("A method reference is not a method row.");
            }
        }

        string Entity(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => "def:" + Text(DefinitionName((TypeDefinitionHandle)handle)),
            HandleKind.TypeReference => "ref:" + ReferenceName((TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => "spec:" + Specification((TypeSpecificationHandle)handle),
            _ => throw new UnreadableClosureException("A Type token is not a Type row."),
        };

        string DefinitionName(TypeDefinitionHandle handle)
        {
            if (_definitions.TryGetValue(handle, out string? name))
                return name;
            name = MetadataTypeDefinitionName.Read(_md, handle) is MetadataTypeDefinitionNameReadResult.Read read
                ? read.Name.ToEscapedFullName()
                : throw new UnreadableClosureException("A Type definition name cannot be read.");
            _definitions.Add(handle, name);
            return name;
        }

        /// <summary>
        /// A Type reference's canonical identity: its resolution scope's
        /// assembly name (never its version), or the module it names, and its
        /// escaped nested name.
        /// </summary>
        string ReferenceName(TypeReferenceHandle handle)
        {
            if (_references.TryGetValue(handle, out string? name))
                return name;
            Span<TypeReferenceHandle> rootToLeaf = stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    _md, handle, rootToLeaf, out _, out EntityHandle terminal, out _)
                || MetadataTypeDefinitionName.Read(_md, handle) is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                throw new UnreadableClosureException("A Type reference name cannot be read.");
            }

            string scope = terminal.Kind switch
            {
                _ when terminal.IsNil => "exported",
                HandleKind.AssemblyReference => "assembly:" + Text(_md.GetString(
                    _md.GetAssemblyReference((AssemblyReferenceHandle)terminal).Name)),
                HandleKind.ModuleReference => "module:" + Text(_md.GetString(
                    _md.GetModuleReference((ModuleReferenceHandle)terminal).Name)),
                HandleKind.ModuleDefinition => "self",
                _ => throw new UnreadableClosureException("A Type reference scope is not a scope row."),
            };
            name = "[" + scope + "]" + Text(read.Name.ToEscapedFullName());
            _references.Add(handle, name);
            return name;
        }

        string Specification(TypeSpecificationHandle handle)
        {
            if (!TypeSpecGuard.TryEnter(_md, handle, out TypeSpecGuard.Scope scope))
                throw new UnreadableClosureException("A Type specification exceeds the decode bounds.");
            using (scope)
                return _md.GetTypeSpecification(handle).DecodeSignature(this, null);
        }

        static T Decoded<T>(GuardedProviderDecode.DecodeResult<T> result)
            => result.IsDegraded
                ? throw new UnreadableClosureException("A signature exceeds the decode bounds.")
                : result.Value;

        static string Flags(int value) => value.ToString("X", CultureInfo.InvariantCulture);

        static string Signature(MethodSignature<string> signature)
            => Flags(signature.Header.RawValue) + "|generic:" + signature.GenericParameterCount
                + "|" + signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes)
                + ";" + signature.RequiredParameterCount + ")";

        public string GetArrayType(string elementType, ArrayShape shape)
            => elementType + "[" + shape.Rank + ";" + string.Join(",", shape.Sizes) + ";"
                + string.Join(",", shape.LowerBounds) + "]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr(" + Signature(signature) + ")";

        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
            => genericType + "<" + string.Join(",", typeArguments) + ">";

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
            => (isRequired ? "modreq(" : "modopt(") + modifier + ")(" + unmodifiedType + ")";

        public string GetPinnedType(string elementType) => "pinned(" + elementType + ")";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "primitive:" + (int)typeCode;

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            _referenced?.Add(handle);
            return "def:" + Text(DefinitionName(handle)) + "/" + rawTypeKind;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => "ref:" + ReferenceName(handle) + "/" + rawTypeKind;

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
            => Specification(handle);
    }
}
