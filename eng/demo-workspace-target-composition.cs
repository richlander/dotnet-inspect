#:project ../src/DotnetInspector.ResearchQueries/DotnetInspector.ResearchQueries.csproj
#:property Configuration=Release
#:property EnablePreviewFeatures=true

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;

Run("Forwarded both sides", includeAfterImplementation: true, divergent: false);
Run("Missing participant", includeAfterImplementation: false, divergent: false);
Run("Divergent domains", includeAfterImplementation: true, divergent: true);

static void Run(string scenario, bool includeAfterImplementation, bool divergent)
{
    Console.WriteLine($"=== {scenario} ===");
    using var before = new DemoContext("before", "ContractsImplementation", includeImplementation: true);
    using var after = new DemoContext("after",
        divergent ? "ReplacementImplementation" : "ContractsImplementation", includeAfterImplementation);
    var sealedPopulation = Require<QueryPopulationSealingOutcome.Sealed>(
        QueryComparisonPopulationSealer.Execute(new ImplementationComparisonPopulationRequest(
            before.Bindings, after.Bindings)));
    var population = Require<QueryComparisonPopulation<ImplementationComparisonBinding>>(sealedPopulation.Population);
    var type = Require<MetadataTypeDefinitionNameResult.Valid>(
        MetadataTypeDefinitionName.Create("N", ["Type"])).Name;
    var plan = Require<WorkspaceResearchTargetPlanningOutcome.Planned>(
        WorkspaceResearchTargetPlanningQuery.Execute(population, type, MemberTargetSelector.Parse("Value"))).Plan;

    var beforeResult = Compose(plan, before, QueryComparisonSide.Before);
    var afterResult = Compose(plan, after, QueryComparisonSide.After);
    var beforeReceipt = Require<WorkspaceResearchTargetCompositionResult.Composed>(beforeResult).Receipt;
    Print("Before", beforeReceipt, population);
    Console.WriteLine();

    if (!includeAfterImplementation)
    {
        var unavailable = Require<WorkspaceResearchTargetCompositionResult.Unavailable>(afterResult);
        var metadata = Require<WorkspaceTypeResolutionEvidence.Available>(unavailable.Evidence);
        var unbound = Require<WorkspaceMetadataEvidence.Outcome.UnboundBinding>(metadata.Outcome);
        var rootAttempt = Require<WorkspaceResearchTargetAttemptEvidence.Unavailable>(unavailable.RootAttempt);
        Console.WriteLine($"After root: {after.Root.Assembly.Identity.Name}");
        Console.WriteLine($"After local attempt: {rootAttempt.Kind} / {rootAttempt.Diagnostic}");
        Console.WriteLine($"After route: {Route(unbound)}");
        Console.WriteLine("After composition: Unavailable / UnboundBinding");
        Console.WriteLine("Effective target: none");
    }
    else
    {
        var afterReceipt = Require<WorkspaceResearchTargetCompositionResult.Composed>(afterResult).Receipt;
        Print("After", afterReceipt, population);
        Console.WriteLine();
        var beforeCorrespondence = plan.Resolution.Correspondences.Single(
            item => ReferenceEquals(item.DomainId, beforeReceipt.Domain));
        var afterCorrespondence = plan.Resolution.Correspondences.Single(
            item => ReferenceEquals(item.DomainId, afterReceipt.Domain));
        Console.WriteLine($"Before effective domain: {beforeCorrespondence.Domain.Key.Identity.Name}");
        Console.WriteLine($"After effective domain: {afterCorrespondence.Domain.Key.Identity.Name}");
        Console.WriteLine($"Before-domain correspondence: {beforeCorrespondence.Kind}");
        Console.WriteLine($"After-domain correspondence: {afterCorrespondence.Kind}");

        var pair = plan.Resolution.Correspondences.OfType<ResearchTargetCorrespondenceOutcome.Paired>()
            .SingleOrDefault(item =>
                ReferenceEquals(item.Before.Attempt.Id, beforeReceipt.EffectiveAttemptId)
                && ReferenceEquals(item.After.Attempt.Id, afterReceipt.EffectiveAttemptId));
        var handoffs = new[] { beforeCorrespondence, afterCorrespondence }.Distinct()
            .Select(item => WorkspaceResearchTargetHandoff.Execute(
                beforeReceipt, afterReceipt, plan.Resolution, item)).ToArray();
        if (divergent)
        {
            Require<ResearchTargetCorrespondenceOutcome.BeforeOnly>(beforeCorrespondence);
            Require<ResearchTargetCorrespondenceOutcome.AfterOnly>(afterCorrespondence);
            foreach (var handoff in handoffs)
                Require<WorkspaceResearchTargetHandoffResult.Unavailable>(handoff);
        }
        else
        {
            Require<ResearchTargetCorrespondenceOutcome.Paired>(pair);
            Require<WorkspaceResearchTargetHandoffResult.Paired>(handoffs.Single());
        }

        Console.WriteLine($"Effective-attempt pair: {(pair is null ? "none" : "Paired")}");
        Console.WriteLine($"Comparison work item: {(handoffs.Any(item => item is WorkspaceResearchTargetHandoffResult.Paired) ? "Paired" : "none")}");
    }

    Console.WriteLine($"Supplemental acquisition requests: {before.Policy.AcquisitionRequests + after.Policy.AcquisitionRequests}");
    Console.WriteLine();
}

