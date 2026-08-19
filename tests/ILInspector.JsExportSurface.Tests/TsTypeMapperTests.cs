using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="TsTypeMapper"/>: all TS-specific "personality" (Task/ValueTask unwrap to
/// Promise, array/nullable mapping, C# primitive to TS primitive, record-name passthrough) lives
/// here per the repo's architecture decision that the OM stays C#-faithful.
/// </summary>
public sealed class TsTypeMapperTests
{
    private static readonly HashSet<string> RecordNames = new(StringComparer.Ordinal) { "WidgetDto" };

    [Theory]
    [InlineData("string", "string")]
    [InlineData("System.String", "string")]
    [InlineData("bool", "boolean")]
    [InlineData("System.Boolean", "boolean")]
    [InlineData("int", "number")]
    [InlineData("System.Int32", "number")]
    [InlineData("long", "number")]
    [InlineData("double", "number")]
    [InlineData("void", "void")]
    public void MapReturnType_MapsCSharpPrimitivesToTsPrimitives(string csharpType, string expected)
    {
        Assert.Equal(expected, TsTypeMapper.MapReturnType(csharpType, RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsGenericTaskToPromise()
    {
        Assert.Equal("Promise<string>", TsTypeMapper.MapReturnType("Task<string>", RecordNames));
        Assert.Equal(
            "Promise<string>",
            TsTypeMapper.MapReturnType("System.Threading.Tasks.Task<string>", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsGenericValueTaskToPromise()
    {
        Assert.Equal("Promise<number>", TsTypeMapper.MapReturnType("ValueTask<int>", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsNonGenericTaskToPromiseVoid()
    {
        Assert.Equal("Promise<void>", TsTypeMapper.MapReturnType("Task", RecordNames));
        Assert.Equal(
            "Promise<void>",
            TsTypeMapper.MapReturnType("System.Threading.Tasks.Task", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsNonGenericValueTaskToPromiseVoid()
    {
        Assert.Equal("Promise<void>", TsTypeMapper.MapReturnType("ValueTask", RecordNames));
    }

    [Fact]
    public void Map_ArrayTypeMapsToTsArraySyntax()
    {
        Assert.Equal("number[]", TsTypeMapper.MapParameterType("int[]", RecordNames));
    }

    [Fact]
    public void Map_NullableTypeMapsToUnionWithNull()
    {
        Assert.Equal("WidgetDto | null", TsTypeMapper.MapParameterType("WidgetDto?", RecordNames));
    }

    [Fact]
    public void Map_KnownRecordNamePassesThroughByName()
    {
        Assert.Equal("WidgetDto", TsTypeMapper.MapParameterType("WidgetDto", RecordNames));
        Assert.Equal(
            "WidgetDto",
            TsTypeMapper.MapParameterType("ILInspector.JsExportSurface.Fixtures.WidgetDto", RecordNames));
    }

    [Fact]
    public void Map_UnknownTypeMapsToUnknown()
    {
        Assert.Equal("unknown", TsTypeMapper.MapParameterType("SomeUnmappedType", RecordNames));
    }
}
