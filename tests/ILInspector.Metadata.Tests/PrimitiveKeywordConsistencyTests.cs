using System.Reflection.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class PrimitiveKeywordConsistencyTests
{
    // The byte-identical table formerly duplicated as a private s_keywords in
    // ILInspector.Analysis.TypeRef and ILInspector.Decompiler.Pipeline.TypeRef now
    // resolves through PrimitiveTypeNames. This pins that the shared simple-name
    // lookup reproduces every former entry exactly, so collapsing the copies onto
    // the single source of truth is behavior-preserving.
    public static TheoryData<string, string> SystemTypeKeywords => new()
    {
        { "Boolean", "bool" }, { "Byte", "byte" }, { "SByte", "sbyte" },
        { "Char", "char" }, { "Int16", "short" }, { "UInt16", "ushort" },
        { "Int32", "int" }, { "UInt32", "uint" }, { "Int64", "long" },
        { "UInt64", "ulong" }, { "Single", "float" }, { "Double", "double" },
        { "Decimal", "decimal" },
        { "IntPtr", "nint" }, { "UIntPtr", "nuint" }, { "String", "string" },
        { "Object", "object" }, { "Void", "void" },
    };

    [Theory]
    [MemberData(nameof(SystemTypeKeywords))]
    public void TryToKeywordForSystemType_ReproducesFormerTypeRefTable(string systemName, string keyword)
    {
        Assert.True(PrimitiveTypeNames.TryToKeywordForSystemType(systemName, out var actual));
        Assert.Equal(keyword, actual);
    }

    [Fact]
    public void TryToKeywordForSystemType_RejectsNonPrimitiveSystemType()
        => Assert.False(PrimitiveTypeNames.TryToKeywordForSystemType("DateTime", out _));

    // SignatureDecoder lives in the bottom-layer MetadataPrimitives project and
    // spells primitives from a PrimitiveTypeCode-keyed SRM provider switch, so it
    // cannot reference PrimitiveTypeNames (a layer above). This binds its keyword
    // spelling to the shared alias table so the two independent producers cannot
    // silently disagree.
    public static TheoryData<PrimitiveTypeCode, string> PrimitiveCodeFullNames => new()
    {
        { PrimitiveTypeCode.Boolean, "System.Boolean" },
        { PrimitiveTypeCode.Char, "System.Char" },
        { PrimitiveTypeCode.SByte, "System.SByte" },
        { PrimitiveTypeCode.Byte, "System.Byte" },
        { PrimitiveTypeCode.Int16, "System.Int16" },
        { PrimitiveTypeCode.UInt16, "System.UInt16" },
        { PrimitiveTypeCode.Int32, "System.Int32" },
        { PrimitiveTypeCode.UInt32, "System.UInt32" },
        { PrimitiveTypeCode.Int64, "System.Int64" },
        { PrimitiveTypeCode.UInt64, "System.UInt64" },
        { PrimitiveTypeCode.Single, "System.Single" },
        { PrimitiveTypeCode.Double, "System.Double" },
        { PrimitiveTypeCode.String, "System.String" },
        { PrimitiveTypeCode.Object, "System.Object" },
        { PrimitiveTypeCode.IntPtr, "System.IntPtr" },
        { PrimitiveTypeCode.UIntPtr, "System.UIntPtr" },
        { PrimitiveTypeCode.Void, "System.Void" },
    };

    [Theory]
    [MemberData(nameof(PrimitiveCodeFullNames))]
    public void SignatureDecoderKeyword_MatchesPrimitiveTypeNames(PrimitiveTypeCode code, string fullName)
    {
        var decoded = SignatureDecoder.Instance.GetPrimitiveType(code);
        Assert.True(PrimitiveTypeNames.TryToKeyword(fullName, out var keyword));
        Assert.Equal(keyword, decoded);
    }
}