static WorkspaceResearchTargetCompositionResult Compose(
    WorkspaceResearchTargetPlan plan, DemoContext context, QueryComparisonSide side)
{
    ResearchComparisonSide researchSide = side == QueryComparisonSide.Before
        ? ResearchComparisonSide.Before : ResearchComparisonSide.After;
    var attempts = plan.Resolution.Attempts.Where(item => item.Request.Side == researchSide).ToArray();
    // Each fixture side has exactly one resolved implementation, or only its unavailable facade.
    var selected = attempts.SingleOrDefault(item => item.Outcome is ResearchTargetOutcome.Resolved)
        ?? attempts.Single();
    var domain = plan.Scope.Domains.Single(item => ReferenceEquals(item.Id, selected.Request.Domain));
    var census = plan.Resolution.Censuses.Single(item =>
        ReferenceEquals(item.Domain, domain) && item.Side == researchSide);
    return plan.Compose(context.Group, context.Root, side, domain, census, AssemblyResolutionScope.Any);
}

static void Print(
    string side, WorkspaceResearchTargetCompositionReceipt receipt,
    QueryComparisonPopulation<ImplementationComparisonBinding> population)
{
    var root = population.Inputs.Single(item => ReferenceEquals(item.Id, receipt.RootInput));
    var terminal = population.Inputs.Single(item => ReferenceEquals(item.Id, receipt.TerminalInput));
    var local = Require<WorkspaceResearchTargetAttemptEvidence.Unavailable>(receipt.RootAttempt);
    var address = receipt.EffectiveAttempt.Address
        ?? throw new InvalidOperationException("The effective method has no durable address.");
    Console.WriteLine($"{side} root: {root.Binding.Assembly.Identity.Name}");
    Console.WriteLine($"{side} local attempt: {local.Kind} / {local.Diagnostic}");
    Console.WriteLine($"{side} route: {Route(receipt.Evidence.Outcome)}");
    Console.WriteLine($"{side} effective target: {terminal.Binding.Assembly.Identity.Name}");
    Console.WriteLine($"{side} address: {address.ModuleVersionId:D}:0x{address.Token:X8}");
}

static string Route(WorkspaceMetadataEvidence.Outcome outcome) =>
    string.Join(" -> ", outcome.Hops.Select(hop => hop.SourceAssembly.Assembly.Identity.Name)
        .Append(outcome.Hops.Last().TargetReference.Name));

