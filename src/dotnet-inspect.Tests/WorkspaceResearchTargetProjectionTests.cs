using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using DotnetInspector.Artifacts;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.Research;
using InertText;

using E = DotnetInspector.Queries.WorkspaceMetadataEvidence;
using M = DotnetInspector.Queries.WorkspaceTypeResolutionProjectionManifest;

namespace DotnetInspector.Tests;

public sealed class WorkspaceResearchTargetProjectionTests
{
    [Fact]
    public void WorkspaceResearchTarget_ProjectionManifestInventoryIsExact()
    {
        new WorkspaceProjectionContractAudit().ValidateInventory();
        WorkspaceProjectionContractAudit.EqualSet(
            [typeof(ArtifactAcquisitionRegistration), typeof(AssemblyBindingPolicyVersion),
                typeof(AssemblyBindingLineage), typeof(AssemblyCatalogId),
                typeof(ResolvedTypeDefinitionKey), typeof(UnresolvedBindingReference)],
            M.OpaqueOwnerLeaves, "owner-authorized opaque inputs");
        WorkspaceProjectionContractAudit.EqualSet(
            [typeof(bool), typeof(int), typeof(Guid), typeof(DateTime), typeof(Version),
                typeof(string), typeof(InertString)], M.PermittedValueLeaves, "permitted value leaves");
        WorkspaceProjectionContractAudit.EqualSet(
            [typeof(AssemblyReferenceIdentity), typeof(QueryComparisonOperationId), typeof(QueryComparisonQuestionId),
                typeof(QueryComparisonInputId), typeof(ResearchComparisonOperationId), typeof(ResearchComparisonQuestionId),
                typeof(ResearchComparisonInputId), typeof(ResearchTargetScopeId), typeof(ResearchTargetDomainId),
                typeof(ResearchTargetRequestId), typeof(ResearchTargetAttemptId)],
            M.RetainedOwnerCurrency, "retained owner-issued currency");
        Assert.Equal(34, M.Materializers.Length);
        Assert.Equal(80, M.Materializers.SelectMany(WorkspaceProjectionContractAudit.Flatten).Count());
    }

    [Fact]
    public void WorkspaceResearchTarget_AvailableProjectionPreservesEveryMetadataOutcome()
    {
        var audit = new WorkspaceProjectionContractAudit();
        audit.ValidateInventory();
        WorkspaceProjectionFixture.ExerciseAll(audit);
        WorkspaceProjectionFixture.AssertCoverage(audit);
    }

    [Fact]
    public void WorkspaceResearchTarget_ImageOpenFailureIsUnavailable()
    {
        var audit = new WorkspaceProjectionContractAudit();
        WorkspaceProjectionFixture.ExerciseQueryResults(audit);
        WorkspaceProjectionSchema schema = audit.Sources[typeof(CandidateOpenFailure)];
        Assert.Equal(3, schema.Properties.Length);
        foreach (WorkspaceProjectionProperty property in schema.Properties)
            Assert.NotEmpty(audit.ObservedProperties[(schema.Source, property.Source!)]);
        Assert.Contains(typeof(WorkspaceTypeResolutionEvidence.QueryRejected),
            audit.ObservedTypes.Select(type => audit.Sources[type].Destination));
    }

    [Fact]
    public void WorkspaceResearchTarget_ModuleHashPublishesOnlyLengthAndSha256()
    {
        var audit = new WorkspaceProjectionContractAudit();
        foreach (int variant in new[] { 0, 1 })
        {
            byte[] sentinel = WorkspaceProjectionFixture.SentinelImage(variant);
            (ModuleFileReference source, E.Module destination, object evidence) =
                WorkspaceProjectionFixture.ProjectModuleHash(audit, sentinel, variant);
            Assert.Equal(sentinel.Length, source.Hash.Length);
            Assert.True(sentinel.Length >= 4096);
            Assert.Equal(sentinel, source.Hash);
            Assert.Equal(source.Hash.Length, destination.Hash.ByteLength);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(source.Hash.AsSpan())),
                destination.Hash.Sha256.ToString());
            Assert.Equal(64, destination.Hash.Sha256.ToString().Length);
            var strings = new List<string>();
            var bytes = new List<byte[]>();
            CollectPublishedValues(evidence, strings, bytes);
            Assert.DoesNotContain(bytes, value => value.AsSpan().IndexOf(sentinel) >= 0);
            var sourceBytes = new List<byte[]>();
            CollectPublishedValues(source, [], sourceBytes);
            Assert.Contains(sourceBytes, value => value.AsSpan().SequenceEqual(sentinel));
            string hex = Convert.ToHexString(sentinel);
            string base64 = Convert.ToBase64String(sentinel);
            Assert.All(strings, text =>
            {
                Assert.DoesNotContain(hex, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(base64, text, StringComparison.Ordinal);
                Assert.DoesNotContain(Encoding.Latin1.GetString(sentinel), text, StringComparison.Ordinal);
            });
            // Flatten through the actual runtime arms so JSON cannot silently
            // omit derived properties merely because the public root is abstract.
            foreach (string json in new[]
            {
                JsonSerializer.Serialize(evidence, evidence.GetType()),
                JsonSerializer.Serialize(destination),
                JsonSerializer.Serialize(SerializationShape(evidence)),
            })
            {
                Assert.DoesNotContain(hex, json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(base64, json, StringComparison.Ordinal);
                Assert.DoesNotContain(JsonSerializer.Serialize(Encoding.Latin1.GetString(sentinel)), json);
                Assert.DoesNotContain(JsonSerializer.Serialize(sentinel.Select(value => (int)value)), json);
            }
        }
    }

