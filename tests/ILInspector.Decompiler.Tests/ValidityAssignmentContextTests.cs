using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Validity")]
public class ValidityAssignmentContextTests
{
    // Observed unchanged body from dotnet-inspect.any 0.14.0,
    // ILInspector.Decompiler.Pipeline.TypeRef::DisplayName (#9939).
    [Fact]
    public void DisplayNameMissingReceiverContext_DeclinesAssignmentCoverage()
    {
        var result = Check("""
            string keyword;
            if (Assembly == "corelib" && Namespace == "System" && TypeRef.s_keywords.TryGetValue(Name, out keyword))
                return keyword;
            int nested = Name.LastIndexOf('+');
            return CSharpNaming.TypeNameSegment(nested < 0 ? Name : Name.Substring(nested + 1));
            """, "string");

        Assert.True(result.SemanticUnavailable);
        Assert.False(result.IsValid);
        Assert.False(result.HasSemanticDefect);
        var diagnostic = Assert.Single(result.SemanticDiagnostics);
        Assert.Equal(ValidityCheck.AssignmentContextUnavailableId, diagnostic.Id);
        Assert.Contains("CS0165", diagnostic.Message);
        Assert.Contains("TypeRef.s_keywords.TryGetValue", diagnostic.Message);
    }

    [Theory]
    [InlineData("int value; if (System.DateTime.Now.Ticks > 0 && Missing.TryRead(out value)) return value; return 0;")]
    [InlineData("if (System.DateTime.Now.Ticks > 0 && Missing.TryRead(out int value)) return value; return 0;")]
    public void UnresolvedOutCall_DeclinesRatherThanProvesValidity(string body)
    {
        var result = Check(body);
        Assert.True(result.SemanticUnavailable);
        Assert.False(result.IsValid);
        Assert.False(result.HasSemanticDefect);
    }

    [Theory]
    [InlineData("string text; int value; if (Missing.TryRead(out text) && int.TryParse(text, out value)) return value; return 0;")]
    [InlineData("string text; int value; if ((Missing.TryRead(out text) && (int.TryParse(text, out value)))) return value; return 0;")]
    public void BoundOutCallAfterUnresolvedOutInAndGuard_DeclinesDependentCoverage(string body)
    {
        var result = Check(body);
        Assert.True(result.SemanticUnavailable);
        Assert.False(result.HasSemanticDefect);
        Assert.False(result.IsValid);
        Assert.Contains(result.SemanticDiagnostics, d => d.Id == ValidityCheck.AssignmentContextUnavailableId
            && d.Message.Contains("'value'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("string text; int value; if (Missing.TryRead(out text) || int.TryParse(\"1\", out value)) return value; return 0;")]
    [InlineData("string text; int value; if (Missing.TryRead(out text) && int.TryParse(\"1\", out value)) { } return value;")]
    public void BoundOutCallThatCanBeSkipped_StaysDefectiveDespiteUnresolvedSibling(string body)
    {
        var result = Check(body);
        Assert.True(result.HasSemanticDefect);
        Assert.Contains(result.SemanticDiagnostics, d => d.Id == "CS0165" && d.Message.Contains("'value'", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundOutCall_ProvesAssignment()
    {
        var result = Check("int value; if (int.TryParse(\"1\", out value)) return value; return 0;");
        Assert.True(result.IsValid);
        Assert.False(result.SemanticUnavailable);
    }

    [Fact]
    public void BoundSkippedOutCall_KeepsRealDefect()
    {
        var result = Check("int value; if (System.DateTime.Now.Ticks > 0 && int.TryParse(\"1\", out value)) { } return value;");
        Assert.True(result.HasSemanticDefect);
        Assert.False(result.SemanticUnavailable);
        Assert.Contains(result.SemanticDiagnostics, d => d.Id == "CS0165");
    }

    [Theory]
    [InlineData("int other; Missing.TryRead(out other); int value; return value;")]
    [InlineData("int value; void Nested() { Missing.TryRead(out value); } return value;")]
    [InlineData("int value; Missing.TryRead(ref value); return value;")]
    [InlineData("int value; int copy = value; Missing.TryRead(out value); return copy;")]
    public void UnrelatedOrNestedOrRefCall_KeepsRealDefect(string body)
    {
        var result = Check(body);
        Assert.True(result.HasSemanticDefect);
        Assert.False(result.SemanticUnavailable);
        Assert.Contains(result.SemanticDiagnostics, d => d.Id == "CS0165");
    }

    [Fact]
    public void UnresolvedBareCallWithoutCs0165_DoesNotInventCoverageDecline()
    {
        // Roslyn still tracks the explicit out assignment for this error-recovery
        // shape. Only an actual CS0165 with missing assignment context declines.
        var result = Check("int value; if (Missing(out value)) return value; return 0;");
        Assert.False(result.SemanticUnavailable);
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Id == "CS0165");
    }

    [Fact]
    public void IndependentDefectSurvivesCoverageDecline()
    {
        var result = Check("int value; if (System.DateTime.Now.Ticks > 0 && Missing.TryRead(out value)) return value; int other; return other;");
        Assert.True(result.SemanticUnavailable);
        Assert.True(result.HasSemanticDefect);
        Assert.False(result.IsValid);
        Assert.Contains(result.SemanticDiagnostics, d => d.Id == "CS0165" && d.Message.Contains("other", StringComparison.Ordinal));
    }

    static ValidityCheck.RenderedBodyResult Check(string body, string returnType = "int")
    {
        var function = new IrFunction("M", TypeRef.Definition("fixture", "", "Host"),
            new MethodSignature(TypeRef.CoreLib("System", returnType == "int" ? "Int32" : "String"),
                [], HasThis: false, GenericParameterCount: 0), [], new BlockContainer());
        return ValidityCheck.EvaluateRenderedBody(function, body, "Host", "M",
            new Dictionary<string, Dictionary<string, string>>(),
            ValidityCheck.MethodShellContext.Create(function, requiresUnsafeContext: false),
            productParameterList: null, ValidityCheck.RuntimeReferences(),
            new CSharpParseOptions(LanguageVersion.Latest), ValidityCheck.CompileOptions(),
            bindSemantics: true);
    }
}
