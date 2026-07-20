using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public sealed class CSharpPrinterReceiverTests
{
    static readonly TypeRef Int32Type = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef VoidType = TypeRef.CoreLib("System", "Void");
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

    [Fact]
    public void StaticCall_OnCrossType_WhenTypeQualifierIsShadowed_UsesGlobalAlias()
    {
        var other = TypeRef.Definition("synthetic", "", "TypeNameShadow");
        var call = new Call(
            new MethodRef(other, "M", Int32Type, [Int32Type], HasThis: false),
            isVirtual: false,
            [new LoadArgument(0, "M", Int32Type)]);

        string body = RenderReturn(
            call,
            Int32Type,
            [
                new Parameter("M", Int32Type),
                new Parameter("TypeNameShadow", Int32Type),
            ]);

        Assert.Contains("return global::TypeNameShadow.M(M);", body);
        Assert.DoesNotContain("return TypeNameShadow.M(M);", body);
        AssertCompiles(
            "public static int Uses(int M, int TypeNameShadow)",
            body,
            "public static class TypeNameShadow { public static int M(int value) => value + 1; }");
    }

    [Fact]
    public void StaticCall_OnCrossType_WhenTypeQualifierIsShadowedByLocal_UsesGlobalAlias()
    {
        var other = TypeRef.Definition("synthetic", "", "TypeNameShadow");
        var call = new Call(
            new MethodRef(other, "M", Int32Type, [Int32Type], HasThis: false),
            isVirtual: false,
            [new Constant(1, Int32Type)]);

        string body = RenderWithLocal(
            [Int32Type],
            ["TypeNameShadow"],
            new StoreLocal(0, Int32Type, new Constant(0, Int32Type)),
            new Return(call));

        Assert.Contains("return global::TypeNameShadow.M(1);", body);
        Assert.DoesNotContain("return TypeNameShadow.M(1);", body);
        AssertCompiles(
            "public static int UsesLocal()",
            body,
            "public static class TypeNameShadow { public static int M(int value) => value + 1; }");
    }

    [Fact]
    public void StaticCall_OnNamespacedKeywordType_WhenTypeQualifierIsShadowed_EscapesNamespace()
    {
        var other = TypeRef.Definition("synthetic", "event.Models", "TypeNameShadow");
        var call = new Call(
            new MethodRef(other, "M", Int32Type, [Int32Type], HasThis: false),
            isVirtual: false,
            [new LoadArgument(0, "M", Int32Type)]);

        string body = RenderReturn(
            call,
            Int32Type,
            [
                new Parameter("M", Int32Type),
                new Parameter("TypeNameShadow", Int32Type),
            ]);

        Assert.Contains("return global::@event.Models.TypeNameShadow.M(M);", body);
        Assert.DoesNotContain("return global::event.Models.TypeNameShadow.M(M);", body);
        AssertCompiles(
            "public static int Uses(int M, int TypeNameShadow)",
            body,
            "namespace @event.Models { public static class TypeNameShadow { public static int M(int value) => value + 1; } }");
    }

    [Fact]
    public void EnumMember_OnCrossType_WhenTypeQualifierIsShadowed_UsesGlobalAlias()
    {
        var color = TypeRef.Definition("synthetic", "", "Color");
        string body = RenderReturn(
            new Constant(1, color),
            color,
            [new Parameter("Color", color)],
            enumMembers: new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [color] = new Dictionary<long, string> { [1] = "Red" },
            });

        Assert.Contains("return global::Color.Red;", body);
        Assert.DoesNotContain("return Color.Red;", body);
        AssertCompiles(
            "public static Color Uses(Color Color)",
            body,
            "public enum Color { Red = 1 }");
    }

    [Fact]
    public void UnboxReceiver_InstanceCall_SpellsUnsafeUnbox()
    {
        // #2916: an instance call on an unboxed value must reach the in-box
        // place, so the receiver spells as the Unsafe.Unbox<T>(o) intrinsic
        // (a `ref T`). The bare cast `((S)o).Read()` calls on a copy — it reads
        // the same value but silently drops any mutation.
        var s = TypeRef.Definition("synthetic", "", "S");
        var call = new Call(
            new MethodRef(s, "Read", Int32Type, [], HasThis: true),
            isVirtual: false,
            [new Unbox(s, new LoadArgument(0, "o", ObjectType))]);

        string body = RenderReturn(call, Int32Type, [new Parameter("o", ObjectType)]);

        Assert.Contains("Unsafe.Unbox<S>(o).Read()", body);
        Assert.DoesNotContain("((S)o)", body);
        AssertCompiles(
            "public static int M(object o)",
            body,
            "public struct S { public int Read() => 0; }");
    }

    [Fact]
    public void UnboxReceiver_FieldAssignment_SpellsUnsafeUnbox()
    {
        // #2916: assigning through an unboxed value's field must target the
        // in-box place. The bare cast `((S)o).X = 5` is CS0445 (cannot modify an
        // unboxing result); `Unsafe.Unbox<S>(o).X = 5` is a valid, faithful
        // `unbox; stfld`.
        var s = TypeRef.Definition("synthetic", "", "S");
        var store = new StoreField(
            new FieldRef(s, "X", Int32Type),
            new Unbox(s, new LoadArgument(0, "o", ObjectType)),
            new Constant(5, Int32Type));

        string body = RenderStatements([new Parameter("o", ObjectType)], store);

        Assert.Contains("Unsafe.Unbox<S>(o).X = 5;", body);
        Assert.DoesNotContain("((S)o).X = 5", body);
        AssertCompiles(
            "public static void M(object o)",
            body,
            "public struct S { public int X; }");
    }

    [Fact]
    public void UnboxReceiver_Nullable_KeepsCastNotUnsafeUnbox()
    {
        // Regression: Unsafe.Unbox<T> constrains T to a non-nullable value type
        // (`where T : struct`), so a Nullable<T> receiver must NOT route through
        // the intrinsic (CS0453). Nullable<T> is immutable, so the value-copy
        // cast `((int?)o).GetValueOrDefault()` is exact and compiles.
        var nullableInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Nullable`1"), [Int32Type]);
        var call = new Call(
            new MethodRef(nullableInt, "GetValueOrDefault", Int32Type, [], HasThis: true),
            isVirtual: false,
            [new Unbox(nullableInt, new LoadArgument(0, "o", ObjectType))]);

        string body = RenderReturn(call, Int32Type, [new Parameter("o", ObjectType)]);

        Assert.Contains(").GetValueOrDefault()", body);
        Assert.DoesNotContain("Unsafe.Unbox", body);
        AssertCompiles("public static int M(object o)", body);
    }

    [Fact]
    public void UnboxReceiver_GenericParameter_KeepsCastNotUnsafeUnbox()
    {
        // Regression: Unsafe.Unbox<T> requires `where T : struct`, which an
        // (unconstrained) open generic parameter does not satisfy (CS0453). A
        // generic-parameter unbox receiver keeps the value-copy cast `((T)o)`.
        var t = TypeRef.MethodGenericParameter(0, "T");
        var call = new Call(
            new MethodRef(ObjectType, "GetHashCode", Int32Type, [], HasThis: true),
            isVirtual: false,
            [new Unbox(t, new LoadArgument(0, "o", ObjectType))]);

        string body = RenderReturn(call, Int32Type, [new Parameter("o", ObjectType)]);

        Assert.Contains("((T)o).GetHashCode()", body);
        Assert.DoesNotContain("Unsafe.Unbox", body);
        AssertCompiles("public static int M<T>(object o)", body);
    }

    [Fact]
    public void UnboxReceiver_KnownReferenceType_KeepsCast()
    {
        // Defensive: `unbox` of a reference type is malformed IL, but if one
        // reaches the printer the receiver must not spell `Unsafe.Unbox<C>` — C is
        // not a struct (CS0453). When the resolver knows the target is a reference
        // type, the value-position receiver keeps the compiling cast `((C)o)`.
        var c = TypeRef.Definition("synthetic", "", "C");
        var call = new Call(
            new MethodRef(c, "M", Int32Type, [], HasThis: true),
            isVirtual: false,
            [new Unbox(c, new LoadArgument(0, "o", ObjectType))]);

        string body = RenderReturn(
            call,
            Int32Type,
            [new Parameter("o", ObjectType)],
            typeShapes: new Dictionary<TypeRef, TypeShape> { [c] = TypeShape.Reference });

        Assert.Contains("((C)o).M()", body);
        Assert.DoesNotContain("Unsafe.Unbox", body);
        AssertCompiles(
            "public static int M(object o)",
            body,
            "public class C { public int M() => 0; }");
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

    static string RenderStatements(IReadOnlyList<Parameter> parameters, params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(VoidType, [.. parameters], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderReturn(
        IrExpression value,
        TypeRef returnType,
        IReadOnlyList<Parameter>? parameters = null,
        IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>>? enumMembers = null,
        IReadOnlyDictionary<TypeRef, TypeShape>? typeShapes = null)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var parameterList = parameters is null ? ImmutableArray<Parameter>.Empty : [.. parameters];
        var signature = new MethodSignature(returnType, parameterList, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            EnumMembers = enumMembers ?? ImmutableDictionary<TypeRef, IReadOnlyDictionary<long, string>>.Empty,
            TypeShapes = typeShapes ?? ImmutableDictionary<TypeRef, TypeShape>.Empty,
        };
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderWithLocal(IReadOnlyList<TypeRef> locals, IReadOnlyList<string?> localNames, params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32Type, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [.. locals], container)
        {
            LocalNames = [.. localNames],
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
        => RoslynTestReferences.TrustedPlatform;
}
