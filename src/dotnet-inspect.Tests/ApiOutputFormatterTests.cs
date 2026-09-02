using System.Diagnostics;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using InertText;
using Markout;
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
    public void SameType_ExactIdentitySeparatesLiteralDotFromNesting()
    {
        MetadataTypeDefinitionName literalName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("N", ["Outer.Inner"]))
            .Name;
        MetadataTypeDefinitionName nestedName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["Outer", "Inner"]))
            .Name;
        var origin = Assert.IsType<TypeReferenceOrigin.CurrentAssembly>(
            typeof(TypeReferenceOrigin.CurrentAssembly)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(constructor =>
                    constructor.GetParameters() is
                    [
                        {
                            ParameterType:
                            var parameterType
                        },
                    ]
                    && parameterType
                        == typeof(AssemblyReferenceIdentity))
                .Invoke([null]));
        var typeRef = TypeRef.Definition(
            Asm,
            "N",
            "Outer.Inner");
        typeof(TypeRef).GetProperty(
                nameof(TypeRef.Resolution),
                BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(
                typeRef,
                new ResolvableTypeReference(
                    origin,
                    literalName));
        var apiType = new ApiType
        {
            Namespace = "N",
            Name = "Outer.Inner",
            MetadataName = "Outer.Inner",
            DefinitionName = nestedName,
        };

        Assert.False(ApiAnalysisInspection.SameType(typeRef, apiType));
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
    public void PopulateAnnotatedSourceDocument_PreservesFailureForSectionAndRawOutput()
    {
        var failure = DecompilerResult.Failure(
            DiagnosticIds.InternalError,
            "InvalidOperationException: document failed");
        var sections = new MemberCodeView();

        Assert.True(ApiOutputFormatter.PopulateAnnotatedSourceDocument(
            sections,
            sourceDocument: null,
            failure));
        Assert.Same(failure, sections.AnnotatedSourceDocumentFailure);
        Assert.Equal(
            "DEC0001: InvalidOperationException: document failed",
            sections.AnnotatedSourceDocumentCode.Content);
        Assert.Equal(
            "DEC0001: InvalidOperationException: document failed",
            ApiCommand.AnnotatedSourceDocumentError(sections));
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
    public void ShapeView_FinalizerUsesExactLiteralPlusLeaf()
    {
        MetadataTypeDefinitionName exactName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "",
                    ["A+B"]))
                .Name;
        var type = new ApiType
        {
            Name = "A+B",
            DefinitionName = exactName,
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Finalize",
                    Kind = "finalizer",
                    Signature = "void Finalize()",
                    IsFinalizer = true,
                },
            ],
        };

        var view = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: []);
        var finalizer = Assert.Single(
            view.Members,
            node => node.Text.StartsWith(
                "Finalizer",
                StringComparison.Ordinal));
        Assert.Equal(
            @"~A\+B()",
            Assert.Single(finalizer.Children!).Text);
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

    [Theory]
    [InlineData(
        nameof(CrossAssemblyConstraintRestatementFixture.ClassConstraint),
        TypeParameterTypeKind.ReferenceType)]
    [InlineData(
        nameof(CrossAssemblyConstraintRestatementFixture.InterfaceConstraint),
        TypeParameterTypeKind.NeitherReferenceNorValue)]
    [InlineData(
        nameof(
            CrossAssemblyConstraintRestatementFixture
                .GenericBaseConstraint),
        TypeParameterTypeKind.ReferenceType)]
    public void ConstraintRestatement_ResolvesCrossAssemblyNamedConstraint(
        string memberName,
        TypeParameterTypeKind expected)
    {
        string path =
            typeof(CrossAssemblyConstraintRestatementFixture)
                .Assembly.Location;
        using (var pe = new PEReader(File.OpenRead(path)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinition type = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(candidate =>
                    reader.GetString(candidate.Name)
                        == nameof(
                            CrossAssemblyConstraintRestatementFixture));
            MethodDefinition method = type.GetMethods()
                .Select(reader.GetMethodDefinition)
                .Single(candidate =>
                    reader.GetString(candidate.Name) == memberName);
            GenericParameter parameter = reader.GetGenericParameter(
                Assert.Single(method.GetGenericParameters()));
            GenericParameterConstraint constraint =
                reader.GetGenericParameterConstraint(
                    Assert.Single(parameter.GetConstraints()));

            Assert.Equal(
                HandleKind.TypeReference,
                constraint.Type.Kind);

            ApiSurface unresolved = ApiSurfaceExtractor.Extract(pe);
            ApiMember unresolvedMember = Assert.Single(
                Assert.Single(
                    unresolved.Types,
                    candidate =>
                        candidate.Name
                            == nameof(
                                CrossAssemblyConstraintRestatementFixture))
                    .Members,
                candidate => candidate.Name == memberName);
            Assert.Equal(
                TypeParameterTypeKind.Undetermined,
                Assert.Single(
                    unresolvedMember.SignatureModel!.TypeParameters)
                    .TypeKind);
        }

        using var resolution =
            new TypeDefinitionResolutionSession(
                path,
                isPlatformAssembly: false);
        ApiSurface surface = Assert.IsType<ApiSurface>(
            resolution.ExtractApiSurface());
        ApiMember member = Assert.Single(
            Assert.Single(
                surface.Types,
                candidate =>
                    candidate.Name
                        == nameof(
                            CrossAssemblyConstraintRestatementFixture))
                .Members,
            candidate => candidate.Name == memberName);

        Assert.Equal(
            expected,
            Assert.Single(member.SignatureModel!.TypeParameters).TypeKind);
    }

    [Fact]
    public void ConstraintRestatement_ClassifiesClassWithConstructedGenericBase()
    {
        string path =
            typeof(DotnetInspector.Fixtures.CrossAssemblyConstraintBase)
                .Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        ApiSurface surface = ApiSurfaceExtractor.Extract(pe);
        ApiMember member = Assert.Single(
            Assert.Single(
                surface.Types,
                candidate =>
                    candidate.Name
                        == nameof(
                            DotnetInspector.Fixtures
                                .CrossAssemblyConstraintBase))
                .Members,
            candidate =>
                candidate.Name
                    == nameof(
                        DotnetInspector.Fixtures
                            .CrossAssemblyConstraintBase
                            .GenericBaseConstraint));

        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            Assert.Single(
                member.SignatureModel!.TypeParameters)
                .TypeKind);
    }

    [Fact]
    public void ConstraintRestatement_UnavailableOrAmbiguousBindingStaysUndetermined()
    {
        string path =
            typeof(CrossAssemblyConstraintRestatementFixture)
                .Assembly.Location;
        string dependencyPath =
            typeof(DotnetInspector.Fixtures.ExternalConstraintClass)
                .Assembly.Location;
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ConstraintRestatement_UnavailableOrAmbiguousBindingStaysUndetermined)));
        ResolvedAssemblyReference first =
            ResolvedAssemblyReference.CreateFromPath(
                dependencyPath,
                AssemblyResolutionProvenance.Local("first"));
        ResolvedAssemblyReference second =
            ResolvedAssemblyReference.CreateFromPath(
                dependencyPath,
                AssemblyResolutionProvenance.Local("second"));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            ClassConstraintKind(new MissingBindingPolicy()));
        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            ClassConstraintKind(
                new AmbiguousBindingPolicy(first, second)));

        TypeParameterTypeKind ClassConstraintKind(
            IAssemblyBindingPolicy policy)
        {
            using var pe = new PEReader(File.OpenRead(path));
            using var catalog = new TypeResolutionCatalog();
            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                policy);
            ApiMember member = Assert.Single(
                Assert.Single(
                    surface.Types,
                    candidate =>
                        candidate.Name
                            == nameof(
                                CrossAssemblyConstraintRestatementFixture))
                    .Members,
                candidate =>
                    candidate.Name
                        == nameof(
                            CrossAssemblyConstraintRestatementFixture
                                .ClassConstraint));
            return Assert.Single(
                member.SignatureModel!.TypeParameters).TypeKind;
        }
    }

    [Fact]
    public void ConstraintRestatement_ExternalValueTypesStayUndetermined()
    {
        var (consumerPath, dependencyPath) =
            EmitExternalValueTypeConstraintSample();
        try
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            ConstraintRestatement_ExternalValueTypesStayUndetermined)));
            ResolvedAssemblyReference dependency =
                ResolvedAssemblyReference.CreateFromPath(
                    dependencyPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            ConstraintRestatement_ExternalValueTypesStayUndetermined)));
            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog();
            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new ExactBindingPolicy(dependency));
            ApiType type = Assert.Single(
                surface.Types,
                candidate => candidate.Name == "ValueTypeSample");

            Assert.All(
                type.Members.Where(
                    candidate =>
                        candidate.Name
                            is "DirectValueType"
                                or "ConstructedValueType"),
                member => Assert.Equal(
                    TypeParameterTypeKind.Undetermined,
                    Assert.Single(
                        member.SignatureModel!.TypeParameters)
                        .TypeKind));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(dependencyPath);
        }
    }

    [Fact]
    public void ConstraintRestatement_CachesLargeCoreSpelledAssemblyReferenceIdentity()
    {
        var (consumerPath, dependencyPath) =
            EmitRepeatedExternalConstraintSample(
                methodCount: 64,
                publicKeyBytes: 1024 * 1024);
        try
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            ConstraintRestatement_CachesLargeCoreSpelledAssemblyReferenceIdentity)));
            ResolvedAssemblyReference dependency =
                ResolvedAssemblyReference.CreateFromPath(
                    dependencyPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            ConstraintRestatement_CachesLargeCoreSpelledAssemblyReferenceIdentity)));

            Extract();
            long before = GC.GetAllocatedBytesForCurrentThread();
            ApiSurface surface = Extract();
            long allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;

            List<ApiMember> members = Assert.Single(
                    surface.Types,
                    candidate =>
                        candidate.Name == "RepeatedConstraintSample")
                    .Members
                    .Where(member => member.Name.StartsWith(
                        "Pick",
                        StringComparison.Ordinal))
                    .ToList();
            Assert.Equal(64, members.Count);
            Assert.All(
                members,
                member => Assert.Equal(
                    TypeParameterTypeKind.ReferenceType,
                    Assert.Single(
                        member.SignatureModel!.TypeParameters)
                        .TypeKind));
            Assert.InRange(allocated, 0, 32 * 1024 * 1024);

            ApiSurface Extract()
            {
                using var pe =
                    new PEReader(File.OpenRead(consumerPath));
                using var catalog = new TypeResolutionCatalog();
                return ApiSurfaceExtractor.Extract(
                    pe,
                    source,
                    catalog,
                    new ExactBindingPolicy(dependency));
            }
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(dependencyPath);
        }
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
        var (dllPath, fakeCorePath) =
            EmitCoreLibraryLookalikeSample();
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

            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    dllPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            ConstraintRestatement_RejectsACoreLibraryLookalike)));
            ResolvedAssemblyReference fakeCore =
                ResolvedAssemblyReference.CreateFromPath(
                    fakeCorePath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            ConstraintRestatement_RejectsACoreLibraryLookalike)));
            using var catalog = new TypeResolutionCatalog();
            ApiSurface resolved = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new ExactBindingPolicy(fakeCore));
            ApiMember resolvedMember = Assert.Single(
                Assert.Single(
                    resolved.Types,
                    candidate => candidate.Name == "LookalikeSample")
                    .Members,
                candidate => candidate.Name == "Pick");
            Assert.Equal(
                TypeParameterTypeKind.ReferenceType,
                Assert.Single(
                    resolvedMember.SignatureModel!.TypeParameters)
                    .TypeKind);
        }
        finally
        {
            File.Delete(dllPath);
            File.Delete(fakeCorePath);
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
    /// </summary>
    /// <remarks>
    /// Current-thread allocation makes the answer-cache gate independent of scheduler
    /// contention. Three KiB per parameter leaves more than twice the graph resolver's
    /// calibrated 5,265,312-byte allocation; clearing its answers after each parameter
    /// allocated 4,521,985,296 bytes by re-resolving every remaining tail.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_ClassifiesALongConstraintChainWithoutRewalkingIt()
    {
        const int Length = 4000;
        const int AllocationBudgetPerParameter = 3 * 1024;
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
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var surface = ApiSurfaceExtractor.Extract(pe);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            var type = Assert.Single(surface.Types, candidate => candidate.Name == "ChainSample");
            var member = Assert.Single(type.Members, candidate => candidate.Name == "Pick");
            Assert.Equal(Length, member.SignatureModel!.TypeParameters.Count);
            Assert.All(
                member.SignatureModel.TypeParameters,
                typeParameter => Assert.Equal(TypeParameterTypeKind.NeitherReferenceNorValue, typeParameter.TypeKind));

            long allocationBudget = (long)Length * AllocationBudgetPerParameter;
            Assert.True(
                allocated <= allocationBudget,
                $"Classifying a {Length}-parameter constraint chain allocated {allocated:N0} bytes; "
                    + $"the linear-work budget is {allocationBudget:N0} bytes.");
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
    /// <remarks>
    /// Current-thread allocation makes this a scheduler-independent gate. Calibration
    /// measured 5,018,352 bytes for graph resolution and 21,230,096 bytes for the
    /// historical budgeted walk. Three KiB per parameter leaves more than twice the
    /// current allocation while rejecting that predecessor.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_ResolvesALongCyclicConstraintChain()
    {
        const int Length = 4000;
        const int AllocationBudgetPerParameter = 3 * 1024;
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
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var surface = ApiSurfaceExtractor.Extract(pe);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            var type = Assert.Single(surface.Types, candidate => candidate.Name.StartsWith("ChainSample", StringComparison.Ordinal));
            Assert.Equal(Length, type.TypeParameters.Count);

            // Nothing is knowable from a chain that only leads back to itself.
            Assert.All(
                type.TypeParameters,
                typeParameter => Assert.Equal(TypeParameterTypeKind.Undetermined, typeParameter.TypeKind));

            long allocationBudget = (long)Length * AllocationBudgetPerParameter;
            Assert.True(
                allocated <= allocationBudget,
                $"Classifying a {Length}-parameter cyclic chain allocated {allocated:N0} bytes; "
                    + $"the linear-work budget is {allocationBudget:N0} bytes.");
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
    /// <remarks>
    /// Current-thread allocation makes the shared-state requirement independent of
    /// scheduler contention. Three KiB per parameter leaves more than twice the
    /// graph resolver's calibrated 4,623,560-byte allocation; a fresh state per
    /// parameter allocated 4,936,034,080 bytes through repeated graph construction.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_ClassifiesALongChainWithoutRewalkingItInDeclarationQuery()
    {
        const int Length = 4000;
        const int AllocationBudgetPerParameter = 3 * 1024;
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

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var typeParameters = MetadataDeclarationQuery.GetTypeParameters(reader, typeDefinition);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.Equal(Length, typeParameters.Count);
            Assert.All(
                typeParameters,
                typeParameter => Assert.Equal(TypeParameterTypeKind.NeitherReferenceNorValue, typeParameter.TypeKind));

            long allocationBudget = (long)Length * AllocationBudgetPerParameter;
            Assert.True(
                allocated <= allocationBudget,
                $"Reading a {Length}-parameter constraint chain allocated {allocated:N0} bytes; "
                    + $"the linear-work budget is {allocationBudget:N0} bytes.");
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
    /// why the assertions below pin the answer and the work bound together.
    /// </remarks>
    /// <remarks>
    /// Current-thread allocation makes that bound independent of scheduler contention.
    /// Eight KiB per parameter leaves more than three times the graph resolver's calibrated
    /// 91,312-byte allocation; discarding its answers after each parameter allocated
    /// 564,168 bytes by repeatedly resolving the fan.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_ProvesAReferenceTypeReachedPastACycle()
    {
        const int Depth = 30;
        const int AllocationBudgetPerParameter = 8 * 1024;

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
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var surface = ApiSurfaceExtractor.Extract(pe);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            var type = Assert.Single(surface.Types, candidate => candidate.Name.StartsWith("ChainSample", StringComparison.Ordinal));
            var byName = type.TypeParameters.ToDictionary(typeParameter => typeParameter.Name);

            // The proof is reached, past the cycle every parameter between also reaches.
            Assert.Equal(TypeParameterTypeKind.ReferenceType, byName["T0"].TypeKind);
            Assert.Equal(TypeParameterTypeKind.ReferenceType, byName["TClass"].TypeKind);

            // The cycle itself remains unanswerable, and says nothing about anything else.
            Assert.Equal(TypeParameterTypeKind.Undetermined, byName["TCycle"].TypeKind);

            long allocationBudget = (long)names.Length * AllocationBudgetPerParameter;
            Assert.True(
                allocated <= allocationBudget,
                $"Classifying a {names.Length}-parameter graph around a cycle allocated {allocated:N0} bytes; "
                    + $"the linear-work budget is {allocationBudget:N0} bytes.");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    /// <summary>
    /// Many declarations, each with its own cyclic parameter list. Resolution is per
    /// declaration, so a module pays for each one; this pins that the per-declaration
    /// allocation stays proportional to that declaration rather than rescanning and
    /// allocating a sibling-number map for every constraint edge.
    /// </summary>
    /// <remarks>
    /// The gate uses current-thread allocation rather than elapsed time so scheduler
    /// contention cannot spend the budget. Calibration on this input measured 221,415,280
    /// bytes for the graph resolver and 9,140,435,984 bytes for the per-list walk it
    /// replaced. Four KiB per parameter leaves nearly three times the current allocation
    /// while the regressed implementation exceeds the resulting budget by over an order
    /// of magnitude.
    /// </remarks>
    [Fact]
    public void ConstraintRestatement_ResolvesManyCyclicListsWithoutPerListWaste()
    {
        const int Lists = 512;
        const int Length = 317;
        const int AllocationBudgetPerParameter = 4 * 1024;
        string dllPath = EmitManyCyclicListsSample(Lists, Length);
        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var surface = ApiSurfaceExtractor.Extract(pe);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            var types = surface.Types
                .Where(candidate => candidate.Name.StartsWith("Many", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(Lists, types.Count);
            Assert.All(
                types,
                type => Assert.All(
                    type.TypeParameters,
                    typeParameter => Assert.Equal(TypeParameterTypeKind.Undetermined, typeParameter.TypeKind)));

            long allocationBudget = (long)Lists * Length * AllocationBudgetPerParameter;
            Assert.True(
                allocated <= allocationBudget,
                $"Classifying {Lists} cyclic lists of {Length} parameters allocated {allocated:N0} bytes; "
                    + $"the linear-work budget is {allocationBudget:N0} bytes.");
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
    static (string ConsumerPath, string FakeCorePath)
        EmitCoreLibraryLookalikeSample()
    {
        var fakeCore = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName($"FakeCoreLib{Guid.NewGuid():N}"), typeof(object).Assembly);
        var fakeModule = fakeCore.DefineDynamicModule("FakeCoreLib");
        Type impostor = fakeModule.DefineType(
            "System.Enum",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.Abstract
                | System.Reflection.TypeAttributes.Class)
            .CreateType()!;
        string fakePath = Path.Combine(Path.GetTempPath(), $"fake-corelib-{Guid.NewGuid():N}.dll");
        fakeCore.Save(fakePath);

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
        return (path, fakePath);
    }

    static (string ConsumerPath, string DependencyPath)
        EmitExternalValueTypeConstraintSample()
    {
        var dependency =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"ExternalValueTypes{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var dependencyModule =
            dependency.DefineDynamicModule("ExternalValueTypes");
        Type direct = dependencyModule
            .DefineType(
                "Fixtures.ExternalStruct",
                System.Reflection.TypeAttributes.Public
                    | System.Reflection.TypeAttributes.Sealed
                    | System.Reflection.TypeAttributes.SequentialLayout,
                typeof(ValueType))
            .CreateType()!;
        var genericBuilder = dependencyModule.DefineType(
            "Fixtures.ExternalStruct`1",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.Sealed
                | System.Reflection.TypeAttributes.SequentialLayout,
            typeof(ValueType));
        genericBuilder.DefineGenericParameters("T");
        Type generic = genericBuilder.CreateType()!;
        string dependencyPath = Path.Combine(
            Path.GetTempPath(),
            $"external-value-types-{Guid.NewGuid():N}.dll");
        dependency.Save(dependencyPath);

        var consumer =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"ExternalValueTypeConsumer{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var module =
            consumer.DefineDynamicModule("ExternalValueTypeConsumer");
        var sample = module.DefineType(
            "ValueTypeSample",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.Class);
        AddMethod("DirectValueType", direct);
        AddMethod(
            "ConstructedValueType",
            generic.MakeGenericType(typeof(int)));
        sample.CreateType();
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"external-value-type-consumer-{Guid.NewGuid():N}.dll");
        consumer.Save(consumerPath);
        return (consumerPath, dependencyPath);

        void AddMethod(string name, Type constraint)
        {
            var method = sample.DefineMethod(
                name,
                System.Reflection.MethodAttributes.Public
                    | System.Reflection.MethodAttributes.Static);
            var parameter =
                method.DefineGenericParameters("T")[0];
            parameter.SetBaseTypeConstraint(constraint);
            method.SetReturnType(parameter);
            method.SetParameters(parameter);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
        }
    }

    static (string ConsumerPath, string DependencyPath)
        EmitRepeatedExternalConstraintSample(
            int methodCount,
            int publicKeyBytes)
    {
        var dependencyName = new System.Reflection.AssemblyName(
            $"LargeKeyConstraint{Guid.NewGuid():N}");
        dependencyName.SetPublicKey(new byte[publicKeyBytes]);
        var dependency =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                dependencyName,
                typeof(object).Assembly);
        var dependencyModule =
            dependency.DefineDynamicModule("LargeKeyConstraint");
        Type externalClass = dependencyModule
            .DefineType(
                "System.Enum",
                System.Reflection.TypeAttributes.Public
                    | System.Reflection.TypeAttributes.Class)
            .CreateType()!;
        string dependencyPath = Path.Combine(
            Path.GetTempPath(),
            $"large-key-constraint-{Guid.NewGuid():N}.dll");
        dependency.Save(dependencyPath);

        var consumer =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"RepeatedConstraint{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var module =
            consumer.DefineDynamicModule("RepeatedConstraint");
        var sample = module.DefineType(
            "RepeatedConstraintSample",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.Class);
        for (int i = 0; i < methodCount; i++)
        {
            var method = sample.DefineMethod(
                $"Pick{i}",
                System.Reflection.MethodAttributes.Public
                    | System.Reflection.MethodAttributes.Static);
            var parameter =
                method.DefineGenericParameters("T")[0];
            parameter.SetBaseTypeConstraint(externalClass);
            method.SetReturnType(parameter);
            method.SetParameters(parameter);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
        }
        sample.CreateType();
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"repeated-constraint-{Guid.NewGuid():N}.dll");
        consumer.Save(consumerPath);
        return (consumerPath, dependencyPath);
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

    /// <summary>
    /// Both decompiled-source routes preserve the compiler-produced distinction between
    /// structural <c>Nullable&lt;T&gt;</c> and bare <c>T</c> under a value-type constraint.
    /// This is the end-to-end gate for issue #3729; applying the enclosing nullable context
    /// to <c>T</c> produces invalid <c>Nullable&lt;T?&gt;</c> and changes bare <c>T</c> to
    /// <c>T?</c>.
    /// </summary>
    [Fact]
    public void ValueConstrainedGenericParameters_IgnoreNullableAnnotationBytes()
    {
        string path = typeof(ValueTypeNullabilityFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(ValueTypeNullabilityFixture).FullName);

        foreach (var member in type.Members.Where(candidate =>
                     candidate.Name is nameof(ValueTypeNullabilityFixture.NullableValue)
                         or nameof(ValueTypeNullabilityFixture.PlainValue)))
        {
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

            if (member.Name == nameof(ValueTypeNullabilityFixture.NullableValue))
            {
                Assert.Contains("Nullable<T> value", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("T?>", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("PlainValue<T>(T value", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("PlainValue<T>(T? value", sections.DecompiledSourceCode.Content, StringComparison.Ordinal);
            }
        }

        var typeSource = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(typeSource);
        Assert.Contains("Nullable<T> value", typeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("T?>", typeSource, StringComparison.Ordinal);
        var plainStart = typeSource.IndexOf("PlainValue<T>", StringComparison.Ordinal);
        Assert.True(plainStart >= 0);
        var plainEnd = typeSource.IndexOf('{', plainStart);
        Assert.True(plainEnd > plainStart);
        var plainDeclaration = typeSource[plainStart..plainEnd];
        Assert.Contains("T value", plainDeclaration, StringComparison.Ordinal);
        Assert.DoesNotContain("T? value", plainDeclaration, StringComparison.Ordinal);
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
        var type = new ApiType
        {
            Namespace = null,
            Name = "A+B",
            MetadataName = "A+B",
            DefinitionName = Assert
                .IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "",
                        ["A+B"]))
                .Name,
            IntroducedTypeParameterCounts = [0],
            MetadataToken = 0x02000002,
            Members = [],
        };

        var filtered = ApiCommand.BuildFilteredTypeForSections(type, new ApiOptions());

        Assert.Equal("A+B", filtered.MetadataName);
        Assert.Equal(type.DefinitionName, filtered.DefinitionName);
        Assert.Equal([0], filtered.IntroducedTypeParameterCounts);
        Assert.Equal(0x02000002, filtered.MetadataToken);
        Assert.True(ApiAnalysisInspection.SameType(TypeRef.Definition(Asm, "", "A+B"), filtered));
    }

    [Fact]
    public void SourceGeneratedJson_RoundTripsAllLegalMetadataNameCharacters()
    {
        MetadataTypeDefinitionName exact = Assert
            .IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["A[]*&,.", "B+C"]))
            .Name;
        var type = new ApiType
        {
            Namespace = "N",
            Name = "A[]*&,..B+C",
            Kind = "class",
            DefinitionName = exact,
            IntroducedTypeParameterCounts = [0, 0],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;

        Assert.Equal(exact, restored.DefinitionName);
        Assert.Equal([0, 0], restored.IntroducedTypeParameterCounts);
    }

    [Fact]
    public void ApiTypeJson_RoundTripsProjectedMemberDeclaringTypeIdentity()
    {
        MetadataTypeDefinitionName receiver = Assert
            .IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["Widget"]))
            .Name;
        MetadataTypeDefinitionName declaring = Assert
            .IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N`1",
                    [
                        "Outer+Literal`1",
                        "Extensions.WithDot`2",
                    ]))
            .Name;
        var type = new ApiType
        {
            Namespace = receiver.Namespace,
            Name = receiver.Segments[0],
            DefinitionName = receiver,
            Members =
            [
                new ApiMember
                {
                    Name = "Extend",
                    Kind = "extension-method",
                    DeclaringType =
                        "N`1.Outer+Literal<T>.Extensions.WithDot<T1, T2>",
                    DeclaringTypeCanonicalName =
                        @"N`1.Outer\+Literal`1.Extensions\.WithDot`2",
                    DeclaringTypeDefinitionName = declaring,
                },
            ],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;

        ApiMember member = Assert.Single(restored.Members);
        Assert.Equal(
            declaring,
            member.DeclaringTypeDefinitionName);
        Assert.NotEqual(
            restored.DefinitionName,
            member.DeclaringTypeDefinitionName);
        Assert.Contains(
            "\"declaring_type_definition_name\"",
            json,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The production API-type JSON context persists the directional
    /// <c>JsonIgnore</c> evidence instead of relying on the legacy derived
    /// <c>has_json_ignore</c> boolean, which cannot identify the retained
    /// direction. The serialization contract is gated here because the
    /// context, rather than a reflection serializer, owns the shipped format.
    /// </summary>
    [Fact]
    public void ApiTypeJson_RoundTripsDirectionalAndMalformedJsonIgnoreEvidence()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    JsonIgnoreConditions =
                    [
                        JsonWireIgnoreCondition.Always,
                        JsonWireIgnoreCondition.Never,
                        JsonWireIgnoreCondition.WhenWritingDefault,
                        JsonWireIgnoreCondition.WhenWritingNull,
                        JsonWireIgnoreCondition.WhenWriting,
                        JsonWireIgnoreCondition.WhenReading,
                        null,
                    ],
                },
                new ApiMember
                {
                    Name = "Unannotated",
                    Kind = "property",
                },
            ],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;
        ApiMember evidence = Assert.Single(
            restored.Members,
            member => member.Name == "Value");
        ApiMember unannotated = Assert.Single(
            restored.Members,
            member => member.Name == "Unannotated");

        Assert.Contains("\"has_json_ignore\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"json_ignore_conditions\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Always\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Never\"", json, StringComparison.Ordinal);
        Assert.Contains("\"WhenWritingDefault\"", json, StringComparison.Ordinal);
        Assert.Contains("\"WhenWritingNull\"", json, StringComparison.Ordinal);
        Assert.Contains("\"WhenWriting\"", json, StringComparison.Ordinal);
        Assert.Contains("\"WhenReading\"", json, StringComparison.Ordinal);
        Assert.Equal(
            type.Members[0].JsonIgnoreConditions,
            evidence.JsonIgnoreConditions);
        Assert.True(evidence.HasJsonIgnore);
        Assert.Empty(unannotated.JsonIgnoreConditions);
        Assert.False(unannotated.HasJsonIgnore);
    }

    [Fact]
    public void ApiTypeJson_RoundTripsEnumWireNameEvidence()
    {
        var type = new ApiType
        {
            Name = "State",
            Kind = "enum",
            Members =
            [
                new ApiMember
                {
                    Name = "Ready",
                    Kind = "field",
                    JsonStringEnumMemberNameAttributeValues =
                    [
                        "wire-ready",
                    ],
                },
                new ApiMember
                {
                    Name = "Malformed",
                    Kind = "field",
                    JsonStringEnumMemberNameAttributeValues = [null],
                },
            ],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;
        ApiMember ready = Assert.Single(
            restored.Members,
            member => member.Name == "Ready");
        ApiMember malformed = Assert.Single(
            restored.Members,
            member => member.Name == "Malformed");

        Assert.Contains(
            "\"json_string_enum_member_names\"",
            json,
            StringComparison.Ordinal);
        Assert.Equal(["wire-ready"], ready.JsonStringEnumMemberNameAttributeValues);
        Assert.Equal("wire-ready", ready.JsonStringEnumMemberName);
        Assert.Equal([null], malformed.JsonStringEnumMemberNameAttributeValues);
        Assert.Null(malformed.JsonStringEnumMemberName);
    }

    [Fact]
    public void ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence()
    {
        var type = new ApiType
        {
            Name = "Exports",
            HasSystemTextJsonSourceGenerationMarker = true,
            FilteredRuntimeJsExportFacts =
            [
                new(
                    "<Run>g__Local|0_0",
                    0x06000002,
                    AttributeCount: 1,
                    HasValidRow: true,
                    HasMalformedRow: false),
            ],
            Members =
            [
                new ApiMember
                {
                    Name = "Run",
                    Kind = "method",
                    GenericArity = 1,
                    HasMethodBody = false,
                    HasRuntimeJsExportWrapperCandidate = false,
                    RuntimeJsExportWrapperCandidates =
                    [
                        new(
                            0x06000003,
                            0x06000004,
                            2)
                        {
                            ModuleVersionId =
                                new Guid(
                                    "01020304-0506-0708-090a-0b0c0d0e0f10"),
                        },
                    ],
                    HasRuntimeJsExport = true,
                    RuntimeJsExportAttributeCount = 2,
                    HasMalformedRuntimeJsExportAttribute = true,
                },
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    IndexParameterCount = 0,
                },
            ],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;
        ApiMember evidence = Assert.Single(
            restored.Members,
            member => member.Name == "Run");
        ApiMember property = Assert.Single(
            restored.Members,
            member => member.Name == "Value");
        FilteredRuntimeJsExportFact filtered = Assert.Single(
            restored.FilteredRuntimeJsExportFacts);

        Assert.Contains("\"has_runtime_js_export\": true", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"runtime_js_export_attribute_count\": 2",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"has_runtime_js_export_wrapper_candidate\": false",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"runtime_js_export_wrapper_candidates\":",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"module_version_id\": "
                + "\"01020304-0506-0708-090a-0b0c0d0e0f10\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"has_malformed_runtime_js_export_attribute\": true",
            json,
            StringComparison.Ordinal);
        Assert.Contains("\"generic_arity\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"has_method_body\": false", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"has_system_text_json_source_generation_marker\": true",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"index_parameter_count\": 0",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"filtered_runtime_js_export_facts\":",
            json,
            StringComparison.Ordinal);
        Assert.True(evidence.HasRuntimeJsExport);
        Assert.Equal(2, evidence.RuntimeJsExportAttributeCount);
        Assert.True(evidence.HasMalformedRuntimeJsExportAttribute);
        Assert.Equal(1, evidence.GenericArity);
        Assert.False(evidence.HasMethodBody);
        Assert.False(
            evidence.HasRuntimeJsExportWrapperCandidate);
        Assert.Equal(
            new RuntimeJsExportWrapperCandidate(
                0x06000003,
                0x06000004,
                2)
            {
                ModuleVersionId =
                    new Guid(
                        "01020304-0506-0708-090a-0b0c0d0e0f10"),
            },
            Assert.Single(
                evidence.RuntimeJsExportWrapperCandidates!));
        Assert.True(restored.HasSystemTextJsonSourceGenerationMarker);
        Assert.Equal(0, property.IndexParameterCount);
        Assert.Equal("<Run>g__Local|0_0", filtered.MethodName);
        Assert.Equal(1, filtered.AttributeCount);
        Assert.True(filtered.HasValidRow);
        Assert.False(filtered.HasMalformedRow);
    }

    [Fact]
    public void ApiSurfaceJson_RoundTripsSurfaceScopedJsExportFailureEvidence()
    {
        var surface = new ApiSurface
        {
            Name = "Fixtures",
            FilteredRuntimeJsExportFacts =
            [
                new(
                    "<Create>b__0_0",
                    0x06000002,
                    AttributeCount: 1,
                    HasValidRow: true,
                    HasMalformedRow: false),
            ],
        };

        string json = JsonSerializer.Serialize(
            surface,
            ApiJsonContext.Default.ApiSurface);
        ApiSurface restored = JsonSerializer.Deserialize(
            json,
            ApiJsonContext.Default.ApiSurface)!;
        FilteredRuntimeJsExportFact fact = Assert.Single(
            restored.FilteredRuntimeJsExportFacts);

        Assert.Contains(
            "\"filtered_runtime_js_export_facts\"",
            json,
            StringComparison.Ordinal);
        Assert.Equal("<Create>b__0_0", fact.MethodName);
        Assert.Equal(0x06000002, fact.MetadataToken);
        Assert.True(fact.HasValidRow);
    }

    [Fact]
    public void ApplySurfaceFilters_ProjectsConstraintFailuresToRetainedTypes()
    {
        const string Path = "/inputs/Filtered.dll";
        var surface = new ApiSurface
        {
            Types =
            [
                Type("Keep", 0x02000002),
                Type("Drop", 0x02000003),
            ],
        };
        AddFailure(0x02000002, "KEEP");
        AddFailure(0x02000003, "DROP");

        ApiCommand.ApplySurfaceFilters(
            surface,
            new TypeOptions(),
            "N.Keep");

        Assert.Equal(
            2,
            surface.ConstraintResolutionFailuresBySubject.Count);
        ApiSurfaceInspectionFailure visible =
            Assert.Single(surface.InspectionFailures);
        Assert.Contains("KEEP", visible.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(
            surface.InspectionFailures,
            failure => failure.Detail.Contains(
                "DROP",
                StringComparison.Ordinal));

        static ApiType Type(string name, int token) =>
            new()
            {
                Namespace = "N",
                Name = name,
                Kind = "class",
                MetadataToken = token,
                SourceAssemblyPath = Path,
                Members = [],
            };

        void AddFailure(int token, string marker)
        {
            var failure =
                new ApiSurfaceInspectionFailure(
                    ApiSurface.ConstraintResolutionOperation,
                    token,
                    MetadataTypeNameFailureMechanism.Metadata,
                    "MalformedMetadata",
                    $"{marker} dependency failed.")
                {
                    SourceAssemblyPath = Path,
                };
            surface.AddConstraintResolutionFailure(
                new ApiSurfaceInspectionSubject(Path, token),
                failure);
        }
    }

    [Fact]
    public void BuildFullApiView_ContainsInspectionFailureDetail()
    {
        const string Marker = "INJECTEDDETAIL";
        const string DependencyMarker = "INJECTEDDEPENDENCY";
        var surface = new ApiSurface();
        surface.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                "resolve\n" + Marker,
                0x02000002,
                MetadataTypeNameFailureMechanism.Metadata,
                "MalformedMetadata",
                "prefix " + Marker + "\n\u202E\u001B[31m",
                DependencyAssembly:
                    new AssemblyReferenceIdentity(
                        DependencyMarker + "\n\u202E",
                        new Version(1, 0, 0, 0),
                        null,
                        null)));

        var (view, _) = ApiOutputFormatter.BuildFullApiView(
            surface,
            new ApiOptions
            {
                Verbosity = Verbosity.Normal,
            });

        ApiInspectionFailureRow row =
            Assert.Single(view.InspectionFailures!);
        HostileOutputAssert.MarkersRendered(
            row.Detail,
            "inspection failure detail",
            Marker);
        HostileOutputAssert.NoRenderingHazard(
            row.Detail,
            "inspection failure detail");
        HostileOutputAssert.NoLineSplit(
            row.Detail,
            [Marker]);
        HostileOutputAssert.MarkersRendered(
            row.DependencyAssembly!,
            "inspection failure dependency assembly",
            DependencyMarker);
        HostileOutputAssert.NoRenderingHazard(
            row.DependencyAssembly!,
            "inspection failure dependency assembly");
        Assert.Equal(
            TextConcern.Control,
            row.OperationText.Concerns);
        Assert.Equal(
            TextConcern.Control | TextConcern.Format,
            row.DetailText.Concerns);
        Assert.Equal(
            TextConcern.Control | TextConcern.Format,
            row.DependencyAssemblyText!.Value.Concerns);
    }

    [Fact]
    public void BuildFullApiView_PreservesCompleteDependencyIdentity()
    {
        var surface = new ApiSurface();
        surface.InspectionFailures.Add(
            CreateFailure(new Version(1, 0, 0, 0)));
        surface.InspectionFailures.Add(
            CreateFailure(new Version(2, 0, 0, 0)));

        var (view, _) = ApiOutputFormatter.BuildFullApiView(
            surface,
            new ApiOptions
            {
                Verbosity = Verbosity.Normal,
            });

        Assert.Equal(
            [
                "Dependency, Version=1.0.0.0, "
                    + "Culture=neutral, PublicKeyToken=null",
                "Dependency, Version=2.0.0.0, "
                    + "Culture=neutral, PublicKeyToken=null",
            ],
            view.InspectionFailures!
                .Select(static row => row.DependencyAssembly)
                .ToList());

        static ApiSurfaceInspectionFailure CreateFailure(
            Version version) =>
            new(
                ApiSurface.ConstraintResolutionOperation,
                0x02000002,
                MetadataTypeNameFailureMechanism.Metadata,
                "Unavailable",
                "Dependency resolution failed.",
                DependencyAssembly:
                    new AssemblyReferenceIdentity(
                        "Dependency",
                        version,
                        null,
                        null));
    }

    [Fact]
    public void ApiPresentationRows_CarryConcernProvenance()
    {
        const string Hostile = "value\u202E\nINJECTED";
        const TextConcern Concerns =
            TextConcern.Control | TextConcern.Format;

        InertString Text() => new(TextPolicy.Field, Hostile);

        var surface = new CliApiSurface(
            Text(),
            Text(),
            Text(),
            Text(),
            Text(),
            Text());
        var info = new ApiInfoSection(
            Text(),
            types: 1,
            methods: 2,
            properties: 3,
            Text(),
            Text(),
            Text());
        var failure = new ApiInspectionFailureRow(
            Hostile,
            Hostile,
            Hostile,
            Hostile,
            Hostile,
            Hostile,
            Hostile);
        var summary = new TypeSummaryRow(
            Hostile,
            Hostile,
            Hostile,
            Hostile);
        var member = new ApiTableRow(
            Hostile,
            Hostile,
            Hostile,
            Hostile);
        var type = new ApiSurfaceTableRow(
            Hostile,
            Hostile,
            Hostile,
            Hostile);

        InertString[] texts =
        [
            surface.NameText!.Value,
            surface.DescriptionText!.Value,
            surface.LibraryText!.Value,
            surface.SourceText!.Value,
            surface.VersionText!.Value,
            surface.TfmText!.Value,
            info.AssemblyText!.Value,
            info.VersionText!.Value,
            info.TfmText!.Value,
            info.SourceText!.Value,
            failure.OperationText,
            failure.SubjectText,
            failure.MechanismText,
            failure.KindText,
            failure.DetailText,
            failure.AssemblyText!.Value,
            failure.DependencyAssemblyText!.Value,
            summary.KindText,
            summary.TypeText,
            summary.MembersText,
            member.KindText,
            member.NameText,
            member.ReturnTypeText,
            member.DetailText,
            type.KindText,
            type.TypeText,
            type.MembersText,
        ];

        Assert.Equal(27, texts.Length);
        Assert.All(
            texts,
            text => Assert.Equal(Concerns, text.Concerns));
    }

    [Fact]
    public void ApiPresentationBuilders_PreserveConcernProvenance()
    {
        const string Hostile = "value\u202E\nINJECTED";
        const TextConcern Concerns =
            TextConcern.Control | TextConcern.Format;

        var api = new ApiSurface
        {
            Name = Hostile,
            Library = Hostile,
            Source = Hostile,
            Version = Hostile,
            Tfm = Hostile,
            Types =
            [
                new ApiType
                {
                    Name = Hostile,
                    Kind = "class",
                    Documentation = new DocComment
                    {
                        Summary = Hostile,
                    },
                    Members =
                    [
                        new ApiMember
                        {
                            Accessibility = Hostile,
                            Kind = "method",
                            Name = Hostile,
                            ReturnType = Hostile,
                            Signature = $"void M({Hostile})",
                        },
                    ],
                },
                new ApiType
                {
                    Name = Hostile,
                    Kind = Hostile,
                    Documentation = new DocComment
                    {
                        Summary = Hostile,
                    },
                },
            ],
        };

        var (compact, _) = ApiOutputFormatter.BuildFullApiView(
            api,
            new ApiOptions
            {
                Verbosity = Verbosity.Quiet,
            });
        Assert.Equal(Concerns, compact.NameText!.Value.Concerns);
        Assert.Equal(Concerns, compact.LibraryText!.Value.Concerns);
        Assert.Equal(Concerns, compact.SourceText!.Value.Concerns);
        Assert.Equal(Concerns, compact.VersionText!.Value.Concerns);
        Assert.Equal(Concerns, compact.TfmText!.Value.Concerns);

        var (document, _) = ApiOutputFormatter.BuildFullApiView(
            api,
            new ApiOptions
            {
                Verbosity = Verbosity.Normal,
                ShowDocs = true,
            });
        Assert.Equal(Concerns, document.NameText!.Value.Concerns);
        Assert.Equal(
            Concerns,
            document.ApiInfo!.AssemblyText!.Value.Concerns);
        TypeSummaryRow summary = Assert.Single(
            document.ClassesWithDocs!);
        Assert.Equal(Concerns, summary.TypeText.Concerns);
        Assert.Equal(
            api.Types[0].Documentation.Summary,
            summary.Description);

        var (memberTable, _) = ApiOutputFormatter.BuildTypeTableView(
            api.Types[0],
            new ApiOptions());
        ApiTableRow member = Assert.Single(memberTable.Rows!);
        Assert.Equal(Concerns, member.KindText.Concerns);
        Assert.Equal(Concerns, member.NameText.Concerns);
        Assert.Equal(Concerns, member.ReturnTypeText.Concerns);
        Assert.Equal(Concerns, member.DetailText.Concerns);

        var (surfaceTable, _) =
            ApiOutputFormatter.BuildSurfaceTableView(
                api,
                new ApiOptions
                {
                    ShowDocs = true,
                });
        ApiSurfaceTableRow type = Assert.Single(
            surfaceTable.RowsWithDescription!,
            row => row.Kind.Contains("INJECTED", StringComparison.Ordinal));
        Assert.Equal(Concerns, type.KindText.Concerns);
        Assert.Equal(Concerns, type.TypeText.Concerns);
        Assert.Equal(
            api.Types[1].Documentation.Summary,
            type.Description);
    }

    [Fact]
    public void ApiPresentationTypedText_RendersAcrossMarkdownTsvAndJsonl()
    {
        const string Hostile = "value\u200D\uFEFF\t\u202E\nINJECTED";

        InertString Text() => new(TextPolicy.Field, Hostile);

        var document = new CliApiSurface(
            Text(),
            Text(),
            Text(),
            Text(),
            Text(),
            Text())
        {
            Types = 1,
            Methods = 2,
            Properties = 3,
            ApiInfo = new ApiInfoSection(
                Text(),
                types: 1,
                methods: 2,
                properties: 3,
                Text(),
                Text(),
                Text()),
            InspectionFailures =
            [
                new ApiInspectionFailureRow(
                    Hostile,
                    Hostile,
                    Hostile,
                    Hostile,
                    Hostile,
                    Hostile,
                    Hostile),
            ],
            ClassesWithDocs =
            [
                new TypeSummaryRow(
                    Hostile,
                    Hostile,
                    Hostile,
                    "description"),
            ],
        };
        var memberTable = new ApiTypeTableView
        {
            Rows =
            [
                new ApiTableRow(
                    Hostile,
                    Hostile,
                    Hostile,
                    Hostile),
            ],
        };
        var surfaceTable = new ApiSurfaceTableView
        {
            RowsWithDescription =
            [
                new ApiSurfaceTableRow(
                    Hostile,
                    Hostile,
                    Hostile,
                    "description"),
            ],
        };

        string markdown = MarkoutSerializer.Serialize(
            document,
            ApiViewContext.Default);
        string memberTsv = RenderApiTable(
            memberTable,
            tsv: true,
            jsonl: false);
        string memberJsonl = RenderApiTable(
            memberTable,
            tsv: false,
            jsonl: true);
        string summaryJsonl = RenderApiTable(
            document,
            tsv: false,
            jsonl: true,
            includeSections: ["Classes"]);
        string surfaceTsv = RenderApiTable(
            surfaceTable,
            tsv: true,
            jsonl: false);
        string surfaceJsonl = RenderApiTable(
            surfaceTable,
            tsv: false,
            jsonl: true);

        foreach (string output in new[]
        {
            markdown,
            memberTsv,
            memberJsonl,
            summaryJsonl,
            surfaceTsv,
            surfaceJsonl,
        })
        {
            HostileOutputAssert.MarkersRendered(
                output,
                "API presentation",
                "INJECTED");
            HostileOutputAssert.NoRenderingHazard(
                output,
                "API presentation");
            HostileOutputAssert.NoLineSplit(
                output,
                ["INJECTED"]);
            Assert.Contains(@"\u200D", output, StringComparison.Ordinal);
            Assert.Contains(@"\uFEFF", output, StringComparison.Ordinal);
            Assert.Contains(@"\^I", output, StringComparison.Ordinal);
            Assert.Contains(@"\u202E", output, StringComparison.Ordinal);
            Assert.Contains(@"\^J", output, StringComparison.Ordinal);
        }

        AssertJsonlSchema(
            memberJsonl,
            ["kind", "name", "return_type", "detail"]);
        string summaryJsonlRecord = Assert.Single(
            summaryJsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith('{'));
        AssertJsonlSchema(
            summaryJsonlRecord,
            ["kind", "type", "members", "description"]);
        AssertJsonlSchema(
            surfaceJsonl,
            ["kind", "type", "members", "description"]);
    }

    private static string RenderApiTable<T>(
        T view,
        bool tsv,
        bool jsonl,
        HashSet<string>? includeSections = null)
        where T : class =>
        OutputFormatter.RenderTable(
            showHeader: true,
            (writer, formatter) => MarkoutSerializer.Serialize(
                view,
                writer,
                formatter,
                ApiViewContext.Default,
                OutputFormatter.ConfigureTableWriterOptions(
                    new MarkoutWriterOptions
                    {
                        IncludeSections = includeSections,
                    },
                    tsv,
                    jsonl)));

    private static void AssertJsonlSchema(
        string jsonl,
        string[] expectedProperties)
    {
        using JsonDocument document = JsonDocument.Parse(
            Assert.Single(
                jsonl.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(
            expectedProperties,
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.DoesNotContain(
            document.RootElement.EnumerateObject(),
            property => property.Name.EndsWith(
                "_text",
                StringComparison.Ordinal));
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

    sealed class ExactBindingPolicy(
        ResolvedAssemblyReference assembly)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && reference.Identity == assembly.Identity
                ? AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.NotFound();
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.NotFound();
    }

    sealed class AmbiguousBindingPolicy(
        ResolvedAssemblyReference first,
        ResolvedAssemblyReference second)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && reference.Identity == first.Identity
                ? AssemblyBindingSelection.Multiple([first, second])
                : AssemblyBindingSelection.NotFound();
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
