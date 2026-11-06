#:property PublishAot=false
#:property TreatWarningsAsErrors=false
#:property NoWarn=$(NoWarn);CS9124;CS8321

// Prototype extractor for the Library symbol document format trial (#11028).
// It is not the product producer: it approximates owner-issued spellings from
// System.Reflection.Metadata so agents can be tested against a complete file
// before slice 1 exists. layout.py turns its output into the document layout.
//
// Usage:
//   dotnet run extract.cs -- pdb-key <dll>
//   dotnet run extract.cs -- extract <dll> <xml|-> <pdb|-> <package-id> <package-version> <package-path> <tfm> <out.json>

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args is ["pdb-key", var keyDll])
{
    using var keyPe = new PEReader(File.OpenRead(keyDll));
    foreach (var entry in keyPe.ReadDebugDirectory())
    {
        if (entry.Type != DebugDirectoryEntryType.CodeView)
        {
            continue;
        }

        var cv = keyPe.ReadCodeViewDebugDirectoryData(entry);
        Console.WriteLine($"{Path.GetFileName(cv.Path)}/{cv.Guid:N}FFFFFFFF/{Path.GetFileName(cv.Path)}");
        return 0;
    }

    Console.Error.WriteLine("No CodeView entry.");
    return 1;
}

if (args is not ["extract", var dll, var xml, var pdb, var packageId, var packageVersion, var packagePath, var tfm, var output])
{
    Console.Error.WriteLine("usage: extract <dll> <xml|-> <pdb|-> <package-id> <package-version> <package-path> <tfm> <out.json>");
    return 2;
}

using var pe = new PEReader(File.OpenRead(dll));
var md = pe.GetMetadataReader();
var extractor = new Extractor(md, xml == "-" ? null : xml, pdb == "-" ? null : pdb);
var root = extractor.Run(Path.GetFileName(dll), packageId, packageVersion, packagePath, tfm);
File.WriteAllText(output, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
Console.Error.WriteLine($"types {extractor.TypeCount}, members {extractor.MemberCount}, documented {extractor.Documented}/{extractor.MemberCount + extractor.TypeCount}, sourced {extractor.Sourced}");
return 0;

// A decoded signature type, rendered three ways: C# display, canonical full name, and documentation ID.
abstract record Sig
{
    public sealed record Prim(PrimitiveTypeCode Code) : Sig;
    public sealed record Named(string Namespace, ImmutableArray<string> Segments, bool IsValueType, ImmutableArray<Sig> Args) : Sig;
    public sealed record TypeParam(int Index, bool IsMethod) : Sig;
    public sealed record SzArray(Sig Element) : Sig;
    public sealed record MdArray(Sig Element, int Rank) : Sig;
    public sealed record ByRef(Sig Element) : Sig;
    public sealed record Pointer(Sig Element) : Sig;
    public sealed record FnPtr : Sig;
}

sealed class SigProvider(MetadataReader md) : ISignatureTypeProvider<Sig, object?>
{
    public Sig GetPrimitiveType(PrimitiveTypeCode typeCode) => new Sig.Prim(typeCode);

    public Sig GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => new Sig.Named(Names.Namespace(md, handle), Names.Segments(md, handle), rawTypeKind == (byte)SignatureTypeKind.ValueType, []);

    public Sig GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var segments = new List<string>();
        var current = handle;
        string ns;
        while (true)
        {
            var reference = md.GetTypeReference(current);
            segments.Insert(0, md.GetString(reference.Name));
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                current = (TypeReferenceHandle)reference.ResolutionScope;
                continue;
            }

            ns = md.GetString(reference.Namespace);
            break;
        }

        return new Sig.Named(ns, [.. segments], rawTypeKind == (byte)SignatureTypeKind.ValueType, []);
    }

    public Sig GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => md.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public Sig GetSZArrayType(Sig elementType) => new Sig.SzArray(elementType);
    public Sig GetArrayType(Sig elementType, ArrayShape shape) => new Sig.MdArray(elementType, shape.Rank);
    public Sig GetByReferenceType(Sig elementType) => new Sig.ByRef(elementType);
    public Sig GetPointerType(Sig elementType) => new Sig.Pointer(elementType);
    public Sig GetGenericInstantiation(Sig genericType, ImmutableArray<Sig> typeArguments)
        => genericType is Sig.Named named ? named with { Args = typeArguments } : genericType;
    public Sig GetGenericTypeParameter(object? genericContext, int index) => new Sig.TypeParam(index, false);
    public Sig GetGenericMethodParameter(object? genericContext, int index) => new Sig.TypeParam(index, true);
    public Sig GetFunctionPointerType(MethodSignature<Sig> signature) => new Sig.FnPtr();
    public Sig GetModifiedType(Sig modifier, Sig unmodifiedType, bool isRequired) => unmodifiedType;
    public Sig GetPinnedType(Sig elementType) => elementType;
}

static class Names
{
    static readonly Regex Arity = new("`\\d+$");

    public static string Namespace(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        return type.GetDeclaringType().IsNil ? md.GetString(type.Namespace) : Namespace(md, type.GetDeclaringType());
    }

    public static ImmutableArray<string> Segments(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var name = md.GetString(type.Name);
        return type.GetDeclaringType().IsNil ? [name] : Segments(md, type.GetDeclaringType()).Add(name);
    }

    public static string StripArity(string name) => Arity.Replace(name, "");

    public static int ArityOf(string name)
    {
        var match = Arity.Match(name);
        return match.Success ? int.Parse(match.Value[1..]) : 0;
    }

    public static string Join(string ns, IEnumerable<string> segments)
        => ns.Length == 0 ? string.Join(".", segments) : ns + "." + string.Join(".", segments);

    public static readonly Dictionary<string, string> Keywords = new()
    {
        ["System.Boolean"] = "bool", ["System.Char"] = "char", ["System.SByte"] = "sbyte", ["System.Byte"] = "byte",
        ["System.Int16"] = "short", ["System.UInt16"] = "ushort", ["System.Int32"] = "int", ["System.UInt32"] = "uint",
        ["System.Int64"] = "long", ["System.UInt64"] = "ulong", ["System.Single"] = "float", ["System.Double"] = "double",
        ["System.String"] = "string", ["System.Object"] = "object", ["System.Void"] = "void",
        ["System.IntPtr"] = "nint", ["System.UIntPtr"] = "nuint",
    };

    public static string PrimitiveFullName(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => "System." + code,
    };
}

sealed class Extractor(MetadataReader md, string? xmlPath, string? pdbPath)
{
    readonly SigProvider _provider = new(md);
    readonly Dictionary<string, string> _summaries = LoadSummaries(xmlPath);
    MetadataReader? _pdb;
    List<(string Prefix, string Target)> _sourceLink = [];
    public int TypeCount, MemberCount, Documented, Sourced;

    static readonly HashSet<string> s_skippedAttributeNamespaces = ["System.Runtime.CompilerServices"];
    static readonly HashSet<string> s_skippedAttributes =
    [
        "System.Diagnostics.DebuggerBrowsableAttribute", "System.Diagnostics.DebuggerDisplayAttribute",
        "System.Diagnostics.DebuggerHiddenAttribute", "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Diagnostics.DebuggerNonUserCodeAttribute", "System.Diagnostics.DebuggerTypeProxyAttribute",
        "System.Reflection.DefaultMemberAttribute", "System.ParamArrayAttribute", "System.Runtime.InteropServices.InAttribute",
        "System.Runtime.InteropServices.OutAttribute", "System.Runtime.InteropServices.OptionalAttribute",
    ];

