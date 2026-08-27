using System.Collections.Immutable;
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
        Assert.Contains(
            "M:ILInspector.Metadata.ResolvedAssemblyReference.CreateFromModulePathIfManaged(System.String,ILInspector.Metadata.AssemblyResolutionProvenance)",
            banned);
        Assert.Contains(
            "M:ILInspector.Metadata.ResolvedAssemblyReference.CreateInspectionReferenceFromPathIfManaged(System.String,ILInspector.Metadata.AssemblyResolutionProvenance)",
            banned);
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
    public void EveryPublicDescriptorFactoryIsCompilerBanned()
    {
        IReadOnlyList<string> banned = BannedSymbols();
        IMethodSymbol[] factories =
            PublicDescriptorFactories(
                ProductCompilation,
                ProductAssemblyNames);

        Assert.NotEmpty(factories);
        Assert.All(
            factories,
            factory => Assert.Contains(
                factory.GetDocumentationCommentId()!,
                banned));
    }

    [Fact]
    public void DescriptorFactoryInventory_IncludesOtherProductTypes()
    {
        const string AssemblyName = "DescriptorFactoryInventoryProbe";
        SyntaxTree source = CSharpSyntaxTree.ParseText(
            """
            using ILInspector.Metadata;

            public static class AlternateDescriptorFactory
            {
                public static ResolvedAssemblyReference? Create() =>
                    null;

                public static bool TryCreate(
                    out ResolvedAssemblyReference? descriptor)
                {
                    descriptor = null;
                    return false;
                }
            }
            """,
            cancellationToken:
                TestContext.Current.CancellationToken);
        CSharpCompilation compilation =
            CSharpCompilation.Create(
                AssemblyName,
                [source],
                ProductCompilation.References,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));

        Assert.Equal(
            ["Create", "TryCreate"],
            PublicDescriptorFactories(
                    compilation,
                    new HashSet<string>(
                        [AssemblyName],
                        StringComparer.Ordinal))
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BrowserBanListIsEffectiveCompilerInput()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                ArgumentList =
                {
                    "msbuild",
                    EngineProjectPath,
                    "-getItem:AdditionalFiles,PackageReference",
                    "-getProperty:WarningsAsErrors,WarningsNotAsErrors,NoWarn,OwnsItsOwnStderr,RunAnalyzers,RunAnalyzersDuringBuild",
                    "-p:Configuration=Release",
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.Start();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(
                cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output = await standardOutput;
        string error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"MSBuild evaluation failed:{Environment.NewLine}{error}");

        using JsonDocument evaluation =
            JsonDocument.Parse(output);
        JsonElement properties =
            evaluation.RootElement.GetProperty("Properties");
        JsonElement items =
            evaluation.RootElement.GetProperty("Items");

        Assert.Contains(
            items.GetProperty("AdditionalFiles")
                .EnumerateArray(),
            item => Path.GetFullPath(
                    item.GetProperty("FullPath").GetString()!)
                .Equals(
                    Path.GetFullPath(BanListPath),
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            items.GetProperty("PackageReference")
                .EnumerateArray(),
            item => item.GetProperty("Identity").GetString()
                    == "Microsoft.CodeAnalysis.BannedApiAnalyzers"
                && SplitItemProperty(
                        item,
                        "IncludeAssets")
                    .Contains(
                        "analyzers",
                        StringComparer.OrdinalIgnoreCase)
                && !SplitItemProperty(
                        item,
                        "ExcludeAssets")
                    .Contains(
                        "analyzers",
                        StringComparer.OrdinalIgnoreCase)
                && !SplitItemProperty(
                        item,
                        "ExcludeAssets")
                    .Contains(
                        "all",
                        StringComparer.OrdinalIgnoreCase));
        Assert.Contains(
            "RS0030",
            SplitProperty(properties, "WarningsAsErrors"));
        Assert.DoesNotContain(
            "RS0030",
            SplitProperty(properties, "NoWarn"));
        Assert.DoesNotContain(
            "RS0030",
            SplitProperty(properties, "WarningsNotAsErrors"));
        Assert.NotEqual(
            "true",
            properties.GetProperty("OwnsItsOwnStderr")
                .GetString());
        Assert.False(
            properties.GetProperty("RunAnalyzers")
                .GetString()
                ?.Equals(
                    "false",
                    StringComparison.OrdinalIgnoreCase)
                == true);
        Assert.False(
            properties.GetProperty("RunAnalyzersDuringBuild")
                .GetString()
                ?.Equals(
                    "false",
                    StringComparison.OrdinalIgnoreCase)
                == true);

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"inspect-web-ban-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string canaryPath = Path.Combine(
                temporaryDirectory,
                "BrowserBanCanary.cs");
            await File.WriteAllTextAsync(
                canaryPath,
                """
            using ILInspector.Metadata;
            static class BrowserBanCanary
            {
                static object? Invoke() =>
                    ResolvedAssemblyReference.CreateFromPathIfManaged(
                        "canary.dll",
                        null!);
            }
            """,
                cancellationToken);
            string targetsPath = Path.Combine(
                temporaryDirectory,
                "BrowserBanCanary.targets");
            new XDocument(
                new XElement(
                    "Project",
                    new XElement(
                        "ItemGroup",
                        new XElement(
                            "Compile",
                            new XAttribute(
                                "Include",
                                canaryPath)))))
                .Save(targetsPath);

            using var canaryProcess = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    ArgumentList =
                    {
                        "msbuild",
                        EngineProjectPath,
                        "-target:Compile",
                        "-p:Configuration=Release",
                        "-p:BuildProjectReferences=false",
                        $"-p:CustomAfterMicrosoftCommonTargets={targetsPath}",
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            canaryProcess.Start();
            Task<string> canaryStandardOutput =
                canaryProcess.StandardOutput.ReadToEndAsync(
                    cancellationToken);
            Task<string> canaryStandardError =
                canaryProcess.StandardError.ReadToEndAsync(
                    cancellationToken);
            await canaryProcess.WaitForExitAsync(cancellationToken);
            string canaryOutput = await canaryStandardOutput;
            string canaryError = await canaryStandardError;

            Assert.True(
                canaryProcess.ExitCode != 0,
                "The browser analyzer canary compiled successfully.");
            Assert.Contains(
                "error RS0030",
                canaryOutput + canaryError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(
                temporaryDirectory,
                recursive: true);
        }
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
            "NuGetFetch.PackageSourcePayload",
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
            "ILInspector.Metadata.ApiSurface",
            "ILInspector.Metadata.ApiType",
            "ILInspector.Metadata.AssemblyBindingOrigin",
            "ILInspector.Metadata.AssemblyBindingSelection",
            "ILInspector.Metadata.TypeResolutionRequest",
            "ILInspector.Research.ImplementationAssemblyInput",
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

    static string[] SplitProperty(
        JsonElement properties,
        string name) =>
        properties.GetProperty(name)
            .GetString()!
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);

    static string[] SplitItemProperty(
        JsonElement item,
        string name) =>
        item.TryGetProperty(
                name,
                out JsonElement value)
            ? value.GetString()!
                .Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries)
            : [];

    static IMethodSymbol[] PublicDescriptorFactories(
        CSharpCompilation compilation,
        IReadOnlySet<string> productAssemblyNames)
    {
        INamedTypeSymbol descriptor =
            compilation.GetTypeByMetadataName(
                "ILInspector.Metadata.ResolvedAssemblyReference")
            ?? throw new InvalidOperationException(
                "The descriptor type is unavailable.");
        return
        [
            .. DescendantTypes(
                    compilation.GlobalNamespace)
                .Where(type => productAssemblyNames.Contains(
                    type.ContainingAssembly.Name))
                .SelectMany(type => type.GetMembers())
                .OfType<IMethodSymbol>()
                .Where(method =>
                    method.DeclaredAccessibility
                        == Accessibility.Public
                    && method.IsStatic
                    && (SymbolEqualityComparer.Default.Equals(
                            method.ReturnType,
                            descriptor)
                        || method.Parameters.Any(parameter =>
                            parameter.RefKind == RefKind.Out
                            && SymbolEqualityComparer.Default.Equals(
                                parameter.Type,
                                descriptor))))
                .OrderBy(
                    method => method.GetDocumentationCommentId(),
                    StringComparer.Ordinal),
        ];
    }

    static bool IsBanned(ISymbol symbol, IReadOnlyList<string> banned) =>
        symbol.GetDocumentationCommentId() is { } symbolId
            && banned.Contains(symbolId)
        || symbol.ContainingType?.GetDocumentationCommentId() is { } typeId
            && banned.Contains(typeId);

    static IReadOnlyList<Assembly> ProductAssemblies { get; } =
        ProductReferenceClosure();

    static IReadOnlySet<string> ProductAssemblyNames { get; } =
        ProductAssemblies
            .Select(assembly => assembly.GetName().Name!)
            .ToHashSet(StringComparer.Ordinal);

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

            XDocument document = XDocument.Load(project);
            foreach (XElement reference in document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "ProjectReference"))
            {
                string include = reference.Attribute("Include")?.Value
                    ?? throw new InvalidOperationException(
                        $"ProjectReference in '{project}' has no Include.");
                string normalized = include.Replace(
                    '\\',
                    Path.DirectorySeparatorChar);
                pending.Push(Path.GetFullPath(
                    Path.Combine(
                        Path.GetDirectoryName(project)!,
                        normalized)));
            }
        }

        return
        [
            .. projects
                .Where(project =>
                    !project.Equals(
                        EngineProjectPath,
                        StringComparison.OrdinalIgnoreCase))
                .Select(ProjectAssemblyName)
                .Distinct(StringComparer.Ordinal)
                .Select(name => Assembly.Load(new AssemblyName(name)))
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal),
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
