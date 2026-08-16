using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// The metadata operator vocabulary and the C# source operator vocabulary are
/// different questions, and API classification answers the first one. A
/// CLI-only operator (<c>op_LogicalAnd</c>, <c>op_AddressOf</c>, …) is still an
/// operator in metadata even though C# cannot declare it; an ordinary method
/// that merely starts with <c>op_</c> is not.
/// </summary>
public sealed class OperatorApiSurfaceTests
{
    // name, isSpecialName, isStatic, genericArity, expectedKind, expectedCSharpDeclaration
    public static TheoryData<string, bool, bool, int, string, bool?> Classification => new()
    {
        // C# operator names.
        { "op_Addition", true, true, 0, "operator", true },
        { "op_Equality", true, true, 0, "operator", false },
        { "op_CheckedAddition", true, true, 0, "operator", true },
        // ECMA-335 I.10.3 names C# has no declaration syntax for: still
        // operators in metadata, never renderable as C# operator declarations.
        { "op_AddressOf", true, true, 0, "operator", false },
        { "op_PointerDereference", true, true, 0, "operator", false },
        { "op_LogicalAnd", true, true, 0, "operator", false },
        { "op_LogicalOr", true, true, 0, "operator", false },
        { "op_Assign", true, true, 0, "operator", false },
        { "op_SignedRightShift", true, true, 0, "operator", false },
        { "op_Comma", true, true, 0, "operator", false },
        { "op_MemberSelection", true, true, 0, "operator", false },
        // Recognized by the operator convention; C# cannot declare it.
        { "op_CheckedImplicit", true, true, 0, "operator", false },
        // Malformed / unknown op_ names are ordinary methods.
        { "op_Custom", true, true, 0, "method", null },
        { "op_SomeFutureOp", true, true, 0, "method", null },
        { "op_CheckedModulusAssignment", true, true, 0, "method", null },
        // SpecialName is required, and so is zero generic arity.
        { "op_Addition", false, true, 0, "method", null },
        { "op_LogicalAnd", false, true, 0, "method", null },
        { "op_Addition", true, true, 1, "method", null },
        // A non-static binary operator is metadata-classified but not a C#
        // declaration (only the C# 14 instance compound-assignment form is).
        { "op_Addition", true, false, 0, "operator", false },
    };

