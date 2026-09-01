using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class AssemblyReferenceTreeResolutionTests
{
    [Fact]
    public void TraversingAssemblyRefName_IsIdentityAndCannotEscapeTheAssemblyDirectory()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            string assemblyDirectory = Directory.CreateDirectory(
                Path.Combine(root, "app")).FullName;
            string ownerPath = Path.Combine(assemblyDirectory, "Owner.dll");
            string siblingPath = Path.Combine(assemblyDirectory, "Sibling.dll");
            string payloadPath = Path.Combine(root, "payload.dll");

            File.WriteAllBytes(
                ownerPath,
                BuildAssembly("Owner", "../payload", "Sibling"));
            File.WriteAllBytes(siblingPath, BuildAssembly("Sibling"));
            File.WriteAllBytes(payloadPath, BuildAssembly("../payload"));

            List<AssemblyReferenceIdentity> references =
                AssemblyInspector.ExtractReferenceIdentities(ownerPath);
            List<AssemblyReferenceNode> nodes =
                LibraryMetadataService.BuildTransitiveReferences(
                    references,
                    ownerPath,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Owner"
                    },
                    new VerboseLogger(enabled: false),
                    deduplicate: true);

            AssemblyReferenceNode traversing =
                Assert.Single(nodes, node => node.Name == "../payload");
            Assert.Null(traversing.Path);
            Assert.Null(traversing.ResolvedFrom);

            AssemblyReferenceNode sibling =
                Assert.Single(nodes, node => node.Name == "Sibling");
            Assert.Equal(siblingPath, sibling.Path);
            Assert.Equal("local", sibling.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VersionSkewedSibling_UsesTheAvailableSibling()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            string ownerPath = Path.Combine(root, "Owner.dll");
            string siblingPath = Path.Combine(root, "Sibling.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    new AssemblyReferenceIdentity(
                        "Sibling",
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            File.WriteAllBytes(
                siblingPath,
                BuildAssembly("Sibling", new Version(2, 0, 0, 0)));

            AssemblyReferenceNode sibling = Assert.Single(
                BuildTree(ownerPath),
                node => node.Name == "Sibling");

            Assert.Equal(siblingPath, sibling.Path);
            Assert.Equal("local", sibling.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReferenceTreePathIdentity_IsCaseSensitiveOutsideWindows()
    {
        Assert.False(
            LibraryMetadataService.ReferenceTreePathComparer(isWindows: false)
                .Equals("Bridge.dll", "bridge.dll"));
        Assert.True(
            LibraryMetadataService.ReferenceTreePathComparer(isWindows: true)
                .Equals("Bridge.dll", "bridge.dll"));
    }

    [Fact]
    public void ReferenceFailureClassification_PreservesMetadataMechanism()
    {
        Assert.Equal(
            IdentifierConfusionAuditFailureKind.InvalidAssemblyMetadata,
            LibraryMetadataService.ClassifyIdentifierConfusionReferenceFailure(
                new UnsupportedMetadataFormatException()));
        Assert.Equal(
            IdentifierConfusionAuditFailureKind.InvalidAssemblyMetadata,
            LibraryMetadataService.ClassifyIdentifierConfusionReferenceFailure(
                new MalformedMetadataRootException(
                    MetadataRootMalformedReason.InvalidSignature)));
        Assert.Equal(
            IdentifierConfusionAuditFailureKind.AssemblyUnreadable,
            LibraryMetadataService.ClassifyIdentifierConfusionReferenceFailure(
                new NotSupportedException()));
    }

    [Fact]
    public void CaseDistinctResolvedPaths_DoNotSuppressDistinctCultures()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            const string firstConcern = "Micr\u03BFsoft.One";
            const string secondConcern = "Micr\u03BFsoft.Two";
            string firstPath = Path.Combine(root, "Bridge.dll");
            string secondPath = Path.Combine(root, "bridge.dll");
            File.WriteAllBytes(
                firstPath,
                BuildAssembly(
                    "Bridge",
                    new Version(1, 0, 0, 0),
                    "en-US",
                    new AssemblyReferenceIdentity(
                        firstConcern,
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            File.WriteAllBytes(
                secondPath,
                BuildAssembly(
                    "Bridge",
                    new Version(1, 0, 0, 0),
                    "fr-FR",
                    new AssemblyReferenceIdentity(
                        secondConcern,
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            if (Directory.EnumerateFiles(root, "*.dll").Count() != 2)
            {
                Assert.Skip(
                    "The filesystem does not support case-distinct sibling files.");
                return;
            }

            string ownerPath = Path.Combine(root, "Owner.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    new AssemblyReferenceIdentity(
                        "Bridge",
                        new Version(1, 0, 0, 0),
                        "en-US",
                        null),
                    new AssemblyReferenceIdentity(
                        "Bridge",
                        new Version(1, 0, 0, 0),
                        "fr-FR",
                        null)));

            List<AssemblyReferenceNode> nodes =
                BuildTree(ownerPath, maxDepth: 2);

            Assert.Equal(
                2,
                nodes.Count(node => node.Name == "Bridge"));
            Assert.Contains(
                nodes,
                node => node.Name == firstConcern
                    && node.Depth == 1);
            Assert.Contains(
                nodes,
                node => node.Name == secondConcern
                    && node.Depth == 1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DistinctSameNameReferences_DoNotSuppressResolvableIdentity()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            const string concerningName = "Micr\u03BFsoft.Hidden";
            File.WriteAllBytes(
                Path.Combine(root, "Bridge.dll"),
                BuildAssembly("Bridge", concerningName));

            var mismatching = new AssemblyReferenceIdentity(
                "Bridge",
                new Version(1, 0, 0, 0),
                null,
                "0000000000000000");
            var wildcard = new AssemblyReferenceIdentity(
                "Bridge",
                new Version(1, 0, 0, 0),
                null,
                null);
            foreach (AssemblyReferenceIdentity[] references in new[]
            {
                new[] { mismatching, wildcard },
                new[] { wildcard, mismatching },
            })
            {
                string ownerPath = Path.Combine(
                    root,
                    $"Owner-{Guid.NewGuid():N}.dll");
                File.WriteAllBytes(
                    ownerPath,
                    BuildAssembly(
                        Path.GetFileNameWithoutExtension(ownerPath),
                        new Version(1, 0, 0, 0),
                        references));

                List<AssemblyReferenceNode> nodes =
                    BuildTree(ownerPath, maxDepth: 2);

                Assert.Equal(
                    2,
                    nodes.Count(node => node.Name == "Bridge"));
                Assert.Contains(
                    nodes,
                    node => node.Name == concerningName
                        && node.Depth == 1);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OmittedCultureReference_UsesCulturedSiblingThroughInspectionModel()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            string ownerPath = Path.Combine(root, "Owner.dll");
            string siblingPath = Path.Combine(root, "Sibling.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    new AssemblyReferenceIdentity(
                        "Sibling",
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            File.WriteAllBytes(
                siblingPath,
                BuildAssembly(
                    "Sibling",
                    new Version(1, 0, 0, 0),
                    assemblyCulture: "fr"));

            using var httpClient = new HttpClient();
            var inspection = await LibraryMetadataService.InspectAsync(
                ownerPath,
                new LibraryOptions
                {
                    CollectReferenceTree = true,
                    ReferenceTreeDepth = 1,
                },
                new VerboseLogger(enabled: false),
                packageName: null,
                packageVersion: null,
                httpClient);
            AssemblyReferenceNode sibling = Assert.Single(
                Assert.IsType<List<AssemblyReferenceNode>>(
                    inspection?.AssemblyInfo?.TransitiveReferences),
                node => node.Name == "Sibling");

            Assert.Equal(siblingPath, sibling.Path);
            Assert.Equal("local", sibling.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyReferenceSet_DoesNotMaterializeTransitiveReferences()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            string ownerPath = Path.Combine(root, "Owner.dll");
            File.WriteAllBytes(ownerPath, BuildAssembly("Owner"));

            using var httpClient = new HttpClient();
            LibraryInspection? inspection =
                await LibraryMetadataService.InspectAsync(
                    ownerPath,
                    new LibraryOptions
                    {
                        CollectReferenceTree = true,
                        ReferenceTreeDepth = 1,
                    },
                    new VerboseLogger(enabled: false),
                    packageName: null,
                    packageVersion: null,
                    httpClient);

            LibraryInspection resolvedInspection =
                Assert.IsType<LibraryInspection>(inspection);
            Assert.Null(
                resolvedInspection.AssemblyInfo?.TransitiveReferences);
            string json = JsonSerializer.Serialize(
                resolvedInspection,
                JsonContext.Default.LibraryInspection);
            Assert.DoesNotContain(
                "\"transitive_references\"",
                json,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlatformSignedSibling_PreservesSiblingFirstResolution()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            AssemblyReferenceIdentity platformIdentity =
                ReadIdentity(typeof(object).Assembly.Location);
            string ownerPath = Path.Combine(root, "Owner.dll");
            string siblingPath = Path.Combine(
                root,
                $"{platformIdentity.Name}.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    platformIdentity));
            File.Copy(typeof(object).Assembly.Location, siblingPath);

            AssemblyReferenceNode sibling = Assert.Single(
                BuildTree(ownerPath),
                node => node.Name == platformIdentity.Name);

            Assert.Equal(siblingPath, sibling.Path);
            Assert.Equal("local", sibling.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MismatchingPlatformNamedSibling_ShadowsInstalledPlatformFallback()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            AssemblyReferenceIdentity platformIdentity =
                ReadIdentity(typeof(object).Assembly.Location);
            string ownerPath = Path.Combine(root, "Owner.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    platformIdentity));
            File.WriteAllBytes(
                Path.Combine(root, $"{platformIdentity.Name}.dll"),
                BuildAssembly(
                    platformIdentity.Name,
                    new Version(1, 0, 0, 0)));

            AssemblyReferenceNode platform = Assert.Single(
                BuildTree(ownerPath),
                node => node.Name == platformIdentity.Name);

            Assert.Null(platform.Path);
            Assert.Null(platform.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InspectorDependency_IsNotImportedFromTheInspectingProcess()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            AssemblyReferenceIdentity serviceIdentity =
                ReadIdentity(typeof(AssemblyDependencyResolver).Assembly.Location);
            string ownerPath = Path.Combine(root, "Owner.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    serviceIdentity));

            AssemblyReferenceNode dependency = Assert.Single(
                BuildTree(ownerPath),
                node => node.Name == serviceIdentity.Name);

            Assert.Null(dependency.Path);
            Assert.Null(dependency.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NewerPlatformReference_UsesTheInstalledPlatformAssembly()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            AssemblyReferenceIdentity platformIdentity =
                ReadIdentity(typeof(object).Assembly.Location);
            string ownerPath = Path.Combine(root, "Owner.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    platformIdentity with
                    {
                        Version = new Version(
                            platformIdentity.Version!.Major + 50,
                            0,
                            0,
                            0)
                    }));

            AssemblyReferenceNode platform = Assert.Single(
                BuildTree(ownerPath),
                node => node.Name == platformIdentity.Name);

            Assert.NotNull(platform.Path);
            Assert.Equal("platform", platform.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VersionTolerance_StillRequiresThePublicKeyToken()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            AssemblyReferenceIdentity platformIdentity =
                ReadIdentity(typeof(object).Assembly.Location);
            string ownerPath = Path.Combine(root, "Owner.dll");
            string siblingPath = Path.Combine(
                root,
                $"{platformIdentity.Name}.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    platformIdentity with
                    {
                        Version = new Version(
                            platformIdentity.Version!.Major + 50,
                            0,
                            0,
                            0),
                        PublicKeyToken = "0000000000000000"
                    }));
            File.Copy(typeof(object).Assembly.Location, siblingPath);

            AssemblyReferenceNode platform = Assert.Single(
                BuildTree(ownerPath),
                node => node.Name == platformIdentity.Name);

            Assert.Null(platform.Path);
            Assert.Null(platform.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecursivePlatformResolution_UsesTheResolvedParentsDirectory()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            var (parentPath, childReference, childPath) =
                FindPlatformParentWithColocatedReference();

            string ownerPath = Path.Combine(root, "Owner.dll");
            string plantedChildPath = Path.Combine(
                root,
                Path.GetFileName(childPath));
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    ReadIdentity(parentPath)));
            File.Copy(childPath, plantedChildPath);

            AssemblyReferenceNode resolvedChild = Assert.Single(
                BuildTree(ownerPath, maxDepth: 2),
                node => node.Depth == 1
                    && node.Name == childReference.Name);

            Assert.Equal(childPath, resolvedChild.Path);
            Assert.NotEqual(plantedChildPath, resolvedChild.Path);
            Assert.Equal("local", resolvedChild.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnreadableSibling_DoesNotFallBackToAPlatformAssembly()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            var (platformPath, _, _, error) =
                PlatformResolver.ResolveAssembly("System.Runtime");
            Assert.Null(error);
            Assert.NotNull(platformPath);
            AssemblyReferenceIdentity platformIdentity =
                ReadIdentity(platformPath);
            string ownerPath = Path.Combine(root, "Owner.dll");
            File.WriteAllBytes(
                ownerPath,
                BuildAssembly(
                    "Owner",
                    new Version(1, 0, 0, 0),
                    platformIdentity));
            File.WriteAllText(
                Path.Combine(root, $"{platformIdentity.Name}.dll"),
                "not a managed assembly");

            AssemblyReferenceNode platform = Assert.Single(
                BuildTree(ownerPath),
                node => node.Name == platformIdentity.Name);

            Assert.Null(platform.Path);
            Assert.Null(platform.ResolvedFrom);
            Assert.Equal(
                AssemblyReferenceResolutionFailure.Unavailable,
                platform.ResolutionFailure);
            Assert.Contains(
                "(unavailable)",
                Assert.Single(
                    LibraryInspectionView.BuildNestedReferenceTree([platform]))
                    .Text,
                StringComparison.Ordinal);

            string json = JsonSerializer.Serialize(
                new LibraryInspection
                {
                    AssemblyInfo = new AssemblyInfo
                    {
                        TransitiveReferences = [platform],
                    },
                },
                JsonContext.Default.LibraryInspection);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal(
                "Unavailable",
                document.RootElement
                    .GetProperty("assembly_info")
                    .GetProperty("transitive_references")[0]
                    .GetProperty("resolution_failure")
                    .GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (
        string ParentPath,
        AssemblyReferenceIdentity ChildReference,
        string ChildPath) FindPlatformParentWithColocatedReference()
    {
        foreach (string parentName in new[]
        {
            "System.Collections",
            "System.Linq",
            "System.Console",
            "System.Runtime"
        })
        {
            var (parentPath, _, _, error) =
                PlatformResolver.ResolveAssembly(parentName);
            if (error is not null || parentPath is null)
                continue;

            string parentDirectory = Path.GetDirectoryName(parentPath)!;
            var filesByName = Directory.EnumerateFiles(
                    parentDirectory,
                    "*.dll")
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path)!,
                    StringComparer.OrdinalIgnoreCase);
            AssemblyReferenceIdentity? childReference =
                AssemblyInspector.ExtractReferenceIdentities(parentPath)
                    .FirstOrDefault(reference =>
                        filesByName.ContainsKey(reference.Name));
            if (childReference is not null)
            {
                return (
                    parentPath,
                    childReference,
                    filesByName[childReference.Name]);
            }
        }

        throw new InvalidOperationException(
            "No installed platform parent had a co-located referenced assembly.");
    }

    private static List<AssemblyReferenceNode> BuildTree(string ownerPath)
        => BuildTree(ownerPath, maxDepth: 1);

    private static List<AssemblyReferenceNode> BuildTree(
        string ownerPath,
        int maxDepth)
    {
        List<AssemblyReferenceIdentity> references =
            AssemblyInspector.ExtractReferenceIdentities(ownerPath);
        return LibraryMetadataService.BuildTransitiveReferences(
            references,
            ownerPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Owner"
            },
            new VerboseLogger(enabled: false),
            deduplicate: true,
            maxDepth: maxDepth);
    }

    private static AssemblyReferenceIdentity ReadIdentity(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    private static byte[] BuildAssembly(
        string assemblyName,
        params string[] references)
        => BuildAssembly(
            assemblyName,
            new Version(1, 0, 0, 0),
            references.Select(reference =>
                new AssemblyReferenceIdentity(
                    reference,
                    new Version(1, 0, 0, 0),
                    null,
                    null)).ToArray());

    private static byte[] BuildAssembly(
        string assemblyName,
        Version assemblyVersion,
        params AssemblyReferenceIdentity[] references)
        => BuildAssembly(
            assemblyName,
            assemblyVersion,
            assemblyCulture: null,
            references);

    private static byte[] BuildAssembly(
        string assemblyName,
        Version assemblyVersion,
        string? assemblyCulture,
        params AssemblyReferenceIdentity[] references)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            assemblyVersion,
            culture: assemblyCulture is null
                ? default
                : metadata.GetOrAddString(assemblyCulture),
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        foreach (AssemblyReferenceIdentity reference in references)
        {
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(reference.Name),
                reference.Version ?? new Version(0, 0, 0, 0),
                culture: reference.Culture is null
                    ? default
                    : metadata.GetOrAddString(reference.Culture),
                publicKeyOrToken: string.IsNullOrEmpty(reference.PublicKeyToken)
                    ? default
                    : metadata.GetOrAddBlob(
                        Convert.FromHexString(reference.PublicKeyToken)),
                flags: default,
                hashValue: default);
        }

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
