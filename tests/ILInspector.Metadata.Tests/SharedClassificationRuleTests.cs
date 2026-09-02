using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
/// The load-bearing check is
/// <see cref="ProviderClassifiesExactlyAsTheSharedRule"/>. It asks the
/// provider what it actually answers for a corpus of rendered names and
/// compares that to the rule, so it does not care how a divergence was
/// written. Every escape found in review — an independent predicate, the
/// shared call made on a path nobody takes, the shared call handed a rewritten
/// name — fails it on the first input differing only by case.
/// </para>
/// <para>
/// Two source checks stand beside it, and both are censuses rather than
/// proofs: the literal must not be spelled outside its definition, and every
/// listed site must still reach the shared rule. They notice a site that
/// appears, disappears, or stops delegating. An earlier version also analyzed
/// what each site *returned*, to reject a shared call made in a branch nobody
/// takes or handed a name rewritten first. That analysis is gone. It defended
/// only against a contributor writing a fake delegation deliberately, it never
/// caught a real defect in four rounds of review, and it once failed a correct
/// site for putting a return inside a local function. Reading source can only
/// forbid the shapes it was taught, and paying for more shapes stopped being
/// worth it once the behavioral check existed.
/// </para>
/// <para>
/// Stated exactly: the provider's classification is pinned to the shared rule
/// behaviorally; the guard's site is pinned only by source, since its entry
/// point takes a handle rather than a rendered name, and no captured blob can
/// distinguish the two because a real compiler always emits the name correctly
/// cased; and no other file spells the rule. A contributor who deliberately
/// rewrites the guard's comparison alongside the shared call is not caught
/// here, nor is a wholly new site that classifies without spelling the literal
/// or joining the list below. Those are accepted limits rather than
/// oversights; the comment on the guard's own method states the obligation at
/// the point of edit.
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

    // Every site that classifies a custom-attribute argument as System.Type,
    // and the member of the shared rule it must reach. The first three decide
    // safety: the guard and the provider must agree or the guard approves a
    // blob the decoder then reads by a different rule. The fourth only
    // renders, but it is listed so that "this rule exists once" is true
    // without an exception. A site removed or renamed fails here rather than
    // silently leaving the list stale.
    static readonly (string File, string Method, string Member)[] ClassifyingSites =
    {
        (Path.Combine("src", "ILInspector.MetadataPrimitives", "AttributeDecoder.cs"),
            "GetSystemType", nameof(SystemTypeArgumentName.Rendered)),
        (Path.Combine("src", "ILInspector.MetadataPrimitives", "AttributeDecoder.cs"),
            "IsSystemType", nameof(SystemTypeArgumentName.Matches)),
        (Path.Combine("src", "ILInspector.MetadataPrimitives", "CustomAttributeValueGuard.cs"),
            "IsSrmSystemType", nameof(SystemTypeArgumentName.Matches)),
        (Path.Combine("src", "ILInspector.Metadata", "AttributeReader.Rendering.cs"),
            "RenderArgument", nameof(SystemTypeArgumentName.Matches)),
    };

    [Fact]
    public void ClassifyingSitesUseTheSharedRule()
    {
        var root = FindRepoRoot();
        var failures = new List<string>();

        foreach (var (file, method, member) in ClassifyingSites)
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path))
            {
                failures.Add($"{file}: file no longer exists");
                continue;
            }

            var declarations = ParseRoot(path).DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(declaration => declaration.Identifier.ValueText == method)
                .ToList();

            if (declarations.Count == 0)
            {
                failures.Add($"{file}: no method named {method}");
                continue;
            }

            foreach (var declaration in declarations.Where(
                declaration => !UsesSharedRule(declaration, member)))
            {
                var line = declaration.GetLocation().GetLineSpan()
                    .StartLinePosition.Line + 1;
                failures.Add(
                    $"{file}:{line}: {method} does not reach "
                    + $"{nameof(SystemTypeArgumentName)}.{member}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Each site that classifies a custom-attribute argument as "
            + "System.Type must reach the one definition of that rule. A site "
            + "that stops delegating puts the guard and the decoder back on "
            + "separate rules:\n  "
            + string.Join("\n  ", failures));
    }

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
    [InlineData("System.Type")]
    [InlineData("system.type")]
    [InlineData("SYSTEM.TYPE")]
    [InlineData("System.type")]
    [InlineData("System.Type ")]
    [InlineData(" System.Type")]
    [InlineData("System.RuntimeType")]
    [InlineData("System.Types")]
    [InlineData("Type")]
    [InlineData("object")]
    [InlineData("")]
    public void ProviderClassifiesExactlyAsTheSharedRule(string rendered)
    {
        // The behavioral half of the gate. The checks above read source and
        // can only ever forbid the shapes they were taught; this asks the
        // provider what it actually answers. Every mutation the source checks
        // were extended to catch -- an independent predicate, the shared call
        // made on a path nobody takes, the shared call handed a transformed
        // name -- diverges here on the first input that differs only by case.
        using var stream = File.OpenRead(
            typeof(SharedClassificationRuleTests).Assembly.Location);
        using var image = new PEReader(stream);
        var provider = new AttributeDecoder.ArgTypeProvider(
            image.GetMetadataReader(),
            preserveSerializedTypeNames: false,
            beforeMaterialize: null,
            enumUnderlyingType: null);

        Assert.Equal(
            SystemTypeArgumentName.Matches(rendered),
            provider.IsSystemType(rendered));
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

    // A member access naming the shared rule, anywhere inside the method.
    // Deliberately shallow: see the remarks above on why this does not also
    // analyze what the method returns.
    static bool UsesSharedRule(MethodDeclarationSyntax method, string member)
        => method.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(access => IsSharedRule(access, member));

    static bool IsSharedRule(MemberAccessExpressionSyntax access, string member)
        => access.Name.Identifier.ValueText == member
            && access.Expression is IdentifierNameSyntax owner
            && owner.Identifier.ValueText == nameof(SystemTypeArgumentName);

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
                // Build output is not the component's source. A generated file
                // holding the literal would fail the census for something no
                // contributor wrote.
                var relative = Path.GetRelativePath(dir, file);
                if (relative.Split(Path.DirectorySeparatorChar)
                    is [var top, ..] && top is "obj" or "bin")
                {
                    continue;
                }

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
