using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Builds a small assembly for method-row gate fixtures: types, methods with
/// hand-written signature blobs and no bodies, custom attributes, TypeRefs,
/// and TypeSpecs, so a test controls every structure the gate walks.
/// </summary>
internal sealed class GateFixtureImage
{
    readonly MetadataBuilder _metadata = new();
    readonly List<FixtureType> _types = [];
    readonly AssemblyReferenceHandle _dependency;
    readonly ModuleReferenceHandle _native;

    public GateFixtureImage()
    {
        _metadata.AddModule(
            0,
            _metadata.GetOrAddString("GateFixture.dll"),
            _metadata.GetOrAddGuid(new Guid("0b5f1f3c-6a0e-4ab2-9c0f-4d1c2c1b7a11")),
            default,
            default);
        _metadata.AddAssembly(
            _metadata.GetOrAddString("GateFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        _dependency = _metadata.AddAssemblyReference(
            _metadata.GetOrAddString("Dependency"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        _native = _metadata.AddModuleReference(_metadata.GetOrAddString("native.dll"));
        Type("", "<Module>");
    }

    public MetadataBuilder Metadata => _metadata;

    /// <summary>A TypeRef in the dependency assembly, or nested in <paramref name="enclosing"/>.</summary>
    public TypeReferenceHandle TypeRef(string ns, string name, TypeReferenceHandle? enclosing = null) =>
        _metadata.AddTypeReference(
            enclosing is { } outer ? outer : _dependency,
            _metadata.GetOrAddString(ns),
            _metadata.GetOrAddString(name));

    public TypeSpecificationHandle TypeSpec(BlobBuilder signature) =>
        _metadata.AddTypeSpecification(_metadata.GetOrAddBlob(signature));

    /// <summary>A parameterless attribute constructor on <paramref name="parent"/>.</summary>
    public MemberReferenceHandle AttributeConstructor(EntityHandle parent)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, r => r.Void(), _ => { });
        return _metadata.AddMemberReference(
            parent,
            _metadata.GetOrAddString(".ctor"),
            _metadata.GetOrAddBlob(signature));
    }

    /// <summary>Starts a type; methods added next belong to it.</summary>
    public FixtureType Type(string ns, string name, FixtureType? enclosing = null)
    {
        var type = new FixtureType(ns, name, enclosing);
        _types.Add(type);
        return type;
    }

    public ImmutableArray<byte> Build()
    {
        int nextMethod = 1;
        var typeHandles = new Dictionary<FixtureType, TypeDefinitionHandle>();
        foreach (FixtureType type in _types)
        {
            TypeDefinitionHandle handle = _metadata.AddTypeDefinition(
                type.Name == "<Module>"
                    ? default
                    : type.Enclosing is null
                        ? TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed
                        : TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
                _metadata.GetOrAddString(type.Namespace),
                _metadata.GetOrAddString(type.Name),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(nextMethod));
            typeHandles[type] = handle;
            if (type.Enclosing is { } enclosing)
                _metadata.AddNestedType(handle, typeHandles[enclosing]);

            foreach (FixtureMethod method in type.Methods)
            {
                MethodDefinitionHandle methodHandle = _metadata.AddMethodDefinition(
                    method.Attributes,
                    method.ImplAttributes,
                    _metadata.GetOrAddString(method.Name),
                    _metadata.GetOrAddBlob(method.Signature),
                    -1,
                    default);
                nextMethod++;
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    _metadata.AddMethodImport(
                        methodHandle,
                        MethodImportAttributes.None,
                        _metadata.GetOrAddString(method.Name),
                        _native);
                }

                foreach (EntityHandle constructor in method.AttributeConstructors)
                    _metadata.AddCustomAttribute(methodHandle, constructor, default);
            }
        }

        var root = new MetadataRootBuilder(_metadata);
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            root,
            new BlobBuilder());
        var output = new BlobBuilder();
        pe.Serialize(output);
        return [.. output.ToArray()];
    }

    public static BlobBuilder StaticSignature(Action<SignatureTypeEncoder> returnType, params Action<SignatureTypeEncoder>[] parameters)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .MethodSignature()
            .Parameters(
                parameters.Length,
                r => returnType(r.Type()),
                p =>
                {
                    foreach (Action<SignatureTypeEncoder> parameter in parameters)
                        parameter(p.AddParameter().Type());
                });
        return blob;
    }

    public static BlobBuilder VoidSignature(params Action<SignatureTypeEncoder>[] parameters)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .MethodSignature()
            .Parameters(
                parameters.Length,
                r => r.Void(),
                p =>
                {
                    foreach (Action<SignatureTypeEncoder> parameter in parameters)
                        parameter(p.AddParameter().Type());
                });
        return blob;
    }

    internal sealed class FixtureType(string ns, string name, FixtureType? enclosing)
    {
        public string Namespace { get; } = ns;
        public string Name { get; } = name;
        public FixtureType? Enclosing { get; } = enclosing;
        public List<FixtureMethod> Methods { get; } = [];

        public FixtureType Method(
            string name,
            BlobBuilder? signature = null,
            MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes implAttributes = MethodImplAttributes.IL,
            params EntityHandle[] attributeConstructors)
        {
            Methods.Add(new FixtureMethod(
                name,
                signature ?? VoidSignature(),
                attributes,
                implAttributes,
                attributeConstructors));
            return this;
        }
    }

    internal sealed record FixtureMethod(
        string Name,
        BlobBuilder Signature,
        MethodAttributes Attributes,
        MethodImplAttributes ImplAttributes,
        EntityHandle[] AttributeConstructors);
}
