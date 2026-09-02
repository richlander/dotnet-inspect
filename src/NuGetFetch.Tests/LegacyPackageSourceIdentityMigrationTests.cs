using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NuGetFetch.Tests;

public sealed class LegacyPackageSourceIdentityMigrationTests
{
    const string LegacyTypeName = "PackageSource" + "Identity";
    const string DescriptorTypeName = "PackageSource" + "Descriptor";
    const string IdentityPropertyName = "Identity";

    [Fact]
    public void LegacyPackageSourceIdentitySurfaceMatchesMigrationSet()
    {
        DirectoryInfo root = FindRepositoryRoot();
        MigrationEntry[] actual = DiscoverReferences(root);
        MigrationEntry[] expected =
        [
            new(
                "#4795",
                "src/NuGetFetch/PackageSourceClients.cs",
                ExplicitReferences: 14,
                ImplicitReferences: 2),
            new(
                "#4795",
                "src/NuGetFetch.Tests/PackageSourceClientTests.cs",
                ExplicitReferences: 13,
                ImplicitReferences: 2),
            new(
                "#4795",
                "src/NuGetFetch.Tests/PackageSourceCustomClientTests.cs",
                ExplicitReferences: 1,
                ImplicitReferences: 0),
            new(
                "#4795",
                "src/NuGetFetch.Tests/PackageSourceResultIdentityTests.cs",
                ExplicitReferences: 9,
                ImplicitReferences: 2),
            new(
                "#4797",
                "src/DotnetInspector.Packages/NuGetCredentialScope.cs",
                ExplicitReferences: 1,
                ImplicitReferences: 0),
            new(
                "#4797",
                "src/DotnetInspector.Packages/PackagePayloadAcquisition.cs",
                ExplicitReferences: 1,
                ImplicitReferences: 0),
            new(
                "#4797",
                "src/DotnetInspector.Services.Tests/PackagePayloadAcquisitionTests.cs",
                ExplicitReferences: 2,
                ImplicitReferences: 0),
            new(
                "#4805",
                "prototypes/inspect-web/engine.Core/BrowserPackageWorkspace.cs",
                ExplicitReferences: 12,
                ImplicitReferences: 0),
            new(
                "#4805",
                "prototypes/inspect-web/engine.Tests/BrowserEngineBoundaryTests.cs",
                ExplicitReferences: 17,
                ImplicitReferences: 0),
        ];

        string? error = MigrationSetError(expected, actual);
        Assert.True(error is null, error);
    }

