using System.Diagnostics;
using System.IO;
using System.Linq;
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
            BodyIsSingleReturnExpression = true
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
            BodyIsSingleReturnExpression = false
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
            BodyIsSingleReturnExpression = true
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