    [Fact]
    public void WorkspaceResearchTarget_ResultSurfaceRetainsNoCapabilities()
    {
        var walker = new WorkspaceCapabilitySurfaceWalker();
        walker.Visit(typeof(WorkspaceResearchTargetCompositionResult), "result");
        // These are independently contracted roots, not a manually selected arm list.
        foreach (Type root in new[]
        {
            typeof(WorkspaceResearchTargetCompositionReceipt), typeof(WorkspaceTypeResolutionEvidence),
            typeof(WorkspaceResearchTargetAttemptEvidence), typeof(WorkspaceResearchTargetCensusEvidence),
        })
        {
            Assert.Contains(root, walker.Visited);
            walker.Visit(root, root.Name);
        }
        Assert.Contains(typeof(WorkspaceTypeResolutionEvidence.Available), walker.Visited);
        Assert.Contains(typeof(WorkspaceTypeResolutionEvidence.QueryRejected), walker.Visited);
        Assert.Contains(typeof(WorkspaceTypeResolutionEvidence.UnsupportedBindingPolicy), walker.Visited);
        foreach (Type union in walker.Visited.Where(type => type.IsAbstract))
            foreach (Type arm in WorkspaceProjectionContractAudit.Arms(union))
                Assert.Contains(arm, walker.Visited);
        foreach (Type currency in M.RetainedOwnerCurrency)
            Assert.Contains(currency, walker.Visited);
    }

    public static TheoryData<Type, string> ProhibitedCarriers => new()
    {
        { typeof(SnapshotCarrier), nameof(AssemblyImageSnapshot) },
        { typeof(ReaderCarrier), nameof(MetadataReader) },
        { typeof(RegistrationCarrier), nameof(AssemblyAcquisitionRegistration) },
        { typeof(ErasedCarrier), "object" },
        { typeof(InterfaceCarrier), "Interface" },
        { typeof(DelegateCarrier), "Capability" },
        { typeof(StreamCarrier), "Capability" },
        { typeof(DisposableCarrier), "Interface" },
        { typeof(AsyncDisposableCarrier), "Interface" },
        { typeof(NonDisposableImageCarrier), "Byte-bearing" },
        { typeof(RawImageCarrier), "Byte-bearing" },
        { typeof(MemoryCarrier), "Byte-bearing" },
        { typeof(SequenceCarrier), "Byte-bearing" },
        { typeof(ImmutableBytesCarrier), "Byte-bearing" },
        { typeof(ExceptionCarrier), "Capability" },
        { typeof(ResearchAttemptCarrier), "Unlisted" },
        { typeof(ResearchOutcomeCarrier), "Unlisted" },
        { typeof(ResearchCensusCarrier), "Unlisted" },
        { typeof(QueryResultCarrier), "Query result" },
        { typeof(OpenGenericCarrier<>), "Open generic" },
        { typeof(NonSealedCarrier), "Unlisted" },
    };

