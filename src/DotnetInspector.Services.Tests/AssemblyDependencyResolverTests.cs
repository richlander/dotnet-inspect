using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class AssemblyDependencyResolverTests
{
    [Fact]
    public void Select_NameMatchingUnreadableCandidateIsUnavailable()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(root, "System.Runtime.dll"),
                "not a managed assembly");
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [],
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });

            var selection = Assert.IsType<
                AssemblyBindingSelection.Unavailable>(
                    resolver.Select(
                        new AssemblyBindingRequest(
                            AssemblyBindingTarget.Reference(
                                new AssemblyReferenceIdentity(
                                    "System.Runtime",
                                    Version: null,
                                    Culture: null,
                                    PublicKeyToken: null)),
                            AssemblyBindingOrigin.Global(),
                            AssemblyResolutionScope.Any)));

            Assert.Equal(
                AssemblyBindingFailureKind.CandidateUnavailable,
                selection.Failure.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_UnreadableSiblingDoesNotFallThroughToTpa()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.Copy(
                typeof(AssemblyDependencyResolverTests).Assembly.Location,
                targetPath);
            File.WriteAllText(
                Path.Combine(root, "System.Runtime.dll"),
                "not a managed assembly");
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [],
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });

            var selection = Assert.IsType<
                AssemblyBindingSelection.Unavailable>(
                    resolver.Select(
                        new AssemblyBindingRequest(
                            AssemblyBindingTarget.Reference(
                                new AssemblyReferenceIdentity(
                                    "System.Runtime",
                                    Version: null,
                                    Culture: null,
                                    PublicKeyToken: null)),
                            AssemblyBindingOrigin.Global(),
                            AssemblyResolutionScope.Any)));

            Assert.Equal(
                AssemblyBindingFailureKind.CandidateUnavailable,
                selection.Failure.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_ReadableMismatchingSiblingShadowsInstalledPlatformFallback()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.Copy(
                typeof(AssemblyDependencyResolverTests).Assembly.Location,
                targetPath);
            File.WriteAllBytes(
                Path.Combine(root, "System.Runtime.dll"),
                BuildAssembly("System.Runtime", [1, 2, 3]));
            string platformPath = (AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                .Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries)
                .Single(path => Path.GetFileName(path).Equals(
                    "System.Runtime.dll",
                    StringComparison.OrdinalIgnoreCase));
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [],
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                    IncludeInstalledPlatformFallback = true,
                });

            Assert.IsType<AssemblyBindingSelection.Missing>(
                resolver.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(platformIdentity),
                        AssemblyBindingOrigin.Global(),
                        AssemblyResolutionScope.Any)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_AnyScopeUsesInstalledPlatformFallbackWhenNoTierOwnsName()
    {
        string targetPath =
            typeof(AssemblyDependencyResolverTests).Assembly.Location;
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var peReader = new PEReader(stream);
        AssemblyReferenceIdentity platformIdentity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                peReader.GetMetadataReader());
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
                IncludeInstalledPlatformFallback = true,
            });

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            resolver.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(platformIdentity),
                    AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Any)));

        Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
            selected.Assembly.Provenance);
    }

    [Fact]
    public void Select_CaseDistinctSameTierCandidateIsMatchedAfterUnavailableCandidate()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            string upperPath = Path.Combine(root, "Dep.dll");
            string lowerPath = Path.Combine(root, "dep.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllBytes(upperPath, BuildAssembly("Dep", [1, 2, 3]));
            File.WriteAllBytes(lowerPath, BuildAssembly("Dep", [1, 2, 3]));
            string[] candidates = Directory.EnumerateFiles(root, "*.dll")
                .Where(path => Path.GetFileNameWithoutExtension(path).Equals(
                    "Dep",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length != 2)
            {
                Assert.Skip(
                    "The filesystem does not support case-distinct sibling files.");
                return;
            }

            byte[] selectedKey = [4, 5, 6];
            File.WriteAllText(
                candidates[0],
                "not a managed assembly");
            File.WriteAllBytes(
                candidates[1],
                BuildAssembly("Dep", selectedKey));
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [],
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });

            var selection = Assert.IsType<
                AssemblyBindingSelection.Selected>(
                    resolver.Select(
                        new AssemblyBindingRequest(
                            AssemblyBindingTarget.Reference(
                                new AssemblyReferenceIdentity(
                                    "Dep",
                                    new Version(1, 0, 0, 0),
                                    Culture: null,
                                    AssemblyReferenceIdentity
                                        .ComputePublicKeyToken(selectedKey))),
                            AssemblyBindingOrigin.Global(),
                            AssemblyResolutionScope.Any)));

            Assert.Equal(candidates[1], selection.Assembly.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Acquire_SnapshotBudgetExhaustionIsTyped()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                SnapshotAssemblyImages = true,
                MaxSnapshotImageBytes = new FileInfo(path).Length - 1,
            });
        var dependency = new ResolvedAssemblyDependency(
            path,
            AssemblyDependencyProvenance.SiblingAssembly);

        var exception = Assert.Throws<
            AssemblyDependencySnapshotBudgetExceededException>(
                () => resolver.Acquire(dependency));

        Assert.Equal(
            new FileInfo(path).Length - 1,
            exception.MaxSnapshotImageBytes);
    }

    [Fact]
    public void Select_IntrinsicCoreLibraryUsesTheTargetsBindingDomain()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                AllowPlatformAssemblyVersionRollForward = true,
            });
        ResolvedAssemblyReference target =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "binding-policy test"));
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.FromAssembly(target),
            AssemblyResolutionScope.Platform);

        var selected = Assert.IsType<
            AssemblyBindingSelection.Selected>(
                resolver.Select(request));
        var repeated = Assert.IsType<
            AssemblyBindingSelection.Selected>(
                resolver.Select(request));

        Assert.Same(selected.Assembly, repeated.Assembly);

        MetadataTypeDefinitionName objectName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System",
                    ["Object"])).Name;
        TypeResolutionRequest typeRequest =
            TypeResolutionRequest.FromCoreLibrary(
                target,
                AssemblyResolutionScope.Platform,
                objectName);
        using TypeResolutionContext context =
            TypeResolutionContext.Create(
                resolver,
                [target],
                [typeRequest]);
        var resolved = Assert.IsType<
            TypeResolutionOutcome.Resolved>(
                context.Resolve(typeRequest));
        Assert.Equal(
            typeof(object).Assembly.GetName().Name,
            resolved.Definition.Assembly.Assembly.Identity.Name);
    }

    [Fact]
    public void AssemblyGroup_SelectsCoreLibraryFromTheRequestingDescriptor()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        ResolvedAssemblyReference template =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "group binding test"));
        int firstOpens = 0;
        int secondOpens = 0;
        ResolvedAssemblyReference first =
            ResolvedAssemblyReference.Create(
                template.Identity,
                path,
                () =>
                {
                    Interlocked.Increment(ref firstOpens);
                    return File.OpenRead(path);
                },
                template.Provenance);
        ResolvedAssemblyReference second =
            ResolvedAssemblyReference.Create(
                template.Identity,
                path,
                () =>
                {
                    Interlocked.Increment(ref secondOpens);
                    return File.OpenRead(path);
                },
                template.Provenance);
        ResolvedAssemblyReference coreLibrary =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(object).Assembly.Location,
                AssemblyResolutionProvenance.Platform(
                    "runtime",
                    frameworkVersion: null,
                    "group binding test"));
        var firstPolicy = new SelectedPolicy(coreLibrary);
        var secondPolicy = new SelectedPolicy(coreLibrary);
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (first, (IAssemblyBindingPolicy)firstPolicy),
                (second, (IAssemblyBindingPolicy)secondPolicy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.FromAssembly(second),
            AssemblyResolutionScope.Platform);

        var selected = Assert.IsType<
            AssemblyBindingSelection.Selected>(
                group.Select(request));
        _ = group.Select(request);

        Assert.Same(coreLibrary, selected.Assembly);
        Assert.Equal(0, firstOpens);
        Assert.Equal(1, secondOpens);
        Assert.Equal(0, firstPolicy.SelectionCount);
        Assert.Equal(1, secondPolicy.SelectionCount);
    }

    [Fact]
    public void AssemblyGroup_VersionSkewedRootRequiresIdentityPolicy()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        ResolvedAssemblyReference root =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "VersionSkewed.Library",
                    new Version(2, 0, 0, 0),
                    null,
                    null),
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local(
                    "version-skewed group binding test"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(root, (IAssemblyBindingPolicy)MissingPolicy.Instance)]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    root.Identity.Name,
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            AssemblyBindingOrigin.FromAssembly(root),
            AssemblyResolutionScope.Any);

        var unavailable = Assert.IsType<
            AssemblyBindingSelection.Unavailable>(
                group.Select(request));

        Assert.Equal(
            AssemblyBindingFailureKind.IdentityPolicyRequired,
            unavailable.Failure.Kind);
    }

    [Fact]
    public void AssemblyGroup_SelectedVersionOutsideGroupRequiresIdentityPolicy()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        var requested = new AssemblyReferenceIdentity(
            "VersionSkewed.Library",
            new Version(1, 0, 0, 0),
            null,
            null);
        ResolvedAssemblyReference selected =
            ResolvedAssemblyReference.Create(
                requested,
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local(
                    "selected version-skew test"));
        ResolvedAssemblyReference root =
            ResolvedAssemblyReference.Create(
                requested with { Version = new Version(2, 0, 0, 0) },
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local(
                    "group version-skew test"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(root, (IAssemblyBindingPolicy)new SelectedPolicy(selected))]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(root),
            AssemblyResolutionScope.Any);

        var unavailable = Assert.IsType<
            AssemblyBindingSelection.Unavailable>(
                group.Select(request));

        Assert.Equal(
            AssemblyBindingFailureKind.IdentityPolicyRequired,
            unavailable.Failure.Kind);
    }

    [Fact]
    public void AssemblyGroup_ExactRootIdentityUsesMetadataCaseSemantics()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        ResolvedAssemblyReference root =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    "en-US",
                    "001122aabbccddee"),
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local(
                    "case-equivalent group binding test"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(root, (IAssemblyBindingPolicy)MissingPolicy.Instance)]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "dependency",
                    new Version(1, 0, 0, 0),
                    "EN-us",
                    "001122aabbccddee".ToUpperInvariant())),
            AssemblyBindingOrigin.FromAssembly(root),
            AssemblyResolutionScope.Any);

        var selected = Assert.IsType<
            AssemblyBindingSelection.Selected>(
                group.Select(request));

        Assert.Same(root, selected.Assembly);
    }

    [Fact]
    public void AssemblyGroup_SelectedRootIdentityPreservesPolicyRollForward()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        var requested = new AssemblyReferenceIdentity(
            "VersionSkewed.Library",
            new Version(1, 0, 0, 0),
            null,
            null);
        AssemblyReferenceIdentity selectedIdentity =
            requested with { Version = new Version(2, 0, 0, 0) };
        ResolvedAssemblyReference selected =
            ResolvedAssemblyReference.Create(
                selectedIdentity,
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local(
                    "selected roll-forward test"));
        ResolvedAssemblyReference root =
            ResolvedAssemblyReference.Create(
                selectedIdentity,
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local(
                    "group roll-forward test"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(root, (IAssemblyBindingPolicy)new SelectedPolicy(selected))]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(root),
            AssemblyResolutionScope.Any);

        var result = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request));

        Assert.Same(selected, result.Assembly);
    }

    [Fact]
    public void Resolve_PlatformReference_RollsForwardToInstalledAssembly()
    {
        var current = typeof(System.Data.Common.DbDataReader).Assembly.GetName();
        string token = Convert.ToHexString(current.GetPublicKeyToken()!).ToLowerInvariant();
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(
            typeof(AssemblyDependencyResolverTests).Assembly.Location)
        {
            AllowPlatformAssemblyVersionRollForward = true,
        });

        var requested = new AssemblyReferenceIdentity(
            current.Name!,
            new Version(1, 0, 0, 0),
            null,
            token);
        var resolved = resolver.Resolve(
            requested,
            AssemblyResolutionScope.Platform);
        var repeated = resolver.Resolve(
            requested,
            AssemblyResolutionScope.Platform);

        Assert.NotNull(resolved);
        Assert.Same(resolved, repeated);
        Assert.Equal(current.Name + ".dll", Path.GetFileName(resolved.Path));

        var future = resolver.Resolve(
            new AssemblyReferenceIdentity(
                current.Name!,
                new Version(current.Version!.Major + 1, 0, 0, 0),
                null,
                token),
            AssemblyResolutionScope.Platform);
        Assert.Null(future);
    }

    sealed class SelectedPolicy(
        ResolvedAssemblyReference selected) : IAssemblyBindingPolicy
    {
        internal int SelectionCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            SelectionCount++;
            return AssemblyBindingSelection.Found(selected);
        }
    }

    sealed class MissingPolicy : IAssemblyBindingPolicy
    {
        internal static MissingPolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.NotFound();
    }

    [Fact]
    public void ResolveAll_IncludesTargetByDefaultAndLetsHarnessExcludeIt()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string target = Path.Combine(root, "Target.dll");
            string sibling = Path.Combine(root, "Sibling.dll");
            File.WriteAllText(target, "");
            File.WriteAllText(sibling, "");

            var defaultResolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(target)
            {
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
            });

            var harnessResolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(target)
            {
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
                ExcludeTargetAssembly = true,
            });

            Assert.Contains(defaultResolver.ResolveAll(), dependency => dependency.Path == target);
            Assert.Contains(defaultResolver.ResolveAll(), dependency => dependency.Path == sibling);
            Assert.DoesNotContain(harnessResolver.ResolveAll(), dependency => dependency.Path == target);
            Assert.Contains(harnessResolver.ResolveAll(), dependency => dependency.Path == sibling);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAll_RecordsPackageProvenanceForRefAssets()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string refDir = Path.Combine(root, "shared.package", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(refDir);
            string refPath = Path.Combine(refDir, "Shared.dll");
            File.WriteAllText(refPath, "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
            });

            var dependency = Assert.Single(resolver.ResolveAll());
            Assert.Equal(refPath, dependency.Path);
            Assert.Equal(AssemblyDependencyProvenance.PackageDependency, dependency.Provenance);
            Assert.Equal("shared.package", dependency.PackageId);
            Assert.Equal("8.0.0", dependency.PackageVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAll_PrefersSiblingAssemblyOverPackageAndFrameworkCandidates()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            string siblingPath = Path.Combine(targetDir, "Shared.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(siblingPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string packageRefDir = Path.Combine(root, "shared.package", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(packageRefDir);
            File.WriteAllText(Path.Combine(packageRefDir, "Shared.dll"), "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
            });

            var dependency = Assert.Single(resolver.ResolveAll(), dependency => Path.GetFileName(dependency.Path) == "Shared.dll");
            Assert.Equal(siblingPath, dependency.Path);
            Assert.Equal(AssemblyDependencyProvenance.SiblingAssembly, dependency.Provenance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAll_CanPreferImplementationPackageAssetsForDecompilerCallers()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string refDir = Path.Combine(root, "shared.package", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(refDir);
            string refPath = Path.Combine(refDir, "Shared.dll");
            File.WriteAllText(refPath, "");
            string libDir = Path.Combine(root, "shared.package", "8.0.0", "lib", "net8.0");
            Directory.CreateDirectory(libDir);
            string libPath = Path.Combine(libDir, "Shared.dll");
            File.WriteAllText(libPath, "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
                PreferImplementationAssemblies = true,
            });

            var dependency = Assert.Single(resolver.ResolveAll());
            Assert.Equal(libPath, dependency.Path);
            Assert.NotEqual(refPath, dependency.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_SelectsCurrentTfmDependencyGroup()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Root.Package</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework=".NETStandard2.0">
                        <dependency id="Shared.Package" version="2.0.0" />
                      </group>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string netstandardPath = CreatePackageAsset(root, "Shared.Package", "2.0.0", "lib", "netstandard2.0", "Shared.dll");
            string net8Path = CreatePackageAsset(root, "Shared.Package", "8.0.0", "lib", "net8.0", "Shared.dll");

            var references = AssemblyDependencyResolver.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(net8Path, references);
            Assert.DoesNotContain(netstandardPath, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_HandlesBracketExactRangeAndLowercaseVersionDirectory()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="[8.0.0-RC1]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string dependencyPath = CreatePackageAsset(root, "Shared.Package", "8.0.0-rc1", "lib", "net8.0", "Shared.dll");

            var references = AssemblyDependencyResolver.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(dependencyPath, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_PlatformTrustFindsTrustedAssemblyWhenPackageCandidateHasSameName()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="System.Runtime" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string refDir = Path.Combine(root, "system.runtime", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(refDir);
            File.WriteAllText(Path.Combine(refDir, "System.Runtime.dll"), "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
            });

            var resolved = resolver.Resolve(
                new AssemblyReferenceIdentity("System.Runtime", Version: null, Culture: null, PublicKeyToken: null),
                AssemblyResolutionScope.Platform);

            Assert.NotNull(resolved);
            Assert.Equal("System.Runtime.dll", Path.GetFileName(resolved.Path));
            using (resolved.OpenRead())
            {
            }
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}system.runtime{Path.DirectorySeparatorChar}", resolved.Path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreatePackageAsset(string root, string id, string version, string assetKind, string tfm, string fileName)
    {
        string assetDir = Path.Combine(root, id.ToLowerInvariant(), version.ToLowerInvariant(), assetKind, tfm);
        Directory.CreateDirectory(assetDir);
        string path = Path.Combine(assetDir, fileName);
        File.WriteAllText(path, "");
        return path;
    }

    static byte[] BuildAssembly(
        string assemblyName,
        byte[] publicKey)
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
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: metadata.GetOrAddBlob(publicKey),
            flags: AssemblyFlags.PublicKey,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public void Resolve_HonorsVersionAndCultureWhenIdentitySpecifiesThem()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.WriteAllText(targetPath, "");
            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
            });

            var runtimeVersion = typeof(object).Assembly.GetName().Version;
            Assert.NotNull(resolver.Resolve(
                new AssemblyReferenceIdentity("System.Private.CoreLib", runtimeVersion, Culture: null, PublicKeyToken: null),
                AssemblyResolutionScope.Platform));
            Assert.Null(resolver.Resolve(
                new AssemblyReferenceIdentity("System.Private.CoreLib", new Version(0, 0, 0, 0), Culture: null, PublicKeyToken: null),
                AssemblyResolutionScope.Platform));
            Assert.Null(resolver.Resolve(
                new AssemblyReferenceIdentity("System.Private.CoreLib", Version: null, Culture: "not-a-real-culture", PublicKeyToken: null),
                AssemblyResolutionScope.Platform));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
