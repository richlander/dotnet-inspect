using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

internal sealed class WorkspaceResearchTargetFixture : IDisposable
{
    readonly InspectionWorkspace _workspace = new();
    readonly ResearchPublicationBindingPolicy _binding = new();

    internal WorkspaceResearchTargetFixture(params byte[][] images)
    {
        AssemblyBindingPolicyVersion version = _binding.Version;
        Nodes = images.Select((image, index) =>
            new ImageNode(image, new ProbePolicy(_binding, version, index))).ToArray();
        Group = CreateGroup(Enumerable.Range(0, Nodes.Length));
    }

    internal ImageNode[] Nodes { get; }
    internal AssemblyContextGroup Group { get; }
    internal static MetadataTypeDefinitionName TypeName =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Type"])).Name;
    internal static MemberTargetSelector Selector => MemberTargetSelector.Parse("Value");

    internal AssemblyContextGroup CreateGroup(IEnumerable<int> members) =>
        _workspace.CreateAssemblyContextGroup(members.Select(index => Nodes[index].Participant));

    internal AssemblyContextGroup CreateGroup(IEnumerable<AssemblyContextParticipant> participants) =>
        _workspace.CreateAssemblyContextGroup(participants);

    internal QueryComparisonPopulation<ImplementationComparisonBinding> Population(
        int[]? before = null, int[]? after = null)
    {
        return Assert.IsType<QueryComparisonPopulation<ImplementationComparisonBinding>>(
            Assert.IsType<QueryPopulationSealingOutcome.Sealed>(
                QueryComparisonPopulationSealer.Execute(
                    new ImplementationComparisonPopulationRequest(
                        (before ?? Enumerable.Range(0, Nodes.Length).ToArray()).Select(Binding).ToArray(),
                        (after ?? []).Select(Binding).ToArray(), null, null))).Population);
    }

    internal ImplementationComparisonBinding Binding(int index)
    {
        ImageNode node = Nodes[index];
        var body = LibraryBodyIndex.OpenFromPrefetchedImage(
            node.Assembly.Identity.Name + ".dll", [.. node.Image],
            LibraryBodyAnalysisFeatures.MethodEvidence);
        return new(node.Assembly, new NoAcquisitionResolver(), body);
    }

    internal WorkspaceResearchTargetPlan PublicPlan(
        QueryComparisonPopulation<ImplementationComparisonBinding>? population = null,
        MemberTargetSelector? selector = null) =>
        Assert.IsType<WorkspaceResearchTargetPlanningOutcome.Planned>(
            WorkspaceResearchTargetPlanningQuery.Execute(population ?? Population(), TypeName, selector ?? Selector)).Plan;

    internal PlanningSession Plan(
        QueryComparisonPopulation<ImplementationComparisonBinding>? population = null,
        int referenceOnly = -1, int selections = 1, bool exact = false,
        MemberTargetSelector? selector = null)
    {
        population ??= Population();
        var projected = Assert.IsType<QueryPopulationProjectionOutcome.Projected>(
            QueryPopulationProjection.Execute(population)).Population;
        ResearchComparisonQuestionId question = projected.Receipt.Questions[population.Question];
        selector ??= Selector;
        ResearchMemberSelectionOccurrence[] selection;
        if (exact)
        {
            using var pe = new PEReader(new MemoryStream(Nodes[0].Image, writable: false));
            MetadataMethodAddress address = MetadataMethodAddress.Create(
                pe.GetMetadataReader(), MetadataTokens.MethodDefinitionHandle(1));
            selection = [new ResearchExactAddressMemberSelection(
                question, projected.Admission.Inputs[0], TypeName, selector, address,
                ResearchTargetRelationshipRole.Method)];
        }
        else
        {
            selection = Enumerable.Range(0, selections).Select(_ =>
                (ResearchMemberSelectionOccurrence)new ResearchCarriedMemberSelection(
                    question, TypeName, selector)).ToArray();
        }
        var resolution = Assert.IsType<ResearchTargetPlanningOutcome.Planned>(
            ResearchTargetResolver.Resolve(new(
                projected.Admission,
                projected.Admission.Inputs.Select((input, index) =>
                    new ResearchTargetInputRoleAssignment(input, index == referenceOnly
                        ? ResearchTargetInputRole.ReferenceOnly
                        : ResearchTargetInputRole.Implementation)),
                selection))).Resolution;
        return new(population, projected, resolution);
    }

    internal void RetainAll()
    {
        foreach (ImageNode node in Nodes)
            Assert.IsType<AssemblyImageAccessResult<int>.Available>(
                Group.UseAssemblyImage(node.Assembly, image => image.Content.Length));
    }

    internal void ResetProbes()
    {
        foreach (ImageNode node in Nodes)
            node.Policy.Reset();
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _binding.Dispose();
    }