    public JsonObject Run(string fileName, string packageId, string packageVersion, string packagePath, string tfm)
    {
        var sourceStatus = LoadPdb();
        var assembly = md.GetAssemblyDefinition();
        var mvid = md.GetGuid(md.GetModuleDefinition().Mvid);

        var visible = md.TypeDefinitions.Where(IsVisible).ToList();
        var types = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var typeHandles = new Dictionary<string, TypeDefinitionHandle>();
        foreach (var handle in visible)
        {
            var id = "T:" + DocTypeName(handle);
            types[id] = TypeRecord(handle);
            typeHandles[id] = handle;
        }

        foreach (var handle in md.ExportedTypes)
        {
            var exported = md.GetExportedType(handle);
            if (!exported.IsForwarder || !exported.Implementation.IsNil && exported.Implementation.Kind == HandleKind.ExportedType)
            {
                continue;
            }

            var ns = md.GetString(exported.Namespace);
            var name = md.GetString(exported.Name);
            var target = exported.Implementation.Kind == HandleKind.AssemblyReference
                ? md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation).Name)
                : "";
            types["T:" + Names.Join(ns, [name])] = ForwarderRecord(ns, name, target, MetadataTokens.GetToken(handle));
        }

        var members = new JsonArray();
        foreach (var (id, record) in types)
        {
            if (!typeHandles.TryGetValue(id, out var handle))
            {
                continue;
            }

            foreach (var member in MemberRecords(handle).OrderBy(m => (long)m["metadata_token"]!.GetValue<int>()))
            {
                members.Add(member);
            }
        }

        TypeCount = types.Count;
        MemberCount = members.Count;

        var typesObject = new JsonObject();
        foreach (var (id, record) in types)
        {
            typesObject[id] = record;
        }

        return new JsonObject
        {
            ["schema_version"] = "1",
            ["generator"] = new JsonObject { ["name"] = "library-symbol-document-prototype", ["version"] = "0" },
            ["library"] = new JsonObject
            {
                ["name"] = md.GetString(assembly.Name),
                ["file_name"] = fileName,
                ["assembly_version"] = assembly.Version.ToString(),
                ["module_version_id"] = mvid.ToString(),
                ["source_kind"] = "nuget",
                ["package_id"] = packageId,
                ["package_version"] = packageVersion,
                ["package_path"] = packagePath,
                ["target_framework"] = tfm,
            },
            ["request"] = new JsonObject
            {
                ["admission"] = "public_api",
                ["sections"] = new JsonArray("declarations", "name_families", "documentation", "source_link"),
            },
            ["types"] = typesObject,
            ["members"] = members,
            ["section_outcomes"] = new JsonArray(
                Outcome("declarations", "complete"),
                Outcome("name_families", "complete"),
                Outcome("documentation", _summaries.Count > 0 ? "complete" : "unavailable"),
                Outcome("source_link", sourceStatus == "complete" ? "complete" : sourceStatus),
                Outcome("findings", "not_requested")),
        };
    }

    static JsonObject Outcome(string name, string status) => new() { ["name"] = name, ["status"] = status };

    string LoadPdb()
    {
        if (pdbPath is null)
        {
            return "no_pdb";
        }

        var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
        _pdb = provider.GetMetadataReader();
        var sourceLinkGuid = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
        foreach (var handle in _pdb.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var info = _pdb.GetCustomDebugInformation(handle);
            if (_pdb.GetGuid(info.Kind) != sourceLinkGuid)
            {
                continue;
            }

            using var json = JsonDocument.Parse(_pdb.GetBlobBytes(info.Value));
            foreach (var property in json.RootElement.GetProperty("documents").EnumerateObject())
            {
                _sourceLink.Add((property.Name.TrimEnd('*'), property.Value.GetString()!.TrimEnd('*')));
            }
        }

        return _sourceLink.Count > 0 ? "complete" : "no_source_link";
    }

    static Dictionary<string, string> LoadSummaries(string? path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (path is null)
        {
            return result;
        }

        foreach (var member in XDocument.Load(path).Descendants("member"))
        {
            var name = (string?)member.Attribute("name");
            var summary = member.Element("summary");
            if (name is null || summary is null)
            {
                continue;
            }

            var text = new StringBuilder();
            Render(summary, text);
            result[name] = Regex.Replace(text.ToString(), "\\s+", " ").Trim();
        }

        return result;
    }

    static void Render(XElement element, StringBuilder text)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement e when e.Name.LocalName is "see" or "seealso":
                    if ((string?)e.Attribute("langword") is { } word)
                    {
                        text.Append(word);
                    }
                    else if ((string?)e.Attribute("cref") is { } cref)
                    {
                        var bare = cref.Contains(':') ? cref[(cref.IndexOf(':') + 1)..] : cref;
                        bare = bare.Split('(')[0].Split('{')[0];
                        text.Append(Regex.Replace(bare.Split('.')[^1], "`+\\d+$", ""));
                    }
                    else
                    {
                        Render(e, text);
                    }

                    break;
                case XElement e when e.Name.LocalName is "paramref" or "typeparamref":
                    text.Append((string?)e.Attribute("name"));
                    break;
                case XElement e:
                    Render(e, text);
                    break;
            }
        }
    }

    bool IsVisible(TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public)
        {
            return true;
        }

        return visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
            && IsVisible(type.GetDeclaringType());
    }


    // Produced types: what a caller obtains, with Task<T>, ValueTask<T>, Nullable<T>, by-ref, and
    // array wrappers removed, reduced to the outermost Type definition.
    static IEnumerable<string> Produced(IEnumerable<Sig> sigs)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var sig in sigs)
        {
            var current = sig;
            while (Unwrap(current) is { } inner)
            {
                current = inner;
            }

            if (DefinitionId(current) is { Length: > 0 } id
                && id is not ("T:System.Threading.Tasks.Task" or "T:System.Threading.Tasks.ValueTask"))
            {
                result.Add(id);
            }
        }

        return result;
    }

    static Sig? Unwrap(Sig sig) => sig switch
    {
        Sig.ByRef r => r.Element,
        Sig.SzArray a => a.Element,
        Sig.MdArray a => a.Element,
        Sig.Named { Namespace: "System.Threading.Tasks", Segments: ["Task`1" or "ValueTask`1"], Args: [var inner] } => inner,
        Sig.Named { Namespace: "System", Segments: ["Nullable`1"], Args: [var inner] } => inner,
        _ => null,
    };

    Dictionary<string, TypeDefinitionHandle>? _byName;

    (TypeDefinitionHandle Handle, ImmutableArray<Sig> Args)? BaseOf(TypeDefinitionHandle handle)
    {
        _byName ??= md.TypeDefinitions.ToDictionary(DocTypeName, h => h);
        var type = md.GetTypeDefinition(handle);
        if (type.BaseType.IsNil || DecodeHandle(type.BaseType) is not Sig.Named named)
        {
            return null;
        }

        return _byName.TryGetValue(Names.Join(named.Namespace, named.Segments), out var baseHandle) ? (baseHandle, named.Args) : null;
    }

    static Sig Substitute(Sig sig, ImmutableArray<Sig> args) => sig switch
    {
        Sig.TypeParam { IsMethod: false } t when t.Index < args.Length => args[t.Index],
        Sig.Named n => n with { Args = [.. n.Args.Select(a => Substitute(a, args))] },
        Sig.SzArray a => new Sig.SzArray(Substitute(a.Element, args)),
        Sig.MdArray a => new Sig.MdArray(Substitute(a.Element, args), a.Rank),
        Sig.ByRef r => new Sig.ByRef(Substitute(r.Element, args)),
        Sig.Pointer p => new Sig.Pointer(Substitute(p.Element, args)),
        _ => sig,
    };

    // The base-class method a method overrides, found along the base chain inside this Library.
    MethodDefinitionHandle? OverriddenMethod(MethodDefinitionHandle handle)
    {
        var method = md.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.Virtual) == 0 || (method.Attributes & MethodAttributes.NewSlot) != 0)
        {
            return null;
        }

        var name = md.GetString(method.Name);
        var arity = method.GetGenericParameters().Count;
        var want = method.DecodeSignature(_provider, null).ParameterTypes.Select(DocId).ToList();
        var current = method.GetDeclaringType();
        ImmutableArray<Sig>? map = null;
        while (BaseOf(current) is var (baseHandle, args))
        {
            ImmutableArray<Sig> inDerived = map is { } m ? [.. args.Select(a => Substitute(a, m))] : args;
            foreach (var candidate in md.GetTypeDefinition(baseHandle).GetMethods())
            {
                var definition = md.GetMethodDefinition(candidate);
                if ((definition.Attributes & MethodAttributes.Virtual) == 0
                    || md.GetString(definition.Name) != name
                    || definition.GetGenericParameters().Count != arity)
                {
                    continue;
                }

                var got = definition.DecodeSignature(_provider, null).ParameterTypes.Select(p => DocId(Substitute(p, inDerived)));
                if (got.SequenceEqual(want))
                {
                    return candidate;
                }
            }

            current = baseHandle;
            map = inDerived;
        }

        return null;
    }

    string MethodDocumentationId(MethodDefinitionHandle handle)
    {
        var method = md.GetMethodDefinition(handle);
        var name = md.GetString(method.Name);
        var signature = method.DecodeSignature(_provider, null);
        var arity = method.GetGenericParameters().Count;
        var docName = name == ".ctor" ? "#ctor" : name.Replace('.', '#');
        var parameters = signature.ParameterTypes.Select(DocId).ToList();
        var conversion = name is "op_Implicit" or "op_Explicit" ? "~" + DocId(signature.ReturnType) : "";
        return $"M:{DocTypeName(method.GetDeclaringType())}.{docName}{(arity > 0 ? "``" + arity : "")}"
            + (parameters.Count > 0 ? "(" + string.Join(",", parameters) + ")" : "") + conversion;
    }

    (PropertyDefinitionHandle Handle, TypeDefinitionHandle Type)? PropertyOwning(MethodDefinitionHandle accessor)
    {
        var type = md.GetMethodDefinition(accessor).GetDeclaringType();
        foreach (var handle in md.GetTypeDefinition(type).GetProperties())
        {
            var accessors = md.GetPropertyDefinition(handle).GetAccessors();
            if (accessors.Getter == accessor || accessors.Setter == accessor)
            {
                return (handle, type);
            }
        }

        return null;
    }

    string PropertyDocumentationId(PropertyDefinitionHandle handle, TypeDefinitionHandle type)
    {
        var property = md.GetPropertyDefinition(handle);
        var indexes = property.DecodeSignature(_provider, null).ParameterTypes;
        return $"P:{DocTypeName(type)}.{md.GetString(property.Name).Replace('.', '#')}"
            + (indexes.IsEmpty ? "" : "(" + string.Join(",", indexes.Select(DocId)) + ")");
    }

    string PropertyOverrides(PropertyDefinitionHandle handle)
    {
        var accessors = md.GetPropertyDefinition(handle).GetAccessors();
        foreach (var accessor in new[] { accessors.Getter, accessors.Setter }.Where(a => !a.IsNil))
        {
            if (OverriddenMethod(accessor) is { } overridden && PropertyOwning(overridden) is var (property, type))
            {
                return PropertyDocumentationId(property, type);
            }
        }

        return "";
    }

    // Accessible abstract members a concrete subclass (or implementer) must provide, after the base
    // chain inside this Library has implemented what it implements.
    IEnumerable<string> RequiredOverrides(TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var isInterface = (type.Attributes & TypeAttributes.Interface) != 0;
        if (!isInterface && ((type.Attributes & TypeAttributes.Abstract) == 0 || (type.Attributes & TypeAttributes.Sealed) != 0))
        {
            return [];
        }

        var chain = new List<TypeDefinitionHandle> { handle };
        while (!isInterface && BaseOf(chain[^1]) is var (baseHandle, _))
        {
            chain.Add(baseHandle);
        }

        var pending = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var current in Enumerable.Reverse(chain))
        {
            var definition = md.GetTypeDefinition(current);
            var accessorOwners = new Dictionary<MethodDefinitionHandle, PropertyDefinitionHandle>();
            foreach (var property in definition.GetProperties())
            {
                var accessors = md.GetPropertyDefinition(property).GetAccessors();
                foreach (var accessor in new[] { accessors.Getter, accessors.Setter }.Where(a => !a.IsNil))
                {
                    accessorOwners[accessor] = property;
                }
            }

            foreach (var method in definition.GetMethods())
            {
                var attributes = md.GetMethodDefinition(method).Attributes;
                if ((attributes & MethodAttributes.Static) != 0)
                {
                    continue;
                }

                var id = accessorOwners.TryGetValue(method, out var owner) ? PropertyDocumentationId(owner, current) : MethodDocumentationId(method);
                if (OverriddenMethod(method) is { } overridden)
                {
                    pending.Remove(PropertyOwning(overridden) is var (p, t) ? PropertyDocumentationId(p, t) : MethodDocumentationId(overridden));
                }

                if ((attributes & MethodAttributes.Abstract) != 0 && VisibleMember(attributes))
                {
                    pending.Add(id);
                }
            }
        }

        return pending;
    }

    string DocTypeName(TypeDefinitionHandle handle) => Names.Join(Names.Namespace(md, handle), Names.Segments(md, handle));

    IReadOnlyList<string> TypeParameterNames(TypeDefinitionHandle handle)
        => md.GetTypeDefinition(handle).GetGenericParameters().Select(p => md.GetString(md.GetGenericParameter(p).Name)).ToList();

    static string TypeAccessibility(TypeAttributes attributes) => (attributes & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.NestedFamily => "protected",
        TypeAttributes.NestedFamORAssem => "protected_internal",
        _ => "public",
    };

    string DefinitionKind(TypeDefinition type, out Sig? baseSig)
    {
        baseSig = type.BaseType.IsNil ? null : DecodeHandle(type.BaseType);
        if ((type.Attributes & TypeAttributes.Interface) != 0)
        {
            return "interface";
        }

        var baseName = baseSig is Sig.Named n ? Names.Join(n.Namespace, n.Segments) : "";
        return baseName switch
        {
            "System.Enum" => "enum",
            "System.ValueType" => "struct",
            "System.MulticastDelegate" => "delegate",
            _ => "class",
        };
    }

    Sig DecodeHandle(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => _provider.GetTypeFromDefinition(md, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => _provider.GetTypeFromReference(md, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => _provider.GetTypeFromSpecification(md, null, (TypeSpecificationHandle)handle, 0),
        _ => new Sig.FnPtr(),
    };

    JsonObject TypeRecord(TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var metadataName = md.GetString(type.Name);
        var kind = DefinitionKind(type, out var baseSig);
        var allParams = TypeParameterNames(handle);
        var own = allParams.Skip(allParams.Count - Names.ArityOf(metadataName)).ToList();
        var name = Names.StripArity(metadataName) + (own.Count > 0 ? "<" + string.Join(", ", own) + ">" : "");
        var isInterface = kind == "interface";
        var isStatic = kind == "class" && (type.Attributes & TypeAttributes.Abstract) != 0 && (type.Attributes & TypeAttributes.Sealed) != 0;
        var baseType = kind == "class" && baseSig is not null ? DefinitionId(baseSig) : kind == "delegate" ? "T:System.MulticastDelegate" : "";
        var interfaces = type.GetInterfaceImplementations()
            .Select(i => DefinitionId(DecodeHandle(md.GetInterfaceImplementation(i).Interface)))
            .Where(i => i.Length > 0).Distinct().OrderBy(i => i, StringComparer.Ordinal);
        var (suffix, twoWord) = Families(metadataName);
        var id = "T:" + DocTypeName(handle);
        var summary = Summary(id);
        var urls = TypeSourceUrls(type);

        return new JsonObject
        {
            ["name"] = name,
            ["metadata_name"] = metadataName,
            ["namespace"] = Names.Namespace(md, handle),
            ["declaring_type"] = type.GetDeclaringType().IsNil ? "" : "T:" + DocTypeName(type.GetDeclaringType()),
            ["declaration_kind"] = "definition",
            ["forwarded_to_assembly"] = "",
            ["definition_kind"] = kind,
            ["accessibility"] = TypeAccessibility(type.Attributes),
            ["is_static"] = isStatic,
            ["is_abstract"] = !isStatic && !isInterface && (type.Attributes & TypeAttributes.Abstract) != 0,
            ["is_sealed"] = !isStatic && kind == "class" && (type.Attributes & TypeAttributes.Sealed) != 0,
            ["generic_parameters"] = Array(own),
            ["base_type"] = baseType,
            ["interfaces"] = Array(interfaces),
            ["required_overrides"] = Array(RequiredOverrides(handle)),
            ["attribute_types"] = Array(AttributeTypes(type.GetCustomAttributes())),
            ["suffix_family"] = suffix,
            ["two_word_suffix_family"] = twoWord,
            ["name_words"] = Array(NameWords(metadataName)),
            ["documentation"] = Documentation(summary),
            ["source"] = new JsonObject
            {
                ["status"] = _pdb is null ? "no_pdb" : _sourceLink.Count == 0 ? "no_source_link" : urls.Count > 0 ? "complete" : "no_method_body",
                ["urls"] = Array(urls),
            },
            ["findings"] = new JsonArray(),
            ["metadata_token"] = MetadataTokens.GetToken(handle),
        };
    }

    static JsonObject ForwarderRecord(string ns, string name, string target, int token) => new()
    {
        ["name"] = Names.StripArity(name),
        ["metadata_name"] = name,
        ["namespace"] = ns,
        ["declaring_type"] = "",
        ["declaration_kind"] = "forwarder",
        ["forwarded_to_assembly"] = target,
        ["definition_kind"] = "",
        ["accessibility"] = "public",
        ["is_static"] = false,
        ["is_abstract"] = false,
        ["is_sealed"] = false,
        ["generic_parameters"] = new JsonArray(),
        ["base_type"] = "",
        ["interfaces"] = new JsonArray(),
        ["required_overrides"] = new JsonArray(),
        ["attribute_types"] = new JsonArray(),
        ["suffix_family"] = "",
        ["two_word_suffix_family"] = "",
        ["name_words"] = Array(NameWords(name)),
        ["documentation"] = new JsonObject { ["status"] = "forwarder", ["summary"] = "" },
        ["source"] = new JsonObject { ["status"] = "forwarder", ["urls"] = new JsonArray() },
        ["findings"] = new JsonArray(),
        ["metadata_token"] = token,
    };

    JsonObject Documentation(string? summary)
    {
        if (summary is not null)
        {
            Documented++;
        }

        return new JsonObject { ["status"] = summary is null ? "no_documentation" : "complete", ["summary"] = summary ?? "" };
    }

    string? Summary(string id) => _summaries.TryGetValue(id, out var s) ? s : null;

    static readonly Regex Words = new("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+");

    // Every word of the Type's own name; a digit run joins the word before it (Utf8).
    static List<string> NameWords(string metadataName)
    {
        var words = new List<string>();
        foreach (var token in Words.Matches(Names.StripArity(metadataName)).Select(m => m.Value))
        {
            if (char.IsDigit(token[0]) && words.Count > 0)
            {
                words[^1] += token;
            }
            else
            {
                words.Add(token);
            }
        }

        return words;
    }

    static (string Suffix, string TwoWord) Families(string metadataName)
    {
        var words = Words.Matches(Names.StripArity(metadataName)).Select(m => m.Value).ToList();
        if (words.Count > 0 && char.IsDigit(words[^1][0]))
        {
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count == 0 || !char.IsLetter(words[^1][0]))
        {
            return ("", "");
        }

        return (words[^1], words.Count > 1 ? words[^2] + words[^1] : "");
    }

    static JsonArray Array(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode)JsonValue.Create(v)!)]);

    IEnumerable<string> AttributeTypes(CustomAttributeHandleCollection attributes)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var handle in attributes)
        {
            var name = AttributeTypeName(handle);
            if (name is null || s_skippedAttributes.Contains(name) || s_skippedAttributeNamespaces.Contains(name[..Math.Max(0, name.LastIndexOf('.'))]))
            {
                continue;
            }

            result.Add("T:" + name);
        }

        return result;
    }

    string? AttributeTypeName(CustomAttributeHandle handle)
    {
        var ctor = md.GetCustomAttribute(handle).Constructor;
        EntityHandle parent = ctor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)ctor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType(),
            _ => default,
        };
        return parent.IsNil ? null : DecodeHandle(parent) is Sig.Named n ? Names.Join(n.Namespace, n.Segments) : null;
    }

    bool HasAttribute(CustomAttributeHandleCollection attributes, string fullName)
        => attributes.Any(a => AttributeTypeName(a) == fullName);

    // Roslyn's nullable encoding: one byte per reference type, array, type parameter, and
    // generic value type, in pre-order; a single byte applies to every position.
    Nullability NullableFlag(CustomAttributeHandleCollection attributes, Func<byte> context)
    {
        foreach (var handle in attributes)
        {
            if (AttributeTypeName(handle) != "System.Runtime.CompilerServices.NullableAttribute")
            {
                continue;
            }

            var blob = md.GetBlobReader(md.GetCustomAttribute(handle).Value);
            blob.ReadUInt16();
            if (blob.RemainingBytes == 3)
            {
                return new Nullability([blob.ReadByte()], single: true);
            }

            var count = blob.ReadInt32();
            return new Nullability(blob.ReadBytes(Math.Max(0, count)), single: false);
        }

        return new Nullability([context()], single: true);
    }

    byte ContextFlag(CustomAttributeHandleCollection attributes, Func<byte> outer)
    {
        foreach (var handle in attributes)
        {
            if (AttributeTypeName(handle) != "System.Runtime.CompilerServices.NullableContextAttribute")
            {
                continue;
            }

            var blob = md.GetBlobReader(md.GetCustomAttribute(handle).Value);
            blob.ReadUInt16();
            return blob.ReadByte();
        }

        return outer();
    }

    byte TypeContext(TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        return ContextFlag(type.GetCustomAttributes(), () => type.GetDeclaringType().IsNil ? (byte)0 : TypeContext(type.GetDeclaringType()));
    }

    // Rendering ---------------------------------------------------------------

    string Display(Sig sig, IReadOnlyList<string> typeParams, IReadOnlyList<string> methodParams, Nullability? n = null)
    {
        string Mark(byte flag) => flag == 2 ? "?" : "";
        switch (sig)
        {
            case Sig.Prim p when p.Code is PrimitiveTypeCode.String or PrimitiveTypeCode.Object:
            {
                var flag = n?.Next() ?? 0;
                return Names.Keywords[Names.PrimitiveFullName(p.Code)] + Mark(flag);
            }
            case Sig.Prim p:
                return Names.Keywords.TryGetValue(Names.PrimitiveFullName(p.Code), out var k) ? k : Names.PrimitiveFullName(p.Code);
            case Sig.Named { Namespace: "System", Segments: ["Nullable`1"], Args: [var inner] }:
                return "System.Nullable<" + Display(inner, typeParams, methodParams, n) + ">";
            case Sig.Named named:
            {
                var flag = !named.IsValueType || !named.Args.IsEmpty ? n?.Next() ?? 0 : (byte)0;
                var text = NamedDisplay(named, typeParams, methodParams, n);
                return named.IsValueType ? text : text + Mark(flag);
            }
            case Sig.TypeParam t:
            {
                var flag = n?.Next() ?? 0;
                return ParamName(t, typeParams, methodParams) + Mark(flag);
            }
            case Sig.SzArray a:
            {
                var flag = n?.Next() ?? 0;
                return Display(a.Element, typeParams, methodParams, n) + "[]" + Mark(flag);
            }
            case Sig.MdArray a:
            {
                var flag = n?.Next() ?? 0;
                return Display(a.Element, typeParams, methodParams, n) + "[" + new string(',', a.Rank - 1) + "]" + Mark(flag);
            }
            case Sig.ByRef r:
                return Display(r.Element, typeParams, methodParams, n);
            case Sig.Pointer p:
                return Display(p.Element, typeParams, methodParams, n) + "*";
            default:
                return "delegate*";
        }
    }

    static string ParamName(Sig.TypeParam t, IReadOnlyList<string> typeParams, IReadOnlyList<string> methodParams)
    {
        var names = t.IsMethod ? methodParams : typeParams;
        return t.Index < names.Count ? names[t.Index] : (t.IsMethod ? "!!" : "!") + t.Index;
    }

    string NamedDisplay(Sig.Named n, IReadOnlyList<string> typeParams, IReadOnlyList<string> methodParams, Nullability? nullability)
    {
        var full = Names.Join(n.Namespace, n.Segments);
        if (n.Args.IsEmpty && Names.Keywords.TryGetValue(full, out var keyword))
        {
            return keyword;
        }

        var bare = Names.Join(n.Namespace, n.Segments.Select(Names.StripArity));
        return n.Args.IsEmpty ? bare : bare + "<" + string.Join(", ", n.Args.Select(a => Display(a, typeParams, methodParams, nullability))) + ">";
    }

    string Canonical(Sig sig, IReadOnlyList<string> typeParams, IReadOnlyList<string> methodParams) => sig switch
    {
        Sig.Prim p => Names.PrimitiveFullName(p.Code),
        Sig.Named n when n.Args.IsEmpty => Names.Join(n.Namespace, n.Segments),
        Sig.Named n => Names.Join(n.Namespace, n.Segments.Select(Names.StripArity)) + "<" + string.Join(",", n.Args.Select(a => Canonical(a, typeParams, methodParams))) + ">",
        Sig.TypeParam t => ParamName(t, typeParams, methodParams),
        Sig.SzArray a => Canonical(a.Element, typeParams, methodParams) + "[]",
        Sig.MdArray a => Canonical(a.Element, typeParams, methodParams) + "[" + new string(',', a.Rank - 1) + "]",
        Sig.ByRef r => "ref " + Canonical(r.Element, typeParams, methodParams),
        Sig.Pointer p => Canonical(p.Element, typeParams, methodParams) + "*",
        _ => "delegate*",
    };

    static string DocId(Sig sig) => sig switch
    {
        Sig.Prim p => Names.PrimitiveFullName(p.Code),
        Sig.Named n when n.Args.IsEmpty => Names.Join(n.Namespace, n.Segments),
        Sig.Named n => Names.Join(n.Namespace, n.Segments.Select(Names.StripArity)) + "{" + string.Join(",", n.Args.Select(DocId)) + "}",
        Sig.TypeParam t => (t.IsMethod ? "``" : "`") + t.Index,
        Sig.SzArray a => DocId(a.Element) + "[]",
        Sig.MdArray a => DocId(a.Element) + "[" + string.Join(",", Enumerable.Repeat("0:", a.Rank)) + "]",
        Sig.ByRef r => DocId(r.Element) + "@",
        Sig.Pointer p => DocId(p.Element) + "*",
        _ => "",
    };

    static string DefinitionId(Sig sig) => sig switch
    {
        Sig.Named n => "T:" + Names.Join(n.Namespace, n.Segments),
        Sig.Prim p when p.Code != PrimitiveTypeCode.Void => "T:" + Names.PrimitiveFullName(p.Code),
        _ => "",
    };

    static void Mentions(Sig sig, SortedSet<string> into)
    {
        switch (sig)
        {
            case Sig.Prim p when p.Code != PrimitiveTypeCode.Void:
                into.Add("T:" + Names.PrimitiveFullName(p.Code));
                break;
            case Sig.Named n:
                into.Add("T:" + Names.Join(n.Namespace, n.Segments));
                foreach (var arg in n.Args)
                {
                    Mentions(arg, into);
                }

                break;
            case Sig.SzArray a: Mentions(a.Element, into); break;
            case Sig.MdArray a: Mentions(a.Element, into); break;
            case Sig.ByRef r: Mentions(r.Element, into); break;
            case Sig.Pointer p: Mentions(p.Element, into); break;
        }
    }

    static JsonArray MentionArray(Sig sig)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        Mentions(sig, set);
        return Array(set);
    }

    // Members -----------------------------------------------------------------

    static bool VisibleMember(MethodAttributes attributes)
        => (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    static string MemberAccessibility(MethodAttributes attributes) => (attributes & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected_internal",
        _ => "public",
    };

    IEnumerable<JsonObject> MemberRecords(TypeDefinitionHandle typeHandle)
    {
        var type = md.GetTypeDefinition(typeHandle);
        var typeDoc = DocTypeName(typeHandle);
        var typeParams = TypeParameterNames(typeHandle);
        var typeCanonical = Names.Join(Names.Namespace(md, typeHandle), CanonicalSegments(typeHandle));
        var (suffix, twoWord) = Families(md.GetString(type.Name));
        var context = new MemberContext(typeHandle, "T:" + typeDoc, typeDoc, typeCanonical, typeParams, Names.Namespace(md, typeHandle), suffix, twoWord,
            DefinitionKind(type, out _) == "interface", NameWords(md.GetString(type.Name)));
        var accessors = new HashSet<MethodDefinitionHandle>();

        foreach (var handle in type.GetProperties())
        {
            var property = md.GetPropertyDefinition(handle);
            var methods = property.GetAccessors();
            accessors.Add(methods.Getter);
            accessors.Add(methods.Setter);
            if (PropertyRecord(context, handle) is { } rec)
            {
                yield return rec;
            }
        }

        foreach (var handle in type.GetEvents())
        {
            var methods = md.GetEventDefinition(handle).GetAccessors();
            accessors.Add(methods.Adder);
            accessors.Add(methods.Remover);
            accessors.Add(methods.Raiser);
            if (EventRecord(context, handle) is { } rec)
            {
                yield return rec;
            }
        }

        foreach (var handle in type.GetMethods())
        {
            if (accessors.Contains(handle))
            {
                continue;
            }

            if (MethodRecord(context, handle) is { } rec)
            {
                yield return rec;
            }
        }

        foreach (var handle in type.GetFields())
        {
            if (FieldRecord(context, handle) is { } rec)
            {
                yield return rec;
            }
        }
    }

    IEnumerable<string> CanonicalSegments(TypeDefinitionHandle handle)
    {
        var all = TypeParameterNames(handle);
        var segments = Names.Segments(md, handle);
        var consumed = 0;
        foreach (var segment in segments)
        {
            var arity = Names.ArityOf(segment);
            yield return arity == 0 ? segment : Names.StripArity(segment) + "<" + string.Join(",", all.Skip(consumed).Take(arity)) + ">";
            consumed += arity;
        }
    }

    sealed record MemberContext(TypeDefinitionHandle Type, string TypeId, string TypeDoc, string TypeCanonical, IReadOnlyList<string> TypeParams,
        string Namespace, string Suffix, string TwoWord, bool IsInterface, List<string> NameWords);

    JsonObject Base(MemberContext c, string kind, string documentationId, string canonical, string display, string name)
    {
        MemberCount++;
        return new JsonObject
        {
            ["documentation_id"] = documentationId,
            ["canonical_signature"] = canonical,
            ["display_signature"] = display,
            ["declaring_type"] = c.TypeId,
            ["namespace"] = c.Namespace,
            ["declaring_type_suffix_family"] = c.Suffix,
            ["declaring_type_two_word_suffix_family"] = c.TwoWord,
            ["declaring_type_name_words"] = Array(c.NameWords),
            ["kind"] = kind,
            ["name"] = name,
        };
    }

    void Modifiers(JsonObject rec, MethodAttributes attributes, bool isInterface, bool isExtension)
    {
        var isAbstract = (attributes & MethodAttributes.Abstract) != 0;
        var isVirtual = (attributes & MethodAttributes.Virtual) != 0;
        var isNewSlot = (attributes & MethodAttributes.NewSlot) != 0;
        var isFinal = (attributes & MethodAttributes.Final) != 0;
        rec["accessibility"] = MemberAccessibility(attributes);
        rec["is_static"] = (attributes & MethodAttributes.Static) != 0;
        rec["is_abstract"] = isAbstract && !isInterface;
        rec["is_virtual"] = isVirtual && !isAbstract && isNewSlot && !isFinal && !isInterface;
        rec["is_override"] = isVirtual && !isNewSlot;
        rec["is_sealed"] = isVirtual && !isNewSlot && isFinal;
        rec["is_extension"] = isExtension;
    }

    JsonObject? MethodRecord(MemberContext c, MethodDefinitionHandle handle)
    {
        var method = md.GetMethodDefinition(handle);
        if (!VisibleMember(method.Attributes))
        {
            return null;
        }

        var name = md.GetString(method.Name);
        if (name == ".cctor")
        {
            return null;
        }

        var isCtor = name == ".ctor";
        var methodParams = method.GetGenericParameters().Select(p => md.GetString(md.GetGenericParameter(p).Name)).ToList();
        var signature = method.DecodeSignature(_provider, null);
        var isExtension = HasAttribute(method.GetCustomAttributes(), "System.Runtime.CompilerServices.ExtensionAttribute");
        byte MethodContext() => ContextFlag(method.GetCustomAttributes(), () => TypeContext(c.Type));

        var parameterRows = method.GetParameters().Select(md.GetParameter).ToList();
        var returnRow = parameterRows.FirstOrDefault(p => p.SequenceNumber == 0);
        var returnNullability = parameterRows.Any(p => p.SequenceNumber == 0)
            ? NullableFlag(returnRow.GetCustomAttributes(), MethodContext)
            : new Nullability([MethodContext()], single: true);

        var parameters = new JsonArray();
        var outSigs = new List<Sig>();
        var displayParams = new List<string>();
        var docParams = new List<string>();
        var canonicalParams = new List<string>();
        for (var i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var sig = signature.ParameterTypes[i];
            var row = parameterRows.FirstOrDefault(p => p.SequenceNumber == i + 1);
            var hasRow = parameterRows.Any(p => p.SequenceNumber == i + 1);
            var paramName = hasRow ? md.GetString(row.Name) : "arg" + i;
            var nullability = hasRow ? NullableFlag(row.GetCustomAttributes(), MethodContext) : new Nullability([MethodContext()], single: true);
            var modifier = "";
            if (sig is Sig.ByRef)
            {
                modifier = hasRow && (row.Attributes & ParameterAttributes.Out) != 0 && (row.Attributes & ParameterAttributes.In) == 0 ? "out"
                    : hasRow && (HasAttribute(row.GetCustomAttributes(), "System.Runtime.CompilerServices.IsReadOnlyAttribute")
                        || (row.Attributes & ParameterAttributes.In) != 0) ? "in" : "ref";
            }
            else if (hasRow && (HasAttribute(row.GetCustomAttributes(), "System.ParamArrayAttribute")
                || HasAttribute(row.GetCustomAttributes(), "System.Runtime.CompilerServices.ParamCollectionAttribute")))
            {
                modifier = "params";
            }
            else if (i == 0 && isExtension)
            {
                modifier = "this";
            }

            if (modifier == "out")
            {
                outSigs.Add(sig);
            }

            var typeText = Display(sig, c.TypeParams, methodParams, nullability);
            var hasDefault = hasRow && (row.Attributes & (ParameterAttributes.HasDefault | ParameterAttributes.Optional)) != 0;
            var defaultText = hasDefault ? " = " + DefaultValue(row, sig) : "";
            // The owner's display spelling omits the extension receiver's "this"; the modifier field keeps it.
            displayParams.Add((modifier.Length > 0 && modifier != "this" ? modifier + " " : "") + typeText + " " + paramName + defaultText);
            docParams.Add(DocId(sig));
            canonicalParams.Add(Canonical(sig, c.TypeParams, methodParams));
            parameters.Add(new JsonObject
            {
                ["name"] = paramName,
                ["type"] = typeText,
                ["type_mentions"] = MentionArray(sig),
                ["modifier"] = modifier,
                ["has_default_value"] = hasDefault,
            });
        }

        var docName = isCtor ? "#ctor" : name.Replace('.', '#');
        var arity = methodParams.Count > 0 ? "``" + methodParams.Count : "";
        var conversion = name is "op_Implicit" or "op_Explicit" ? "~" + DocId(signature.ReturnType) : "";
        var documentationId = $"M:{c.TypeDoc}.{docName}{arity}" + (docParams.Count > 0 ? "(" + string.Join(",", docParams) + ")" : "") + conversion;
        var canonicalName = (isCtor ? "#ctor" : name) + (methodParams.Count > 0 ? "<" + string.Join(",", methodParams) + ">" : "");
        var canonical = $"M:{c.TypeCanonical}.{canonicalName}(" + string.Join(",", canonicalParams) + ")";
        var returnText = isCtor ? "" : Display(signature.ReturnType, c.TypeParams, methodParams, returnNullability);
        var typeDisplayName = Names.StripArity(Names.Segments(md, c.Type)[^1]);
        var display = isCtor
            ? $"{typeDisplayName}({string.Join(", ", displayParams)})"
            : $"{returnText} {name}{(methodParams.Count > 0 ? "<" + string.Join(", ", methodParams) + ">" : "")}({string.Join(", ", displayParams)})";

        var rec = Base(c, isCtor ? "constructor" : name.StartsWith("op_", StringComparison.Ordinal) && (method.Attributes & MethodAttributes.SpecialName) != 0 ? "operator" : "method",
            documentationId, canonical, display, isCtor ? "#ctor" : name);
        Modifiers(rec, method.Attributes, c.IsInterface, isExtension);
        rec["generic_parameters"] = Array(methodParams);
        rec["return_type"] = returnText;
        rec["return_type_mentions"] = isCtor ? new JsonArray() : MentionArray(signature.ReturnType);
        rec["produced_types"] = Array(isCtor ? [c.TypeId] : Produced([signature.ReturnType, .. outSigs]));
        rec["parameters"] = parameters;
        rec["overrides"] = OverriddenMethod(handle) is { } overridden ? MethodDocumentationId(overridden) : "";
        Finish(rec, documentationId, method.GetCustomAttributes(), handle, MetadataTokens.GetToken(handle));
        return rec;
    }

    string DefaultValue(Parameter row, Sig sig)
    {
        var constantHandle = row.GetDefaultValue();
        if (constantHandle.IsNil)
        {
            return "default";
        }

        var constant = md.GetConstant(constantHandle);
        var reader = md.GetBlobReader(constant.Value);
        var isValueType = sig is Sig.Named { IsValueType: true } || sig is Sig.Prim p && p.Code is not (PrimitiveTypeCode.String or PrimitiveTypeCode.Object);
        return constant.TypeCode switch
        {
            ConstantTypeCode.NullReference => isValueType && sig is not Sig.Named { Namespace: "System", Segments: ["Nullable`1"] } ? "default" : "null",
            ConstantTypeCode.Boolean => reader.ReadBoolean() ? "true" : "false",
            ConstantTypeCode.String => JsonSerializer.Serialize(reader.ReadSerializedString() ?? reader.ReadUTF16(reader.Length)),
            ConstantTypeCode.Char => "'" + reader.ReadChar() + "'",
            ConstantTypeCode.SByte => reader.ReadSByte().ToString(),
            ConstantTypeCode.Byte => reader.ReadByte().ToString(),
            ConstantTypeCode.Int16 => reader.ReadInt16().ToString(),
            ConstantTypeCode.UInt16 => reader.ReadUInt16().ToString(),
            ConstantTypeCode.Int32 => reader.ReadInt32().ToString(),
            ConstantTypeCode.UInt32 => reader.ReadUInt32().ToString(),
            ConstantTypeCode.Int64 => reader.ReadInt64().ToString(),
            ConstantTypeCode.UInt64 => reader.ReadUInt64().ToString(),
            ConstantTypeCode.Single => reader.ReadSingle().ToString("R"),
            ConstantTypeCode.Double => reader.ReadDouble().ToString("R"),
            _ => "default",
        };
    }

    JsonObject? PropertyRecord(MemberContext c, PropertyDefinitionHandle handle)
    {
        var property = md.GetPropertyDefinition(handle);
        var accessors = property.GetAccessors();
        var visible = new[] { accessors.Getter, accessors.Setter }.Where(a => !a.IsNil && VisibleMember(md.GetMethodDefinition(a).Attributes)).ToList();
        if (visible.Count == 0)
        {
            return null;
        }

        var primary = md.GetMethodDefinition(visible[0]);
        var name = md.GetString(property.Name);
        var signature = property.DecodeSignature(_provider, null);
        var nullability = NullableFlag(property.GetCustomAttributes(), () => ContextFlag(primary.GetCustomAttributes(), () => TypeContext(c.Type)));
        var typeText = Display(signature.ReturnType, c.TypeParams, [], nullability);
        var indexParams = signature.ParameterTypes;
        var accessorText = string.Join(" ", visible.Select(a => a == accessors.Getter ? "get;" : IsInit(a) ? "init;" : "set;"));
        var parameters = new JsonArray();
        var display = indexParams.IsEmpty
            ? $"{typeText} {name} {{ {accessorText} }}"
            : $"{typeText} this[{string.Join(", ", indexParams.Select((p, i) => Display(p, c.TypeParams, []) + " index" + i))}] {{ {accessorText} }}";
        foreach (var (p, i) in indexParams.Select((p, i) => (p, i)))
        {
            parameters.Add(new JsonObject
            {
                ["name"] = "index" + i,
                ["type"] = Display(p, c.TypeParams, []),
                ["type_mentions"] = MentionArray(p),
                ["modifier"] = "",
                ["has_default_value"] = false,
            });
        }

        var documentationId = $"P:{c.TypeDoc}.{name.Replace('.', '#')}" + (indexParams.IsEmpty ? "" : "(" + string.Join(",", indexParams.Select(DocId)) + ")");
        var canonical = $"P:{c.TypeCanonical}.{name}" + (indexParams.IsEmpty ? "" : "(" + string.Join(",", indexParams.Select(p => Canonical(p, c.TypeParams, []))) + ")");
        var rec = Base(c, "property", documentationId, canonical, display, name);
        Modifiers(rec, primary.Attributes, c.IsInterface, false);
        rec["accessibility"] = visible.Select(a => MemberAccessibility(md.GetMethodDefinition(a).Attributes)).Contains("public") ? "public" : rec["accessibility"]!.GetValue<string>();
        rec["generic_parameters"] = new JsonArray();
        rec["return_type"] = typeText;
        rec["return_type_mentions"] = MentionArray(signature.ReturnType);
        rec["produced_types"] = Array(Produced([signature.ReturnType]));
        rec["parameters"] = parameters;
        rec["overrides"] = PropertyOverrides(handle);
        Finish(rec, documentationId, property.GetCustomAttributes(), visible[0], MetadataTokens.GetToken(handle), visible);
        return rec;
    }

    bool IsInit(MethodDefinitionHandle setter)
    {
        return md.GetMethodDefinition(setter).DecodeSignature(new ModifierProbe(md), null).ReturnType;
    }

    JsonObject? EventRecord(MemberContext c, EventDefinitionHandle handle)
    {
        var ev = md.GetEventDefinition(handle);
        var adder = ev.GetAccessors().Adder;
        if (adder.IsNil || !VisibleMember(md.GetMethodDefinition(adder).Attributes))
        {
            return null;
        }

        var name = md.GetString(ev.Name);
        var sig = DecodeHandle(ev.Type);
        var typeText = Display(sig, c.TypeParams, [], NullableFlag(ev.GetCustomAttributes(), () => TypeContext(c.Type)));
        var documentationId = $"E:{c.TypeDoc}.{name}";
        var rec = Base(c, "event", documentationId, $"E:{c.TypeCanonical}.{name}", $"event {typeText} {name}", name);
        Modifiers(rec, md.GetMethodDefinition(adder).Attributes, c.IsInterface, false);
        rec["generic_parameters"] = new JsonArray();
        rec["return_type"] = typeText;
        rec["return_type_mentions"] = MentionArray(sig);
        rec["produced_types"] = new JsonArray();
        rec["parameters"] = new JsonArray();
        rec["overrides"] = "";
        Finish(rec, documentationId, ev.GetCustomAttributes(), adder, MetadataTokens.GetToken(handle), [adder]);
        return rec;
    }

    JsonObject? FieldRecord(MemberContext c, FieldDefinitionHandle handle)
    {
        var field = md.GetFieldDefinition(handle);
        var access = field.Attributes & FieldAttributes.FieldAccessMask;
        if (access is not (FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem) || (field.Attributes & FieldAttributes.RTSpecialName) != 0)
        {
            return null;
        }

        var name = md.GetString(field.Name);
        var sig = field.DecodeSignature(_provider, null);
        var typeText = Display(sig, c.TypeParams, [], NullableFlag(field.GetCustomAttributes(), () => TypeContext(c.Type)));
        var isConst = (field.Attributes & FieldAttributes.Literal) != 0;
        var documentationId = $"F:{c.TypeDoc}.{name}";
        var display = (isConst ? "const " : "") + $"{typeText} {name}";
        var rec = Base(c, "field", documentationId, $"F:{c.TypeCanonical}.{name}", display, name);
        rec["accessibility"] = access switch { FieldAttributes.Family => "protected", FieldAttributes.FamORAssem => "protected_internal", _ => "public" };
        rec["is_static"] = (field.Attributes & FieldAttributes.Static) != 0;
        rec["is_abstract"] = false;
        rec["is_virtual"] = false;
        rec["is_override"] = false;
        rec["is_sealed"] = false;
        rec["is_extension"] = false;
        rec["generic_parameters"] = new JsonArray();
        rec["return_type"] = typeText;
        rec["return_type_mentions"] = MentionArray(sig);
        rec["produced_types"] = Array(Produced([sig]));
        rec["parameters"] = new JsonArray();
        rec["overrides"] = "";
        Finish(rec, documentationId, field.GetCustomAttributes(), default, MetadataTokens.GetToken(handle));
        return rec;
    }

    void Finish(JsonObject rec, string documentationId, CustomAttributeHandleCollection attributes, MethodDefinitionHandle body, int token,
        IReadOnlyList<MethodDefinitionHandle>? accessors = null)
    {
        var attributeTypes = new SortedSet<string>(AttributeTypes(attributes), StringComparer.Ordinal);
        foreach (var accessor in accessors ?? [])
        {
            attributeTypes.UnionWith(AttributeTypes(md.GetMethodDefinition(accessor).GetCustomAttributes()));
        }

        rec["attribute_types"] = Array(attributeTypes);
        rec["documentation"] = Documentation(Summary(documentationId));
        rec["source"] = MemberSource(body);
        rec["findings"] = new JsonArray();
        rec["metadata_token"] = token;
    }

    JsonObject MemberSource(MethodDefinitionHandle body)
    {
        if (_pdb is null || _sourceLink.Count == 0)
        {
            return new JsonObject { ["status"] = _pdb is null ? "no_pdb" : "no_source_link", ["url"] = "", ["line"] = 0 };
        }

        if (!body.IsNil && FirstSequencePoint(body) is { } point)
        {
            Sourced++;
            return new JsonObject { ["status"] = "complete", ["url"] = point.Url, ["line"] = point.Line };
        }

        // A body the PDB does not map, such as an implicit constructor, differs from no body at all.
        var status = !body.IsNil && md.GetMethodDefinition(body).RelativeVirtualAddress != 0 ? "no_sequence_points" : "no_method_body";
        return new JsonObject { ["status"] = status, ["url"] = "", ["line"] = 0 };
    }

    Dictionary<MethodDefinitionHandle, MethodDefinitionHandle>? _moveNext;

    (string Url, int Line)? FirstSequencePoint(MethodDefinitionHandle method)
    {
        var info = _pdb!.GetMethodDebugInformation(method.ToDebugInformationHandle());
        if (info.SequencePointsBlob.IsNil)
        {
            // Async and iterator bodies keep their sequence points in the state machine.
            if (_moveNext is null)
            {
                _moveNext = [];
                foreach (var handle in _pdb.MethodDebugInformation)
                {
                    var kickoff = _pdb.GetMethodDebugInformation(handle).GetStateMachineKickoffMethod();
                    if (!kickoff.IsNil)
                    {
                        _moveNext[kickoff] = handle.ToDefinitionHandle();
                    }
                }
            }

            if (!_moveNext.TryGetValue(method, out var moveNext))
            {
                return null;
            }

            info = _pdb.GetMethodDebugInformation(moveNext.ToDebugInformationHandle());
            if (info.SequencePointsBlob.IsNil)
            {
                return null;
            }
        }

        foreach (var point in info.GetSequencePoints())
        {
            if (point.IsHidden)
            {
                continue;
            }

            var url = MapDocument(point.Document);
            return url is null ? null : (url, point.StartLine);
        }

        return null;
    }

    string? MapDocument(DocumentHandle handle)
    {
        var path = _pdb!.GetString(_pdb.GetDocument(handle).Name);
        foreach (var (prefix, target) in _sourceLink)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return target + path[prefix.Length..].Replace('\\', '/');
            }
        }

        return null;
    }

    List<string> TypeSourceUrls(TypeDefinition type)
    {
        var urls = new SortedSet<string>(StringComparer.Ordinal);
        if (_pdb is null || _sourceLink.Count == 0)
        {
            return [];
        }

        foreach (var method in type.GetMethods())
        {
            var info = _pdb.GetMethodDebugInformation(method.ToDebugInformationHandle());
            if (info.SequencePointsBlob.IsNil)
            {
                continue;
            }

            foreach (var point in info.GetSequencePoints())
            {
                if (!point.IsHidden && MapDocument(point.Document) is { } url)
                {
                    urls.Add(url);
                }
            }
        }

        return [.. urls];
    }
}

// Detects the IsExternalInit modreq that marks an init accessor.
sealed class ModifierProbe(MetadataReader md) : ISignatureTypeProvider<bool, object?>
{
    public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => modifier || unmodifiedType;
    public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => md.GetString(md.GetTypeReference(handle).Name) == "IsExternalInit";
    public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => md.GetString(md.GetTypeDefinition(handle).Name) == "IsExternalInit";
    public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
    public bool GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => false;
    public bool GetSZArrayType(bool elementType) => false;
    public bool GetArrayType(bool elementType, ArrayShape shape) => false;
    public bool GetByReferenceType(bool elementType) => false;
    public bool GetPointerType(bool elementType) => false;
    public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments) => false;
    public bool GetGenericTypeParameter(object? genericContext, int index) => false;
    public bool GetGenericMethodParameter(object? genericContext, int index) => false;
    public bool GetFunctionPointerType(MethodSignature<bool> signature) => false;
    public bool GetPinnedType(bool elementType) => false;
}

sealed class Nullability(byte[] bytes, bool single)
{
    int _position;

    public byte Next() => single ? bytes[0] : _position < bytes.Length ? bytes[_position++] : (byte)0;
}
