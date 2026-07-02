using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Decompiler.Pipeline;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Addressable generated C# fixtures for progressive shape and compile-back
/// checks. The catalogue names the source shape, expected target methods, and
/// expected outcomes; the runner materializes those entries as a temporary class
/// library and grades them with a Roslyn shape check plus
/// <see cref="FidelityCheck.Evaluate(string)"/>.
/// </summary>
internal static class GeneratedFixtureCatalog
{
    public static readonly GeneratedFixtureDefinition MinimalPropertyLiteral = new(
        "minimal.property.literal",
        """
        namespace GeneratedFixtures.MinimalPropertyLiteral;

        public class Class1
        {
            public string Method1 => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalPropertyLiteral.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalPropertyLiteral.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "property", "literal"]);

    public static readonly GeneratedFixtureDefinition MinimalPrimaryCtorFieldInit = new(
        "minimal.primary-ctor.field-init",
        """
        namespace GeneratedFixtures.MinimalPrimaryCtorFieldInit;

        public class Class1(string message)
        {
            private readonly string _message = message;

            public string Method1 => _message;
        }
        """,
        [
            new(
                "GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1",
                ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "primary-constructor", "field-initializer"]);

    public static readonly GeneratedFixtureDefinition MinimalCtorFieldGetter = new(
        "minimal.ctor-field.getter",
        """
        namespace GeneratedFixtures.MinimalCtorFieldGetter;

        public class Class1
        {
            private readonly string _message;

            public Class1(string message)
            {
                _message = message;
            }

            public string Method1 => _message;
        }
        """,
        [
            new("GeneratedFixtures.MinimalCtorFieldGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalCtorFieldGetter.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "constructor", "field", "property"]);

    public static readonly GeneratedFixtureDefinition MinimalAutoPropertyGetter = new(
        "minimal.auto-property.getter",
        """
        namespace GeneratedFixtures.MinimalAutoPropertyGetter;

        public class Class1
        {
            public Class1(string message)
            {
                Method1 = message;
            }

            public string Method1 { get; }
        }
        """,
        [
            new("GeneratedFixtures.MinimalAutoPropertyGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalAutoPropertyGetter.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "constructor", "auto-property"]);

    public static readonly GeneratedFixtureDefinition MinimalMethodCallSameType = new(
        "minimal.method-call.same-type",
        """
        namespace GeneratedFixtures.MinimalMethodCallSameType;

        public class Class1
        {
            public string Method1() => Method2();

            private string Method2() => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", "Method2",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "method-call"]);

    public static readonly GeneratedFixtureDefinition MinimalStaticMethodCall = new(
        "minimal.static-method-call",
        """
        namespace GeneratedFixtures.MinimalStaticMethodCall;

        public class Class1
        {
            public static string Method1() => Helper();

            private static string Helper() => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", "Helper",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "static", "method-call"]);

    public static readonly GeneratedFixtureDefinition MinimalStaticConstructor = new(
        "minimal.static-constructor",
        """
        namespace GeneratedFixtures.MinimalStaticConstructor;

        public class Class1
        {
            private static int s_value;

            static Class1()
            {
                s_value = 42;
            }

            public static int Method1() => s_value;
        }
        """,
        [
            new("GeneratedFixtures.MinimalStaticConstructor.Class1", ".cctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticConstructor.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticConstructor.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "static", "constructor", "field"]);

    public static readonly GeneratedFixtureDefinition MinimalInterfaceImplementation = new(
        "minimal.interface-implementation",
        """
        namespace GeneratedFixtures.MinimalInterfaceImplementation;

        public interface IValue
        {
            int GetValue();
        }

        public class Class1 : IValue
        {
            public int GetValue() => 42;
        }
        """,
        [
            new("GeneratedFixtures.MinimalInterfaceImplementation.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalInterfaceImplementation.Class1", "GetValue",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "interface", "implementation"]);

    public static readonly GeneratedFixtureDefinition MinimalObjectInitializer = new(
        "minimal.object-initializer",
        """
        namespace GeneratedFixtures.MinimalObjectInitializer;

        public class Helper
        {
            public int Value { get; set; }
        }

        public class Class1
        {
            public Helper Method1(int value) => new Helper { Value = value };
        }
        """,
        [
            new("GeneratedFixtures.MinimalObjectInitializer.Helper", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalObjectInitializer.Helper", "get_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalObjectInitializer.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalObjectInitializer.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "object-initializer", "property", "setter"]);

    public static readonly GeneratedFixtureDefinition MinimalCollectionInitializer = new(
        "minimal.collection-initializer",
        """
        namespace GeneratedFixtures.MinimalCollectionInitializer;

        public class Class1
        {
            public System.Collections.Generic.List<int> Method1(int value)
                => new System.Collections.Generic.List<int> { value };
        }
        """,
        [
            new("GeneratedFixtures.MinimalCollectionInitializer.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalCollectionInitializer.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "collection", "initializer", "generic"]);

    public static readonly GeneratedFixtureDefinition MinimalIfElse = new(
        "minimal.if-else",
        """
        namespace GeneratedFixtures.MinimalIfElse;

        public class Class1
        {
            public int Method1(int value)
            {
                if (value < 0)
                    return -1;
                return 1;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalIfElse.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIfElse.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "branch", "if-else"]);

    public static readonly GeneratedFixtureDefinition MinimalIntegerAddition = new(
        "minimal.integer-addition",
        """
        namespace GeneratedFixtures.MinimalIntegerAddition;

        public class Class1
        {
            public int Method1(int left, int right) => left + right;
        }
        """,
        [
            new("GeneratedFixtures.MinimalIntegerAddition.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIntegerAddition.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.AddExpression),
        ],
        ["minimal", "integer", "arithmetic", "addition"]);

    public static readonly GeneratedFixtureDefinition MinimalArrayIndex = new(
        "minimal.array-index",
        """
        namespace GeneratedFixtures.MinimalArrayIndex;

        public class Class1
        {
            public int Method1(int[] values, int index) => values[index];
        }
        """,
        [
            new("GeneratedFixtures.MinimalArrayIndex.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalArrayIndex.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.ElementAccessExpression),
        ],
        ["minimal", "array", "index"]);

    public static readonly GeneratedFixtureDefinition MinimalArrayLength = new(
        "minimal.array-length",
        """
        namespace GeneratedFixtures.MinimalArrayLength;

        public class Class1
        {
            public int Method1(int[] values) => values.Length;
        }
        """,
        [
            new("GeneratedFixtures.MinimalArrayLength.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalArrayLength.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.SimpleMemberAccessExpression),
        ],
        ["minimal", "array", "length"]);

    public static readonly GeneratedFixtureDefinition MinimalIndexerGetter = new(
        "minimal.indexer.getter",
        """
        namespace GeneratedFixtures.MinimalIndexerGetter;

        public class Class1
        {
            public int this[int index] => index + 1;
        }
        """,
        [
            new("GeneratedFixtures.MinimalIndexerGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIndexerGetter.Class1", "get_Item",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "indexer", "property"]);

    public static readonly GeneratedFixtureDefinition MinimalStringLength = new(
        "minimal.string-length",
        """
        namespace GeneratedFixtures.MinimalStringLength;

        public class Class1
        {
            public int Method1(string value) => value.Length;
        }
        """,
        [
            new("GeneratedFixtures.MinimalStringLength.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStringLength.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.SimpleMemberAccessExpression),
        ],
        ["minimal", "string", "length"]);

    public static readonly GeneratedFixtureDefinition MinimalNullCoalesce = new(
        "minimal.null-coalesce",
        """
        namespace GeneratedFixtures.MinimalNullCoalesce;

        public class Class1
        {
            public string Method1(string value) => value ?? "fallback";
        }
        """,
        [
            new("GeneratedFixtures.MinimalNullCoalesce.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalNullCoalesce.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.CoalesceExpression),
        ],
        ["minimal", "null-coalesce"]);

    public static readonly GeneratedFixtureDefinition MinimalTryFinally = new(
        "minimal.try-finally",
        """
        namespace GeneratedFixtures.MinimalTryFinally;

        public class Class1
        {
            private int _count;

            public int Method1(int value)
            {
                try
                {
                    return value + 1;
                }
                finally
                {
                    _count++;
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalTryFinally.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalTryFinally.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.TryStatement),
        ],
        ["minimal", "try-finally", "lifetime"]);

    public static readonly GeneratedFixtureDefinition MinimalUsingDispose = new(
        "minimal.using-dispose",
        """
        namespace GeneratedFixtures.MinimalUsingDispose;

        public class Class1
        {
            public long Method1()
            {
                using var stream = new System.IO.MemoryStream();
                return stream.Length;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalUsingDispose.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalUsingDispose.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "using", "dispose", "lifetime"]);

    public static readonly GeneratedFixtureDefinition MinimalForeachArray = new(
        "minimal.foreach-array",
        """
        namespace GeneratedFixtures.MinimalForeachArray;

        public class Class1
        {
            public int Method1(int[] values)
            {
                var sum = 0;
                foreach (var value in values)
                    sum += value;
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalForeachArray.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalForeachArray.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.ForEachStatement),
        ],
        ["minimal", "foreach", "array", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalForLoop = new(
        "minimal.for-loop",
        """
        namespace GeneratedFixtures.MinimalForLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                for (var i = 0; i < count; i++)
                    sum += i;
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalForLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalForLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.ForStatement),
        ],
        ["minimal", "for", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalWhileLoop = new(
        "minimal.while-loop",
        """
        namespace GeneratedFixtures.MinimalWhileLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                while (count > 0)
                {
                    sum += count;
                    count--;
                }
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalWhileLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalWhileLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.WhileStatement),
        ],
        ["minimal", "while", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalDoWhileLoop = new(
        "minimal.do-while",
        """
        namespace GeneratedFixtures.MinimalDoWhileLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                do
                {
                    sum += count;
                    count--;
                }
                while (count > 0);
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalDoWhileLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDoWhileLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.DoStatement),
        ],
        ["minimal", "do-while", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalSwitchInt = new(
        "minimal.switch-int",
        """
        namespace GeneratedFixtures.MinimalSwitchInt;

        public class Class1
        {
            public string Method1(int value)
            {
                switch (value)
                {
                    case 0:
                        return "zero";
                    case 1:
                        return "one";
                    case 2:
                        return "two";
                    case 3:
                        return "three";
                    default:
                        return "many";
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalSwitchInt.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalSwitchInt.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.SwitchStatement),
        ],
        ["minimal", "switch", "int", "branch"]);

    public static readonly GeneratedFixtureDefinition MinimalSwitchTwoCaseLowersIf = new(
        "minimal.switch-two-case-lowers-if",
        """
        namespace GeneratedFixtures.MinimalSwitchTwoCaseLowersIf;

        public class Class1
        {
            public string Method1(int value)
            {
                switch (value)
                {
                    case 0:
                        return "zero";
                    case 1:
                        return "one";
                    default:
                        return "many";
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new(
                "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
                "Method1",
                FidelityCheck.CompileBackStatus.OpcodeDiff,
                IsFrontier: true,
                Note: "Current SDK lowers this two-case source switch to if/else; dense minimal.switch-int is the stable switch-statement rung."),
        ],
        ["minimal", "switch", "int", "branch", "frontier", "compiler-lowering"]);

    public static readonly GeneratedFixtureDefinition MinimalConditionalExpressionShapeFrontier = new(
        "minimal.conditional-expression-shape-frontier",
        """
        namespace GeneratedFixtures.MinimalConditionalExpressionShapeFrontier;

        public class Class1
        {
            public int Method1(bool flag) => flag ? 1 : 2;
        }
        """,
        [
            new("GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new(
                "GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1",
                "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                IsFrontier: true,
                Note: "Current output is compile-back exact but does not preserve the conditional-expression source shape.",
                ExpectedShape: SyntaxKind.ReturnStatement,
                FrontierShape: SyntaxKind.ConditionalExpression),
        ],
        ["minimal", "conditional-expression", "branch", "frontier", "shape"]);

    public static IReadOnlyList<GeneratedFixtureDefinition> All { get; } =
    [
        MinimalPropertyLiteral,
        MinimalPrimaryCtorFieldInit,
        MinimalCtorFieldGetter,
        MinimalAutoPropertyGetter,
        MinimalMethodCallSameType,
        MinimalStaticMethodCall,
        MinimalStaticConstructor,
        MinimalInterfaceImplementation,
        MinimalObjectInitializer,
        MinimalCollectionInitializer,
        MinimalIfElse,
        MinimalIntegerAddition,
        MinimalArrayIndex,
        MinimalArrayLength,
        MinimalIndexerGetter,
        MinimalStringLength,
        MinimalNullCoalesce,
        MinimalTryFinally,
        MinimalUsingDispose,
        MinimalForeachArray,
        MinimalForLoop,
        MinimalWhileLoop,
        MinimalDoWhileLoop,
        MinimalSwitchInt,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> Frontiers { get; } =
    [
        MinimalSwitchTwoCaseLowersIf,
        MinimalConditionalExpressionShapeFrontier,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> Catalog { get; } =
    [
        .. All,
        .. Frontiers,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> MinimalCompileBackRungs => All;

    public static IReadOnlyList<GeneratedFixtureDefinition> Select(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return All;

        return Catalog
            .Where(fixture => fixture.Id.Equals(selector, StringComparison.Ordinal)
                || fixture.Id.StartsWith(selector, StringComparison.Ordinal))
            .ToArray();
    }
}

internal sealed record GeneratedFixtureDefinition(
    string Id,
    string Source,
    IReadOnlyList<GeneratedFixtureTarget> Targets,
    IReadOnlyList<string> Tags);

internal sealed record GeneratedFixtureTarget(
    string Type,
    string Method,
    FidelityCheck.CompileBackStatus ExpectedStatus,
    int Overload = 0,
    bool IsFrontier = false,
    string? Note = null,
    SyntaxKind? ExpectedShape = null,
    SyntaxKind? FrontierShape = null)
{
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal sealed record GeneratedFixtureRunOptions(
    string? TargetFramework = null,
    bool KeepArtifacts = false)
{
    public static GeneratedFixtureRunOptions Default { get; } = new();
}

internal sealed record GeneratedFixtureRunResult(
    string ProjectDirectory,
    string AssemblyPath,
    IReadOnlyList<GeneratedFixtureResult> Results)
{
    public bool Passed => Results.All(result => result.Passed);
}

internal sealed record GeneratedFixtureResult(
    string FixtureId,
    string Type,
    string Method,
    int Overload,
    string DecompilerFidelity,
    FidelityCheck.CompileBackStatus? ActualStatus,
    FidelityCheck.CompileBackStatus ExpectedStatus,
    SyntaxKind? ActualShape,
    SyntaxKind? ExpectedShape,
    SyntaxKind? FrontierShape,
    string? ShapeDetail,
    bool IsFrontier,
    string? Detail,
    string? Note)
{
    public bool CompileBackPassed => ActualStatus == ExpectedStatus;
    public bool ShapePassed => ExpectedShape is null || ActualShape == ExpectedShape;
    public bool Passed => CompileBackPassed && ShapePassed;
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal enum GeneratedFixtureReturnToSenderStatus
{
    Pass,
    Skip,
    Fail,
}

internal sealed record GeneratedFixtureReturnToSenderRunResult(
    string ProjectDirectory,
    string AssemblyPath,
    IReadOnlyList<GeneratedFixtureReturnToSenderResult> Results)
{
    public bool Passed => Results.All(result => result.Status != GeneratedFixtureReturnToSenderStatus.Fail);
}

internal sealed record GeneratedFixtureReturnToSenderResult(
    string FixtureId,
    string Type,
    string Method,
    int Overload,
    GeneratedFixtureReturnToSenderStatus Status,
    FidelityCheck.CompileBackStatus? ActualStatus,
    string Reason,
    string? Detail,
    bool IsFrontier,
    string? Note)
{
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal sealed record GeneratedFixtureRender(string DecompilerFidelity, string? Body);

internal static class GeneratedFixtureRunner
{
    static readonly SyntaxKind[] s_interestingShapes =
    [
        SyntaxKind.IfStatement,
        SyntaxKind.ForStatement,
        SyntaxKind.ForEachStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.TryStatement,
        SyntaxKind.UsingStatement,
        SyntaxKind.ConditionalExpression,
        SyntaxKind.CoalesceExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.AddExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ReturnStatement,
    ];

    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GeneratedFixtureRunResult Run(
        IReadOnlyList<GeneratedFixtureDefinition> fixtures,
        GeneratedFixtureRunOptions? options = null)
    {
        return RunWithMaterializedFixtures(fixtures, options, (root, assemblyPath) =>
        {
            var compileBack = FidelityCheck.Evaluate(assemblyPath)
                .ToDictionary(result => Key(result.Type, result.Method, result.Overload), StringComparer.Ordinal);
            var renders = DecompilerRenders(assemblyPath, fixtures);
            var results = new List<GeneratedFixtureResult>();
            foreach (var fixture in fixtures)
            {
                foreach (var target in fixture.Targets)
                {
                    compileBack.TryGetValue(Key(target.Type, target.Method, target.Overload), out var actual);
                    renders.TryGetValue(Key(target.Type, target.Method, target.Overload), out var render);
                    var shape = ShapeVerdict(render?.Body, target.ExpectedShape, target.FrontierShape);
                    results.Add(new GeneratedFixtureResult(
                        fixture.Id,
                        target.Type,
                        target.Method,
                        target.Overload,
                        render?.DecompilerFidelity ?? "Unknown",
                        actual?.Status,
                        target.ExpectedStatus,
                        shape.ActualShape,
                        target.ExpectedShape,
                        target.FrontierShape,
                        shape.Detail,
                        target.IsFrontier,
                        actual?.Detail ?? (actual is null ? "target-method-not-found" : null),
                        target.Note));
                }
            }

            return new GeneratedFixtureRunResult(root, assemblyPath, results);
        });
    }

    public static GeneratedFixtureReturnToSenderRunResult RunReturnToSenderCatalog(
        IReadOnlyList<GeneratedFixtureDefinition> fixtures,
        GeneratedFixtureRunOptions? options = null)
    {
        return RunWithMaterializedFixtures(fixtures, options, (root, assemblyPath) =>
        {
            var requestedTargets = fixtures
                .SelectMany(fixture => fixture.Targets)
                .Select(target => new ReturnToSender.RequestedTarget(target.Type, target.Method, target.Overload))
                .Distinct()
                .ToArray();
            IReadOnlyDictionary<string, ReturnToSender.Result> rtsResults = ReturnToSender.CompileBackTargets(assemblyPath, requestedTargets)
                .ToDictionary(result => Key(
                    result.Plan.TargetMethod.Type,
                    result.Plan.TargetMethod.Method,
                    result.Plan.TargetMethod.Overload), StringComparer.Ordinal);
            var results = new List<GeneratedFixtureReturnToSenderResult>();
            foreach (var fixture in fixtures)
            {
                foreach (var target in fixture.Targets)
                {
                    if (!rtsResults.TryGetValue(Key(target.Type, target.Method, target.Overload), out var actual))
                    {
                        results.Add(Skipped(fixture, target, "unsupported-rts-target"));
                        continue;
                    }

                    var status = actual.Status == FidelityCheck.CompileBackStatus.Exact
                        ? GeneratedFixtureReturnToSenderStatus.Pass
                        : GeneratedFixtureReturnToSenderStatus.Fail;
                    results.Add(new GeneratedFixtureReturnToSenderResult(
                        fixture.Id,
                        target.Type,
                        target.Method,
                        target.Overload,
                        status,
                        actual.Status,
                        status == GeneratedFixtureReturnToSenderStatus.Pass ? "exact" : FailureReason(actual),
                        actual.Detail,
                        target.IsFrontier,
                        target.Note));
                }
            }

            return new GeneratedFixtureReturnToSenderRunResult(root, assemblyPath, results);
        });
    }

    static GeneratedFixtureReturnToSenderResult Skipped(GeneratedFixtureDefinition fixture, GeneratedFixtureTarget target, string reason)
        => new(
            fixture.Id,
            target.Type,
            target.Method,
            target.Overload,
            GeneratedFixtureReturnToSenderStatus.Skip,
            ActualStatus: null,
            reason,
            Detail: null,
            target.IsFrontier,
            target.Note);

    static string FailureReason(ReturnToSender.Result result)
        => result.Status switch
        {
            FidelityCheck.CompileBackStatus.RecompileFail => DiagnosticCode(result.Detail),
            FidelityCheck.CompileBackStatus.ContextFail => string.IsNullOrWhiteSpace(result.Detail) ? "context-fail" : result.Detail,
            FidelityCheck.CompileBackStatus.OpcodeDiff => "opcode-diff",
            _ => result.Status.ToString(),
        };

    static T RunWithMaterializedFixtures<T>(
        IReadOnlyList<GeneratedFixtureDefinition> fixtures,
        GeneratedFixtureRunOptions? options,
        Func<string, string, T> run)
    {
        if (fixtures.Count == 0)
            throw new ArgumentException("At least one generated fixture is required.", nameof(fixtures));

        options ??= GeneratedFixtureRunOptions.Default;
        var root = Path.Combine(Path.GetTempPath(), "dotnet-inspect-generated-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string projectPath = Path.Combine(root, "GeneratedDecompilerFixtures.csproj");
            File.WriteAllText(projectPath, ProjectFile(options.TargetFramework ?? CurrentTargetFramework()));

            for (int i = 0; i < fixtures.Count; i++)
            {
                string sourcePath = Path.Combine(root, $"{i:000}_{SafeFileName(fixtures[i].Id)}.cs");
                File.WriteAllText(sourcePath, fixtures[i].Source);
            }

            Build(root);
            string assemblyPath = Path.Combine(root, "bin", "Release",
                options.TargetFramework ?? CurrentTargetFramework(), "GeneratedDecompilerFixtures.dll");
            if (!File.Exists(assemblyPath))
                throw new InvalidOperationException($"Generated fixture assembly was not produced: {assemblyPath}");

            return run(root, assemblyPath);
        }
        finally
        {
            if (!options.KeepArtifacts)
                TryDelete(root);
        }
    }

    public static string FormatReport(GeneratedFixtureRunResult run)
    {
        var sb = new StringBuilder();
        int fixtureCount = run.Results.Select(r => r.FixtureId).Distinct(StringComparer.Ordinal).Count();
        sb.AppendLine(
            $"GENERATED FIXTURE LADDER over {fixtureCount} fixture(s), {run.Results.Count} target method(s)");
        foreach (var result in run.Results.OrderBy(r => r.FixtureId, StringComparer.Ordinal).ThenBy(r => r.DisplayMember, StringComparer.Ordinal))
        {
            string actual = result.ActualStatus?.ToString() ?? "Missing";
            string shape = result.ExpectedShape is null
                ? ""
                : $"  shape={result.ActualShape?.ToString() ?? "Missing"}  expected-shape={result.ExpectedShape}";
            string frontierShape = result.FrontierShape is null
                ? ""
                : $"  frontier-shape={result.FrontierShape}";
            string frontier = result.IsFrontier ? " frontier" : "";
            string status = result.Passed ? "PASS" : "FAIL";
            sb.AppendLine(
                $"  {status}{frontier}  {result.FixtureId}  {result.DisplayMember}  " +
                $"decompiler={result.DecompilerFidelity}  compile-back={actual}  expected-compile-back={result.ExpectedStatus}{shape}{frontierShape}");
            if (!string.IsNullOrWhiteSpace(result.Detail))
                sb.AppendLine($"      detail: {result.Detail}");
            if (!string.IsNullOrWhiteSpace(result.ShapeDetail))
                sb.AppendLine($"      shape-detail: {result.ShapeDetail}");
            if (!string.IsNullOrWhiteSpace(result.Note))
                sb.AppendLine($"      note: {result.Note}");
        }
        return sb.ToString();
    }

    public static string FormatReturnToSenderCatalogReport(GeneratedFixtureReturnToSenderRunResult run, int maxExamples)
    {
        var sb = new StringBuilder();
        var fixtureRows = run.Results
            .GroupBy(result => result.FixtureId, StringComparer.Ordinal)
            .Select(group =>
            {
                var rows = group.ToArray();
                GeneratedFixtureReturnToSenderStatus status =
                    rows.Any(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail)
                        ? GeneratedFixtureReturnToSenderStatus.Fail
                        : rows.Any(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass)
                            ? GeneratedFixtureReturnToSenderStatus.Pass
                            : GeneratedFixtureReturnToSenderStatus.Skip;
                return new { FixtureId = group.Key, Status = status, Rows = rows };
            })
            .OrderBy(row => row.FixtureId, StringComparer.Ordinal)
            .ToArray();

        int passedFixtures = fixtureRows.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass);
        int skippedFixtures = fixtureRows.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Skip);
        int failedFixtures = fixtureRows.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail);
        int passedTargets = run.Results.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass);
        int skippedTargets = run.Results.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Skip);
        int failedTargets = run.Results.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail);

        sb.AppendLine($"RETURNTOSENDER GENERATED FIXTURE FRONTIER over {fixtureRows.Length} fixture(s), {run.Results.Count} target(s)");
        sb.AppendLine();
        sb.AppendLine("Fixtures:");
        sb.AppendLine($"  Passed : {passedFixtures}");
        sb.AppendLine($"  Skipped: {skippedFixtures}");
        sb.AppendLine($"  Failed : {failedFixtures}");
        sb.AppendLine();
        sb.AppendLine("Targets:");
        sb.AppendLine($"  Passed : {passedTargets}");
        sb.AppendLine($"  Skipped: {skippedTargets}");
        sb.AppendLine($"  Failed : {failedTargets}");

        var skipBuckets = run.Results
            .Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Skip)
            .GroupBy(row => row.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        if (skipBuckets.Length != 0)
        {
            sb.AppendLine();
            sb.AppendLine("Skipped target reasons:");
            foreach (var group in skipBuckets)
                sb.AppendLine($"  {group.Key}: {group.Count()}");
        }

        var failureBuckets = run.Results
            .Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail)
            .GroupBy(row => row.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        if (failureBuckets.Length != 0)
        {
            sb.AppendLine();
            sb.AppendLine("Failed target buckets:");
            foreach (var group in failureBuckets)
                sb.AppendLine($"  {group.Key}: {group.Count()}");
        }

        var failedFixturesToShow = fixtureRows
            .Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail)
            .Take(maxExamples)
            .ToArray();
        if (failedFixturesToShow.Length != 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Failed fixtures (first {failedFixturesToShow.Length} of {failedFixtures}):");
            foreach (var fixture in failedFixturesToShow)
            {
                sb.AppendLine($"  {fixture.FixtureId}");
                foreach (var row in fixture.Rows.Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail))
                {
                    string actual = row.ActualStatus?.ToString() ?? "Missing";
                    sb.AppendLine($"      {row.DisplayMember}  rts={actual}  bucket={row.Reason}");
                    if (!string.IsNullOrWhiteSpace(row.Detail))
                        sb.AppendLine($"      detail: {row.Detail}");
                }
            }
        }

        var passedFixturesToShow = fixtureRows
            .Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass)
            .Take(maxExamples)
            .ToArray();
        if (passedFixturesToShow.Length != 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Passed fixtures (first {passedFixturesToShow.Length} of {passedFixtures}):");
            foreach (var fixture in passedFixturesToShow)
                sb.AppendLine($"  {fixture.FixtureId}");
        }

        return sb.ToString();
    }

    public static string FormatJson(GeneratedFixtureRunResult run)
    {
        var payload = new
        {
            ProjectDirectory = Directory.Exists(run.ProjectDirectory) ? run.ProjectDirectory : null,
            AssemblyPath = File.Exists(run.AssemblyPath) ? run.AssemblyPath : null,
            run.Results,
            run.Passed,
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    public static string FormatReturnToSenderCatalogJson(GeneratedFixtureReturnToSenderRunResult run)
    {
        var payload = new
        {
            ProjectDirectory = Directory.Exists(run.ProjectDirectory) ? run.ProjectDirectory : null,
            AssemblyPath = File.Exists(run.AssemblyPath) ? run.AssemblyPath : null,
            run.Results,
            run.Passed,
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    public static string FormatList(IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GENERATED FIXTURE CATALOG ({fixtures.Count} fixture(s))");
        foreach (var fixture in fixtures.OrderBy(fixture => fixture.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {fixture.Id}  [{string.Join(", ", fixture.Tags)}]");
            foreach (var target in fixture.Targets)
            {
                string frontier = target.IsFrontier ? " frontier" : "";
                string shape = target.ExpectedShape?.ToString() ?? "none";
                string frontierShape = target.FrontierShape is null ? "" : $"  frontier-shape={target.FrontierShape}";
                sb.AppendLine(
                    $"      {target.DisplayMember}  compile-back={target.ExpectedStatus}  shape={shape}{frontierShape}{frontier}");
            }
        }
        return sb.ToString();
    }

    public static string FormatListJson(IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var items = fixtures
            .OrderBy(fixture => fixture.Id, StringComparer.Ordinal)
            .Select(fixture => new
            {
                fixture.Id,
                fixture.Tags,
                Targets = fixture.Targets.Select(target => new
                {
                    target.Type,
                    target.Method,
                    target.Overload,
                    ExpectedStatus = target.ExpectedStatus.ToString(),
                    ExpectedShape = target.ExpectedShape?.ToString(),
                    FrontierShape = target.FrontierShape?.ToString(),
                    target.IsFrontier,
                    target.Note,
                }),
            });
        return JsonSerializer.Serialize(items, s_jsonOptions);
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static string DiagnosticCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "recompile-fail";

        for (int i = 0; i <= detail.Length - 6; i++)
        {
            if (detail[i] == 'C'
                && detail[i + 1] == 'S'
                && char.IsDigit(detail[i + 2])
                && char.IsDigit(detail[i + 3])
                && char.IsDigit(detail[i + 4])
                && char.IsDigit(detail[i + 5]))
            {
                return detail.Substring(i, 6);
            }
        }

        return "recompile-fail";
    }

    static string ProjectFile(string targetFramework) =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>disable</Nullable>
            <LangVersion>preview</LangVersion>
            <IsPackable>false</IsPackable>
            <IsAotCompatible>false</IsAotCompatible>
            <AssemblyName>GeneratedDecompilerFixtures</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    static string CurrentTargetFramework()
    {
        var frameworkName = typeof(GeneratedFixtureRunner).Assembly
            .GetCustomAttributes(typeof(TargetFrameworkAttribute), inherit: false)
            .OfType<TargetFrameworkAttribute>()
            .FirstOrDefault()
            ?.FrameworkName;
        if (frameworkName is null)
            return "net11.0";

        const string prefix = ".NETCoreApp,Version=v";
        return frameworkName.StartsWith(prefix, StringComparison.Ordinal)
            ? "net" + frameworkName[prefix.Length..]
            : "net11.0";
    }

    static string SafeFileName(string id)
    {
        var chars = id.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars);
    }

    static Dictionary<string, GeneratedFixtureRender> DecompilerRenders(
        string assemblyPath,
        IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var renders = new Dictionary<string, GeneratedFixtureRender>(StringComparer.Ordinal);
        using var source = MetadataSource.Open(assemblyPath);
        foreach (var target in fixtures.SelectMany(fixture => fixture.Targets))
        {
            var function = IrImporter.Import(source, target.Type, target.Method, target.Overload);
            if (function is null)
                continue;

            var rendered = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            renders[Key(target.Type, target.Method, target.Overload)] = new(function.Fidelity.ToString(), rendered.Output);
        }

        return renders;
    }

    static (SyntaxKind? ActualShape, string? Detail) ShapeVerdict(string? body, SyntaxKind? expected, SyntaxKind? frontier)
    {
        if (expected is null)
            return (null, null);
        if (string.IsNullOrWhiteSpace(body))
            return (null, "shape-body-missing");

        var tree = CSharpSyntaxTree.ParseText(
            $$"""
            class __GeneratedFixtureShapeShell
            {
                void __M()
                {
            {{Indent(body)}}
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview));
        var root = tree.GetRoot();
        if (frontier is { } frontierKind && root.DescendantNodes().Any(node => node.IsKind(frontierKind)))
            return (frontierKind, "frontier-shape-achieved");

        if (root.DescendantNodes().Any(node => node.IsKind(expected.Value)))
            return (expected.Value, null);

        var observed = root
            .DescendantNodes()
            .Select(node => (SyntaxKind)node.RawKind)
            .FirstOrDefault(kind => s_interestingShapes.Contains(kind));
        return observed == SyntaxKind.None
            ? (null, "shape-not-found")
            : (observed, "shape-not-found");
    }

    static string Indent(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => "        " + line));
    }

    static void Build(string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("Release");
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("--verbosity");
        process.StartInfo.ArgumentList.Add("quiet");
        if (!process.Start())
            throw new InvalidOperationException("Could not start dotnet build for generated fixtures.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw new TimeoutException("Generated fixture build timed out.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Generated fixture build failed with exit code {process.ExitCode}.\n{stdout}\n{stderr}");
    }

    static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
