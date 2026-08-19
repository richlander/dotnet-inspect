using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CSharpText;
using DotnetInspector.Inspectors;
using DotnetInspector.Services;
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
        { "op_Equality", true, true, 0, "operator", true },
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
            // C# operators cannot return by reference.
            Define("op_LeftShift", Operator, [builder, typeof(int)], builder.MakeByRefType());
            // C# operators permit in parameters, but not ref parameters.
            Define("op_UnaryNegation", Operator, [builder.MakeByRefType()], builder);
            // A conversion cannot convert the declaring type to itself (CS0555).
            Define("op_CheckedExplicit", Operator, [builder], builder);
            // Increment/decrement must return the operand type or a derived type.
            Define("op_Increment", Operator, [builder], typeof(int));
            Define("op_Decrement", Operator, [builder], builder);
            // C# permits comparison operators to return any type; only
            // true/false operators require bool.
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
        Assert.False(image.IsCSharpOperatorDeclaration("op_LeftShift"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_UnaryNegation"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_CheckedExplicit"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_Increment"));
        Assert.True(image.IsCSharpOperatorDeclaration("op_Decrement"));
        Assert.True(image.IsCSharpOperatorDeclaration("op_Equality"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_True"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_Explicit"));

        // Every one of them is still an operator in metadata: the C# proof
        // narrows rendering and raising, never metadata classification.
        foreach (string name in new[]
        {
            "op_Addition", "op_Subtraction", "op_Multiply", "op_Division",
            "op_Modulus", "op_BitwiseAnd", "op_BitwiseOr", "op_LeftShift",
            "op_UnaryNegation", "op_CheckedExplicit", "op_Increment",
            "op_Decrement", "op_Equality", "op_True", "op_Implicit", "op_Explicit",
        })
        {
            Assert.Equal("operator", Assert.Single(image.Members, m => m.Name == name).Kind);
        }
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsParamArrayParameter()
    {
        using var image = OperatorImage.Build(builder =>
        {
            var method = builder.DefineMethod(
                "op_Addition",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName,
                builder,
                [builder, typeof(int[])]);
            method.DefineParameter(1, ParameterAttributes.None, "left");
            method.DefineParameter(2, ParameterAttributes.None, "values")
                .SetCustomAttribute(
                    new CustomAttributeBuilder(
                        typeof(ParamArrayAttribute)
                            .GetConstructor(Type.EmptyTypes)!,
                        []));
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        });

        Assert.False(
            image.IsCSharpOperatorDeclaration("op_Addition"));
        Assert.False(
            Assert.Single(
                image.Members,
                member => member.Name == "op_Addition")
                .CSharpOperatorDeclaration);
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsContradictoryValueTypeEncoding()
    {
        using var wellFormed =
            OperatorImage.BuildObjectParameterEncoding(
                (byte)SignatureTypeKind.Class);
        using var malformed =
            OperatorImage.BuildObjectParameterEncoding(
                (byte)SignatureTypeKind.ValueType);

        Assert.True(
            wellFormed.IsCSharpOperatorDeclaration("op_Addition"));
        Assert.False(
            malformed.IsCSharpOperatorDeclaration("op_Addition"));
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
    public void CSharpOperatorDeclaration_RequiresExactDeclaringTypeInstantiation()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var parameter = Assert.Single(builder.DefineGenericParameters("T"));
                var self = builder.MakeGenericType(parameter);
                var closed = builder.MakeGenericType(typeof(int));
                var method = builder.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    self,
                    [closed, closed]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            },
            "Container`1");

        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RequiresExactSelfConstraint()
    {
        using var image = OperatorImage.BuildModule(
            module =>
            {
                var other = module.DefineType(
                    "Other",
                    TypeAttributes.Public
                        | TypeAttributes.Sealed
                        | TypeAttributes.SequentialLayout,
                    typeof(ValueType));
                var contract = module.DefineType(
                    "IContract`1",
                    TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
                var parameter = Assert.Single(contract.DefineGenericParameters("T"));
                parameter.SetInterfaceConstraints(contract.MakeGenericType(other));
                contract.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public
                        | MethodAttributes.Static
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual
                        | MethodAttributes.SpecialName,
                    parameter,
                    [parameter, parameter]);
                other.CreateType();
                contract.CreateType();
            },
            "IContract`1");

        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsNullableLookalike()
    {
        using var image = OperatorImage.BuildModule(
            module =>
            {
                var nullable = module.DefineType(
                    "System.Nullable`1",
                    TypeAttributes.Public | TypeAttributes.Class);
                nullable.DefineGenericParameters("T");
                var declaring = module.DefineType(
                    "NullableConsumer",
                    TypeAttributes.Public | TypeAttributes.Class);
                var wrapped = nullable.MakeGenericType(declaring);
                var method = declaring.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    declaring,
                    [wrapped, wrapped]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
                nullable.CreateType();
                declaring.CreateType();
            },
            "NullableConsumer");

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
    [InlineData("System.String", "op_Equality")]
    [InlineData("System.Nullable`1", "op_Implicit")]
    [InlineData("System.Nullable`1", "op_Explicit")]
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

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsPrimitiveEncodedReferenceAssemblyOperators()
    {
        var referenceAssemblies = PlatformResolver.GetAllPacksDirectories()
            .Select(root => Path.Combine(root, "Microsoft.NETCore.App.Ref"))
            .Where(Directory.Exists)
            .SelectMany(pack => Directory.EnumerateFiles(
                pack,
                "System.Runtime.dll",
                SearchOption.AllDirectories))
            .OrderDescending()
            .ToList();
        Assert.NotEmpty(referenceAssemblies);

        using var stream = File.OpenRead(referenceAssemblies[0]);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = Assert.Single(
            reader.TypeDefinitions,
            handle => FullName(reader, reader.GetTypeDefinition(handle)) == "System.String");
        var method = Assert.Single(
            reader.GetTypeDefinition(typeHandle).GetMethods()
                .Select(reader.GetMethodDefinition),
            candidate => reader.GetString(candidate.Name) == "op_Equality");

        Assert.True(OperatorMetadata.IsMetadataOperator(reader, method));
        Assert.True(OperatorMetadata.IsCSharpOperatorDeclaration(reader, method));

        var member = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types
                .Single(type => type.FullName == "System.String")
                .Members,
            candidate => candidate.Name == "op_Equality");
        Assert.Equal(true, member.CSharpOperatorDeclaration);
    }

    [Theory]
    [InlineData("op_Equality")]
    [InlineData("op_Inequality")]
    [InlineData("op_LessThan")]
    [InlineData("op_GreaterThan")]
    [InlineData("op_LessThanOrEqual")]
    [InlineData("op_GreaterThanOrEqual")]
    public void CSharpOperatorDeclaration_AcceptsNonBooleanComparisonOperators(
        string operatorName)
    {
        using var stream = File.OpenRead(typeof(System.Data.SqlTypes.SqlInt32).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = Assert.Single(
            reader.TypeDefinitions,
            handle => FullName(reader, reader.GetTypeDefinition(handle))
                == "System.Data.SqlTypes.SqlInt32");
        var method = Assert.Single(
            reader.GetTypeDefinition(typeHandle).GetMethods()
                .Select(reader.GetMethodDefinition),
            candidate => reader.GetString(candidate.Name) == operatorName);

        Assert.True(OperatorMetadata.IsCSharpOperatorDeclaration(reader, method));
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsDerivedIncrementReturn()
    {
        using var stream = File.OpenRead(typeof(DerivedIncrementBase).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = Assert.Single(
            reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                == nameof(DerivedIncrementBase));
        var method = Assert.Single(
            reader.GetTypeDefinition(typeHandle).GetMethods()
                .Select(reader.GetMethodDefinition),
            candidate => reader.GetString(candidate.Name) == "op_Increment");

        Assert.True(OperatorMetadata.IsCSharpOperatorDeclaration(reader, method));
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsInParameters()
    {
        using var stream = File.OpenRead(typeof(InParameterOperator).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = Assert.Single(
            reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                == nameof(InParameterOperator));
        var method = Assert.Single(
            reader.GetTypeDefinition(typeHandle).GetMethods()
                .Select(reader.GetMethodDefinition),
            candidate => reader.GetString(candidate.Name) == "op_Addition");

        Assert.True(OperatorMetadata.IsCSharpOperatorDeclaration(reader, method));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsOtherInFlaggedByRefParameters()
    {
        using var image = OperatorImage.Build(builder =>
        {
            void Define(string name, Type? modifierAttribute)
            {
                var method = builder.DefineMethod(
                    name,
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    builder,
                    [builder.MakeByRefType()]);
                var parameter = method.DefineParameter(1, ParameterAttributes.In, "value");
                if (modifierAttribute is not null)
                {
                    parameter.SetCustomAttribute(
                        modifierAttribute.GetConstructor(Type.EmptyTypes)!,
                        [1, 0, 0, 0]);
                }
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }

            Define("op_UnaryNegation", modifierAttribute: null);
            Define("op_UnaryPlus", typeof(RequiresLocationAttribute));
        });

        Assert.False(image.IsCSharpOperatorDeclaration("op_UnaryNegation"));
        Assert.False(image.IsCSharpOperatorDeclaration("op_UnaryPlus"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsNullableSelfConversion()
    {
        using var image = OperatorImage.BuildModule(
            module =>
            {
                var valueType = module.DefineType(
                    "OperatorSurface",
                    TypeAttributes.Public
                        | TypeAttributes.Sealed
                        | TypeAttributes.SequentialLayout,
                    typeof(ValueType));
                var method = valueType.DefineMethod(
                    "op_Implicit",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    typeof(Nullable<>).MakeGenericType(valueType),
                    [valueType]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
                valueType.CreateType();
            },
            "OperatorSurface");

        Assert.False(image.IsCSharpOperatorDeclaration("op_Implicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsBaseDerivedConversion()
    {
        using var image = OperatorImage.BuildModule(
            module =>
            {
                var baseType = module.DefineType(
                    "ConversionBase",
                    TypeAttributes.Public | TypeAttributes.Class);
                var derivedType = module.DefineType(
                    "ConversionDerived",
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);
                var method = baseType.DefineMethod(
                    "op_Implicit",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    baseType,
                    [derivedType]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
                baseType.CreateType();
                derivedType.CreateType();
            },
            "ConversionBase");

        Assert.False(image.IsCSharpOperatorDeclaration("op_Implicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsInterfaceConversion()
    {
        using var image = OperatorImage.BuildModule(
            module =>
            {
                var contract = module.DefineType(
                    "IContract",
                    TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
                var source = module.DefineType(
                    "OperatorSurface",
                    TypeAttributes.Public | TypeAttributes.Class);
                var method = source.DefineMethod(
                    "op_Explicit",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    contract,
                    [source]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
                contract.CreateType();
                source.CreateType();
            },
            "OperatorSurface");

        Assert.False(image.IsCSharpOperatorDeclaration("op_Explicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsUnresolvedExternalInterfaceConversion()
    {
        using var image = OperatorImage.Build(builder =>
        {
            var method = builder.DefineMethod(
                "op_Explicit",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                typeof(IDisposable),
                [builder]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        });

        Assert.False(image.IsCSharpOperatorDeclaration("op_Explicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsUnresolvedTransitiveBaseConversion()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var method = builder.DefineMethod(
                    "op_Implicit",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    typeof(Stream),
                    [builder]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            },
            parent: typeof(MemoryStream));

        Assert.False(image.IsCSharpOperatorDeclaration("op_Implicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsExternalConversionEndpoints()
    {
        using var image = OperatorImage.Build(builder =>
        {
            void Define(string name, Type parameterType)
            {
                var method = builder.DefineMethod(
                    name,
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    builder,
                    [parameterType]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }

            Define("op_Implicit", typeof(decimal));
            Define("op_Explicit", typeof(string));
        });

        Assert.True(image.IsCSharpOperatorDeclaration("op_Implicit"));
        Assert.True(image.IsCSharpOperatorDeclaration("op_Explicit"));
    }

    [Fact]
    public void ResolutionAwareSurface_AcceptsExternalReferenceConversions()
    {
        using var image = OperatorImage.Build(builder =>
        {
            void Define(
                string name,
                Type returnType,
                Type parameterType)
            {
                var method = builder.DefineMethod(
                    name,
                    MethodAttributes.Public
                        | MethodAttributes.Static
                        | MethodAttributes.SpecialName,
                    returnType,
                    [parameterType]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }

            Define(
                "op_Implicit",
                typeof(System.Text.StringBuilder),
                builder);
            Define(
                "op_Explicit",
                builder,
                typeof(System.Collections.Generic.List<int>));
        });
        using var session = new TypeDefinitionResolutionSession(
            image.AssemblyPath,
            isPlatformAssembly: false);

        ApiSurface? extracted =
            session.ExtractApiSurface(includeAll: true);
        Assert.NotNull(extracted);
        ApiSurface surface = extracted;
        var operators = Assert.Single(
            surface.Types,
            type => type.FullName == "OperatorSurface")
            .Members
            .Where(member => member.Kind == "operator")
            .ToArray();

        Assert.Equal(2, operators.Length);
        Assert.All(
            operators,
            member => Assert.True(
                member.CSharpOperatorDeclaration,
                member.Signature));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsObjectConversions()
    {
        using var toObject = OperatorImage.Build(builder =>
        {
            var method = builder.DefineMethod(
                "op_Explicit",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                typeof(object),
                [builder]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        });
        using var fromObject = OperatorImage.Build(builder =>
        {
            var method = builder.DefineMethod(
                "op_Explicit",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                builder,
                [typeof(object)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        });

        Assert.False(toObject.IsCSharpOperatorDeclaration("op_Explicit"));
        Assert.False(fromObject.IsCSharpOperatorDeclaration("op_Explicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsConversionToDeclaringTypeParameter()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var parameter = Assert.Single(builder.DefineGenericParameters("T"));
                var self = builder.MakeGenericType(parameter);
                var method = builder.DefineMethod(
                    "op_Implicit",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    parameter,
                    [self]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            },
            "Wrapper`1");

        Assert.True(image.IsCSharpOperatorDeclaration("op_Implicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsArrayAndPointerConversions()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var parameter = Assert.Single(builder.DefineGenericParameters("T"));
                var self = builder.MakeGenericType(parameter);
                var arrayConversion = builder.DefineMethod(
                    "op_Implicit",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    self,
                    [parameter.MakeArrayType()]);
                var arrayIl = arrayConversion.GetILGenerator();
                arrayIl.Emit(OpCodes.Ldnull);
                arrayIl.Emit(OpCodes.Ret);

                var pointerConversion = builder.DefineMethod(
                    "op_Explicit",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    self,
                    [typeof(void).MakePointerType()]);
                var pointerIl = pointerConversion.GetILGenerator();
                pointerIl.Emit(OpCodes.Ldnull);
                pointerIl.Emit(OpCodes.Ret);
            },
            "Buffer`1");

        Assert.True(image.IsCSharpOperatorDeclaration("op_Implicit"));
        Assert.True(image.IsCSharpOperatorDeclaration("op_Explicit"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsVarArgSignature()
    {
        using var image = OperatorImage.Build(builder =>
        {
            var method = builder.DefineMethod(
                "op_Addition",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                CallingConventions.VarArgs,
                builder,
                [builder, builder]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        });

        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsCompilerEmittedCrossAssemblyConversions()
    {
        Type[] declaringTypes =
        [
            typeof(Span<>),
            typeof(ReadOnlySpan<>),
            typeof(System.Numerics.BigInteger),
            typeof(System.Xml.Linq.XElement),
        ];

        foreach (var declaringType in declaringTypes)
        {
            using var stream = File.OpenRead(declaringType.Assembly.Location);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var typeHandle = Assert.Single(
                reader.TypeDefinitions,
                handle => FullName(reader, reader.GetTypeDefinition(handle))
                    == declaringType.FullName);
            var conversions = reader.GetTypeDefinition(typeHandle).GetMethods()
                .Select(reader.GetMethodDefinition)
                .Where(method => OperatorNames.IsConversionOperatorMethodName(
                    reader.GetString(method.Name)))
                .ToList();

            Assert.NotEmpty(conversions);
            foreach (var conversion in conversions)
            {
                Assert.True(
                    OperatorMetadata.IsCSharpOperatorDeclaration(reader, conversion),
                    $"{declaringType.FullName}.{reader.GetString(conversion.Name)}");
            }
        }
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsStaticDeclaringType()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var method = builder.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    builder,
                    [builder, builder]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            },
            "OperatorSurface",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);

        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsEnumDeclaringType()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                builder.DefineField(
                    "value__",
                    typeof(int),
                    FieldAttributes.Public
                        | FieldAttributes.SpecialName
                        | FieldAttributes.RTSpecialName);
                var method = builder.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    builder,
                    [builder, builder]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ret);
            },
            "OperatorEnum",
            TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(Enum));

        var member = Assert.Single(image.Members, candidate => candidate.Name == "op_Addition");
        Assert.Equal("operator", member.Kind);
        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
        Assert.False(member.CSharpOperatorDeclaration);
    }

    [Fact]
    public void CSharpOperatorDeclaration_RejectsDelegateDeclaringType()
    {
        using var image = OperatorImage.Build(
            builder =>
            {
                var constructor = builder.DefineConstructor(
                    MethodAttributes.Public
                        | MethodAttributes.HideBySig
                        | MethodAttributes.SpecialName
                        | MethodAttributes.RTSpecialName,
                    CallingConventions.Standard,
                    [typeof(object), typeof(IntPtr)]);
                constructor.SetImplementationFlags(
                    MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
                var invoke = builder.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public
                        | MethodAttributes.HideBySig
                        | MethodAttributes.NewSlot
                        | MethodAttributes.Virtual,
                    typeof(int),
                    [typeof(int)]);
                invoke.SetImplementationFlags(
                    MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
                var method = builder.DefineMethod(
                    "op_Addition",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                    builder,
                    [builder, builder]);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ret);
            },
            "OperatorDelegate",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(MulticastDelegate));

        var member = Assert.Single(image.Members, candidate => candidate.Name == "op_Addition");
        Assert.Equal("operator", member.Kind);
        Assert.False(image.IsCSharpOperatorDeclaration("op_Addition"));
        Assert.False(member.CSharpOperatorDeclaration);
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsMulticastDelegateOperators()
    {
        using var stream = File.OpenRead(typeof(MulticastDelegate).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = Assert.Single(
            reader.TypeDefinitions,
            handle => FullName(reader, reader.GetTypeDefinition(handle))
                == typeof(MulticastDelegate).FullName);
        var operators = reader.GetTypeDefinition(typeHandle).GetMethods()
            .Select(reader.GetMethodDefinition)
            .Where(method => reader.GetString(method.Name) is "op_Equality" or "op_Inequality")
            .ToList();

        Assert.NotEmpty(operators);
        Assert.All(
            operators,
            method => Assert.True(
                OperatorMetadata.IsCSharpOperatorDeclaration(reader, method),
                reader.GetString(method.Name)));
    }

    [Fact]
    public void CSharpOperatorDeclaration_AcceptsNullableSelfConstrainedOperands()
    {
        using var stream = File.OpenRead(typeof(NullableSelfConstrainedOperator<>).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = Assert.Single(
            reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                == "NullableSelfConstrainedOperator`1");
        var method = Assert.Single(
            reader.GetTypeDefinition(typeHandle).GetMethods()
                .Select(reader.GetMethodDefinition),
            candidate => reader.GetString(candidate.Name) == "op_Addition");

        Assert.True(OperatorMetadata.IsCSharpOperatorDeclaration(reader, method));
    }

    static string FullName(MetadataReader reader, TypeDefinition type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    public interface NullableSelfConstrainedOperator<T>
        where T : struct, NullableSelfConstrainedOperator<T>
    {
        static abstract T? operator +(T? left, T? right);
    }

    public class DerivedIncrementBase
    {
        public static DerivedIncrementResult operator ++(DerivedIncrementBase value) => new();
    }

    public sealed class DerivedIncrementResult : DerivedIncrementBase;

    public readonly struct InParameterOperator
    {
        public static InParameterOperator operator +(
            in InParameterOperator left,
            InParameterOperator right)
            => left;
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
        public string AssemblyPath => _path;

        public static OperatorImage Build(
            Action<TypeBuilder> define,
            string typeName = TypeName,
            TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Class,
            Type? parent = null)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"operator-surface-{Guid.NewGuid():N}.dll");
            var assembly = new PersistedAssemblyBuilder(
                new AssemblyName("OperatorSurface"),
                typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("OperatorSurface");
            var type = module.DefineType(typeName, attributes, parent);
            define(type);
            type.CreateType();
            assembly.Save(path);
            return new OperatorImage(path, typeName);
        }

        public static OperatorImage BuildModule(
            Action<ModuleBuilder> define,
            string targetTypeName)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"operator-surface-{Guid.NewGuid():N}.dll");
            var assembly = new PersistedAssemblyBuilder(
                new AssemblyName("OperatorSurface"),
                typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("OperatorSurface");
            define(module);
            assembly.Save(path);
            return new OperatorImage(path, targetTypeName);
        }

        public static OperatorImage BuildObjectParameterEncoding(
            byte objectKind)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"operator-surface-{Guid.NewGuid():N}.dll");
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("OperatorSurface.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("OperatorSurface"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            var runtime = metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                new byte[]
                {
                    0xb0, 0x3f, 0x5f, 0x7f,
                    0x11, 0xd5, 0x0a, 0x3a,
                }),
                default,
                default);
            var objectType = metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
            metadata.AddTypeDefinition(
                default,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(TypeName),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

            var bodies = new BlobBuilder();
            var bodyEncoder = new MethodBodyStreamEncoder(bodies);
            var code = new BlobBuilder();
            var instructions =
                new InstructionEncoder(
                    code,
                    new ControlFlowBuilder());
            instructions.OpCode(ILOpCode.Ldnull);
            instructions.OpCode(ILOpCode.Ret);
            int bodyOffset =
                bodyEncoder.AddMethodBody(
                    instructions,
                    maxStack: 1);
            byte[] signature =
            [
                0x00,
                0x02,
                (byte)SignatureTypeKind.Class,
                0x08,
                (byte)SignatureTypeKind.Class,
                0x08,
                objectKind,
                0x05,
            ];
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("op_Addition"),
                metadata.GetOrAddBlob(signature),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));

            var builder = new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                bodies,
                flags: CorFlags.ILOnly);
            var image = new BlobBuilder();
            builder.Serialize(image);
            File.WriteAllBytes(path, image.ToArray());
            return new OperatorImage(path, TypeName);
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
