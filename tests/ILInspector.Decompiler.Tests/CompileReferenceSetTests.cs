using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using InertText;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class CompileReferenceSetTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    static byte[] SourceBytes() => Bytes(FixtureIds.DecompilerTypeIdentity);
    static byte[] TargetBytes() => Bytes(FixtureIds.AnalysisCallerGraphTarget);
    static byte[] Bytes(string id) => File.ReadAllBytes(FixtureCatalog.AssemblyPath(id));

    [Fact]
    public async Task CompileReferenceSelectionDoesNotUseSimpleNameFirstWins()
    {
        await using var fixture = await Published.Create(
            SourceBytes(), TargetBytes(), Bytes(FixtureIds.AnalysisCallerGraphTargetV2));
        CompileReferenceInventory forward = fixture.Discover(1, 2);
        CompileReferenceInventory reverse = fixture.Discover(2, 1);
        Assert.Equal(forward.Candidates.Select(image => image.InventoryId),
            reverse.Candidates.Select(image => image.InventoryId));
        Assert.Equal(forward.Candidates[0].Identity.Name, forward.Candidates[1].Identity.Name);
        Assert.NotEqual(forward.Candidates[0].Identity.Version, forward.Candidates[1].Identity.Version);

        CompileReferenceRequest[] requests = [.. forward.Candidates.Select(image => new CompileReferenceRequest(image.Identity))];
        CompileReferenceSet first = Ready(forward.Select(requests, Cancellation));
        CompileReferenceSet second = Ready(reverse.Select(requests.Reverse(), Cancellation));
        Assert.Equal(2, first.References.Length);
        Assert.Equal([0, 1], first.References.Select(reference => reference.SelectedOrdinal));
        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(first.References.Select(reference => reference.InventoryId),
            second.References.Select(reference => reference.InventoryId));

        foreach (CompileReferenceImage image in forward.Candidates)
        {
            CompileReferenceSet selected = Ready(reverse.Select([new(image.Identity)], Cancellation));
            Assert.Same(image.InventoryId, Assert.Single(selected.References).InventoryId);
        }
        Assert.Throws<ArgumentNullException>(() => new CompileReferenceRequest(
            new(forward.Candidates[0].Identity.Name, null, null, null)));
        Rejected(forward.Select([new(forward.Candidates[0].Identity, forward.Candidates[1].InventoryId)], Cancellation),
            CompileReferenceFailureKind.ReferenceNotFound);
    }

    [Fact]
    public async Task CompileReferenceSelectionRejectsSameIdentityDifferentContent()
    {
        await using var fixture = await Published.Create(
            SourceBytes(), Bytes(FixtureIds.DiffV1), Bytes(FixtureIds.DiffV2));
        CompileReferenceInventory inventory = fixture.Discover(1, 2);
        CompileReferenceImage first = inventory.Candidates[0];
        CompileReferenceImage second = inventory.Candidates[1];
        Assert.True(first.Identity.IsEquivalentTo(second.Identity));
        Assert.NotEqual(first.ContentDigest.HexValue, second.ContentDigest.HexValue);
        Assert.NotEqual(first.ModuleVersionId, second.ModuleVersionId);
        CompileReferenceRequest[] requests = [new(first.Identity)];

        CompileReferenceFailure forward = Rejected(inventory.Select(requests, Cancellation),
            CompileReferenceFailureKind.ReferenceSelectionAmbiguous);
        CompileReferenceFailure reverse = Rejected(fixture.Discover(2, 1).Select(requests, Cancellation),
            CompileReferenceFailureKind.ReferenceSelectionAmbiguous);
        Assert.Equal([first.InventoryId, second.InventoryId], forward.Candidates);
        Assert.Equal(forward.Candidates, reverse.Candidates);

        CompileReferenceSet pinned = Ready(inventory.Select([new(first.Identity, second.InventoryId)], Cancellation));
        Assert.Same(second, Assert.Single(pinned.References).Image);
        Ready(pinned.Use(context =>
        {
            var metadata = Assert.IsType<AssemblyMetadata>(Assert.Single(context.CompilerReferences).GetMetadata());
            Assert.Equal(second.ModuleVersionId, Assert.Single(metadata.GetModules()).GetModuleVersionId());
            return true;
        }, Cancellation));
        Rejected(inventory.Select([new(first.Identity, first.InventoryId), new(second.Identity, second.InventoryId)], Cancellation),
            CompileReferenceFailureKind.ReferenceSelectionAmbiguous);
    }

    [Fact]
    public async Task CompileReferenceSelectionPreservesDistinctIdenticalRegistrations()
    {
        byte[] target = TargetBytes();
        await using var fixture = await Published.Create(SourceBytes(), target, target);
        CompileReferenceInventory inventory = fixture.Discover(2, 1, 2, 1);
        Assert.Equal(2, inventory.Candidates.Length);
        CompileReferenceImage first = inventory.Candidates[0];
        CompileReferenceImage second = inventory.Candidates[1];
        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(first.ContentDigest.HexValue, second.ContentDigest.HexValue);
        Assert.Equal(first.ModuleVersionId, second.ModuleVersionId);
        Assert.NotSame(first.ArtifactRegistration, second.ArtifactRegistration);
        Assert.NotSame(first.MetadataRegistration, second.MetadataRegistration);
        Assert.Same(first.ArtifactRegistration, first.MetadataRegistration.ArtifactRegistration);
        Assert.Same(fixture.Session.GetContentReference(fixture.Ids[1], fixture.Lease).Registration,
            first.ArtifactRegistration);
        Assert.Equal(first.ModuleVersionId, first.MetadataRegistration.ModuleVersionId);
        Rejected(inventory.Select([new(first.Identity)], Cancellation),
            CompileReferenceFailureKind.ReferenceSelectionAmbiguous);
        Rejected(fixture.Discover(1, 2).Select([new(first.Identity)], Cancellation),
            CompileReferenceFailureKind.ReferenceSelectionAmbiguous);

        CompileReferenceSet pinnedFirst = Ready(inventory.Select([new(first.Identity, first.InventoryId)], Cancellation));
        CompileReferenceSet pinnedSecond = Ready(inventory.Select([new(first.Identity, second.InventoryId)], Cancellation));
        Assert.NotEqual(pinnedFirst.Digest, pinnedSecond.Digest);
        CompileReferenceSet repeated = Ready(fixture.Discover(1, 1, 1).Select(
            [new(first.Identity), new(first.Identity)], Cancellation));
        Assert.Single(repeated.References);
        Assert.Equal(pinnedFirst.Digest, repeated.Digest);
    }

    [Fact]
    public async Task RolesAreCanonicalAndPartOfTheSetAndCompilerReferences()
    {
        await using var fixture = await Published.Create(SourceBytes(), TargetBytes());
        CompileReferenceInventory inventory = fixture.Discover(1);
        AssemblyReferenceIdentity identity = inventory.Candidates[0].Identity;
        CompileReferenceSet global = Ready(inventory.Select([new(identity)], Cancellation));
        CompileReferenceSet explicitGlobal = Ready(inventory.Select([new(identity, aliases: ["global"])], Cancellation));
        CompileReferenceSet aliased = Ready(inventory.Select([new(identity, aliases: ["z", "a", "a"])], Cancellation));
        CompileReferenceSet reordered = Ready(inventory.Select([new(identity, aliases: ["a", "z"])], Cancellation));
        CompileReferenceSet embedded = Ready(inventory.Select(
            [new(identity, aliases: ["a", "z"], embedInteropTypes: true)], Cancellation));
        Assert.Equal(global.Digest, explicitGlobal.Digest);
        Assert.Equal(aliased.Digest, reordered.Digest);
        Assert.NotEqual(global.Digest, aliased.Digest);
        Assert.NotEqual(aliased.Digest, embedded.Digest);
        Assert.Equal(["a", "z"], Assert.Single(aliased.References).Properties.Aliases);
        Assert.NotEqual(
            Ready(inventory.Select([new(identity, aliases: ["a", "bc"])], Cancellation)).Digest,
            Ready(inventory.Select([new(identity, aliases: ["ab", "c"])], Cancellation)).Digest);

        Ready(embedded.Use(context =>
        {
            PortableExecutableReference reference = Assert.Single(context.CompilerReferences);
            Assert.Equal(["a", "z"], reference.Properties.Aliases);
            Assert.True(reference.Properties.EmbedInteropTypes);
            Assert.Null(reference.FilePath);
            return true;
        }, Cancellation));
        Rejected(inventory.Select([new(identity), new(identity, aliases: ["other"])], Cancellation),
            CompileReferenceFailureKind.ReferenceRoleConflict);
    }

    [Fact]
    public async Task AliasedReferenceActuallyBindsOnlyThroughItsAlias()
    {
        await using var fixture = await Published.Create(
            SourceBytes(), TargetBytes(), File.ReadAllBytes(typeof(object).Assembly.Location),
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")));
        CompileReferenceInventory inventory = fixture.Discover(1, 2, 3);
        CompileReferenceSet set = Ready(inventory.Select(
            inventory.Candidates.Select(image => new CompileReferenceRequest(image.Identity,
                aliases: ReferenceEquals(image.InventoryId, fixture.Ids[1]) ? ["fixture"] : default)), Cancellation));

        Ready(set.Use(context =>
        {
            var syntax = CSharpSyntaxTree.ParseText(
                "extern alias fixture; public static class Consumer { public static void M() => fixture::Target.Api.Ping(); }",
                cancellationToken: Cancellation);
            var compilation = CSharpCompilation.Create("AliasConsumer", [syntax], context.CompilerReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.Empty(compilation.GetDiagnostics(Cancellation).Where(d => d.Severity == DiagnosticSeverity.Error));
            InvocationExpressionSyntax call = syntax.GetRoot(Cancellation).DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            IMethodSymbol method = Assert.IsAssignableFrom<IMethodSymbol>(
                compilation.GetSemanticModel(syntax).GetSymbolInfo(call, Cancellation).Symbol);
            Assert.Equal("Ping", method.Name);
            Assert.Equal("Target.Api", method.ContainingType.ToDisplayString());
            Assert.Equal(inventory.Candidates[0].Identity.Version, method.ContainingAssembly.Identity.Version);
            Assert.Null(compilation.GetTypeByMetadataName("Target.Api"));
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task SourceIsSeparateAndCannotSelectItself()
    {
        await using var fixture = await Published.Create(SourceBytes(), TargetBytes());
        CompileReferenceInventory inventory = fixture.Discover(0, 1);
        Assert.Contains(inventory.Source, inventory.Candidates);
        Rejected(inventory.Select([new(inventory.Source.Identity)], Cancellation),
            CompileReferenceFailureKind.ReferenceNotFound);
        Rejected(inventory.Select([new(inventory.Source.Identity, inventory.Source.InventoryId)], Cancellation),
            CompileReferenceFailureKind.SourceReferenceExcluded);
        CompileReferenceSet set = Ready(inventory.Select([new(inventory.Candidates[1].Identity)], Cancellation));
        Assert.DoesNotContain(set.References, reference => ReferenceEquals(reference.InventoryId, set.Source.InventoryId));
        Ready(set.Use(context =>
        {
            Assert.Null(context.Resolve(set.Source.Identity, AssemblyResolutionScope.Any));
            var compilation = CSharpCompilation.Create("NoSourceReference", references: context.CompilerReferences);
            Assert.Null(compilation.GetTypeByMetadataName("Sample.Container`1"));
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task SourceAssociationContributesToSetIdentity()
    {
        byte[] source = SourceBytes();
        await using var fixture = await Published.Create(source, TargetBytes(), source);
        CompileReferenceInventory first = fixture.Discover(1);
        CompileReferenceInventory otherSource = Ready(CompileReferenceInventory.Discover(
            fixture.Session, fixture.Lease, fixture.Input(2), [fixture.Input(1)], _ => { }, Cancellation));
        CompileReferenceSet firstSet = Ready(first.Select([new(first.Candidates[0].Identity)], Cancellation));
        CompileReferenceSet secondSet = Ready(otherSource.Select([new(first.Candidates[0].Identity)], Cancellation));
        Assert.Equal(first.Source.ContentDigest.HexValue, otherSource.Source.ContentDigest.HexValue);
        Assert.NotEqual(firstSet.Digest, secondSet.Digest);
    }

    [Fact]
    public async Task SetDigestIdentityIsScopedToItsArtifactGeneration()
    {
        await using var first = await Published.Create(SourceBytes(), TargetBytes());
        await using var second = await Published.Create(SourceBytes(), TargetBytes());
        CompileReferenceInventory left = first.Discover(1);
        CompileReferenceInventory right = second.Discover(1);
        CompileReferenceSet leftSet = Ready(left.Select([new(left.Candidates[0].Identity)], Cancellation));
        CompileReferenceSet rightSet = Ready(right.Select([new(right.Candidates[0].Identity)], Cancellation));
        Assert.Equal(leftSet.Digest.HexValue, rightSet.Digest.HexValue);
        Assert.NotEqual(leftSet.Digest, rightSet.Digest);
        Assert.NotSame(leftSet.Source.InventoryId, rightSet.Source.InventoryId);
    }

    [Fact]
    public async Task DuplicateRegistrationCannotSilentlyReplaceItsProvenance()
    {
        await using var fixture = await Published.Create(SourceBytes(), TargetBytes());
        Rejected(CompileReferenceInventory.Discover(fixture.Session, fixture.Lease, fixture.Input(0),
            [fixture.Input(1), fixture.Input(1) with { Provenance = AssemblyResolutionProvenance.Designated("other") }],
            _ => { }, Cancellation), CompileReferenceFailureKind.ReferenceCandidateConflict);
    }

    [Fact]
    public async Task ExactRequestsDoNotTreatCultureAndTokenAsWildcards()
    {
        byte[] core = File.ReadAllBytes(typeof(object).Assembly.Location);
        await using var fixture = await Published.Create(SourceBytes(), core);
        CompileReferenceInventory inventory = fixture.Discover(1);
        AssemblyReferenceIdentity identity = inventory.Candidates[0].Identity;
        Assert.False(string.IsNullOrEmpty(identity.PublicKeyToken));
        Rejected(inventory.Select([new(identity with { PublicKeyToken = null })], Cancellation),
            CompileReferenceFailureKind.ReferenceNotFound);
        Rejected(inventory.Select([new(identity with { Culture = "fr-FR" })], Cancellation),
            CompileReferenceFailureKind.ReferenceNotFound);
        Rejected(inventory.Select([new(identity with { Version = new Version(1, 0, 0, 0) })], Cancellation),
            CompileReferenceFailureKind.ReferenceNotFound);
        CompileReferenceSet set = Ready(inventory.Select(
            [new(identity with { Name = identity.Name.ToLowerInvariant(), Culture = "neutral" })], Cancellation));
        Assert.False(Assert.Single(set.References).IsPlatformAuthorized);
        Ready(set.Use(context =>
        {
            Assert.NotNull(context.Resolve(identity, AssemblyResolutionScope.Any));
            Assert.Null(context.Resolve(identity with { Version = null }, AssemblyResolutionScope.Any));
            Assert.Null(context.Resolve(identity with { PublicKeyToken = null }, AssemblyResolutionScope.Any));
            Assert.Null(context.Resolve(identity, AssemblyResolutionScope.Platform));
            return true;
        }, Cancellation));
    }

    [Fact]
    public async Task CompileReferenceDigestComesFromRetainedArtifactOwner()
    {
        byte[][] bytes = [SourceBytes(), TargetBytes(), Bytes(FixtureIds.DiffV1)];
        await using var fixture = await Published.Create(bytes);
        var charges = new List<long>();
        CompileReferenceInventory inventory = Ready(CompileReferenceInventory.Discover(
            fixture.Session, fixture.Lease, fixture.Input(0), [fixture.Input(2), fixture.Input(1)],
            charges.Add, Cancellation));
        Assert.Equal(bytes.Select(image => (long)image.Length).Order(), charges.Order());
        foreach (CompileReferenceImage image in inventory.Candidates.Prepend(inventory.Source))
        {
            var digest = Assert.IsType<ArtifactContentAccessOutcome<ArtifactContentDigest>.Accessed>(
                fixture.Session.GetContentDigest(image.InventoryId, fixture.Lease, _ => Assert.Fail("Warm digest charged."), Cancellation));
            Assert.Same(digest.Value, image.ContentDigest);
            Assert.Same(image.InventoryId, image.ContentDigest.Artifact);
            Assert.Same(fixture.Session.Generation, image.ContentDigest.Generation);
            using Stream retained = fixture.Session.GetContentReference(image.InventoryId, fixture.Lease).OpenRead();
            using var copy = new MemoryStream();
            retained.CopyTo(copy);
            Assert.Equal(copy.ToArray(), image.Snapshot.Content);
        }
        CompileReferenceSet selected = Ready(inventory.Select([new(inventory.Candidates[0].Identity)], Cancellation));
        Assert.Single(selected.References);
        Assert.Equal(3, charges.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DigestUnavailabilityPrecedesAllMetadataDescriptorWork(bool unavailableSource)
    {
        // A malformed image would fail descriptor construction if it ran before all digests.
        await using var fixture = await Published.Create([1, 2, 3], TargetBytes());
        await using var foreign = await Published.Create(TargetBytes());
        CompileReferenceInput source = unavailableSource ? foreign.Input(0) : fixture.Input(0);
        CompileReferenceInput[] candidates = unavailableSource ? [fixture.Input(1)] : [fixture.Input(1), foreign.Input(0)];
        CompileReferenceFailure failure = Rejected(CompileReferenceInventory.Discover(
            fixture.Session, fixture.Lease, source, candidates, _ => { }, Cancellation),
            CompileReferenceFailureKind.ReferenceDigestUnavailable);
        Assert.Same(foreign.Ids[0], failure.Artifact);
    }

    [Fact]
    public async Task EveryDigestPrecedesInvalidSourceOrCandidateRejection()
    {
        await using var fixture = await Published.Create([1, 2, 3], TargetBytes());
        var charges = new List<long>();
        Rejected(CompileReferenceInventory.Discover(fixture.Session, fixture.Lease,
            fixture.Input(0), [fixture.Input(1)], charges.Add, Cancellation),
            CompileReferenceFailureKind.ReferenceImageInvalid);
        Assert.Equal([3L, TargetBytes().LongLength], charges);

        await using var invalidCandidate = await Published.Create(SourceBytes(), [1, 2, 3]);
        Rejected(CompileReferenceInventory.Discover(invalidCandidate.Session, invalidCandidate.Lease,
            invalidCandidate.Input(0), [invalidCandidate.Input(1)], _ => { }, Cancellation),
            CompileReferenceFailureKind.ReferenceImageInvalid);
    }

    [Fact]
    public async Task DigestChargeFailureAndCancellationPropagate()
    {
        await using var fixture = await Published.Create(SourceBytes(), TargetBytes());
        var expected = new KeyNotFoundException("caller work accounting failure");
        Assert.Same(expected, Assert.Throws<KeyNotFoundException>(() =>
            CompileReferenceInventory.Discover(fixture.Session, fixture.Lease,
                fixture.Input(0), [fixture.Input(1)], _ => throw expected, Cancellation)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompileReferenceInventory.Discover(
            fixture.Session, fixture.Lease, fixture.Input(0), [fixture.Input(1)], _ => { }, cancelled.Token));
    }

    [Theory]
    [InlineData("disposed-lease")]
    [InlineData("revoked")]
    [InlineData("replaced")]
    [InlineData("disposed-session")]
    public async Task StaleAuthorityRejectsDiscoverySelectionAndConsumption(string mode)
    {
        await using var fixture = await Published.Create(SourceBytes(), TargetBytes());
        CompileReferenceInventory inventory = fixture.Discover(1);
        CompileReferenceRequest[] requests = [new(inventory.Candidates[0].Identity)];
        CompileReferenceSet set = Ready(inventory.Select(requests, Cancellation));
        Assert.Equal(1, Ready(set.Use(context => context.CompilerReferences.Length, Cancellation)));

        switch (mode)
        {
            case "disposed-lease": fixture.Lease.Dispose(); break;
            case "revoked": fixture.Session.Revoke(fixture.Authorization); break;
            case "replaced": fixture.Session.ReplaceQueryAuthorization(fixture.Authorization); break;
            case "disposed-session": await fixture.Session.DisposeAsync(); break;
        }

        Rejected(CompileReferenceInventory.Discover(fixture.Session, fixture.Lease,
            fixture.Input(0), [fixture.Input(1)], _ => Assert.Fail("Unauthorized digest charged."), Cancellation),
            CompileReferenceFailureKind.ReferenceDigestUnavailable);
        Rejected(inventory.Select(requests, Cancellation), CompileReferenceFailureKind.ReferenceAuthorityUnavailable);
        Rejected(set.Use<int>(_ => throw new InvalidOperationException("Must not consume unavailable retained content."), Cancellation),
            CompileReferenceFailureKind.ReferenceAuthorityUnavailable);
    }

    [Fact]
    public async Task CompilerCallbackFailureIsNotAReferenceFailure()
    {
        await using var fixture = await Published.Create(SourceBytes(), TargetBytes());
        CompileReferenceInventory inventory = fixture.Discover(1);
        CompileReferenceSet set = Ready(inventory.Select([new(inventory.Candidates[0].Identity)], Cancellation));
        var expected = new IOException("consumer failure");
        Assert.Same(expected, Assert.Throws<IOException>(() => set.Use<int>(_ => throw expected, Cancellation)));
        Assert.Equal(1, Ready(set.Use(context => context.CompilerReferences.Length, Cancellation)));
    }

    [Fact]
    public async Task CompileReferenceSetBindsMetadataAndCompilerToSameSnapshot()
    {
        string directory = Path.Combine("artifacts", "compile-reference-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "source.dll");
        string referencePath = Path.Combine(directory, "target.dll");
        byte[] source = SourceBytes();
        byte[] reference = TargetBytes();
        File.WriteAllBytes(sourcePath, source);
        File.WriteAllBytes(referencePath, reference);
        int opens = 0;
        try
        {
            await using var fixture = await Published.CreateOpeners(
                () => { opens++; return File.OpenRead(sourcePath); },
                () => { opens++; return File.OpenRead(referencePath); },
                () => File.OpenRead(typeof(object).Assembly.Location),
                () => File.OpenRead(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")));
            CompileReferenceInventory inventory = Ready(CompileReferenceInventory.Discover(
                fixture.Session, fixture.Lease, fixture.Input(0) with { Location = new(TextPolicy.Field, sourcePath) },
                [fixture.Input(1) with { Location = new(TextPolicy.Field, referencePath) }, fixture.Input(2), fixture.Input(3)],
                _ => { }, Cancellation));
            File.WriteAllBytes(referencePath, Bytes(FixtureIds.AnalysisCallerGraphTargetV2));
            File.WriteAllBytes(referencePath, reference);
            CompileReferenceSet set = Ready(inventory.Select(
                inventory.Candidates.Select(image => new CompileReferenceRequest(image.Identity)), Cancellation));
            File.WriteAllBytes(referencePath, Bytes(FixtureIds.AnalysisCallerGraphTargetV2));
            File.WriteAllBytes(sourcePath, Bytes(FixtureIds.DiffV1));

            Ready(set.Use(context =>
            {
                CompileReferenceImage selected = set.References[0].Image;
                ResolvedAssemblyReference assembly = Assert.IsType<ResolvedAssemblyReference>(
                    context.Resolve(selected.Identity, AssemblyResolutionScope.Any));
                Assert.Same(selected.MetadataRegistration, assembly.Registration);
                using Stream stream = assembly.OpenRead();
                using var pe = new PEReader(stream);
                MetadataReader reader = pe.GetMetadataReader();
                Assert.Equal(selected.ModuleVersionId, reader.GetGuid(reader.GetModuleDefinition().Mvid));
                Assert.Equal(selected.Identity, AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
                Assert.Contains(reader.TypeDefinitions, handle =>
                    reader.GetString(reader.GetTypeDefinition(handle).Name) == "Api");
                using Stream sourceStream = context.Source.OpenRead();
                using var sourcePe = new PEReader(sourceStream);
                Assert.Equal(set.Source.Identity, AssemblyReferenceIdentity.FromAssemblyDefinition(sourcePe.GetMetadataReader()));

                MetadataTypeDefinitionName name = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create("Target", ["Api"])).Name;
                TypeResolutionRequest request = TypeResolutionRequest.FromReference(
                    selected.Identity, AssemblyBindingOrigin.FromAssembly(context.Source), AssemblyResolutionScope.Any, name);
                using TypeResolutionContext metadata = TypeResolutionContext.Create(
                    new AssemblyReferenceBindingPolicy(context), [context.Source], [request]);
                var resolution = Assert.IsType<TypeResolutionOutcome.Resolved>(metadata.Resolve(request));
                Assert.Equal(selected.ModuleVersionId, resolution.Definition.Address.ModuleVersionId);
                Assert.Same(selected.MetadataRegistration, resolution.Definition.Assembly.Assembly.Registration);

                var syntax = CSharpSyntaxTree.ParseText(
                    "public static class Consumer { public static void M() => Target.Api.Ping(); }",
                    cancellationToken: Cancellation);
                var compilation = CSharpCompilation.Create("FrozenConsumer", [syntax], context.CompilerReferences,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.Empty(compilation.GetDiagnostics(Cancellation).Where(d => d.Severity == DiagnosticSeverity.Error));
                var compilerMetadata = Assert.IsType<AssemblyMetadata>(context.CompilerReferences[0].GetMetadata());
                Assert.Equal(selected.ModuleVersionId, Assert.Single(compilerMetadata.GetModules()).GetModuleVersionId());
                var invocation = syntax.GetRoot(Cancellation).DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                IMethodSymbol method = Assert.IsAssignableFrom<IMethodSymbol>(
                    compilation.GetSemanticModel(syntax).GetSymbolInfo(invocation, Cancellation).Symbol);
                Assert.Equal("Ping", method.Name);
                Assert.Equal("Target.Api", method.ContainingType.ToDisplayString());
                Assert.Equal(selected.Identity.Version, method.ContainingAssembly.Identity.Version);
                Assert.NotEqual(new Version(2, 0, 0, 0), method.ContainingAssembly.Identity.Version);
                return true;
            }, Cancellation));
            Assert.Equal(2, opens);
            File.Delete(referencePath);
            File.Delete(sourcePath);
            Assert.Equal(3, Ready(set.Use(context => context.CompilerReferences.Length, Cancellation)));
            await fixture.Session.DisposeAsync();
            Rejected(set.Use(context => context.CompilerReferences.Length, Cancellation),
                CompileReferenceFailureKind.ReferenceAuthorityUnavailable);
            Assert.Equal(2, opens);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static T Ready<T>(CompileReferenceResult<T> result) =>
        Assert.IsType<CompileReferenceResult<T>.Ready>(result).Value;

    static CompileReferenceFailure Rejected<T>(CompileReferenceResult<T> result, CompileReferenceFailureKind kind)
    {
        CompileReferenceFailure failure = Assert.IsType<CompileReferenceResult<T>.Rejected>(result).Failure;
        Assert.Equal(kind, failure.Kind);
        return failure;
    }

    sealed record Provenance(int Index) : IArtifactProvenance;

    sealed class Published : IAsyncDisposable
    {
        public ArtifactSetSession Session { get; } = new();
        public ArtifactQueryAuthorization Authorization { get; private set; } = null!;
        public ArtifactQueryLease Lease { get; private set; } = null!;
        public ArtifactIdentity[] Ids { get; private set; } = [];

        public static Task<Published> Create(params byte[][] images) =>
            CreateOpeners([.. images.Select<byte[], Func<Stream>>(image => () => new MemoryStream(image, writable: false))]);

        public static async Task<Published> CreateOpeners(params Func<Stream>[] openers)
        {
            var fixture = new Published();
            try
            {
                for (int index = 0; index < openers.Length; index++)
                {
                    Func<Stream> open = openers[index];
                    await fixture.Session.AddRequiredAcquisitionAsync((scope, _) =>
                        ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                            new ArtifactAcquisitionOutcome.Acquired(
                                [scope.Register(new Provenance(index), _ => open())], ArtifactAcquisitionLeases.None)),
                        cancellationToken: Cancellation);
                }
                Assert.IsType<ArtifactSetPublicationOutcome.Published>(await fixture.Session.SealAsync(Cancellation));
                fixture.Authorization = fixture.Session.CreateQueryAuthorization();
                fixture.Lease = fixture.Session.IssueLease(fixture.Authorization);
                fixture.Ids = [.. fixture.Session.GetCatalog(fixture.Lease).Select(descriptor => descriptor.Identity)];
                return fixture;
            }
            catch
            {
                await fixture.Session.DisposeAsync();
                throw;
            }
        }

        public CompileReferenceInput Input(int index) =>
            new(Ids[index], AssemblyResolutionProvenance.Local("CompileReferenceSetTests"));

        public CompileReferenceInventory Discover(params int[] candidates) => Ready(CompileReferenceInventory.Discover(
            Session, Lease, Input(0), candidates.Select(Input), _ => { }, Cancellation));

        public async ValueTask DisposeAsync()
        {
            Lease.Dispose();
            await Session.DisposeAsync();
        }
    }
}
