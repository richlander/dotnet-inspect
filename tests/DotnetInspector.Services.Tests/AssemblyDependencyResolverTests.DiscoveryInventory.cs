using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public partial class AssemblyDependencyResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureDiscoveryInventory_PreservesVersionsRegistrationsAndTargetRoles(bool snapshots)
    {
        using var files = new DiscoveryFiles();
        string other = files.Write("other/Target.dll",
            BuildAssembly("Target", [], new Version(2, 0, 0, 0)));
        var resolver = new AssemblyDependencyResolver(files.Options with
        {
            IncludeSiblingAssemblies = true,
            CorpusAssemblyPaths = [other, other, files.Target],
            SnapshotAssemblyImages = snapshots,
        });

        Assert.Empty(resolver.ResolveAll());
        var target = Assert.IsType<ResolvedAssemblyReference>(resolver.AcquireTargetAssembly());
        var inventory = Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
        Assert.Same(resolver.Version, inventory.Version);
        Assert.Equal(4, inventory.Entries.Length);
        var sibling = inventory.Entries[0];
        var firstOther = inventory.Entries[1];
        var repeatedOther = inventory.Entries[2];
        var designatedTarget = inventory.Entries[3];

        Assert.True(sibling.IsTargetInput);
        Assert.True(designatedTarget.IsTargetInput);
        Assert.False(firstOther.IsTargetInput);
        Assert.False(repeatedOther.IsTargetInput);
        Assert.Equal(AssemblyDependencyProvenance.SiblingAssembly, sibling.Dependency.Provenance);
        Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(sibling.Provenance);
        Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(designatedTarget.Provenance);
        Assert.NotSame(target.Registration, Acquired(sibling).Registration);
        Assert.NotSame(Acquired(sibling).Registration, Acquired(designatedTarget).Registration);
        Assert.Same(Acquired(firstOther), Acquired(repeatedOther));
        Assert.Same(Acquired(firstOther), resolver.Acquire(firstOther.Dependency));
        Assert.Equal(new Version(1, 0, 0, 0), Acquired(sibling).Identity.Version);
        Assert.Equal(new Version(2, 0, 0, 0), Acquired(firstOther).Identity.Version);

        var repeated = Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
        Assert.Equal(inventory.Entries.Select(Acquired), repeated.Entries.Select(Acquired));
        Assert.Empty(resolver.ResolveAll());
    }

    [Fact]
    public void CaptureDiscoveryInventory_DoesNotChangeNameOwningTierOrBindingOrder()
    {
        using var files = new DiscoveryFiles();
        string sibling = files.Write("Dep.dll", BuildAssembly("Dep", [], new Version(1, 0, 0, 0)));
        string designated = files.Write("other/Dep.dll", BuildAssembly("Dep", [], new Version(2, 0, 0, 0)));
        var resolver = new AssemblyDependencyResolver(files.Options with
        {
            IncludeSiblingAssemblies = true,
            CorpusAssemblyPaths = [designated],
        });
        var older = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(new("Dep", new Version(1, 0, 0, 0), null, null)),
            AssemblyBindingOrigin.Global(), AssemblyResolutionScope.Any);
        var newer = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(new("Dep", new Version(2, 0, 0, 0), null, null)),
            AssemblyBindingOrigin.Global(), AssemblyResolutionScope.Any);
        var before = Assert.IsType<AssemblyBindingSelection.Selected>(resolver.Select(older).Selection);
        var inventory = Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));

        Assert.Equal(sibling, before.Assembly.Path);
        Assert.Same(before, resolver.Select(older).Selection);
        Assert.Equal(AssemblyBindingMissDisposition.NameOwnedNoMatch,
            Assert.IsType<AssemblyBindingSelection.Missing>(resolver.Select(newer).Selection).Disposition);
        Assert.Contains(inventory.Entries, row =>
            Acquired(row).Identity.Version == new Version(2, 0, 0, 0));
        Assert.Equal(sibling, Assert.Single(resolver.ResolveAll()).Path);
    }

    [Theory]
    [InlineData(false, "native")]
    [InlineData(true, "native")]
    [InlineData(false, "non-pe")]
    [InlineData(true, "non-pe")]
    [InlineData(false, "module")]
    [InlineData(true, "module")]
    [InlineData(false, "malformed")]
    [InlineData(true, "malformed")]
    public void CaptureDiscoveryInventory_PreservesMetadataClassification(bool snapshots, string kind)
    {
        using var files = new DiscoveryFiles();
        byte[] image = kind switch
        {
            "non-pe" => [1, 2, 3],
            "module" => BuildAssembly("Module", [], isModule: true),
            _ => BuildAssembly("Candidate", []),
        };
        if (kind is "native" or "malformed")
        {
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            if (kind == "native")
            {
                int directoryStart = pe.PEHeaders.CoffHeaderStartOffset + 20
                    + (pe.PEHeaders.PEHeader!.Magic == PEMagic.PE32 ? 96 : 112);
                Array.Clear(image, directoryStart + 14 * 8, 8);
            }
            else
                Array.Clear(image, pe.PEHeaders.MetadataStartOffset, 4);
        }
        string path = files.Write("Candidate.dll", image);
        var resolver = new AssemblyDependencyResolver(files.Options with
        {
            CorpusAssemblyPaths = [path],
            SnapshotAssemblyImages = snapshots,
        });
        var result = resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken);
        if (kind == "malformed")
        {
            var failure = Assert.IsType<AssemblyDependencyDiscoveryResult.Failed>(result);
            var rejected = Assert.IsType<AssemblyDependencyAcquisition.Rejected>(
                Assert.Single(failure.PartialEntries).Acquisition);
            Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Evidence.Failure.Kind);
            Assert.Empty(failure.DiscoveryFailures);
        }
        else
        {
            var inventory = Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(result);
            Assert.IsType<AssemblyDependencyAcquisition.Descriptorless>(
                Assert.Single(inventory.Entries).Acquisition);
        }
    }

    [Theory]
    [InlineData("corpus")]
    [InlineData("deps")]
    [InlineData("project")]
    public void CaptureDiscoveryInventory_EmittedMissingPathIsUnavailable(string tier)
    {
        using var files = new DiscoveryFiles();
        string missing = Path.Combine(files.Root, "Missing.dll");
        var options = files.Options;
        string? oldPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            if (tier == "corpus")
                options = options with { CorpusAssemblyPaths = [missing] };
            else if (tier == "deps")
            {
                files.WriteText("Target.deps.json", """
                    {"targets":{"net10.0":{"Missing/1.0.0":{"runtime":
                      {"Missing.dll":{"localPath":"Missing.dll"}}}}},"libraries":{}}
                    """);
                options = options with { IncludeDepsJsonAssets = true };
            }
            else
            {
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", files.Root);
                missing = Path.Combine(files.Root, "missing", "1.0.0", "Missing.dll");
                string manifest = files.WriteText("project.assets.json", """
                    {"targets":{"net10.0":{"Missing/1.0.0":{"compile":{"Missing.dll":{}}}}},
                     "libraries":{"Missing/1.0.0":{"type":"package","path":"missing/1.0.0"}}}
                    """);
                options = options with { ProjectAssetsPath = manifest };
            }
            var resolver = new AssemblyDependencyResolver(options);
            Assert.Empty(resolver.ResolveAll());
            var failure = Assert.IsType<AssemblyDependencyDiscoveryResult.Failed>(
                resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
            var row = Assert.Single(failure.PartialEntries);
            Assert.Equal(missing, row.Dependency.Path);
            Assert.Equal(CandidateOpenFailureKind.Unreadable,
                Assert.IsType<AssemblyDependencyAcquisition.Unavailable>(row.Acquisition).Failure.Kind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", oldPackages);
        }
    }

    [Theory]
    [InlineData("nuspec", false)]
    [InlineData("nuspec", true)]
    [InlineData("deps", false)]
    [InlineData("deps", true)]
    [InlineData("project", false)]
    [InlineData("project", true)]
    public void CaptureDiscoveryInventory_DocumentFailureCannotBecomePartialSuccess(string document, bool unreadable)
    {
        using var files = new DiscoveryFiles();
        string path = files.WriteText(document switch
        {
            "nuspec" => "root.nuspec",
            "deps" => "Target.deps.json",
            _ => "project.assets.json",
        }, unreadable ? "" : document == "nuspec" ? "<package>" : "{");
        var options = files.Options with { IncludeSiblingAssemblies = true };
        options = document switch
        {
            "nuspec" => options with { RootPackageDirectory = files.Root, TargetFramework = "net10.0" },
            "deps" => options with { IncludeDepsJsonAssets = true },
            _ => options with { ProjectAssetsPath = path },
        };
        using var held = unreadable ? File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        var resolver = new AssemblyDependencyResolver(options);

        Assert.Empty(resolver.ResolveAll());
        var failed = Assert.IsType<AssemblyDependencyDiscoveryResult.Failed>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
        Assert.Same(resolver.Version, failed.Version);
        Assert.True(Assert.Single(failed.PartialEntries).IsTargetInput);
        var failure = Assert.Single(failed.DiscoveryFailures);
        Assert.Equal(unreadable
            ? AssemblyDependencyDiscoveryFailureKind.Unreadable
            : AssemblyDependencyDiscoveryFailureKind.InvalidDocument, failure.Kind);
        Assert.Equal(document switch
        {
            "nuspec" => AssemblyDependencyProvenance.PackageDependency,
            "deps" => AssemblyDependencyProvenance.DepsJsonAsset,
            _ => AssemblyDependencyProvenance.ProjectAsset,
        }, failure.Tier);
    }

    public static TheoryData<string, string, bool> DiscoveryDocumentStructures => new()
    {
        { "deps", "{}", false },
        { "deps", """{"targets":{}}""", false },
        { "deps", """{"libraries":{}}""", false },
        { "deps", """{"targets":[],"libraries":{}}""", false },
        { "deps", """{"targets":{},"libraries":[]}""", false },
        { "deps", """{"targets":{"net10.0":[]},"libraries":{}}""", false },
        { "deps", """{"targets":{},"libraries":{"P/1.0.0":null}}""", false },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":[]}},"libraries":{}}""", false },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"compile":[]}}},"libraries":{}}""", false },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":null}}},"libraries":{}}""", false },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":{"P.dll":null}}}},"libraries":{}}""", false },
        { "project", "{}", false },
        { "project", """{"targets":{}}""", false },
        { "project", """{"libraries":{}}""", false },
        { "project", """{"targets":[],"libraries":{}}""", false },
        { "project", """{"targets":{},"libraries":[]}""", false },
        { "project", """{"targets":{"net10.0":[]},"libraries":{}}""", false },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":[]}},"libraries":{}}""", false },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":[]}}},"libraries":{}}""", false },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":[]}}},"libraries":{"P/1.0.0":{"path":"p/1.0.0"}}}""", false },
        { "nuspec", "<other />", false },
        { "nuspec", "<Package />", false },
        { "nuspec", """<package xmlns="urn:not-nuspec" />""", false },
        { "deps", """{"targets":{},"libraries":{}}""", true },
        { "deps", """{"targets":{"net10.0":{}},"libraries":{}}""", true },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{}}},"libraries":{}}""", true },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"compile":{},"runtime":{}}}},"libraries":{}}""", true },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":{"_._":{}}}}},"libraries":{}}""", true },
        { "project", """{"targets":{},"libraries":{}}""", true },
        { "project", """{"targets":{"net10.0":{}},"libraries":{}}""", true },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{}}},"libraries":{"P/1.0.0":{"path":"p/1.0.0"}}}""", true },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":{},"runtime":[]}}},"libraries":{"P/1.0.0":{"path":"p/1.0.0"}}}""", true },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":{}}}},"libraries":{"P/1.0.0":{"type":"project"}}}""", true },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":{"_._":{}}}}},"libraries":{"P/1.0.0":{"path":"p/1.0.0"}}}""", true },
        { "nuspec", "<package />", true },
        { "nuspec", "<package><metadata><dependencies /></metadata></package>", true },
        { "nuspec", """<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata /></package>""", true },
        { "nuspec", """<package><metadata><dependencies><dependency id="P" version="[1.0,2.0)" /></dependencies></metadata></package>""", true },
    };

    [Theory]
    [MemberData(nameof(DiscoveryDocumentStructures))]
    public void CaptureDiscoveryInventory_DistinguishesRequiredStructureFromEmptyDiscovery(
        string document, string content, bool valid)
    {
        using var files = new DiscoveryFiles();
        var resolver = new AssemblyDependencyResolver(DiscoveryDocumentOptions(files, document, content));
        var result = resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken);
        if (valid)
        {
            Assert.Empty(Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(result).Entries);
            Assert.Empty(resolver.ResolveAll());
        }
        else
        {
            var failed = Assert.IsType<AssemblyDependencyDiscoveryResult.Failed>(result);
            Assert.Empty(failed.PartialEntries);
            Assert.Equal(AssemblyDependencyDiscoveryFailureKind.InvalidDocument,
                Assert.Single(failed.DiscoveryFailures).Kind);
        }
    }

    public static TheoryData<string, string> RejectedDiscoveryPaths => new()
    {
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":{"P.dll":{"localPath":"../outside.dll"}}}}},"libraries":{}}""" },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":{"P.dll":{"localPath":null}}}}},"libraries":{}}""" },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":{"P.dll":{}}}}},"libraries":{"P/1.0.0":{"path":"../outside"}}}""" },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":{"../outside.dll":{}}}}},"libraries":{"P/1.0.0":{"path":"p/1.0.0"}}}""" },
        { "deps", """{"targets":{"net10.0":{"P/1.0.0":{"runtime":{}}}},"libraries":{"P/1.0.0":{"path":""}}}""" },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":{}}}},"libraries":{"P/1.0.0":{"path":"../outside"}}}""" },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":{"../outside.dll":{}}}}},"libraries":{"P/1.0.0":{"path":"p/1.0.0"}}}""" },
        { "project", """{"targets":{"net10.0":{"P/1.0.0":{"compile":{}}}},"libraries":{"P/1.0.0":{"path":""}}}""" },
    };

    [Theory]
    [MemberData(nameof(RejectedDiscoveryPaths))]
    public void CaptureDiscoveryInventory_RejectsDeclaredPathsWithoutChangingLegacyProjection(
        string document, string content)
    {
        using var files = new DiscoveryFiles();
        var resolver = new AssemblyDependencyResolver(DiscoveryDocumentOptions(files, document, content));
        Assert.Empty(resolver.ResolveAll());
        var failed = Assert.IsType<AssemblyDependencyDiscoveryResult.Failed>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
        Assert.Empty(failed.PartialEntries);
        Assert.Equal(AssemblyDependencyDiscoveryFailureKind.InvalidDocument,
            Assert.Single(failed.DiscoveryFailures).Kind);
    }

    [Fact]
    public void CaptureDiscoveryInventory_LateStructuralFailureRetainsOnlyPartialEvidence()
    {
        using var files = new DiscoveryFiles();
        var resolver = new AssemblyDependencyResolver(DiscoveryDocumentOptions(files, "deps", """
            {"targets":{"net10.0":{"P/1.0.0":{"runtime":
              {"Target.dll":{"localPath":"Target.dll"}}},"Invalid":{"runtime":[]}}},"libraries":{}}
            """) with { ExcludeTargetAssembly = false });
        Assert.Equal(files.Target, Assert.Single(resolver.ResolveAll()).Path);
        var failed = Assert.IsType<AssemblyDependencyDiscoveryResult.Failed>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
        var observed = Assert.Single(failed.PartialEntries);
        Assert.True(observed.IsTargetInput);
        Assert.Equal("Target", Acquired(observed).Identity.Name);
        Assert.Equal(AssemblyDependencyDiscoveryFailureKind.InvalidDocument,
            Assert.Single(failed.DiscoveryFailures).Kind);
    }

    [Fact]
    public void CaptureDiscoveryInventory_PreservesTargetFrameworkSelectionBoundary()
    {
        using var files = new DiscoveryFiles();
        var options = DiscoveryDocumentOptions(files, "project", """
            {"targets":{"net8.0":{"P/1.0.0":{"compile":{"Missing.dll":{}}}}},"libraries":{}}
            """) with { TargetFramework = "net10.0" };
        var resolver = new AssemblyDependencyResolver(options);
        Assert.Empty(Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken)).Entries);
    }

    static AssemblyDependencyResolutionOptions DiscoveryDocumentOptions(
        DiscoveryFiles files, string document, string content)
    {
        string path = files.WriteText(document switch
        {
            "nuspec" => "root.nuspec",
            "deps" => "Target.deps.json",
            _ => "project.assets.json",
        }, content);
        return document switch
        {
            "nuspec" => files.Options with { RootPackageDirectory = files.Root, TargetFramework = "net10.0" },
            "deps" => files.Options with { IncludeDepsJsonAssets = true },
            _ => files.Options with { ProjectAssetsPath = path },
        };
    }

    [Fact]
    public void CaptureDiscoveryInventory_AbsentOptionalDocumentsAreNotFailures()
    {
        using var files = new DiscoveryFiles();
        var resolver = new AssemblyDependencyResolver(files.Options with
        {
            RootPackageDirectory = files.Root,
            TargetFramework = "net10.0",
            ProjectAssetsPath = Path.Combine(files.Root, "absent.assets.json"),
            IncludeDepsJsonAssets = true,
        });
        Assert.Empty(Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken)).Entries);
    }

    [Fact]
    public void CaptureDiscoveryInventory_BudgetExhaustionRetainsFailedRows()
    {
        using var files = new DiscoveryFiles();
        var resolver = new AssemblyDependencyResolver(files.Options with
        {
            CorpusAssemblyPaths = [files.Target],
            SnapshotAssemblyImages = true,
            MaxSnapshotImageBytes = 0,
        });
        var failure = Assert.IsType<AssemblyDependencyDiscoveryResult.Failed>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
        var row = Assert.Single(failure.PartialEntries);
        Assert.Equal(CandidateOpenFailureKind.ResourceBudget,
            Assert.IsType<AssemblyDependencyAcquisition.Unavailable>(row.Acquisition).Failure.Kind);
        Assert.Throws<AssemblyDependencySnapshotBudgetExceededException>(() => resolver.Acquire(row.Dependency));
    }

    [Fact]
    public void CaptureDiscoveryInventory_SnapshotCacheRetainsImagesAcrossProvenance()
    {
        using var files = new DiscoveryFiles();
        byte[] original = File.ReadAllBytes(files.Target);
        var resolver = new AssemblyDependencyResolver(files.Options with
        {
            IncludeSiblingAssemblies = true,
            CorpusAssemblyPaths = [files.Target],
            SnapshotAssemblyImages = true,
            MaxSnapshotImageBytes = original.Length,
        });
        var inventory = Assert.IsType<AssemblyDependencyDiscoveryResult.Captured>(
            resolver.CaptureDiscoveryInventory(TestContext.Current.CancellationToken));
        Assert.Equal(2, inventory.Entries.Length);
        File.WriteAllBytes(files.Target, BuildAssembly("Replacement", []));
        foreach (var row in inventory.Entries)
        {
            using var stream = Acquired(row).OpenRead();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.Equal(original, copy.ToArray());
            Assert.Equal("Target", Acquired(row).Identity.Name);
        }
        Assert.NotSame(Acquired(inventory.Entries[0]).Registration, Acquired(inventory.Entries[1]).Registration);
    }

    [Fact]
    public void CaptureDiscoveryInventory_CancellationRetainsExceptionSemantics()
    {
        using var files = new DiscoveryFiles();
        var resolver = new AssemblyDependencyResolver(files.Options);
        Assert.Throws<OperationCanceledException>(() =>
            resolver.CaptureDiscoveryInventory(new CancellationToken(canceled: true)));
    }

    static ResolvedAssemblyReference Acquired(AssemblyDependencyDiscoveryEntry row) =>
        Assert.IsType<AssemblyDependencyAcquisition.Acquired>(row.Acquisition).Assembly;

    sealed class DiscoveryFiles : IDisposable
    {
        internal DiscoveryFiles()
        {
            Root = Path.Combine(Environment.CurrentDirectory, "artifacts", "dependency-inventory-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Target = Write("Target.dll", BuildAssembly("Target", []));
        }

        internal string Root { get; }
        internal string Target { get; }
        internal AssemblyDependencyResolutionOptions Options => new(Target)
        {
            PackageRoots = [],
            IncludeTrustedPlatformAssemblies = false,
            IncludeAspNetCoreSharedFramework = false,
            IncludeSiblingAssemblies = false,
            IncludeDepsJsonAssets = false,
            ExcludeTargetAssembly = true,
        };

        internal string Write(string relativePath, byte[] image)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, image);
            return path;
        }

        internal string WriteText(string relativePath, string text) =>
            Write(relativePath, System.Text.Encoding.UTF8.GetBytes(text));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