    [Theory]
    [MemberData(nameof(Classification))]
    public void Extract_ClassifiesByTheMetadataOperatorVocabulary(
        string name,
        bool isSpecialName,
        bool isStatic,
        int genericArity,
        string expectedKind,
        bool? expectedCSharpDeclaration)
    {
        using var image = OperatorImage.Build(builder =>
        {
            var attributes = MethodAttributes.Public
                | (isSpecialName ? MethodAttributes.SpecialName : 0)
                | (isStatic ? MethodAttributes.Static : 0);
            // The declaring type participates in every operand, so a C#
            // declaration rejection here is about the name, the SpecialName
            // flag, the arity, or the static-ness — never participation.
            var method = builder.DefineMethod(
                name,
                attributes,
                builder,
                isStatic ? [builder, builder] : [builder]);
            if (genericArity > 0)
                method.DefineGenericParameters("T");
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        });

        var member = Assert.Single(image.Members, candidate => candidate.Name == name);
        Assert.Equal(expectedKind, member.Kind);
        Assert.Equal(expectedCSharpDeclaration, member.CSharpOperatorDeclaration);
        Assert.StartsWith(
            expectedKind == "operator" ? $"operator:{name}~" : $"{name}~",
            image.Anchor(name).StableSelector,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The C#-declaration proof rejects metadata no C# compiler could have
    /// produced, so declaration rendering and reconstruction never spell it as
    /// operator syntax. Each negative differs from a positive in exactly one
    /// obligation.
    /// </summary>
    [Fact]
    public void CSharpOperatorDeclaration_RequiresTheFullSourceShape()
    {
        const MethodAttributes Operator =
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName;
        using var image = OperatorImage.Build(builder =>
        {
            void Define(string name, MethodAttributes attributes, Type[] parameters, Type returnType)
            {
                var method = builder.DefineMethod(name, attributes, returnType, parameters);
                var il = method.GetILGenerator();
                if (returnType != typeof(void))
                    il.Emit(returnType == typeof(int) || returnType == typeof(long)
                        ? OpCodes.Ldc_I4_0
                        : OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }

            Define("op_Addition", Operator, [builder, builder], builder);
            Define("op_Subtraction", Operator, [builder, typeof(int)], builder);
            // Neither operand is the declaring type (CS0563).
            Define("op_Multiply", Operator, [typeof(int), typeof(int)], typeof(int));
            // Not public: C# requires operators to be public and static.
            Define(
                "op_Division",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName,
                [builder, builder],
                builder);
            // Instance, and not the C# 14 compound-assignment form.
            Define(
                "op_Modulus",
                MethodAttributes.Public | MethodAttributes.SpecialName,
                [builder],
                builder);
            // Void return (CS0590).
            Define("op_BitwiseAnd", Operator, [builder, builder], typeof(void));
            // Wrong arity for a binary operator.
            Define("op_BitwiseOr", Operator, [builder], builder);
            // Equality and true/false operators must return bool.
            Define("op_Equality", Operator, [builder, builder], builder);
            Define("op_True", Operator, [builder], builder);
            // Conversion participating through the return type only, which C# allows.
            Define("op_Implicit", Operator, [typeof(int)], builder);
            // Conversion touching the declaring type nowhere (CS0556).
            Define("op_Explicit", Operator, [typeof(int)], typeof(long));
        });

        Assert.True(image.IsCSharpOperatorDeclaration("op_Addition"));
        Assert.True(image.IsCSharpOperatorDeclaration("op_Subtraction"));
        Assert.True(image.IsCSharpOperatorDeclaration("op_Implicit"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_Multiply"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_Division"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_Modulus"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_BitwiseAnd"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_BitwiseOr"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_Equality"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_True"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_Explicit"));

        // Every one of them is still an operator in metadata: the C# proof
        // narrows rendering and raising, never metadata classification.
        foreach (string name in new[]
        {
            "op_Addition", "op_Subtraction", "op_Multiply", "op_Division",
            "op_Modulus", "op_BitwiseAnd", "op_BitwiseOr", "op_Equality",
            "op_True", "op_Implicit", "op_Explicit",
        })
        {
            Assert.Equal("operator", Assert.Single(image.Members, m => m.Name == name).Kind);
        }
    }

    [Fact]
    public void CSharpOperatorDeclaration_RequiresExactDeclaringTypeIdentity()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var method = builder.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    builder,
                    [typeof(int), typeof(int)]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            },
            "System.Int32");

        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_InterfaceTypeParameterRequiresSelfConstraint()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var self = Assert.Single(builder.DefineGenericParameters("TSelf"));
                builder.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public
                        | MethodAttributes.Static
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual
                        | MethodAttributes.SpecialName,
                    self,
                    [self, self]);
            },
            "IOperators`1",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);

        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
    }

    /// <summary>
    /// Compiler-emitted operators must stay positive under the same proof —
    /// including the generic-math interface shape, whose operands are the
    /// interface's own type parameters rather than the interface itself.
    /// </summary>
    [Theory]
    [InlineData("System.Decimal", "op_Addition")]
    [InlineData("System.Decimal", "op_Equality")]
    [InlineData("System.Decimal", "op_Implicit")]
    [InlineData("System.Decimal", "op_Explicit")]
    [InlineData("System.Int128", "op_CheckedExplicit")]
    [InlineData("System.Int128", "op_CheckedAddition")]
    [InlineData("System.Decimal", "op_UnaryNegation")]
    [InlineData("System.Decimal", "op_Increment")]
    [InlineData("System.TimeSpan", "op_LessThan")]
    [InlineData("System.Numerics.IAdditionOperators`3", "op_Addition")]
    [InlineData("System.Numerics.IComparisonOperators`3", "op_GreaterThan")]
    public void CSharpOperatorDeclaration_AcceptsCompilerEmittedOperators(
        string typeName,
        string operatorName)
    {
        using var stream = File.OpenRead(typeof(decimal).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = Assert.Single(
            reader.TypeDefinitions,
            handle => FullName(reader, reader.GetTypeDefinition(handle)) == typeName);

        var matches = reader.GetTypeDefinition(typeHandle).GetMethods()
            .Select(reader.GetMethodDefinition)
            .Where(method => reader.GetString(method.Name) == operatorName)
            .ToList();

        Assert.NotEmpty(matches);
        foreach (var method in matches)
        {
            Assert.True(
                OperatorMetadata.IsMetadataOperator(reader, method),
                $"{typeName}.{operatorName} should classify as a metadata operator.");
            Assert.True(
                OperatorMetadata.IsCSharpOperatorDeclaration(reader, method),
                $"{typeName}.{operatorName} should classify as a C# operator declaration.");
        }
    }

    static string FullName(MetadataReader reader, TypeDefinition type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    sealed class OperatorImage : IDisposable
    {
        const string TypeName = "OperatorSurface";

        readonly string _path;
        readonly FileStream _stream;
        readonly PEReader _peReader;
        readonly MetadataReader _reader;
        readonly TypeDefinitionHandle _typeHandle;

        OperatorImage(string path, string typeName)
        {
            _path = path;
            _stream = File.OpenRead(path);
            _peReader = new PEReader(_stream);
            _reader = _peReader.GetMetadataReader();
            _typeHandle = _reader.TypeDefinitions.Single(
                handle => FullName(_reader, _reader.GetTypeDefinition(handle)) == typeName);
            Members = ApiSurfaceExtractor.Extract(_peReader, includeAll: true).Types
                .Single(candidate => candidate.FullName == typeName)
                .Members;
        }

        public List<ApiMember> Members { get; }

        public static OperatorImage Build(
            Action<TypeBuilder> define,
            string typeName = TypeName,
            TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Class)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"operator-surface-{Guid.NewGuid():N}.dll");
            var assembly = new PersistedAssemblyBuilder(
                new AssemblyName("OperatorSurface"),
                typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("OperatorSurface");
            var type = module.DefineType(typeName, attributes);
            define(type);
            type.CreateType();
            assembly.Save(path);
            return new OperatorImage(path, typeName);
        }

        public MemberAnchor Anchor(string methodName)
            => ApiMemberIdentity.CreateMethodAnchor(_reader, _typeHandle, Method(methodName));

        public bool IsCSharpOperatorDeclaration(string methodName)
            => OperatorMetadata.IsCSharpOperatorDeclaration(_reader, Method(methodName));

        MethodDefinition Method(string methodName)
            => Assert.Single(
                _reader.GetTypeDefinition(_typeHandle).GetMethods()
                    .Select(_reader.GetMethodDefinition),
                method => _reader.GetString(method.Name) == methodName);

        public void Dispose()
        {
            _peReader.Dispose();
            _stream.Dispose();
            File.Delete(_path);
        }
    }
}