    [Fact]
    public void LegacyMigrationSetComparisonRejectsInventoryMutations()
    {
        var enrolled = new MigrationEntry(
            "#4795",
            "src/enrolled.cs",
            ExplicitReferences: 1,
            ImplicitReferences: 1);
        var other = new MigrationEntry(
            "#4805",
            "src/other.cs",
            ExplicitReferences: 1,
            ImplicitReferences: 0);

        Assert.Null(MigrationSetError([enrolled], [enrolled]));
        Assert.Contains(
            "unlisted",
            MigrationSetError([enrolled], [enrolled, other]),
            StringComparison.Ordinal);
        Assert.Contains(
            "stale",
            MigrationSetError([enrolled, other], [enrolled]),
            StringComparison.Ordinal);
        Assert.Contains(
            "reference drift",
            MigrationSetError(
                [enrolled],
                [enrolled with { ExplicitReferences = 2 }]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyReferenceDiscoveryIncludesImplicitFormattingAndEquality()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"legacy-source-inventory-{Guid.NewGuid():N}");
        string sourceRoot = Path.Combine(root, "tests");
        Directory.CreateDirectory(sourceRoot);
        string descriptorType = DescriptorTypeName;
        string identityMember = "." + "Identity";
        string source = $$"""
            namespace NuGetFetch
            {
                sealed class {{descriptorType}}
                {
                    public object {{IdentityPropertyName}} => new();
                }
            }
            namespace Probe
            {
                using NuGetFetch;

                sealed class Consumer
                {
                    void Read({{descriptorType}} first, {{descriptorType}} second)
                    {
                        var alias = first;
                        _ = $"{alias{{identityMember}}}";
                        _ = alias{{identityMember}} == second{{identityMember}};
                        _ = alias?{{identityMember}};
                        _ = alias is { {{IdentityPropertyName}}: not null };
                        _ = "alias.Identity";
                        // alias.Identity is not an executable reader.
                    }
                }
            }
            """;

        try
        {
            File.WriteAllText(
                Path.Combine(sourceRoot, "AliasedDescriptorReader.cs"),
                source);

            MigrationEntry reader =
                Assert.Single(DiscoverReferences(new DirectoryInfo(root)));
            Assert.Equal(0, reader.ExplicitReferences);
            Assert.Equal(5, reader.ImplicitReferences);
            Assert.Contains(
                "unlisted",
                MigrationSetError([], [reader]),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyReferenceDiscoveryIncludesAliasesAndInactiveBranches()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"legacy-source-inventory-{Guid.NewGuid():N}");
        string sourceRoot = Path.Combine(root, "tests");
        Directory.CreateDirectory(sourceRoot);

        try
        {
            File.WriteAllText(
                Path.Combine(sourceRoot, "LegacyDefinition.cs"),
                $$"""
                namespace NuGetFetch;
                sealed class {{LegacyTypeName}}
                {
                }
                sealed class {{DescriptorTypeName}}
                {
                    public object {{IdentityPropertyName}} => new();
                }
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "GlobalAlias.cs"),
                $$"""
                #if CONDITIONAL_GLOBAL_ALIAS
                global using LegacyIdentity =
                    NuGetFetch.{{LegacyTypeName}};
                #endif
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "GlobalAliasConsumer.cs"),
                """
                namespace Probe;
                sealed class GlobalAliasConsumer
                {
                    LegacyIdentity? Read() => null;
                }
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "LocalAliasConsumer.cs"),
                $$"""
                #if CONDITIONAL_LOCAL_ALIAS
                using LocalIdentity =
                    NuGetFetch.{{LegacyTypeName}};
                #endif
                namespace Probe;
                sealed class LocalAliasConsumer
                {
                    LocalIdentity? Read() => null;
                }
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "InactiveConsumer.cs"),
                $$"""
                #if LEGACY_INACTIVE_BRANCH
                namespace Probe;
                sealed class InactiveConsumer
                {
                    NuGetFetch.{{LegacyTypeName}}? Read() => null;
                    LegacyIdentity? ReadAlias() => null;
                    object ReadDescriptor(
                        NuGetFetch.{{DescriptorTypeName}} descriptor) =>
                        descriptor.{{IdentityPropertyName}};
                }
                #endif
                """);
            File.WriteAllText(
                Path.Combine(
                    sourceRoot,
                    "ConditionalDescriptorConsumer.cs"),
                $$"""
                #if CONDITIONAL_DESCRIPTOR_ALIAS
                using DescriptorAlias =
                    NuGetFetch.{{DescriptorTypeName}};
                #else
                using DescriptorAlias = Probe.OtherDescriptor;
                #endif
                namespace Probe;
                sealed class OtherDescriptor
                {
                    public object {{IdentityPropertyName}} => new();
                }
                sealed class ConditionalDescriptorConsumer
                {
                    object Read(DescriptorAlias descriptor) =>
                        descriptor.{{IdentityPropertyName}};
                }
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "ConditionalUsing.cs"),
                """
                #if CONDITIONAL_NUGETFETCH_USING
                global using NuGetFetch;
                #endif
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "ConditionalUsingConsumer.cs"),
                $$"""
                namespace Probe;
                sealed class ConditionalUsingConsumer
                {
                    {{LegacyTypeName}}? Read() => null;
                }
                """);

            Dictionary<string, MigrationEntry> readers =
                DiscoverReferences(new DirectoryInfo(root))
                    .ToDictionary(entry => entry.Path, StringComparer.Ordinal);
            Assert.Equal(
                1,
                readers["tests/LegacyDefinition.cs"].ExplicitReferences);
            Assert.Equal(
                1,
                readers["tests/GlobalAlias.cs"].ExplicitReferences);
            Assert.Equal(
                1,
                readers["tests/GlobalAliasConsumer.cs"].ExplicitReferences);
            Assert.Equal(
                2,
                readers["tests/LocalAliasConsumer.cs"].ExplicitReferences);
            Assert.Equal(
                2,
                readers["tests/InactiveConsumer.cs"].ExplicitReferences);
            Assert.Equal(
                1,
                readers["tests/InactiveConsumer.cs"].ImplicitReferences);
            Assert.Equal(
                1,
                readers[
                    "tests/ConditionalDescriptorConsumer.cs"]
                    .ImplicitReferences);
            Assert.Equal(
                1,
                readers["tests/ConditionalUsingConsumer.cs"]
                    .ExplicitReferences);
            Assert.All(
                readers.Values.Where(entry =>
                    entry.Path is not
                        "tests/InactiveConsumer.cs"
                        and not
                        "tests/ConditionalDescriptorConsumer.cs"),
                entry => Assert.Equal(0, entry.ImplicitReferences));
            Assert.Contains(
                "unlisted",
                MigrationSetError([], [.. readers.Values]),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static MigrationEntry[] DiscoverReferences(DirectoryInfo root)
    {
        string[] paths =
        [
            .. Directory.EnumerateFiles(
                root.FullName,
                "*.cs",
                SearchOption.TopDirectoryOnly),
            .. Directory
                .EnumerateDirectories(root.FullName)
                .Where(path => !IsExcludedSourceRoot(path))
                .SelectMany(path =>
                    Directory.EnumerateFiles(
                        path,
                        "*.cs",
                        SearchOption.AllDirectories))
                .Where(path => !IsBuildOutput(root.FullName, path))
                .Order(StringComparer.Ordinal),
        ];
        SourceDocument[] sources =
        [
            .. paths.Select(path =>
                new SourceDocument(path, File.ReadAllText(path))),
        ];
        SyntaxTree[] baseline = ParseSources(sources, []);
        string[] conditionalSymbols = DiscoverConditionalSymbols(baseline);
        Dictionary<string, SyntaxTree> baselineByPath =
            baseline.ToDictionary(
                tree => tree.FilePath,
                StringComparer.Ordinal);
        HashSet<string> conditionalPaths =
        [
            .. baseline
                .Where(HasConditionalDirectives)
                .Select(tree => tree.FilePath),
        ];
        Assert.True(
            conditionalSymbols.Length <= 8,
            "The exhaustive legacy migration inventory supports at most "
            + "eight conditional-compilation symbols; observed "
            + $"[{string.Join(", ", conditionalSymbols)}].");
        Dictionary<string, ReferenceLocations> references =
            sources.ToDictionary(
                source => source.Path,
                _ => new ReferenceLocations(),
                StringComparer.Ordinal);
        CSharpCompilation compilation = CreateCompilation(baseline);
        INamedTypeSymbol? legacyType =
            compilation.GetTypeByMetadataName(
                $"NuGetFetch.{LegacyTypeName}");
        IPropertySymbol? descriptorIdentity =
            compilation
                .GetTypeByMetadataName(
                    $"NuGetFetch.{DescriptorTypeName}")
                ?.GetMembers(IdentityPropertyName)
                .OfType<IPropertySymbol>()
                .SingleOrDefault();
        HashSet<string> legacyAliases =
            DiscoverLegacyAliases(
                baseline,
                compilation,
                legacyType);
        Dictionary<string, HashSet<int>> baselineSyntaxLocations =
            baseline.ToDictionary(
                tree => tree.FilePath,
                tree => ActiveSyntaxLocations(tree.GetRoot()),
                StringComparer.Ordinal);

        foreach (SyntaxTree tree in baseline)
        {
            SyntaxNode syntax = tree.GetRoot();
            if (!HasPotentialReferences(syntax, legacyAliases))
                continue;

            SemanticModel semantics = compilation.GetSemanticModel(tree);
            ReferenceLocations locations = references[tree.FilePath];
            locations.Explicit.UnionWith(
                ExplicitReferenceLocations(
                    syntax,
                    semantics,
                    legacyType,
                    legacyAliases));
            locations.Implicit.UnionWith(
                ImplicitReferenceLocations(
                    syntax,
                    semantics,
                    descriptorIdentity));
        }

        int configurationCount = 1 << conditionalSymbols.Length;
        for (int mask = 0; mask < configurationCount; mask++)
        {
            string[] definedSymbols =
            [
                .. conditionalSymbols.Where(
                    (_, index) => (mask & (1 << index)) != 0),
            ];
            SyntaxTree[] trees = ParseConditionalSources(
                sources,
                definedSymbols,
                baselineByPath,
                conditionalPaths);
            AddSyntacticLegacyAliases(
                [
                    .. trees.Where(tree =>
                        conditionalPaths.Contains(tree.FilePath)),
                ],
                legacyAliases);
        }

        HashSet<string> analyzedBindingConfigurations =
            new(StringComparer.Ordinal)
            {
                BindingConfigurationFingerprint(
                    baseline.Where(tree =>
                        conditionalPaths.Contains(tree.FilePath))),
            };
        for (int mask = 0; mask < configurationCount; mask++)
        {
            string[] definedSymbols =
            [
                .. conditionalSymbols.Where(
                    (_, index) => (mask & (1 << index)) != 0),
            ];
            SyntaxTree[] trees = ParseConditionalSources(
                sources,
                definedSymbols,
                baselineByPath,
                conditionalPaths);
            string bindingConfiguration =
                BindingConfigurationFingerprint(
                    trees.Where(tree =>
                        conditionalPaths.Contains(tree.FilePath)));
            if (!analyzedBindingConfigurations.Add(bindingConfiguration))
                continue;

            CSharpCompilation configuredCompilation =
                CreateCompilation(trees);
            INamedTypeSymbol? configuredLegacyType =
                configuredCompilation.GetTypeByMetadataName(
                    $"NuGetFetch.{LegacyTypeName}");
            IPropertySymbol? configuredDescriptorIdentity =
                configuredCompilation
                    .GetTypeByMetadataName(
                        $"NuGetFetch.{DescriptorTypeName}")
                    ?.GetMembers(IdentityPropertyName)
                    .OfType<IPropertySymbol>()
                    .SingleOrDefault();
            HashSet<string> configuredLegacyAliases =
                DiscoverLegacyAliases(
                    trees,
                    configuredCompilation,
                    configuredLegacyType);

            foreach (SyntaxTree tree in trees)
            {
                SyntaxNode syntax = tree.GetRoot();
                if (!HasPotentialReferences(
                        syntax,
                        configuredLegacyAliases))
                {
                    continue;
                }

                SemanticModel semantics =
                    configuredCompilation.GetSemanticModel(tree);
                ReferenceLocations locations = references[tree.FilePath];
                locations.Explicit.UnionWith(
                    ExplicitReferenceLocations(
                        syntax,
                        semantics,
                        configuredLegacyType,
                        configuredLegacyAliases));
                locations.Implicit.UnionWith(
                    ImplicitReferenceLocations(
                        syntax,
                        semantics,
                        configuredDescriptorIdentity));
            }
        }

        for (int mask = 0; mask < configurationCount; mask++)
        {
            string[] definedSymbols =
            [
                .. conditionalSymbols.Where(
                    (_, index) => (mask & (1 << index)) != 0),
            ];
            SyntaxTree[] trees = ParseConditionalSources(
                sources,
                definedSymbols,
                baselineByPath,
                conditionalPaths);
            foreach (SyntaxTree tree in trees.Where(tree =>
                         conditionalPaths.Contains(tree.FilePath)))
            {
                SyntaxNode syntax = tree.GetRoot();
                ReferenceLocations locations = references[tree.FilePath];
                IReadOnlySet<int> active =
                    baselineSyntaxLocations[tree.FilePath];
                locations.Explicit.UnionWith(
                    InactiveExplicitReferenceLocations(
                        syntax,
                        active,
                        legacyAliases));
                locations.Implicit.UnionWith(
                    InactiveImplicitReferenceLocations(
                        syntax,
                        active));
            }
        }

        return
        [
            .. sources
                .Select(source =>
                {
                    ReferenceLocations locations = references[source.Path];
                    return new MigrationEntry(
                        Issue: "",
                        Path.GetRelativePath(root.FullName, source.Path)
                            .Replace('\\', '/'),
                        locations.Explicit.Count,
                        locations.Implicit.Count);
                })
                .Where(entry =>
                    entry.ExplicitReferences != 0
                    || entry.ImplicitReferences != 0)
                .OrderBy(entry => entry.Path, StringComparer.Ordinal),
        ];
    }

    static void AddSyntacticLegacyAliases(
        IReadOnlyList<SyntaxTree> trees,
        HashSet<string> aliases)
    {
        foreach (UsingDirectiveSyntax directive in
                 trees.SelectMany(tree =>
                     tree.GetRoot()
                         .DescendantNodes()
                         .OfType<UsingDirectiveSyntax>()))
        {
            if (directive is
                    {
                        Alias: { } alias,
                        Name: { } name,
                    }
                && name.DescendantNodesAndSelf()
                    .OfType<SimpleNameSyntax>()
                    .Any(candidate =>
                        candidate.Identifier.ValueText
                            == LegacyTypeName))
            {
                aliases.Add(alias.Name.Identifier.ValueText);
            }
        }
    }

    static string BindingConfigurationFingerprint(
        IEnumerable<SyntaxTree> trees) =>
        string.Join(
            '\n',
            trees.SelectMany(tree =>
                    tree.GetRoot()
                        .DescendantNodes()
                        .Where(node => node switch
                        {
                            UsingDirectiveSyntax => true,
                            BaseNamespaceDeclarationSyntax => true,
                            TypeDeclarationSyntax declaration =>
                                declaration.Identifier.ValueText
                                    is LegacyTypeName
                                    or DescriptorTypeName,
                            TypeParameterSyntax parameter =>
                                parameter.Identifier.ValueText
                                    is LegacyTypeName
                                    or DescriptorTypeName,
                            _ => false,
                        })
                        .Select(node =>
                            $"{tree.FilePath}:{node.SpanStart}:{node}"))
                .Order(StringComparer.Ordinal));

    static HashSet<int> ActiveSyntaxLocations(SyntaxNode syntax) =>
        [
            .. syntax.DescendantNodes()
                .Where(node =>
                    node is TypeDeclarationSyntax
                        or ConstructorDeclarationSyntax
                        or SimpleNameSyntax)
                .Select(node => node.SpanStart),
        ];

    static IEnumerable<int> InactiveExplicitReferenceLocations(
        SyntaxNode syntax,
        IReadOnlySet<int> active,
        IReadOnlySet<string> legacyAliases)
    {
        foreach (TypeDeclarationSyntax declaration in
                 syntax.DescendantNodes()
                     .OfType<TypeDeclarationSyntax>())
        {
            if (!active.Contains(declaration.SpanStart)
                && declaration.Identifier.ValueText == LegacyTypeName)
            {
                yield return declaration.Identifier.SpanStart;
            }
        }

        foreach (ConstructorDeclarationSyntax constructor in
                 syntax.DescendantNodes()
                     .OfType<ConstructorDeclarationSyntax>())
        {
            if (!active.Contains(constructor.SpanStart)
                && constructor.Identifier.ValueText == LegacyTypeName)
            {
                yield return constructor.Identifier.SpanStart;
            }
        }

        foreach (SimpleNameSyntax name in
                 syntax.DescendantNodes()
                     .OfType<SimpleNameSyntax>())
        {
            if (!active.Contains(name.SpanStart)
                && name.Parent is not NameEqualsSyntax
                && legacyAliases.Contains(name.Identifier.ValueText))
            {
                yield return name.SpanStart;
            }
        }
    }

    static IEnumerable<int> InactiveImplicitReferenceLocations(
        SyntaxNode syntax,
        IReadOnlySet<int> active) =>
        syntax.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Where(name =>
                !active.Contains(name.SpanStart)
                && name.Identifier.ValueText == IdentityPropertyName)
            .Select(name => name.SpanStart);

    static IEnumerable<int> ExplicitReferenceLocations(
        SyntaxNode syntax,
        SemanticModel semantics,
        INamedTypeSymbol? legacyType,
        IReadOnlySet<string> legacyAliases)
    {
        if (legacyType is null)
            yield break;

        foreach (TypeDeclarationSyntax declaration in
                 syntax.DescendantNodes()
                     .OfType<TypeDeclarationSyntax>())
        {
            if (SymbolEqualityComparer.Default.Equals(
                    semantics.GetDeclaredSymbol(declaration),
                    legacyType))
            {
                yield return declaration.Identifier.SpanStart;
            }
        }

        foreach (ConstructorDeclarationSyntax constructor in
                 syntax.DescendantNodes()
                     .OfType<ConstructorDeclarationSyntax>())
        {
            if (semantics.GetDeclaredSymbol(constructor)
                    is IMethodSymbol method
                && SymbolEqualityComparer.Default.Equals(
                    method.ContainingType,
                    legacyType))
            {
                yield return constructor.Identifier.SpanStart;
            }
        }

        foreach (SimpleNameSyntax name in
                 syntax.DescendantNodes()
                     .OfType<SimpleNameSyntax>())
        {
            if (name.Parent is NameEqualsSyntax
                || !legacyAliases.Contains(name.Identifier.ValueText)
                || !TargetsLegacyType(name, semantics, legacyType))
            {
                continue;
            }

            yield return name.SpanStart;
        }
    }

    static IEnumerable<int> ImplicitReferenceLocations(
        SyntaxNode syntax,
        SemanticModel semantics,
        IPropertySymbol? descriptorIdentity)
    {
        if (descriptorIdentity is null)
            yield break;

        foreach (SimpleNameSyntax name in
                 syntax.DescendantNodes()
            .OfType<SimpleNameSyntax>()
                     .Where(name =>
                         name.Identifier.ValueText
                             == IdentityPropertyName))
        {
            if (SymbolEqualityComparer.Default.Equals(
                    semantics.GetSymbolInfo(name).Symbol,
                    descriptorIdentity))
            {
                yield return name.SpanStart;
            }
        }
    }

    static HashSet<string> DiscoverLegacyAliases(
        IReadOnlyList<SyntaxTree> trees,
        CSharpCompilation compilation,
        INamedTypeSymbol? legacyType)
    {
        HashSet<string> aliases =
            new(StringComparer.Ordinal)
            {
                LegacyTypeName,
            };
        if (legacyType is null)
            return aliases;

        foreach (SyntaxTree tree in trees)
        {
            UsingDirectiveSyntax[] aliasDirectives =
            [
                .. tree.GetRoot()
                    .DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Where(directive => directive.Alias is not null),
            ];
            if (aliasDirectives.Length == 0)
                continue;

            SemanticModel semantics = compilation.GetSemanticModel(tree);
            foreach (UsingDirectiveSyntax directive in aliasDirectives)
            {
                if (directive is
                        {
                            Alias: { } alias,
                            Name: { } name,
                        }
                    && TargetsLegacyType(
                        name,
                        semantics,
                        legacyType))
                {
                    aliases.Add(alias.Name.Identifier.ValueText);
                }
            }
        }

        return aliases;
    }

    static bool HasPotentialReferences(
        SyntaxNode syntax,
        IReadOnlySet<string> legacyAliases) =>
        syntax.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Any(declaration =>
                declaration.Identifier.ValueText == LegacyTypeName)
        || syntax.DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .Any(constructor =>
                constructor.Identifier.ValueText == LegacyTypeName)
        || syntax.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Any(name =>
                name.Identifier.ValueText == IdentityPropertyName
                || legacyAliases.Contains(name.Identifier.ValueText));

    static bool TargetsLegacyType(
        SyntaxNode syntax,
        SemanticModel semantics,
        INamedTypeSymbol legacyType)
    {
        ISymbol? symbol = semantics.GetSymbolInfo(syntax).Symbol;
        if (symbol is IAliasSymbol alias)
            symbol = alias.Target;

        if (symbol is IMethodSymbol
            {
                MethodKind: MethodKind.Constructor,
            } constructor)
        {
            symbol = constructor.ContainingType;
        }

        return SymbolEqualityComparer.Default.Equals(symbol, legacyType)
            || SymbolEqualityComparer.Default.Equals(
                semantics.GetTypeInfo(syntax).Type,
                legacyType);
    }

    static SyntaxTree[] ParseSources(
        IReadOnlyList<SourceDocument> sources,
        IReadOnlyList<string> definedSymbols)
    {
        CSharpParseOptions options = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(definedSymbols);
        return
        [
            .. sources.Select(source =>
                CSharpSyntaxTree.ParseText(
                    source.Text,
                    options,
                    source.Path)),
        ];
    }

    static SyntaxTree[] ParseConditionalSources(
        IReadOnlyList<SourceDocument> sources,
        IReadOnlyList<string> definedSymbols,
        IReadOnlyDictionary<string, SyntaxTree> baselineByPath,
        IReadOnlySet<string> conditionalPaths)
    {
        CSharpParseOptions options = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(definedSymbols);
        return
        [
            .. sources.Select(source =>
                conditionalPaths.Contains(source.Path)
                    ? CSharpSyntaxTree.ParseText(
                        source.Text,
                        options,
                        source.Path)
                    : baselineByPath[source.Path]),
        ];
    }

    static bool HasConditionalDirectives(SyntaxTree tree) =>
        tree.GetRoot()
            .DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .Any(structure =>
                structure is IfDirectiveTriviaSyntax
                    or ElifDirectiveTriviaSyntax);

    static string[] DiscoverConditionalSymbols(
        IReadOnlyList<SyntaxTree> trees) =>
        [
            .. trees
                .SelectMany(tree =>
                    tree.GetRoot()
                        .DescendantTrivia(descendIntoTrivia: true))
                .Select(trivia => trivia.GetStructure())
                .SelectMany(structure => structure switch
                {
                    IfDirectiveTriviaSyntax directive =>
                        ConditionalSymbols(directive.Condition),
                    ElifDirectiveTriviaSyntax directive =>
                        ConditionalSymbols(directive.Condition),
                    _ => [],
                })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    static IEnumerable<string> ConditionalSymbols(
        ExpressionSyntax condition) =>
        condition.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(name => name.Identifier.ValueText);

    static bool IsExcludedSourceRoot(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith(".", StringComparison.Ordinal)
            || name is "artifacts" or "node_modules";
    }

    static CSharpCompilation CreateCompilation(
        IReadOnlyList<SyntaxTree> trees)
    {
        string trustedPlatformAssemblies =
            Assert.IsType<string>(
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
        MetadataReference[] references =
        [
            .. trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path =>
                    MetadataReference.CreateFromFile(path)),
        ];
        return CSharpCompilation.Create(
            "LegacyPackageSourceIdentityMigrationInventory",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    static string? MigrationSetError(
        IReadOnlyList<MigrationEntry> expected,
        IReadOnlyList<MigrationEntry> actual)
    {
        Dictionary<string, MigrationEntry> expectedByPath =
            expected.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        Dictionary<string, MigrationEntry> actualByPath =
            actual.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        string[] unlisted =
        [
            .. actualByPath.Keys
                .Except(expectedByPath.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string[] stale =
        [
            .. expectedByPath.Keys
                .Except(actualByPath.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string[] drift =
        [
            .. expectedByPath.Keys
                .Intersect(actualByPath.Keys, StringComparer.Ordinal)
                .Where(path =>
                    expectedByPath[path].ExplicitReferences
                        != actualByPath[path].ExplicitReferences
                    || expectedByPath[path].ImplicitReferences
                        != actualByPath[path].ImplicitReferences)
                .Order(StringComparer.Ordinal)
                .Select(path =>
                    $"{path} expected "
                    + $"{expectedByPath[path].ExplicitReferences} explicit/"
                    + $"{expectedByPath[path].ImplicitReferences} implicit "
                    + $"for {expectedByPath[path].Issue}, observed "
                    + $"{actualByPath[path].ExplicitReferences} explicit/"
                    + $"{actualByPath[path].ImplicitReferences} implicit"),
        ];

        if (unlisted.Length == 0 && stale.Length == 0 && drift.Length == 0)
            return null;

        return "Legacy package-source identity migration inventory mismatch: "
            + $"unlisted [{string.Join(", ", unlisted)}]; "
            + $"stale [{string.Join(", ", stale)}]; "
            + $"reference drift [{string.Join("; ", drift)}].";
    }

    static bool IsBuildOutput(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.Split(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment is "bin" or "obj" or "artifacts");
    }

    static DirectoryInfo FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                 new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test binary.");
    }

    sealed record MigrationEntry(
        string Issue,
        string Path,
        int ExplicitReferences,
        int ImplicitReferences);

    sealed record SourceDocument(string Path, string Text);

    sealed class ReferenceLocations
    {
        public HashSet<int> Explicit { get; } = [];
        public HashSet<int> Implicit { get; } = [];
    }
}
