using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InspectWeb.Engine.Tests;

/// <summary>
/// Pins the browser engine's layering rule: every interaction that inspects an assembly runs
/// inside a workspace, through a public product query that owns the session, the metadata source,
/// and the analysis index.
/// </summary>
/// <remarks>
/// <para>
/// The first is that the ban list is not vacuous: a banned identifier that no longer resolves
/// bans nothing, and the analyzer reports no such entry. <see cref="EveryBannedSymbolStillExists"/>
/// resolves each entry against the product assemblies so a rename fails here rather than silently
/// reopening the door.
/// </para>
/// <para>
/// The second is that package selection and participant realization stay in product code, so the
/// engine cannot decode raw images or mint descriptors.
/// </para>
/// </remarks>
public sealed class BrowserEngineLayeringTests
{
    [Fact]
    public void BanListForbidsEverySessionAndImageDoor()
    {
        IReadOnlyList<string> banned = BannedSymbols();

        Assert.Contains("T:ILInspector.Metadata.AssemblyInspectionSession", banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyImage", banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyImageSnapshot", banned);
        Assert.Contains("T:ILInspector.Metadata.PdbContext", banned);
        Assert.Contains(
            "T:ILInspector.Metadata.AssemblyTypeDeclarationInventoryReader",
            banned);
        Assert.Contains(
            "T:ILInspector.Metadata.AssemblySurfaceClassifier",
            banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyInspector", banned);
        Assert.Contains(
            "T:ILInspector.Metadata.TypeResolutionCatalog",
            banned);
        Assert.Contains(
            "T:ILInspector.Metadata.TypeResolutionContext",
            banned);
        Assert.Contains(
            "T:ILInspector.Metadata.TypeResolutionEnumWidth",
            banned);
        Assert.Contains(
            "T:ILInspector.Metadata.SignatureSpellability",
            banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyReader", banned);
        Assert.Contains("T:ILInspector.Metadata.ApiSurfaceExtractor", banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyIdentityScanner", banned);
        Assert.Contains("T:ILInspector.Metadata.ExtensionMethodScanner", banned);
        Assert.Contains("T:ILInspector.Metadata.MethodClassificationScanner", banned);
        Assert.Contains("T:ILInspector.Metadata.ResourceScanner", banned);
        Assert.Contains("T:ILInspector.Metadata.TypeDependencyScanner", banned);
        Assert.Contains("T:ILInspector.Metadata.TypeHierarchyScanner", banned);
        Assert.Contains("P:ILInspector.Metadata.ResolvedAssemblyReference.OpenRead", banned);
        Assert.Contains("T:ILInspector.Metadata.IAssemblyReferenceResolver", banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyReferenceBindingPolicy", banned);
        Assert.Contains("T:System.Reflection.PortableExecutable.PEReader", banned);
        Assert.Contains("T:System.Reflection.Metadata.MetadataReader", banned);
        Assert.Contains("T:ILInspector.Decompiler.Pipeline.MetadataSource", banned);
        Assert.Contains("T:ILInspector.Decompiler.MemberBodyProducer", banned);
        Assert.Contains("T:ILInspector.SourceLink.SourceLinkService", banned);
        Assert.Contains("T:ILInspector.SourceLink.SourceLinkInspector", banned);
        Assert.Contains("T:ILInspector.Instructions.IlAssemblyDiff", banned);
        Assert.Contains("T:DotnetInspector.Services.PdbAcquisitionService", banned);
        Assert.Contains("T:ILInspector.Analysis.LeakTriageAnalyzer", banned);
        Assert.Contains("T:ILInspector.Analysis.ResourceLifecycleAnalysis", banned);
        Assert.Contains("T:ILInspector.Decompiler.CSharpBodyDiff", banned);
        Assert.Contains("T:ILInspector.Decompiler.CSharpFindings", banned);
        Assert.Contains("T:ILInspector.Metadata.MemberSearch", banned);
        Assert.Contains("T:ILInspector.Metadata.Corpus", banned);
        Assert.Contains("T:ILInspector.Research.ImplementationDiff", banned);
        Assert.Contains("T:ILInspector.Research.ResearchDiff", banned);
        Assert.Contains("T:ILInspector.Research.ResearchDiffInput", banned);
        Assert.Contains("T:ILInspector.Research.ResearchMatch", banned);
        Assert.Contains(
            "T:DotnetInspector.Services.AssemblyDependencyResolver",
            banned);
        Assert.Contains(
            "T:DotnetInspector.Services.AssemblySetResolutionSession",
            banned);
        Assert.Contains(
            "T:DotnetInspector.Services.AssemblySetSurfaceBuilder",
            banned);
        Assert.Contains("T:DotnetInspector.Services.PlatformResolver", banned);
        Assert.Contains("T:DotnetInspector.Services.TfmSelector", banned);
        Assert.Contains("T:DotnetInspector.Services.PackageContentAudit", banned);
        Assert.Contains(
            "T:ILInspector.Analysis.CallerScopeReachabilityPlan",
            banned);
        Assert.Contains("T:ILInspector.Analysis.LibraryBodyIndex", banned);
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.InspectionWorkspace.CreateAssemblyContextGroup",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.InspectionWorkspace.CreatePackageAssemblyContextRoles",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Packages.PackageAssetSelector.Select",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Packages.PackageCompileAssetSelector.Select",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.Create(",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.CreateFromPath(",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.CreateFromPathIfManaged",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.SelectFromPath(",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.SelectFromStream(",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.CreateFromStreamIfManaged",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.CreateFromStreamWithFallbackIdentity",
                StringComparison.Ordinal));
        Assert.Equal(
            2,
            banned.Count(symbol => symbol.StartsWith(
                "M:ILInspector.Metadata.ResolvedAssemblyReference.TryCreateFromPath",
                StringComparison.Ordinal)));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextGroup.UseAssemblyImage",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextGroup.GetAssemblyImageSpan",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextGroup.RetainAssemblyReference",
                StringComparison.Ordinal));

        // A package load is not an explicit request for unbounded work: the whole-group and
        // participant API-surface entry points are declared InspectionCost.Unbounded, so the
        // browser can reach only the bounded overload.
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextApiSurfaceQuery.Execute(",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextApiSurfaceQuery.ExecuteParticipant(",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextApiSurfaceQuery.ExecuteBounded",
                StringComparison.Ordinal));

        // #3932's streaming form releases the participant terminally, and this engine reuses one
        // workspace across exports, so a later whole-group query over the same group would find
        // the released participant unavailable.
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextIntegrationsQuery.ExecuteParticipantAsync",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextIntegrationOpportunitiesQuery.ExecuteParticipantAsync",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeLoadingInspectedAssembliesIsCompilerBanned()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        INamedTypeSymbol[] capabilityOwners =
        [
            RequiredType("System.Reflection.Assembly"),
            RequiredType("System.Reflection.AssemblyName"),
            RequiredType("System.AppDomain"),
            RequiredType("System.Activator"),
            RequiredType("System.Runtime.Loader.AssemblyLoadContext"),
        ];
        IMethodSymbol[] publicEntryPoints =
        [
            .. capabilityOwners
                .SelectMany(owner => owner.GetMembers())
                .OfType<IMethodSymbol>()
                .Where(method =>
                    method.DeclaredAccessibility == Accessibility.Public),
        ];

        Assert.NotEmpty(publicEntryPoints);
        Assert.All(
            capabilityOwners,
            owner => Assert.Contains(owner.GetDocumentationCommentId()!, banned));
        Assert.All(
            publicEntryPoints,
            entryPoint => Assert.True(
                IsBanned(entryPoint, banned),
                $"Runtime capability '{entryPoint.GetDocumentationCommentId()}' is unguarded."));
    }

    [Fact]
    public void EveryFrameworkRawDecoderProducerIsCompilerBanned()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        string[] expected =
        [
            "T:System.Reflection.Metadata.MetadataReader",
            "T:System.Reflection.Metadata.MetadataReaderProvider",
            "T:System.Reflection.Metadata.PEReaderExtensions",
            "T:System.Reflection.PortableExecutable.PEHeaders",
            "T:System.Reflection.PortableExecutable.PEReader",
        ];
        INamedTypeSymbol metadataReader =
            RequiredType("System.Reflection.Metadata.MetadataReader");
        var decoderIds = new HashSet<string>(StringComparer.Ordinal)
        {
            metadataReader.GetDocumentationCommentId()!,
            RequiredType("System.Reflection.PortableExecutable.PEHeaders")
                .GetDocumentationCommentId()!,
        };
        INamedTypeSymbol[] frameworkTypes =
        [
            .. DescendantTypes(metadataReader.ContainingAssembly.GlobalNamespace)
                .Where(type =>
                    type.DeclaredAccessibility == Accessibility.Public),
        ];

        bool changed;
        do
        {
            changed = false;
            foreach (INamedTypeSymbol type in frameworkTypes)
            {
                string typeId = type.GetDocumentationCommentId()!;
                if (decoderIds.Contains(typeId)
                    || !type.GetMembers().Any(member =>
                        member.DeclaredAccessibility == Accessibility.Public
                        && ResultTypeId(member) is { } resultId
                        && decoderIds.Contains(resultId)))
                {
                    continue;
                }

                changed |= decoderIds.Add(typeId);
            }
        }
        while (changed);

        Assert.Equal(expected, decoderIds.Order(StringComparer.Ordinal));
        Assert.All(decoderIds, decoder => Assert.Contains(decoder, banned));
    }

