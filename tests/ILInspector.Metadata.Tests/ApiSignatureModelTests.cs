using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class ApiSignatureModelTests
{
    static readonly ApiSurface Surface;

    static ApiSignatureModelTests()
    {
        using var stream = File.OpenRead(typeof(ApiSignatureModelTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    [Theory]
    [InlineData(nameof(ReadOnlyMethodFixtures.ReadOnly), true)]
    [InlineData(nameof(ReadOnlyMethodFixtures.Mutable), false)]
    public void MethodSignatureModel_RetainsPhysicalReadOnlyAttribute(
        string methodName,
        bool isReadOnly)
    {
        var member = GetMember(nameof(ReadOnlyMethodFixtures), methodName);
        Assert.Equal(isReadOnly, member.IsReadOnly);
    }

    [Fact]
    public void MethodSignatureModel_ExposesReturnTypeParametersAndDefaults()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefKinds));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal("string", member.SignatureModel.ReturnType);
        Assert.Equal("MethodWithRefKinds", member.SignatureModel.MemberName);
        Assert.Equal(5, member.SignatureModel.ParameterCount);
        Assert.Equal("(ref int, out string, in long, int, params byte[])", member.SignatureModel.ParameterTypesSummary);
        Assert.Equal("value", member.SignatureModel.Parameters[0].Name);
        Assert.Equal("ref", member.SignatureModel.Parameters[0].Modifier);
        Assert.Equal("int", member.SignatureModel.Parameters[0].Type);
        Assert.True(member.SignatureModel.Parameters[3].HasDefault);
        Assert.Equal("1", member.SignatureModel.Parameters[3].DefaultValueText);
        Assert.Null(member.SignatureModel.Parameters[0].StructuralType);
        Assert.Null(member.SignatureModel.Parameters[2].StructuralType);
        Assert.Null(member.SignatureModel.Parameters[3].StructuralType);
    }

    [Fact]
    public void MethodSignatureModel_ExposesStructuralTypeForFunctionPointerParameter()
    {
        var member = GetMember(
            nameof(FunctionPointerShapeFixture),
            nameof(FunctionPointerShapeFixture.Ret));

        Assert.NotNull(member.SignatureModel);
        string? structuralType = Assert.Single(member.SignatureModel.Parameters).StructuralType;
        Assert.StartsWith("delegate*", structuralType, StringComparison.Ordinal);
        Assert.Contains(";I0;E0;G0;R1", structuralType, StringComparison.Ordinal);
        Assert.StartsWith("delegate*", member.SignatureModel.StructuralReturnType, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodSignatureModel_UsesPositionalGenericsUnderRefReadonlyAndFunctionPointerPayload()
    {
        var genericReturn = GetMember(
            nameof(StructuralGenericPayloadFixtures),
            nameof(StructuralGenericPayloadFixtures.GenericRefReadonly));
        Assert.NotNull(genericReturn.SignatureModel);
        Assert.Contains(
            "M0@",
            genericReturn.SignatureModel.StructuralReturnType,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "{T@",
            genericReturn.SignatureModel.StructuralReturnType,
            StringComparison.Ordinal);

        var nested = GetMember(
            nameof(StructuralGenericPayloadFixtures),
            nameof(StructuralGenericPayloadFixtures.Nested));
        var flat = GetMember(
            nameof(StructuralGenericPayloadFixtures),
            nameof(StructuralGenericPayloadFixtures.Flat));
        Assert.Contains(
            ".Inner{",
            nested.SignatureModel?.StructuralReturnType,
            StringComparison.Ordinal);
        Assert.NotEqual(
            nested.SignatureModel?.StructuralReturnType,
            flat.SignatureModel?.StructuralReturnType);

        var fnptr = GetMember(
            nameof(StructuralGenericPayloadFixtures),
            nameof(StructuralGenericPayloadFixtures.FnptrOfList));
        string? structuralType = Assert.Single(fnptr.SignatureModel!.Parameters).StructuralType;
        Assert.Contains("List{M0}", structuralType, StringComparison.Ordinal);
        Assert.DoesNotContain("List{T}", structuralType, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodSignatureModel_OmitsStructuralTypeForGenericParameters()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.GenericMethod));

        Assert.NotNull(member.SignatureModel);
        Assert.NotEmpty(member.SignatureModel.TypeParameters);
        Assert.Equal("T", Assert.Single(member.SignatureModel.Parameters).Type);
        Assert.Null(Assert.Single(member.SignatureModel.Parameters).StructuralType);
        Assert.Null(member.SignatureModel.StructuralReturnType);
    }

    [Fact]
    public void PropertySignatureModel_ExposesReturnTypeIndexerParametersAndPublicAccessors()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), "Item");

        Assert.NotNull(member.SignatureModel);
        Assert.Equal("string", member.SignatureModel.ReturnType);
        Assert.Equal("this[]", member.SignatureModel.MemberName);
        Assert.Equal("(int)", member.SignatureModel.ParameterTypesSummary);
        Assert.Equal("get", member.SignatureModel.PublicAccessorsSummary);
        Assert.All(
            member.SignatureModel.Accessors,
            accessor => Assert.False(accessor.IsExplicitInterfaceImplementation));
        Assert.Null(
            member.SignatureModel.Accessors.Single(accessor => accessor.Kind == "set")
                .StructuralReturnType);
    }

    [Fact]
    public void PropertySignatureModel_ExposesInitSetterStructuralReturn()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.InitValue));

        Assert.NotNull(member.SignatureModel);
        ApiAccessor setter = Assert.Single(
            member.SignatureModel.Accessors,
            accessor => accessor.Kind == "set");
        Assert.Equal(
            StructuralTypeIdentity.Modified(
                required: true,
                "System.Runtime.CompilerServices.IsExternalInit",
                "System.Void"),
            setter.StructuralReturnType);
        Assert.Null(
            member.SignatureModel.Accessors.Single(accessor => accessor.Kind == "get")
                .StructuralReturnType);
    }

    [Fact]
    public void PropertySignatureModel_ExposesExplicitInterfaceAccessorMethodName()
    {
        ApiType type = GetType(nameof(ExplicitAccessorFixtures));
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Kind == "property"
                && candidate.Name.EndsWith(
                    $".{nameof(IExplicitAccessor.Value)}",
                    StringComparison.Ordinal));

        Assert.NotNull(member.SignatureModel);
        ApiAccessor getter = Assert.Single(
            member.SignatureModel.Accessors,
            accessor => accessor.Kind == "get");
        Assert.False(string.IsNullOrEmpty(getter.Name));
        Assert.True(getter.IsExplicitInterfaceImplementation);
        Assert.False(getter.Name.StartsWith("get_", StringComparison.Ordinal));
        Assert.EndsWith(
            $".get_{nameof(IExplicitAccessor.Value)}",
            getter.Name,
            StringComparison.Ordinal);

        ApiMember accessor = Assert.Single(
            ApiMemberAccessors.Create(member, type));
        ApiMember physical = Assert.Single(
            type.Members,
            candidate => candidate.MetadataToken == accessor.MetadataToken);
        Assert.Equal(getter.Name, accessor.Name);
        Assert.Equal(
            "explicit-interface-implementation",
            accessor.Kind);
        Assert.Equal(
            ApiMemberIdentity.GetMemberAnchor(type, physical),
            ApiMemberIdentity.GetMemberAnchor(type, accessor));
    }

    [Theory]
    [InlineData("readValue", true, "explicit-interface-implementation")]
    [InlineData("I.get_Value", false, "method")]
    [InlineData("I.get_Value", null, "explicit-interface-implementation")]
    [InlineData("get_Value", null, "method")]
    public void AccessorProjection_UsesKnownClassificationBeforeName(
        string name,
        bool? isExplicitImplementation,
        string expectedKind)
    {
        var type = new ApiType { Name = "C", Namespace = "Example" };
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            GetterToken = 0x06000001,
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "get",
                        Name = name,
                        IsExplicitInterfaceImplementation = isExplicitImplementation,
                    },
                ],
            },
        };

        ApiMember projected = Assert.Single(ApiMemberAccessors.Create(property, type));
        Assert.Equal(name, projected.Name);
        Assert.Equal(expectedKind, projected.Kind);
    }

    [Fact]
    public void FieldAndEventSignatureModels_ExposeReturnType()
    {
        var field = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.Count));
        var evt = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.Changed));

        Assert.Equal("int", field.SignatureModel?.ReturnType);
        Assert.Equal("System.EventHandler?", evt.SignatureModel?.ReturnType);
    }

    [Fact]
    public void ConstructorSignatureModel_ExposesParameterFacts()
    {
        var ctor = GetType(nameof(ApiSignatureFixtures)).Members
            .Where(member => member.Kind == "constructor")
            .Single(member => member.SignatureModel?.ParameterCount == 2);

        Assert.NotNull(ctor.SignatureModel);
        Assert.Equal(".ctor", ctor.SignatureModel.MemberName);
        Assert.Equal(2, ctor.SignatureModel.ParameterCount);
        Assert.Equal("(string, int)", ctor.SignatureModel.ParameterTypesSummary);
        Assert.Equal("name", ctor.SignatureModel.Parameters[0].Name);
        Assert.Equal("string", ctor.SignatureModel.Parameters[0].Type);
        Assert.False(ctor.SignatureModel.Parameters[0].HasDefault);
        Assert.Equal("count", ctor.SignatureModel.Parameters[1].Name);
        Assert.Equal("int", ctor.SignatureModel.Parameters[1].Type);
        Assert.True(ctor.SignatureModel.Parameters[1].HasDefault);
        Assert.Equal("1", ctor.SignatureModel.Parameters[1].DefaultValueText);
        Assert.Equal(
            [("name", "string", false), ("count", "int", true)],
            ctor.SignatureModel.ParameterInfoSummary);
    }

    [Fact]
    public void CanonicalSignature_UsesStructuredSignatureModel()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefKinds));
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = "BROKEN",
            SignatureModel = source.SignatureModel
        };

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.MethodWithRefKinds(ref int,out string,in long,int,params byte[])",
            canonical);

        var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
        Assert.Equal(canonical, anchor.Format(MemberAnchorFormat.CanonicalSignature));
        Assert.Equal(anchor.StableSelector, anchor.Format(MemberAnchorFormat.StableSelector));
        Assert.Equal(anchor.Fingerprint, anchor.Format(MemberAnchorFormat.Fingerprint));
        Assert.Equal($"{anchor.TypeFullName}.{anchor.StableSelector}", anchor.Format(MemberAnchorFormat.Qualified));
    }

    [Fact]
    public void CanonicalSignature_UsesGenericMethodNameFromStructuredModel()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.GenericMethod));
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = "BROKEN",
            SignatureModel = source.SignatureModel
        };

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.GenericMethod<T>(T)",
            canonical);
        Assert.Equal("T", source.SignatureModel!.TypeParameters.Single().Name);
    }

    [Fact]
    public void MethodSignatureModel_ExposesGenericParameterConstraints()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.ConstrainedGenericMethod));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal("ConstrainedGenericMethod<TSource, TResult>", member.SignatureModel.MemberName);
        Assert.Equal("TResult", member.SignatureModel.ReturnType);
        Assert.Equal(2, member.SignatureModel.TypeParameters.Count);
        Assert.Equal("TSource", member.SignatureModel.TypeParameters[0].Name);
        Assert.Equal(["unmanaged"], member.SignatureModel.TypeParameters[0].Constraints);
        Assert.Equal("TResult", member.SignatureModel.TypeParameters[1].Name);
        Assert.Equal(["System.IComparable<TResult>", "new()"], member.SignatureModel.TypeParameters[1].Constraints);
    }

    [Fact]
    public void MethodSignatureModel_ExposesSourceRenderableParameterAttributes()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterMarker(\"id\", Order = 2)"],
            member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_RendersParameterAttributeTypeAndEscapedStringArguments()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithTypeParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterTypeMarker(typeof(System.Int32), Text = \"System.String\\\\Path\\nNext\")"],
            member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_RendersNestedTypeParameterAttributeArguments()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithNestedTypeParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterTypeMarker(typeof(ILInspector.Metadata.Tests.ApiSignatureFixtures.Nested))"],
            member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_SkipsUnspellableGenericTypeParameterAttributeArguments()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithGenericTypeParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Empty(member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_SuppressesCompilerReservedRefReadonlyParameterAttributes()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefReadonlyParameter));

        Assert.NotNull(member.SignatureModel);
        Assert.Empty(member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_EscapesCharArguments()
    {
        var quote = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithQuoteCharAttribute));
        var backslash = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithBackslashCharAttribute));
        var newline = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithNewlineCharAttribute));

        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterCharMarker('\\'')"],
            quote.SignatureModel?.Parameters[0].Attributes);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterCharMarker('\\\\')"],
            backslash.SignatureModel?.Parameters[0].Attributes);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterCharMarker('\\n')"],
            newline.SignatureModel?.Parameters[0].Attributes);
    }

    [Fact]
    public void CanonicalSignature_NormalizesMultiGenericMethodNameWhitespace()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.PairGenericMethod));
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = "BROKEN",
            SignatureModel = source.SignatureModel
        };

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.PairGenericMethod<TLeft,TRight>(TLeft,TRight)",
            canonical);
    }

    [Fact]
    public void CanonicalSignature_PreservesLegacyGenericNameSubstringDigestContract()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.Validate));

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, source, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.Validate()",
            canonical);
    }

    [Fact]
    public void XmlDocIdentity_UsesStructuredMethodParameters()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefKinds));

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, source, out var identity));

        Assert.Equal("M:ILInspector.Metadata.Tests.ApiSignatureFixtures.MethodWithRefKinds", identity.LookupKey);
        Assert.Equal(
            ["System.Int32@", "System.String@", "System.Int64@", "System.Int32", "System.Byte[]"],
            identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_MapsGenericTypeAndMethodParameters()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.PairGenericMethod));

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, source, out var identity));

        Assert.Equal("M:ILInspector.Metadata.Tests.ApiSignatureFixtures.PairGenericMethod", identity.LookupKey);
        Assert.Equal(["M0", "M1"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_UsesIndexerParametersForProperties()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), "Item");

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, source, out var identity));

        Assert.Equal("P:ILInspector.Metadata.Tests.ApiSignatureFixtures.Item", identity.LookupKey);
        Assert.Equal(["System.Int32"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_UsesHashForExplicitInterfaceMembers()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Impl" };
        var member = new ApiMember
        {
            Name = "IFoo.Bar",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature { MemberName = "IFoo.Bar" }
        };

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity));

        Assert.Equal("M:Samples.Impl.IFoo#Bar", identity.LookupKey);
    }

    [Fact]
    public void XmlDocIdentity_UsesBracesForGenericExplicitInterfaceMembers()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Impl" };
        var member = new ApiMember
        {
            Name = "System.IEquatable<System.String>.Equals",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                MemberName = "System.IEquatable<System.String>.Equals",
                Parameters = [new ApiParameter { Name = "other", Type = "System.String" }]
            }
        };

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity));

        Assert.Equal("M:Samples.Impl.System#IEquatable{System#String}#Equals", identity.LookupKey);
        Assert.Equal(["System.String"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_IncludesConversionOperatorReturnType()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Number" };
        var member = new ApiMember
        {
            Name = "op_Implicit",
            Kind = "operator",
            SignatureModel = new ApiSignature
            {
                MemberName = "op_Implicit",
                ReturnType = "int",
                Parameters = [new ApiParameter { Name = "value", Type = "Samples.Number" }]
            }
        };

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity));

        Assert.Equal("System.Int32", identity.NormalizedReturnType);
        Assert.Equal(["Samples.Number"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_FallsBackWhenSignatureModelIsMissing()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), "Item");
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = source.Signature
        };

        Assert.False(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out _));
    }

    static ApiType GetType(string typeName)
        => Surface.Types.First(type => type.Name == typeName);

    static ApiMember GetMember(string typeName, string memberName)
        => GetType(typeName)
            .Members
            .First(member => member.Name == memberName);
}

