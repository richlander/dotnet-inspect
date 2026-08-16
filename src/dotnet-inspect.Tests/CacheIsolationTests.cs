using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetInspector.Tests;

public sealed class CacheIsolationTests
{
    [Fact]
    public void ConsoleCollection_IsAssemblyExclusive()
    {
        var definition = Assert.Single(
            typeof(ConsoleCollection)
                .GetCustomAttributes<CollectionDefinitionAttribute>());

        Assert.Equal(ConsoleCollection.Name, definition.Name);
        Assert.True(definition.DisableParallelization);
    }

    [Fact]
    public void CacheInitializers_AreOwnedByAssemblyExclusiveCollections()
    {
        string sourceRoot = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "dotnet-inspect.Tests");
        SourceFile[] sources = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(
                Path.GetRelativePath(sourceRoot, path)))
            .Select(path => new SourceFile(
                Path.GetRelativePath(sourceRoot, path)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path)))
            .ToArray();

        CacheIsolationScanResult result = Scan(sources);

        Assert.True(
            result.InitializerCount >= 20,
            $"Only {result.InitializerCount} cache initializers were scanned; "
            + "the census is not reading the real test sources.");
        Assert.True(
            result.DelegatedCallCount >= 5,
            $"Only {result.DelegatedCallCount} HostileCli call sites were scanned; "
            + "the delegated-owner gate is vacuous.");
        Assert.Empty(result.Offenders);
    }

    [Fact]
    public void CacheIsolationScan_RejectsDirectAndDelegatedUnisolatedOwners()
    {
        CacheIsolationScanResult result = Scan(
        [
            new(
                "Synthetic.cs",
                """
                using CacheAlias = DotnetInspector.Packages.NuGetCache;
                using static DotnetInspector.Core.CoreCache;

                [CollectionDefinition("Exclusive", DisableParallelization = true)]
                public sealed class ExclusiveCollection;

                [Collection("Exclusive")]
                public sealed class Safe
                {
                    public void Run() => NuGetCache.Initialize("safe");
                }

                public sealed class Unsafe
                {
                    public void Run() => CoreCache.Initialize("unsafe");
                    public void Alias() => CacheAlias.Initialize("alias");
                    public void StaticImport() => Initialize("static");
                    public void Delegate() => HostileCli.RunAsync();
                }

                internal static class HostileCli
                {
                    public static void RunAsync() =>
                        NuGetCache.Initialize("delegated");
                }
                """),
        ]);

        Assert.Equal(5, result.InitializerCount);
        Assert.Equal(1, result.DelegatedCallCount);
        Assert.Equal(4, result.Offenders.Length);
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.Run"));
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.Alias"));
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.StaticImport"));
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.Delegate"));
    }

    static CacheIsolationScanResult Scan(IReadOnlyCollection<SourceFile> sources)
    {
        var trees = sources
            .Select(source => (
                source.Path,
                Tree: CSharpSyntaxTree.ParseText(source.Content, path: source.Path)))
            .ToArray();
        HashSet<string> exclusiveCollections = trees
            .SelectMany(source => source.Tree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>())
            .SelectMany(type => type.AttributeLists
                .SelectMany(list => list.Attributes))
            .Where(attribute => AttributeName(attribute) == "CollectionDefinition")
            .Where(attribute => NamedBooleanArgument(
                attribute,
                "DisableParallelization"))
            .Select(CollectionName)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        List<string> offenders = [];
        int initializers = 0;
        int delegatedCalls = 0;

        foreach ((string path, SyntaxTree tree) in trees)
        {
            SyntaxNode root = tree.GetRoot();
            HashSet<string> cacheTypeNames = CacheTypeNames(root);
            bool importsCacheStatically = ImportsCacheStatically(root);
            foreach (InvocationExpressionSyntax invocation in root
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                if (IsCacheInitializer(
                    invocation,
                    cacheTypeNames,
                    importsCacheStatically))
                {
                    initializers++;
                    TypeDeclarationSyntax? owner =
                        invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                    if (owner is null
                        || IsAssemblyExclusive(owner, exclusiveCollections)
                        || owner.Identifier.ValueText == nameof(HostileCli))
                    {
                        continue;
                    }

                    offenders.Add(Describe(path, invocation, owner));
                    continue;
                }

                if (!IsHostileCliCall(invocation))
                    continue;

                delegatedCalls++;
                TypeDeclarationSyntax? caller =
                    invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (caller is null
                    || !IsAssemblyExclusive(caller, exclusiveCollections))
                {
                    offenders.Add(Describe(path, invocation, caller));
                }
            }
        }

        return new(
            [.. offenders.Order(StringComparer.Ordinal)],
            initializers,
            delegatedCalls);
    }

    static bool IsAssemblyExclusive(
        TypeDeclarationSyntax type,
        IReadOnlySet<string> exclusiveCollections)
    {
        string? collection = type.AttributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute => AttributeName(attribute) == "Collection")
            .Select(CollectionName)
            .FirstOrDefault(name => name is not null);
        return collection is not null
            && exclusiveCollections.Contains(collection);
    }

    static bool IsCacheInitializer(
        InvocationExpressionSyntax invocation,
        IReadOnlySet<string> cacheTypeNames,
        bool importsCacheStatically) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Initialize",
            } member =>
                cacheTypeNames.Any(name =>
                    EndsWithIdentifier(member.Expression, name)),
            IdentifierNameSyntax
            {
                Identifier.ValueText: "Initialize",
            } =>
                importsCacheStatically,
            _ => false,
        };

    static HashSet<string> CacheTypeNames(SyntaxNode root)
    {
        HashSet<string> names =
            ["CoreCache", "NuGetCache"];
        foreach (UsingDirectiveSyntax directive in root
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>())
        {
            string? alias = directive.Alias?.Name.Identifier.ValueText;
            if (alias is not null
                && IsCacheTypeName(directive.Name?.ToString()))
            {
                names.Add(alias);
            }
        }
        return names;
    }

    static bool ImportsCacheStatically(SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Any(directive =>
                !directive.StaticKeyword.IsKind(SyntaxKind.None)
                && IsCacheTypeName(directive.Name?.ToString()));

    static bool IsCacheTypeName(string? name) =>
        name is not null
        && (name.EndsWith(".CoreCache", StringComparison.Ordinal)
            || name.EndsWith(".NuGetCache", StringComparison.Ordinal)
            || name is "CoreCache" or "NuGetCache");

    static bool IsHostileCliCall(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "RunAsync",
        } member
        && EndsWithIdentifier(member.Expression, nameof(HostileCli));

    static bool EndsWithIdentifier(ExpressionSyntax expression, string name) =>
        expression switch
        {
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText == name,
            MemberAccessExpressionSyntax member =>
                member.Name.Identifier.ValueText == name,
            _ => false,
        };

    static string AttributeName(AttributeSyntax attribute) =>
        attribute.Name switch
        {
            IdentifierNameSyntax identifier =>
                TrimAttributeSuffix(identifier.Identifier.ValueText),
            QualifiedNameSyntax qualified =>
                TrimAttributeSuffix(qualified.Right.Identifier.ValueText),
            AliasQualifiedNameSyntax alias =>
                TrimAttributeSuffix(alias.Name.Identifier.ValueText),
            _ => TrimAttributeSuffix(attribute.Name.ToString()),
        };

    static string TrimAttributeSuffix(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name[..^"Attribute".Length]
            : name;

    static string? CollectionName(AttributeSyntax attribute)
    {
        AttributeArgumentSyntax? argument = attribute.ArgumentList?.Arguments
            .FirstOrDefault(candidate =>
                candidate.NameEquals is null
                && candidate.NameColon is null);
        return argument?.Expression switch
        {
            LiteralExpressionSyntax literal
                when literal.IsKind(SyntaxKind.StringLiteralExpression) =>
                literal.Token.ValueText,
            IdentifierNameSyntax
                {
                    Identifier.ValueText: nameof(ConsoleCollection.Name),
                } =>
                ConsoleCollection.Name,
            MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: nameof(ConsoleCollection),
                    },
                    Name.Identifier.ValueText: nameof(ConsoleCollection.Name),
                } =>
                ConsoleCollection.Name,
            _ => null,
        };
    }

    static bool NamedBooleanArgument(
        AttributeSyntax attribute,
        string name) =>
        attribute.ArgumentList?.Arguments.Any(argument =>
            argument.NameEquals?.Name.Identifier.ValueText == name
            && argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression))
        == true;

    static string Describe(
        string path,
        InvocationExpressionSyntax invocation,
        TypeDeclarationSyntax? type)
    {
        string member = invocation
            .FirstAncestorOrSelf<MemberDeclarationSyntax>() switch
        {
            MethodDeclarationSyntax method =>
                method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor =>
                constructor.Identifier.ValueText,
            _ => "<member>",
        };
        FileLinePositionSpan line = invocation
            .GetLocation()
            .GetLineSpan();
        return $"{path}:{line.StartLinePosition.Line + 1} "
            + $"{type?.Identifier.ValueText ?? "<type>"}.{member}";
    }

    static bool IsGeneratedPath(string relativePath) =>
        relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "bin" or "obj" or "artifacts");

    sealed record SourceFile(string Path, string Content);

    sealed record CacheIsolationScanResult(
        string[] Offenders,
        int InitializerCount,
        int DelegatedCallCount);
}