    [Theory]
    [MemberData(nameof(ProhibitedCarriers))]
    public void WorkspaceResearchTarget_StructuralWalkerRejectsProhibitedCarriers(Type carrier, string reason)
    {
        var walker = new WorkspaceCapabilitySurfaceWalker();
        Exception? failure = Record.Exception(() => walker.VisitMembers(carrier, carrier.Name));
        Assert.NotNull(failure);
        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(NonDisposableImageCarrier)));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(typeof(NonDisposableImageCarrier)));
    }

    [Fact]
    public void WorkspaceResearchTarget_DestinationClaimsRejectUnclaimedEncodedImage()
    {
        byte[] sentinel = WorkspaceProjectionFixture.SentinelImage(7);
        foreach (string encoding in new[] { Convert.ToHexString(sentinel), Convert.ToBase64String(sentinel) })
        {
            var value = new UnclaimedModule(
                new(TextPolicy.Field, "module.netmodule"), true,
                new(sentinel.Length, new(TextPolicy.Field, Convert.ToHexString(SHA256.HashData(sentinel)))),
                new(TextPolicy.Field, encoding));
            Assert.Equal(encoding, value.Unclaimed.ToString());
            var schema = new WorkspaceProjection<ModuleFileReference, UnclaimedModule>(
                "UnclaimedModule", (_, _) => value, M.Module.Properties);
            Exception? failure = Record.Exception(() => WorkspaceProjectionContractAudit.ValidateDestinationClaims(schema));
            Assert.NotNull(failure);
            Assert.Contains(nameof(UnclaimedModule.Unclaimed), failure.Message);
            var hidden = new PrivateUnclaimedModule(value.Name, value.ContainsMetadata, value.Hash, value.Unclaimed);
            Assert.Equal(encoding, hidden.RetainedForProbe.ToString());
            var hiddenSchema = new WorkspaceProjection<ModuleFileReference, PrivateUnclaimedModule>(
                "PrivateUnclaimedModule", (_, _) => hidden, M.Module.Properties);
            Exception? hiddenFailure = Record.Exception(() => WorkspaceProjectionContractAudit.ValidateDestinationClaims(hiddenSchema));
            Assert.NotNull(hiddenFailure);
            Assert.Contains("_unclaimed", hiddenFailure.Message);
            // An extra InertString passes the type allow list; exact destination
            // claims, rather than a coarse capability deny list, must catch it.
            new WorkspaceCapabilitySurfaceWalker().Visit(typeof(InertString), "encoded image");
        }
    }

    [Fact]
    public void WorkspaceResearchTarget_OpenReadIsNeitherReadNorInvokedByProjection()
    {
        WorkspaceProjectionFixture.ExerciseSentinelOpenRead(new WorkspaceProjectionContractAudit());
        MethodInfo getter = typeof(ResolvedAssemblyReference).GetProperty("OpenRead")!.GetMethod!;
        Type[] implementation = typeof(M).Assembly.GetTypes()
            .Where(type => WorkspaceProjectionIl.BelongsTo(type, typeof(M))
                || WorkspaceProjectionIl.BelongsTo(type, typeof(WorkspaceProjectionContext))).ToArray();
        Assert.DoesNotContain(
            implementation.SelectMany(WorkspaceProjectionIl.Methods).SelectMany(WorkspaceProjectionIl.References),
            member => member == getter);
    }

    [Fact]
    public void WorkspaceResearchTarget_SharedProjectionSitesUseTheNamedProjector()
    {
        var audit = new WorkspaceProjectionContractAudit();
        foreach (WorkspaceProjectionSchema schema in audit.Sources.Values.Where(schema => schema.Arms.IsEmpty))
        {
            var references = WorkspaceProjectionIl.MaterializerReferences(schema);
            foreach (WorkspaceProjectionProperty property in schema.Properties.Where(property =>
                property.Disposition is WorkspaceProjectionDisposition.Project or WorkspaceProjectionDisposition.Contain))
            {
                FieldInfo field = typeof(M).GetField(property.Rule, BindingFlags.Static | BindingFlags.NonPublic)!;
                Assert.True(references.Contains(field),
                    $"{schema.Source.FullName}.{property.Source} does not call its declared shared projector {property.Rule}.");
            }
        }
    }

    [Fact]
    public void WorkspaceResearchTarget_BindingPlanArmHasNoMetadataConstructionSite()
    {
        // Metadata currently declares this arm but never issues one. This is an
        // explicit owner reachability exclusion, NOT an owner-produced witness.
        // Any adoption invalidates this assertion and requires new fixtures.
        ConstructorInfo constructor = Assert.Single(
            typeof(ResolutionPlanRequest.Binding).GetConstructors(WorkspaceProjectionContractAudit.DeclaredInstance));
        Assert.DoesNotContain(
            typeof(TypeResolutionContext).Assembly.GetTypes()
                .SelectMany(WorkspaceProjectionIl.Methods).SelectMany(WorkspaceProjectionIl.References),
            member => member == constructor);
        Assert.Contains(M.PlanRequest.Arms, arm => arm.Source == typeof(ResolutionPlanRequest.Binding));
    }

    [Fact]
    public void WorkspaceResearchTarget_FixturesNeverConstructMetadataOutcomeArms()
    {
        var arms = WorkspaceProjectionContractAudit.RequiredUnions.Keys
            .SelectMany(WorkspaceProjectionContractAudit.Arms).ToHashSet();
        Type[] harness = typeof(WorkspaceProjectionFixture).Assembly.GetTypes()
            .Where(type => WorkspaceProjectionIl.BelongsTo(type, typeof(WorkspaceProjectionFixture))
                || WorkspaceProjectionIl.BelongsTo(type, typeof(WorkspaceProjectionContractAudit))).ToArray();
        Assert.DoesNotContain(harness.SelectMany(WorkspaceProjectionIl.Methods)
            .SelectMany(WorkspaceProjectionIl.References),
            member => member is ConstructorInfo && arms.Contains(member.DeclaringType!));
    }

    static void CollectPublishedValues(object? value, List<string> strings, List<byte[]> bytes)
    {
        if (value is null)
            return;
        switch (value)
        {
            case string text:
                strings.Add(text);
                return;
            case InertString text:
                strings.Add(text.ToString());
                return;
            case byte[] raw:
                bytes.Add(raw);
                return;
            case ImmutableArray<byte> raw:
                bytes.Add(raw.ToArray());
                return;
            case System.Collections.IEnumerable sequence:
                foreach (object? element in sequence)
                    CollectPublishedValues(element, strings, bytes);
                return;
        }
        if (value.GetType().IsEnum || value.GetType().IsPrimitive
            || value is Guid or DateTime or Version)
            return;
        foreach (MemberInfo member in WorkspaceProjectionContractAudit.PublishedMembers(value.GetType()))
            CollectPublishedValues(WorkspaceProjectionContractAudit.ReadMember(member, value), strings, bytes);
    }

    static object? SerializationShape(object? value)
    {
        if (value is null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum
            || value is Guid or DateTime or Version)
            return value;
        if (value is InertString text)
            return text.ToString();
        if (value is System.Collections.IEnumerable sequence)
            return sequence.Cast<object?>().Select(SerializationShape).ToArray();
        return WorkspaceProjectionContractAudit.PublishedMembers(value.GetType())
            .ToDictionary(member => member.Name,
                member => SerializationShape(WorkspaceProjectionContractAudit.ReadMember(member, value)));
    }

    sealed record UnclaimedModule(InertString Name, bool ContainsMetadata, E.HashSummary Hash, InertString Unclaimed);
    sealed class PrivateUnclaimedModule
    {
        readonly InertString _unclaimed;
        internal PrivateUnclaimedModule(InertString name, bool containsMetadata, E.HashSummary hash, InertString unclaimed)
        {
            Name = name;
            ContainsMetadata = containsMetadata;
            Hash = hash;
            _unclaimed = unclaimed;
        }
        public InertString Name { get; }
        public bool ContainsMetadata { get; }
        public E.HashSummary Hash { get; }
        internal InertString RetainedForProbe => _unclaimed;
    }
    sealed class SnapshotCarrier { public AssemblyImageSnapshot Value { get; } = null!; }
    sealed class ReaderCarrier { public MetadataReader Value { get; } = null!; }
    sealed class RegistrationCarrier { public AssemblyAcquisitionRegistration Value { get; } = null!; }
    sealed class ErasedCarrier { readonly object _value = new(); }
    sealed class InterfaceCarrier { public IReadOnlyList<string> Value { get; } = []; }
    sealed class DelegateCarrier { public Func<Stream> Value { get; } = null!; }
    sealed class StreamCarrier { public MemoryStream Value { get; } = null!; }
    sealed class DisposableCarrier { public IDisposable Value { get; } = null!; }
    sealed class AsyncDisposableCarrier { public IAsyncDisposable Value { get; } = null!; }
    sealed class NonDisposableImageCarrier { public ImmutableArray<byte> Image { get; } = []; }
    sealed class RawImageCarrier { readonly byte[] _image = []; }
    sealed class MemoryCarrier { public ReadOnlyMemory<byte> Value { get; } }
    sealed class SequenceCarrier { public ReadOnlySequence<byte> Value { get; } }
    sealed class ImmutableBytesCarrier { public ImmutableList<byte> Value { get; } = []; }
    sealed class ExceptionCarrier { public Exception Value { get; } = null!; }
    sealed class ResearchAttemptCarrier { public ResearchTargetAttempt Value { get; } = null!; }
    sealed class ResearchOutcomeCarrier { public ResearchTargetOutcome Value { get; } = null!; }
    sealed class ResearchCensusCarrier { public ResearchTargetDomainSideCensus Value { get; } = null!; }
    sealed class QueryResultCarrier { public AssemblyContextTypeResolutionResult Value { get; } = null!; }
    sealed class OpenGenericCarrier<T> { public T Value { get; } = default!; }
    sealed class NonSealedCarrier { public NonSealedValue Value { get; } = null!; }
    class NonSealedValue;
}
