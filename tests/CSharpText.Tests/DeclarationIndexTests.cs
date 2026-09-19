using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpText.Tests;

/// <summary>
/// Gates <see cref="DeclarationIndex"/> against Roslyn over the real source of every PDB-bearing
/// assembly beside the test binary.
/// </summary>
/// <remarks>
/// Roslyn is the independent oracle, exactly as it is for the parse-validity gate: the product
/// stays Roslyn-free, and a hand-written expectation table would only prove that the index agrees
/// with whatever the index happened to do when the table was written. The index is a lexical scan
/// plus declaration recognition — it is not a parser — so the claim gated here is deliberately the
/// one a lexical scan can own: for each declaration Roslyn reports, the index reports the same
/// kind, the same name, and the same first and last line.
/// </remarks>
public partial class DeclarationIndexTests
{
    [Fact]
    public void LineLimit_StopsLineDenseInputBeforeSplitting()
    {
        var source = new string('\n', 500_000);
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();

        var error = Assert.Throws<CSharpTextComplexityException>(
            () => DeclarationIndex.Build(source));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(500_000, error.Limit);
        Assert.Equal("lines", error.Unit);
        Assert.True(
            allocated < 256 * 1024,
            $"line-limit failure allocated {allocated:N0} bytes before refusing the source");
    }

    [Fact]
    public void CrLfAndCrOnlySource_UseTheSamePhysicalLineModel()
    {
        const string cr = "class C\r{\r    void M() { }\r}";
        const string crlf = "class C\r\n{\r\n    void M() { }\r\n}";

        foreach (var source in new[] { cr, crlf })
        {
            var method = Assert.Single(
                DeclarationIndex.Build(source).FindByName(DeclarationKind.Method, "M"));
            Assert.Equal(3, method.SignatureStartLine);
            Assert.Equal(3, method.EndLine);
        }

        var error = Assert.Throws<CSharpTextComplexityException>(
            () => DeclarationIndex.Build(new string('\r', 500_000)));
        Assert.Equal(500_000, error.Limit);
        Assert.Equal("lines", error.Unit);
    }

    [Fact]
    public void ConditionalGroups_ReportNestedAndEmptyHalfOpenBranches()
    {
        const string source = """
            class C
            {
            #if OUTER
            #if INNER
                int x;
            #else
            #endif
            #elif OTHER
                int y;
            #else
                int z;
            #endif
            }
            """;

        var index = DeclarationIndex.Build(source);

        Assert.Collection(
            index.ConditionalGroups,
            outer =>
            {
                Assert.Equal(0, outer.Id);
                Assert.Equal(-1, outer.ParentGroupId);
                Assert.Equal(3, outer.IfDirectiveLine);
                Assert.Equal(12, outer.EndIfDirectiveLine);
                Assert.Collection(
                    outer.Branches,
                    branch => AssertBranch(branch, 0, 0, 3, 4, 8),
                    branch => AssertBranch(branch, 3, 0, 8, 9, 10),
                    branch => AssertBranch(branch, 4, 0, 10, 11, 12));
            },
            inner =>
            {
                Assert.Equal(1, inner.Id);
                Assert.Equal(0, inner.ParentGroupId);
                Assert.Equal(4, inner.IfDirectiveLine);
                Assert.Equal(7, inner.EndIfDirectiveLine);
                Assert.Collection(
                    inner.Branches,
                    branch => AssertBranch(branch, 1, 1, 4, 5, 6),
                    branch => AssertBranch(branch, 2, 1, 6, 7, 7));
            });

        static void AssertBranch(
            ConditionalBranchSpan branch,
            int id,
            int groupId,
            int directiveLine,
            int contentStartLine,
            int contentEndLineExclusive)
        {
            Assert.Equal(id, branch.Id);
            Assert.Equal(groupId, branch.GroupId);
            Assert.Equal(directiveLine, branch.DirectiveLine);
            Assert.Equal(contentStartLine, branch.ContentStartLine);
            Assert.Equal(contentEndLineExclusive, branch.ContentEndLineExclusive);
        }
    }

