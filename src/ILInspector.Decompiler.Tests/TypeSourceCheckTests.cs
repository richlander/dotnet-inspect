using System.Reflection.PortableExecutable;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Fixtures for the whole-type source oracle (issue #1112). They drive the pure
/// comparison <see cref="TypeSourceCheck.CompareType"/> with hand-built metadata
/// and composed source, so each artifact class is proven in isolation — and,
/// critically, that a method body the decompiler cannot render does not mask nor
/// manufacture a type-level delta.
/// </summary>
public class TypeSourceCheckTests
{
    static ApiType Type(string? ns, string name, string kind, params ApiMember[] members) =>
        new() { Namespace = ns, Name = name, Kind = kind, Members = [.. members] };

    static ApiMember Member(string name, string kind, string? signature = null) =>
        new() { Name = name, Kind = kind, Signature = signature };

    [Fact]
    public void CleanType_ReportsNoDeltas()
    {
        var type = Type("N", "C", "class", Member("Foo", "method"), Member("Bar", "property"));
        string source = """
            namespace N;
            public class C
            {
                public void Foo() { }
                public int Bar { get; }
            }
            """;

        Assert.Empty(TypeSourceCheck.CompareType(type, source));
    }

    [Fact]
    public void DroppedMember_IsCaught()
    {
        var type = Type("N", "C", "class", Member("Foo", "method"), Member("Bar", "property"));
        // The Bar property is missing from the composed source.
        string source = """
            namespace N;
            public class C
            {
                public void Foo() { }
            }
            """;

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.Contains(deltas, d => d.Kind == TypeSourceCheck.Deltas.MemberMissing
            && d.Detail.Contains("Bar"));
    }

    [Fact]
    public void WrongNamespace_IsCaught()
    {
        var type = Type("Right.Place", "C", "class", Member("Foo", "method"));
        string source = """
            namespace Wrong.Place;
            public class C
            {
                public void Foo() { }
            }
            """;

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.Contains(deltas, d => d.Kind == TypeSourceCheck.Deltas.Namespace);
    }

    [Fact]
    public void DroppedReadonlyModifier_IsCaught()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "S",
            Kind = "struct",
            IsReadOnly = true,
            Members = [Member("Foo", "method")],
        };
        // A readonly struct rendered without the readonly modifier.
        string source = """
            namespace N;
            public struct S
            {
                public void Foo() { }
            }
            """;

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.Contains(deltas, d => d.Kind == TypeSourceCheck.Deltas.ModifierDropped
            && d.Detail == "readonly");
    }

    [Theory]
    [InlineData(false, false, "public struct S")]
    [InlineData(true, false, "public readonly struct S")]
    [InlineData(false, true, "public ref struct S")]
    [InlineData(true, true, "public readonly ref struct S")]
    public void StructModifiers_MatchMetadata(bool isReadOnly, bool isByRefLike, string declaration)
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "S",
            Kind = "struct",
            IsReadOnly = isReadOnly,
            IsByRefLike = isByRefLike,
            Members = [Member("Foo", "method")],
        };
        string source = $$"""
            namespace N;
            {{declaration}}
            {
                public void Foo() { }
            }
            """;

        Assert.Empty(TypeSourceCheck.CompareType(type, source));
    }

    [Fact]
    public void GenericConstraints_MatchMetadata()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C`2",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter { Name = "TKey", Constraints = ["class"] },
                new TypeParameter { Name = "TValue", Constraints = ["System.IDisposable", "new()"] },
            ],
            Members = [Member("Foo", "method")],
        };
        string source = """
            namespace N;
            public class C<TKey, TValue> where TKey : class where TValue : System.IDisposable, new()
            {
                public void Foo() { }
            }
            """;

        Assert.Empty(TypeSourceCheck.CompareType(type, source));
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void EnumUnderlyingType_MatchesMetadata(string underlying)
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "E",
            Kind = "enum",
            EnumUnderlyingType = underlying,
            Members = [new ApiMember { Name = "A", Kind = "field", EnumValue = 0, EnumValueLiteral = "0" }],
        };
        string source = $$"""
            namespace N;
            public enum E : {{underlying}}
            {
                A = 0,
            }
            """;

        Assert.Empty(TypeSourceCheck.CompareType(type, source));
    }

    [Fact]
    public void DroppedEnumUnderlyingType_IsCaught()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "E",
            Kind = "enum",
            EnumUnderlyingType = "long",
            Members = [new ApiMember { Name = "A", Kind = "field", EnumValue = 0, EnumValueLiteral = "0" }],
        };
        string source = """
            namespace N;
            public enum E
            {
                A = 0,
            }
            """;

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.Contains(deltas, d => d.Kind == TypeSourceCheck.Deltas.EnumUnderlyingType
            && d.Detail == "expected 'long', declared 'int'");
    }

    [Fact]
    public void IntBackedEnum_DoesNotRequireExplicitUnderlyingType()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "E",
            Kind = "enum",
            EnumUnderlyingType = "int",
            Members = [new ApiMember { Name = "A", Kind = "field", EnumValue = 0, EnumValueLiteral = "0" }],
        };
        string source = """
            namespace N;
            public enum E
            {
                A = 0,
            }
            """;

        Assert.Empty(TypeSourceCheck.CompareType(type, source));
    }

    [Fact]
    public void DroppedGenericConstraint_IsCaught()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T", Constraints = ["struct"] }],
            Members = [Member("Foo", "method")],
        };
        string source = """
            namespace N;
            public class C<T>
            {
                public void Foo() { }
            }
            """;

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.Contains(deltas, d => d.Kind == TypeSourceCheck.Deltas.GenericConstraintDropped
            && d.Detail == "T : struct");
    }

    [Theory]
    [InlineData("System.ArgIterator", "public ref struct ArgIterator")]
    [InlineData("System.DateTime", "public readonly struct DateTime")]
    [InlineData("System.ReadOnlySpan`1", "public readonly ref struct ReadOnlySpan<T>")]
    public void Evaluate_OnExtractorFixtures_DoesNotReportStructModifierDeltas(string typeName, string declaration)
    {
        string path = typeof(int).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == typeName);
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);
        Assert.Contains(declaration, source);

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.DoesNotContain(deltas, d => d.Kind == TypeSourceCheck.Deltas.ModifierDropped);
    }

    [Fact]
    public void Evaluate_OnExtractorFixture_DoesNotReportGenericConstraintDeltas()
    {
        string path = typeof(int).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == "System.Nullable`1");
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);
        Assert.Contains("where T : struct", source);

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.DoesNotContain(deltas, d => d.Kind == TypeSourceCheck.Deltas.GenericConstraintDropped);
        Assert.DoesNotContain(deltas, d => d.Kind == TypeSourceCheck.Deltas.GenericConstraintExtra);
    }

    [Fact]
    public void Evaluate_OnExtractorFixtures_DoesNotReportEnumUnderlyingTypeDeltas()
    {
        string path = typeof(int).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var api = ApiSurfaceExtractor.Extract(pe);

        var eventKeywords = Assert.Single(api.Types, t => t.FullName == "System.Diagnostics.Tracing.EventKeywords");
        var eventKeywordsSource = TypeSourceComposer.Compose(eventKeywords, path, pdbPath: null);
        Assert.NotNull(eventKeywordsSource);
        Assert.Contains("public enum EventKeywords : long", eventKeywordsSource);
        Assert.DoesNotContain(TypeSourceCheck.CompareType(eventKeywords, eventKeywordsSource),
            d => d.Kind == TypeSourceCheck.Deltas.EnumUnderlyingType);

        var securityRuleSet = Assert.Single(api.Types, t => t.FullName == "System.Security.SecurityRuleSet");
        var securityRuleSetSource = TypeSourceComposer.Compose(securityRuleSet, path, pdbPath: null);
        Assert.NotNull(securityRuleSetSource);
        Assert.Contains("public enum SecurityRuleSet : byte", securityRuleSetSource);
        Assert.DoesNotContain(TypeSourceCheck.CompareType(securityRuleSet, securityRuleSetSource),
            d => d.Kind == TypeSourceCheck.Deltas.EnumUnderlyingType);

        var dayOfWeek = Assert.Single(api.Types, t => t.FullName == "System.DayOfWeek");
        var dayOfWeekSource = TypeSourceComposer.Compose(dayOfWeek, path, pdbPath: null);
        Assert.NotNull(dayOfWeekSource);
        Assert.Contains("public enum DayOfWeek", dayOfWeekSource);
        Assert.DoesNotContain("public enum DayOfWeek : int", dayOfWeekSource);
        Assert.DoesNotContain(TypeSourceCheck.CompareType(dayOfWeek, dayOfWeekSource),
            d => d.Kind == TypeSourceCheck.Deltas.EnumUnderlyingType);
    }

    [Fact]
    public void Evaluate_OnNullableConstraintFixture_RendersRecoveredConstraints()
    {
        string path = typeof(TypeSourceNullableConstraintMatrix<,,,>).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == typeof(TypeSourceNullableConstraintMatrix<,,,>).FullName);
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);

        Assert.Contains("where TNotNull : notnull", source);
        Assert.Contains("where TClassNullable : class?", source);
        Assert.Contains("where TUnmanaged : unmanaged", source);
        Assert.Contains("where TNullableInterface : IDisposable?", source);

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.DoesNotContain(deltas, d => d.Kind == TypeSourceCheck.Deltas.GenericConstraintDropped);
        Assert.DoesNotContain(deltas, d => d.Kind == TypeSourceCheck.Deltas.GenericConstraintExtra);
    }

    [Fact]
    public void Evaluate_OnOperatorFixture_RendersCSharpOperatorDeclarations()
    {
        string path = typeof(TypeSourceOperatorFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == typeof(TypeSourceOperatorFixture).FullName);
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);

        Assert.Contains("public static TypeSourceOperatorFixture operator +(TypeSourceOperatorFixture left, TypeSourceOperatorFixture right)", source);
        Assert.Contains("public static bool operator ==(TypeSourceOperatorFixture left, TypeSourceOperatorFixture right)", source);
        Assert.Contains("public static implicit operator int(TypeSourceOperatorFixture value)", source);
        Assert.Contains("public static explicit operator TypeSourceOperatorFixture(int value)", source);
        Assert.Contains("public static explicit operator long(TypeSourceOperatorFixture value)", source);
        Assert.Contains("public static explicit operator byte(TypeSourceOperatorFixture value)", source);
        Assert.DoesNotContain(" op_", source);

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.DoesNotContain(deltas, d => d.Kind == TypeSourceCheck.Deltas.MemberMissing);
    }

    [Fact]
    public void Evaluate_OnKeywordIdentifierFixture_EscapesTypeSourceIdentifiers()
    {
        string path = typeof(@class<>).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == typeof(@class<>).FullName);
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);

        Assert.Contains("public class @class<@event> where @event : class", source);
        Assert.Contains("public int @delegate;", source);
        Assert.Contains("public @event @return;", source);
        Assert.Contains("public int @void()", source);
        Assert.Contains("=> @delegate;", source);
        Assert.Contains("public int @int { get; set; }", source);
        Assert.DoesNotContain(" class<event>", source);
        Assert.DoesNotContain(" int delegate;", source);

        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(tree.GetDiagnostics(TestContext.Current.CancellationToken),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WrongTypeKind_IsCaught()
    {
        var type = Type("N", "C", "struct", Member("Foo", "method"));
        string source = """
            namespace N;
            public class C
            {
                public void Foo() { }
            }
            """;

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.Contains(deltas, d => d.Kind == TypeSourceCheck.Deltas.TypeKind);
    }

    [Fact]
    public void UnparseableMethodBody_DoesNotMaskSiblingArtifacts()
    {
        // The oracle's reason for existing: a method body the decompiler emits
        // with synthesized names Roslyn cannot parse must neither manufacture a
        // delta of its own nor derail recovery of the *following* constructor.
        // Left unstubbed, the malformed expression body swallows the ctor and
        // the oracle would falsely report a missing constructor.
        var type = Type("N", "C", "class",
            Member("GetEnumerator", "method"),
            Member(".ctor", "constructor"));
        string source = """
            namespace N;
            public class C
            {
                public IEnumerator GetEnumerator() => new <GetEnumerator>d__2(-3) { <>4__this = this };

                public C()
                {
                }
            }
            """;

        Assert.Empty(TypeSourceCheck.CompareType(type, source));
    }

    [Fact]
    public void IndexerProperty_MatchesThisAccessor()
    {
        // Metadata names an indexer Item/Chars; the composer renders this[...].
        var type = Type("N", "C", "class",
            Member("Item", "property", signature: "char this[int index] { get; }"));
        string source = """
            namespace N;
            public class C
            {
                public char this[int index] { get; }
            }
            """;

        Assert.Empty(TypeSourceCheck.CompareType(type, source));
    }

    [Fact]
    public void Evaluate_OnThisAssembly_DoesNotThrow()
    {
        // End-to-end smoke: compose + compare every public type in a real
        // assembly without throwing. The metadata assembly is a stable target.
        string path = typeof(ApiType).Assembly.Location;
        var deltas = TypeSourceCheck.Evaluate(path);
        Assert.NotNull(deltas);
    }
    [Fact]
    public void Evaluate_OnFieldInitializerFixture_RendersInitializersOnDeclarations()
    {
        string path = typeof(TypeSourceFieldInitializerFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == typeof(TypeSourceFieldInitializerFixture).FullName);
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);

        // Field initializers (this.f = value before the base call) are lifted out
        // of the constructor body by the printer and rendered on the declaration,
        // not dropped (#1713).
        Assert.Contains("public readonly int Seed = 5;", source);
        Assert.Contains("public readonly string Label = \"hello\";", source);

        // A field assigned from a constructor parameter is NOT a field initializer
        // (it reads a parameter), so its declaration has no initializer and the
        // assignment stays in the constructor body.
        Assert.Contains("public int Configured;", source);
        Assert.DoesNotContain("public int Configured =", source);
        Assert.Contains("Configured = configured", source);
    }

}