public sealed class ApiSignatureFixtures
{
    public int Count;

    public event EventHandler? Changed;

    public ApiSignatureFixtures()
    {
    }

    public ApiSignatureFixtures(string name, int count = 1)
    {
        Count = name.Length + count;
    }

    public string MethodWithRefKinds(ref int value, out string text, in long source, int count = 1, params byte[] bytes)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        text = count.ToString();
        return text;
    }

    public T GenericMethod<T>(T value) => value;

    public (TLeft Left, TRight Right) PairGenericMethod<TLeft, TRight>(TLeft left, TRight right)
        => (left, right);

    public TResult ConstrainedGenericMethod<TSource, TResult>(TSource source)
        where TSource : unmanaged
        where TResult : IComparable<TResult>, new()
        => new();

    public void Validate<TValidateOptions>()
    {
    }

    public void MethodWithParameterAttribute([ParameterMarker("id", Order = 2)] string id)
    {
    }

    public void MethodWithTypeParameterAttribute([ParameterTypeMarker(typeof(int), Text = "System.String\\Path\nNext")] string value)
    {
    }

    public void MethodWithNestedTypeParameterAttribute([ParameterTypeMarker(typeof(Nested))] string value)
    {
    }

    public void MethodWithGenericTypeParameterAttribute([ParameterTypeMarker(typeof(List<int>))] string value)
    {
    }

    public void MethodWithRefReadonlyParameter(ref readonly int value)
    {
    }

    public void MethodWithQuoteCharAttribute([ParameterCharMarker('\'')] string value)
    {
    }

    public void MethodWithBackslashCharAttribute([ParameterCharMarker('\\')] string value)
    {
    }

    public void MethodWithNewlineCharAttribute([ParameterCharMarker('\n')] string value)
    {
    }

    public sealed class Nested
    {
    }

    public string this[int index]
    {
        get => index.ToString();
        private set { }
    }

    public int InitValue { get; init; }
}

public struct ReadOnlyMethodFixtures
{
    public readonly int ReadOnly() => 42;
    public int Mutable() => 42;
}

public interface IExplicitAccessor
{
    int Value { get; }
}

public sealed class ExplicitAccessorFixtures : IExplicitAccessor
{
    int IExplicitAccessor.Value => 1;
}

public sealed class StructuralGenericPayloadFixtures
{
    public ref readonly T GenericRefReadonly<T>()
        => throw new InvalidOperationException();

    public ref readonly Outer<int>.Inner<string> Nested()
        => throw new InvalidOperationException();

    public ref readonly Flat<int, string> Flat()
        => throw new InvalidOperationException();

    public unsafe void FnptrOfList<T>(delegate*<List<T>, void> callback)
    {
    }
}

public class Outer<TOuter>
{
    public class Inner<TInner>
    {
    }
}

public class Flat<T1, T2>
{
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterMarkerAttribute : Attribute
{
    public ParameterMarkerAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public int Order { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterTypeMarkerAttribute : Attribute
{
    public ParameterTypeMarkerAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }
    public string? Text { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterCharMarkerAttribute : Attribute
{
    public ParameterCharMarkerAttribute(char value)
    {
        Value = value;
    }

    public char Value { get; }
}
