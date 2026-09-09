using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

[Collection(ManifestPathEnvironmentCollection.Name)]
public partial class AssemblyDependencyResolverTests
{
    [Fact]
    public void ResolveAll_DepsJsonLocalPathCannotEscapeTargetDirectory()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-deps-path-").FullName;
        try
        {
            string targetDirectory = Path.Combine(root, "target");
            string outsideDirectory = Path.Combine(root, "outside");
            Directory.CreateDirectory(targetDirectory);
            Directory.CreateDirectory(outsideDirectory);
            string targetPath = Path.Combine(targetDirectory, "Target.dll");
            string outsidePath = Path.Combine(outsideDirectory, "Escape.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(outsidePath, "");
            File.WriteAllText(
                Path.Combine(targetDirectory, "Target.deps.json"),
                """
                {
                  "targets": {
                    "net9.0": {
                      "Escape/1.0.0": {
                        "runtime": {
                          "Escape.dll": {
                            "localPath": "../outside/Escape.dll"
                          }
                        }
                      }
                    }
                  },
                  "libraries": {}
                }
                """);

            var resolver = CreateManifestOnlyResolver(targetPath);

            Assert.DoesNotContain(
                resolver.ResolveAll(),
                dependency => dependency.Path == Path.GetFullPath(outsidePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAll_DepsJsonPackagePathCannotEscapeGlobalPackagesRoot()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-deps-path-").FullName;
        try
        {
            string targetDirectory = Path.Combine(root, "target");
            string packagesRoot = Path.Combine(root, "packages");
            string outsideDirectory = Path.Combine(root, "outside");
            Directory.CreateDirectory(targetDirectory);
            Directory.CreateDirectory(packagesRoot);
            Directory.CreateDirectory(outsideDirectory);
            string targetPath = Path.Combine(targetDirectory, "Target.dll");
            string outsidePath = Path.Combine(outsideDirectory, "Escape.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(outsidePath, "");
            File.WriteAllText(
                Path.Combine(targetDirectory, "Target.deps.json"),
                """
                {
                  "targets": {
                    "net9.0": {
                      "Escape/1.0.0": {
                        "runtime": {
                          "Escape.dll": {}
                        }
                      }
                    }
                  },
                  "libraries": {
                    "Escape/1.0.0": {
                      "path": "../outside"
                    }
                  }
                }
                """);
            using var environment =
                new NuGetPackagesEnvironment(packagesRoot);

            var resolver = CreateManifestOnlyResolver(targetPath);

            Assert.DoesNotContain(
                resolver.ResolveAll(),
                dependency => dependency.Path == Path.GetFullPath(outsidePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AssemblyDependencyResolver CreateManifestOnlyResolver(
        string targetPath)
        => new(
            new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [],
                IncludeSiblingAssemblies = false,
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = true,
            });

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
                            AssemblyResolutionScope.Any)).Selection);

            Assert.Equal(
                AssemblyBindingFailureKind.CandidateUnavailable,
                selection.Failure.Kind);
            Assert.Equal(
                CandidateOpenFailureKind.InvalidImage,
                selection.Failure.CandidateFailureKind);
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
                            AssemblyResolutionScope.Any)).Selection);

            Assert.Equal(
                AssemblyBindingFailureKind.CandidateUnavailable,
                selection.Failure.Kind);
            Assert.Equal(
                CandidateOpenFailureKind.InvalidImage,
                selection.Failure.CandidateFailureKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AssemblyDependencyResolver_PreservesOwnerIssuedNameDisposition()
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

            var missing = Assert.IsType<AssemblyBindingSelection.Missing>(
                resolver.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(platformIdentity),
                        AssemblyBindingOrigin.Global(),
                        AssemblyResolutionScope.Any)).Selection);

