using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public sealed class CSharpPrinterReceiverTests
{
    static readonly TypeRef Int32Type = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");

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

    static string RenderReturn(IrExpression value, TypeRef returnType)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0);
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
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return references.ToImmutable();
    }
}
