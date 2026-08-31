using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using DotnetInspector.Core;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for PlatformResolver, particularly around DOTNET_ROOT priority and Linux path detection.
/// </summary>
// Some tests here mutate the process-global DOTNET_ROOT env var, which GetSharedDirectory /
// GetInstalledFrameworks read live. Share the "Console" collection so these run serially with
// other PlatformResolver-reading tests and never race a parallel reader (#1256).
[Collection("Console")]
public class PlatformResolverTests
{
    [Fact]
    public void FrameworkMappings_ContainsExpectedFrameworks()
    {
        Assert.True(PlatformResolver.FrameworkMappings.ContainsKey("runtime"));
        Assert.True(PlatformResolver.FrameworkMappings.ContainsKey("aspnetcore"));
        Assert.True(PlatformResolver.FrameworkMappings.ContainsKey("netstandard"));

        Assert.Equal("Microsoft.NETCore.App.Ref", PlatformResolver.FrameworkMappings["runtime"]);
        Assert.Equal("Microsoft.AspNetCore.App.Ref", PlatformResolver.FrameworkMappings["aspnetcore"]);
        Assert.Equal("NETStandard.Library.Ref", PlatformResolver.FrameworkMappings["netstandard"]);
    }

    [Fact]
    public void ReverseFrameworkMappings_MapsBackToShortNames()
    {
        Assert.Equal("runtime", PlatformResolver.ReverseFrameworkMappings["Microsoft.NETCore.App.Ref"]);
        Assert.Equal("aspnetcore", PlatformResolver.ReverseFrameworkMappings["Microsoft.AspNetCore.App.Ref"]);
        Assert.Equal("netstandard", PlatformResolver.ReverseFrameworkMappings["NETStandard.Library.Ref"]);
    }

    [Fact]
    public void GetInstalledVersions_NonExistentPath_ReturnsEmpty()
    {
        var versions = PlatformResolver.GetInstalledVersions("/nonexistent/path/that/does/not/exist");

        Assert.Empty(versions);
    }

