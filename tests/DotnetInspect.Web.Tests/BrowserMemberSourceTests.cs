using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Text.Json;
using CSharpText;
using DotnetInspect.Web.Interop.Source;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserMemberSourceTests
{
    [Fact]
    public void RealRepositoryMemberParts_RebaseAgainstExactMemberText()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "CSharpText.MemberSlicing",
            "MemberTextSlicer.cs"));
        int signatureStart = source.IndexOf(
            "    public static string? ExtractMemberText(",
            StringComparison.Ordinal) + "    ".Length;
        int memberStart = source.LastIndexOf(
            "    /// <summary>",
            signatureStart,
            StringComparison.Ordinal) + "    ".Length;
        int bodyStart = source.IndexOf(
            "\n    {\n",
            signatureStart,
            StringComparison.Ordinal) + "\n    ".Length;
        int memberEnd = source.IndexOf(
            "\n\n    /// <summary>\n    /// Returns exact lexical parts",
            bodyStart,
            StringComparison.Ordinal);
        Assert.True(signatureStart > memberStart);
        Assert.True(bodyStart > signatureStart);
        Assert.True(memberEnd > bodyStart);

        int documentationEnd = TrimEnd(source, memberStart, signatureStart);
        int signatureEnd = TrimEnd(source, signatureStart, bodyStart);
        var member = Part(source, memberStart, memberEnd);
        var documentation = Part(source, memberStart, documentationEnd);
        var signature = Part(source, signatureStart, signatureEnd);
        var body = Part(source, bodyStart, memberEnd);
        var parts = new MemberTextParts(
            member,
            Part(source, signatureStart, memberEnd),
            ImmutableArray.Create(documentation),
            ImmutableArray<MemberTextPart>.Empty,
            signature,
            body);
        string memberText =
            source.Substring(parts.Member.Start, parts.Member.Length);
        BrowserMemberSource browser = BrowserMemberSource(
            source,
            memberText,
            parts);

        Assert.Equal(memberText, browser.Source.Text);
        Assert.DoesNotContain(
            "public static class MemberTextSlicer",
            browser.Source.Text,
            StringComparison.Ordinal);
        Assert.Collection(
            browser.Parts,
            member =>
            {
                Assert.Equal(BrowserMemberSourcePartKind.Member, member.Kind);
                BrowserMemberSourceSpan span = Assert.Single(member.Spans);
                Assert.Equal(0, span.Start);
                Assert.Equal(memberText.Length, span.Length);
                Assert.Equal(memberText.Length, span.End);
                Assert.Equal("    ", span.LeadingIndentation);
            },
            documentation =>
            {
                Assert.Equal(
                    BrowserMemberSourcePartKind.XmlDocumentation,
                    documentation.Kind);
                Assert.NotEmpty(documentation.Spans);
                Assert.All(
                    documentation.Spans,
                    span => Assert.Equal("    ", span.LeadingIndentation));
            },
            signature =>
            {
                Assert.Equal(
                    BrowserMemberSourcePartKind.Signature,
                    signature.Kind);
                BrowserMemberSourceSpan span = Assert.Single(signature.Spans);
                Assert.Contains(
                    "public static string? ExtractMemberText(",
                    memberText.Substring(span.Start, span.Length),
                    StringComparison.Ordinal);
                Assert.Equal("    ", span.LeadingIndentation);
            },
            body =>
            {
                Assert.Equal(BrowserMemberSourcePartKind.Body, body.Kind);
                BrowserMemberSourceSpan span = Assert.Single(body.Spans);
                Assert.StartsWith(
                    "{",
                    memberText.Substring(span.Start, span.Length),
                    StringComparison.Ordinal);
                Assert.Equal("    ", span.LeadingIndentation);
            });

        string json = JsonSerializer.Serialize(
            browser,
            BrowserSourceJsonContext.Default.BrowserMemberSource);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            memberText,
            root.GetProperty("source").GetProperty("text").GetString());
        Assert.False(root.GetProperty("source").TryGetProperty("parts", out _));
        JsonElement memberSpan = root.GetProperty("parts")[0]
            .GetProperty("spans")[0];
        Assert.Equal(0, memberSpan.GetProperty("start").GetInt32());
        Assert.Equal(memberText.Length, memberSpan.GetProperty("end").GetInt32());
        Assert.Equal(
            "    ",
            memberSpan.GetProperty("leadingIndentation").GetString());
        Assert.True(memberSpan.GetProperty("startLine").GetInt32() > 0);
        Assert.True(memberSpan.GetProperty("endLine").GetInt32() > 0);
    }

    [Fact]
    public void MarkoutWriteHeadingWire_RestoresEveryXmlDocumentationLineIndentation()
    {
        const string prefix =
            "namespace Markout;\n\npublic partial class MarkoutWriter\n{\n";
        const string indentedMember =
            "    /// <summary>\n"
            + "    /// Writes a heading at the specified level.\n"
            + "    /// </summary>\n"
            + "    /// <returns><c>true</c> if rendered or filtered; "
            + "<c>false</c> if the formatter does not support headings.</returns>\n"
            + "    public bool WriteHeading(int level, string text) "
            + "=> WriteHeading(level, text, null);";
        string document = prefix + indentedMember + "\n}\n";
        int memberStart = prefix.Length + "    ".Length;
        int memberEnd = prefix.Length + indentedMember.Length;
        int signatureStart = document.IndexOf(
            "public bool WriteHeading(",
            memberStart,
            StringComparison.Ordinal);
        int bodyStart = document.IndexOf(
            "=> WriteHeading(",
            signatureStart,
            StringComparison.Ordinal);
        var parts = new MemberTextParts(
            Part(document, memberStart, memberEnd),
            Part(document, signatureStart, memberEnd),
            [Part(document, memberStart, TrimEnd(document, memberStart, signatureStart))],
            [],
            Part(document, signatureStart, TrimEnd(document, signatureStart, bodyStart)),
            Part(document, bodyStart, memberEnd));
        string memberText =
            document.Substring(parts.Member.Start, parts.Member.Length);

        BrowserMemberSource browser = BrowserMemberSource(
            document,
            memberText,
            parts);

        Assert.StartsWith("/// <summary>", browser.Source.Text);
        Assert.Equal(memberText, browser.Source.Text);
        BrowserMemberSourcePart member = Assert.Single(
            browser.Parts,
            part => part.Kind == BrowserMemberSourcePartKind.Member);
        BrowserMemberSourceSpan memberSpan = Assert.Single(member.Spans);
        Assert.Equal(0, memberSpan.Start);
        Assert.Equal(memberText.Length, memberSpan.Length);
        Assert.Equal("    ", memberSpan.LeadingIndentation);

        BrowserMemberSourcePart documentation = Assert.Single(
            browser.Parts,
            part => part.Kind == BrowserMemberSourcePartKind.XmlDocumentation);
        BrowserMemberSourceSpan documentationSpan =
            Assert.Single(documentation.Spans);
        string rawDocumentation = memberText.Substring(
            documentationSpan.Start,
            documentationSpan.Length);
        Assert.Equal(
            [0, 4, 4, 4],
            rawDocumentation.Split('\n')
                .Select(line => line.TakeWhile(character => character == ' ').Count())
                .ToArray());
        Assert.Equal("    ", documentationSpan.LeadingIndentation);
    }

    [Fact]
    public void DecompiledMemberSource_HasNoAuthoredPartCatalog()
    {
        BrowserMemberSource browser = new(
            BrowserSource(
                "public string Value => \"decompiled\";",
                provider: "decompiled"),
            []);

        Assert.Equal("decompiled", browser.Source.Provider);
        Assert.Empty(browser.Parts);
    }

    [Fact]
    public void Projection_PreservesEveryExactNativeFragment()
    {
        const string prefix = "class C\n{\n";
        const string indentedMember =
            "    /// first\r\n"
            + "    /// continuation\r\n"
            + "    [First(\r\n"
            + "        1)]\n"
            + "    /// second\n"
            + "\t[Second]\r\n"
            + "    public void M(\r\n"
            + "        int value)\n"
            + "    {\r\n"
            + "        _ = value;\n"
            + "    }";
        string document = prefix + indentedMember + "\n}\n";
        int memberStart = prefix.Length + "    ".Length;
        int memberEnd = prefix.Length + indentedMember.Length;
        var native = new MemberTextParts(
            Part(document, memberStart, memberEnd),
            Fragment(
                document,
                memberStart,
                "[First(\r\n        1)]\n    /// second\n\t[Second]\r\n    public void M(\r\n        int value)\n    {\r\n        _ = value;\n    }"),
            [
                Fragment(
                    document,
                    memberStart,
                    "/// first\r\n    /// continuation"),
                Fragment(document, memberStart, "/// second"),
            ],
            [
                Fragment(
                    document,
                    memberStart,
                    "[First(\r\n        1)]"),
                Fragment(document, memberStart, "[Second]"),
            ],
            Fragment(
                document,
                memberStart,
                "public void M(\r\n        int value)"),
            Fragment(
                document,
                memberStart,
                "{\r\n        _ = value;\n    }"));
        string member =
            document.Substring(native.Member.Start, native.Member.Length);

        BrowserMemberSource browser = BrowserMemberSource(
            document,
            member,
            native);

        Assert.Equal(member, browser.Source.Text);
        AssertExactFragments(
            document,
            member,
            native.XmlDocumentation,
            BrowserPart(BrowserMemberSourcePartKind.XmlDocumentation));
        AssertExactFragments(
            document,
            member,
            native.Attributes,
            BrowserPart(BrowserMemberSourcePartKind.Attributes));
        AssertExactFragments(
            document,
            member,
            [native.Signature],
            BrowserPart(BrowserMemberSourcePartKind.Signature));
        AssertExactFragments(
            document,
            member,
            [Assert.IsType<MemberTextPart>(native.Body)],
            BrowserPart(BrowserMemberSourcePartKind.Body));

        BrowserMemberSourcePart BrowserPart(BrowserMemberSourcePartKind kind) =>
            Assert.Single(browser.Parts, part => part.Kind == kind);
    }

    [Fact]
    public void TypeAndGraphSourceWire_RemainsFlatFiveFieldContract()
    {
        string json = JsonSerializer.Serialize(
            BrowserSource("class C {}"),
            BrowserSourceJsonContext.Default.BrowserSource);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(
            ["provider", "provenance", "url", "pdbSourceLimitation", "text"],
            document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.False(document.RootElement.TryGetProperty("source", out _));
        Assert.False(document.RootElement.TryGetProperty("parts", out _));
    }

    static void AssertExactFragments(
        string document,
        string member,
        IEnumerable<MemberTextPart> native,
        BrowserMemberSourcePart browser)
    {
        Assert.Equal(
            native.Select(
                part => document.Substring(part.Start, part.Length)),
            browser.Spans.Select(
                span => member.Substring(span.Start, span.Length)));
        Assert.Equal(
            native.Select(
                part => MemberSourcePartsProjection.GetLeadingIndentation(
                    document,
                    part)),
            browser.Spans.Select(span => span.LeadingIndentation));
    }

    static MemberTextPart Fragment(
        string source,
        int searchStart,
        string fragment)
    {
        int start = source.IndexOf(
            fragment,
            searchStart,
            StringComparison.Ordinal);
        Assert.True(start >= searchStart);
        return Part(source, start, start + fragment.Length);
    }

    static MemberTextPart Part(string source, int start, int end) =>
        new(
            start,
            end - start,
            new LineRange(
                PhysicalLine(source, start),
                PhysicalLine(source, end - 1)));

    static int PhysicalLine(string source, int offset) =>
        source.AsSpan(0, offset).Count('\n') + 1;

    static int TrimEnd(string source, int start, int end)
    {
        while (end > start && char.IsWhiteSpace(source[end - 1]))
            end--;
        return end;
    }

    static BrowserSource BrowserSource(
        string text,
        string provider = "pdb") =>
        new(
            provider,
            new InertString(TextPolicy.Field, "test provenance"),
            "https://example.test/source.cs",
            null,
            text);

    static BrowserMemberSource BrowserMemberSource(
        string document,
        string member,
        MemberTextParts parts) =>
        new(
            BrowserSource(member),
            SourceExports.ProjectMemberParts(document, parts, member.Length));

    static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