    [Fact]
    public void AmbiguousConditionalTopology_WithholdsItsOpenAndLaterGroups()
    {
        const string source = """
            #if OUTER
            /*
            #if HIDDEN
            */
            #endif
            #if LATER
            class C { }
            #endif
            """;

        Assert.Empty(DeclarationIndex.Build(source).ConditionalGroups);
    }

    [Theory]
    [InlineData("#if OUTER\n#if INNER\n#endif")]
    [InlineData("#endif\n#if LATER\n#endif")]
    public void UnclosedOrStrayConditionalTopology_WithholdsAffectedGroups(string source)
    {
        Assert.Empty(DeclarationIndex.Build(source).ConditionalGroups);
    }

    [Fact]
    public void DuplicateElse_WithholdsThatGroupButNotALaterIndependentGroup()
    {
        const string source = """
            #if FIRST
            class A { }
            #else
            class B { }
            #else
            class C { }
            #endif
            #if LATER
            class D { }
            #endif
            """;

        var group = Assert.Single(DeclarationIndex.Build(source).ConditionalGroups);
        Assert.Equal(8, group.IfDirectiveLine);
        Assert.Equal(10, group.EndIfDirectiveLine);
    }

    [Fact]
    public void SelectedConditionalBranch_ProjectsOnlyThatGroupAndPreservesPhysicalLines()
    {
        const string source = """
            class C
            {
            #if FIRST
                void Dead() { }
            #else
                void Live() { }
            #endif
            #if UNKNOWN
                void Maybe() { }
            #endif
            }
            """;
        var index = DeclarationIndex.Build(source);
        var selected = index.ConditionalGroups[0].Branches[1];

        var projected = index.WithSelectedConditionalBranches([selected]);

        var live = Assert.Single(projected.FindByName(DeclarationKind.Method, "Live"));
        Assert.True(live.SpanKnown);
        Assert.Equal(6, live.SignatureStartLine);
        Assert.Empty(projected.FindByName(DeclarationKind.Method, "Dead"));
        Assert.False(
            Assert.Single(projected.FindByName(DeclarationKind.Method, "Maybe")).SpanKnown);
    }

    [Fact]
    public void ConditionalProjection_RejectsABranchFromAnotherIndex()
    {
        const string source = "#if X\nclass A { }\n#else\nclass B { }\n#endif";
        var first = DeclarationIndex.Build(source);
        var second = DeclarationIndex.Build(source);

        Assert.Throws<ArgumentException>(
            () => first.WithSelectedConditionalBranches(
                [second.ConditionalGroups[0].Branches[0]]));
    }

    [Fact]
    public void ConditionalProjection_ManySelectionsAllocateLinearly()
    {
        var smaller = ProjectionFixture(512);
        var larger = ProjectionFixture(1_024);

        _ = smaller.Index.WithSelectedConditionalBranches(smaller.Selected);
        _ = larger.Index.WithSelectedConditionalBranches(larger.Selected);

        long smallAllocation = Measure(smaller);
        long largeAllocation = Measure(larger);

        Assert.True(
            largeAllocation < smallAllocation * 3,
            $"doubling groups changed projection allocation from "
            + $"{smallAllocation:N0} to {largeAllocation:N0} bytes");

        static long Measure(
            (DeclarationIndex Index, ConditionalBranchSpan[] Selected) fixture)
        {
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var projected = fixture.Index.WithSelectedConditionalBranches(fixture.Selected);
            long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(projected);
            return allocation;
        }

        static (DeclarationIndex Index, ConditionalBranchSpan[] Selected) ProjectionFixture(
            int groupCount)
        {
            var source = new StringBuilder("class C\n{\n");
            for (int i = 0; i < groupCount; i++)
            {
                source.Append("#if X\n    void A");
                source.Append(i);
                source.Append("() { }\n#else\n    void B");
                source.Append(i);
                source.Append("() { }\n#endif\n");
            }
            source.Append('}');

            var index = DeclarationIndex.Build(source.ToString());
            return (
                index,
                [.. index.ConditionalGroups.Select(static group => group.Branches[0])]);
        }
    }

