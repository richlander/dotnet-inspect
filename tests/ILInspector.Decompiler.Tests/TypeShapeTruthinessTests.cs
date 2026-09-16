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
/// The printer's shape-driven truthiness: given a resolved TypeShape for a
/// non-generic definition branch operand, an enum zero-tests and a reference
/// null-tests. Built directly with a TypeShapes map so the rendering is tested
/// independent of csc codegen (enum brfalse is Release-only).
/// </summary>
public class TypeShapeTruthinessTests
{
    static string PrintConditionOn(TypeRef conditionType, TypeShape shape, bool negated = false)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var then = new Block(0);
        then.Add(new Return(new Constant(1, intType)));
        IrExpression condition = new LoadLocal(0, conditionType);
        if (negated)
            condition = new LogicalNot(condition);
        var entry = new Block(0);
        entry.Add(new IfStatement(condition, then, null));
        entry.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [conditionType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [conditionType] = shape },
        };
        return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
    }

    [Fact]
    public void EnumShape_ZeroTests()
    {
        var enumType = TypeRef.Definition("asm", "NS", "MyEnum");
        Assert.Contains("if (V_0 != 0)", PrintConditionOn(enumType, TypeShape.Enum));
    }

    [Fact]
    public void ReferenceShape_NullTests()
    {
        var classType = TypeRef.Definition("asm", "NS", "MyClass");
        Assert.Contains("if (V_0 is not null)", PrintConditionOn(classType, TypeShape.Reference));
    }

    [Fact]
    public void ReferenceHint_NullTests()
    {
        var classType = TypeRef.Definition("other-asm", "NS", "MyClass", ValueTypeHint.ReferenceType);

        Assert.Contains("if (V_0 is not null)", PrintConditionOn(classType, TypeShape.Unknown));
        Assert.Contains("if (V_0 is null)", PrintConditionOn(classType, TypeShape.Unknown, negated: true));
    }

    [Fact]
    public void ValueTypeHint_ZeroTests()
    {
        var enumType = TypeRef.Definition("other-asm", "NS", "MyEnum", ValueTypeHint.ValueType);
        Assert.Contains("if (V_0 != 0)", PrintConditionOn(enumType, TypeShape.Unknown));
    }

    [Fact]
    public void UnknownShapeWithoutHint_StaysRaw()
    {
        // A cross-assembly definition with no CLASS/VALUETYPE hint resolves to
        // Unknown — print raw, no guess.
        var classType = TypeRef.Definition("other-asm", "NS", "Mystery");
        string output = PrintConditionOn(classType, TypeShape.Unknown);

        Assert.DoesNotContain("is null", output);
        Assert.DoesNotContain("!= 0", output);
        Assert.Contains("V_0", output);   // still references the operand, raw
    }
}
