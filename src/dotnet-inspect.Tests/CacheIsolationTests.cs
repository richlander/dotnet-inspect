using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetInspector.Tests;

public sealed class CacheIsolationTests
{
    const string HostileCliType = "DotnetInspector.Tests.HostileCli";

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
    public void CacheIsolationScan_RejectsCompilationAndOwnershipEvasions()
    {
        CacheIsolationScanResult result = Scan(
        [
            new(
                "GlobalUsings.cs",
                """
                global using CacheAlias = DotnetInspector.Packages.NuGetCache;
                global using static DotnetInspector.Core.CoreCache;
                """),
            new(
                "Synthetic.cs",
                """
                namespace DotnetInspector.Tests;

                [CollectionDefinition("Exclusive", DisableParallelization = true)]
                public sealed class ExclusiveCollection;

                [CollectionDefinition(ParallelCollection.Name)]
                public sealed class ParallelCollection
                {
                    public const string Name = "Parallel";
                }

                [Collection("Exclusive")]
                public sealed class Safe
                {
                    [Fact]
                    public void Run() => NuGetCache.Initialize("safe");
                }

                public sealed class Unsafe
                {
                    [Fact]
                    public void Run() => CoreCache.Initialize("unsafe");

                    [Fact]
                    public void Alias() => CacheAlias.Initialize("alias");

                    [Fact]
                    public void StaticImport() => Initialize("static");

                    [Fact]
                    public void Delegate() => HostileCli.RunAsync();

                #if NET
                    [Fact]
                    public void Conditional() => CacheAlias.Initialize("conditional");
                #endif
                }

                [Collection("Exclusive")]
                internal static class LaunderingHelper
                {
                    public static void Run() => NuGetCache.Initialize("laundered");
                }

                public sealed class LaunderingCaller
                {
                    [Fact]
                    public void Run() => LaunderingHelper.Run();
                }

                [Collection(ParallelCollection.Name)]
                public sealed class ParallelTests
                {
                    [Fact]
                    public void Run() => NuGetCache.Initialize("parallel");
                }

                internal static class HostileCli
                {
                    public static void RunAsync() =>
                        NuGetCache.Initialize("delegated");
                }
                """),
            new(
                "PartialA.cs",
                """
                namespace DotnetInspector.Tests;

                [Collection("Exclusive")]
                public sealed partial class PartialSafe;
                """),
            new(
                "PartialB.cs",
                """
                namespace DotnetInspector.Tests;

                public sealed partial class PartialSafe
                {
                    [Fact]
                    public void Run() => NuGetCache.Initialize("partial");
                }
                """),
        ]);

        Assert.Equal(9, result.InitializerCount);
        Assert.Equal(1, result.DelegatedCallCount);
        Assert.Equal(7, result.Offenders.Length);
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.Run"));
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.Alias"));
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.StaticImport"));
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.Delegate"));
        Assert.Contains(result.Offenders, offender => offender.Contains("Unsafe.Conditional"));
        Assert.Contains(result.Offenders, offender => offender.Contains("LaunderingHelper.Run"));
        Assert.Contains(result.Offenders, offender => offender.Contains("ParallelTests.Run"));
    }