    [Theory]
    [InlineData("#line 200")]
    [InlineData("\uFEFF  #line default")]
    public void LineDirective_IsReportedForPdbCorrelationRefusal(string directive)
    {
        var index = DeclarationIndex.Build($"{directive}\nclass C {{ }}");

        Assert.True(index.HasLineDirectives);
    }

    [Theory]
    [InlineData('\u0085')]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    public void UnicodeLineSeparators_UseTheCompilerPhysicalLineModel(char separator)
    {
        string source = "class C\n{\n    string S = @\"a"
            + separator
            + "b\";\n    void M() { }\n}";
        var cancellationToken = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            cancellationToken: cancellationToken);
        Assert.DoesNotContain(
            tree.GetDiagnostics(cancellationToken),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var syntax = Assert.Single(
            tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>());
        int compilerLine = syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        var method = Assert.Single(
            DeclarationIndex.Build(source).FindByName(DeclarationKind.Method, "M"));

        Assert.Equal(5, compilerLine);
        Assert.Equal(compilerLine, method.SignatureStartLine);
        Assert.Equal(compilerLine, method.EndLine);
    }

    [Fact]
    public void ManyInitializerArguments_DoNotRescanTheAccumulatedHeader()
    {
        var source = new StringBuilder("class C { void M() { Register(");
        for (int i = 0; i < 8_000; i++)
            source.Append("new Item { A = ").Append(i).Append(" },");
        source.Append("null); } }");

        var timer = Stopwatch.StartNew();
        _ = DeclarationIndex.Build(source.ToString());
        timer.Stop();

        Assert.True(
            timer.Elapsed < TimeSpan.FromSeconds(5),
            $"indexing 8,000 initializer arguments took {timer.Elapsed}");
    }

    [Fact]
    public void ManyDeclarators_DoNotRescanTheirSharedHeader()
    {
        var source = new StringBuilder("class C { int f0");
        for (int i = 1; i < 16_000; i++)
            source.Append(", f").Append(i);
        source.Append("; void M() { } }");

        var timer = Stopwatch.StartNew();
        var index = DeclarationIndex.Build(source.ToString());
        timer.Stop();

        Assert.Equal(16_002, index.Declarations.Length);
        Assert.True(
            timer.Elapsed < TimeSpan.FromSeconds(5),
            $"indexing one declaration with 16,000 declarators took {timer.Elapsed}");
    }

    [Fact]
    public void ManyUnclosedExtensionScopes_ApplyTrustInOneFinalPass()
    {
        var baselineSource = new StringBuilder("static class S {\n");
        for (int i = 0; i < 80_000; i++)
            baselineSource.Append("int f").Append(i).AppendLine(";");
        baselineSource.AppendLine("}");
        var baselineTimer = Stopwatch.StartNew();
        _ = DeclarationIndex.Build(baselineSource.ToString());
        baselineTimer.Stop();

        var source = new StringBuilder("static class S {\n");
        for (int i = 0; i < 60_000; i++)
            source.AppendLine("extension(){");
        for (int i = 0; i < 80_000; i++)
            source.Append("int f").Append(i).AppendLine(";");

        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var index = DeclarationIndex.Build(source.ToString());
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(80_001, index.Declarations.Length);
        Assert.Equal(60_000, index.TransparentScopes.Length);
        Assert.DoesNotContain(
            index.Declarations,
            declaration => declaration.Kind == DeclarationKind.Field && declaration.SpanKnown);
        Assert.True(
            timer.Elapsed < baselineTimer.Elapsed * 8 + TimeSpan.FromMilliseconds(500),
            $"scope case took {timer.Elapsed} against baseline {baselineTimer.Elapsed}");
        Assert.True(
            allocated < 384L * 1024 * 1024,
            $"indexing allocated {allocated / (1024 * 1024)} MiB");
    }