public class TypeSourceNullableConstraintMatrix<TNotNull, TClassNullable, TUnmanaged, TNullableInterface>
    where TNotNull : notnull
    where TClassNullable : class?
    where TUnmanaged : unmanaged
    where TNullableInterface : System.IDisposable?
{
    public int Value => 0;
}

public readonly struct TypeSourceOperatorFixture
{
    public static TypeSourceOperatorFixture operator +(TypeSourceOperatorFixture left, TypeSourceOperatorFixture right) => default;
    public static bool operator ==(TypeSourceOperatorFixture left, TypeSourceOperatorFixture right) => true;
    public static bool operator !=(TypeSourceOperatorFixture left, TypeSourceOperatorFixture right) => false;
    public static implicit operator int(TypeSourceOperatorFixture value) => 0;
    public static explicit operator TypeSourceOperatorFixture(int value) => default;
    public static explicit operator long(TypeSourceOperatorFixture value) => 0;
    public static explicit operator byte(TypeSourceOperatorFixture value) => 0;
    public override bool Equals(object? obj) => obj is TypeSourceOperatorFixture;
    public override int GetHashCode() => 0;
}

public class @class<@event>
    where @event : class
{
    public int @delegate;
    public @event? @return;
    public int @int { get; set; }

    public int @void() => @delegate;
}

public class TypeSourceFieldInitializerFixture
{
    public readonly int Seed = 5;
    public readonly string Label = "hello";
    public int Configured;

    public TypeSourceFieldInitializerFixture(int configured)
    {
        Configured = configured;
    }
}