    [Fact]
    public void EveryProductMetadataIdentityDecoderIsCompilerBanned()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        string metadataReaderId =
            RequiredType("System.Reflection.Metadata.MetadataReader")
                .GetDocumentationCommentId()!;
        IMethodSymbol[] decoders =
        [
            .. RequiredType("ILInspector.Metadata.AssemblyReferenceIdentity")
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method =>
                    method.DeclaredAccessibility == Accessibility.Public
                    && method.Parameters.Any(parameter =>
                        ResultTypeId(parameter) == metadataReaderId)),
        ];

        Assert.NotEmpty(decoders);
        Assert.All(
            decoders,
            decoder => Assert.Contains(
                decoder.GetDocumentationCommentId()!,
                banned));
    }

    [Fact]
    public void ReflectionOnlyAssemblyLoadingIsUnavailableToBrowser()
    {
        Assert.Null(
            ProductCompilation.GetTypeByMetadataName(
                "System.Reflection.MetadataLoadContext"));
        Assert.Null(
            ProductCompilation.GetTypeByMetadataName(
                "System.Reflection.PathAssemblyResolver"));
    }

    [Fact]
    public void EveryPublicInspectionStreamOwnerIsBannedOrApprovedAcquisitionSurface()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        string[] approvedOwners =
        [
            "DotnetInspector.Core.HardenedXml",
            "DotnetInspector.Packages.BoundedContentReader",
            "DotnetInspector.Packages.FileSystemPackageStore",
            "DotnetInspector.Packages.FileSystemPdbStore",
            "DotnetInspector.Packages.IPackageStore",
            "DotnetInspector.Packages.IPdbStore",
            "DotnetInspector.Packages.InMemoryPackageStore",
            "DotnetInspector.Packages.InMemoryPdbStore",
            "DotnetInspector.Packages.SnupkgPdbReader",
            "DotnetInspector.Services.NuspecParser",
            "NuGetFetch.NuGetApi",
            "NuGetFetch.PackageExtractor",
            "NuGetFetch.PackageSignatureVerifier",
            "NuGetFetch.PackageSourceResultFactory",
        ];
        HashSet<string> approved =
            approvedOwners.ToHashSet(StringComparer.Ordinal);
        Type[] streamOwners =
        [
            .. ProductAssemblies
                .SelectMany(assembly => assembly.GetExportedTypes())
                .Distinct()
                .Where(type =>
                    type.GetMembers(
                            BindingFlags.Public
                            | BindingFlags.Instance
                            | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)
                        .OfType<MethodBase>()
                        .Any(method => method.GetParameters().Any(
                            parameter => typeof(Stream).IsAssignableFrom(
                                parameter.ParameterType))))
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

        AssertGuardedOwners(
            streamOwners,
            approved,
            banned,
            "Direct-Stream owner");
    }

    [Fact]
    public void EveryPublicDescriptorConsumerIsBannedOrApprovedProductCurrency()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        string[] approvedOwners =
        [
            // Product queries and typed carriers may exchange descriptors without opening them
            // through an unaccounted inspection primitive.
            "DotnetInspector.Queries.AssemblyContextGroup",
            "DotnetInspector.Queries.AssemblyContextParticipant",
            "DotnetInspector.Queries.AssemblyContextTypeResolutionResult+Rejected",
            "DotnetInspector.Queries.InspectionGraphSubject",
            "DotnetInspector.Queries.MemberCallGraphAcquisitionFailure",
            "DotnetInspector.Queries.MemberCallGraphAcquisitionFailure+InvalidImage",
            "DotnetInspector.Queries.MemberCallGraphAcquisitionFailure+Rejected",
            "DotnetInspector.Queries.MemberCallGraphSession",
            "DotnetInspector.Queries.PackageAssemblyRoleCorrespondence",
            "DotnetInspector.Services.PlatformTypeLookupCandidate",
            "ILInspector.Analysis.CallerResolutionPlan",
            "ILInspector.Analysis.CatalogCallGraphParticipant",
            "ILInspector.Analysis.CatalogMemberCorrespondencePlan",
            "ILInspector.Analysis.CatalogMethodDefinitionCorrespondencePlan",
            "ILInspector.Metadata.AssemblyBindingOrigin",
            "ILInspector.Metadata.AssemblyBindingSelection",
            "ILInspector.Metadata.TypeResolutionRequest",
            "ILInspector.Research.ImplementationAssemblyInput",
            "ILInspector.Research.ImplementationComparisonInputOccurrence",
            "ILInspector.Research.ILOffsetProjectionRequest",
        ];
        HashSet<string> approved =
            approvedOwners.ToHashSet(StringComparer.Ordinal);
        Type[] consumers =
        [
            .. ProductAssemblies
                .SelectMany(assembly => assembly.GetExportedTypes())
                .Where(type =>
                    type.GetMembers(
                            BindingFlags.Public
                            | BindingFlags.Instance
                            | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)
                        .OfType<MethodBase>()
                        .Any(method => method.GetParameters().Any(
                            parameter => parameter.ParameterType
                                == typeof(ResolvedAssemblyReference))))
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

        AssertGuardedOwners(
            consumers,
            approved,
            banned,
            "Descriptor owner");
    }

    [Fact]
    public void EveryPublicPathMethodOwnerIsBannedOrApprovedNonInspectionSurface()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        string[] approvedOwners =
        [
            "CSharpText.XmlDocText",
            "DotnetInspector.Core.CoreCache",
            "DotnetInspector.Core.HardenedXml",
            "DotnetInspector.Packages.FileSystemPackageContent",
            "DotnetInspector.Packages.HttpRetryHelper",
            "DotnetInspector.Packages.IPackageContent",
            "DotnetInspector.Packages.IPackageContentEntryManifest",
            "DotnetInspector.Packages.InMemoryPackageContent",
            "DotnetInspector.Packages.NuGetCache",
            "DotnetInspector.Packages.PackageCoordinateResolver",
            "DotnetInspector.Packages.PackageExtractor",
            "DotnetInspector.Packages.SymbolPackageDownloader",
            "DotnetInspector.Services.DepsJsonParser",
            "DotnetInspector.Services.GitHubUrlResolver",
            "DotnetInspector.Services.LocalRepoSourceAcquisition",
            "DotnetInspector.Services.NuspecParser",
            "DotnetInspector.Services.PdbSourceAcquisition",
            "DotnetInspector.Services.ProjectAssetsParser",
            "DotnetInspector.Services.SignatureVerifier",
            "ILInspector.Metadata.ApiSurface",
            "ILInspector.Metadata.ResolvedAssemblyReference",
            "ILInspector.SourceLink.SourceLinkResolver",
            "InertText.UrlRedaction",
            "NuGetFetch.NuGetClient",
            "NuGetFetch.PackageCache",
            "NuGetFetch.PackageExtractor",
            "NuGetFetch.PackageSignatureVerifier",
            "NuGetFetch.SourceResolver",
            "NuGetFetch.TfmResolver",
            "SourceLinkFetch.SourceLinkProvenance",
            "SourceLinkFetch.SourceLinkResolver",
        ];
        HashSet<string> approved =
            approvedOwners.ToHashSet(StringComparer.Ordinal);
        Type[] owners =
        [
            .. ProductAssemblies
                .SelectMany(assembly => assembly.GetExportedTypes())
                .Distinct()
                .Where(type =>
                    type.GetMembers(
                            BindingFlags.Public
                            | BindingFlags.Instance
                            | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)
                        .OfType<MethodBase>()
                        .Any(method =>
                            !method.IsConstructor
                            && !method.IsSpecialName
                            && method.Name != "Deconstruct"
                            && method.GetParameters().Any(parameter =>
                                parameter.Name?.Contains(
                                    "path",
                                    StringComparison.OrdinalIgnoreCase)
                                == true)))
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

        AssertGuardedOwners(
            owners,
            approved,
            banned,
            "Path-method owner");
    }

    [Fact]
    public void EveryPublicAssemblyPathCarrierIsBannedOrApprovedData()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        string[] approvedOwners =
        [
            "DotnetInspector.Services.AssemblyDependencyResolutionOptions",
            "ILInspector.Decompiler.Pipeline.IrFunction",
            "ILInspector.Metadata.ApiDiffInspectionFailure",
            "ILInspector.Metadata.ApiSurfaceInspectionFailure",
            "ILInspector.Metadata.ApiSurfaceInspectionSubject",
            "ILInspector.Metadata.ApiType",
            "ILInspector.Metadata.CorpusMember",
        ];
        HashSet<string> approved =
            approvedOwners.ToHashSet(StringComparer.Ordinal);
        Type[] owners =
        [
            .. ProductAssemblies
                .SelectMany(assembly => assembly.GetExportedTypes())
                .Distinct()
                .Where(type =>
                    type.GetProperties(
                            BindingFlags.Public
                            | BindingFlags.Instance
                            | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)
                        .Any(property =>
                            property.SetMethod is not null
                            && property.Name.Contains(
                                "assemblypath",
                                StringComparison.OrdinalIgnoreCase))
                    || type.GetFields(
                            BindingFlags.Public
                            | BindingFlags.Instance
                            | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)
                        .Any(field => field.Name.Contains(
                            "assemblypath",
                            StringComparison.OrdinalIgnoreCase)))
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

        AssertGuardedOwners(
            owners,
            approved,
            banned,
            "Assembly-path carrier");
    }

    [Fact]
    public void EveryBannedSymbolStillExists()
    {
        foreach (string symbol in BannedSymbols())
        {
            ISymbol? resolved = DocumentationCommentId.GetFirstSymbolForDeclarationId(
                symbol,
                ProductCompilation);
            Assert.True(
                resolved is not null,
                $"Banned symbol '{symbol}' no longer resolves exactly, so the entry bans nothing.");
        }
    }

    [Fact]
    public void QueryCurrencyRemainsAvailableWithoutAcquisitionFactories()
    {
        IReadOnlyList<string> banned = BannedSymbols();

        // Query results still expose typed identities and descriptors. The host may consume that
        // currency, but product realization owns how package descriptors are minted.
        Assert.DoesNotContain("T:ILInspector.Metadata.AssemblyReferenceIdentity", banned);
        Assert.DoesNotContain("T:ILInspector.Metadata.ResolvedAssemblyReference", banned);
    }

    [Fact]
    public void EngineCoreProject_HasOneWayOwnerReference()
    {
        Assert.Contains(
            ProjectReferences(EngineProjectPath),
            project => project.Equals(
                CoreProjectPath,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            ProjectReferences(CoreProjectPath),
            project => project.Equals(
                EngineProjectPath,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EcosystemCatalogIsFacadeOnly()
    {
        string inspectWebRoot = Path.Combine(
            RepositoryRoot(),
            "prototypes",
            "inspect-web");
        string ecosystemsProject = Path.Combine(
            RepositoryRoot(),
            "src",
            "DotnetInspector.Ecosystems",
            "DotnetInspector.Ecosystems.csproj");
        string[] productionProjects =
        [
            .. Directory.EnumerateFiles(
                    inspectWebRoot,
                    "*.csproj",
                    SearchOption.AllDirectories)
                .Where(project =>
                    !project.Contains(
                        $"{Path.DirectorySeparatorChar}engine.Tests"
                        + $"{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase)
                    && !project.Contains(
                        $"{Path.DirectorySeparatorChar}msdl-proxy.Tests"
                        + $"{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase)
                    && !project.Contains(
                        $"{Path.DirectorySeparatorChar}multi-facade-canary"
                        + $"{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal),
        ];
        Assert.Contains(
            EngineProjectPath,
            productionProjects,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            CoreProjectPath,
            productionProjects,
            StringComparer.OrdinalIgnoreCase);

        foreach (string project in productionProjects)
        {
            if (project.Equals(
                EngineProjectPath,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.DoesNotContain(
                EvaluatedProjectItems(project, "ProjectReference")
                    .Select(item => item.GetProperty("FullPath").GetString())
                    .OfType<string>(),
                reference => string.Equals(
                    reference,
                    ecosystemsProject,
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                EvaluatedProjectItems(project, "Reference")
                    .Select(item => item.GetProperty("Identity").GetString())
                    .OfType<string>(),
                reference => string.Equals(
                    reference.Split(',', 2)[0],
                    "DotnetInspector.Ecosystems",
                    StringComparison.OrdinalIgnoreCase));
        }
    }
    [Fact]
    [Fact]
    public void EveryBrowserProjectPinsTheLayeringGate()
    {
        // The ban list is what makes the compiler enforce "inspect only through a public product
        // query". Every browser-owned project that can reach product APIs must pin it, or a
        // capability could quietly open a session in its own assembly.
        string[] projects =
        [
            CoreProjectPath,
            .. CapabilityProjectPaths,
        ];

        Assert.NotEmpty(projects);
        Assert.All(
            projects,
            project => Assert.Contains(
                ProjectItems(project, "AdditionalFiles"),
                path => path.Equals(
                    BanListPath,
                    StringComparison.OrdinalIgnoreCase)));
    }

    static IReadOnlyList<string> CapabilityProjectPaths =>
    [
        .. new[]
        {
            ("engine.PackageExports", "InspectWeb.Engine.PackageExports.csproj"),
            ("engine.MetadataExports", "InspectWeb.Engine.MetadataExports.csproj"),
            ("engine.AnalysisExports", "InspectWeb.Engine.AnalysisExports.csproj"),
            ("engine.SourceExports", "InspectWeb.Engine.SourceExports.csproj"),
            ("engine.CallGraphExports", "InspectWeb.Engine.CallGraphExports.csproj"),
            ("engine.CatalogExports", "InspectWeb.Engine.CatalogExports.csproj"),
        }
            .Select(project => Path.Combine(
                RepositoryRoot(),
                "prototypes",
                "inspect-web",
                project.Item1,
                project.Item2)),
    ];

    [Fact]
    public void EngineCoreAssembly_HasNoFacadeContracts()
    {
        Assembly core = typeof(BrowserSourceOperationCoordinator).Assembly;

        Assert.Equal("InspectWeb.Engine.Core", core.GetName().Name);
        Assert.DoesNotContain(
            core.GetReferencedAssemblies(),
            reference => reference.Name is not null
                && reference.Name.StartsWith(
                    "InspectWeb.Engine",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            core.DefinedTypes.SelectMany(type =>
                type.GetMethods(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)),
            method => method
                .GetCustomAttributesData()
                .Any(attribute =>
                    attribute.AttributeType.FullName
                    == "System.Runtime.InteropServices.JavaScript.JSExportAttribute"));
        Assert.DoesNotContain(
            core.DefinedTypes,
            type => typeof(System.Text.Json.Serialization.JsonSerializerContext)
                .IsAssignableFrom(type));
    }

    [Fact]
    public void EngineCoreAssembly_OwnsSharedWorkspaceState()
    {
        Assembly core = typeof(BrowserSourceOperationCoordinator).Assembly;
        Type[] workspaceTypes =
        [
            typeof(BrowserPackageWorkspace),
            typeof(BrowserInspectionScope),
            typeof(BrowserPlatformWorkspace),
            typeof(BrowserApiSurfacePolicy),
        ];
        Type[] internalResultTypes =
        [
            typeof(BrowserPackageCacheSnapshot),
            typeof(BrowserPackageDocumentEntry),
            typeof(BrowserPackageDocumentPayload),
            typeof(BrowserPackageIconPayload),
        ];

        Assert.All(workspaceTypes, type => Assert.Same(core, type.Assembly));
        Assert.All(
            internalResultTypes,
            type =>
            {
                Assert.Same(core, type.Assembly);
                Assert.False(type.IsPublic);
            });
    }

    static Type? Resolve(string fullName) => ProductAssemblies
        .Select(assembly => assembly.GetType(fullName, throwOnError: false))
        .OfType<Type>()
        .FirstOrDefault();

    static INamedTypeSymbol RequiredType(string fullName) =>
        ProductCompilation.GetTypeByMetadataName(fullName)
        ?? throw new InvalidOperationException(
            $"Required framework type '{fullName}' is unavailable.");

    static IEnumerable<INamedTypeSymbol> DescendantTypes(
        INamespaceOrTypeSymbol container)
    {
        foreach (ISymbol member in container.GetMembers())
        {
            if (member is INamedTypeSymbol type)
                yield return type;
            if (member is INamespaceOrTypeSymbol child)
            {
                foreach (INamedTypeSymbol descendant in DescendantTypes(child))
                    yield return descendant;
            }
        }
    }

    static string? ResultTypeId(ISymbol symbol)
    {
        ITypeSymbol? type = symbol switch
        {
            IMethodSymbol method when !method.ReturnsVoid => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null,
        };
        return type is INamedTypeSymbol named
            ? named.OriginalDefinition.GetDocumentationCommentId()
            : null;
    }

    static bool IsBanned(ISymbol symbol, IReadOnlyList<string> banned) =>
        symbol.GetDocumentationCommentId() is { } symbolId
            && banned.Contains(symbolId)
        || symbol.ContainingType?.GetDocumentationCommentId() is { } typeId
            && banned.Contains(typeId);

    static IReadOnlyList<Assembly> ProductAssemblies { get; } =
        ProductReferenceClosure();

    static CSharpCompilation ProductCompilation { get; } = CSharpCompilation.Create(
        "BrowserEngineBannedSymbols",
        references:
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Concat(ProductAssemblies.Select(assembly => assembly.Location))
                .Distinct(StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path)),
        ]);

    static IReadOnlyList<string> BannedSymbols() =>
    [
        .. File.ReadAllLines(BanListPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith(';'))
            .Select(line => line.Split(';')[0].Trim()),
    ];

    static string EngineProjectPath => Path.Combine(
        RepositoryRoot(),
        "prototypes",
        "inspect-web",
        "engine",
        "InspectWeb.Engine.csproj");

    static string CoreProjectPath => Path.Combine(
        RepositoryRoot(),
        "prototypes",
        "inspect-web",
        "engine.Core",
        "InspectWeb.Engine.Core.csproj");

    static string BanListPath => Path.Combine(
        Path.GetDirectoryName(EngineProjectPath)!,
        "BannedSymbols.txt");

    static IReadOnlyList<Assembly> ProductReferenceClosure()
    {
        var projects = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(EngineProjectPath);
        while (pending.TryPop(out string? project))
        {
            if (!projects.Add(project))
                continue;

            foreach (string reference in ProjectReferences(project))
                pending.Push(reference);
        }

        // Owner-issued product assemblies only. The browser's own host, core, and capability
        // export assemblies are L3 consumer adapters, so they are guarded by the facade
        // partition tests rather than by the product-ownership guards below.
        return
        [
            .. projects
                .Where(project =>
                    !project.Equals(
                        EngineProjectPath,
                        StringComparison.OrdinalIgnoreCase))
                .Select(ProjectAssemblyName)
                .Where(name => !name.StartsWith("InspectWeb.", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(name => Assembly.Load(new AssemblyName(name)))
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal),
        ];
    }

    static IReadOnlyList<string> ProjectReferences(string project) =>
        ProjectItems(project, "ProjectReference");

    static IReadOnlyList<JsonElement> EvaluatedProjectItems(
        string project,
        string itemName)
    {
        string dotnetHost =
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnetHost)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add($"-getItem:{itemName}");
        startInfo.ArgumentList.Add("-property:Configuration=Release");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start '{dotnetHost}' to evaluate '{project}'.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string standardOutput = output.GetAwaiter().GetResult();
        string standardError = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"MSBuild could not evaluate '{project}': {standardError}");
        }

        using JsonDocument document = JsonDocument.Parse(standardOutput);
        if (!document.RootElement
            .GetProperty("Items")
            .TryGetProperty(itemName, out JsonElement items))
        {
            return [];
        }

        return [.. items.EnumerateArray().Select(item => item.Clone())];
    }

    static IReadOnlyList<string> ProjectItems(
        string project,
        string itemName)
    {
        XDocument document = XDocument.Load(project);
        return
        [
            .. document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == itemName)
                .Select(item =>
                {
                    string include = item.Attribute("Include")?.Value
                        ?? throw new InvalidOperationException(
                            $"{itemName} in '{project}' has no Include.");
                    string normalized = include.Replace(
                        '\\',
                        Path.DirectorySeparatorChar);
                    return Path.GetFullPath(
                        Path.Combine(
                            Path.GetDirectoryName(project)!,
                            normalized));
                }),
        ];
    }

    static string ProjectAssemblyName(string project)
    {
        XDocument document = XDocument.Load(project);
        string? assemblyName = document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "AssemblyName")
            ?.Value;
        return assemblyName is { Length: > 0 }
            ? assemblyName
            : Path.GetFileNameWithoutExtension(project);
    }

    static void AssertGuardedOwners(
        IReadOnlyCollection<Type> owners,
        IReadOnlySet<string> approved,
        IReadOnlyList<string> banned,
        string category)
    {
        Assert.NotEmpty(owners);
        string[] staleApprovals =
        [
            .. approved.Except(
                owners.Select(type => type.FullName!),
                StringComparer.Ordinal),
        ];
        string[] unguardedOwners =
        [
            .. owners
                .Where(type =>
                    !approved.Contains(type.FullName!)
                    && !banned.Contains(
                        "T:" + type.FullName!.Replace('+', '.')))
                .Select(type => type.FullName!),
        ];

        Assert.True(
            staleApprovals.Length == 0 && unguardedOwners.Length == 0,
            $"{category} guard is stale. "
            + $"Stale approvals: {string.Join(", ", staleApprovals)}. "
            + $"Unguarded owners: {string.Join(", ", unguardedOwners)}.");
    }

    static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
