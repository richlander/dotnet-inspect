using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// String and char literal escaping: control characters, quotes, and
/// backslashes are escaped so the rendered literal always compiles. A raw
/// newline or tab in the output would be invalid C#.
/// </summary>
public class StringEscapingTests
{
    static string PrintStringConstant(string value)
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var block = new Block(0);
        block.Add(new Return(new Constant(value, stringType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(stringType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    [Fact]
    public void ControlCharacters_QuotesAndBackslashes_AreEscaped()
    {
        Assert.Equal("return \"a\\nb\";", PrintStringConstant("a\nb"));
        Assert.Equal("return \"\\t\\r\\0\";", PrintStringConstant("\t\r\0"));
        Assert.Equal("return \"say \\\"hi\\\"\";", PrintStringConstant("say \"hi\""));
        Assert.Equal("return \"c:\\\\tmp\";", PrintStringConstant("c:\\tmp"));
    }

    [Fact]
    public void OtherControlChar_UsesUnicodeEscape()
    {
        //  has no recognized short escape.
        Assert.Equal("return \"x\\u0001y\";", PrintStringConstant("xy"));
    }
}
