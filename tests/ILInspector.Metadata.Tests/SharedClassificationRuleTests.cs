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
/// This is the enforcing gate for that claim, and it took three rounds of
/// review to learn that reading source can only ever forbid the shapes it was
/// taught. The census fails if the literal is spelled anywhere but its single
/// definition. The declared-site check names the sites that classify and
/// requires each to use the shared rule — and for the three that decide
/// safety, to *return* its result, applied to an argument passed through
/// untouched. Those two are censuses: they notice a site that appears,
/// disappears, or stops delegating.
/// </para>
/// <para>
/// The load-bearing check is neither of them.
/// <see cref="ProviderClassifiesExactlyAsTheSharedRule"/> asks the provider
/// what it actually answers and compares it to the rule, so it does not care
/// how a divergence was written. Every escape found in review — an independent
/// predicate, the shared call made on a path nobody takes, the shared call
/// handed a rewritten name — fails it on the first input differing only by
/// case.
/// </para>
/// <para>
/// Stated exactly: the provider's classification is pinned to the shared rule
/// behaviorally; the guard's site is pinned by source, since its entry point
/// takes a handle rather than a name and has no name-level seam to compare
/// against; and no other file spells the rule. What none of it proves is that
/// a wholly new site cannot classify without either spelling the literal or
/// joining the list below.
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

    // How strictly a site must be bound to the shared rule.
    enum Binding
    {
        // Every value the method returns is the shared rule's own result. This
        // is what the three safety sites owe: their answer *is* the shared
        // answer, not merely informed by it.
        Decides,

        // The method reaches the shared rule inside a larger expression. The
        // rendering site is a switch whose other arms answer other questions,
        // so it cannot owe the stronger form.
        Consults,
    }

    // Every site that classifies a custom-attribute argument as System.Type,
    // the member of the shared rule it must use, and how tightly. The first
    // three decide safety: the guard and the provider must agree or the guard
    // approves a blob the decoder then reads by a different rule. The fourth
    // only renders, but it is listed so that "this rule exists once" is true
    // without an exception. A site removed or renamed fails here rather than
    // silently leaving the list stale.
    static readonly (string File, string Method, string Member, Binding Binding)[] ClassifyingSites =
    {
        (Path.Combine("src", "ILInspector.MetadataPrimitives", "AttributeDecoder.cs"),
            "GetSystemType", nameof(SystemTypeArgumentName.Rendered), Binding.Decides),
        (Path.Combine("src", "ILInspector.MetadataPrimitives", "AttributeDecoder.cs"),
            "IsSystemType", nameof(SystemTypeArgumentName.Matches), Binding.Decides),
        (Path.Combine("src", "ILInspector.MetadataPrimitives", "CustomAttributeValueGuard.cs"),
            "IsSrmSystemType", nameof(SystemTypeArgumentName.Matches), Binding.Decides),
        (Path.Combine("src", "ILInspector.Metadata", "AttributeReader.Rendering.cs"),
            "RenderArgument", nameof(SystemTypeArgumentName.Matches), Binding.Consults),
    };

    [Fact]
    public void ClassifyingSitesUseTheSharedRule()
    {
        var root = FindRepoRoot();
        var failures = new List<string>();

        foreach (var (file, method, member, binding) in ClassifyingSites)
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
                declaration => !SatisfiesBinding(declaration, member, binding)))
            {
                var line = declaration.GetLocation().GetLineSpan()
                    .StartLinePosition.Line + 1;
                var owed = binding == Binding.Decides
                    ? "does not return"
                    : "does not call";
                failures.Add(
                    $"{file}:{line}: {method} {owed} "
                    + $"{nameof(SystemTypeArgumentName)}.{member}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Each site that classifies a custom-attribute argument as "
            + "System.Type must use the one definition of that rule, and a "
            + "site that decides safety must return its result rather than "
            + "consult it beside a rule of its own. Anything else puts the "
            + "guard and the decoder back on separate rules:\n  "
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

    static bool SatisfiesBinding(
        MethodDeclarationSyntax method,
        string member,
        Binding binding)
        => binding == Binding.Consults
            ? ConsultsSharedRule(method, member)
            : DecidesBySharedRule(method, member);

    // A member access naming the shared rule, anywhere inside the method.
    static bool ConsultsSharedRule(MethodDeclarationSyntax method, string member)
        => method.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(access => IsSharedRule(access, member));

    // Every value the method returns is the shared rule's own result, applied
    // to an argument passed through untouched. Reaching the rule on one path
    // is not enough: a site could call it in a branch it rarely takes, or hand
    // it a name it rewrote first, and answer the rest of the time by a rule of
    // its own -- the divergence this gate exists to prevent, wearing the shape
    // of compliance.
    static bool DecidesBySharedRule(MethodDeclarationSyntax method, string member)
    {
        var returned = method.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Where(statement => OwnedBy(statement, method))
            .Select(statement => statement.Expression)
            .Concat(new[] { method.ExpressionBody?.Expression })
            .OfType<ExpressionSyntax>()
            .ToList();

        return returned.Count > 0 && returned.All(IsSharedRuleResult);

        bool IsSharedRuleResult(ExpressionSyntax expression)
            => Strip(expression) switch
            {
                MemberAccessExpressionSyntax access => IsSharedRule(access, member),
                InvocationExpressionSyntax invocation =>
                    Strip(invocation.Expression) is MemberAccessExpressionSyntax callee
                    && IsSharedRule(callee, member)
                    && invocation.ArgumentList.Arguments.All(argument =>
                        Strip(argument.Expression) is IdentifierNameSyntax),
                _ => false,
            };
    }

    // A return statement belongs to the listed method only when no lambda or
    // local function stands between them. A helper closure answering its own
    // question is not this method answering it.
    static bool OwnedBy(SyntaxNode node, MethodDeclarationSyntax method)
        => node.Ancestors()
            .FirstOrDefault(ancestor => ancestor
                is MethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax) == method;

    static ExpressionSyntax Strip(ExpressionSyntax expression)
        => expression is ParenthesizedExpressionSyntax parenthesized
            ? Strip(parenthesized.Expression)
            : expression;

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
