using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// Locks the <see cref="ApiAnalysisInspection.SameType"/> matching used to scope
/// render rows (and, since #2233, the type-targeted decode gate) to a single
/// type. The regression under test (#2238): the old predicate normalized nested
/// names by blindly replacing '+' with '.', which dropped rows for a
/// <em>non-nested</em> type whose metadata name literally contains '+'.
/// </summary>
public class ApiOutputFormatterTests
{
    const string Asm = "PlusType";

    static ApiType Type(string? ns, string name, string? metadataName)
        => new() { Namespace = ns, Name = name, MetadataName = metadataName };

    // --- SameType: deterministic unit coverage (no external tooling) ---

    [Fact]
    public void SameType_NonNestedTypeWithLiteralPlus_Matches()
    {
        // Raw IL permits '+' in a type identifier (`.class public 'A+B'`). Such a
        // type's metadata name is "A+B" on both the analysis TypeRef and the API
        // surface. The old '+'→'.' replace turned the surface name into "A.B" and
        // silently dropped the type; MetadataName restores the match.
        var apiType = Type(ns: null, name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.Definition(Asm, "", "A+B");

        Assert.True(ApiAnalysisInspection.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_NestedType_StillMatches()
    {
        // A genuinely nested type: analysis TypeRef uses the metadata '+'
        // separator (Outer+Inner); the API surface display name uses '.'
        // (Outer.Inner). MetadataName carries the '+' form so they reconcile.
        var apiType = Type(ns: "N", name: "Outer.Inner", metadataName: "Outer+Inner");
        var typeRef = TypeRef.Definition(Asm, "N", "Outer+Inner");

        Assert.True(ApiAnalysisInspection.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_FallbackWhenMetadataNameAbsent_UsesReplace()
    {
        // Older serialized surfaces carry no MetadataName; the predicate falls
        // back to the legacy '+'→'.' reconciliation so nested types still match.
        var apiType = Type(ns: "N", name: "Outer.Inner", metadataName: null);
        var typeRef = TypeRef.Definition(Asm, "N", "Outer+Inner");

        Assert.True(ApiAnalysisInspection.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_GlobalNamespace_MatchesNullSurfaceNamespace()
    {
        // The API surface stores the global namespace as null; a TypeRef stores
        // it as "". The predicate must treat these as equivalent.
        var apiType = Type(ns: null, name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.Definition(Asm, "", "A+B");

        Assert.True(ApiAnalysisInspection.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_DifferentNamespace_DoesNotMatch()
    {
        var apiType = Type(ns: "N1", name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.Definition(Asm, "N2", "A+B");

        Assert.False(ApiAnalysisInspection.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_NonDefinitionTypeRef_DoesNotMatch()
    {
        var apiType = Type(ns: null, name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.SzArray(TypeRef.Definition(Asm, "", "A+B"));

        Assert.False(ApiAnalysisInspection.SameType(typeRef, apiType));
    }

    // The taste side comment rides the signature line, so it has to survive every
    // declaration suffix and body shape the formatter can emit (#3191).

    [Fact]
    public void FormatSourceWithDeclaration_TrailingComment_RidesTheSignatureLine()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var method = new ApiMember
        {
            Name = "Read",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Read", ReturnType = "System.Int32" }
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            method,
            methodGenericParameters: null,
            DecompilerResult.Success("return this._count;"),
            declarationTrailingComment: "taste.qualify-field-access(_count)");

        var lines = source.ReplaceLineEndings("\n").Split('\n');
        Assert.EndsWith("  // taste.qualify-field-access(_count)", lines[0]);
        Assert.Equal("{", lines[1]);
        // The body is untouched source: the comment never leaks into it.
        Assert.Contains("    return this._count;", lines);
    }

    [Fact]
    public void FormatSourceWithDeclaration_TrailingComment_FollowsTheConstructorChain()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature { MemberName = "#ctor" }
        };
        var result = DecompilerResult.Success("return;") with { ConstructorChain = "base(42)" };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            constructor,
            methodGenericParameters: null,
            result,
            declarationTrailingComment: "taste.prefer-conditional-return(fidelity=byte-divergent)");

        var first = source.ReplaceLineEndings("\n").Split('\n')[0];
        Assert.EndsWith(
            ": base(42)  // taste.prefer-conditional-return(fidelity=byte-divergent)",
            first);
    }

    [Fact]
    public void FormatSourceWithDeclaration_TrailingComment_FollowsAnExpressionBodyTerminator()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var method = new ApiMember
        {
            Name = "Read",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Read", ReturnType = "System.Int32" }
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            method,
            methodGenericParameters: null,
            DecompilerResult.Success("return this._count;"),
            preferExpressionBodied: true,
            declarationTrailingComment: "taste.qualify-field-access(_count)");

        // After the ';', never inside the expression.
        Assert.EndsWith(";  // taste.qualify-field-access(_count)", source);
    }

    [Fact]
    public void FormatSourceWithDeclaration_NoTrailingComment_IsUnchanged()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var method = new ApiMember
        {
            Name = "Read",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Read", ReturnType = "System.Int32" }
        };
        var result = DecompilerResult.Success("return this._count;");

        Assert.Equal(
            ApiOutputFormatter.FormatSourceWithDeclaration(type, method, null, result),
            ApiOutputFormatter.FormatSourceWithDeclaration(
                type, method, null, result, declarationTrailingComment: null));
    }

    [Fact]
    public void FormatSourceWithDeclaration_NoDeclaration_KeepsTheCommentAsOneLeadingLine()
    {
        // A member the formatter cannot spell a declaration for still gets the
        // signal, on one line above the body rather than dropped on the floor.
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var method = new ApiMember
        {
            Name = "Read",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Read" }
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            method,
            methodGenericParameters: null,
            DecompilerResult.Success("return this._count;"),
            declarationTrailingComment: "taste.qualify-field-access(_count)");

        var lines = source.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("// taste.qualify-field-access(_count)", lines[0]);
        Assert.Equal("return this._count;", lines[1]);
    }

    [Fact]
    public void FormatSourceWithDeclaration_UsesTypedConstructorChain()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature { MemberName = "#ctor" }
        };
        var result = DecompilerResult.Success("return;") with
        {
            ConstructorChain = "base(42)"
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            constructor,
            methodGenericParameters: null,
            result);

        Assert.Contains("Widget() : base(42)", source.ReplaceLineEndings("\n").Split('\n')[0]);
        Assert.DoesNotContain("    : base(42)", source);
    }

    [Fact]
    public void FormatSourceWithDeclaration_DoesNotParseConstructorChainFromBodyText()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature { MemberName = "#ctor" }
        };
        var result = DecompilerResult.Success(": base(42)\nreturn;");

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            constructor,
            methodGenericParameters: null,
            result);
        var lines = source.ReplaceLineEndings("\n").Split('\n');

        Assert.DoesNotContain(": base(42)", lines[0]);
        Assert.Contains("    : base(42)", lines);
    }

    [Fact]
    public void FormatSourceWithDeclaration_AllowsInitializerOnlyConstructorBody()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature { MemberName = "#ctor" }
        };
        var result = DecompilerResult.Success("") with
        {
            FieldInitializers = [("Value", "42")]
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            constructor,
            methodGenericParameters: null,
            result);

        Assert.StartsWith("public Widget()", source);
        Assert.DoesNotContain("Value = 42", source);
        Assert.DoesNotContain(DiagnosticIds.EmptyOutput, source);
    }

    [Fact]
    public void FormatSourceWithDeclaration_SingleSwitchReturn_RendersExpressionBodied()
    {
        // #3088: a member whose only statement is a multi-line
        // `return <switch>;` renders expression-bodied. The block lines keep
        // their column-zero body indent under the column-zero declaration.
        var type = new ApiType { Namespace = "Samples", Name = "Shapes", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Area",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Area", ReturnType = "System.Int32" }
        };
        var result = DecompilerResult.Success(
            "return shape switch\n{\n    string s => s.Length,\n    int[] a => a.Length,\n    _ => 0,\n};") with
        {
            BodyIsSingleExpressionBody = true
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            member,
            methodGenericParameters: null,
            result,
            preferExpressionBodied: true)
            .ReplaceLineEndings("\n");

        Assert.EndsWith(" => shape switch", Declaration(source));
        Assert.Contains("\n{\n    string s => s.Length,\n    int[] a => a.Length,\n    _ => 0,\n};", source);
        Assert.EndsWith("};", source.TrimEnd());
        Assert.DoesNotContain("return shape switch", source);
    }

