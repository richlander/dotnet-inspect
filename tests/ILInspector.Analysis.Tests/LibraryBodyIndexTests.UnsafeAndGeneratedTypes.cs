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
            var index = BodyAnalysisTestExecution.Open(path);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.NotEmpty(index.CallGraph.Methods);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnsafeEvidence_FindsSignatureOperationsAndUnsafeCalls()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        Assert.Contains(index.Safety.Evidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence.Reason == "Unsafe signature"
            && evidence.Detail.Contains("int*", StringComparison.Ordinal));
        Assert.Contains(index.Safety.Evidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence is { Reason: "Unsafe operation", Detail: "ldind.i4", Kind: "opcode", ILOffset: not null });
        Assert.Contains(index.Safety.Evidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)
            && evidence.Reason == "Unsafe call"
            && evidence.Detail.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", StringComparison.Ordinal)
            && evidence.OperandToken is not null);
        Assert.DoesNotContain(
            index.Safety.MemberUses,
            use => use.Method.Name
                == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs));
        Assert.DoesNotContain(index.Safety.Evidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.PInvokeOnly));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void UnsafeEvidence_ClassifiesMembersDeclaredOnUnsafeApi()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(Unsafe).Assembly.Location);

        Assert.Contains(index.Safety.Evidence, evidence =>
            evidence.Member.DeclaringType is { Namespace: "System.Runtime.CompilerServices", Name: "Unsafe" }
            && evidence.Member.Name == "Add"
            && evidence is { Reason: "Unsafe API member", Kind: "api" });
    }

    [Fact]
    public void CallerUnsafeMode_PointerSignatureIsImplicitWhenModuleNotOptedIn()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.Safety.MemorySafetyRules);
        Assert.Equal(MemorySafetyRulesState.Legacy, rules.State);
        Assert.NotEqual(0, index.Safety.UnsafeModes.Implicit);
        Assert.Equal(0, index.Safety.UnsafeModes.Explicit);
        Assert.Equal(0, index.Safety.UnsafeModes.Unavailable);
        Assert.Same(index.Receipt, index.Safety.Receipt);

        var pointerRead = Assert.Single(index.CallGraph.Methods.Where(m =>
            m.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)));
        Assert.Equal(CallerUnsafeMode.Implicit, pointerRead.CallerUnsafeMode);
    }

    [Fact]
    public void CallerUnsafeMode_UsesNormalizedUpdatedContracts()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());

        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.Safety.MemorySafetyRules);
        Assert.Equal(MemorySafetyRulesState.Updated, rules.State);
        Assert.NotEqual(0, index.Safety.UnsafeModes.Explicit);
        Assert.Same(index.Receipt, index.Safety.Receipt);
        Assert.DoesNotContain(
            index.CallGraph.DeclaredMethods,
            method => method.CallerUnsafeMode == CallerUnsafeMode.Implicit);

        MethodIdentity pointerOnly = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && method.Name == "PointerNoneMethod");
        Assert.Equal(
            CallerUnsafeMode.None,
            pointerOnly.CallerUnsafeMode);

        MethodIdentity pointerFreeUnsafe = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && method.Name == "PointerFreeUnsafeMethod");
        Assert.Equal(
            CallerUnsafeMode.Explicit,
            pointerFreeUnsafe.CallerUnsafeMode);

        MethodIdentity constructor = Assert.Single(
            index.CallGraph.DeclaredMethods,
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
                index.CallGraph.DeclaredMethods,
                method =>
                    method.DeclaringType.Name
                        == "AccessorContractFixtures"
                    && method.Name == accessorName);
            Assert.Equal(
                CallerUnsafeMode.Explicit,
                accessor.CallerUnsafeMode);
        }

        MethodIdentity safeExtern = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && method.Name == "SafeExtern");
        Assert.Equal(
            CallerUnsafeMode.None,
            safeExtern.CallerUnsafeMode);
    }

    [Fact]
    public void
        UnsafeMemberUses_ApplyUpdatedSemanticsToUpdatedAssembly()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.Open(
                FixtureCatalog.DecompilerUnsafeNew
                    .AssemblyPath());

        UnsafeMemberUse pointerFree = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && use.Method.Name
                    == "PointerFreeUnsafeMethod");
        Assert.True(
            pointerFree.HasExplicitUnsafeContract);
        Assert.Contains(
            pointerFree.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.ExplicitContract);

        UnsafeMemberUse pointerDereference = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && use.Method.Name == "PointerNoneMethod");
        Assert.False(
            pointerDereference.HasExplicitUnsafeContract);
        Assert.Contains(
            pointerDereference.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.PointerDereference);

        UnsafeMemberUse explicitCall = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && use.Method.Name
                    == "CallPointerFreeUnsafeMethod");
        Assert.Contains(
            explicitCall.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.ExplicitContractCall);
        Assert.DoesNotContain(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && use.Method.Name
                    == "CallPointerNoneMethod");

        Assert.DoesNotContain(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name
                    == "StackAllocDefault");
        AssertSpanStackallocInitializersAreNotUses(index);
        Assert.Contains(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name
                    == "StackAllocSkipInit"
                && use.Evidence.Any(
                    evidence => evidence.Kind
                        == UnsafeMemberUseKind
                            .StackAllocation));
        Assert.Contains(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name
                    == "StackAllocEventData"
                && use.Evidence.Any(
                    evidence => evidence.Kind
                        == UnsafeMemberUseKind
                            .StackAllocation));
    }

    [Fact]
    public void
        UnsafeMemberUses_ApplyUpdatedSemanticsToLegacyAssembly()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.Open(
                FixtureCatalog.DecompilerUnsafeLegacy
                    .AssemblyPath());

        UnsafeMemberUse pointerDereference = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name == "ConsumePointer");
        Assert.False(
            pointerDereference.HasExplicitUnsafeContract);
        Assert.Contains(
            pointerDereference.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.PointerDereference);

        Assert.DoesNotContain(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name == "Risky");
        Assert.DoesNotContain(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name
                    == "StackAllocDefault");
        AssertSpanStackallocInitializersAreNotUses(index);
        Assert.Contains(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name
                    == "StackAllocSkipInit"
                && use.Evidence.Any(
                    evidence => evidence.Kind
                        == UnsafeMemberUseKind
                            .StackAllocation));
        Assert.Contains(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "UnsafeFixtures"
                && use.Method.Name
                    == "StackAllocEventData"
                && use.Evidence.Any(
                    evidence => evidence.Kind
                        == UnsafeMemberUseKind
                            .StackAllocation));
    }

    // Roslyn lowers each initializer through the allocation pointer before
    // wrapping it in Span<T>: CreateSpan plus cpblk, an RVA cpblk, and element
    // stores. None of those is source pointer use.
    static void AssertSpanStackallocInitializersAreNotUses(
        LibraryBodyAnalysisExecution index)
    {
        foreach (var (type, method) in new[]
        {
            ("StackallocInitializerResiduals",
                "StackallocSpanInitializer"),
            ("SpanStackallocInitializers", "ByteElements"),
            ("SpanStackallocInitializers", "ArgumentElements"),
            ("SpanStackallocInitializers", "WidenedConstantElement"),
            ("SpanStackallocInitializers", "FieldElements"),
            ("SpanStackallocInitializers", "VirtualCallElement"),
            ("SpanStackallocInitializers", "DivisionElement"),
            ("SpanStackallocInitializers", "ArrayElement"),
            ("SpanStackallocInitializers", "ConditionalElement"),
            ("SpanStackallocInitializers", "StructElements"),
            ("SpanStackallocInitializers", "NativeIntElements"),
        })
        {
            Assert.DoesNotContain(
                index.Safety.MemberUses,
                use =>
                    use.Method.DeclaringType.Name == type
                    && use.Method.Name == method);
        }
        Assert.Contains(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "StackallocInitializerResiduals"
                && use.Method.Name
                    == "StackallocPointerInitializer"
                && use.Evidence.Any(
                    evidence => evidence.Kind
                        == UnsafeMemberUseKind
                            .StackAllocation));

        // Stores the initializer shape cannot account for are source
        // dereferences even when the allocation is then wrapped by Span<T>:
        // a variable-length allocation, or a constant one written out of
        // bounds.
        UnsafeMemberUse pointerLocal = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "SpanStackallocInitializers"
                && use.Method.Name == "PointerLocalWrapped");
        Assert.Contains(
            pointerLocal.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.PointerDereference);
        UnsafeMemberUse outOfBounds = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "SpanStackallocInitializers"
                && use.Method.Name == "OutOfBoundsStoreWrapped");
        Assert.Contains(
            outOfBounds.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.PointerDereference);
        // A wrapping constant index is unprovable: the raw pointer keeps its
        // roles while the Span<T> allocation beside it is still recognized.
        UnsafeMemberUse overflowing = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "SpanStackallocInitializers"
                && use.Method.Name == "OverflowingConstantIndex");
        Assert.Single(
            overflowing.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.StackAllocation);
        Assert.Contains(
            overflowing.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.PointerDereference);
        // Terms that cancel exactly but wrap in IL are not provably in bounds.
        UnsafeMemberUse wrapping = Assert.Single(
            index.Safety.MemberUses,
            use =>
                use.Method.DeclaringType.Name
                    == "SpanStackallocInitializers"
                && use.Method.Name == "WrappingDisplacementWrapped");
        Assert.Contains(
            wrapping.Evidence,
            evidence => evidence.Kind
                == UnsafeMemberUseKind.PointerDereference);
    }

    [Theory]
    [InlineData(99, MemorySafetyRulesState.Unsupported)]
    [InlineData(null, MemorySafetyRulesState.Malformed)]
    public void
        CallerUnsafeMode_InvalidMarkerUsesNormalizedCompatibilityContract(
            int? marker,
            MemorySafetyRulesState expectedRules)
    {
        LibraryBodyAnalysisExecution index =
            OpenMemorySafetyContractImage(marker);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.Safety.MemorySafetyRules);
        Assert.Equal(expectedRules, rules.State);
        Assert.Same(index.Receipt, index.Safety.Receipt);

        MethodIdentity pointerOnly = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name == "PointerOnly");
        Assert.Equal(
            CallerUnsafeMode.Implicit,
            pointerOnly.CallerUnsafeMode);

        MethodIdentity attributeOnly = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name == "AttributeOnly");
        Assert.Equal(
            CallerUnsafeMode.None,
            attributeOnly.CallerUnsafeMode);
    }

    [Fact]
    public void CallerUnsafeMode_UnavailableContractIsNotAPropagator()
    {
        LibraryBodyAnalysisExecution index =
            OpenMemorySafetyContractImage(2, 1);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.Safety.MemorySafetyRules);
        Assert.Equal(
            MemorySafetyRulesState.Conflicting,
            rules.State);
        Assert.All(
            index.CallGraph.DeclaredMethods,
            method => Assert.Equal(
                CallerUnsafeMode.Unavailable,
                method.CallerUnsafeMode));
        Assert.Equal(
            index.CallGraph.DeclaredMethods.Length,
            index.Safety.UnsafeModes.Unavailable);
        Assert.Equal(0, index.Safety.UnsafeModes.Unsafe);

        Assert.Empty(index.Leverage.TopUnsafe(1));
        Assert.Same(index.Receipt, index.Safety.Receipt);
        Assert.Empty(OpaqueUnsafe.Collect(index.CallGraph.Methods));
        Assert.Empty(HollowUnsafe.Collect(
            index.CallGraph.Methods,
            index.Safety.Evidence));
    }

    [Fact]
    public void CallerUnsafeMode_CallingUnsafeApiIsNotRequiresUnsafe()
    {
        // The authoritative model is RequiresUnsafeAttribute || pointer signature.
        // Calling an unsafe API does not itself make a method requires-unsafe —
        // that is the heuristic's domain, deliberately excluded from the model.
        var index = BodyAnalysisTestExecution.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var callsUnsafeAs = Assert.Single(index.CallGraph.Methods.Where(m =>
            m.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)));
        Assert.Equal(CallerUnsafeMode.None, callsUnsafeAs.CallerUnsafeMode);
    }

    [Fact]
    public void GeneratedFrameworkTypes_DetectsGrpcStub_AndRejectsUnauthenticProtobufSpoof()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(FakeProtobufReflection).Assembly.Location);
        var generated = index.Optimization.GeneratedFrameworkTypes;

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
        Assert.Contains(index.Optimization.Opportunities, opportunity =>
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
        var index = BodyAnalysisTestExecution.Open(FixtureCatalog.AnalysisLookalike.AssemblyPath());
        var generated = index.Optimization.GeneratedFrameworkTypes;

        Assert.DoesNotContain(
            generated,
            type => type.Name == "ProtobufBootstrapLookalike");

        // Because it is not classified as generated, its optimization opportunity stays
        // visible — Performance Triage suppresses opportunities only for generated types.
        Assert.Contains(index.Optimization.Opportunities, opportunity =>
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
            var index = BodyAnalysisTestExecution.Open(path);
            var generated = index.Optimization.GeneratedFrameworkTypes;

            TypeRef namespaceLeaf = index.CallGraph.Methods
                .First(method => method.DeclaringType.Namespace == "CollisionNs.A"
                    && method.DeclaringType.Name == "GeneratedLeaf")
                .DeclaringType;
            TypeRef nestedLeaf = index.CallGraph.Methods
                .First(method => method.DeclaringType.Namespace == "CollisionNs"
                    && method.DeclaringType.Name == "A+GeneratedLeaf")
                .DeclaringType;

            Assert.Equal(
                namespaceLeaf.ToQualifiedDisplayString(),
                nestedLeaf.ToQualifiedDisplayString());
            Assert.Contains(generated, type => type.Equals(namespaceLeaf));
            Assert.DoesNotContain(generated, type => type.Equals(nestedLeaf));
            Assert.True(
                GeneratedFrameworkTypeAnalysis.Contains(
                    generated,
                    namespaceLeaf));
            Assert.False(
                GeneratedFrameworkTypeAnalysis.Contains(
                    generated,
                    nestedLeaf));
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
            var index = BodyAnalysisTestExecution.Open(path);
            var generated = index.Optimization.GeneratedFrameworkTypes;

            TypeRef nestedGenerated = index.CallGraph.Methods
                .First(method => method.DeclaringType.Namespace == "ReverseNs"
                    && method.DeclaringType.Name == "A+NestedLeaf")
                .DeclaringType;
            TypeRef namespaceLookalike = index.CallGraph.Methods
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
        Assert.True(
            GeneratedFrameworkTypeAnalysis.Contains(
                set,
                generatedNested));
        Assert.False(
            GeneratedFrameworkTypeAnalysis.Contains(
                set,
                lookalikeNested));
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
            var index = BodyAnalysisTestExecution.Open(path);

            TypeRef stub = index.CallGraph.Methods
                .First(method => method.DeclaringType.Namespace == "Ns"
                    && method.DeclaringType.Name == "GenStub")
                .DeclaringType;
            TypeRef nested = index.CallGraph.Methods
                .First(method => method.Name == "InnerMethod")
                .DeclaringType;
            TypeRef literalPlus = index.CallGraph.Methods
                .First(method => method.Name == "UnrelatedUserMethod")
                .DeclaringType;

            Assert.Equal(["GenStub"], stub.Resolution!.Type.Segments);
            Assert.Equal(["GenStub", "Inner"], nested.Resolution!.Type.Segments);
            Assert.Equal(["GenStub+LiteralPlus"], literalPlus.Resolution!.Type.Segments);

            IReadOnlySet<TypeRef> generated =
                index.Optimization.GeneratedFrameworkTypes;
            Assert.Contains(generated, type => type.Equals(stub));
            Assert.True(
                GeneratedFrameworkTypeAnalysis.Contains(
                    generated,
                    nested));
            Assert.False(
                GeneratedFrameworkTypeAnalysis.Contains(
                    generated,
                    literalPlus));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OptimizationOpportunities_SuppressesSourceGeneratedTypes()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A [GeneratedCode] type (source generators: JSON/regex/etc.) is not an actionable
        // source-shape target, so none of its methods produce optimization opportunities,
        // even though MakesSmallArray would otherwise be a small-array row (#1273).
        Assert.DoesNotContain(index.Optimization.Opportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == nameof(SourceGeneratedOptimizationFixtures));
        Assert.DoesNotContain(
            index.Optimization.Opportunities,
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
