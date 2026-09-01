using System.Collections.Generic;
using System.IO;
using System.Linq;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gate for the shared <c>System.Type</c> classification rule.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CustomAttributeValueGuard"/> and the type provider inside
/// <c>AttributeDecoder</c> must classify a <c>System.Type</c> argument
/// identically. Classification runs before width and selects the reading rule,
/// so a divergence means the guard approves a blob that the decoder then reads
/// with a different rule: the cursor drifts and a later field is taken as an
/// array element count. That is the dotnet/runtime#57531 shape, worth a
/// 28,515 MiB allocation request on a shipping package.
/// </para>
/// <para>
/// Both sides already share the rendering that produces the name, so the only
/// thing that could diverge is the final comparison. The design document
/// requires that the two agree by construction, "never through two
/// implementations believed to be equivalent", so the comparison exists once
/// in <c>SystemTypeArgumentName</c> and every site calls it.
/// </para>
/// <para>
/// This is the enforcing gate for that claim. It fails if the literal is
/// spelled anywhere but its single definition, which is what re-introducing an
/// independent copy of the rule looks like in source.
/// </para>
/// </remarks>
public class SharedClassificationRuleTests
{
    // The assemblies that participate in custom-attribute argument
    // classification: the guard and provider that must agree for safety, and
    // the rendering layer that consumes the same provider-produced name.
    static readonly string[] AssemblyRoots =
    {
        Path.Combine("src", "ILInspector.MetadataPrimitives"),
        Path.Combine("src", "ILInspector.Metadata"),
    };

    // The single file permitted to spell the rule.
    const string DefiningFile = "SystemTypeArgumentName.cs";

    [Fact]
    public void SystemTypeArgumentNameIsSpelledOnce()
    {
        var root = FindRepoRoot();
        var occurrences = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            foreach (var literal in SystemTypeLiterals(ParseRoot(file)))
            {
                if (Path.GetFileName(file) == DefiningFile)
                    continue;

                var line = literal.GetLocation().GetLineSpan()
                    .StartLinePosition.Line + 1;
                occurrences.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(
            occurrences.Count == 0,
            "The System.Type classification rule must exist once, in "
            + $"{DefiningFile}, so the guard and the decoder's type provider "
            + "agree by construction rather than by two spellings believed to "
            + "be equivalent. Call SystemTypeArgumentName.Matches instead of "
            + "comparing the literal at:\n  "
            + string.Join("\n  ", occurrences));
    }

    [Fact]
    public void DefinitionSpellsTheRuleExactlyOnce()
    {
        var root = FindRepoRoot();
        var defining = EnumerateSourceFiles(root)
            .Single(file => Path.GetFileName(file) == DefiningFile);

        // Non-vacuity: the census above passes trivially if the literal stops
        // existing altogether, which would mean the rule had been renamed or
        // deleted rather than shared.
        Assert.Single(SystemTypeLiterals(ParseRoot(defining)));
    }

    [Theory]
    [InlineData("System.Type", true)]
    [InlineData("system.type", false)]
    [InlineData("System.Types", false)]
    [InlineData("Type", false)]
    [InlineData("object", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MatchesClassifiesRenderedNames(string? rendered, bool expected)
        => Assert.Equal(expected, SystemTypeArgumentName.Matches(rendered));

    // String literals whose value is the rendered System.Type name. Doc
    // comments are trivia rather than literal expressions, so prose that names
    // the type is not an occurrence of the rule.
    static IEnumerable<LiteralExpressionSyntax> SystemTypeLiterals(SyntaxNode root)
        => root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Where(literal => literal.Token.ValueText == SystemTypeArgumentName.Rendered);

    static SyntaxNode ParseRoot(string file)
    {
        var token = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(
            File.ReadAllText(file),
            cancellationToken: token);
        return tree.GetRoot(token);
    }

    static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        foreach (var assemblyRoot in AssemblyRoots)
        {
            var dir = Path.Combine(root, assemblyRoot);
            if (!Directory.Exists(dir))
            {
                throw new DirectoryNotFoundException(
                    $"Classification rule census root does not exist: {assemblyRoot}");
            }

            foreach (var file in Directory.EnumerateFiles(
                dir,
                "*.cs",
                SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
