using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class CompileReferencePlatformPolicyTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    static string RuntimePath => PlatformPath("System.Runtime.dll");
    static string JsonPath => typeof(System.Text.Json.JsonSerializer).Assembly.Location;
    static AssemblyReferenceIdentity RuntimeIdentity => Identity(RuntimePath);
    static AssemblyReferenceIdentity JsonIdentity => Identity(JsonPath);
    static AssemblyReferenceIdentity OlderRuntime => RuntimeIdentity with
    {
        Version = new Version(RuntimeIdentity.Version!.Major - 1, 0, 0, 0),
    };

    [Fact]
    public async Task OlderPlatformRequestPreparesForwardingAndBindsRetainedCoreLib()
    {
        await using var fixture = new Fixture();
        CompileReferencePlatformPolicy policy = await fixture.Prepare(OlderRuntime);
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        CompileReferenceSet set = Ready(policy.Select(inventory, [], Cancellation));
        Assert.Contains(policy.Bindings, binding =>
            binding.PlatformSelection.Selection is AssemblyBindingSelection.Selected selected
            && selected.Assembly.Identity.Name == "System.Private.CoreLib");
        Assert.Same(fixture.Resolver.Version, policy.OwnerPolicyVersion);
        Assert.Same(policy.OwnerPolicyVersion, set.Digest.OwnerPolicyVersion);
        Assert.All(set.References, reference => Assert.True(reference.IsPlatformAuthorized));

        Ready(set.Use(context =>
        {
            var request = TypeResolutionRequest.FromReference(
                OlderRuntime, AssemblyBindingOrigin.FromAssembly(context.Source),
                AssemblyResolutionScope.Platform, TypeName("System", "IConvertible"));
            using TypeResolutionContext metadata = TypeResolutionContext.Create(context, [context.Source], [request]);
            var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(metadata.Resolve(request));
            Assert.Equal("System.Private.CoreLib", resolved.Definition.Assembly.Assembly.Identity.Name);
            CompileReferenceImage image = Assert.Single(set.References,
                reference => reference.Image.Identity.Name == "System.Private.CoreLib").Image;
            Assert.Same(image.ArtifactRegistration, resolved.Definition.Assembly.Assembly.Registration.ArtifactRegistration);
            Assert.Equal(image.ModuleVersionId, resolved.Definition.Address.ModuleVersionId);
            AssertCompilerType(context, "System.IConvertible", image);
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task IdenticalSiblingRemainsDistinctAndOnlyOwnerPlatformIsSelected()
    {
        await using var fixture = new Fixture("identical");
        CompileReferencePlatformPolicy policy = await fixture.Prepare(JsonIdentity, OlderRuntime);
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        CompilePlatformBindingEvidence binding = JsonBinding(policy);
        Assert.NotSame(binding.PlatformArtifact, binding.AgreementArtifact);
        CompileReferenceImage platform = Image(inventory, binding.PlatformArtifact);
        CompileReferenceImage sibling = Image(inventory, binding.AgreementArtifact);
        Assert.Equal(platform.ContentDigest.HexValue, sibling.ContentDigest.HexValue);
        Assert.Equal(platform.ModuleVersionId, sibling.ModuleVersionId);
        Assert.NotSame(platform.MetadataRegistration, sibling.MetadataRegistration);
        Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(platform.Provenance);
        Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(sibling.Provenance);
        Rejected(inventory.Select([new(JsonIdentity)], Cancellation),
            CompileReferenceFailureKind.ReferenceSelectionAmbiguous);

        CompileReferenceSet set = Ready(policy.Select(inventory, [], Cancellation));
        Assert.Contains(set.References, reference => ReferenceEquals(reference.InventoryId, binding.PlatformArtifact));
        Assert.DoesNotContain(set.References, reference => ReferenceEquals(reference.InventoryId, binding.AgreementArtifact));
        Assert.All(inventory.Candidates, image =>
            Assert.Same(image.ArtifactRegistration, image.MetadataRegistration.ArtifactRegistration));
        Ready(set.Use(context =>
        {
            AssertCompilerType(context, "System.Text.Json.JsonSerializerOptions", platform);
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task ChangedSiblingWithSameIdentityAndMvidRefusesAfterOwnerDigests()
    {
        await using var fixture = new Fixture("changed");
        CompileReferencePlatformPolicy policy = await fixture.Prepare(JsonIdentity);
        var charges = new List<long>();
        CompileReferenceInventory inventory = await fixture.Publish(policy, charges.Add);
        CompilePlatformBindingEvidence binding = JsonBinding(policy);
        CompileReferenceImage platform = Image(inventory, binding.PlatformArtifact);
        CompileReferenceImage sibling = Image(inventory, binding.AgreementArtifact);
        Assert.True(platform.Identity.IsEquivalentTo(sibling.Identity));
        Assert.Equal(platform.ModuleVersionId, sibling.ModuleVersionId);
        Assert.NotEqual(platform.ContentDigest.HexValue, sibling.ContentDigest.HexValue);
        Assert.Equal(inventory.Candidates.Length + 1, charges.Count);
        foreach (CompileReferenceImage image in inventory.Candidates.Prepend(inventory.Source))
        {
            var digest = Assert.IsType<ArtifactContentAccessOutcome<ArtifactContentDigest>.Accessed>(
                fixture.Owner.GetContentDigest(image.InventoryId, fixture.Lease,
                    _ => Assert.Fail("Expected an already-issued owner digest."), Cancellation));
            Assert.Same(digest.Value, image.ContentDigest);
        }
        CompileReferenceFailure failure = Rejected(policy.Select(inventory, [], Cancellation),
            CompileReferenceFailureKind.ReferencePlatformAgreementMismatch);
        Assert.Equal([binding.PlatformArtifact, binding.AgreementArtifact], failure.Candidates);
    }

    [Fact]
    public async Task VersionSkewedSiblingRetainsOwnerNameMismatchRefusal()
    {
        await using var fixture = new Fixture("skewed");
        CompileReferenceFailure failure = Rejected(await fixture.TryPrepare(JsonIdentity),
            CompileReferenceFailureKind.ReferencePlatformSelectionUnavailable);
        Assert.Equal(AssemblyResolutionScope.Any, failure.BindingRequest!.Scope);
        Assert.Equal(JsonIdentity, ((AssemblyBindingTarget.AssemblyReference)failure.BindingRequest.Target).Identity);
        Assert.Equal(AssemblyBindingMissDisposition.NameOwnedNoMatch,
            Assert.IsType<AssemblyBindingSelection.Missing>(failure.PolicySelection!.Selection).Disposition);
    }

    [Fact]
    public async Task PlatformScopeDoesNotPromoteDesignatedAcquisitions()
    {
        await using var fixture = new Fixture("identical", designateSibling: true);
        CompileReferenceFailure failure = Rejected(await fixture.TryPrepare(JsonIdentity),
            CompileReferenceFailureKind.ReferencePlatformSelectionUnavailable);
        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(failure.PolicySelection!.Selection);
        Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(selected.Assembly.Provenance);
        Assert.Equal(AssemblyResolutionScope.Platform, failure.BindingRequest!.Scope);
    }

    [Fact]
    public async Task ServicesWildcardAndDescriptiveOptionsDoNotWeakenExactFamilyOrOneWayVersion()
    {
        await using var fixture = new Fixture(ignoreVersion: true);
        Rejected(await fixture.TryPrepare(JsonIdentity with { PublicKeyToken = null }),
            CompileReferenceFailureKind.ReferencePlatformIdentityMismatch);
        Rejected(await fixture.TryPrepare(JsonIdentity with { Version = new Version(JsonIdentity.Version!.Major + 1, 0, 0, 0) }),
            CompileReferenceFailureKind.ReferencePlatformIdentityMismatch);
        Rejected(await fixture.TryPrepare(JsonIdentity with { Culture = "fr-FR" }),
            CompileReferenceFailureKind.ReferencePlatformSelectionUnavailable);
    }

    [Fact]
    public async Task PlatformLookingLocalInputAndLabelCannotAuthorizeSelection()
    {
        await using var fixture = new Fixture(includePlatform: false);
        AssemblyReferenceIdentity identity = fixture.Source.Identity;
        var labelled = ResolvedAssemblyReference.CreateFromPath(fixture.SourcePath,
            AssemblyResolutionProvenance.Platform("claimed framework", "1", "caller label"));
        var request = new AssemblyBindingRequest(AssemblyBindingTarget.Reference(identity),
            AssemblyBindingOrigin.FromAssembly(labelled), AssemblyResolutionScope.Platform);
        Rejected(await CompileReferencePlatformPolicy.PrepareAsync(
            fixture.Owner, fixture.Resolver, labelled, [request], Cancellation),
            CompileReferenceFailureKind.ReferencePlatformSelectionUnavailable);
    }

    [Fact]
    public async Task ExactDefaultNeverUsesCapturedPlatformPermission()
    {
        await using var fixture = new Fixture();
        CompileReferencePlatformPolicy policy = await fixture.Prepare(OlderRuntime);
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        Rejected(inventory.Select([new(OlderRuntime)], Cancellation), CompileReferenceFailureKind.ReferenceNotFound);
        CompileReferenceSet exact = Ready(inventory.Select([new(RuntimeIdentity)], Cancellation));
        Assert.Null(exact.Digest.OwnerPolicyVersion);
        Assert.False(Assert.Single(exact.References).IsPlatformAuthorized);
        Ready(exact.Use(context =>
        {
            Assert.Null(context.Resolve(OlderRuntime, AssemblyResolutionScope.Platform));
            Assert.IsType<AssemblyBindingSelection.Unavailable>(context.Select(
                Request(OlderRuntime, context.Source)).Selection);
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task FrozenBindingsRemapArtifactOriginsAndPreserveScopeAndSeedOccurrences()
    {
        await using var fixture = new Fixture();
        CompileReferencePlatformPolicy policy = await fixture.Prepare(OlderRuntime, JsonIdentity);
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        CompileReferenceSet set = Ready(policy.Select(inventory, [], Cancellation));
        Ready(set.Use(context =>
        {
            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                context.Select(Request(OlderRuntime, context.Source)).Selection);
            Assert.Same(AssemblyBindingLineage.Seed, selected.Occurrence.Lineage);
            Assert.NotSame(fixture.Source.Registration, context.Source.Registration);
            Assert.IsType<AssemblyBindingSelection.Rejected>(
                context.Select(Request(OlderRuntime, fixture.Source)).Selection);
            Assert.IsType<AssemblyBindingSelection.Unavailable>(context.Select(new(
                AssemblyBindingTarget.Reference(OlderRuntime), AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Platform)).Selection);
            Assert.IsType<AssemblyBindingSelection.Unavailable>(context.Select(new(
                AssemblyBindingTarget.Reference(OlderRuntime), AssemblyBindingOrigin.FromAssembly(context.Source),
                AssemblyResolutionScope.Any)).Selection);
            ResolvedAssemblyReference other = context.Resolve(JsonIdentity, AssemblyResolutionScope.Any)!;
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                context.Select(Request(OlderRuntime, other)).Selection);
            var lineage = new ForeignLineage(new AssemblyBindingPolicyVersion());
            Assert.IsType<AssemblyBindingSelection.Rejected>(context.Select(new(
                AssemblyBindingTarget.Reference(OlderRuntime),
                AssemblyBindingOrigin.FromOccurrence(lineage.Bind(context.Source)),
                AssemblyResolutionScope.Platform)).Selection);
            // Services could resolve this identity, but it was never prepared for this origin.
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                context.Select(Request(RuntimeIdentity, context.Source)).Selection);
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task PolicyRequestsOriginsAndIssuerVersionArePartOfSetIdentity()
    {
        await using var fixture = new Fixture();
        AssemblyReferenceIdentity earlier = OlderRuntime with { Version = new Version(OlderRuntime.Version!.Major - 1, 0, 0, 0) };
        var first = await Build(OlderRuntime, global: false);
        var anotherVersion = await Build(earlier, global: false);
        var anotherOrigin = await Build(OlderRuntime, global: true);
        Assert.Equal(first.Images, anotherVersion.Images);
        Assert.Equal(first.Images, anotherOrigin.Images);
        Assert.NotEqual(first.Digest.HexValue, anotherVersion.Digest.HexValue);
        Assert.NotEqual(first.Digest.HexValue, anotherOrigin.Digest.HexValue);
        Assert.Same(fixture.Resolver.Version, first.Digest.OwnerPolicyVersion);

        async Task<(CompileReferenceSetDigest Digest, string[] Images)> Build(AssemblyReferenceIdentity identity, bool global)
        {
            await using var owner = new ArtifactSetSession();
            AssemblyBindingRequest request = global
                ? new(AssemblyBindingTarget.Reference(identity), AssemblyBindingOrigin.Global(), AssemblyResolutionScope.Platform)
                : Request(identity, fixture.Source);
            CompileReferencePlatformPolicy policy = Ready(await CompileReferencePlatformPolicy.PrepareAsync(
                owner, fixture.Resolver, fixture.Source, [request], Cancellation));
            Assert.IsType<ArtifactSetPublicationOutcome.Published>(await owner.SealAsync(Cancellation));
            using ArtifactQueryLease lease = owner.IssueLease(owner.CreateQueryAuthorization());
            CompileReferenceInventory inventory = Ready(policy.Discover(lease, _ => { }, Cancellation));
            CompileReferenceSet set = Ready(policy.Select(inventory, [], Cancellation));
            return (set.Digest, [.. set.References.Select(reference => reference.Image.ContentDigest.HexValue)]);
        }
    }

    [Fact]
    public async Task RepeatedDiscoveryAndReorderedSelectionsPreservePolicySetIdentity()
    {
        await using var fixture = new Fixture("identical");
        CompileReferencePlatformPolicy policy = await fixture.Prepare(OlderRuntime, JsonIdentity);
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        CompileReferenceRequest[] requests = [.. policy.Bindings.Select(binding => binding.PlatformArtifact).Distinct()
            .Select(id => new CompileReferenceRequest(Image(inventory, id).Identity, id))];
        CompileReferenceSet first = Ready(policy.Select(inventory, requests, Cancellation));
        CompileReferenceInventory rediscovered = Ready(policy.Discover(fixture.Lease, _ => Assert.Fail("Warm digest charge."), Cancellation));
        CompileReferenceSet second = Ready(policy.Select(rediscovered, requests.Reverse(), Cancellation));
        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(first.References.Select(reference => reference.InventoryId), second.References.Select(reference => reference.InventoryId));
    }

    [Fact]
    public async Task EquivalentRepeatedRequestsDoNotCreateCompetingBindings()
    {
        await using var fixture = new Fixture();
        CompileReferencePlatformPolicy policy = await fixture.Prepare(OlderRuntime,
            OlderRuntime with { Name = OlderRuntime.Name.ToLowerInvariant(), Culture = "neutral" });
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        CompileReferenceSet set = Ready(policy.Select(inventory, [], Cancellation));
        Ready(set.Use(context =>
        {
            var first = Assert.IsType<AssemblyBindingSelection.Selected>(
                context.Select(Request(OlderRuntime, context.Source)).Selection);
            var second = Assert.IsType<AssemblyBindingSelection.Selected>(
                context.Select(Request(OlderRuntime with { Culture = "neutral" }, context.Source)).Selection);
            Assert.Same(first.Assembly, second.Assembly);
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task PlatformSelectionCannotReintroduceTheSourceRegistration()
    {
        await using var fixture = new Fixture();
        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            fixture.Resolver.Select(Request(JsonIdentity, fixture.Source)).Selection);
        CompileReferencePlatformPolicy policy = Ready(await CompileReferencePlatformPolicy.PrepareAsync(
            fixture.Owner, fixture.Resolver, selected.Assembly, [Request(JsonIdentity, selected.Assembly)], Cancellation));
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        Rejected(policy.Select(inventory, [], Cancellation), CompileReferenceFailureKind.SourceReferenceExcluded);
    }

    [Fact]
    public async Task FailedSupportingImageRetainsTheMetadataFailure()
    {
        await using var fixture = new Fixture();
        var source = ResolvedAssemblyReference.Create(fixture.Source.Identity, null,
            () => new MemoryStream([1, 2, 3]), AssemblyResolutionProvenance.Local("malformed source"));
        CompileReferenceFailure failure = Rejected(await CompileReferencePlatformPolicy.PrepareAsync(
            fixture.Owner, fixture.Resolver, source, [Request(OlderRuntime, source)], Cancellation),
            CompileReferenceFailureKind.ReferenceContentUnavailable);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, failure.ContentFailure!.Kind);
    }

    [Fact]
    public async Task SupportingAcquisitionsAreRetainedBeforeArtifactSealAndScopedUse()
    {
        await using var fixture = new Fixture("identical");
        CompileReferencePlatformPolicy policy = await fixture.Prepare(OlderRuntime, JsonIdentity);
        File.Delete(fixture.SourcePath);
        File.WriteAllBytes(fixture.SiblingPath, [1, 2, 3]);
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        CompileReferenceSet set = Ready(policy.Select(inventory, [], Cancellation));
        File.Delete(fixture.SiblingPath);
        Ready(set.Use(context =>
        {
            using var pe = new PEReader(context.Source.OpenRead());
            Assert.Equal(inventory.Source.ModuleVersionId, pe.GetMetadataReader().GetGuid(pe.GetMetadataReader().GetModuleDefinition().Mvid));
            CompileReferenceImage json = Image(inventory, JsonBinding(policy).PlatformArtifact);
            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
                context.Select(Request(JsonIdentity, context.Source)).Selection);
            using var selectedPe = new PEReader(selected.Assembly.OpenRead());
            Assert.Equal(json.ModuleVersionId, selectedPe.GetMetadataReader().GetGuid(selectedPe.GetMetadataReader().GetModuleDefinition().Mvid));
            AssertCompilerType(context, "System.Text.Json.JsonSerializerOptions", json);
            return true;
        }, Cancellation));
    }

    [Theory]
    [InlineData("lease")]
    [InlineData("revoked")]
    [InlineData("session")]
    public async Task StaleAuthorityRejectsPolicySelectionAndConsumption(string mode)
    {
        await using var fixture = new Fixture();
        CompileReferencePlatformPolicy policy = await fixture.Prepare(OlderRuntime);
        CompileReferenceInventory inventory = await fixture.Publish(policy);
        CompileReferenceSet set = Ready(policy.Select(inventory, [], Cancellation));
        switch (mode)
        {
            case "lease": fixture.Lease.Dispose(); break;
            case "revoked": fixture.Owner.Revoke(fixture.Authorization); break;
            case "session": await fixture.Owner.DisposeAsync(); break;
        }
        Rejected(policy.Select(inventory, [], Cancellation), CompileReferenceFailureKind.ReferenceAuthorityUnavailable);
        Rejected(set.Use<int>(_ => throw new InvalidOperationException("Must not consume."), Cancellation),
            CompileReferenceFailureKind.ReferenceAuthorityUnavailable);
    }

    [Fact]
    public async Task PreparationCannotBindAnUnrelatedArtifactGeneration()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        CompileReferencePlatformPolicy firstPolicy = await first.Prepare(OlderRuntime);
        CompileReferencePlatformPolicy secondPolicy = await second.Prepare(OlderRuntime);
        CompileReferenceInventory secondInventory = await second.Publish(secondPolicy);
        Rejected(firstPolicy.Select(secondInventory, [], Cancellation), CompileReferenceFailureKind.ReferencePlatformPolicyMismatch);
    }

    static void AssertCompilerType(CompileReferenceContext context, string typeName, CompileReferenceImage image)
    {
        var syntax = CSharpSyntaxTree.ParseText($"public sealed class Consumer {{ public {typeName} Value; }}",
            cancellationToken: Cancellation);
        var compilation = CSharpCompilation.Create("PlatformConsumer", [syntax], context.CompilerReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(Cancellation).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        VariableDeclaratorSyntax declaration = syntax.GetRoot(Cancellation).DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        IFieldSymbol field = Assert.IsAssignableFrom<IFieldSymbol>(compilation.GetSemanticModel(syntax).GetDeclaredSymbol(declaration, Cancellation));
        Assert.Equal(typeName, field.Type.ToDisplayString());
        Assert.Equal(image.Identity.Name, field.Type.ContainingAssembly.Name);
        Assert.Equal(image.Identity.Version, field.Type.ContainingAssembly.Identity.Version);
        PortableExecutableReference reference = Assert.Single(context.CompilerReferences, candidate =>
            compilation.GetAssemblyOrModuleSymbol(candidate) is IAssemblySymbol symbol
            && SymbolEqualityComparer.Default.Equals(symbol, field.Type.ContainingAssembly));
        Assert.Equal(image.ModuleVersionId,
            Assert.Single(Assert.IsType<AssemblyMetadata>(reference.GetMetadata()).GetModules()).GetModuleVersionId());
    }

    static AssemblyBindingRequest Request(AssemblyReferenceIdentity identity, ResolvedAssemblyReference source) =>
        new(AssemblyBindingTarget.Reference(identity), AssemblyBindingOrigin.FromAssembly(source), AssemblyResolutionScope.Platform);

    static CompilePlatformBindingEvidence JsonBinding(CompileReferencePlatformPolicy policy) =>
        Assert.Single(policy.Bindings, binding => ((AssemblyBindingTarget.AssemblyReference)binding.Request.Target).Identity.IsEquivalentTo(JsonIdentity));

    static CompileReferenceImage Image(CompileReferenceInventory inventory, ArtifactIdentity id) =>
        inventory.Candidates.Prepend(inventory.Source).Single(image => ReferenceEquals(image.InventoryId, id));

    static MetadataTypeDefinitionName TypeName(string ns, string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(MetadataTypeDefinitionName.Create(ns, [name])).Name;

    static string PlatformPath(string name) => (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Single(path => Path.GetFileName(path) == name);

    static AssemblyReferenceIdentity Identity(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader());
    }

    static T Ready<T>(CompileReferenceResult<T> result) =>
        Assert.IsType<CompileReferenceResult<T>.Ready>(result).Value;

    static CompileReferenceFailure Rejected<T>(CompileReferenceResult<T> result, CompileReferenceFailureKind kind)
    {
        CompileReferenceFailure failure = Assert.IsType<CompileReferenceResult<T>.Rejected>(result).Failure;
        Assert.Equal(kind, failure.Kind);
        return failure;
    }

    sealed record ForeignLineage(AssemblyBindingPolicyVersion PolicyVersion) : AssemblyBindingLineage(PolicyVersion)
    {
        internal AssemblyBindingOccurrence Bind(ResolvedAssemblyReference assembly) => CreateOccurrence(assembly);
    }

    sealed class Fixture : IAsyncDisposable
    {
        readonly string _directory = Path.Combine("artifacts", "platform-policy-tests", Guid.NewGuid().ToString("N"));
        public ArtifactSetSession Owner { get; } = new();
        public ArtifactQueryAuthorization Authorization { get; private set; } = null!;
        public ArtifactQueryLease Lease { get; private set; } = null!;
        public AssemblyDependencyResolver Resolver { get; }
        public ResolvedAssemblyReference Source { get; }
        public string SourcePath { get; }
        public string SiblingPath { get; }

        public Fixture(string? sibling = null, bool designateSibling = false, bool ignoreVersion = false, bool includePlatform = true)
        {
            Directory.CreateDirectory(_directory);
            SourcePath = Path.Combine(_directory, "Fixture.dll");
            File.Copy(FixtureCatalog.AssemblyPath(FixtureIds.DecompilerTypeIdentity), SourcePath);
            SiblingPath = Path.Combine(_directory, "System.Text.Json.dll");
            if (sibling is not null)
            {
                byte[] bytes = File.ReadAllBytes(JsonPath);
                using var pe = new PEReader(new MemoryStream(bytes, writable: false));
                if (sibling == "changed")
                    bytes[pe.PEHeaders.CoffHeaderStartOffset + 4] ^= 1;
                else if (sibling == "skewed")
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(pe.PEHeaders.MetadataStartOffset + pe.GetMetadataReader().GetTableMetadataOffset(TableIndex.Assembly) + 4),
                        checked((ushort)(JsonIdentity.Version!.Major - 1)));
                File.WriteAllBytes(SiblingPath, bytes);
            }
            Resolver = new(new AssemblyDependencyResolutionOptions(SourcePath)
            {
                PackageRoots = [],
                ExcludeTargetAssembly = true,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
                IncludeTrustedPlatformAssemblies = includePlatform,
                AllowPlatformAssemblyVersionRollForward = true,
                IgnoreAssemblyVersion = ignoreVersion,
                CorpusAssemblyPaths = designateSibling ? [SiblingPath] : null,
            });
            Source = Resolver.AcquireTargetAssembly()!;
        }

        public ValueTask<CompileReferenceResult<CompileReferencePlatformPolicy>> TryPrepare(params AssemblyReferenceIdentity[] identities) =>
            CompileReferencePlatformPolicy.PrepareAsync(Owner, Resolver, Source,
                identities.Select(identity => Request(identity, Source)), Cancellation);

        public async Task<CompileReferencePlatformPolicy> Prepare(params AssemblyReferenceIdentity[] identities) =>
            Ready(await TryPrepare(identities));

        public async Task<CompileReferenceInventory> Publish(CompileReferencePlatformPolicy policy, Action<long>? charge = null)
        {
            Assert.IsType<ArtifactSetPublicationOutcome.Published>(await Owner.SealAsync(Cancellation));
            Authorization = Owner.CreateQueryAuthorization();
            Lease = Owner.IssueLease(Authorization);
            return Ready(policy.Discover(Lease, charge ?? (_ => { }), Cancellation));
        }

        public async ValueTask DisposeAsync()
        {
            Lease?.Dispose();
            await Owner.DisposeAsync();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