    [Fact]
    public void ManyFileScopedNamespaces_ReuseOneSuffixSummary()
    {
        var baselineSource = new StringBuilder("class C {\n");
        for (int i = 0; i < 150_000; i++)
            baselineSource.Append("int f").Append(i).AppendLine(";");
        baselineSource.AppendLine("void M() { }");
        baselineSource.AppendLine("}");
        var baselineTimer = Stopwatch.StartNew();
        _ = DeclarationIndex.Build(baselineSource.ToString());
        baselineTimer.Stop();

        var source = new StringBuilder();
        for (int i = 0; i < 150_000; i++)
            source.AppendLine("namespace N;");
        source.AppendLine("class C {");
        source.AppendLine("void M() { }");
        source.AppendLine("}");

        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var index = DeclarationIndex.Build(source.ToString());
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(150_002, index.Declarations.Length);
        Assert.True(
            timer.Elapsed < baselineTimer.Elapsed * 8 + TimeSpan.FromMilliseconds(500),
            $"namespace case took {timer.Elapsed} against baseline {baselineTimer.Elapsed}");
        Assert.True(
            allocated < 384L * 1024 * 1024,
            $"indexing allocated {allocated / (1024 * 1024)} MiB");
    }

    [Fact]
    public void RelationalInitializerChain_DoesNotRescanEachRemainingSuffix()
    {
        var source = new StringBuilder("class C { static dynamic f = a");
        for (int i = 0; i < 32_000; i++)
            source.Append(" < a");
        source.Append("; }");

        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        _ = DeclarationIndex.Build(source.ToString());
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            timer.Elapsed < TimeSpan.FromSeconds(5),
            $"indexing 32,000 relational operators took {timer.Elapsed}");
        Assert.True(
            allocated < 128 * 1024 * 1024,
            $"indexing 32,000 relational operators allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ConditionalInitializerTail_ExaminesEachPendingTokenOnce()
    {
        const int count = 64_000;
        var baselineSource = new StringBuilder("class C {\n");
        for (int i = 0; i < count; i++)
            baselineSource.Append("x ");
        baselineSource.Append('x');
        for (int i = 0; i < count; i++)
            baselineSource.Append(" =");
        baselineSource.AppendLine(";\n}");

        var baselineTimer = Stopwatch.StartNew();
        _ = DeclarationIndex.Build(baselineSource.ToString());
        baselineTimer.Stop();

        var source = new StringBuilder("class C {\n#if X\n");
        for (int i = 0; i < count; i++)
            source.Append("x ");
        source.AppendLine("\n#endif");
        source.Append('x');
        for (int i = 0; i < count; i++)
            source.Append(" =");
        source.AppendLine(";\n}");

        var timer = Stopwatch.StartNew();
        var index = DeclarationIndex.Build(source.ToString());
        timer.Stop();

        Assert.Equal(2, index.Declarations.Length);
        Assert.False(Assert.Single(index.FindByName(DeclarationKind.Field, "x")).SpanKnown);
        Assert.True(
            timer.Elapsed < baselineTimer.Elapsed * 8 + TimeSpan.FromMilliseconds(500),
            $"conditional header took {timer.Elapsed} against baseline {baselineTimer.Elapsed}");
    }

    [Fact]
    public void ConditionalNamespaceChainAndRepeatedTerminators_TraverseEachOutwardEdgeOnce()
    {
        // Each namespace contributes five retained tokens and each terminator contributes three,
        // keeping both sources at 490,000 of the 500,000-token limit and 390,000 physical lines.
        const int depth = 50_000;
        const int terminators = 80_000;
        var baselineSource = new StringBuilder();
        for (int i = 0; i < depth; i++)
            baselineSource.AppendLine("namespace N;\n#if X\n#endif");
        for (int i = 0; i < terminators; i++)
            baselineSource.AppendLine("#if X\n#endif\n;");

        var baselineTimer = Stopwatch.StartNew();
        _ = DeclarationIndex.Build(baselineSource.ToString());
        baselineTimer.Stop();

        var source = new StringBuilder();
        for (int i = 0; i < depth; i++)
            source.AppendLine("#if X\nnamespace N;\n#endif");
        for (int i = 0; i < terminators; i++)
            source.AppendLine("#if X\n#endif\n;");

        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var index = DeclarationIndex.Build(source.ToString());
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(depth, index.Declarations.Length);
        Assert.All(index.Declarations, declaration => Assert.False(declaration.SpanKnown));
        Assert.True(
            timer.Elapsed < baselineTimer.Elapsed * 4 + TimeSpan.FromMilliseconds(500),
            $"conditional namespace chain took {timer.Elapsed} against baseline {baselineTimer.Elapsed}");
        Assert.True(
            allocated < 384L * 1024 * 1024,
            $"indexing allocated {allocated / (1024 * 1024)} MiB");
    }

    [Fact]
    public void ConditionalSiblingFanOut_RefusesEachSiblingOnce()
    {
        // Seven retained tokens and four physical lines per child keep both sources at 490,006
        // tokens and 280,006 lines. The control puts the optional trailer before the empty group,
        // so it performs the same lexical and row work without a terminator reaching backward.
        const int count = 70_000;
        var baselineSource = new StringBuilder("#if A\nclass Owner\n#endif\n{\n");
        for (int i = 0; i < count; i++)
            baselineSource.AppendLine("class T { };\n#if X\n#endif\n");
        baselineSource.AppendLine("}");

        var baselineTimer = Stopwatch.StartNew();
        _ = DeclarationIndex.Build(baselineSource.ToString());
        baselineTimer.Stop();

        var source = new StringBuilder("#if A\nclass Owner\n#endif\n{\n");
        for (int i = 0; i < count; i++)
            source.AppendLine("class T { }\n#if X\n#endif\n;");
        source.AppendLine("}");

        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var index = DeclarationIndex.Build(source.ToString());
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(count + 1, index.Declarations.Length);
        Assert.All(index.Declarations, declaration => Assert.False(declaration.SpanKnown));
        Assert.True(
            timer.Elapsed < baselineTimer.Elapsed * 4 + TimeSpan.FromMilliseconds(500),
            $"conditional sibling fan-out took {timer.Elapsed} against baseline {baselineTimer.Elapsed}");
        Assert.True(
            allocated < 384L * 1024 * 1024,
            $"indexing allocated {allocated / (1024 * 1024)} MiB");
    }

    /// <summary>
    /// Completing a parent's outward walk does not complete its sibling prefix. A later child must
    /// still be refused before the memo stops at that parent; checking the memo first leaves
    /// <c>B</c> incorrectly vouched.
    /// </summary>
    [Fact]
    public void AChildAddedAfterAnOutwardRefusal_IsStillRefused()
    {
        var index = DeclarationIndex.Build("""
            #if X
            class P
            #endif
            {
                class A { }
            #if Y
            #endif
                ;
                class B { }
            #if Z
            #endif
                ;
            }
            """);

        Assert.False(Assert.Single(index.FindByName(DeclarationKind.Class, "A")).SpanKnown);
        Assert.False(Assert.Single(index.FindByName(DeclarationKind.Class, "B")).SpanKnown);
    }

    [Fact]
    public void MemberSlicingCannotAccessLexerInternals()
    {
        Assert.DoesNotContain(
            typeof(DeclarationIndex).Assembly.GetCustomAttributesData(),
            attribute =>
                attribute.AttributeType == typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute)
                && attribute.ConstructorArguments[0].Value is string assemblyName
                && assemblyName.StartsWith(
                    "CSharpText.MemberSlicing",
                    StringComparison.Ordinal));
    }
}