    [Fact]
    public void GetInstalledVersions_SortsVersionsDescending()
    {
        // Use a temp directory with version-like subdirectories
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "8.0.0"));
            Directory.CreateDirectory(Path.Combine(tempDir, "9.0.1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "10.0.0-preview.1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "7.0.15"));

            var versions = PlatformResolver.GetInstalledVersions(tempDir);

            Assert.Equal(4, versions.Count);
            // Should be sorted descending: 10.x, 9.x, 8.x, 7.x
            Assert.Equal("10.0.0-preview.1", versions[0]);
            Assert.Equal("9.0.1", versions[1]);
            Assert.Equal("8.0.0", versions[2]);
            Assert.Equal("7.0.15", versions[3]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledVersions_OrdersPrereleasesOfSameBaseVersion()
    {
        // Regression: prerelease labels sharing a base version (e.g. multiple
        // 11.0.0 previews installed side-by-side) must order by SemVer
        // precedence, not collapse to the base version and fall back to
        // directory enumeration order.
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "11.0.0-preview.1.26104.118"));
            Directory.CreateDirectory(Path.Combine(tempDir, "11.0.0-preview.3.26207.106"));
            Directory.CreateDirectory(Path.Combine(tempDir, "11.0.0-preview.4.26230.115"));
            Directory.CreateDirectory(Path.Combine(tempDir, "11.0.0-preview.2.26159.112"));

            var versions = PlatformResolver.GetInstalledVersions(tempDir);

            Assert.Equal(4, versions.Count);
            Assert.Equal("11.0.0-preview.4.26230.115", versions[0]);
            Assert.Equal("11.0.0-preview.3.26207.106", versions[1]);
            Assert.Equal("11.0.0-preview.2.26159.112", versions[2]);
            Assert.Equal("11.0.0-preview.1.26104.118", versions[3]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledVersions_OrdersReleaseAbovePrereleaseOfSameBase()
    {
        // A stable release must sort above any prerelease sharing its base version.
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "11.0.0-preview.4.26230.115"));
            Directory.CreateDirectory(Path.Combine(tempDir, "11.0.0"));

            var versions = PlatformResolver.GetInstalledVersions(tempDir);

            Assert.Equal(2, versions.Count);
            Assert.Equal("11.0.0", versions[0]);
            Assert.Equal("11.0.0-preview.4.26230.115", versions[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledVersions_IgnoresNonVersionDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "8.0.0"));
            Directory.CreateDirectory(Path.Combine(tempDir, "ref"));  // Not a version
            Directory.CreateDirectory(Path.Combine(tempDir, "tools")); // Not a version

            var versions = PlatformResolver.GetInstalledVersions(tempDir);

            Assert.Single(versions);
            Assert.Equal("8.0.0", versions[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveAssembly_RuntimeOnlyImplementationAssembly_ResolvesFromSharedRuntime()
    {
        // Runtime-only implementation assemblies (e.g. System.Private.CoreLib) exist
        // in the shared runtime but have no ref-pack counterpart. They must still
        // resolve (as Platform) rather than falling through to a NuGet lookup.
        if (PlatformResolver.GetSharedDirectory() is not { } sharedDir
            || !Directory.Exists(Path.Combine(sharedDir, "Microsoft.NETCore.App")))
        {
            Assert.Skip("No shared Microsoft.NETCore.App runtime installed.");
            return;
        }

        var (assemblyPath, framework, version, error) =
            PlatformResolver.ResolveAssembly("System.Private.CoreLib");

        Assert.Null(error);
        Assert.NotNull(assemblyPath);
        Assert.EndsWith("System.Private.CoreLib.dll", assemblyPath);
        Assert.Contains(Path.Combine("shared", "Microsoft.NETCore.App"), assemblyPath);
        Assert.Equal("runtime", framework);
        Assert.NotNull(version);
    }

    [Fact]
    public void ResolveAssembly_AssemblyNameCannotEscapeReferencePack()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-platform-resolution-").FullName;
        try
        {
            string referenceDirectory = Path.Combine(
                root,
                "Microsoft.NETCore.App.Ref",
                "1.0.0",
                "ref",
                "net1.0");
            Directory.CreateDirectory(referenceDirectory);
            Directory.CreateDirectory(Path.Combine(referenceDirectory, "System.X"));

            string payload = Path.Combine(root, "payload.dll");
            File.Copy(typeof(PlatformResolverTests).Assembly.Location, payload);
            string valid = Path.Combine(referenceDirectory, "System.Valid.dll");
            File.Copy(typeof(PlatformResolverTests).Assembly.Location, valid);

            var escaped = PlatformResolver.ResolveAssembly(
                "System.X/../../../../../payload",
                frameworkSpec: "runtime@1.0.0",
                packsDirectory: root);
            var resolved = PlatformResolver.ResolveAssembly(
                "System.Valid",
                frameworkSpec: "runtime@1.0.0",
                packsDirectory: root);

            Assert.Null(escaped.AssemblyPath);
            Assert.NotNull(escaped.Error);
            Assert.Equal(valid, resolved.AssemblyPath);
            Assert.Null(resolved.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClassifyAssemblySurface_UnsafeFacade_ReturnsFacade()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime.CompilerServices.Unsafe");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime.CompilerServices.Unsafe not available: {error}");
            return;
        }

        var classified = Assert.IsType<
            AssemblySurfaceClassificationOutcome.Classified>(
                PlatformResolver.ClassifyAssemblySurface(assemblyPath));
        Assert.Equal(
            AssemblySurfaceKind.Facade,
            classified.Classification.Kind);
        Assert.True(classified.Classification.ForwarderCount > 0);
        Assert.Equal(0, classified.Classification.MeaningfulPublicTypeCount);
    }

    [Fact]
    public void ClassifyAssemblySurface_SystemTextJson_ReturnsImplementation()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        var classified = Assert.IsType<
            AssemblySurfaceClassificationOutcome.Classified>(
                PlatformResolver.ClassifyAssemblySurface(assemblyPath));
        Assert.Equal(
            AssemblySurfaceKind.Implementation,
            classified.Classification.Kind);
        Assert.True(
            classified.Classification.MeaningfulPublicTypeCount > 0);
    }

    [Theory]
    [InlineData("Dictionary<TKey,TValue>", "System.Collections")]
    [InlineData("System.Collections.Generic.Dictionary`2", "System.Collections")]
    [InlineData("FrozenDictionary", "System.Collections.Immutable")]
    [InlineData("int", "System.Runtime")]
    public void LookupType_ResolvesDefinitionDeterministically(
        string pattern,
        string expectedAssembly)
    {
        var resolved = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
            PlatformResolver.LookupType(pattern));

        Assert.Equal(
            expectedAssembly,
            resolved.Candidate.Assembly.Identity.Name);
        Assert.Equal(
            PlatformTypeDeclarationKind.Definition,
            resolved.Candidate.DeclarationKind);
    }

    [Fact]
    public void LookupType_MissingPattern_ReturnsMissing()
    {
        Assert.IsType<PlatformTypeLookupOutcome.Missing>(
            PlatformResolver.LookupType(
                $"Definitely.Missing.Type{Guid.NewGuid():N}"));
    }

    [Fact]
    public void LookupType_ExplicitMissingGenericArity_ReturnsMissing()
    {
        Assert.IsType<PlatformTypeLookupOutcome.Missing>(
            PlatformResolver.LookupType("Dictionary<T1,T2,T3>"));
    }

    [Fact]
    public void PlatformTypeCatalog_UnavailableDirectory_IsRetried()
    {
        string referencePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-platform-catalog-{Guid.NewGuid():N}");
        string typeName = typeof(PlatformResolverTests).FullName!;
        try
        {
            var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                PlatformTypeCatalog.Lookup(
                    typeName,
                    referencePath,
                    "test",
                    "1.0.0"));
            Assert.Equal(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                rejected.Failure.Kind);

            Directory.CreateDirectory(referencePath);
            File.Copy(
                typeof(PlatformResolverTests).Assembly.Location,
                Path.Combine(referencePath, "CatalogFixture.dll"));

            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformTypeCatalog.Lookup(
                    typeName,
                    referencePath,
                    "test",
                    "1.0.0"));
        }
        finally
        {
            if (Directory.Exists(referencePath))
                Directory.Delete(referencePath, recursive: true);
        }
    }

    [Fact]
    public void PlatformTypeCatalog_EmptyDirectory_IsRejectedAndRetried()
    {
        string referencePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-platform-catalog-{Guid.NewGuid():N}");
        string typeName = typeof(PlatformResolverTests).FullName!;
        try
        {
            Directory.CreateDirectory(referencePath);
            var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                PlatformTypeCatalog.Lookup(
                    typeName,
                    referencePath,
                    "test",
                    "1.0.0"));
            Assert.Equal(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                rejected.Failure.Kind);

            File.Copy(
                typeof(PlatformResolverTests).Assembly.Location,
                Path.Combine(referencePath, "CatalogFixture.dll"));

            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformTypeCatalog.Lookup(
                    typeName,
                    referencePath,
                    "test",
                    "1.0.0"));
        }
        finally
        {
            if (Directory.Exists(referencePath))
                Directory.Delete(referencePath, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PlatformTypeCatalog_PreservesMetadataFailure(
        int failure)
    {
        string packsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-platform-format-{Guid.NewGuid():N}");
        string referencePath = Path.Combine(
            packsDirectory,
            "Microsoft.NETCore.App.Ref",
            "1.0.0",
            "ref",
            "net1.0");
        try
        {
            Directory.CreateDirectory(referencePath);
            File.WriteAllBytes(
                Path.Combine(referencePath, "Rejected.dll"),
                failure switch
                {
                    0 => BuildNoMetadataImage(),
                    1 => BuildManagedWindowsMetadata(),
                    2 => BuildMalformedMetadataRoot(),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(failure)),
                });

            var rejected =
                Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                    PlatformResolver.LookupTypeInFramework(
                        "System.String",
                        "runtime@1.0.0",
                        packsDirectory));

            Assert.Equal(
                failure switch
                {
                    0 => PlatformTypeLookupFailureKind.NoMetadata,
                    1 => PlatformTypeLookupFailureKind
                        .UnsupportedMetadataFormat,
                    2 => PlatformTypeLookupFailureKind
                        .MalformedMetadataRoot,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(failure)),
                },
                rejected.Failure.Kind);
            Assert.Equal(
                failure == 2
                    ? MetadataRootMalformedReason.InvalidSignature
                    : null,
                rejected.Failure.MetadataRootReason);
        }
        finally
        {
            Directory.Delete(packsDirectory, recursive: true);
        }
    }

    [Fact]
    public void PlatformTypeLookupFailure_PreservesCompatibilityShape()
    {
        Assert.Equal(
            0,
            (int)PlatformTypeLookupFailureKind.InvalidPattern);
        Assert.Equal(
            1,
            (int)PlatformTypeLookupFailureKind.CatalogUnavailable);
        Assert.Equal(
            2,
            (int)PlatformTypeLookupFailureKind.InvalidAssembly);

        Type type = typeof(PlatformTypeLookupFailure);
        Assert.NotNull(type.GetConstructor(
            [
                typeof(PlatformTypeLookupFailureKind),
                typeof(string),
            ]));
        MethodInfo deconstruct = Assert.Single(
            type.GetMethods(),
            method => method.Name == "Deconstruct");
        Assert.Equal(2, deconstruct.GetParameters().Length);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void LookupTypeAcrossFrameworks_PrefersTypedMetadataFailure(
        int failure,
        bool typedFailureInRuntime)
    {
        string packsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"platform-format-precedence-{Guid.NewGuid():N}");
        try
        {
            string runtimePath = Path.Combine(
                packsDirectory,
                "Microsoft.NETCore.App.Ref",
                "1.0.0",
                "ref",
                "net1.0");
            string aspNetCorePath = Path.Combine(
                packsDirectory,
                "Microsoft.AspNetCore.App.Ref",
                "1.0.0",
                "ref",
                "net1.0");
            Directory.CreateDirectory(runtimePath);
            Directory.CreateDirectory(aspNetCorePath);
            File.WriteAllBytes(
                Path.Combine(runtimePath, "Runtime.dll"),
                typedFailureInRuntime
                    ? failure == 0
                        ? BuildManagedWindowsMetadata()
                        : BuildMalformedMetadataRoot()
                    : BuildNoMetadataImage());
            File.WriteAllBytes(
                Path.Combine(aspNetCorePath, "AspNetCore.dll"),
                typedFailureInRuntime
                    ? BuildNoMetadataImage()
                    : failure == 0
                        ? BuildManagedWindowsMetadata()
                        : BuildMalformedMetadataRoot());

            var rejected =
                Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                    PlatformResolver.LookupTypeAcrossFrameworks(
                        "Missing.Type",
                        packsDirectory));

            Assert.Equal(
                failure == 0
                    ? PlatformTypeLookupFailureKind
                        .UnsupportedMetadataFormat
                    : PlatformTypeLookupFailureKind
                        .MalformedMetadataRoot,
                rejected.Failure.Kind);
            Assert.Equal(
                failure == 0
                    ? null
                    : MetadataRootMalformedReason.InvalidSignature,
                rejected.Failure.MetadataRootReason);
        }
        finally
        {
            Directory.Delete(packsDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
        LookupTypeAcrossFrameworks_UnsupportedCatalogPreservesHealthyCandidate(
            bool unsupportedInRuntime)
    {
        string packsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"platform-format-neighbor-{Guid.NewGuid():N}");
        try
        {
            string runtimePath = Path.Combine(
                packsDirectory,
                "Microsoft.NETCore.App.Ref",
                "1.0.0",
                "ref",
                "net1.0");
            string aspNetCorePath = Path.Combine(
                packsDirectory,
                "Microsoft.AspNetCore.App.Ref",
                "1.0.0",
                "ref",
                "net1.0");
            Directory.CreateDirectory(runtimePath);
            Directory.CreateDirectory(aspNetCorePath);
            byte[] healthy = File.ReadAllBytes(
                typeof(PlatformResolverTests).Assembly.Location);
            File.WriteAllBytes(
                Path.Combine(runtimePath, "Runtime.dll"),
                unsupportedInRuntime
                    ? BuildManagedWindowsMetadata()
                    : healthy);
            File.WriteAllBytes(
                Path.Combine(aspNetCorePath, "AspNetCore.dll"),
                unsupportedInRuntime
                    ? healthy
                    : BuildManagedWindowsMetadata());

            var resolved =
                Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                    PlatformResolver.LookupTypeAcrossFrameworks(
                        typeof(PlatformResolverTests).FullName!,
                        packsDirectory));
            var provenance =
                Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                    resolved.Candidate.Assembly.Provenance);
            Assert.Equal(
                unsupportedInRuntime ? "aspnetcore" : "runtime",
                provenance.Framework);
        }
        finally
        {
            Directory.Delete(packsDirectory, recursive: true);
        }
    }

    [Fact]
    public void LookupType_UnqualifiedCollision_ReturnsOrderedAmbiguity()
    {
        var ambiguous = Assert.IsType<PlatformTypeLookupOutcome.Ambiguous>(
            PlatformResolver.LookupType("Enumerator"));

        Assert.True(ambiguous.Candidates.Length > 1);
        Assert.Equal(
            ambiguous.Candidates
                .OrderBy(
                    candidate => candidate.Assembly.Identity.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    candidate => candidate.Type.ToMetadataFullName(),
                    StringComparer.Ordinal)
                .ThenBy(candidate => candidate.DeclarationKind)
                .ThenBy(
                    candidate => candidate.Assembly.Path,
                    StringComparer.Ordinal),
            ambiguous.Candidates);
    }

    [Fact]
    public void LookupType_NestedSeparatorsAreEquivalent()
    {
        var dotted = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
            PlatformResolver.LookupType(
                "System.Collections.Generic.Dictionary`2.Enumerator"));
        var plus = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
            PlatformResolver.LookupType(
                "System.Collections.Generic.Dictionary`2+Enumerator"));

        Assert.Equal(dotted.Candidate.Type, plus.Candidate.Type);
        Assert.Equal(
            "System.Collections",
            plus.Candidate.Assembly.Identity.Name);
    }

    [Fact]
    public void LookupType_UnqualifiedNestedGenericUsesLeafArity()
    {
        var outcome =
            PlatformResolver.LookupType("AlternateLookup<TAlternateKey>");
        IReadOnlyList<PlatformTypeLookupCandidate> candidates = outcome switch
        {
            PlatformTypeLookupOutcome.Resolved resolved =>
                [resolved.Candidate],
            PlatformTypeLookupOutcome.Ambiguous ambiguous =>
                ambiguous.Candidates,
            _ => []
        };

        Assert.Contains(
            candidates,
            candidate => candidate.Type.ToMetadataFullName().Equals(
                "System.Collections.Concurrent.ConcurrentDictionary`2.AlternateLookup`1",
                StringComparison.Ordinal));
    }

    [Fact]
    public void LookupTypeAcrossFrameworks_ResolvesAspNetCoreNestedGenericType()
    {
        var (referencePath, _, _) =
            PlatformResolver.ResolveFramework("aspnetcore");
        Assert.SkipUnless(
            referencePath is not null,
            "ASP.NET Core reference pack is not available.");

        var resolved = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
            PlatformResolver.LookupTypeAcrossFrameworks(
                "Microsoft.AspNetCore.Components.Endpoints.FormMapping.ArrayPoolBufferAdapter<T1,T2,T3>.PooledBuffer"));

        Assert.Equal(
            "Microsoft.AspNetCore.Components.Endpoints",
            resolved.Candidate.Assembly.Identity.Name);
        Assert.Equal(
            "Microsoft.AspNetCore.Components.Endpoints.FormMapping.ArrayPoolBufferAdapter`3.PooledBuffer",
            resolved.Candidate.Type.ToMetadataFullName());

        var frameworkResolved =
            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformResolver.LookupTypeInFramework(
                    "Microsoft.AspNetCore.Components.Endpoints.FormMapping.ArrayPoolBufferAdapter<T1,T2,T3>",
                    "aspnetcore"));
        Assert.Equal(
            "Microsoft.AspNetCore.Components.Endpoints",
            frameworkResolved.Candidate.Assembly.Identity.Name);
    }

    [Fact]
    public void LookupTypeAcrossFrameworks_PreservesDistinctTypeAmbiguity()
    {
        var ambiguous = Assert.IsType<PlatformTypeLookupOutcome.Ambiguous>(
            PlatformResolver.LookupTypeAcrossFrameworks("Enumerator"));

        Assert.True(
            ambiguous.Candidates
                .Select(candidate => candidate.Type.ToMetadataFullName())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1);
    }

    [Fact]
    public void LookupTypeAcrossFrameworks_SameIdentityPrefersRuntimeContract()
    {
        var netstandard = PlatformResolver.ResolveFramework("netstandard");
        Assert.SkipUnless(
            netstandard.RefPath is not null,
            "netstandard reference pack is not available.");

        var resolved = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
            PlatformResolver.LookupTypeAcrossFrameworks("Span<T>"));

        Assert.Equal(
            "System.Span`1",
            resolved.Candidate.Type.ToMetadataFullName());
        var platform = Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
            resolved.Candidate.Assembly.Provenance);
        Assert.Equal("runtime", platform.Framework);
    }

    [Fact]
    public void LookupTypeInFramework_UsesFrameworkSpecificAssemblyOwner()
    {
        var netstandard = PlatformResolver.ResolveFramework("netstandard");
        Assert.SkipUnless(
            netstandard.RefPath is not null,
            "netstandard reference pack is not available.");

        var resolved = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
            PlatformResolver.LookupTypeInFramework(
                "System.Threading.Tasks.Task<T>",
                "netstandard"));

        Assert.NotEqual(
            "System.Runtime",
            resolved.Candidate.Assembly.Identity.Name);
        var platform = Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
            resolved.Candidate.Assembly.Provenance);
        Assert.Equal("netstandard", platform.Framework);
    }

    [Fact]
    public void LookupTypeAcrossFrameworks_NoCatalogs_ReturnsRejected()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                PlatformResolver.LookupTypeAcrossFrameworks(
                    "System.String",
                    tempDir));

            Assert.Equal(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                rejected.Failure.Kind);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LookupTypeAcrossFrameworks_IncompleteNewerVersionDoesNotShadowValidCatalog()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"platform-test-{Guid.NewGuid():N}");
        try
        {
            var runtimePack = Path.Combine(
                tempDir,
                "Microsoft.NETCore.App.Ref");
            var validPath = Path.Combine(
                runtimePack,
                "1.0.0",
                "ref",
                "net1.0");
            Directory.CreateDirectory(validPath);
            File.Copy(
                typeof(string).Assembly.Location,
                Path.Combine(validPath, "System.Private.CoreLib.dll"));
            Directory.CreateDirectory(
                Path.Combine(runtimePack, "99.0.0"));

            var framework =
                PlatformResolver.ResolveFramework("runtime", tempDir);
            Assert.NotNull(framework.RefPath);
            Assert.Equal("1.0.0", framework.Version);
            Assert.Null(framework.Error);

            var resolved = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformResolver.LookupTypeAcrossFrameworks(
                    "System.String",
                    tempDir));

            Assert.Equal(
                "System.String",
                resolved.Candidate.Type.ToMetadataFullName());
            var platform =
                Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                    resolved.Candidate.Assembly.Provenance);
            Assert.Equal("1.0.0", platform.FrameworkVersion);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LookupTypeAcrossFrameworks_PartialCatalogFailurePreservesResolvedCandidate()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"platform-test-{Guid.NewGuid():N}");
        try
        {
            var runtimePath = Path.Combine(
                tempDir,
                "Microsoft.NETCore.App.Ref",
                "1.0.0",
                "ref",
                "net1.0");
            var aspNetCorePath = Path.Combine(
                tempDir,
                "Microsoft.AspNetCore.App.Ref",
                "1.0.0",
                "ref",
                "net1.0");
            Directory.CreateDirectory(runtimePath);
            File.Copy(
                typeof(string).Assembly.Location,
                Path.Combine(runtimePath, "System.Private.CoreLib.dll"));

            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformResolver.LookupTypeAcrossFrameworks(
                    "System.String",
                    tempDir));

            Directory.CreateDirectory(aspNetCorePath);
            File.WriteAllText(
                Path.Combine(aspNetCorePath, "Invalid.dll"),
                "not an assembly");

            var resolved = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformResolver.LookupTypeAcrossFrameworks(
                    "System.String",
                    tempDir));
            Assert.Equal(
                "System.String",
                resolved.Candidate.Type.ToMetadataFullName());

            var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                PlatformResolver.LookupTypeAcrossFrameworks(
                    "Missing.Type",
                    tempDir));
            Assert.Equal(
                PlatformTypeLookupFailureKind.MalformedMetadataRoot,
                rejected.Failure.Kind);
            Assert.Equal(
                MetadataRootMalformedReason
                    .UnmappableMetadataDirectory,
                rejected.Failure.MetadataRootReason);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LookupType_EmptyPattern_ReturnsRejected()
    {
        var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
            PlatformResolver.LookupType(""));

        Assert.Equal(
            PlatformTypeLookupFailureKind.InvalidPattern,
            rejected.Failure.Kind);
    }

    [Fact]
    public void ResolveFramework_UnknownFramework_ReturnsError()
    {
        var (refPath, version, error) = PlatformResolver.ResolveFramework("unknownframework");

        Assert.Null(refPath);
        Assert.Null(version);
        Assert.NotNull(error);
        Assert.Contains("Unknown framework", error);
        Assert.Contains("unknownframework", error);
    }

    [Fact]
    public void ResolveFramework_ParsesVersionSyntax()
    {
        // This tests the framework@version parsing logic
        // Even if the path doesn't exist, we can verify the error message shows the parsed framework
        var (refPath, version, error) = PlatformResolver.ResolveFramework("runtime@9.0", "/nonexistent");

        Assert.Null(refPath);
        Assert.NotNull(error);
        // The error should indicate the framework was parsed (runtime), not that the syntax was invalid
        Assert.Contains("runtime", error);
    }

    [Fact]
    public void ResolveAssembly_AddsExtensionIfMissing()
    {
        // Test that assembly resolution adds .dll extension
        var (path, framework, version, error) = PlatformResolver.ResolveAssembly(
            "System.Runtime",
            "runtime",
            "/nonexistent/packs");

        // Should fail because path doesn't exist, but the assembly name processing happens first
        Assert.NotNull(error);
        // The key thing is it didn't crash due to missing extension
    }

    [Fact]
    public void ResolveAssembly_WithDllExtension_DoesNotDoubleAdd()
    {
        var (path, framework, version, error) = PlatformResolver.ResolveAssembly(
            "System.Runtime.dll",
            "runtime",
            "/nonexistent/packs");

        Assert.NotNull(error);
        // Should not have .dll.dll in error message
        Assert.DoesNotContain(".dll.dll", error ?? "");
    }

    [Fact]
    public void ResolveAssembly_RuntimeVersionWithoutFramework_SearchesRuntimeFamilies()
    {
        // Decouple from the host's running runtime version (#1256). Binding to
        // typeof(object).Assembly.Location asserts "the host's running runtime is a
        // discoverable shared framework" — an environment precondition, not resolver
        // behavior — and fails on preview/self-contained hosts. Probe an installed
        // shared runtime version the resolver itself can find, then assert against that.
        var (_, installedVersion, frameworkError) = PlatformResolver.ResolveRuntimeFramework("runtime");
        Assert.SkipWhen(
            installedVersion is null,
            $"No installed Microsoft.NETCore.App shared runtime found: {frameworkError}");

        var (path, framework, version, error) = PlatformResolver.ResolveAssembly(
            "System.Text.Json",
            useRuntimeAssemblies: true,
            platformVersion: installedVersion);

        Assert.Null(error);
        Assert.NotNull(path);
        Assert.Equal(installedVersion, version);
        Assert.Equal("runtime", framework);
        Assert.Contains($"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}", path);
    }

    [Fact]
    public void ResolveAssembly_NetstandardWithRuntimeFlag_ReturnsError()
    {
        // netstandard doesn't have runtime assemblies (ref-only)
        var (path, framework, version, error) = PlatformResolver.ResolveAssembly(
            "netstandard",
            "netstandard",
            packsDirectory: null,
            useRuntimeAssemblies: true);

        Assert.NotNull(error);
        Assert.Contains("netstandard", error);
        Assert.Contains("runtime", error.ToLowerInvariant());
    }

    [Fact]
    public void GetRefAssemblyPath_NonExistentPath_ReturnsNull()
    {
        var path = PlatformResolver.GetRefAssemblyPath("/nonexistent/path", "9.0.0");

        Assert.Null(path);
    }

    [Fact]
    public void GetAssemblies_NonExistentPath_ReturnsEmpty()
    {
        var assemblies = PlatformResolver.GetAssemblies("/nonexistent/path");

        Assert.Empty(assemblies);
    }

    [Fact]
    public void GetAssemblies_ReturnsOrderedList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "Zebra.dll"), "");
            File.WriteAllText(Path.Combine(tempDir, "Alpha.dll"), "");
            File.WriteAllText(Path.Combine(tempDir, "Middle.dll"), "");

            var assemblies = PlatformResolver.GetAssemblies(tempDir);

            Assert.Equal(3, assemblies.Count);
            Assert.Equal("Alpha.dll", assemblies[0].Name);
            Assert.Equal("Middle.dll", assemblies[1].Name);
            Assert.Equal("Zebra.dll", assemblies[2].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies GetPacksDirectory returns an existing packs directory: either the app cache
    /// packs category (preferred, versioned as <c>packs-v*</c>) or an SDK-installed
    /// <c>packs</c> directory. Skips, rather than silently passing, when neither exists.
    /// </summary>
    [Fact]
    public void GetPacksDirectory_ReturnsValidPath()
    {
        var result = PlatformResolver.GetPacksDirectory();

        Assert.SkipWhen(
            result is null,
            "No packs directory exists on this machine: the app cache packs category is absent and no SDK-installed packs directory was found.");

        Assert.True(Directory.Exists(result), $"Packs directory should exist: {result}");

        // The app cache category is versioned (packs-v2), so a bare "packs" suffix only
        // describes the SDK-installed branch.
        var name = Path.GetFileName(result!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var isAppCachePacks = name.StartsWith(PlatformPackService.PacksCategoryPrefix, StringComparison.Ordinal);
        Assert.True(
            isAppCachePacks || name == "packs",
            $"Expected the app cache packs category ('{PlatformPackService.PacksCategoryPrefix}*') or an SDK-installed 'packs' directory, but got: {result}");
    }

    /// <summary>
    /// Pins the invariant that makes GetPacksDirectory's suffix contract two-shaped: the app cache
    /// packs category is versioned and never the bare "packs" name used by SDK installs. If this
    /// ever changes, GetPacksDirectory_ReturnsValidPath's suffix check must be revisited.
    /// </summary>
    [Fact]
    public void PacksCacheCategory_IsVersioned_AndNotBarePacks()
    {
        Assert.StartsWith(
            PlatformPackService.PacksCategoryPrefix,
            PlatformPackService.PacksCategory,
            StringComparison.Ordinal);
        Assert.NotEqual("packs", PlatformPackService.PacksCategory);
    }

    /// <summary>
    /// Scans all assemblies in installed ref packs and verifies every one passes IsPlatformCandidate.
    /// If this test fails, a new platform assembly name needs to be added to the candidate check.
    /// This caught WindowsBase, mscorlib, netstandard, and System (bare) as special cases.
    /// </summary>
    [Fact]
    public void IsPlatformCandidate_CoversAllRefPackAssemblies()
    {
        // Check installed packs on disk
        var packsDir = FindAnyRefPacksDirectory();
        if (packsDir == null)
        {
            return; // No packs available to scan
        }

        // Only scan the 3 known ref packs (runtime, aspnetcore, netstandard)
        List<string> dllFiles = [];
        foreach (var packName in PlatformResolver.FrameworkMappings.Values)
        {
            var packPath = Path.Combine(packsDir, packName);
            if (!Directory.Exists(packPath))
                continue;

            var files = Directory.GetFiles(packPath, "*.dll", SearchOption.AllDirectories)
                .Where(f => f.Contains(Path.Combine("ref", "net")))
                .Select(f => Path.GetFileNameWithoutExtension(f));
            dllFiles.AddRange(files);
        }

        dllFiles = dllFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (dllFiles.Count == 0)
        {
            return; // No ref assemblies found
        }

        List<string> uncovered = [];
        foreach (var name in dllFiles)
        {
            if (!PlatformResolver.IsPlatformCandidate(name))
            {
                uncovered.Add(name);
            }
        }

        Assert.True(uncovered.Count == 0,
            $"Platform assemblies not covered by IsPlatformCandidate: {string.Join(", ", uncovered)}. " +
            $"Add these names to PlatformResolver.IsPlatformCandidate().");
    }

    /// <summary>
    /// Finds any available ref packs directory (app cache or installed SDK).
    /// Only returns directories for the 3 known ref packs (runtime, aspnetcore, netstandard)
    /// to avoid picking up extra SDK packs (Android, WPF, etc.) that IsPlatformCandidate
    /// is not intended to cover.
    /// </summary>
    private static string? FindAnyRefPacksDirectory()
    {
        // Try app cache first
        var appCache = PlatformPackService.GetPacksCachePath();
        if (appCache != null && Directory.Exists(appCache))
            return appCache;

        // Fall back to installed SDK — but only scan known ref pack subdirectories
        string[] roots = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? [@"C:\Program Files\dotnet\packs"]
            : ["/usr/lib/dotnet/packs", "/usr/share/dotnet/packs", "/usr/local/share/dotnet/packs"];

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            // Check that at least one known ref pack exists
            foreach (var packName in PlatformResolver.FrameworkMappings.Values)
            {
                if (Directory.Exists(Path.Combine(root, packName)))
                    return root;
            }
        }

        return null;
    }

    /// <summary>
    /// Verifies DOTNET_ROOT is checked FIRST for shared directory as well.
    /// </summary>
    [Fact]
    public void GetSharedDirectory_WhenDotnetRootSet_ChecksItFirst()
    {
        var tempDotnetRoot = Path.Combine(Path.GetTempPath(), $"dotnet-root-test-{Guid.NewGuid():N}");
        var sharedDir = Path.Combine(tempDotnetRoot, "shared");
        var runtimeDir = Path.Combine(sharedDir, "Microsoft.NETCore.App", "9.0.0");

        var originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Directory.CreateDirectory(runtimeDir);

            Environment.SetEnvironmentVariable("DOTNET_ROOT", tempDotnetRoot);

            var result = PlatformResolver.GetSharedDirectory();

            Assert.NotNull(result);
            Assert.Equal(sharedDir, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalDotnetRoot);
            if (Directory.Exists(tempDotnetRoot))
                Directory.Delete(tempDotnetRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that when DOTNET_ROOT is not set, the resolver falls back to standard paths.
    /// </summary>
    [Fact]
    public void GetPacksDirectory_WhenDotnetRootNotSet_UsesStandardPaths()
    {
        var originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            // Clear DOTNET_ROOT
            Environment.SetEnvironmentVariable("DOTNET_ROOT", null);

            // GetPacksDirectory should still find the system installation
            var result = PlatformResolver.GetPacksDirectory();

            // On a system with .NET installed, this should return a valid path
            // We can't assert a specific path since it varies by OS, but we can verify
            // it either returns null (no .NET) or a path that exists
            if (result != null)
            {
                Assert.True(Directory.Exists(result), $"Returned packs directory should exist: {result}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalDotnetRoot);
        }
    }

    [Fact]
    public void GetSharedDirectory_WhenDotnetRootNotSet_UsesCurrentRuntime()
    {
        var originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", null);

            var result = PlatformResolver.GetSharedDirectory();

            Assert.NotNull(result);
            Assert.True(Directory.Exists(result), $"Returned shared directory should exist: {result}");
            Assert.True(Directory.Exists(Path.Combine(result!, "Microsoft.NETCore.App")),
                $"Returned shared directory should contain Microsoft.NETCore.App: {result}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalDotnetRoot);
        }
    }

    /// <summary>
    /// Tests that GetInstalledFrameworks works with a custom packs directory.
    /// </summary>
    [Fact]
    public void GetInstalledFrameworks_WithCustomPacksDir_ReturnsFrameworks()
    {
        var tempPacksDir = Path.Combine(Path.GetTempPath(), $"packs-test-{Guid.NewGuid():N}");
        try
        {
            // Create a minimal runtime ref pack structure
            var runtimeRef = Path.Combine(tempPacksDir, "Microsoft.NETCore.App.Ref", "9.0.0", "ref", "net9.0");
            Directory.CreateDirectory(runtimeRef);
            File.WriteAllText(Path.Combine(runtimeRef, "System.Runtime.dll"), "");
            File.WriteAllText(Path.Combine(runtimeRef, "System.Console.dll"), "");

            var frameworks = PlatformResolver.GetInstalledFrameworks(tempPacksDir);

            Assert.Single(frameworks);
            Assert.Equal("runtime", frameworks[0].ShortName);
            Assert.Equal("Microsoft.NETCore.App.Ref", frameworks[0].RefPackName);
            Assert.Equal("9.0.0", frameworks[0].LatestVersion);
            Assert.Equal(2, frameworks[0].AssemblyCount);
        }
        finally
        {
            if (Directory.Exists(tempPacksDir))
                Directory.Delete(tempPacksDir, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledFrameworks_DefaultDiscoveryRefreshesAfterCoreCacheRootChanges()
    {
        const string SyntheticVersion = "999.0.0";
        string temporaryCache = Directory.CreateTempSubdirectory(
            "dotnet-inspect-platform-cache-").FullName;
        try
        {
            CoreCache.Initialize("dotnet-inspect-test", temporaryCache);
            var (baselineRefPath, _, baselineError) =
                PlatformResolver.ResolveFramework("runtime");
            if (baselineRefPath is null)
            {
                Assert.Skip(
                    $"Runtime reference pack not available: {baselineError}");
                return;
            }

            string packsDirectory =
                Assert.IsType<string>(PlatformPackService.GetPacksCachePath());
            string syntheticRefPath = Path.Combine(
                packsDirectory,
                "Microsoft.NETCore.App.Ref",
                SyntheticVersion,
                "ref",
                "net999.0");
            Directory.CreateDirectory(syntheticRefPath);
            File.Copy(
                typeof(PlatformResolverTests).Assembly.Location,
                Path.Combine(syntheticRefPath, "System.Runtime.dll"));

            FrameworkInfo runtime = Assert.Single(
                PlatformResolver.GetInstalledFrameworks(),
                framework => framework.ShortName == "runtime");
            Assert.Equal(SyntheticVersion, runtime.LatestVersion);
            Assert.Equal(
                Path.Combine(
                    packsDirectory,
                    "Microsoft.NETCore.App.Ref"),
                runtime.Path);
        }
        finally
        {
            CoreCache.Initialize("dotnet-inspect-test");
            Directory.Delete(temporaryCache, recursive: true);
        }

        var resolved = Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
            PlatformResolver.LookupType("System.String"));
        Assert.NotNull(resolved.Candidate.Assembly.Path);
        Assert.True(File.Exists(resolved.Candidate.Assembly.Path));
        Assert.False(
            resolved.Candidate.Assembly.Path.StartsWith(
                temporaryCache,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssemblyDependencyResolver_InstalledFallbackUsesOneFrameworkSnapshotPerResolver()
    {
        string? originalDotnetRoot =
            Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string temporaryRoot = Directory.CreateTempSubdirectory(
            "dotnet-inspect-platform-snapshot-").FullName;
        try
        {
            CoreCache.Initialize(
                "dotnet-inspect-test",
                Path.Combine(temporaryRoot, "cache"));
            var (baselineRefPath, _, baselineError) =
                PlatformResolver.ResolveFramework("runtime");
            if (baselineRefPath is null)
            {
                Assert.Skip(
                    $"Runtime reference pack not available: {baselineError}");
                return;
            }

            string runtimeSource = Path.Combine(
                baselineRefPath,
                "System.Runtime.dll");
            string consoleSource = Path.Combine(
                baselineRefPath,
                "System.Console.dll");
            string coreLibrarySource = typeof(object).Assembly.Location;
            if (!File.Exists(runtimeSource)
                || !File.Exists(consoleSource)
                || !File.Exists(coreLibrarySource))
            {
                Assert.Skip(
                    $"Runtime installation is incomplete: {baselineRefPath}");
                return;
            }

            string packsDirectory =
                Assert.IsType<string>(PlatformPackService.GetPacksCachePath());
            string firstRefPath = Path.Combine(
                packsDirectory,
                "Microsoft.NETCore.App.Ref",
                "999.0.0",
                "ref",
                "net999.0");
            Directory.CreateDirectory(firstRefPath);
            File.Copy(
                runtimeSource,
                Path.Combine(firstRefPath, "System.Runtime.dll"));
            File.Copy(
                consoleSource,
                Path.Combine(firstRefPath, "System.Console.dll"));

            string firstDotnetRoot = Path.Combine(temporaryRoot, "first");
            string firstRuntimePath = Path.Combine(
                firstDotnetRoot,
                "shared",
                "Microsoft.NETCore.App",
                "998.0.0");
            Directory.CreateDirectory(firstRuntimePath);
            File.Copy(
                runtimeSource,
                Path.Combine(firstRuntimePath, "System.Runtime.dll"));
            File.Copy(
                consoleSource,
                Path.Combine(firstRuntimePath, "System.Console.dll"));
            File.Copy(
                coreLibrarySource,
                Path.Combine(firstRuntimePath, "System.Private.CoreLib.dll"));
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                firstDotnetRoot);

            AssemblyDependencyResolver resolver = CreateResolver();
            Assert.Equal(
                Path.Combine(firstRefPath, "System.Runtime.dll"),
                Select(resolver, "System.Runtime").Assembly.Path);

            string secondRefPath = Path.Combine(
                packsDirectory,
                "Microsoft.NETCore.App.Ref",
                "1000.0.0",
                "ref",
                "net1000.0");
            Directory.CreateDirectory(secondRefPath);
            File.Copy(
                runtimeSource,
                Path.Combine(secondRefPath, "System.Runtime.dll"));

            string secondDotnetRoot = Path.Combine(temporaryRoot, "second");
            string secondRuntimePath = Path.Combine(
                secondDotnetRoot,
                "shared",
                "Microsoft.NETCore.App",
                "999.5.0");
            Directory.CreateDirectory(secondRuntimePath);
            File.Copy(
                consoleSource,
                Path.Combine(secondRuntimePath, "System.Console.dll"));
            File.Copy(
                coreLibrarySource,
                Path.Combine(secondRuntimePath, "System.Private.CoreLib.dll"));
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                secondDotnetRoot);

            Assert.Equal(
                Path.Combine(firstRefPath, "System.Console.dll"),
                Select(resolver, "System.Console").Assembly.Path);
            Assert.Equal(
                Path.Combine(
                    firstRuntimePath,
                    "System.Private.CoreLib.dll"),
                Select(resolver, "System.Private.CoreLib").Assembly.Path);

            AssemblyDependencyResolver refreshedResolver = CreateResolver();
            Assert.Equal(
                Path.Combine(secondRefPath, "System.Runtime.dll"),
                Select(refreshedResolver, "System.Runtime").Assembly.Path);
            Assert.Equal(
                Path.Combine(
                    secondRuntimePath,
                    "System.Private.CoreLib.dll"),
                Select(
                    refreshedResolver,
                    "System.Private.CoreLib").Assembly.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                originalDotnetRoot);
            CoreCache.Initialize("dotnet-inspect-test");
            Directory.Delete(temporaryRoot, recursive: true);
        }

        AssemblyDependencyResolver CreateResolver() =>
            new(
                new AssemblyDependencyResolutionOptions(
                    typeof(PlatformResolverTests).Assembly.Location)
                {
                    PackageRoots = [],
                    IncludeSiblingAssemblies = false,
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                    IncludeInstalledPlatformFallback = true,
                    IgnoreAssemblyVersion = true,
                });

        static AssemblyBindingSelection.Selected Select(
            AssemblyDependencyResolver resolver,
            string assemblyName) =>
            Assert.IsType<AssemblyBindingSelection.Selected>(
                resolver.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(
                            new AssemblyReferenceIdentity(
                                assemblyName,
                                Version: null,
                                Culture: null,
                                PublicKeyToken: null)),
                        AssemblyBindingOrigin.Global(),
                        AssemblyResolutionScope.Any)));
    }

    /// <summary>
    /// Tests that multiple framework versions are detected and sorted correctly.
    /// </summary>
    [Fact]
    public void GetInstalledFrameworks_MultipleVersions_SortsDescending()
    {
        var tempPacksDir = Path.Combine(Path.GetTempPath(), $"packs-test-{Guid.NewGuid():N}");
        try
        {
            // Create multiple versions
            foreach (var version in new[] { "8.0.0", "9.0.0", "10.0.0-preview.1" })
            {
                var refDir = Path.Combine(tempPacksDir, "Microsoft.NETCore.App.Ref", version, "ref", $"net{version.Split('.')[0]}.0");
                Directory.CreateDirectory(refDir);
                File.WriteAllText(Path.Combine(refDir, "System.Runtime.dll"), "");
            }

            var frameworks = PlatformResolver.GetInstalledFrameworks(tempPacksDir);

            Assert.Single(frameworks);
            var runtime = frameworks[0];
            Assert.Equal(3, runtime.AllVersions.Count);
            Assert.Equal("10.0.0-preview.1", runtime.AllVersions[0]); // Latest first
            Assert.Equal("9.0.0", runtime.AllVersions[1]);
            Assert.Equal("8.0.0", runtime.AllVersions[2]);
            Assert.Equal("10.0.0-preview.1", runtime.LatestVersion);
        }
        finally
        {
            if (Directory.Exists(tempPacksDir))
                Directory.Delete(tempPacksDir, recursive: true);
        }
    }

    static byte[] BuildManagedWindowsMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildMalformedMetadataRoot()
    {
        byte[] image = File.ReadAllBytes(
            typeof(PlatformResolverTests).Assembly.Location);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        BinaryPrimitives.WriteUInt32LittleEndian(
            image.AsSpan(
                peReader.PEHeaders.MetadataStartOffset,
                sizeof(uint)),
            0);
        return image;
    }

    static byte[] BuildNoMetadataImage()
    {
        byte[] image = File.ReadAllBytes(
            typeof(PlatformResolverTests).Assembly.Location);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        return image;
    }

    [Theory]
    [InlineData("System.Text.Json", "runtime")]
    [InlineData("System.Runtime", "runtime")]
    [InlineData("Microsoft.CSharp", "runtime")]
    [InlineData("Microsoft.Win32.Registry", "runtime")]
    [InlineData("Microsoft.VisualBasic", "runtime")]
    [InlineData("Microsoft.AspNetCore.Http", "aspnetcore")]
    [InlineData("Microsoft.Extensions.Logging", "aspnetcore")]
    [InlineData("Microsoft.JSInterop.Something", "aspnetcore")]
    [InlineData("Microsoft.Net.Http.Headers", "aspnetcore")]
    public void GetBiasedPack_ReturnsCorrectPack(string assemblyName, string expected)
    {
        Assert.Equal(expected, PlatformPackService.GetBiasedPack(assemblyName));
    }

    [Theory]
    [InlineData("mscorlib")]
    [InlineData("netstandard")]
    [InlineData("WindowsBase")]
    [InlineData("Newtonsoft.Json")]
    public void GetBiasedPack_ReturnsNull_ForNonBiasedNames(string assemblyName)
    {
        Assert.Null(PlatformPackService.GetBiasedPack(assemblyName));
    }
}
