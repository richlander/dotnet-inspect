using CSharpText;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public unsafe class MetadataMemberSignatureShapeTests
{
    [Theory]
    [InlineData(
        nameof(ShapeSpecimens.Primitive),
        "void Primitive(int value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Nullable),
        "void Nullable(int? value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.GenericNullable),
        "void GenericNullable<T>(T? value) where T : struct;",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Arrays),
        "void Arrays(int[][,] first, int[,][] second);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Tuple),
        "void Tuple((int left, string right) value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Tuple8),
        "void Tuple8((int, int, int, int, int, int, int, int) value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Pointer),
        "unsafe void Pointer(int* value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.FunctionPointer),
        "unsafe void FunctionPointer(delegate* unmanaged[Cdecl]<int, string> callback);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.ByRefFunctionPointer),
        "unsafe void ByRefFunctionPointer(delegate*<ref int, void> callback);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        "op_Implicit",
        "public static implicit operator int(global::ILInspector.Metadata.Tests.ShapeSpecimens value) => 0;",
        SourceMemberSignatureKind.ConversionOperator)]
    public void SourceAndMetadataAdapters_ProduceTheSameShape(
        string methodName,
        string declaration,
        SourceMemberSignatureKind kind)
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(reader, nameof(ShapeSpecimens), methodName);

        MemberSignatureShapeResult metadata =
            MetadataMemberSignatureShape.Create(reader, handle);
        MemberSignatureShapeResult source =
            SourceMemberSignatureShape.Create(declaration, kind);

        Assert.True(metadata.IsAvailable, metadata.UnavailableReason);
        Assert.True(source.IsAvailable, source.UnavailableReason);
        Assert.True(
            source.Shape == metadata.Shape,
            $"source={MemberSignatureShapeCodec.Encode(source.Shape!)}{Environment.NewLine}"
            + $"metadata={MemberSignatureShapeCodec.Encode(metadata.Shape!)}");
    }

    [Fact]
    public void NestedTypeAndMethodGenericParameters_UseCumulativePositions()
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(reader, "Inner`1", "Pair");

        MemberSignatureShapeResult metadata =
            MetadataMemberSignatureShape.Create(reader, handle);
        MemberSignatureShapeResult source =
            SourceMemberSignatureShape.Create(
                "void Pair<V>(T outer, U inner, V method);",
                SourceMemberSignatureKind.Method,
                ["T", "U"]);

        Assert.True(metadata.IsAvailable, metadata.UnavailableReason);
        Assert.True(source.IsAvailable, source.UnavailableReason);
        Assert.Equal(source.Shape, metadata.Shape);
    }

    [Theory]
    [InlineData(nameof(ShapeSpecimens.LegacyNamed), "`0(IReadOnlyList<string>)", true)]
    [InlineData(nameof(ShapeSpecimens.LegacyNamed), "`0(List<string>)", false)]
    [InlineData(nameof(ShapeSpecimens.LegacyGeneric), "`1(T)", true)]
    public void LegacyCompatibility_ValidatesAnAlreadyIdentifiedMethodOnly(
        string methodName,
        string legacyText,
        bool expected)
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(reader, nameof(ShapeSpecimens), methodName);
        MemberSignatureShapeResult legacy = MemberSignatureShapeCodec.Decode(legacyText);

        Assert.True(legacy.IsAvailable, legacy.UnavailableReason);
        Assert.Equal(
            expected,
            MetadataMemberSignatureShape.LegacyShapeCanDescribe(
                reader,
                handle,
                legacy.Shape!));
    }

    [Fact]
    public void MetadataAdapter_RefusesShapeBeyondTransportDepthLimit()
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(
            reader,
            nameof(ShapeSpecimens),
            nameof(ShapeSpecimens.DeepArray));

        MemberSignatureShapeResult result =
            MetadataMemberSignatureShape.Create(reader, handle);

        Assert.False(result.IsAvailable);
        Assert.Contains("transport safety limits", result.UnavailableReason);
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName)
    {
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return methodHandle;
            }
        }
        throw new Xunit.Sdk.XunitException($"Method '{typeName}.{methodName}' was not found.");
    }
}

public unsafe class ShapeSpecimens
{
    public void Primitive(int value) { }
    public void Nullable(int? value) { }
    public void GenericNullable<T>(T? value) where T : struct { }
    public void Arrays(int[][,] first, int[,][] second) { }
    public void Tuple((int left, string right) value) { }
    public void Tuple8((int, int, int, int, int, int, int, int) value) { }
    public void Pointer(int* value) { }
    public void FunctionPointer(delegate* unmanaged[Cdecl]<int, string> callback) { }
    public void ByRefFunctionPointer(delegate*<ref int, void> callback) { }
    public void LegacyNamed(IReadOnlyList<string> values) { }
    public void LegacyGeneric<T>(T value) { }
    public void DeepArray(
        int[][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [] value) { }
    public static implicit operator int(ShapeSpecimens value) => 0;

    public class Outer<T>
    {
        public class Inner<U>
        {
            public void Pair<V>(T outer, U inner, V method) { }
        }
    }
}