    static CacheIsolationScanResult Scan(IReadOnlyCollection<SourceFile> sources)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(ProjectPreprocessorSymbols());
        SyntaxTree[] trees =
        [
            CSharpSyntaxTree.ParseText(
                """
                global using System;
                global using System.Collections.Generic;
                global using System.IO;
                global using System.Linq;
                global using System.Net.Http;
                global using System.Threading;
                global using System.Threading.Tasks;
                global using Xunit;
                global using DotnetInspector.Core;
                global using DotnetInspector.Packages;
                global using ILInspector.Instructions;
                global using ILInspector.SourceLink;
                """,
                parseOptions,
                "__CacheIsolationGlobalUsings.g.cs"),
            .. sources.Select(source =>
                CSharpSyntaxTree.ParseText(
                    source.Content,
                    parseOptions,
                    source.Path)),
        ];
        CSharpCompilation compilation = CSharpCompilation.Create(
            "CacheIsolationCensus",
            trees,
            CompilationReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true));
        HashSet<string> exclusiveCollectionNames =
            ExclusiveCollectionNames(compilation);
        List<string> offenders = [];
        int initializers = 0;
        int delegatedCalls = 0;

        foreach (SyntaxTree tree in trees.Skip(1))
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (InvocationExpressionSyntax invocation in tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                IMethodSymbol[] targets = InvokedMethods(model, invocation);
                IMethodSymbol? initializer =
                    targets.FirstOrDefault(IsCacheInitializer);
                if (initializer is not null)
                {
                    initializers++;
                    IMethodSymbol? owner = EnclosingMethod(model, invocation);
                    if (IsExactHostileCliRunAsync(owner))
                        continue;

                    if (!IsAssemblyExclusiveTestClass(
                        owner?.ContainingType,
                        exclusiveCollectionNames))
                    {
                        offenders.Add(Describe(invocation, owner));
                    }
                    continue;
                }

                if (!targets.Any(IsExactHostileCliRunAsync))
                    continue;

                delegatedCalls++;
                IMethodSymbol? caller = EnclosingMethod(model, invocation);
                if (!IsAssemblyExclusiveTestClass(
                    caller?.ContainingType,
                    exclusiveCollectionNames))
                {
                    offenders.Add(Describe(invocation, caller));
                }
            }
        }

        return new(
            [.. offenders.Order(StringComparer.Ordinal)],
            initializers,
            delegatedCalls);
    }

    static IEnumerable<MetadataReference> CompilationReferences()
    {
        string testAssembly = typeof(CacheIsolationTests).Assembly.Location;
        IEnumerable<string> platformAssemblies =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        IEnumerable<string> projectAssemblies = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .Where(path => !string.Equals(
                path,
                testAssembly,
                StringComparison.OrdinalIgnoreCase));

        return platformAssemblies
            .Concat(projectAssemblies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    static IEnumerable<string> ProjectPreprocessorSymbols()
    {
        HashSet<string> symbols = new(StringComparer.Ordinal);
        string? constants = typeof(CacheIsolationTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                attribute.Key == "DefineConstants")
            ?.Value;
        if (constants is not null)
        {
            symbols.UnionWith(constants.Split(
                [';', ','],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
        }

        var framework = new FrameworkName(
            AppContext.TargetFrameworkName
                ?? throw new InvalidOperationException(
                    "The test target framework is unavailable."));
        if (framework.Identifier == ".NETCoreApp")
        {
            symbols.Add("NET");
            symbols.Add("NETCOREAPP");
            symbols.Add(
                $"NET{framework.Version.Major}_{framework.Version.Minor}");
            for (int major = 5; major <= framework.Version.Major; major++)
                symbols.Add($"NET{major}_0_OR_GREATER");
        }

        return symbols;
    }

    static HashSet<string> ExclusiveCollectionNames(
        CSharpCompilation compilation) =>
        AllTypes(compilation.Assembly.GlobalNamespace)
            .SelectMany(type => type.GetAttributes())
            .Where(IsExclusiveCollectionDefinition)
            .Select(attribute =>
                attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol scope)
    {
        foreach (INamedTypeSymbol type in scope.GetTypeMembers())
        {
            yield return type;
            foreach (INamedTypeSymbol nested in AllTypes(type))
                yield return nested;
        }
        foreach (INamespaceSymbol child in scope.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in AllTypes(child))
                yield return type;
        }
    }

    static IEnumerable<INamedTypeSymbol> AllTypes(INamedTypeSymbol scope)
    {
        foreach (INamedTypeSymbol type in scope.GetTypeMembers())
        {
            yield return type;
            foreach (INamedTypeSymbol nested in AllTypes(type))
                yield return nested;
        }
    }

    static bool IsExclusiveCollectionDefinition(AttributeData attribute) =>
        IsXunitAttribute(attribute, "CollectionDefinitionAttribute")
        && attribute.NamedArguments.Any(argument =>
            argument.Key == "DisableParallelization"
            && argument.Value.Value is true);

    static bool IsAssemblyExclusiveTestClass(
        INamedTypeSymbol? type,
        IReadOnlySet<string> exclusiveCollectionNames)
    {
        if (type is null || !IsTestClass(type))
            return false;

        foreach (AttributeData attribute in type.GetAttributes()
            .Where(attribute =>
                IsXunitAttribute(attribute, "CollectionAttribute")))
        {
            TypedConstant argument =
                attribute.ConstructorArguments.FirstOrDefault();
            if (argument.Value is string name
                && exclusiveCollectionNames.Contains(name))
            {
                return true;
            }
            if (argument.Kind == TypedConstantKind.Type
                && argument.Value is INamedTypeSymbol definition
                && definition.GetAttributes().Any(
                    IsExclusiveCollectionDefinition))
            {
                return true;
            }
        }

        AttributeData? genericCollection = type.GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass is { IsGenericType: true } attributeType
                && IsXunitAttribute(
                    attribute,
                    "CollectionAttribute")
                && attributeType.TypeArguments[0]
                    is INamedTypeSymbol definition
                && definition.GetAttributes().Any(
                    IsExclusiveCollectionDefinition));
        return genericCollection is not null;
    }

    static bool IsTestClass(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(method => method.GetAttributes().Any(IsTestAttribute));

    static bool IsTestAttribute(AttributeData attribute)
    {
        for (INamedTypeSymbol? type = attribute.AttributeClass;
             type is not null;
             type = type.BaseType)
        {
            if (type.ContainingNamespace.ToDisplayString() == "Xunit"
                && type.Name is "FactAttribute" or "TheoryAttribute")
            {
                return true;
            }
        }
        return false;
    }

    static bool IsXunitAttribute(
        AttributeData attribute,
        string name) =>
        attribute.AttributeClass is { } type
        && type.ContainingNamespace.ToDisplayString() == "Xunit"
        && type.OriginalDefinition.Name == name;

    static IMethodSymbol[] InvokedMethods(
        SemanticModel model,
        InvocationExpressionSyntax invocation)
    {
        SymbolInfo info = model.GetSymbolInfo(invocation);
        return info.Symbol is IMethodSymbol method
            ? [method]
            : [.. info.CandidateSymbols.OfType<IMethodSymbol>()];
    }

    static IMethodSymbol? EnclosingMethod(
        SemanticModel model,
        InvocationExpressionSyntax invocation)
    {
        ISymbol? owner = model.GetEnclosingSymbol(invocation.SpanStart);
        while (owner is not null and not IMethodSymbol)
            owner = owner.ContainingSymbol;
        var method = owner as IMethodSymbol;
        while (method?.MethodKind is
            MethodKind.AnonymousFunction or MethodKind.LocalFunction)
        {
            owner = method.ContainingSymbol;
            while (owner is not null and not IMethodSymbol)
                owner = owner.ContainingSymbol;
            method = owner as IMethodSymbol;
        }
        return method;
    }

    static bool IsCacheInitializer(IMethodSymbol? method) =>
        method is
        {
            Name: "Initialize",
            ContainingType: { } type,
        }
        && type.ToDisplayString() is
            "DotnetInspector.Core.CoreCache"
            or "DotnetInspector.Packages.NuGetCache";

    static bool IsExactHostileCliRunAsync(IMethodSymbol? method) =>
        method?.Name == "RunAsync"
        && method.ContainingType?.ToDisplayString() == HostileCliType;

    static string Describe(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method)
    {
        FileLinePositionSpan line = invocation
            .GetLocation()
            .GetLineSpan();
        return $"{line.Path}:{line.StartLinePosition.Line + 1} "
            + $"{method?.ContainingType?.Name ?? "<type>"}."
            + $"{method?.Name ?? "<member>"}";
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
