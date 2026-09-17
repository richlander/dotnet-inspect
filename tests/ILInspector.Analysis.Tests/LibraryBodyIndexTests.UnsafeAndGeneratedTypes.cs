using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    [Fact]
    public void Open_DoesNotKeepAssemblyFileLocked()
    {
        string path = Path.Combine(Path.GetTempPath(), $"analysis-lock-{Guid.NewGuid():N}.dll");
        File.Copy(typeof(CallSiteFixtures).Assembly.Location, path);
        try
        {
            var index = LibraryBodyIndex.Open(path);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.NotEmpty(index.Methods);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnsafeEvidence_FindsSignatureOperationsAndUnsafeCalls()
    {
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence.Reason == "Unsafe signature"
            && evidence.Detail.Contains("int*", StringComparison.Ordinal));
        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence is { Reason: "Unsafe operation", Detail: "ldind.i4", Kind: "opcode", ILOffset: not null });
        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)
            && evidence.Reason == "Unsafe call"
            && evidence.Detail.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", StringComparison.Ordinal)
            && evidence.OperandToken is not null);
        Assert.DoesNotContain(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.PInvokeOnly));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void UnsafeEvidence_ClassifiesMembersDeclaredOnUnsafeApi()
    {
        var index = LibraryBodyIndex.Open(typeof(Unsafe).Assembly.Location);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.DeclaringType is { Namespace: "System.Runtime.CompilerServices", Name: "Unsafe" }
            && evidence.Member.Name == "Add"
            && evidence is { Reason: "Unsafe API member", Kind: "api" });
    }

    [Fact]
    public void CallerUnsafeMode_PointerSignatureIsImplicitWhenModuleNotOptedIn()
    {
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        Assert.False(index.MemorySafetyRulesEnabled);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.MemorySafetyRules);
        Assert.Equal(MemorySafetyRulesState.Legacy, rules.State);
        Assert.Equal(0, index.UnsafeModes.Explicit);
        Assert.Equal(0, index.UnsafeModes.Unavailable);

        var pointerRead = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)));
        Assert.Equal(CallerUnsafeMode.Implicit, pointerRead.CallerUnsafeMode);
    }

    [Fact]
    public void CallerUnsafeMode_UsesNormalizedUpdatedContracts()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());

        Assert.True(index.MemorySafetyRulesEnabled);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.MemorySafetyRules);
        Assert.Equal(MemorySafetyRulesState.Updated, rules.State);
        Assert.NotEqual(0, index.UnsafeModes.Explicit);
        Assert.DoesNotContain(
            index.DeclaredMethods,
            method => method.CallerUnsafeMode == CallerUnsafeMode.Implicit);

        MethodIdentity pointerOnly = Assert.Single(
            index.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && method.Name == "PointerNoneMethod");
        Assert.Equal(
            CallerUnsafeMode.None,
            pointerOnly.CallerUnsafeMode);

        MethodIdentity pointerFreeUnsafe = Assert.Single(
            index.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && method.Name == "PointerFreeUnsafeMethod");
        Assert.Equal(
            CallerUnsafeMode.Explicit,
            pointerFreeUnsafe.CallerUnsafeMode);

        MethodIdentity constructor = Assert.Single(
            index.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && method.Name == ".ctor");
        Assert.Equal(
            CallerUnsafeMode.Explicit,
            constructor.CallerUnsafeMode);

        foreach (string accessorName in
            new[] { "get_Property", "add_Changed", "remove_Changed" })
        {
            MethodIdentity accessor = Assert.Single(
                index.DeclaredMethods,
                method =>
                    method.DeclaringType.Name
                        == "AccessorContractFixtures"
                    && method.Name == accessorName);
            Assert.Equal(
                CallerUnsafeMode.Explicit,
                accessor.CallerUnsafeMode);
        }

        MethodIdentity safeExtern = Assert.Single(
            index.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && method.Name == "SafeExtern");
        Assert.Equal(
            CallerUnsafeMode.None,
            safeExtern.CallerUnsafeMode);
    }

    [Theory]
    [InlineData(99, MemorySafetyRulesState.Unsupported)]
    [InlineData(null, MemorySafetyRulesState.Malformed)]
    public void
        CallerUnsafeMode_InvalidMarkerUsesNormalizedCompatibilityContract(
            int? marker,
            MemorySafetyRulesState expectedRules)
    {
        LibraryBodyIndex index =
            OpenMemorySafetyContractImage(marker);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.MemorySafetyRules);
        Assert.Equal(expectedRules, rules.State);
        Assert.False(index.MemorySafetyRulesEnabled);

        MethodIdentity pointerOnly = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == "PointerOnly");
        Assert.Equal(
            CallerUnsafeMode.Implicit,
            pointerOnly.CallerUnsafeMode);

        MethodIdentity attributeOnly = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == "AttributeOnly");
        Assert.Equal(
            CallerUnsafeMode.None,
            attributeOnly.CallerUnsafeMode);
    }

    [Fact]
    public void CallerUnsafeMode_UnavailableContractIsNotAPropagator()
    {
        LibraryBodyIndex index =
            OpenMemorySafetyContractImage(2, 1);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.MemorySafetyRules);
        Assert.Equal(
            MemorySafetyRulesState.Conflicting,
            rules.State);
        Assert.False(index.MemorySafetyRulesEnabled);

        Assert.All(
            index.DeclaredMethods,
            method => Assert.Equal(
                CallerUnsafeMode.Unavailable,
                method.CallerUnsafeMode));
        Assert.Equal(
            index.DeclaredMethods.Length,
            index.UnsafeModes.Unavailable);
        Assert.Equal(0, index.UnsafeModes.Unsafe);

        Assert.Empty(index.TopUnsafeLeverage(1));
        Assert.Empty(index.OpaqueUnsafeMethods());
        Assert.Empty(index.HollowUnsafeMethods());
    }

    [Fact]
    public void CallerUnsafeMode_CallingUnsafeApiIsNotRequiresUnsafe()
    {
        // The authoritative model is RequiresUnsafeAttribute || pointer signature.
        // Calling an unsafe API does not itself make a method requires-unsafe —
        // that is the heuristic's domain, deliberately excluded from the model.
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var callsUnsafeAs = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)));
        Assert.Equal(CallerUnsafeMode.None, callsUnsafeAs.CallerUnsafeMode);
    }

    [Fact]
    public void GeneratedFrameworkTypes_DetectsGrpcStub_AndRejectsUnauthenticProtobufSpoof()
    {
        var index = LibraryBodyIndex.Open(typeof(FakeProtobufReflection).Assembly.Location);
        var generated = index.GeneratedFrameworkTypes;

        // #1735: the bootstrap types are bound from an unsigned assembly literally named
        // Google.Protobuf (no real public-key-token), so these must NOT be classified as
        // protobuf-generated — otherwise a same-name spoof could suppress actionable product
        // rows. (Authentic Google.Protobuf identity is exercised by the unit test below.)
        Assert.DoesNotContain(generated, type => SameClrType(type, typeof(FakeProtobufReflection)));
        Assert.DoesNotContain(generated, type => SameClrType(type, typeof(FakeProtobufMessage)));
        // gRPC detection is identity-independent (namespace + generated __* members tied to a
        // Grpc.Core call), so the gRPC service stub is still correctly detected.
        Assert.Contains(generated, type => SameClrType(type, typeof(FakeGrpcServiceStub)));
        // A normal protobuf-using type that doesn't bootstrap generated infrastructure stays out.
        Assert.DoesNotContain(generated, type => SameClrType(type, typeof(NormalProtobufConsumer)));
        // Hand-written gRPC registration calling ServerServiceDefinition.CreateBuilder (without
        // generated __* members) must not be flagged — gRPC calls alone are not a signal.
        Assert.DoesNotContain(generated, type => SameClrType(type, typeof(HandWrittenGrpcRegistration)));
        // A user type with a generated-looking __Helper_* member but no Grpc.Core call must not
        // be flagged — a generated member name alone is not enough (#1574).
        Assert.DoesNotContain(generated, type => SameClrType(type, typeof(GeneratedLookalike)));
        // Because it is not classified as generated, its optimization opportunity stays visible
        // (Performance Triage suppresses opportunities only for generated-framework types).
        Assert.Contains(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == nameof(GeneratedLookalike)
            && opportunity.Method.Name == nameof(GeneratedLookalike.MakesLocalArrayUnsuppressed));
    }

    // #1735: generated-code suppression must authenticate Google.Protobuf by public-key-token,
    // not simple name. The unsigned fixture assembly named Google.Protobuf is NOT authentic; the
    // real strong-named package assembly IS.
    [Fact]
    public void GoogleProtobufIdentity_AuthenticatesByPublicKeyToken()
    {
        using (var pe = new System.Reflection.PortableExecutable.PEReader(System.IO.File.OpenRead(FixtureCatalog.AnalysisProtobuf.AssemblyPath())))
            Assert.False(FrameworkAssemblyKeys.IsAuthenticProtobufDefinition(pe.GetMetadataReader()),
                "an unsigned same-name assembly must not be treated as the real Google.Protobuf");

        var realProtobuf = FindRealGoogleProtobufAssembly();
        if (realProtobuf is null)
            return; // the Google.Protobuf package is not restored in this environment

        using var realPe = new System.Reflection.PortableExecutable.PEReader(System.IO.File.OpenRead(realProtobuf));
        Assert.True(FrameworkAssemblyKeys.IsAuthenticProtobufDefinition(realPe.GetMetadataReader()),
            "the real strong-named Google.Protobuf must be authentic");
    }

    [Fact]
    public void GeneratedFrameworkTypes_IgnoresUserDefinedProtobufLookalikes()
    {
        // The lookalike fixture assembly is NOT named Google.Protobuf; it declares its own
        // Google.Protobuf.* bootstrap-shaped types and calls them from product code. The
        // protobuf generated-bootstrap predicates require real Google.Protobuf assembly
        // identity, so the calling product type must not be classified as generated (#1580).
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisLookalike.AssemblyPath());
        var generated = index.GeneratedFrameworkTypes;

        Assert.DoesNotContain(
            generated,
            type => type.Name == "ProtobufBootstrapLookalike");

        // Because it is not classified as generated, its optimization opportunity stays
        // visible — Performance Triage suppresses opportunities only for generated types.
        Assert.Contains(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == "ProtobufBootstrapLookalike"
            && opportunity.Method.Name == "ShouldStillBeActionable");
    }

    [Fact]
    public void GeneratedFrameworkTypes_DistinguishesNamespaceFromNestedDisplayCollision()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-framework-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "DisplayCollision.dll");
        try
        {
            File.WriteAllBytes(path, EmitDisplayNameCollisionAssembly());
            var index = LibraryBodyIndex.Open(path);
            var generated = index.GeneratedFrameworkTypes;

            TypeRef namespaceLeaf = index.Methods
                .First(method => method.DeclaringType.Namespace == "CollisionNs.A"
                    && method.DeclaringType.Name == "GeneratedLeaf")
                .DeclaringType;
            TypeRef nestedLeaf = index.Methods
                .First(method => method.DeclaringType.Namespace == "CollisionNs"
                    && method.DeclaringType.Name == "A+GeneratedLeaf")
                .DeclaringType;

            Assert.Equal(
                namespaceLeaf.ToQualifiedDisplayString(),
                nestedLeaf.ToQualifiedDisplayString());
            Assert.Contains(generated, type => type.Equals(namespaceLeaf));
            Assert.DoesNotContain(generated, type => type.Equals(nestedLeaf));
            Assert.True(index.IsGeneratedFrameworkType(namespaceLeaf));
            Assert.False(index.IsGeneratedFrameworkType(nestedLeaf));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GeneratedFrameworkTypes_DistinguishesNestedGeneratedFromNamespaceLookalike()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-framework-identity-rev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "DisplayCollision.dll");
        try
        {
            File.WriteAllBytes(path, EmitDisplayNameCollisionAssembly());
            var index = LibraryBodyIndex.Open(path);
            var generated = index.GeneratedFrameworkTypes;

            TypeRef nestedGenerated = index.Methods
                .First(method => method.DeclaringType.Namespace == "ReverseNs"
                    && method.DeclaringType.Name == "A+NestedLeaf")
                .DeclaringType;
            TypeRef namespaceLookalike = index.Methods
                .First(method => method.DeclaringType.Namespace == "ReverseNs.A"
                    && method.DeclaringType.Name == "NestedLeaf")
                .DeclaringType;

            Assert.Equal(
                nestedGenerated.ToQualifiedDisplayString(),
                namespaceLookalike.ToQualifiedDisplayString());
            Assert.Contains(generated, type => type.Equals(nestedGenerated));
            Assert.DoesNotContain(generated, type => type.Equals(namespaceLookalike));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GeneratedFrameworkTypes_NestedCompilerGeneratedWalksMetadataParentsNotDisplay()
    {
        static TypeRef Exact(string @namespace, params string[] segments)
        {
            MetadataTypeDefinitionName name =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        @namespace,
                        [.. segments]))
                .Name;
            return TypeRef.Definition(
                "Asm",
                @namespace,
                string.Join('+', segments),
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.CurrentAssembly(),
                    name));
        }

        TypeRef generated =
            Exact("CollisionNs.A", "GeneratedLeaf");
        TypeRef generatedNested =
            Exact("CollisionNs.A", "GeneratedLeaf", "<>c");
        TypeRef lookalikeNested =
            Exact("CollisionNs", "A", "GeneratedLeaf", "<>c");

        Assert.Equal(
            generatedNested.ToQualifiedDisplayString(),
            lookalikeNested.ToQualifiedDisplayString());

        var set = new HashSet<TypeRef> { generated };
        Assert.True(LibraryBodyIndex.IsGeneratedFrameworkType(set, generatedNested));
        Assert.False(LibraryBodyIndex.IsGeneratedFrameworkType(set, lookalikeNested));
    }

    [Fact]
    public void GeneratedFrameworkTypes_DoesNotTreatLiteralPlusNameAsNested()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-literal-plus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "LiteralPlus.dll");
        try
        {
            File.WriteAllBytes(path, EmitLiteralPlusVsNestedAssembly());
            var index = LibraryBodyIndex.Open(path);

            TypeRef stub = index.Methods
                .First(method => method.DeclaringType.Namespace == "Ns"
                    && method.DeclaringType.Name == "GenStub")
                .DeclaringType;
            TypeRef nested = index.Methods
                .First(method => method.Name == "InnerMethod")
                .DeclaringType;
            TypeRef literalPlus = index.Methods
                .First(method => method.Name == "UnrelatedUserMethod")
                .DeclaringType;

            Assert.Equal(["GenStub"], stub.Resolution!.Type.Segments);
            Assert.Equal(["GenStub", "Inner"], nested.Resolution!.Type.Segments);
            Assert.Equal(["GenStub+LiteralPlus"], literalPlus.Resolution!.Type.Segments);

            Assert.Contains(index.GeneratedFrameworkTypes, type => type.Equals(stub));
            Assert.True(index.IsGeneratedFrameworkType(nested));
            Assert.False(index.IsGeneratedFrameworkType(literalPlus));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OptimizationOpportunities_SuppressesSourceGeneratedTypes()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A [GeneratedCode] type (source generators: JSON/regex/etc.) is not an actionable
        // source-shape target, so none of its methods produce optimization opportunities,
        // even though MakesSmallArray would otherwise be a small-array row (#1273).
        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == nameof(SourceGeneratedOptimizationFixtures));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    .Contains(
                        nameof(
                            SourceGeneratedClassicAsyncOuter.Nested),
                        StringComparison.Ordinal));
    }

    [Fact]
    public void AsyncSiblingFriendAccess_StrongNamedGrantorRequiresFullFriendKey()
    {
        using var strongGrantor = new PEReader(
            new MemoryStream(
                EmitAssemblyIdentity(
                    "StrongGrantor",
                    [1, 2, 3, 4])));
        using var unsignedSource = new PEReader(
            new MemoryStream(
                EmitAssemblyIdentity("UnsignedFriend", [])));

        Assert.False(
            LibraryBodyAsyncSiblingAccessibilityAnalyzer
                .FriendIdentityGrantsAccess(
                strongGrantor.GetMetadataReader(),
                unsignedSource.GetMetadataReader(),
                new AssemblyName("UnsignedFriend"),
                "UnsignedFriend"));
    }
}
