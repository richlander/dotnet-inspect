using System.Reflection.PortableExecutable;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

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
}