static T Require<T>(object? value) where T : class =>
    value as T ?? throw new InvalidOperationException(
        $"Expected {typeof(T).FullName}, received {value?.GetType().FullName ?? "null"}.");

sealed class DemoContext : IDisposable
{
    readonly InspectionWorkspace workspace = new();

    public DemoContext(string side, string implementationName, bool includeImplementation)
    {
        Policy = new ClosedPopulationPolicy();
        var images = new List<byte[]>
        {
            BuildAssembly("ContractsFacade", side, implementationName),
        };
        // An omitted participant has neither an image nor an opener to accidentally acquire.
        if (includeImplementation)
            images.Add(BuildAssembly(implementationName, side));
        var participants = new List<AssemblyContextParticipant>();
        var bindings = new List<ImplementationComparisonBinding>();
        foreach (byte[] image in images)
        {
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            AssemblyReferenceIdentity identity = AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader());
            var assembly = ResolvedAssemblyReference.Create(identity, null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local("in-memory workspace composition demo"));
            participants.Add(new(assembly, Policy));
            // This label is presentation only; the index consumes the supplied immutable image.
            var index = LibraryBodyIndex.OpenFromPrefetchedImage(
                identity.Name + ".dll", [.. image], LibraryBodyAnalysisFeatures.MethodEvidence);
            bindings.Add(new(assembly, Policy, index));
        }
        Bindings = bindings.ToArray();
        Root = participants[0];
        Group = workspace.CreateAssemblyContextGroup(participants);
    }

    public ClosedPopulationPolicy Policy { get; }
    public AssemblyContextGroup Group { get; }
    public AssemblyContextParticipant Root { get; }
    public ImplementationComparisonBinding[] Bindings { get; }
    public void Dispose() => workspace.Dispose();

    static byte[] BuildAssembly(string name, string side, string? forwardsTo = null)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{side}:{name}:{forwardsTo}"));
        var mvid = new Guid(digest.AsSpan(0, 16));
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(mvid), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0),
            default, default, default, default);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var bodies = new BlobBuilder();
        if (forwardsTo is null)
        {
            metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature().Parameters(0,
                result => result.Type().Int32(), _ => { });
            var instructions = new BlobBuilder();
            var encoder = new InstructionEncoder(instructions);
            encoder.LoadConstantI4(side == "before" ? 42 : 43);
            encoder.OpCode(ILOpCode.Ret);
            int body = new MethodBodyStreamEncoder(bodies).AddMethodBody(encoder);
            metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL, metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(signature), body, MetadataTokens.ParameterHandle(1));
        }
        else
        {
            var reference = metadata.AddAssemblyReference(metadata.GetOrAddString(forwardsTo),
                new Version(1, 0, 0, 0), default, default, default, default);
            metadata.AddExportedType(TypeAttributes.Public | (TypeAttributes)0x00200000,
                metadata.GetOrAddString("N"), metadata.GetOrAddString("Type"), reference, 0);
        }
        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata), bodies, flags: CorFlags.ILOnly,
            deterministicIdProvider: _ => new BlobContentId(mvid, 0));
        var output = new BlobBuilder();
        builder.Serialize(output);
        return output.ToArray();
    }
}

sealed class ClosedPopulationPolicy : IAssemblyBindingPolicy, IAssemblyReferenceResolver
{
    public AssemblyBindingPolicyVersion Version { get; } = new();
    public int AcquisitionRequests { get; private set; }

    // The workspace's retained participant catalog is the only binding source.
    public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
        new(Version, request.Target is AssemblyBindingTarget.AssemblyReference
            ? AssemblyBindingSelection.NotFound()
            : AssemblyBindingSelection.CannotSelect(new(AssemblyBindingFailureKind.UnsupportedScope)));

    public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity reference, AssemblyResolutionScope scope)
    {
        AcquisitionRequests++;
        throw new InvalidOperationException("The demo forbids supplemental acquisition.");
    }
}
