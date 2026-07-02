using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Slice 1 of docs/design/value-typed-emission.md: the coercion decision lives in
// one place (Coerce / TryCoerceEnumOperand / EnumConstantText), not per render
// branch. These tests pin the two behavior deltas of the consolidation:
//
// 1. Guard drift fixed: the binary/comparison enum-operand sites used
//    TypeFamilies.IsInteger, which admits bool (I4 stack family), so an enum
//    met by a bool operand rendered `(E)(x == y)` — CS0030. The shared operand
//    coercion composes the truthiness spelling instead: `(E)(x == y ? 1 : 0)`.
//    csc never emits this shape; it is verifiable IL (bool and enum share I4),
//    so the fixtures are synthetic IR.
// 2. Member naming is part of the one name-or-cast rule: an un-retyped integer
//    constant at a known-enum sink renders the member name when one matches,
//    where the sink previously open-coded the cast.
public class CoerceChokePointTests
{
    [Fact]
    public void EnumComparedToBool_ComposesTruthinessWithEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var inner = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(1, "x", intType),
            new LoadArgument(2, "y", intType));
        var outer = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "flags", enumType),
            inner);
        string body = RenderReturn(
            outer,
            TypeRef.CoreLib("System", "Boolean"),
            [new Parameter("flags", enumType), new Parameter("x", intType), new Parameter("y", intType)],
            enumType);

        Assert.Contains("(Tiny)(x == y ? 1 : 0)", body);
        Assert.DoesNotContain("(Tiny)(x == y)", body);
        AssertCompiles("public static bool M(Tiny flags, int x, int y)", body, "public enum Tiny { }");
    }

    [Fact]
    public void EnumBitwiseWithBool_ComposesTruthinessWithEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var inner = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(1, "x", intType),
            new LoadArgument(2, "y", intType));
        var and = new Binary(
            BinaryKind.And,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "flags", enumType),
            inner);
        string body = RenderReturn(
            and,
            enumType,
            [new Parameter("flags", enumType), new Parameter("x", intType), new Parameter("y", intType)],
            enumType);

        Assert.Contains("flags & (Tiny)(x == y ? 1 : 0)", body);
        Assert.DoesNotContain("& (Tiny)(x == y)", body);
        AssertCompiles("public static Tiny M(Tiny flags, int x, int y)", body, "public enum Tiny { }");
    }

    [Fact]
    public void BoolConditionalArm_AtEnumMergedType_ComposesTruthinessWithEnumCast()
    {
        // A conditional whose join is enum-typed but whose true arm is a raw
        // comparison result: the bool arm cannot render bare (CS0029) or as a
        // direct enum cast (CS0030); it composes.
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(1, "x", intType),
                new LoadArgument(2, "y", intType)),
            new LoadArgument(3, "e", enumType))
        {
            MergedType = enumType,
        };
        string body = RenderReturn(
            conditional,
            enumType,
            [
                new Parameter("c", boolType),
                new Parameter("x", intType),
                new Parameter("y", intType),
                new Parameter("e", enumType),
            ],
            enumType);

        Assert.Contains("(Tiny)(x == y ? 1 : 0)", body);
        AssertCompiles("public static Tiny M(bool c, int x, int y, Tiny e)", body, "public enum Tiny { }");
    }

    [Fact]
    public void UnretypedNamedConstant_AtKnownEnumSink_RendersMemberName()
    {
        // A long-payload constant TypedConstantsPass (int-only) never retyped:
        // the sink coercion still spells the member name, not `(LEnum)2`.
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        string body = RenderReturn(
            new Constant(2L, longType),
            enumType,
            [],
            enumType,
            underlying: longType,
            members: new Dictionary<long, string> { [2] = "High" });

        Assert.Contains("return LEnum.High;", body);
        Assert.DoesNotContain("(LEnum)2", body);
        AssertCompiles("public static LEnum M()", body, "public enum LEnum : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void UnretypedUnnamedConstant_AtKnownEnumSink_KeepsOverflowAwareCast()
    {
        // The cast half of the name-or-cast rule is unchanged: no matching
        // member falls back to the overflow-aware enum cast.
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        string body = RenderReturn(
            new Constant(7L, longType),
            enumType,
            [],
            enumType,
            underlying: longType,
            members: new Dictionary<long, string> { [2] = "High" });

        Assert.Contains("return (LEnum)7;", body);
        AssertCompiles("public static LEnum M()", body, "public enum LEnum : long { Low = 0, High = 2 }");
    }

    static string RenderReturn(
        IrExpression value,
        TypeRef returnType,
        IReadOnlyList<Parameter> parameters,
        TypeRef enumType,
        TypeRef? underlying = null,
        IReadOnlyDictionary<long, string>? members = null)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = underlying is null
                ? new Dictionary<TypeRef, TypeRef>()
                : new Dictionary<TypeRef, TypeRef> { [enumType] = underlying },
            EnumMembers = members is null
                ? new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()
                : new Dictionary<TypeRef, IReadOnlyDictionary<long, string>> { [enumType] = members },
        };

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
