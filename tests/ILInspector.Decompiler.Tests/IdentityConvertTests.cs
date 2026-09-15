using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

public class IdentityConvertTests
{
    [Fact]
    public void ArrayLengthConversion_IsElided()
    {
        // ldlen yields the int-typed ArrayLength; the trailing conv.i4 is an
        // identity conversion and must not print as a cast.
        var intType = TypeRef.CoreLib("System", "Int32");
        var arrayType = TypeRef.SzArray(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var length = new ArrayLength(new LoadArgument(0, "a", arrayType));
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: false, isUnsigned: false, length)));
        var signature = new MethodSignature(intType, [new Parameter("a", arrayType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return a.Length;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void GenuineNarrowing_IsKept()
    {
        // conv.i4 of a long is a real narrowing — the cast stays.
        var intType = TypeRef.CoreLib("System", "Int32");
        var longType = TypeRef.CoreLib("System", "Int64");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: false, isUnsigned: false, new LoadArgument(0, "x", longType))));
        var signature = new MethodSignature(intType, [new Parameter("x", longType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (int)x;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void CheckedUnsignedConversion_AtEqualType_IsKept()
    {
        // conv.ovf.i4.un of an int is Int32 -> Int32, but it reinterprets the
        // source as unsigned and throws for negative bit patterns — eliding it
        // would drop the overflow check. The cast must survive equal types.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: true, isUnsigned: true, new LoadArgument(0, "x", intType))));
        var signature = new MethodSignature(intType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return checked((int)(uint)x);", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }
}