    internal sealed record PlanningSession(
        QueryComparisonPopulation<ImplementationComparisonBinding> Population,
        ProjectedQueryPopulation Projected,
        ResearchTargetResolution Resolution)
    {
        internal ResearchTargetScope Scope => Resolution.Scopes[0];

        internal WorkspaceResearchTargetCompositionRequest Request(
            WorkspaceResearchTargetFixture fixture, int terminal = 0, int root = 0,
            QueryComparisonSide side = QueryComparisonSide.Before,
            AssemblyContextGroup? group = null,
            QueryComparisonPopulation<ImplementationComparisonBinding>? population = null,
            ProjectedQueryPopulation? projected = null,
            ResearchTargetResolution? resolution = null,
            ResearchTargetScope? scope = null,
            ResearchTargetDomain? domain = null,
            ResearchTargetDomainSideCensus? census = null,
            AssemblyResolutionScope resolutionScope = AssemblyResolutionScope.Any)
        {
            scope ??= Scope;
            QueryComparisonInputId input = (side == QueryComparisonSide.Before
                ? Population.Before : Population.After)[terminal].Id;
            ResearchComparisonInputId research = Projected.Receipt.Inputs[input].Research;
            domain ??= scope.Domains.Single(candidate =>
                candidate.Inputs.Any(item => ReferenceEquals(item.Input, research)));
            census ??= Resolution.Censuses.Single(candidate =>
                ReferenceEquals(candidate.Domain, domain)
                && candidate.Side == QueryPopulationProjection.ResearchSide(side));
            return new(group ?? fixture.Group, fixture.Nodes[root].Participant,
                (group ?? fixture.Group).BindingPolicyVersion, population ?? Population,
                projected ?? Projected, resolution ?? Resolution, (population ?? Population).Question,
                side, scope, TypeName, domain, census, resolutionScope);
        }
    }

    internal sealed class ImageNode
    {
        internal ImageNode(byte[] image, ProbePolicy policy)
        {
            Image = image;
            Policy = policy;
            Assembly = Descriptor(image, () =>
            {
                Opens++;
                OnOpen?.Invoke();
                return new MemoryStream(Image, writable: false);
            });
            Participant = new(Assembly, policy);
        }

        internal byte[] Image { get; set; }
        internal ProbePolicy Policy { get; }
        internal ResolvedAssemblyReference Assembly { get; }
        internal AssemblyContextParticipant Participant { get; }
        internal int Opens { get; private set; }
        internal Action? OnOpen { get; set; }
    }

    internal sealed class ProbePolicy(
        ResearchPublicationBindingPolicy owner,
        AssemblyBindingPolicyVersion captured,
        int participant) : IAcquisitionFreeAssemblyBindingPolicy
    {
        internal int Reads { get; private set; }
        internal int Selections { get; private set; }
        internal int? DriftOnRead { get; set; }
        internal Action<int>? OnVersion { get; set; }
        internal Action? OnSelect { get; set; }
        internal ResolvedAssemblyReference? Target { get; set; }
        internal Func<AssemblyBindingRequest, AssemblyBindingSelection>? SelectOverride { get; set; }
        internal List<string>? Trace { get; set; }

        public AssemblyBindingPolicyVersion Version
        {
            get
            {
                Reads++;
                Trace?.Add($"version:{participant}");
                OnVersion?.Invoke(Reads);
                // The mismatch is deliberately transient: later reads recover the captured
                // token, so the query must latch the first mismatch rather than retry it.
                return Reads == DriftOnRead ? new AssemblyBindingPolicyVersion() : owner.Version;
            }
        }

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            Selections++;
            Trace?.Add($"select:{participant}");
            OnSelect?.Invoke();
            return new(captured, SelectOverride?.Invoke(request) ?? (Target is { } target
                ? AssemblyBindingSelection.Found(target)
                : AssemblyBindingSelection.CannotSelect(new(
                    AssemblyBindingFailureKind.CandidateUnavailable))));
        }

        internal void Reset()
        {
            Reads = 0;
            Selections = 0;
            DriftOnRead = null;
            OnVersion = null;
            OnSelect = null;
            Trace = null;
        }
    }

    internal sealed class UnattestedPolicy(IAssemblyBindingPolicy inner) : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version => inner.Version;
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
            throw new InvalidOperationException("An unattested policy must not be invoked.");
    }

    sealed class NoAcquisitionResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity reference, AssemblyResolutionScope scope) => null;
    }

    internal static ResolvedAssemblyReference Descriptor(byte[] image, Func<Stream>? open = null) =>
        ResolvedAssemblyReference.Create(
            Identity(image), null, open ?? (() => new MemoryStream(image, writable: false)),
            AssemblyResolutionProvenance.Local("workspace target gate"));

    internal static AssemblyReferenceIdentity Identity(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader());
    }

    // The Metadata tests' SRM fixture pattern, extended with a real MethodDef and IL body.
    internal static byte[] BuildAssembly(
        string name, bool definesType = true, AssemblyReferenceIdentity? forwardsTo = null,
        Guid? mvid = null, bool leadingType = false, string methodName = "Value", Version? version = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(mvid ?? Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), version ?? new Version(1, 0, 0, 0),
            default, default, default, default);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        if (leadingType)
            metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Other"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var bodies = new BlobBuilder();
        if (definesType)
        {
            metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature().Parameters(0,
                result => result.Type().Int32(), _ => { });
            var instructions = new BlobBuilder();
            var encoder = new InstructionEncoder(instructions);
            encoder.LoadConstantI4(42);
            encoder.OpCode(ILOpCode.Ret);
            int body = new MethodBodyStreamEncoder(bodies).AddMethodBody(encoder);
            metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL, metadata.GetOrAddString(methodName),
                metadata.GetOrAddBlob(signature), body, MetadataTokens.ParameterHandle(1));
        }
        if (forwardsTo is not null)
        {
            AssemblyReferenceHandle reference = metadata.AddAssemblyReference(
                metadata.GetOrAddString(forwardsTo.Name), forwardsTo.Version!, default, default, default, default);
            metadata.AddExportedType(TypeAttributes.Public | (TypeAttributes)0x00200000,
                metadata.GetOrAddString("N"), metadata.GetOrAddString("Type"), reference, 0);
        }
        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata), bodies, flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
