using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 9 guard for the decompiler product quality ladder (#1599): dynamic
/// and expression-tree honesty. Beyond the fully-owned homogeneous-<c>Int32</c>
/// arithmetic slice (#2864), which recovers to its source lambda, it does not
/// claim dynamic or expression-tree source-syntax recovery. It locks the current
/// safe boundary: dynamic call-site scaffolding degrades honestly across the
/// Roslyn binder families this row owns, unproven expression-tree builders render
/// as explicit <c>Expression.*</c> calls where supported, and prohibited
/// expression-tree forms stay documented as the row's honesty frontier.
/// </summary>
public class LadderRung9GateTests
{
    static string FixturePath => typeof(LadderRung9.DynamicAndExpressionTrees).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung9.DynamicAndExpressionTrees).FullName!;

    static readonly string[] ExpectedMembers =
    [
        ".ctor",
        "CapturedExpressionTree",
        "DynamicAdd",
        "DynamicCompoundMember",
        "DynamicConstruct",
        "DynamicConvert",
        "DynamicEventAdd",
        "DynamicEventRemove",
        "DynamicGetIndex",
        "DynamicGetLength",
        "DynamicInvoke",
        "DynamicInvokeMember",
        "DynamicNamedOut",
        "DynamicNegate",
        "DynamicRefArgument",
        "DynamicResultDiscarded",
        "DynamicSetIndex",
        "DynamicSetMember",
        "NamedArgumentExpressionTree",
        "Optional",
        "OptionalArgumentExpressionTree",
        "SimpleExpressionTree",
    ];

    [Fact]
    public void Rung9Fixture_ExposesExactMemberSet()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Rung9Fixture_HasNoInvalidFull()
    {
        var results = ValidityCheck.Evaluate(FixturePath, importSiblingBodies: true)
            .Where(r => r.TypeName == FixtureType)
            .ToList();

        Assert.NotEmpty(results);

        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 9 requires no invalid Full; malformed Full: " + string.Join("; ", malformedFull));

        var semanticFull = results
            .Where(r => r.IsFull && r.HasSemanticDefect)
            .Select(r => $"{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semanticFull.Length == 0,
            "Rung 9 requires zero non-noise semantic defects on Full methods; defects: " + string.Join("; ", semanticFull));

        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.MethodName} was not semantically bound."));
    }

    [Fact]
    public void Rung9Fixture_DegradesDynamicCallSitesHonestly()
    {
        var members = LoadRaisedMembers();

        AssertDynamicPartial(members, "DynamicAdd", "Binder.BinaryOperation", "CallSite<Func<CallSite, object, object, object>>");
        AssertDynamicPartial(members, "DynamicGetLength", "Binder.GetMember", "CallSite<Func<CallSite, object, object>>");
        AssertDynamicPartial(members, "DynamicInvoke", "Binder.Invoke", "CallSite<Func<CallSite, object, int, object>>");
        AssertDynamicPartial(members, "DynamicInvokeMember", "Binder.InvokeMember", "CallSite<Func<CallSite, object, int, int, object>>");
        AssertDynamicPartial(members, "DynamicConvert", "Binder.Convert", "CallSite<Func<CallSite, object, int>>");
        AssertDynamicPartial(members, "DynamicNegate", "Binder.UnaryOperation", "CallSite<Func<CallSite, object, object>>");
        AssertDynamicPartial(members, "DynamicConstruct", "Binder.InvokeConstructor", "CallSite<Func<CallSite, Type, object, DynamicConstructTarget>>");
        AssertDynamicPartial(members, "DynamicSetMember", "Binder.SetMember");
        AssertDynamicPartial(members, "DynamicGetIndex", "Binder.GetIndex");
        AssertDynamicPartial(members, "DynamicSetIndex", "Binder.SetIndex");
        AssertDynamicPartial(members, "DynamicCompoundMember", "Binder.SetMember(unchecked((CSharpBinderFlags)128)", "Binder.GetMember", "Binder.BinaryOperation");
        AssertDynamicPartial(members, "DynamicResultDiscarded", "Binder.InvokeMember(unchecked((CSharpBinderFlags)256)", "CallSite<Action<CallSite, object>>");
        AssertDynamicPartial(members, "DynamicEventAdd", "Binder.IsEvent", "add_Changed");
        AssertDynamicPartial(members, "DynamicEventRemove", "Binder.IsEvent", "remove_Changed");
    }

    [Fact]
    public void Rung9Fixture_DegradesDynamicArgumentMetadataHonestly()
    {
        var members = LoadRaisedMembers();

        AssertDynamicPartial(
            members,
            "DynamicNamedOut",
            "Binder.InvokeMember",
            "CSharpArgumentInfo.Create",
            "\"name\"",
            "\"result\"");
        AssertDynamicPartial(
            members,
            "DynamicRefArgument",
            "Binder.InvokeMember",
            "CSharpArgumentInfo.Create");
    }

    [Fact]
    public void Rung9Fixture_RaisesSimpleExpressionTree_AndKeepsUnprovenFormsFactory()
    {
        var members = LoadRaisedMembers();

        // The fully-owned Int32 arithmetic slice recovers to its source lambda.
        var simple = members.Single(m => m.Name == "SimpleExpressionTree");
        Assert.Equal(DecompilationFidelity.Full, simple.Function.Fidelity);
        Assert.Contains("=> x + 1", simple.Body);
        Assert.DoesNotContain("Expression.Lambda", simple.Body);
        Assert.DoesNotContain("Expression.Parameter", simple.Body);
        Assert.DoesNotContain("Expression.Add", simple.Body);

        // Captured member-token graph stays in its honest factory-call form.
        var captured = members.Single(m => m.Name == "CapturedExpressionTree");
        Assert.Equal(DecompilationFidelity.Partial, captured.Function.Fidelity);
        Assert.Contains("Expression.GreaterThan(", captured.Body);
        Assert.Contains("FieldInfo.GetFieldFromHandle", captured.Body);
        Assert.Contains("LoadToken Field", captured.Body);
        Assert.DoesNotContain("=>", captured.Body);

        foreach (string name in new[]
        {
            "OptionalArgumentExpressionTree",
            "NamedArgumentExpressionTree",
        })
        {
            var arguments = members.Single(m => m.Name == name);
            Assert.Equal(DecompilationFidelity.Partial, arguments.Function.Fidelity);
            Assert.Contains("Expression.Call(", arguments.Body);
            Assert.Contains("Expression.Lambda<Func<int, int>>", arguments.Body);
            Assert.DoesNotContain("=>", arguments.Body);
        }
    }

    [Theory]
    [InlineData("Expression<Func<dynamic, object>> e = x => x.Value;", "CS1963")]
    [InlineData("Expression<Func<int, int>> e = x => x = 1;", "CS0832")]
    [InlineData("Expression<Action> e = () => { };", "CS0834")]
    [InlineData("Expression<Func<System.Threading.Tasks.Task>> e = async () => await System.Threading.Tasks.Task.CompletedTask;", "CS1989")]
    [InlineData("Expression<Func<object, bool>> e = x => x is string s;", "CS8122")]
    [InlineData("Expression<Func<(int, int)>> e = () => (1, 2);", "CS8143")]
    [InlineData("Expression<Func<int[], int>> e = x => x[^1];", "CS8791")]
    [InlineData("Expression<Func<int[], int[]>> e = x => x[1..];", "CS8792")]
    [InlineData("Expression<Func<RecordSample, RecordSample>> e = x => x with { X = 1 };", "CS8849")]
    [InlineData("Expression<Func<int[]>> e = () => [1, 2];", "CS9175")]
    public void Rung9ExpressionTreeRestrictions_AreTrackedAsHonestyFrontier(string statement, string expectedDiagnostic)
    {
        var diagnostics = CompileExpressionTreeStatement(statement);

        Assert.Contains(expectedDiagnostic, diagnostics);
    }

    [Theory]
    [InlineData("Expression<Func<int, int>> e = x => Optional(x);")]
    [InlineData("Expression<Func<int, int>> e = x => Optional(value: x, delta: 1);")]
    public void Rung9ExpressionTreeArgumentForms_AreAcceptedByCompilerOracle(string statement)
    {
        Assert.Empty(CompileExpressionTreeStatement(statement));
    }

    static void AssertDynamicPartial(
        List<(string Name, IrFunction Function, DecompilerResult Result, string Body)> members,
        string name,
        params string[] expectedFragments)
    {
        var member = members.Single(m => m.Name == name);
        Assert.Equal(DecompilationFidelity.Partial, member.Function.Fidelity);
        foreach (string fragment in expectedFragments)
            Assert.Contains(fragment, member.Body);
        Assert.DoesNotContain("dynamic", member.Body);
    }

    static List<(string Name, IrFunction Function, DecompilerResult Result, string Body)> LoadRaisedMembers()
    {
        var members = new List<(string Name, IrFunction Function, DecompilerResult Result, string Body)>();
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != FixtureType)
                continue;

            var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            members.Add((methodName, function, result, result.Output ?? ""));
        }

        return members;
    }

    static string[] CompileExpressionTreeStatement(string statement)
    {
        string source = $$"""
            using System;
            using System.Linq.Expressions;

            public record RecordSample(int X);

            public static class ExpressionTreeRestrictionShell
            {
                static int Optional(int value, int delta = 1) => value + delta;

                public static void Check(dynamic dynamicValue)
                {
                    {{statement}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "expression-tree-restriction-shell",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;
}
