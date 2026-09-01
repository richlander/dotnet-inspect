using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpText.Tests;

public sealed class CSharpTypeDeclarationIdentifierAdmissionTests
{
    public static TheoryData<string, string> AdmittedIdentifiers => new()
    {
        { "Widget", "Widget" },
        { "_", "_" },
        { "class", "@class" },
        { "extension", "@extension" },
        { "\u03A9", "\u03A9" },
        { "\u2160", "\u2160" },
        { "A\u0301", "A\u0301" },
        { "A\u203F", "A\u203F" },
    };

    public static TheoryData<string, CSharpTypeDeclarationIdentifierRefusalReason>
        RefusedIdentifiers => new()
        {
            { "", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "1Widget", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "A+B", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "A B", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "A\nB", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "\u0301A", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "\U00010400", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "\uD800", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "\u200CA", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "\uFEFF", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "\uFEFF\u200CA", CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier },
            { "A\u200D", CSharpTypeDeclarationIdentifierRefusalReason.IdentityNotPreserved },
            { "A\u00AD", CSharpTypeDeclarationIdentifierRefusalReason.IdentityNotPreserved },
            { "\uFEFFA", CSharpTypeDeclarationIdentifierRefusalReason.IdentityNotPreserved },
            { "\uFEFF\uFEFFA", CSharpTypeDeclarationIdentifierRefusalReason.IdentityNotPreserved },
        };

    [Theory]
    [MemberData(nameof(AdmittedIdentifiers))]
    public void AdmittedSpellingsCompileAndPreserveTypeDefIdentity(
        string identity,
        string expectedSpelling)
    {
        var admitted = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Admitted>(
            CSharpIdentifier.AdmitTypeDeclaration(identity));

        Assert.Equal(expectedSpelling, admitted.Spelling);
        Assert.Equal(identity, CompileAndReadTypeDefName(admitted.Spelling));
    }

    [Fact]
    public void EveryCompilerKeywordAdmitsWithExactTypeDefIdentity()
    {
        string[] keywords = [.. Enum.GetValues<SyntaxKind>()
            .Where(static kind =>
                SyntaxFacts.IsKeywordKind(kind) || SyntaxFacts.IsContextualKeyword(kind))
            .Select(SyntaxFacts.GetText)
            .Where(static text => !string.IsNullOrEmpty(text))
            .Distinct(StringComparer.Ordinal)];

        Assert.True(keywords.Length > 100, $"only {keywords.Length} keywords discovered");
        Assert.Contains("extension", keywords);
        Assert.Contains("__arglist", keywords);

        foreach (string keyword in keywords)
        {
            var admitted = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Admitted>(
                CSharpIdentifier.AdmitTypeDeclaration(keyword));
            Assert.Equal(keyword, CompileAndReadTypeDefName(admitted.Spelling));
        }
    }

    [Theory]
    [MemberData(nameof(RefusedIdentifiers))]
    public void RefusedTextReturnsStableReason(
        string identity,
        CSharpTypeDeclarationIdentifierRefusalReason expectedReason)
    {
        var refused = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Refused>(
            CSharpIdentifier.AdmitTypeDeclaration(identity));

        Assert.Equal(expectedReason, refused.Reason);
    }

    [Theory]
    [InlineData("A\u200C")]
    [InlineData("A\u200D")]
    [InlineData("A\u00AD")]
    [InlineData("\uFEFFA")]
    [InlineData("\uFEFF\uFEFFA")]
    public void FormatCharactersAreRefusedBecauseCompilerDropsThem(string identity)
    {
        var refused = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Refused>(
            CSharpIdentifier.AdmitTypeDeclaration(identity));
        Assert.Equal(
            CSharpTypeDeclarationIdentifierRefusalReason.IdentityNotPreserved,
            refused.Reason);

        Assert.Equal("A", CompileAndReadTypeDefName(identity));
    }

    [Theory]
    [InlineData("\u200CA")]
    public void LeadingFormatCharactersRejectedByCurrentCompilerAreInvalid(string identity)
    {
        var refused = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Refused>(
            CSharpIdentifier.AdmitTypeDeclaration(identity));
        Assert.Equal(
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier,
            refused.Reason);

        Assert.False(TryCompile(identity, out _, out var diagnostics));
        Assert.Contains(diagnostics, static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EveryBmpFormatCharacterMatchesCompilerClassification()
    {
        char[] formatCharacters = [.. Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Select(static value => (char)value)
            .Where(static ch => char.GetUnicodeCategory(ch) == UnicodeCategory.Format)];

        Assert.True(
            formatCharacters.Length > 40,
            $"only {formatCharacters.Length} BMP format characters discovered");

        foreach (char formatCharacter in formatCharacters)
        {
            AssertMatchesCompilerClassification($"{formatCharacter}A");
            AssertMatchesCompilerClassification($"A{formatCharacter}");
        }
    }

    [Fact]
    public void SupplementaryPlaneLetterRejectedByCurrentCompilerIsRefused()
    {
        const string Identity = "\U00010400";

        var refused = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Refused>(
            CSharpIdentifier.AdmitTypeDeclaration(Identity));
        Assert.Equal(
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier,
            refused.Reason);

        Assert.False(TryCompile(Identity, out _, out var diagnostics));
        Assert.Contains(diagnostics, static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void BareExtensionIsRejectedButAdmittedSpellingPreservesIdentity()
    {
        Assert.False(TryCompile("extension", out _, out _));

        var admitted = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Admitted>(
            CSharpIdentifier.AdmitTypeDeclaration("extension"));
        Assert.Equal("@extension", admitted.Spelling);
        Assert.Equal("extension", CompileAndReadTypeDefName(admitted.Spelling));
    }

    [Fact]
    public void NullIdentityIsRejectedAtTheBoundary()
        => Assert.Throws<ArgumentNullException>(
            () => CSharpIdentifier.AdmitTypeDeclaration(null!));

    static void AssertMatchesCompilerClassification(string identity)
    {
        var refused = Assert.IsType<CSharpTypeDeclarationIdentifierAdmission.Refused>(
            CSharpIdentifier.AdmitTypeDeclaration(identity));
        bool compiled = TryCompile(identity, out string? emittedName, out _);

        if (compiled)
        {
            Assert.NotEqual(identity, emittedName);
            Assert.Equal(
                CSharpTypeDeclarationIdentifierRefusalReason.IdentityNotPreserved,
                refused.Reason);
        }
        else
        {
            Assert.Equal(
                CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier,
                refused.Reason);
        }
    }

    static string CompileAndReadTypeDefName(string spelling)
    {
        bool compiled = TryCompile(spelling, out string? emittedName, out var diagnostics);
        Assert.True(compiled, string.Join(Environment.NewLine, diagnostics));
        return Assert.IsType<string>(emittedName);
    }

    static bool TryCompile(
        string spelling,
        out string? emittedName,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        string source = $"public class {spelling} {{ }}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "TypeDeclarationIdentifierOracle",
            [syntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        diagnostics = emit.Diagnostics;
        if (!emit.Success)
        {
            emittedName = null;
            return false;
        }

        stream.Position = 0;
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        emittedName = reader.TypeDefinitions
            .Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name))
            .Single(static name => name != "<Module>");
        return true;
    }
}
