using ILInspector.Decompiler.Pipeline;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class NestedScopeNameCollisionTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef FuncIntInt = TypeRef.GenericInstance(
        TypeRef.CoreLib("System", "Func`2"),
        [TypeRef.CoreLib("System", "Int32"), TypeRef.CoreLib("System", "Int32")]);
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "", "Holder");

    [Fact]
    public void LambdaStackSlot_AvoidsOuterStackSlotName()
    {
        var lambdaBody = Body(
            new StoreStackSlot(0, new Constant(2, Int32)),
            new ExpressionStatement(new LoadStackSlot(0, Int32)));
        var lambda = new Lambda(
            Action,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);

        string body = RenderBody(
            [Int32, Action],
            new StoreStackSlot(0, new Constant(1, Int32)),
            new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)),
            new StoreLocal(1, Action, lambda));

        Assert.Contains("int S_0 = 1;", body);
        Assert.Contains("int S_0_1 = 2;", body);
        Assert.DoesNotContain("() => { int S_0 = 2;", body);
        AssertCompiles(body);
    }

    [Fact]
    public void LambdaFallbackLocal_AvoidsOuterFallbackLocalName()
    {
        var lambdaBody = Body(
            new StoreLocal(0, Int32, new Constant(2, Int32)),
            new ExpressionStatement(new LoadLocal(0, Int32)));
        var lambda = new Lambda(
            Action,
            [],
            [Int32],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);

        string body = RenderBody(
            [Int32, Action],
            new StoreLocal(0, Int32, new Constant(1, Int32)),
            new StoreLocal(1, Action, lambda));

        Assert.Contains("int V_0 = 1;", body);
        Assert.Contains("int V_0_1 = 2;", body);
        Assert.DoesNotContain("() => { int V_0 = 2;", body);
        AssertCompiles(body);
    }

    [Fact]
    public void LocalFunctionStackSlot_AvoidsOuterStackSlotName()
    {
        var localBody = Body(
            new StoreStackSlot(0, new Constant(2, Int32)),
            new ExpressionStatement(new LoadStackSlot(0, Int32)));
        var localFunction = new LocalFunctionStatement(
            "Inner",
            Void,
            [],
            isStatic: false,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);

        string body = RenderBody(
            [Int32],
            new StoreStackSlot(0, new Constant(1, Int32)),
            new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)),
            localFunction);

        Assert.Contains("int S_0 = 1;", body);
        Assert.Contains("int S_0_1 = 2;", body);
        Assert.DoesNotContain("void Inner()\n    {\n        int S_0 = 2;", body);
        AssertCompiles(body);
    }

    [Fact]
    public void DeepNestedLambda_CarriesGrandparentReservedNames()
    {
        var innerBody = Body(
            new StoreStackSlot(0, new Constant(3, Int32)),
            new ExpressionStatement(new LoadStackSlot(0, Int32)));
        var inner = new Lambda(
            Action,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            innerBody);
        var middleBody = Body(new StoreLocal(0, Action, inner));
        var middle = new Lambda(
            Action,
            [],
            [Action],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            middleBody);

        string body = RenderBody(
            [Int32, Action],
            new StoreStackSlot(0, new Constant(1, Int32)),
            new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)),
            new StoreLocal(1, Action, middle));

        Assert.Contains("int S_0 = 1;", body);
        Assert.Contains("int S_0_1 = 3;", body);
        Assert.DoesNotContain("() => { int S_0 = 3;", body);
        AssertCompiles(body);
    }

    [Fact]
    public void NestedSwitchTemp_AvoidsOuterSwitchTemp()
    {
        var innerLambda = new Lambda(
            Action,
            [],
            [Int32],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            SwitchBody());

        string body = RenderBody(
            [Action],
            new SwitchBranch(new Constant(0, Int32), ImmutableArray.Create(4)),
            new StoreLocal(0, Action, innerLambda));

        Assert.Contains("int __dotnet_inspect_switch0 = default;", body);
        Assert.Contains("int __dotnet_inspect_switch0_1 = default;", body);
        Assert.DoesNotContain("() => { int __dotnet_inspect_switch0 = default;", body);
    }

    [Fact]
    public void OuterGeneratedName_AvoidsNestedLambdaParameter()
    {
        var lambda = new Lambda(
            FuncIntInt,
            [new Parameter("S_0", Int32)],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            Body(new Return(new LoadArgument(0, "S_0", Int32))));

        string body = RenderBody(
            [Int32, FuncIntInt],
            new StoreStackSlot(0, new Constant(1, Int32)),
            new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)),
            new StoreLocal(1, FuncIntInt, lambda));

        Assert.Contains("int S_0_1 = 1;", body);
        Assert.Contains("S_0 =>", body);
        Assert.DoesNotContain("int S_0 = 1;", body);
        AssertCompiles(body);
    }

    static BlockContainer Body(params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return body;
    }

    static BlockContainer SwitchBody()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new SwitchBranch(new Constant(0, Int32), ImmutableArray.Create(4)));
        var target = new Block(4);
        target.Add(new Return(null));
        body.Add(entry);
        body.Add(target);
        return body;
    }

    static string RenderBody(IReadOnlyList<TypeRef> locals, params IrNode[] statements)
    {
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [.. locals],
            Body(statements));
        return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").Trim();
    }

    static void AssertCompiles(string body)
    {
        string source = $$"""
            using System;
            static class __Gate
            {
                public static void M()
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
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static IEnumerable<MetadataReference> RuntimeReferences()
    {
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                yield return MetadataReference.CreateFromFile(path);
        }
    }
}