            Assert.Equal(
                AssemblyBindingMissDisposition.NameOwnedNoMatch,
                missing.Disposition);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstalledPlatformFallback_DoesNotOwnAbsentPrefixedName()
    {
        string targetPath = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
            });
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Microsoft.Absent.PlatformOwnershipProbe",
                    new Version(1, 0, 0, 0),
                    null,
                    "adb9793829ddae60")),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);

        var missing = Assert.IsType<AssemblyBindingSelection.Missing>(
            resolver.Select(request).Selection);

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
    }

    [Fact]
    public void KnownInventoryBindingPolicy_DistinguishesNameAbsenceFromIdentityMiss()
    {
        string targetPath = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = true,
                IncludeDepsJsonAssets = false,
                IncludeInstalledPlatformFallback = false,
            });
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Absent.Library",
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

        var missing = Assert.IsType<AssemblyBindingSelection.Missing>(
            resolver.Select(request).Selection);

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);

        AssemblyName targetName =
            typeof(AssemblyBindingSelection).Assembly.GetName();
        var ownedRequest = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    targetName.Name!,
                    new Version(
                        targetName.Version!.Major + 1,
                        0,
                        0,
                        0),
                    targetName.CultureName,
                    Convert.ToHexString(
                            targetName.GetPublicKeyToken() ?? [])
                        .ToLowerInvariant())),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var owned = Assert.IsType<AssemblyBindingSelection.Missing>(
            resolver.Select(ownedRequest).Selection);

        Assert.Equal(
            AssemblyBindingMissDisposition.NameOwnedNoMatch,
            owned.Disposition);
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
                    AssemblyResolutionScope.Any)).Selection);

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
                            AssemblyResolutionScope.Any)).Selection);

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
                resolver.Select(request).Selection);
        var repeated = Assert.IsType<
            AssemblyBindingSelection.Selected>(
                resolver.Select(request).Selection);

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
                group.Select(request).Selection);
        _ = group.Select(request).Selection;

        Assert.Same(coreLibrary, selected.Assembly);
        Assert.Equal(0, firstOpens);
        Assert.Equal(1, secondOpens);
        Assert.Equal(0, firstPolicy.SelectionCount);
        Assert.Equal(1, secondPolicy.SelectionCount);
    }

    [Fact]
    public void AssemblyGroup_DuplicateRegistrationIsRejected()
    {
        ResolvedAssemblyReference assembly = Descriptor(
            new AssemblyReferenceIdentity(
                "Duplicate.Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("duplicate owner"));

        Assert.Throws<ArgumentException>(
            () => new SourceRelativeAssemblyGroupBindingPolicy(
                [
                    (assembly, (IAssemblyBindingPolicy)MissingPolicy.Instance),
                    (assembly, (IAssemblyBindingPolicy)MissingPolicy.Instance),
                ]));
    }

    [Fact]
    public async Task AssemblyGroup_ConcurrentSelectionsRetainBothOccurrences()
    {
        ResolvedAssemblyReference defaultOwner = Descriptor(
            new AssemblyReferenceIdentity(
                "Default.Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("default owner"));
        ResolvedAssemblyReference firstOwner = Descriptor(
            new AssemblyReferenceIdentity(
                "First.Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("first owner"));
        ResolvedAssemblyReference secondOwner = Descriptor(
            new AssemblyReferenceIdentity(
                "Second.Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("second owner"));
        ResolvedAssemblyReference firstCandidate = Descriptor(
            new AssemblyReferenceIdentity(
                "First.Candidate",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("first candidate"));
        ResolvedAssemblyReference secondCandidate = Descriptor(
            new AssemblyReferenceIdentity(
                "Second.Candidate",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("second candidate"));
        using var barrier = new Barrier(2);
        var defaultPolicy = new FixedSelectionPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var firstPolicy =
            new CoordinatedSelectedPolicy(
                firstCandidate,
                barrier);
        var secondPolicy =
            new CoordinatedSelectedPolicy(
                secondCandidate,
                barrier);
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (defaultOwner, (IAssemblyBindingPolicy)defaultPolicy),
                (firstOwner, (IAssemblyBindingPolicy)firstPolicy),
                (secondOwner, (IAssemblyBindingPolicy)secondPolicy),
            ]);
        AssemblyBindingPolicyVersion version = group.Version;

        Task<AssemblyBindingSelectionSnapshot> firstSelection = Task.Run(
            () => group.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        firstCandidate.Identity),
                    AssemblyBindingOrigin.FromAssembly(firstOwner),
                    AssemblyResolutionScope.Any)));
        Task<AssemblyBindingSelectionSnapshot> secondSelection = Task.Run(
            () => group.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        secondCandidate.Identity),
                    AssemblyBindingOrigin.FromAssembly(secondOwner),
                    AssemblyResolutionScope.Any)));

        AssemblyBindingSelectionSnapshot[] snapshots =
            await Task.WhenAll(firstSelection, secondSelection);

        var firstSelected = Assert.IsType<AssemblyBindingSelection.Selected>(
            snapshots[0].Selection);
        var secondSelected = Assert.IsType<AssemblyBindingSelection.Selected>(
            snapshots[1].Selection);
        Assert.Same(firstCandidate, firstSelected.Assembly);
        Assert.Same(secondCandidate, secondSelected.Assembly);
        Assert.All(
            snapshots,
            snapshot => Assert.Same(version, snapshot.Version));

        var probe = new AssemblyReferenceIdentity(
            "Probe",
            new Version(1, 0, 0, 0),
            null,
            null);
        var firstProbe = Assert.IsType<
            AssemblyBindingSelection.Selected>(
                group.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(probe),
                        AssemblyBindingOrigin.FromOccurrence(firstSelected.Occurrence),
                        AssemblyResolutionScope.Any)).Selection);
        var secondProbe = Assert.IsType<
            AssemblyBindingSelection.Selected>(
                group.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(probe),
                        AssemblyBindingOrigin.FromOccurrence(secondSelected.Occurrence),
                        AssemblyResolutionScope.Any)).Selection);

        Assert.Same(firstCandidate, firstProbe.Assembly);
        Assert.Same(secondCandidate, secondProbe.Assembly);
        Assert.Equal(0, defaultPolicy.SelectionCount);
        Assert.Equal(2, firstPolicy.SelectionCount);
        Assert.Equal(2, secondPolicy.SelectionCount);
        Assert.Same(version, group.Version);
    }

    [Theory]
    [InlineData(AssemblyBindingMissDisposition.Undifferentiated)]
    [InlineData(AssemblyBindingMissDisposition.NoNameOwner)]
    [InlineData(AssemblyBindingMissDisposition.NameOwnedNoMatch)]
    public void IntrinsicFacadeMiss_ContinuesToLaterFacadeSelection(
        AssemblyBindingMissDisposition disposition)
    {
        byte[] image = BuildAssembly(
            "IntrinsicMissOwner",
            [1, 2, 3],
            assemblyReferences:
            [
                "System.Private.CoreLib",
                "mscorlib",
            ]);
        ResolvedAssemblyReference owner =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "IntrinsicMissOwner",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "intrinsic miss validation test"));
        int selectionCount = 0;

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            IntrinsicCoreLibraryBinding.Select(
                owner,
                _ => ++selectionCount == 1
                    ? Missing(disposition)
                    : AssemblyBindingSelection.Found(owner)));

        Assert.Same(owner, selected.Assembly);
        Assert.Equal(2, selectionCount);
    }

    [Theory]
    [InlineData(AssemblyBindingMissDisposition.Undifferentiated)]
    [InlineData(AssemblyBindingMissDisposition.NoNameOwner)]
    [InlineData(AssemblyBindingMissDisposition.NameOwnedNoMatch)]
    public void IntrinsicFacadeMisses_ExhaustAsUnsupportedScope(
        AssemblyBindingMissDisposition disposition)
    {
        byte[] image = BuildAssembly(
            "IntrinsicMissOwner",
            [1, 2, 3],
            assemblyReferences:
            [
                "System.Private.CoreLib",
                "mscorlib",
            ]);
        ResolvedAssemblyReference owner =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "IntrinsicMissOwner",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "intrinsic miss exhaustion test"));
        int selectionCount = 0;

        var unavailable =
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                IntrinsicCoreLibraryBinding.Select(
                    owner,
                    _ =>
                    {
                        selectionCount++;
                        return Missing(disposition);
                    }));

        Assert.Equal(
            AssemblyBindingFailureKind.UnsupportedScope,
            unavailable.Failure.Kind);
        Assert.Equal(2, selectionCount);
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
                group.Select(request).Selection);

        Assert.Equal(
            AssemblyBindingFailureKind.IdentityPolicyRequired,
            unavailable.Failure.Kind);
    }

    [Fact]
    public void AssemblyGroup_AbsentPlatformPrefixedNamePreservesAmbiguity()
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        var requested = new AssemblyReferenceIdentity(
            "Microsoft.Absent.PlatformOwnershipProbe",
            new Version(1, 0, 0, 0),
            null,
            "adb9793829ddae60");
        ResolvedAssemblyReference first = ResolvedAssemblyReference.Create(
            requested with { Version = new Version(2, 0, 0, 0) },
            path,
            () => File.OpenRead(path),
            AssemblyResolutionProvenance.Local(
                "first absent platform-prefixed root"));
        ResolvedAssemblyReference second = ResolvedAssemblyReference.Create(
            requested with { Version = new Version(3, 0, 0, 0) },
            path,
            () => File.OpenRead(path),
            AssemblyResolutionProvenance.Local(
                "second absent platform-prefixed root"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (first, (IAssemblyBindingPolicy)
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(path))),
                (second, (IAssemblyBindingPolicy)
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(path))),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(first),
            AssemblyResolutionScope.Platform);

        var ambiguous = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            group.Select(request).Selection);

        Assert.Equal([first, second], ambiguous.Assemblies);
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
                group.Select(request).Selection);

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
                group.Select(request).Selection);

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
            group.Select(request).Selection);

        Assert.Same(selected, result.Assembly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyGroup_DesignatedPrecedenceIsRegistrationOrderIndependent(
        bool designatedFirst)
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference platform = Descriptor(
            requested,
            AssemblyResolutionProvenance.Platform(
                "test platform",
                frameworkVersion: null,
                "group precedence test"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "group precedence test"));
        (ResolvedAssemblyReference, IAssemblyBindingPolicy)[] participants =
            designatedFirst
                ? [
                    (designated, MissingPolicy.Instance),
                    (platform, MissingPolicy.Instance),
                ]
                : [
                    (platform, MissingPolicy.Instance),
                    (designated, MissingPolicy.Instance),
                ];
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            participants);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(platform),
            AssemblyResolutionScope.Any);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request).Selection);

        Assert.Same(designated, selected.Assembly);
        Assert.Same(platform, Assert.Single(selected.ShadowedAssemblies));
    }

    [Theory]
    [InlineData(AssemblyBindingMissDisposition.Undifferentiated)]
    [InlineData(AssemblyBindingMissDisposition.NameOwnedNoMatch)]
    public void SourceRelativeAssemblyGroupBindingPolicy_ContinuesOnlyAfterNoNameOwner(
        AssemblyBindingMissDisposition disposition)
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "terminal miss test"));
        var policy = new FixedSelectionPolicy(
            Missing(disposition));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (designated, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var missing = Assert.IsType<AssemblyBindingSelection.Missing>(
            group.Select(request).Selection);

        Assert.Equal(disposition, missing.Disposition);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyBindingMissDisposition_UndifferentiatedLegacyMissFailsClosed()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "legacy terminal miss test"));
        var policy = new FixedSelectionPolicy(
            AssemblyBindingSelection.NotFound());
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (designated, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var missing = Assert.IsType<AssemblyBindingSelection.Missing>(
            group.Select(request).Selection);
        Assert.Equal(
            AssemblyBindingMissDisposition.Undifferentiated,
            missing.Disposition);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyBindingMissDisposition_CompleteExhaustionRequired()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "complete chain test"));
        var policy = new FixedSelectionPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (designated, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request).Selection);

        Assert.Same(designated, selected.Assembly);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyBindingMissDisposition_AllNoOwnerRemainsNoOwner()
    {
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, (IAssemblyBindingPolicy)MissingPolicy.Instance)]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Absent",
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var missing = Assert.IsType<AssemblyBindingSelection.Missing>(
            group.Select(request).Selection);

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
    }

    [Fact]
    public void AssemblyGroup_MultipleDesignatedCandidatesAreAmbiguous()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference platform = Descriptor(
            requested,
            AssemblyResolutionProvenance.Platform(
                "test platform",
                frameworkVersion: null,
                "group ambiguity test"));
        ResolvedAssemblyReference first = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "first group ambiguity candidate"));
        ResolvedAssemblyReference second = Descriptor(
            requested with { Version = new Version(3, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "second group ambiguity candidate"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (platform, (IAssemblyBindingPolicy)MissingPolicy.Instance),
                (first, (IAssemblyBindingPolicy)MissingPolicy.Instance),
                (second, (IAssemblyBindingPolicy)MissingPolicy.Instance),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(platform),
            AssemblyResolutionScope.Any);

        var ambiguous = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            group.Select(request).Selection);

        Assert.Equal(2, ambiguous.Assemblies.Length);
        Assert.Contains(first, ambiguous.Assemblies);
        Assert.Contains(second, ambiguous.Assemblies);
        Assert.DoesNotContain(platform, ambiguous.Assemblies);
    }

    [Fact]
    public void AssemblyGroup_LoneDesignatedCandidateIgnoresVersion()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "lone designated candidate"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(designated, (IAssemblyBindingPolicy)MissingPolicy.Instance)]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(designated),
            AssemblyResolutionScope.Platform);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request).Selection);

        Assert.Same(designated, selected.Assembly);
        Assert.Empty(selected.ShadowedAssemblies);
    }

    [Fact]
    public void AssemblyGroup_PlatformShadowRetainsVersionEligibility()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference platform = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Platform(
                "test platform",
                frameworkVersion: null,
                "group shadow eligibility test"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(3, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "group shadow eligibility test"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (platform, (IAssemblyBindingPolicy)MissingPolicy.Instance),
                (designated, (IAssemblyBindingPolicy)MissingPolicy.Instance),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(platform),
            AssemblyResolutionScope.Platform);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request).Selection);

        Assert.Same(designated, selected.Assembly);
        Assert.Empty(selected.ShadowedAssemblies);
    }

    [Fact]
    public void AssemblyGroup_SkewedDesignatedPreservesOriginNameOwner()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "group origin-policy test"));
        ResolvedAssemblyReference sibling = Descriptor(
            requested,
            AssemblyResolutionProvenance.Local("sibling"));
        var policy = new SelectedPolicy(sibling);
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (designated, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var unavailable = Assert.IsType<AssemblyBindingSelection.Unavailable>(
            group.Select(request).Selection);

        Assert.Equal(
            AssemblyBindingFailureKind.IdentityPolicyRequired,
            unavailable.Failure.Kind);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyGroup_SkewedDesignatedShadowsOriginPlatformSelection()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with { Version = new Version(2, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "group origin-policy test"));
        ResolvedAssemblyReference platform = Descriptor(
            requested,
            AssemblyResolutionProvenance.Platform(
                "test platform",
                frameworkVersion: null,
                "group origin-policy test"));
        var policy = new SelectedPolicy(platform);
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (designated, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request).Selection);

        Assert.Same(designated, selected.Assembly);
        Assert.Same(platform, Assert.Single(selected.ShadowedAssemblies));
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyGroup_DesignatedAmbiguityPreservesOriginFailure()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference first = Descriptor(
            requested,
            AssemblyResolutionProvenance.Designated(
                "first group overlay"));
        ResolvedAssemblyReference second = Descriptor(
            requested with { Version = new Version(3, 0, 0, 0) },
            AssemblyResolutionProvenance.Designated(
                "second group overlay"));
        var policy = new FixedSelectionPolicy(
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable,
                    CandidateOpenFailureKind.Unreadable)));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (first, (IAssemblyBindingPolicy)policy),
                (second, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var unavailable = Assert.IsType<AssemblyBindingSelection.Unavailable>(
            group.Select(request).Selection);

        Assert.Equal(
            AssemblyBindingFailureKind.CandidateUnavailable,
            unavailable.Failure.Kind);
        Assert.Equal(
            CandidateOpenFailureKind.Unreadable,
            unavailable.Failure.CandidateFailureKind);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyGroup_PolicyDesignatedCandidateJoinsAmbiguity()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        AssemblyReferenceIdentity designatedIdentity =
            requested with { Version = new Version(2, 0, 0, 0) };
        ResolvedAssemblyReference root = Descriptor(
            designatedIdentity,
            AssemblyResolutionProvenance.Designated(
                "root group overlay"));
        ResolvedAssemblyReference policyCandidate = Descriptor(
            designatedIdentity,
            AssemblyResolutionProvenance.Designated(
                "policy group overlay"));
        var policy = new FixedSelectionPolicy(
            AssemblyBindingSelection.Found(policyCandidate));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (root, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var ambiguous = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            group.Select(request).Selection);

        Assert.Equal(2, ambiguous.Assemblies.Length);
        Assert.Contains(root, ambiguous.Assemblies);
        Assert.Contains(policyCandidate, ambiguous.Assemblies);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyGroup_ExactPolicyDesignatedCandidateJoinsAmbiguity()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference root = Descriptor(
            requested,
            AssemblyResolutionProvenance.Designated(
                "root group overlay"));
        ResolvedAssemblyReference policyCandidate = Descriptor(
            requested,
            AssemblyResolutionProvenance.Designated(
                "policy group overlay"));
        var policy = new FixedSelectionPolicy(
            AssemblyBindingSelection.Found(policyCandidate));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (root, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var ambiguous = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            group.Select(request).Selection);

        Assert.Equal(2, ambiguous.Assemblies.Length);
        Assert.Contains(root, ambiguous.Assemblies);
        Assert.Contains(policyCandidate, ambiguous.Assemblies);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Fact]
    public void AssemblyGroup_ExactDesignatedRetainsAllPlatformShadows()
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            null,
            "001122aabbccddee");
        ResolvedAssemblyReference owner = Descriptor(
            new AssemblyReferenceIdentity(
                "Owner",
                new Version(1, 0, 0, 0),
                null,
                null),
            AssemblyResolutionProvenance.Local("owner"));
        ResolvedAssemblyReference designated = Descriptor(
            requested,
            AssemblyResolutionProvenance.Designated(
                "root group overlay"));
        ResolvedAssemblyReference rootPlatform = Descriptor(
            requested,
            AssemblyResolutionProvenance.Platform(
                "runtime",
                frameworkVersion: null,
                "root platform"));
        ResolvedAssemblyReference policyPlatform = Descriptor(
            requested,
            AssemblyResolutionProvenance.Platform(
                "runtime",
                frameworkVersion: null,
                "policy platform"));
        var policy = new FixedSelectionPolicy(
            AssemblyBindingSelection.Found(policyPlatform));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (owner, (IAssemblyBindingPolicy)policy),
                (designated, (IAssemblyBindingPolicy)policy),
                (rootPlatform, (IAssemblyBindingPolicy)policy),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request).Selection);

        Assert.Same(designated, selected.Assembly);
        Assert.Equal(2, selected.ShadowedAssemblies.Length);
        Assert.Contains(rootPlatform, selected.ShadowedAssemblies);
        Assert.Contains(policyPlatform, selected.ShadowedAssemblies);
        Assert.Equal(1, policy.SelectionCount);
    }

    [Theory]
    [InlineData("fr-FR", "001122aabbccddee")]
    [InlineData("en-US", "ffeeddccbbaa1100")]
    public void AssemblyGroup_DesignatedPrecedenceRetainsIdentityConstraints(
        string? designatedCulture,
        string designatedToken)
    {
        var requested = new AssemblyReferenceIdentity(
            "Platform.Library",
            new Version(1, 0, 0, 0),
            "en-US",
            "001122aabbccddee");
        ResolvedAssemblyReference platform = Descriptor(
            requested,
            AssemblyResolutionProvenance.Platform(
                "test platform",
                frameworkVersion: null,
                "group identity test"));
        ResolvedAssemblyReference designated = Descriptor(
            requested with
            {
                Version = new Version(2, 0, 0, 0),
                Culture = designatedCulture,
                PublicKeyToken = designatedToken,
            },
            AssemblyResolutionProvenance.Designated(
                "group identity test"));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (platform, (IAssemblyBindingPolicy)MissingPolicy.Instance),
                (designated, (IAssemblyBindingPolicy)MissingPolicy.Instance),
            ]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested),
            AssemblyBindingOrigin.FromAssembly(platform),
            AssemblyResolutionScope.Any);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            group.Select(request).Selection);

        Assert.Same(platform, selected.Assembly);
        Assert.Empty(selected.ShadowedAssemblies);
    }

    [Theory]
    [InlineData(false, AssemblyResolutionScope.Any)]
    [InlineData(true, AssemblyResolutionScope.Any)]
    [InlineData(true, AssemblyResolutionScope.Platform)]
    public void Select_DesignatedOverlayWinsWithEqualOrUnequalVersion(
        bool versionSkewed,
        AssemblyResolutionScope scope)
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-designated-overlay-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.Copy(
                typeof(AssemblyDependencyResolverTests).Assembly.Location,
                targetPath);
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
            byte[] publicKey =
                AssemblyName.GetAssemblyName(platformPath).GetPublicKey()
                ?? throw new InvalidOperationException(
                    "The platform fixture must carry a full public key.");
            Version designatedVersion = versionSkewed
                ? new Version(
                    (platformIdentity.Version?.Major ?? 1) + 1,
                    0,
                    0,
                    0)
                : platformIdentity.Version
                    ?? new Version(1, 0, 0, 0);
            string designatedPath = Path.Combine(
                root,
                $"{platformIdentity.Name}.dll");
            File.WriteAllBytes(
                designatedPath,
                BuildAssembly(
                    platformIdentity.Name,
                    publicKey,
                    designatedVersion));
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = [designatedPath],
                    IncludeSiblingAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                scope);

            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                resolver.Select(request).Selection);

            Assert.Equal(designatedPath, selected.Assembly.Path);
            Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
                selected.Assembly.Provenance);
            ResolvedAssemblyReference shadow =
                Assert.Single(selected.ShadowedAssemblies);
            Assert.Equal(platformPath, shadow.Path);
            Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                shadow.Provenance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_DuplicateSamePathSharesSnapshotAcrossProvenance()
    {
        string platformPath = typeof(System.Runtime.GCSettings)
            .Assembly.Location;
        using var stream = File.OpenRead(platformPath);
        using var peReader = new PEReader(stream);
        AssemblyReferenceIdentity platformIdentity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                peReader.GetMetadataReader());
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(
                typeof(AssemblyDependencyResolverTests).Assembly.Location)
            {
                PackageRoots = [],
                CorpusAssemblyPaths = [platformPath, platformPath],
                IncludeSiblingAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
                SnapshotAssemblyImages = true,
                MaxSnapshotImageBytes =
                    new FileInfo(platformPath).Length,
            });
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(platformIdentity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            resolver.Select(request).Selection);

        Assert.Equal(platformPath, selected.Assembly.Path);
        Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
            selected.Assembly.Provenance);
        ResolvedAssemblyReference shadow =
            Assert.Single(selected.ShadowedAssemblies);
        Assert.Equal(platformPath, shadow.Path);
        Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
            shadow.Provenance);
        Assert.NotSame(selected.Assembly, shadow);
    }

    [Fact]
    public void Select_SnapshotBudgetCannotFallBackFromDesignatedToPlatform()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-overlay-budget-").FullName;
        try
        {
            string platformPath = typeof(System.Runtime.GCSettings)
                .Assembly.Location;
            string designatedPath = Path.Combine(
                root,
                Path.GetFileName(platformPath));
            File.Copy(platformPath, designatedPath);
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(platformPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = [designatedPath],
                    IncludeSiblingAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                    SnapshotAssemblyImages = true,
                    MaxSnapshotImageBytes =
                        new FileInfo(platformPath).Length,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Platform);

            var unavailable =
                Assert.IsType<AssemblyBindingSelection.Unavailable>(
                    resolver.Select(request).Selection);

            Assert.Equal(
                AssemblyBindingFailureKind.CandidateUnavailable,
                unavailable.Failure.Kind);
            Assert.Equal(
                CandidateOpenFailureKind.ResourceBudget,
                unavailable.Failure.CandidateFailureKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_RenamedSnapshotBudgetCannotFallBackToPlatform()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-renamed-overlay-budget-").FullName;
        try
        {
            string platformPath = typeof(System.Runtime.GCSettings)
                .Assembly.Location;
            string designatedPath = Path.Combine(root, "overlay.dll");
            File.Copy(platformPath, designatedPath);
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(platformPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = [designatedPath],
                    IncludeSiblingAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                    SnapshotAssemblyImages = true,
                    MaxSnapshotImageBytes =
                        new FileInfo(platformPath).Length,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Platform);

            var unavailable =
                Assert.IsType<AssemblyBindingSelection.Unavailable>(
                    resolver.Select(request).Selection);

            Assert.Equal(
                AssemblyBindingFailureKind.CandidateUnavailable,
                unavailable.Failure.Kind);
            Assert.Equal(
                CandidateOpenFailureKind.ResourceBudget,
                unavailable.Failure.CandidateFailureKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_RenamedDesignatedOverlayUsesMetadataIdentity()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-renamed-overlay-").FullName;
        try
        {
            string platformPath = typeof(System.Runtime.GCSettings)
                .Assembly.Location;
            string designatedPath = Path.Combine(root, "overlay.dll");
            File.Copy(platformPath, designatedPath);
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(platformPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = [designatedPath],
                    IncludeSiblingAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Platform);

            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                resolver.Select(request).Selection);

            Assert.Equal(designatedPath, selected.Assembly.Path);
            Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
                selected.Assembly.Provenance);
            Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                Assert.Single(selected.ShadowedAssemblies).Provenance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_DesignatedOverlayRetainsInstalledPlatformShadow()
    {
        string platformPath = typeof(System.Runtime.GCSettings)
            .Assembly.Location;
        using var stream = File.OpenRead(platformPath);
        using var peReader = new PEReader(stream);
        AssemblyReferenceIdentity platformIdentity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                peReader.GetMetadataReader());
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(platformPath)
            {
                PackageRoots = [],
                CorpusAssemblyPaths = [platformPath],
                IncludeSiblingAssemblies = false,
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
            });
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(platformIdentity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            resolver.Select(request).Selection);

        Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
            selected.Assembly.Provenance);
        ResolvedAssemblyReference shadow =
            Assert.Single(selected.ShadowedAssemblies);
        Assert.Equal(platformPath, shadow.Path);
        Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
            shadow.Provenance);
    }

    [Fact]
    public void Select_UnreadableNonEligibleOverlayDoesNotVetoPlatform()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-unreadable-overlay-").FullName;
        try
        {
            string platformPath = typeof(System.Runtime.GCSettings)
                .Assembly.Location;
            string designatedPath = Path.Combine(
                root,
                Path.GetFileName(platformPath));
            File.WriteAllText(designatedPath, "not a managed assembly");
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(platformPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = [designatedPath],
                    IncludeSiblingAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Platform);

            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                resolver.Select(request).Selection);

            Assert.Equal(platformPath, selected.Assembly.Path);
            Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                selected.Assembly.Provenance);
            Assert.Empty(selected.ShadowedAssemblies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_UnreadablePeerDoesNotVetoDesignatedOverlay()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-unreadable-peer-").FullName;
        try
        {
            string platformPath = typeof(System.Runtime.GCSettings)
                .Assembly.Location;
            string unreadableDirectory = Path.Combine(root, "unreadable");
            Directory.CreateDirectory(unreadableDirectory);
            string unreadablePath = Path.Combine(
                unreadableDirectory,
                Path.GetFileName(platformPath));
            File.WriteAllText(unreadablePath, "not a managed assembly");
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(platformPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths =
                        [platformPath, unreadablePath],
                    IncludeSiblingAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Platform);

            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                resolver.Select(request).Selection);

            Assert.Equal(platformPath, selected.Assembly.Path);
            Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
                selected.Assembly.Provenance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_AnyScopeInstalledShadowRetainsRollForwardPolicy()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-any-fallback-shadow-").FullName;
        try
        {
            string platformPath = typeof(System.Runtime.GCSettings)
                .Assembly.Location;
            string designatedPath = Path.Combine(root, "overlay.dll");
            File.Copy(platformPath, designatedPath);
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var requested = platformIdentity with
            {
                Version = new Version(
                    platformIdentity.Version!.Major - 1,
                    0,
                    0,
                    0),
            };
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(platformPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = [designatedPath],
                    IncludeSiblingAssemblies = false,
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                    IncludeInstalledPlatformFallback = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(requested),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Any);

            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                resolver.Select(request).Selection);

            Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
                selected.Assembly.Provenance);
            ResolvedAssemblyReference shadow =
                Assert.Single(selected.ShadowedAssemblies);
            Assert.Equal(platformPath, shadow.Path);
            Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                shadow.Provenance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_MultipleDesignatedOverlaysAreAmbiguous()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-designated-ambiguity-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.Copy(
                typeof(AssemblyDependencyResolverTests).Assembly.Location,
                targetPath);
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
            byte[] publicKey =
                AssemblyName.GetAssemblyName(platformPath).GetPublicKey()
                ?? throw new InvalidOperationException(
                    "The platform fixture must carry a full public key.");
            var designatedPaths = new List<string>();
            for (int index = 0; index < 2; index++)
            {
                string directory = Path.Combine(root, $"overlay-{index}");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(
                    directory,
                    $"{platformIdentity.Name}.dll");
                File.WriteAllBytes(
                    path,
                    BuildAssembly(
                        platformIdentity.Name,
                        publicKey,
                        new Version(index + 20, 0, 0, 0)));
                designatedPaths.Add(path);
            }

            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = designatedPaths,
                    IncludeSiblingAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Any);

            var ambiguous = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
                resolver.Select(request).Selection);

            Assert.Equal(2, ambiguous.Assemblies.Length);
            Assert.All(
                ambiguous.Assemblies,
                candidate => Assert.IsType<
                    AssemblyResolutionProvenance.DesignatedAsset>(
                        candidate.Provenance));
            Assert.Equal(
                designatedPaths.Order(),
                ambiguous.Assemblies
                    .Select(candidate => candidate.Path)
                    .Order());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Select_AnyScopeDesignatedOverlayDoesNotBypassSiblingTier()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-designated-tier-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.Copy(
                typeof(AssemblyDependencyResolverTests).Assembly.Location,
                targetPath);
            string platformPath = typeof(System.Runtime.GCSettings)
                .Assembly.Location;
            string siblingPath = Path.Combine(
                root,
                Path.GetFileName(platformPath));
            File.Copy(platformPath, siblingPath);
            using var stream = File.OpenRead(platformPath);
            using var peReader = new PEReader(stream);
            AssemblyReferenceIdentity platformIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [],
                    CorpusAssemblyPaths = [platformPath],
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(platformIdentity),
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Any);

            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                resolver.Select(request).Selection);

            Assert.Equal(siblingPath, selected.Assembly.Path);
            Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(
                selected.Assembly.Provenance);
            Assert.Empty(selected.ShadowedAssemblies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                SelectionCount++;
                return AssemblyBindingSelection.Found(selected);

            }
        }
    }

    sealed class CoordinatedSelectedPolicy(
        ResolvedAssemblyReference selected,
        Barrier firstSelectionBarrier) : IAssemblyBindingPolicy
    {
        int _selectionCount;

        internal int SelectionCount =>
            Volatile.Read(ref _selectionCount);

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                if (Interlocked.Increment(ref _selectionCount) == 1
                    && !firstSelectionBarrier.SignalAndWait(
                        TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException(
                        "Concurrent binding selections did not rendezvous.");
                }

                return AssemblyBindingSelection.Found(selected);

            }
        }
    }

    sealed class MissingPolicy : IAssemblyBindingPolicy
    {
        internal static MissingPolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.NameNotOwned();
        }
    }

    static AssemblyBindingSelection Missing(
        AssemblyBindingMissDisposition disposition) =>
        disposition switch
        {
            AssemblyBindingMissDisposition.Undifferentiated =>
                AssemblyBindingSelection.NotFound(),
            AssemblyBindingMissDisposition.NoNameOwner =>
                AssemblyBindingSelection.NameNotOwned(),
            AssemblyBindingMissDisposition.NameOwnedNoMatch =>
                AssemblyBindingSelection.NameOwnedButNoMatch(),
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    sealed class FixedSelectionPolicy(
        AssemblyBindingSelection selection) : IAssemblyBindingPolicy
    {
        internal int SelectionCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                SelectionCount++;
                return selection;

            }
        }
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
    public void ResolveAll_NestedPackageRootsUseTheMostSpecificPackageContext()
    {
        string outerRoot = Directory.CreateTempSubdirectory(
            "dotnet-inspect-nested-package-root-").FullName;
        try
        {
            string packagesRoot = Path.Combine(
                outerRoot,
                ".nuget",
                "packages");
            string rootPackage = Path.Combine(outerRoot, "root-extraction");
            string targetDirectory = Path.Combine(
                rootPackage,
                "lib",
                "net8.0");
            Directory.CreateDirectory(targetDirectory);
            string targetPath = Path.Combine(targetDirectory, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(rootPackage, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Root.Package</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string dependencyDirectory = Path.Combine(
                packagesRoot,
                "shared.package",
                "8.0.0",
                "lib",
                "net8.0");
            Directory.CreateDirectory(dependencyDirectory);
            string dependencyPath = Path.Combine(
                dependencyDirectory,
                "Shared.dll");
            File.WriteAllBytes(dependencyPath, [1, 2, 3]);

            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(targetPath)
                {
                    PackageRoots = [outerRoot, packagesRoot],
                    RootPackageDirectory = rootPackage,
                    TargetFramework = "net8.0",
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeSiblingAssemblies = false,
                    IncludeDepsJsonAssets = false,
                });

            ResolvedAssemblyDependency dependency = Assert.Single(
                resolver.ResolveAll());
            Assert.Equal(dependencyPath, dependency.Path);
            Assert.Equal(
                AssemblyDependencyProvenance.PackageDependency,
                dependency.Provenance);
            Assert.Equal(
                "shared.package",
                dependency.PackageId,
                ignoreCase: true);
            Assert.Equal("8.0.0", dependency.PackageVersion);
        }
        finally
        {
            Directory.Delete(outerRoot, recursive: true);
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

    [Theory]
    [InlineData("8.0.0")]
    [InlineData("8.0.0-Beta")]
    public async Task ResolveAll_SourcePolicyUsesTheSameAdmittedCachePayloadAsPackageReplay(
        string dependencyVersion)
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-package-policy-").FullName;
        string appCache = Path.Combine(root, "app-cache");
        string globalRoot = Path.Combine(root, "global");
        string rootPackage = Path.Combine(root, "root-extraction");
        string staging = Path.Combine(root, "staging");
        const string source = "https://authorized.test/v3/index.json";
        const string otherSource = "https://other.test/v3/index.json";
        const string dependencyId = "Shared.Package";
        string? previousNuGetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable(
            "NUGET_PACKAGES",
            globalRoot);
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            appCache,
            skipNuGetCache: false);
        try
        {
            string targetDir = Path.Combine(rootPackage, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(rootPackage, "root.package.nuspec"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Root.Package</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="{dependencyVersion}" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            byte[] appAssembly = [1, 2, 3, 4];
            byte[] globalAssembly = [5, 6, 7, 8];
            byte[] appPackage = TestPackageArchive.Create(
                ("shared.package.nuspec", """
                    <?xml version="1.0" encoding="utf-8"?>
                    <package>
                      <metadata>
                        <id>Shared.Package</id>
                        <version>8.0.0-Beta</version>
                      </metadata>
                    </package>
                    """u8.ToArray()),
                ("lib/net8.0/Shared.dll", appAssembly));
            Directory.CreateDirectory(staging);
            string appNupkg = Path.Combine(
                staging,
                $"shared.package.{dependencyVersion.ToLowerInvariant()}.nupkg");
            File.WriteAllBytes(appNupkg, appPackage);
            string appExtracted = Path.Combine(staging, "extracted");
            ZipFile.ExtractToDirectory(appNupkg, appExtracted);
            NuGetCache.CommitPackage(
                appExtracted,
                appNupkg,
                dependencyId,
                dependencyVersion,
                NuGetCache.GetSourceKey(source));

            string globalPackage = Path.Combine(
                globalRoot,
                dependencyId.ToLowerInvariant(),
                dependencyVersion.ToLowerInvariant());
            string globalAssetDirectory = Path.Combine(
                globalPackage,
                "lib",
                "net8.0");
            Directory.CreateDirectory(globalAssetDirectory);
            File.WriteAllText(
                Path.Combine(globalPackage, ".nupkg.metadata"),
                $$"""{"source":"{{source}}"}""");
            File.WriteAllText(
                Path.Combine(globalPackage, "shared.package.nuspec"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Shared.Package</id>
                    <version>{dependencyVersion}</version>
                  </metadata>
                </package>
                """);
            string globalAsset = Path.Combine(globalAssetDirectory, "Shared.dll");
            File.WriteAllBytes(globalAsset, globalAssembly);

            var options = new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [globalRoot],
                RootPackageDirectory = rootPackage,
                PackageSourceOptions = new NuGetSourceOptions
                {
                    Sources = [source],
                },
                UsePackageSourcePolicy = true,
                TargetFramework = "net8.0",
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
                PreferImplementationAssemblies = true,
            };

            ResolvedAssemblyDependency appDependency = Assert.Single(
                new AssemblyDependencyResolver(options).ResolveAll());
            string appAsset = appDependency.Path;
            Assert.Equal(
                dependencyId,
                appDependency.PackageId,
                ignoreCase: true);
            Assert.Equal(
                dependencyVersion,
                appDependency.PackageVersion,
                ignoreCase: true);
            Assert.Equal(appAssembly, File.ReadAllBytes(appAsset));
            Assert.StartsWith(
                Path.GetFullPath(NuGetCache.GetPackageContentCachePath()),
                Path.GetFullPath(appAsset),
                StringComparison.Ordinal);
            using var client = new HttpClient(new NoNetworkHandler());
            PackageExtractionOutcome appReplay =
                await PackageExtractor.ExtractPackageAsync(
                    client,
                    $"{dependencyId}@{dependencyVersion}",
                    sourceOptions: options.PackageSourceOptions);
            Assert.True(appReplay.IsSuccess);
            Assert.Equal(
                Path.GetDirectoryName(
                    Path.GetDirectoryName(
                        Path.GetDirectoryName(appAsset)))!,
                appReplay.Result!.ExtractPath);

            File.WriteAllBytes(appAsset, [4, 3, 2, 1]);
            string fallbackAsset = Assert.Single(
                new AssemblyDependencyResolver(options).ResolveAll()).Path;
            Assert.Equal(globalAsset, fallbackAsset);
            Assert.Equal(globalAssembly, File.ReadAllBytes(fallbackAsset));
            using var globalClient =
                new HttpClient(new NoNetworkHandler());
            PackageExtractionOutcome globalReplay =
                await PackageExtractor.ExtractPackageAsync(
                    globalClient,
                    $"{dependencyId}@{dependencyVersion}",
                    sourceOptions: options.PackageSourceOptions);
            Assert.True(
                globalReplay.IsSuccess,
                globalReplay.ErrorMessage);
            Assert.Equal(
                globalPackage,
                globalReplay.Result!.ExtractPath);

            File.WriteAllText(
                Path.Combine(globalPackage, ".nupkg.metadata"),
                $$"""{"source":"{{otherSource}}"}""");
            Assert.Empty(
                new AssemblyDependencyResolver(options).ResolveAll());
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                previousNuGetPackages);
            NuGetCache.Initialize("dotnet-inspect");
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_SourceMappingDenialAndInvalidCoordinatesAreUnavailable()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-package-policy-denial-").FullName;
        try
        {
            string rootPackage = Path.Combine(root, "root-extraction");
            string targetDirectory = Path.Combine(
                rootPackage,
                "lib",
                "net8.0");
            Directory.CreateDirectory(targetDirectory);
            string targetPath = Path.Combine(targetDirectory, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(rootPackage, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Root.Package</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Unmapped.Package" version="1.0.0" />
                        <dependency id="../../escape" version="1.0.0" />
                        <dependency id="Invalid.Version" version="1.0.0/../x" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);
            string configPath = Path.Combine(root, "nuget.config");
            File.WriteAllText(
                configPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="mapped" value="https://mapped.test/v3/index.json" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="mapped">
                      <package pattern="Allowed.*" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);

            IReadOnlyList<string> references =
                AssemblyDependencyResolver.PackageDependencyReferencePaths(
                    targetPath,
                    packageRoots: [root],
                    preferImplementationAssemblies: true,
                    rootPackageDirectory: rootPackage,
                    targetFramework: "net8.0",
                    sourceOptions: new NuGetSourceOptions
                    {
                        ConfigFile = configPath,
                    },
                    useSourcePolicy: true);

            Assert.Empty(references);
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

    sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    static byte[] BuildAssembly(
        string assemblyName,
        byte[] publicKey,
        Version? version = null,
        string? culture = null,
        IReadOnlyList<string>? assemblyReferences = null,
        bool isModule = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        if (!isModule)
            metadata.AddAssembly(
                metadata.GetOrAddString(assemblyName),
                version ?? new Version(1, 0, 0, 0),
                culture is null
                    ? default
                    : metadata.GetOrAddString(culture),
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
        foreach (string reference in assemblyReferences ?? [])
        {
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(reference),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
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

    static ResolvedAssemblyReference Descriptor(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionProvenance provenance)
    {
        string path = typeof(AssemblyDependencyResolverTests)
            .Assembly.Location;
        return ResolvedAssemblyReference.Create(
            identity,
            path,
            () => File.OpenRead(path),
            provenance);
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
