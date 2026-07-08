using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public sealed class CSharpPrinterReceiverTests
{
    static readonly TypeRef Int32Type = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef IndexType = TypeRef.CoreLib("System", "Index");
    static readonly TypeRef RangeType = TypeRef.CoreLib("System", "Range");
    static readonly TypeRef RecordType = TypeRef.Definition("synthetic", "", "R");

    [Fact]
    public void NegativeConstant_InstanceMethodReceiver_IsParenthesized()
    {
        // #2151: -1.ToString() parses as -(1.ToString()), so a negative
        // literal member receiver must spell as (-1).ToString().
        var call = new Call(
            new MethodRef(Int32Type, "ToString", StringType, [], HasThis: true),
            isVirtual: false,
            [new Constant(-1, Int32Type)]);

        string body = RenderReturn(call, StringType);

        Assert.Contains("return (-1).ToString();", body);
        Assert.DoesNotContain("return -1.ToString();", body);
        AssertCompiles("public static string M()", body);
    }

    [Fact]
    public void NegativeConstant_ExtensionMethodReceiver_IsParenthesized()
    {
        var extension = new MethodRef(
            TypeRef.Definition("synthetic", "", "Extensions"),
            "Ext",
            Int32Type,
            [Int32Type],
            HasThis: false)
        {
            IsExtension = MetadataFactState.Yes,
        };
        var call = new Call(extension, isVirtual: false, [new Constant(-1, Int32Type)]);

        string body = RenderReturn(call, Int32Type);

        Assert.Contains("return (-1).Ext();", body);
        Assert.DoesNotContain("return -1.Ext();", body);
        AssertCompiles(
            "public static int M()",
            body,
            "public static class Extensions { public static int Ext(this int value) => value; }");
    }

    [Fact]
    public void IndexFromEnd_ExtensionMethodReceiver_IsParenthesized()
    {
        var call = new Call(
            Extension("ExtIndex", IndexType),
            isVirtual: false,
            [new IndexFromEnd(new Constant(1, Int32Type))]);

        string body = RenderReturn(call, Int32Type);

        Assert.Contains("return (^1).ExtIndex();", body);
        Assert.DoesNotContain("return ^1.ExtIndex();", body);
        AssertCompiles(
            "public static int M()",
            body,
            "public static class Extensions { public static int ExtIndex(this Index value) => value.GetOffset(100); }");
    }

    [Fact]
    public void RangeExpression_ExtensionMethodReceiver_IsParenthesized()
    {
        var call = new Call(
            Extension("ExtRange", RangeType),
            isVirtual: false,
            [new RangeExpression(new Constant(1, Int32Type), new Constant(2, Int32Type))]);

        string body = RenderReturn(call, Int32Type);

        Assert.Contains("return (1..2).ExtRange();", body);
        Assert.DoesNotContain("return 1..2.ExtRange();", body);
        AssertCompiles(
            "public static int M()",
            body,
            "public static class Extensions { public static int ExtRange(this Range value) => value.End.GetOffset(100); }");
    }

    [Fact]
    public void PrefixIncrement_ExtensionMethodReceiver_IsParenthesized()
    {
        var call = new Call(
            Extension("Ext", Int32Type),
            isVirtual: false,
            [new IncrementDecrement(new LoadArgument(0, "x", Int32Type), isIncrement: true, isPrefix: true)]);

        string body = RenderReturn(call, Int32Type, [new Parameter("x", Int32Type)]);

        Assert.Contains("return (++x).Ext();", body);
        Assert.DoesNotContain("return ++x.Ext();", body);
        AssertCompiles(
            "public static int M(int x)",
            body,
            "public static class Extensions { public static int Ext(this int value) => value; }");
    }

    [Fact]
    public void WithExpression_ExtensionMethodReceiver_IsParenthesized()
    {
        var call = new Call(
            Extension("ExtRecord", RecordType),
            isVirtual: false,
            [
                new WithExpression(
                    new LoadArgument(0, "r", RecordType),
                    [new InitializerEntry("A", [new Constant(2, Int32Type)])])
            ]);

        string body = RenderReturn(call, Int32Type, [new Parameter("r", RecordType)]);

        Assert.Contains("return (r with { A = 2 }).ExtRecord();", body);
        Assert.DoesNotContain("return r with { A = 2 }.ExtRecord();", body);
        AssertCompiles(
            "public static int M(R r)",
            body,
            """
            public record R(int A);
            public static class Extensions { public static int ExtRecord(this R value) => value.A; }
            """);
    }

    [Fact]
    public void StaticCall_OnCurrentType_IsUnqualified()
    {
        // #2497: a static call to a member of the current type needs no type
        // qualifier — Helper(1), not Holder.Helper(1) — matching the this-receiver
        // instance form and same-type method groups.
        var self = TypeRef.Definition("synthetic", "", "Holder");
        var call = new Call(
            new MethodRef(self, "Helper", Int32Type, [Int32Type], HasThis: false),
            isVirtual: false,
            [new Constant(1, Int32Type)]);

        string body = RenderReturn(call, Int32Type);

        Assert.Contains("return Helper(1);", body);
        Assert.DoesNotContain("Holder.Helper", body);
    }

    [Fact]
    public void StaticCall_OnCrossType_StaysQualified()
    {
        // Near-miss: a static call to another type's member must remain qualified.
        var other = TypeRef.Definition("synthetic", "", "Other");
        var call = new Call(
            new MethodRef(other, "Helper", Int32Type, [Int32Type], HasThis: false),
            isVirtual: false,
            [new Constant(1, Int32Type)]);

        string body = RenderReturn(call, Int32Type);

        Assert.Contains("return Other.Helper(1);", body);
    }

    static MethodRef Extension(string name, TypeRef receiverType)
        => new(
            TypeRef.Definition("synthetic", "", "Extensions"),
            name,
            Int32Type,
            [receiverType],
            HasThis: false)
        {
            IsExtension = MetadataFactState.Yes,
        };

    static string RenderReturn(IrExpression value, TypeRef returnType, IReadOnlyList<Parameter>? parameters = null)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var parameterList = parameters is null ? ImmutableArray<Parameter>.Empty : [.. parameters];
        var signature = new MethodSignature(returnType, parameterList, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static void AssertCompiles(string header, string body, string extraDeclarations = "")
    {
        var errors = Recompile(header, body, extraDeclarations)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body, string extraDeclarations)
    {
        string source = $$"""
            using System;
            {{extraDeclarations}}
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;
}
