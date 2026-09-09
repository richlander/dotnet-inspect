using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class LocalFunctionKeywordEscapeTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");

    // Builds `int M() { int <name>() => 1; return <name>(); }` and prints it.
    static string PrintWithLocalFunction(string name)
    {
        var localBody = new BlockContainer();
        var localBlock = new Block();
        localBlock.Add(new Return(new Constant(1, s_int)));
        localBody.Add(localBlock);
        var localFunction = new LocalFunctionStatement(
            name, s_int, [], isStatic: false, [], [], usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, localBody);

        var block = new Block();
        block.Add(localFunction);
        block.Add(new Return(new LocalFunctionInvocation(name, s_int, [])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
    }

    // #1465: a recovered local function named with a C# keyword must be @-escaped at
    // both its declaration header and its invocation, not emitted raw at Full.
    [Fact]
    public void KeywordLocalFunctionName_IsEscapedAtDeclarationAndInvocation()
    {
        var output = PrintWithLocalFunction("return");

        Assert.Contains("@return(", output);            // declaration header escaped
        Assert.Contains("return @return();", output);   // invocation escaped
        Assert.DoesNotContain("int return(", output);   // header not emitted raw
        Assert.DoesNotContain("return return()", output);
    }

    [Fact]
    public void ContextualKeywordLocalFunctionName_IsEscapedAtDeclarationAndInvocation()
    {
        var output = PrintWithLocalFunction("await");

        Assert.Contains("@await(", output);
        Assert.Contains("return @await();", output);
        Assert.DoesNotContain("int await(", output);
        Assert.DoesNotContain("return await()", output);

        string shell = $$"""
            using System.Threading.Tasks;
            class C
            {
                async Task<int> M()
                {
            {{output}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(shell, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(tree.GetDiagnostics(TestContext.Current.CancellationToken),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OrdinaryLocalFunctionName_IsUnchanged()
    {
        var output = PrintWithLocalFunction("Helper");

        Assert.Contains("Helper(", output);
        Assert.Contains("return Helper();", output);
        Assert.DoesNotContain("@Helper", output);
    }
}