    [Fact]
    public void FormatSourceWithDeclaration_SwitchReturn_StaysBlockWhenSignalNotSet()
    {
        // Same multi-line switch-return body, but the printer did not prove it a
        // single-return expression (e.g. a statement precedes it), so it must
        // keep the brace-block body.
        var type = new ApiType { Namespace = "Samples", Name = "Shapes", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Area",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Area", ReturnType = "System.Int32" }
        };
        var result = DecompilerResult.Success(
            "return shape switch\n{\n    string s => s.Length,\n    int[] a => a.Length,\n    _ => 0,\n};") with
        {
            BodyIsSingleExpressionBody = false
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            member,
            methodGenericParameters: null,
            result,
            preferExpressionBodied: true)
            .ReplaceLineEndings("\n");

        Assert.DoesNotContain("=>", Declaration(source));
        Assert.Contains("return shape switch", source);
    }

    [Fact]
    public void FormatSourceWithDeclaration_SingleFluentReturn_RendersExpressionBodied()
    {
        // #3084: the single-return expression-body fold is not switch-specific.
        // A member whose only statement is a multi-line `return <fluent chain>;`
        // renders expression-bodied too — the chain receiver trails the arrow and
        // the chained calls keep their column-zero body indent under the
        // column-zero declaration.
        var type = new ApiType { Namespace = "Samples", Name = "Builder", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Build",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Build", ReturnType = "System.String" }
        };
        var result = DecompilerResult.Success(
            "return builder\n    .Append(\"a\")\n    .Append(\"b\")\n    .ToString();") with
        {
            BodyIsSingleExpressionBody = true
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            member,
            methodGenericParameters: null,
            result,
            preferExpressionBodied: true)
            .ReplaceLineEndings("\n");

        Assert.EndsWith(" => builder", Declaration(source));
        Assert.Contains("\n    .Append(\"a\")\n    .Append(\"b\")\n    .ToString();", source);
        Assert.EndsWith(".ToString();", source.TrimEnd());
        Assert.DoesNotContain("return builder", source);
    }

    [Fact]
    public void FormatSourceWithDeclaration_SingleVoidFluentExpressionStatement_RendersExpressionBodied()
    {
        // #3084 (this slice): the fold is not return-specific. A void member whose
        // only statement is a multi-line expression statement (a wrapped fluent
        // chain, no `return`) renders expression-bodied too — the whole first line
        // trails the arrow and the chained calls keep their column-zero body indent
        // under the column-zero declaration.
        var type = new ApiType { Namespace = "Samples", Name = "Builder", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Build",
            Kind = "method",
            SignatureModel = new ApiSignature { MemberName = "Build", ReturnType = "System.Void" }
        };
        var result = DecompilerResult.Success(
            "builder\n    .Append(\"a\")\n    .Append(\"b\")\n    .Clear();") with
        {
            BodyIsSingleExpressionBody = true
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            member,
            methodGenericParameters: null,
            result,
            preferExpressionBodied: true)
            .ReplaceLineEndings("\n");

        Assert.EndsWith(" => builder", Declaration(source));
        Assert.Contains("\n    .Append(\"a\")\n    .Append(\"b\")\n    .Clear();", source);
        Assert.EndsWith(".Clear();", source.TrimEnd());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormatSourceWithDeclaration_UsesBodyAsyncMetadata(
        bool requiresAsyncBodyModifier)
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                MemberName = "Run",
                ReturnType = "System.Threading.Tasks.Task"
            }
        };
        var result = DecompilerResult.Success("Console.WriteLine(\"await\");");

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            member,
            methodGenericParameters: null,
            result,
            requiresAsyncBodyModifier: requiresAsyncBodyModifier);
        var declaration = source.ReplaceLineEndings("\n").Split('\n')[0];

        Assert.Equal(requiresAsyncBodyModifier, declaration.Contains(" async ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormatSourceWithDeclaration_UsesTypedUnsafeBodyFact(
        bool requiresUnsafeBodyModifier)
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = Method("Run");
        var result = DecompilerResult.Success("return;") with
        {
            RequiresUnsafeBodyModifier = requiresUnsafeBodyModifier
        };

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            member,
            methodGenericParameters: null,
            result);
        var declaration = Declaration(source);

        Assert.Equal(requiresUnsafeBodyModifier, declaration.Contains(" unsafe ", StringComparison.Ordinal));
        Assert.False(member.IsUnsafe);
    }

    [Fact]
    public void FormatSourceWithDeclaration_PreservesObsoleteAttribute()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = Method("Run");
        member.IsObsolete = true;

        var source = ApiOutputFormatter.FormatSourceWithDeclaration(
            type,
            member,
            methodGenericParameters: null,
            DecompilerResult.Success("return;"));

        Assert.StartsWith("[Obsolete] public", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PopulateCSharpSections_PreservesOverlayFailureDiagnostics()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = Method("Run");
        var code = new MemberCodeProvider.Item(
            DecompiledResult: null,
            MethodGenericParameters: null,
            AnnotatedResult: DecompilerResult.Failure(DiagnosticIds.ContextUnavailable, "annotated failure"),
            CostOverlayResult: DecompilerResult.Failure(DiagnosticIds.UnsupportedConstruct, "cost failure"),
            CostOverlayHeaderComments: null,
            SemanticsOverlayResult: DecompilerResult.Failure(DiagnosticIds.UnsupportedType, "semantics failure"),
            ILText: null,
            ILDiagnostic: null,
            Attributes: null);
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(sections, type, member, code));
        Assert.Equal("// DEC0002: annotated failure", sections.AnnotatedSourceCode.Content);
        Assert.Equal("// DEC0004: cost failure", sections.CostOverlayCode.Content);
        Assert.Equal("// DEC0005: semantics failure", sections.SemanticsOverlayCode.Content);
    }

    [Fact]
    public void PopulateCSharpSections_AppliesBodyModifierFactsToAllOverlays()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = Method("Run");
        var result = DecompilerResult.Success("await Task.Yield();") with
        {
            RequiresUnsafeBodyModifier = true
        };
        var code = new MemberCodeProvider.Item(
            DecompiledResult: null,
            MethodGenericParameters: null,
            AnnotatedResult: result,
            CostOverlayResult: result,
            CostOverlayHeaderComments: ["// cost evidence"],
            SemanticsOverlayResult: result,
            ILText: null,
            ILDiagnostic: null,
            Attributes: null,
            RequiresAsyncBodyModifier: true);
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(sections, type, member, code));
        Assert.Contains(" async ", Declaration(sections.AnnotatedSourceCode.Content));
        Assert.Contains(" unsafe ", Declaration(sections.AnnotatedSourceCode.Content));
        Assert.Contains(" async ", Declaration(sections.CostOverlayCode.Content));
        Assert.Contains(" unsafe ", Declaration(sections.CostOverlayCode.Content));
        Assert.Contains("// cost evidence", sections.CostOverlayCode.Content);
        Assert.Contains(" async ", Declaration(sections.SemanticsOverlayCode.Content));
        Assert.Contains(" unsafe ", Declaration(sections.SemanticsOverlayCode.Content));
    }

    [Fact]
    public void MinimalSummary_Finalizer_PopulatesFinalizerSummarySection()
    {
        // Regression guard: in the compact (Minimal) view the finalizer must land
        // in its own Finalizer summary section, not be silently dropped like it
        // was before it had a dedicated kind/section.
        var type = new ApiType
        {
            Name = "Handle",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = ".ctor", Kind = "constructor", Signature = "void .ctor()" },
                new ApiMember { Name = "Finalize", Kind = "finalizer", Signature = "void Finalize()", IsFinalizer = true },
            ]
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSummarySections(
            view, new MethodGroupsView(), new EventsView(), type, new ApiOptions());

        Assert.NotNull(view.FinalizerSummaryRows);
        var row = Assert.Single(view.FinalizerSummaryRows!);
        Assert.Equal("Finalize", row.Name);
    }

    [Fact]
    public void ShapeView_Finalizer_RendersDestructorSpellingInFinalizerGroup()
    {
        var type = new ApiType
        {
            Name = "Handle",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = ".ctor", Kind = "constructor", Signature = "void .ctor()" },
                new ApiMember { Name = "Finalize", Kind = "finalizer", Signature = "void Finalize()", IsFinalizer = true },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, memberFilter: []);
        var finalizerNode = Assert.Single(view.Members, n => n.Text.StartsWith("Finalizer", System.StringComparison.Ordinal));
        var child = Assert.Single(finalizerNode.Children!);
        Assert.Equal("~Handle()", child.Text);
        // The raw metadata signature must never leak into the shape.
        Assert.DoesNotContain(view.Members, n => n.Children?.Any(c => c.Text.Contains("Finalize", System.StringComparison.Ordinal)) == true);
    }

    [Fact]
    public void ShapeView_FinalizerOnTypeNestedInGeneric_SpellsInnermostSegment()
    {
        // Regression guard (adversarial review): the destructor spelling must
        // isolate the innermost nested-type segment before stripping generic
        // arity. A finalizer on a type nested inside a generic outer carries a
        // dotted metadata name like "Outer`1.Nested"; stripping the backtick
        // first would truncate to "~Outer()".
        var type = new ApiType
        {
            Name = "GenericOuter`1.Nested",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = "Finalize", Kind = "finalizer", Signature = "void Finalize()", IsFinalizer = true },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, memberFilter: []);
        var finalizerNode = Assert.Single(view.Members, n => n.Text.StartsWith("Finalizer", System.StringComparison.Ordinal));
        var child = Assert.Single(finalizerNode.Children!);
        Assert.Equal("~Nested()", child.Text);
    }

    [Fact]
    public void TableView_Finalizer_BlanksReturnTypeSoVoidFinalizeNeverReconstructs()
    {
        // Regression guard (adversarial review): the table must blank the
        // finalizer return type (symmetric with constructors), otherwise the
        // Kind/Name/ReturnType columns visually reconstruct "void Finalize()".
        var type = new ApiType
        {
            Name = "Handle",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = "Finalize", Kind = "finalizer", Signature = "void Finalize()", ReturnType = "void", IsFinalizer = true },
            ]
        };

        var (view, _) = ApiOutputFormatter.BuildTypeTableView(type, new ApiOptions());
        var row = Assert.Single(view.Rows!, r => r.Kind.Contains("finalizer", System.StringComparison.Ordinal));
        Assert.Equal("", row.ReturnType);
    }

    [Fact]
    public void ApiTypeJson_OmitsIsFinalizerOnNonFinalizers_KeepsItTrueOnFinalizer()
    {
        // Regression guard (adversarial review of #3168): the finalizer identity
        // is already carried by the dedicated Kind = "finalizer". Serializing
        // is_finalizer: false on every other member is redundant schema noise.
        // The ApiType JSON contexts default to WhenWritingNull (not
        // WhenWritingDefault), so without a property-level [JsonIgnore] the
        // false bool leaks onto every member. Assert it is omitted for a plain
        // member and still present (true) for the finalizer.
        var type = new ApiType
        {
            Name = "Handle",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = "Work", Kind = "method", Signature = "void Work()" },
                new ApiMember { Name = "Finalize", Kind = "finalizer", Signature = "void Finalize()", IsFinalizer = true },
            ]
        };

        string json = System.Text.Json.JsonSerializer.Serialize(type, ApiTypeJsonContext.Default.ApiType);

        // The non-finalizer member must not carry the redundant false bool...
        Assert.DoesNotContain("\"is_finalizer\": false", json, System.StringComparison.Ordinal);
        // ...while the finalizer keeps its true marker.
        Assert.Contains("\"is_finalizer\": true", json, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Real-artifact canary for issue #3664. The single-member decompiled-source
    /// view supplies its own generic-parameter names, which makes the ApiSignature
    /// renderer decline; the text path it falls back to used to drop the `where`
    /// clauses, so a constrained generic method rendered as C# that no longer
    /// compiles. Runs against this test assembly's own compiled metadata, so it
    /// pins the whole chain — constraint decoding, the member view, and the
    /// whole-type listing — rather than a hand-built signature model.
    /// </summary>
    [Fact]
    public void ConstrainedGenericMethod_KeepsConstraintsInBothDecompiledViews()
    {
        string path = typeof(ConstraintFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(ConstraintFixture).FullName);
        var member = Assert.Single(type.Members, candidate => candidate.Name == nameof(ConstraintFixture.Compare));
        var collected = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            path,
            overloadIndex: 0,
            new MemberCodeProvider.Request(
                DecompiledSource: true,
                AnnotatedSource: false,
                CostOverlay: false,
                SemanticsOverlay: false,
                IL: false,
                Attributes: false,
                Calls: false,
                Callers: false,
                CallGraph: false,
                UnsafeOperations: false)));
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(sections, type, member, collected.Code));
        Assert.Contains(
            "where T : System.IComparable<T>",
            sections.DecompiledSourceCode.Content,
            StringComparison.Ordinal);

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains("where T : IComparable<T>", typeSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same chain must drop the parts of an inherited constraint that C# forbids
    /// restating: `Run&lt;T&gt;` is declared `where T : class, new()`, and `new()` is
    /// CS0460 on an override while the bare `class` is the permitted carve-out.
    /// </summary>
    [Fact]
    public void ConstrainedGenericOverride_OmitsTheConstraintsCSharpForbidsRestating()
    {
        string path = typeof(ConstraintFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(ConstraintFixture).FullName);
        var member = Assert.Single(type.Members, candidate => candidate.Name == nameof(ConstraintFixture.Run));
        var collected = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            path,
            overloadIndex: 0,
            new MemberCodeProvider.Request(
                DecompiledSource: true,
                AnnotatedSource: false,
                CostOverlay: false,
                SemanticsOverlay: false,
                IL: false,
                Attributes: false,
                Calls: false,
                Callers: false,
                CallGraph: false,
                UnsafeOperations: false)));
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(sections, type, member, collected.Code));
        Assert.Contains("void Run<T>", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);
        Assert.Contains("where T : class", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("new()", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains("void Run<T>", typeSource, StringComparison.Ordinal);
        Assert.Contains("where T : class", typeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class, new()", typeSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every row of the restatement table, through the real chain over real compiled
    /// metadata. An override may not repeat its inherited constraints (CS0460) but must
    /// restate exactly one fact about each type parameter, because that fact decides
    /// whether `T?` binds as a nullable reference type or as Nullable&lt;T&gt;. Omitting
    /// it is CS0453/CS0115/CS0534; naming the wrong one is CS8822 or CS8665.
    /// </summary>
    /// <remarks>
    /// The rows cannot be told apart by constraint spelling, which is why this runs
    /// against compiler-produced metadata rather than a hand-built model: `Enumish` and
    /// `Named` are both class constraints, and `Interface` and `Named` are both named
    /// type constraints, yet each pair needs opposite restatements. This is the gate for
    /// <c>TypeParameterKindClassifier</c>; the writer's reduction from the classified
    /// kind is gated separately by
    /// <c>CSharpDeclarationWriterTests.OverrideGenericMethod_RestatesWhatTheClassifiedKindRequires</c>.
    /// </remarks>
    [Theory]
    // No constraint, and constraints that prove nothing about T being a reference or
    // value type. `default` is required: omitting the clause does not compile.
    [InlineData(nameof(RestatementRowFixture.None), "default")]
    [InlineData(nameof(RestatementRowFixture.NotNull), "default")]
    [InlineData(nameof(RestatementRowFixture.Ctor), "default")]
    // An interface constraint proves nothing either -- T may still be a struct.
    [InlineData(nameof(RestatementRowFixture.Interface), "default")]
    // System.Enum is the trap: it is a class, but it is one of the three base types
    // that does not make T known to be a reference type, so `class` here is CS8665.
    [InlineData(nameof(RestatementRowFixture.Enumish), "default")]
    // Any other named class constraint does make T known to be a reference type.
    [InlineData(nameof(RestatementRowFixture.Named), "class")]
    // `where T : U` inherits U's answer, so the chain has to be followed rather than
    // treated as unknowable: U is class-constrained here and unconstrained below.
    [InlineData(nameof(RestatementRowFixture.Transitive), "class")]
    [InlineData(nameof(RestatementRowFixture.OpenChain), "default")]
    public void ConstraintRestatement_MatchesWhatCSharpRequires(string memberName, string expected)
    {
        string path = typeof(RestatementRowFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(RestatementRowFixture).FullName);
        var member = Assert.Single(type.Members, candidate => candidate.Name == memberName);
        var collected = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            path,
            overloadIndex: 0,
            new MemberCodeProvider.Request(
                DecompiledSource: true,
                AnnotatedSource: false,
                CostOverlay: false,
                SemanticsOverlay: false,
                IL: false,
                Attributes: false,
                Calls: false,
                Callers: false,
                CallGraph: false,
                UnsafeOperations: false)));
        var sections = new MemberCodeView();

        // A second type parameter exists only on the rows that need one to constrain T.
        string signature = memberName is nameof(RestatementRowFixture.Transitive)
            or nameof(RestatementRowFixture.OpenChain)
            ? $"{memberName}<T, U>(T? value) where T : {expected} where U : {expected}"
            : $"{memberName}<T>(T? value) where T : {expected}";

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(sections, type, member, collected.Code));
        Assert.Contains(signature, sections.DecompiledSourceCode.Content, StringComparison.Ordinal);

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains(signature, typeSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three core types that prove nothing about a type parameter are recognized by
    /// typed identity, never by display name. An assembly may declare its own
    /// <c>System.Enum</c>, and a parameter constrained to that type IS known to be a
    /// reference type -- so treating the impostor as the real one would emit
    /// <c>where T : default</c> and produce CS8822 rather than merely incomplete output.
    /// </summary>
    /// <remarks>
    /// Non-vacuous: dropping the resolution-scope check from the classifier's
    /// <c>TypeReference</c> arm classifies this parameter as
    /// <see cref="TypeParameterTypeKind.NeitherReferenceNorValue"/> and fails this test.
    /// The metadata is synthesized because the attack needs a second assembly that
    /// declares <c>System.Enum</c> without a core-library strong name, which a compiled
    /// in-repo fixture cannot express.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_RejectsACoreLibraryLookalike()
    {
        string dllPath = EmitCoreLibraryLookalikeSample();
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var surface = ApiSurfaceExtractor.Extract(pe);
            var type = Assert.Single(surface.Types, candidate => candidate.Name == "LookalikeSample");
            var member = Assert.Single(type.Members, candidate => candidate.Name == "Pick");
            var typeParameter = Assert.Single(member.SignatureModel!.TypeParameters);

            // The constraint really is a TypeReference spelled `System.Enum`, so the
            // display name alone would have matched.
            Assert.Contains(
                typeParameter.StructuredConstraints ?? [],
                constraint => constraint.Value.Contains("Enum", StringComparison.Ordinal));

            Assert.Equal(TypeParameterTypeKind.Undetermined, typeParameter.TypeKind);
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// The gate for the same-module half of core-type identity. An ordinary assembly may
    /// declare its own <c>System.Enum</c>; that type is a plain class, so a parameter
    /// constrained to it IS known to be a reference type and the override must restate
    /// <c>class</c>. Matching the name alone yields <c>default</c>, which is CS8822.
    /// </summary>
    [Fact]
    public void ConstraintRestatement_RejectsASameModuleCoreLibraryLookalike()
    {
        string dllPath = EmitSameModuleLookalikeSample();
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var surface = ApiSurfaceExtractor.Extract(pe);
            var type = Assert.Single(surface.Types, candidate => candidate.Name == "SameModuleLookalikeSample");
            var member = Assert.Single(type.Members, candidate => candidate.Name == "Pick");
            var typeParameter = Assert.Single(member.SignatureModel!.TypeParameters);

            // The constraint really is spelled `System.Enum`, and it really is a
            // same-module TypeDefinition, so neither the name nor the handle kind
            // distinguishes it from the core library's.
            Assert.Contains(
                typeParameter.StructuredConstraints ?? [],
                constraint => constraint.Value.Contains("Enum", StringComparison.Ordinal));

            Assert.Equal(TypeParameterTypeKind.ReferenceType, typeParameter.TypeKind);
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// The gate for reconvergence. Two `where T : U` branches that meet at the same
    /// parameter must both be answered: the shared parameter is not on the second
    /// branch's path, so treating it as a cycle would drop a clause C# requires
    /// (CS0115/CS0534 on the override).
    /// </summary>
    /// <remarks>
    /// Two mechanisms in <c>TypeParameterKindClassifier.ClassifySibling</c> are each
    /// independently sufficient here -- releasing a parameter from the path once its own
    /// subtree is done, and reusing an answer already reached -- so this fails only when
    /// both are absent, which is the state it was written against. The long-chain gate
    /// below isolates the answer cache.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_AnswersAReconvergingConstraintChain()
    {
        string dllPath = EmitConstraintChainSample(
            static parameters =>
            {
                parameters[0].SetInterfaceConstraints(parameters[1], parameters[2]);
                parameters[1].SetInterfaceConstraints(parameters[3]);
                parameters[2].SetInterfaceConstraints(parameters[3]);
            },
            ["T", "A", "B", "X"]);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var surface = ApiSurfaceExtractor.Extract(pe);
            var type = Assert.Single(surface.Types, candidate => candidate.Name == "ChainSample");
            var member = Assert.Single(type.Members, candidate => candidate.Name == "Pick");
            var typeParameter = member.SignatureModel!.TypeParameters[0];

            Assert.Equal(TypeParameterTypeKind.NeitherReferenceNorValue, typeParameter.TypeKind);
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// The gate for memoization. Following `where T : U` without reusing answers
    /// reclassifies the whole remaining chain from every parameter, which is quadratic:
    /// a 4,000-parameter chain measured over twenty seconds before answers were cached.
    /// The bound is loose enough that only the missing cache can trip it -- the cached
    /// walk finishes in milliseconds.
    /// </summary>
    [Fact]
    public void ConstraintRestatement_ClassifiesALongConstraintChainWithoutRewalkingIt()
    {
        const int Length = 4000;
        string[] names = [.. Enumerable.Range(0, Length).Select(index => $"T{index}")];
        string dllPath = EmitConstraintChainSample(
            static parameters =>
            {
                for (int index = 0; index < parameters.Length - 1; index++)
                    parameters[index].SetInterfaceConstraints(parameters[index + 1]);
            },
            names);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var surface = ApiSurfaceExtractor.Extract(pe);
            stopwatch.Stop();

            var type = Assert.Single(surface.Types, candidate => candidate.Name == "ChainSample");
            var member = Assert.Single(type.Members, candidate => candidate.Name == "Pick");
            Assert.Equal(Length, member.SignatureModel!.TypeParameters.Count);
            Assert.All(
                member.SignatureModel.TypeParameters,
                typeParameter => Assert.Equal(TypeParameterTypeKind.NeitherReferenceNorValue, typeParameter.TypeKind));

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Classifying a {Length}-parameter constraint chain took {stopwatch.Elapsed}.");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// A parameter whose only route to a proof runs through a cycle still gets the proof.
    /// Both parameters here are reference types, and the cycle between them is incidental
    /// to that; an answer that came out different depending on which parameter was asked
    /// first would drop a <c>class</c> clause C# requires (CS0115/CS0534).
    /// </summary>
    /// <remarks>
    /// The shape: <c>T1 : T2</c>, <c>T2 : T1, T4</c>, <c>T3 : T1</c>, <c>T4 : class</c>.
    /// T1 reaches T4 by way of T2, so T1 is a reference type; T3's only route is through
    /// T1, so T3 is one too. This was the counterexample that retired a design in which
    /// meeting the <c>T2 : T1</c> edge made T1's answer belong to the route rather than
    /// to T1 -- T3 then inherited a verdict that was never about it.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_AnswersEveryParameterOnARouteThroughACycle()
    {
        string dllPath = EmitConstraintChainSample(
            static parameters =>
            {
                parameters[3].SetGenericParameterAttributes(GenericParameterAttributes.ReferenceTypeConstraint);
                parameters[0].SetInterfaceConstraints(parameters[1]);
                parameters[1].SetInterfaceConstraints(parameters[0], parameters[3]);
                parameters[2].SetInterfaceConstraints(parameters[0]);
            },
            ["T1", "T2", "T3", "T4"],
            onType: true);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var surface = ApiSurfaceExtractor.Extract(pe);
            var type = Assert.Single(surface.Types, candidate => candidate.Name.StartsWith("ChainSample", StringComparison.Ordinal));
            var typeParameters = type.TypeParameters;

            // T1 is a reference type by way of T2 -> T4, despite the T2 -> T1 cycle.
            Assert.Equal(TypeParameterTypeKind.ReferenceType, typeParameters[0].TypeKind);

            // T3's only route is through T1, so it must reach the same answer rather than
            // one that belonged to the route T1 was reached by.
            Assert.Equal(TypeParameterTypeKind.ReferenceType, typeParameters[2].TypeKind);
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// A parameter list that is one long cycle. Nothing in it is knowable, and finding
    /// that out has to cost about what reading the list costs -- a cycle is the shape that
    /// invites re-deriving the same parameters once per parameter, which is quadratic and
    /// reachable from malformed metadata.
    /// </summary>
    [Fact]
    public void ConstraintRestatement_ResolvesALongCyclicConstraintChain()
    {
        const int Length = 4000;
        string[] names = [.. Enumerable.Range(0, Length).Select(index => $"T{index}")];
        string dllPath = EmitConstraintChainSample(
            static parameters =>
            {
                // Every parameter constrained to the next, and the last back to the first.
                for (int index = 0; index < parameters.Length; index++)
                    parameters[index].SetInterfaceConstraints(parameters[(index + 1) % parameters.Length]);
            },
            names,
            onType: true);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var surface = ApiSurfaceExtractor.Extract(pe);
            stopwatch.Stop();

            var type = Assert.Single(surface.Types, candidate => candidate.Name.StartsWith("ChainSample", StringComparison.Ordinal));
            Assert.Equal(Length, type.TypeParameters.Count);

            // Nothing is knowable from a chain that only leads back to itself.
            Assert.All(
                type.TypeParameters,
                typeParameter => Assert.Equal(TypeParameterTypeKind.Undetermined, typeParameter.TypeKind));

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Classifying a {Length}-parameter cyclic chain took {stopwatch.Elapsed}.");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// The same bound, through the other consumer. <c>MetadataDeclarationQuery</c>
    /// classifies parameters independently of <c>ApiSurfaceExtractor</c>, so it needs its
    /// own proof that it shares one chain state across a parameter list rather than
    /// allocating one per parameter, which rewalks the chain's whole tail.
    /// </summary>
    [Fact]
    public void ConstraintRestatement_ClassifiesALongChainWithoutRewalkingItInDeclarationQuery()
    {
        const int Length = 4000;
        string[] names = [.. Enumerable.Range(0, Length).Select(index => $"T{index}")];
        string dllPath = EmitConstraintChainSample(
            static parameters =>
            {
                for (int index = 0; index < parameters.Length - 1; index++)
                    parameters[index].SetInterfaceConstraints(parameters[index + 1]);
            },
            names,
            onType: true);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var reader = pe.GetMetadataReader();
            var typeDefinition = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(candidate => reader.GetString(candidate.Name) == "ChainSample");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var typeParameters = MetadataDeclarationQuery.GetTypeParameters(reader, typeDefinition);
            stopwatch.Stop();

            Assert.Equal(Length, typeParameters.Count);
            Assert.All(
                typeParameters,
                typeParameter => Assert.Equal(TypeParameterTypeKind.NeitherReferenceNorValue, typeParameter.TypeKind));

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Reading a {Length}-parameter constraint chain took {stopwatch.Elapsed}.");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// Emits one assembly declaring its own <c>System.Enum</c> alongside a generic
    /// virtual method constrained to it, so the constraint is a same-module
    /// TypeDefinition rather than a cross-assembly TypeReference.
    /// </summary>
    static string EmitSameModuleLookalikeSample()
    {
        var ab = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName("SameModuleLookalikeEmit"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("SameModuleLookalikeEmit");
        var impostor = module.DefineType(
            "System.Enum",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.Abstract
                | System.Reflection.TypeAttributes.Class);
        var tb = module.DefineType(
            "SameModuleLookalikeSample",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
        var mb = tb.DefineMethod(
            "Pick",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Virtual);
        var typeParameters = mb.DefineGenericParameters("T");
        typeParameters[0].SetBaseTypeConstraint(impostor);
        mb.SetReturnType(typeParameters[0]);
        mb.SetParameters(typeParameters[0]);
        var il = mb.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        impostor.CreateType();
        tb.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"same-module-lookalike-{Guid.NewGuid():N}.dll");
        ab.Save(path);
        return path;
    }

    /// <summary>
    /// Emits a generic virtual method whose type parameters are wired to each other by
    /// <paramref name="constrain"/>, which is how `where T : U` chains -- absent from
    /// every assembly measured, but expressible -- reach the classifier.
    /// </summary>
    /// <summary>
    /// The gate on resolving the constraint graph without recursion. A chain this long
    /// exhausts the call stack when each link is a stack frame -- measured at roughly
    /// 21,000 frames, which a 30,000-link chain passes -- and the process dies rather
    /// than answering. Nothing about such a chain is invalid: it is acyclic, every link
    /// is readable, and the proof at the far end is real, so the answer is required to
    /// arrive.
    /// </summary>
    /// <remarks>
    /// Asserting the proof reaches every link, rather than merely that the call returns,
    /// is what keeps this from passing on a depth-limited walk that fails closed instead
    /// of overflowing. A limit would answer <c>Undetermined</c> here and drop 30,000
    /// required clauses.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_ResolvesADeepConstraintChainWithoutRecursion()
    {
        const int Length = 30_000;
        string[] names = [.. Enumerable.Range(0, Length).Select(index => $"T{index}")];
        string dllPath = EmitConstraintChainSample(
            static parameters =>
            {
                for (int index = 0; index < parameters.Length - 1; index++)
                    parameters[index].SetInterfaceConstraints(parameters[index + 1]);

                // The one witness, as far from the start of the chain as it can be.
                parameters[^1].SetGenericParameterAttributes(
                    System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint);
            },
            names,
            onType: true);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var surface = ApiSurfaceExtractor.Extract(pe);

            var type = Assert.Single(surface.Types, candidate => candidate.Name.StartsWith("ChainSample", StringComparison.Ordinal));
            Assert.Equal(Length, type.TypeParameters.Count);

            // Every link inherits the far end's answer, however far away it is.
            Assert.All(
                type.TypeParameters,
                typeParameter => Assert.Equal(TypeParameterTypeKind.ReferenceType, typeParameter.TypeKind));
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// A proof that sits past a cycle still arrives. Parameters here reach both a cycle
    /// and, further on, a parameter constrained to <c>class</c>; the cycle is genuinely
    /// unanswerable, and the proof is genuinely a proof, so the two must not contaminate
    /// each other.
    /// </summary>
    /// <remarks>
    /// The shape, from adversarial review: <c>TCycle : TCycle</c>, then a fan of
    /// <c>Ti : TCycle, Ti+1, Ti+2</c>, with <c>T0 : T1, TClass</c> and
    /// <c>TClass : class</c>. A design that treats meeting a cycle as a reason to
    /// distrust everything computed around it answers <c>T0</c> as <c>Undetermined</c>
    /// and drops its required clause; one that re-derives the fan for every path through
    /// it does not finish. Both were real behaviors of the walk this replaced, which is
    /// why the assertions below pin the answer and the time together.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_ProvesAReferenceTypeReachedPastACycle()
    {
        const int Depth = 30;

        // T0 .. T31, then TCycle, then TClass.
        string[] names =
        [
            .. Enumerable.Range(0, Depth + 2).Select(index => $"T{index}"),
            "TCycle",
            "TClass",
        ];
        string dllPath = EmitConstraintChainSample(
            static parameters =>
            {
                var cycle = parameters[^2];
                var proof = parameters[^1];
                cycle.SetInterfaceConstraints(cycle);
                proof.SetGenericParameterAttributes(
                    System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint);

                for (int index = 1; index < Depth; index++)
                    parameters[index].SetInterfaceConstraints(cycle, parameters[index + 1], parameters[index + 2]);

                parameters[0].SetInterfaceConstraints(parameters[1], proof);
            },
            names,
            onType: true);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var surface = ApiSurfaceExtractor.Extract(pe);
            stopwatch.Stop();

            var type = Assert.Single(surface.Types, candidate => candidate.Name.StartsWith("ChainSample", StringComparison.Ordinal));
            var byName = type.TypeParameters.ToDictionary(typeParameter => typeParameter.Name);

            // The proof is reached, past the cycle every parameter between also reaches.
            Assert.Equal(TypeParameterTypeKind.ReferenceType, byName["T0"].TypeKind);
            Assert.Equal(TypeParameterTypeKind.ReferenceType, byName["TClass"].TypeKind);

            // The cycle itself remains unanswerable, and says nothing about anything else.
            Assert.Equal(TypeParameterTypeKind.Undetermined, byName["TCycle"].TypeKind);

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Classifying a {names.Length}-parameter graph around a cycle took {stopwatch.Elapsed}.");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// Many declarations, each with its own cyclic parameter list. Resolution is per
    /// declaration, so a module pays for each one; this pins that the per-declaration
    /// cost stays proportional to that declaration rather than to a fixed allowance that
    /// each list is free to spend in full.
    /// </summary>
    [Fact]
    public void ConstraintRestatement_ResolvesManyCyclicListsWithoutPerListWaste()
    {
        const int Lists = 512;
        const int Length = 317;
        string dllPath = EmitManyCyclicListsSample(Lists, Length);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var surface = ApiSurfaceExtractor.Extract(pe);
            stopwatch.Stop();

            var types = surface.Types
                .Where(candidate => candidate.Name.StartsWith("Many", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(Lists, types.Count);
            Assert.All(
                types,
                type => Assert.All(
                    type.TypeParameters,
                    typeParameter => Assert.Equal(TypeParameterTypeKind.Undetermined, typeParameter.TypeKind)));

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Classifying {Lists} cyclic lists of {Length} parameters took {stopwatch.Elapsed}.");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    static string EmitConstraintChainSample(
        Action<System.Reflection.Emit.GenericTypeParameterBuilder[]> constrain,
        string[] names,
        bool onType = false)
    {
        var ab = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName("ChainEmit"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("ChainEmit");
        var tb = module.DefineType(
            "ChainSample",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);

        if (onType)
        {
            constrain(tb.DefineGenericParameters(names));
        }
        else
        {
            var mb = tb.DefineMethod(
                "Pick",
                System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Virtual);
            var typeParameters = mb.DefineGenericParameters(names);
            constrain(typeParameters);
            mb.SetReturnType(typeParameters[0]);
            mb.SetParameters(typeParameters[0]);
            var il = mb.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
        }

        tb.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"chain-{Guid.NewGuid():N}.dll");
        ab.Save(path);
        return path;
    }

    /// <summary>
    /// Emits one module holding <paramref name="lists"/> generic types, each with its own
    /// self-contained cycle of <paramref name="length"/> type parameters.
    /// </summary>
    static string EmitManyCyclicListsSample(int lists, int length)
    {
        var ab = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName("ManyEmit"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("ManyEmit");
        string[] names = [.. Enumerable.Range(0, length).Select(index => $"T{index}")];

        for (int list = 0; list < lists; list++)
        {
            var tb = module.DefineType(
                $"Many{list}",
                System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
            var parameters = tb.DefineGenericParameters(names);
            for (int index = 0; index < parameters.Length; index++)
                parameters[index].SetInterfaceConstraints(parameters[(index + 1) % parameters.Length]);

            tb.CreateType();
        }

        string path = Path.Combine(Path.GetTempPath(), $"many-{Guid.NewGuid():N}.dll");
        ab.Save(path);
        return path;
    }

    /// <summary>
    /// Emits two assemblies: one declaring a <c>System.Enum</c> that is not the core
    /// library's, and one whose generic virtual method is constrained to it. Returns the
    /// path of the second.
    /// </summary>
    static string EmitCoreLibraryLookalikeSample()
    {
        var fakeCore = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName($"FakeCoreLib{Guid.NewGuid():N}"), typeof(object).Assembly);
        var fakeModule = fakeCore.DefineDynamicModule("FakeCoreLib");
        fakeModule.DefineType(
            "System.Enum",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.Abstract
                | System.Reflection.TypeAttributes.Class)
            .CreateType();
        string fakePath = Path.Combine(Path.GetTempPath(), $"fake-corelib-{Guid.NewGuid():N}.dll");
        fakeCore.Save(fakePath);

        var impostor = System.Reflection.Assembly.LoadFrom(fakePath).GetType("System.Enum")!;

        var ab = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName("LookalikeEmit"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("LookalikeEmit");
        var tb = module.DefineType(
            "LookalikeSample",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
        var mb = tb.DefineMethod(
            "Pick",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Virtual);
        var typeParameters = mb.DefineGenericParameters("T");
        typeParameters[0].SetBaseTypeConstraint(impostor);
        mb.SetReturnType(typeParameters[0]);
        mb.SetParameters(typeParameters[0]);
        var il = mb.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        tb.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"lookalike-{Guid.NewGuid():N}.dll");
        ab.Save(path);
        return path;
    }

    /// <summary>
    /// The same chain must reduce, not drop, the constraints an override inherits.
    /// C# allows exactly a bare `class` or `struct` to be restated, and that carve-out
    /// decides how `T?` binds: without it `T?` becomes Nullable&lt;T&gt; and the render
    /// stops compiling (CS0453/CS0115). Real compiled metadata records the constraint
    /// as `class?`, which is itself CS0460, so this also pins the normalization.
    /// </summary>
    [Fact]
    public void ConstrainedGenericOverride_RestatesTheNullabilityDecidingConstraint()
    {
        string path = typeof(ConstraintFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(ConstraintFixture).FullName);
        var member = Assert.Single(type.Members, candidate => candidate.Name == nameof(ConstraintFixture.Pick));
        var collected = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            path,
            overloadIndex: 0,
            new MemberCodeProvider.Request(
                DecompiledSource: true,
                AnnotatedSource: false,
                CostOverlay: false,
                SemanticsOverlay: false,
                IL: false,
                Attributes: false,
                Calls: false,
                Callers: false,
                CallGraph: false,
                UnsafeOperations: false)));
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(sections, type, member, collected.Code));
        Assert.Contains("where T : class", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);
        // The annotated spelling metadata records is itself CS0460 on an override.
        Assert.DoesNotContain("class?", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains("where T : class", typeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class?", typeSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// An explicit interface implementation reaches the text fallback by a second route:
    /// the signature-model path renders only <c>method</c>, so this kind falls through it
    /// even in the whole-type view, where no caller supplies a generic-parameter list.
    /// The recovery has to fire on that route too — the rendered parameter is spelled
    /// <c>Nullable&lt;T&gt;</c>, which is not even legal without the <c>struct</c> clause.
    /// </summary>
    [Fact]
    public void ConstrainedGenericExplicitImplementation_KeepsItsConstraintInTheWholeTypeView()
    {
        string path = typeof(ConstraintFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(ConstraintFixture).FullName);

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains("Wrap<T>", typeSource, StringComparison.Ordinal);
        Assert.Contains("where T : struct", typeSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeAsyncBodyConsumers_UseResolvedMethodModifier(
        bool invalidateMetadataToken)
    {
        string path = typeof(RuntimeAsyncHeaderFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(RuntimeAsyncHeaderFixture).FullName);
        var member = Assert.Single(type.Members, candidate => candidate.Name == nameof(RuntimeAsyncHeaderFixture.YieldAsync));
        if (invalidateMetadataToken)
            member.MetadataToken = 0x02000001;
        var collected = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            path,
            overloadIndex: 0,
            new MemberCodeProvider.Request(
                DecompiledSource: true,
                AnnotatedSource: false,
                CostOverlay: false,
                SemanticsOverlay: false,
                IL: false,
                Attributes: false,
                Calls: false,
                Callers: false,
                CallGraph: false,
                UnsafeOperations: false)));
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(
            sections,
            type,
            member,
            collected.Code));
        Assert.Contains(
            "public static async System.Threading.Tasks.Task<int> YieldAsync",
            sections.DecompiledSourceCode.Content,
            StringComparison.Ordinal);
        Assert.Contains("await Task.Yield();", sections.DecompiledSourceCode.Content);
        Assert.DoesNotContain("AsyncHelpers", sections.DecompiledSourceCode.Content);

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains(
            "public static async Task<int> YieldAsync",
            typeSource,
            StringComparison.Ordinal);
        Assert.Contains("await Task.Yield();", typeSource);
        Assert.DoesNotContain("AsyncHelpers", typeSource);
    }

    [Fact]
    public void UnsafeBodyConsumers_UseTypedBodyModifier()
    {
        string path = typeof(RuntimeAsyncHeaderFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(RuntimeAsyncHeaderFixture).FullName);
        var member = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(RuntimeAsyncHeaderFixture.ReadAddress));
        Assert.False(member.IsUnsafe);

        var collected = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            path,
            overloadIndex: 0,
            new MemberCodeProvider.Request(
                DecompiledSource: true,
                AnnotatedSource: false,
                CostOverlay: false,
                SemanticsOverlay: false,
                IL: false,
                Attributes: false,
                Calls: false,
                Callers: false,
                CallGraph: false,
                UnsafeOperations: false)));
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateCSharpSections(
            sections,
            type,
            member,
            collected.Code));
        Assert.Contains(
            "public static unsafe int ReadAddress",
            sections.DecompiledSourceCode.Content,
            StringComparison.Ordinal);

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains(
            "public static unsafe int ReadAddress",
            typeSource,
            StringComparison.Ordinal);
    }

    static ApiMember Method(string name)
        => new()
        {
            Name = name,
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = "System.Threading.Tasks.Task"
            }
        };

    static string Declaration(string source)
        => source.ReplaceLineEndings("\n").Split('\n')[0];

    // --- Extraction: MetadataName reconstruction from real metadata (no ilasm) ---

    [Fact]
    public void Extract_NestedType_PopulatesMetadataNameWithPlusSeparator()
    {
        var assemblyPath = typeof(ApiOutputFormatterTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, typesOnly: true);

        var outer = surface.Types.Single(t => t.Name == nameof(PlusFixtureOuter));
        Assert.Equal(nameof(PlusFixtureOuter), outer.MetadataName);

        // Nested public types are surfaced with a '.' display name but a '+'
        // metadata name, mirroring how the analysis TypeRef spells them.
        var inner = surface.Types.Single(t => t.Name == "PlusFixtureOuter.Inner");
        Assert.Equal("PlusFixtureOuter+Inner", inner.MetadataName);

        // The reconstructed metadata name is exactly what a TypeRef would carry,
        // so SameType reconciles the two without any string surgery.
        var typeRef = TypeRef.Definition(Asm, inner.Namespace ?? "", "PlusFixtureOuter+Inner");
        Assert.True(ApiAnalysisInspection.SameType(typeRef, inner));
    }

    // --- Filtered projections must carry MetadataName to the analysis path ---

    [Fact]
    public void BuildFilteredTypeForSections_PreservesMetadataName()
    {
        // The type-command render path filters the extracted type through
        // BuildFilteredTypeForSections before opening the type-scope analysis
        // session (which calls SameType). If the projection dropped MetadataName,
        // SameType would fall back to the lossy '+'→'.' compare and re-drop rows
        // for a literal-'+' type — the exact #2238 bug, reintroduced downstream.
        var type = new ApiType { Namespace = null, Name = "A+B", MetadataName = "A+B", Members = [] };

        var filtered = ApiCommand.BuildFilteredTypeForSections(type, new ApiOptions());

        Assert.Equal("A+B", filtered.MetadataName);
        Assert.True(ApiAnalysisInspection.SameType(TypeRef.Definition(Asm, "", "A+B"), filtered));
    }

    // --- Extraction: non-nested type with a literal '+' (requires ilasm) ---

    [Fact]
    public void Extract_NonNestedTypeWithLiteralPlus_MatchesTypeRef()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"plus-type-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string ilPath = Path.Combine(dir, "PlusType.il");
        string dllPath = Path.Combine(dir, "PlusType.dll");

        const string il = """
            .assembly extern mscorlib { }
            .assembly 'PlusType' { }
            .module 'PlusType.dll'

            .class public auto ansi beforefieldinit 'A+B'
                   extends [mscorlib]System.Object
            {
              .method public hidebysig specialname rtspecialname
                      instance void .ctor() cil managed
              {
                .maxstack 8
                ldarg.0
                call instance void [mscorlib]System.Object::.ctor()
                ret
              }
            }
            """;

        try
        {
            File.WriteAllText(ilPath, il);
            if (!TryAssemble(ilPath, dllPath) || !File.Exists(dllPath))
            {
                Assert.Skip("ilasm not available or failed to assemble the fixture");
                return;
            }

            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            var surface = ApiSurfaceExtractor.Extract(peReader, typesOnly: true);

            // The literal '+' must survive extraction unmangled in both the
            // display name and the metadata name (the type is not nested).
            var apiType = surface.Types.Single(
                t => t.Name == "A+B" && string.IsNullOrEmpty(t.Namespace));
            Assert.Equal("A+B", apiType.MetadataName);

            // End-to-end: the row would previously be dropped because
            // "A+B".Replace('+','.') == "A.B" != "A+B". It now matches.
            var typeRef = TypeRef.Definition(Asm, "", "A+B");
            Assert.True(ApiAnalysisInspection.SameType(typeRef, apiType));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    static bool TryAssemble(string ilPath, string dllPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ilasm",
                ArgumentList = { ilPath, "-dll", $"-output={dllPath}", "-quiet" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            // Drain both pipes concurrently before waiting: a synchronous
            // ReadToEnd() on one stream blocks until EOF, so if ilasm fills the
            // other pipe's buffer the child blocks on write and the timeout below
            // is never reached (classic process deadlock).
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            // The streams reach EOF once the process exits; give the reads a
            // bounded chance to finish so no background read leaks.
            Task.WaitAll([stdout, stderr], 5_000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ilasm not found on PATH.
            return false;
        }
    }
}

/// <summary>Fixture with a public nested type used to exercise metadata-name reconstruction.</summary>
public class PlusFixtureOuter
{
    public class Inner { }
}

public static class RuntimeAsyncHeaderFixture
{
    public static async Task<int> YieldAsync(int value)
    {
        await Task.Yield();
        return value;
    }

    public static unsafe int ReadAddress(nint address) => *(int*)address;
}

/// <summary>
/// Real compiled witness for issue #3664: generic methods whose constraints the
/// decompiled-source views have to spell, and an override whose inherited
/// constraints they must not restate (CS0460).
/// </summary>
public abstract class ConstraintFixtureBase
{
    public abstract void Run<T>(T value) where T : class, new();

    /// <summary>
    /// The `class` constraint here is what makes `T?` a nullable reference type rather
    /// than Nullable&lt;T&gt;, so an override that drops it renders uncompilable C#.
    /// </summary>
    public abstract T? Pick<T>(T? value) where T : class;
}

public class ConstraintFixture : ConstraintFixtureBase, IConstraintFixture
{
    public static int Compare<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);

    public override void Run<T>(T value) => value.ToString();

    public override T? Pick<T>(T? value) where T : class => value;

    void IConstraintFixture.Wrap<T>(T? value) { }
}

/// <summary>
/// The explicit implementation of <see cref="Wrap"/> is rendered by the text fallback in
/// the whole-type view — the signature-model path declines the kind — so it is the case
/// that reaches the constraint recovery without a caller-supplied parameter list.
/// </summary>
public interface IConstraintFixture
{
    void Wrap<T>(T? value) where T : struct;
}

/// <summary>
/// One row per line of the restatement table an override has to satisfy. The rows are
/// distinguished by what the base constrains, not by how the constraint is spelled:
/// <see cref="Enumish"/> and <see cref="Named"/> are both class constraints in
/// metadata, yet C# requires opposite restatements for them.
/// </summary>
public class RestatementRowBase
{
    public virtual T? None<T>(T? value) => value;

    public virtual T? NotNull<T>(T? value) where T : notnull => value;

    public virtual T? Interface<T>(T? value) where T : IConstraintFixture => value;

    public virtual T? Enumish<T>(T? value) where T : Enum => value;

    public virtual T? Named<T>(T? value) where T : ConstraintFixtureBase => value;

    public virtual T? Ctor<T>(T? value) where T : new() => value;

    /// <summary>
    /// `where T : U` makes T exactly as known as U, so these two rows differ only in
    /// what the *other* parameter is constrained to.
    /// </summary>
    public virtual T? Transitive<T, U>(T? value) where T : U where U : ConstraintFixtureBase => value;

    public virtual T? OpenChain<T, U>(T? value) where T : U => value;
}

public class RestatementRowFixture : RestatementRowBase
{
    public override T? None<T>(T? value) where T : default => value;

    public override T? NotNull<T>(T? value) where T : default => value;

    public override T? Interface<T>(T? value) where T : default => value;

    public override T? Enumish<T>(T? value) where T : default => value;

    public override T? Named<T>(T? value) where T : class => value;

    public override T? Ctor<T>(T? value) where T : default => value;

    public override T? Transitive<T, U>(T? value) where T : class where U : class => value;

    public override T? OpenChain<T, U>(T? value) where T : default where U : default => value;
}
