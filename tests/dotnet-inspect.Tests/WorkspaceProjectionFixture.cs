using System.Buffers.Binary;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Inspector.Artifacts;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;

using E = DotnetInspector.Queries.WorkspaceMetadataEvidence;
using M = DotnetInspector.Queries.WorkspaceTypeResolutionProjectionManifest;

namespace DotnetInspector.Tests;

// SRM fixtures follow TypeResolutionContextTests' owner-driven scenarios. No
// TypeResolutionOutcome, TypeResolutionFailure, or ambiguity arm is constructed here.
internal static class WorkspaceProjectionFixture
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
    static readonly byte[] BodyIndexImage = Image("SealingOnly", metadata => Define(metadata));
    static readonly LibraryBodyIndex BodyIndex = LibraryBodyIndex.OpenFromPrefetchedImage(
        "SealingOnly.dll", [.. BodyIndexImage], LibraryBodyAnalysisFeatures.None);

    internal static void ExerciseAll(WorkspaceProjectionContractAudit audit)
    {
        for (int variant = 0; variant < 2; variant++)
        {
            foreach (AssemblyResolutionScope scope in Enum.GetValues<AssemblyResolutionScope>())
            {
                ExerciseOutcomes(audit, variant, scope);
                ExercisePlansAndLineages(audit, variant, scope);
            }
            ExerciseDefinitionsAndDeclarations(audit, variant);
            ExerciseFailures(audit, variant);
            ExerciseKindDependencies(audit, variant);
            ExerciseProvenanceAndAcquisitions(audit, variant);
            ExerciseIdentityValues(audit, variant);
            ProjectModuleHash(audit, SentinelImage(variant), variant);
        }
        ExerciseQueryResults(audit);
        ExerciseSentinelOpenRead(audit);
        ExerciseOrderedCollections(audit);
    }

    static void ExerciseOutcomes(WorkspaceProjectionContractAudit audit, int variant, AssemblyResolutionScope scope)
    {
        foreach (bool forwarded in new[] { false, true })
        foreach (string ending in new[] { "resolved", "missing", "unbound", "unavailable", "binding", "declaration", "module" })
        {
            string suffix = $"{ending}-{variant}-{scope}-{forwarded}";
            byte[] endImage = Image($"End-{suffix}", metadata =>
            {
                if (ending is "resolved" or "binding" or "declaration")
                    Define(metadata);
                if (ending == "declaration")
                    Define(metadata, kind: MetadataTypeDefinitionKind.Interface);
                if (ending == "module")
                    Module(metadata, $"part-{variant}.netmodule", variant != 0, [1, 2, (byte)(3 + variant)]);
            });
            var end = Descriptor(endImage, variant: variant);
            var other = Descriptor(endImage, variant: 1 - variant);
            var bridge = Descriptor(Image($"Bridge-{suffix}", metadata =>
                Forward(metadata, end.Identity, count: 2)), variant: 1 - variant);
            var root = Descriptor(Image($"Root-{suffix}", metadata =>
                Forward(metadata, bridge.Identity, count: 1)), variant: variant);
            var policy = new Policy(request =>
            {
                if (request.Target is AssemblyBindingTarget.AssemblyReference { Identity: var identity }
                    && identity == bridge.Identity)
                    return AssemblyBindingSelection.Found(bridge);
                return ending switch
                {
                    "unbound" => AssemblyBindingSelection.NotFound(),
                    "unavailable" => AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(variant == 0
                            ? AssemblyBindingFailureKind.UnsupportedScope : AssemblyBindingFailureKind.CandidateUnavailable)),
                    "binding" => AssemblyBindingSelection.Multiple(variant == 0 ? [end, other] : [other, end]),
                    _ => AssemblyBindingSelection.Found(end),
                };
            });
            TypeResolutionRequest request = forwarded
                ? TypeResolutionRequest.FromAssembly(root, scope, Name())
                : ending is "unbound" or "unavailable" or "binding"
                    ? TypeResolutionRequest.FromReference(end.Identity,
                        variant == 0 ? AssemblyBindingOrigin.Global() : AssemblyBindingOrigin.FromAssembly(root), scope, Name())
                    : TypeResolutionRequest.FromAssembly(end, scope, Name());
            E.Outcome result = Run(audit, policy, [root, bridge, end, other],
                forwarded || ending is "unbound" or "unavailable" or "binding" ? [root] : [end], request);
            Assert.Equal(forwarded ? 2 : 0, result.Hops.Length);
            Assert.Equal(ending switch
            {
                "resolved" => typeof(E.Outcome.Resolved),
                "missing" => typeof(E.Outcome.NotFound),
                "unbound" => typeof(E.Outcome.UnboundBinding),
                "unavailable" => typeof(E.Outcome.Unavailable),
                "binding" or "declaration" => typeof(E.Outcome.Ambiguous),
                _ => typeof(E.Outcome.Rejected),
            }, result.GetType());
        }
    }

    static void ExerciseDefinitionsAndDeclarations(WorkspaceProjectionContractAudit audit, int variant)
    {
        ImmutableArray<string> segments = variant == 0 ? ["Outer", "Inner"] : ["Inner", "Outer"];
        string ns = variant == 0 ? "N" : "";
        foreach (MetadataTypeDefinitionKind kind in Enum.GetValues<MetadataTypeDefinitionKind>())
        {
            var assembly = Descriptor(Image($"Kind-{kind}-{variant}", metadata =>
                Define(metadata, ns, segments, kind, coreRoot: variant != 0)), variant: variant);
            var resolved = Assert.IsType<E.Outcome.Resolved>(Run(audit, new Policy(), [assembly], [assembly],
                TypeResolutionRequest.FromAssembly(assembly, AssemblyResolutionScope.Any, Name(ns, segments))));
            Assert.Equal(kind, resolved.Definition.Kind);
            Assert.Equal(variant != 0, resolved.Definition.DeclaringAssemblyDefinesCoreLibraryRoot);

            var competing = Descriptor(Image($"Competing-{kind}-{variant}", metadata =>
            {
                Define(metadata, ns, segments, kind, coreRoot: variant != 0);
                Define(metadata, ns, segments, kind == MetadataTypeDefinitionKind.Interface
                    ? MetadataTypeDefinitionKind.Class : MetadataTypeDefinitionKind.Interface);
                if (variant == 0)
                {
                    Forward(metadata, Identity("CandidateTarget-0"), ns, segments, count: 2);
                    Module(metadata, "first.netmodule", true, [], ns, segments);
                }
                else
                {
                    Module(metadata, "second.netmodule", false, [9, 7, 3], ns, segments);
                    Forward(metadata, Identity("CandidateTarget-1"), ns, segments, count: 2);
                }
            }), variant: variant);
            var ambiguous = Assert.IsType<E.Outcome.Ambiguous>(Run(audit, new Policy(), [competing], [competing],
                TypeResolutionRequest.FromAssembly(competing, AssemblyResolutionScope.Any, Name(ns, segments))));
            var declaration = Assert.IsType<E.Ambiguity.TypeDeclaration>(ambiguous.Ambiguity);
            Assert.Equal(4, declaration.Candidates.Length);
        }
    }

    static void ExercisePlansAndLineages(WorkspaceProjectionContractAudit audit, int variant, AssemblyResolutionScope scope)
    {
        var assembly = Descriptor(Image($"Plan-{variant}-{scope}", metadata => Define(metadata)), variant: variant);
        var policy = new Policy();
        var inputs = Inputs([assembly]);
        var projection = new WorkspaceProjectionContext(inputs);
        using TypeResolutionContext context = TypeResolutionContext.Create(policy, [assembly], []);
        MetadataTypeDefinitionName type = Name($"PlanNamespace-{variant}", [$"PlanType-{variant}-{scope}"]);
        AssemblyBindingOccurrence[] occurrences =
        [
            AssemblyBindingOccurrence.Seed(assembly),
            new ProbeLineage(policy.Version, variant).Select(assembly),
            new ProbeLineage(policy.Version, variant).Select(assembly),
            new ProbeLineage(policy.Version, variant + 10).Select(assembly),
        ];
        Assert.NotSame(occurrences[1].Lineage, occurrences[2].Lineage);
        Assert.Equal(occurrences[1].Lineage, occurrences[2].Lineage);
        Assert.NotEqual(occurrences[1].Lineage, occurrences[3].Lineage);
        var requests = new List<TypeResolutionRequest>
        {
            TypeResolutionRequest.FromAssembly(assembly, scope, type),
            TypeResolutionRequest.FromReference(Identity($"Unplanned-{variant}"), AssemblyBindingOrigin.Global(), scope, type),
            TypeResolutionRequest.FromReference(Identity($"Relative-{variant}"),
                AssemblyBindingOrigin.FromAssembly(assembly), scope, type),
            TypeResolutionRequest.FromCoreLibrary(assembly, scope, type),
            TypeResolutionRequest.FromModule(assembly, $"plan-{variant}.netmodule", type),
        };
        foreach (AssemblyBindingOccurrence occurrence in occurrences)
        {
            requests.Add(TypeResolutionRequest.FromOccurrence(occurrence, scope, type));
            requests.Add(TypeResolutionRequest.FromCoreLibraryOccurrence(occurrence, scope, type));
            requests.Add(TypeResolutionRequest.FromModuleOccurrence(occurrence, $"continued-{variant}.netmodule", type));
            requests.Add(TypeResolutionRequest.FromReference(Identity($"Continued-{variant}"),
                AssemblyBindingOrigin.FromOccurrence(occurrence), scope, type));
        }
        foreach (TypeResolutionRequest request in requests)
        {
            var rejected = Assert.IsType<E.Outcome.Rejected>(audit.Observe(context, request, projection, inputs));
            Assert.IsType<E.PlanRequest.Type>(Assert.IsType<E.Failure.PlanExpansionRequired>(rejected.Failure).Request);
        }
        var otherOperation = new WorkspaceProjectionContext(inputs);
        var source = context.Resolve(requests[0]);
        audit.Compare(M.Outcome, source, M.Outcome.Project(otherOperation, source), otherOperation, inputs);
        Assert.NotSame(projection.Operation, otherOperation.Operation);
    }

    static void ExerciseFailures(WorkspaceProjectionContractAudit audit, int variant)
    {
        var moduleOwner = Descriptor(Image($"ModuleOwner-{variant}", metadata => Define(metadata)), variant: variant);
        foreach (bool continued in new[] { false, true })
        {
            var module = continued
                ? TypeResolutionRequest.FromModuleOccurrence(AssemblyBindingOccurrence.Seed(moduleOwner),
                    $"missing-{variant}.netmodule", Name())
                : TypeResolutionRequest.FromModule(moduleOwner, $"raw-{variant}.netmodule", Name());
            Run(audit, new Policy(), [moduleOwner], [moduleOwner], module);
        }
        var foreign = Descriptor(Image($"Foreign-{variant}", metadata => Define(metadata)), variant: variant);
        Run(audit, new Policy(), [moduleOwner], [moduleOwner],
            TypeResolutionRequest.FromAssembly(foreign, AssemblyResolutionScope.Any, Name()), included: []);

        int budget = variant + 1;
        var cycleA = Descriptor(Image($"CycleA-{variant}", metadata => Forward(metadata, Identity($"CycleB-{variant}"))));
        var cycleB = Descriptor(Image($"CycleB-{variant}", metadata => Forward(metadata, cycleA.Identity)));
        var cyclePolicy = new Policy(request => AssemblyBindingSelection.Found(
            ((AssemblyBindingTarget.AssemblyReference)request.Target).Identity == cycleA.Identity ? cycleA : cycleB));
        Run(audit, cyclePolicy, [cycleA, cycleB], [cycleA],
            TypeResolutionRequest.FromAssembly(cycleA, AssemblyResolutionScope.Any, Name()));

        var chain = Enumerable.Range(0, budget + 2).Select(index =>
            Descriptor(Image($"Budget-{variant}-{index}", metadata =>
                Forward(metadata, Identity($"Budget-{variant}-{index + 1}"))))).ToArray();
        var chainPolicy = new Policy(request => AssemblyBindingSelection.Found(chain.Single(assembly =>
            assembly.Identity == ((AssemblyBindingTarget.AssemblyReference)request.Target).Identity)));
        Run(audit, chainPolicy, chain, [chain[0]],
            TypeResolutionRequest.FromAssembly(chain[0], AssemblyResolutionScope.Any, Name()),
            options: new() { MaxForwarderHops = budget });
        Run(audit, chainPolicy, chain, [chain[0]],
            TypeResolutionRequest.FromAssembly(chain[0], AssemblyResolutionScope.Any, Name()),
            options: new() { MaxCandidates = budget });

        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(moduleOwner, AssemblyResolutionScope.Any, Name("N", [$"R{budget}"]));
        TypeResolutionRequest[] included = Enumerable.Range(0, budget + 1).Select(index =>
            TypeResolutionRequest.FromAssembly(moduleOwner, AssemblyResolutionScope.Any, Name("N", [$"R{index}"]))).ToArray();
        Run(audit, new Policy(), [moduleOwner], [moduleOwner], request, included,
            new() { MaxTypeResolutionRequests = budget });

        foreach (AssemblyBindingFailureKind kind in Enum.GetValues<AssemblyBindingFailureKind>())
        foreach (CandidateOpenFailureKind? openKind in Enum.GetValues<CandidateOpenFailureKind>()
            .Select(kind => (CandidateOpenFailureKind?)kind).Prepend(null))
        foreach (MetadataRootMalformedReason? reason in openKind == CandidateOpenFailureKind.InvalidImage
            ? Enum.GetValues<MetadataRootMalformedReason>().Select(reason => (MetadataRootMalformedReason?)reason).Prepend(null)
            : [null])
        {
            var failure = new AssemblyBindingFailure(kind, openKind) { MetadataRootReason = reason };
            var policy = new Policy(_ => AssemblyBindingSelection.CannotSelect(failure));
            Run(audit, policy, [moduleOwner], [moduleOwner],
                TypeResolutionRequest.FromCoreLibrary(moduleOwner, (AssemblyResolutionScope)variant, Name()));
            Run(audit, new Policy(_ => AssemblyBindingSelection.Invalid(failure)), [moduleOwner], [moduleOwner],
                TypeResolutionRequest.FromReference(Identity($"Invalid-{variant}"), AssemblyBindingOrigin.FromAssembly(moduleOwner),
                    (AssemblyResolutionScope)variant, Name()));
        }
        ExerciseDeclarationFailures(audit, variant);
        foreach (var failure in FailureDescriptors(variant))
            Run(audit, new Policy(), [failure.Assembly], [failure.Assembly],
                TypeResolutionRequest.FromAssembly(failure.Assembly, AssemblyResolutionScope.Any, Name()));
    }

    static void ExerciseDeclarationFailures(WorkspaceProjectionContractAudit audit, int variant)
    {
        var malformed = Descriptor(Image($"MalformedDeclaration-{variant}", metadata =>
            Forward(metadata, Identity($"Target-{variant}"), forwarderFlag: false)));
        Run(audit, new Policy(), [malformed], [malformed],
            TypeResolutionRequest.FromAssembly(malformed, AssemblyResolutionScope.Any, Name()));

        // Name matching is before kind classification. A malformed nesting
        // relationship therefore remains a DeclarationRejected owner result.
        foreach (int depth in new[] { variant + 1, MetadataSafetyPolicy.MaxRelationshipNodes + variant + 1 })
        {
            bool cycle = depth < MetadataSafetyPolicy.MaxRelationshipNodes;
            var nested = Descriptor(Image($"Nesting-{variant}-{depth}", metadata =>
            {
                TypeDefinitionHandle[] types = Enumerable.Range(0, depth).Select(index =>
                    Define(metadata, segments: [index == depth - 1 ? "Type" : $"Parent{index}"])).ToArray();
                for (int index = 1; index < types.Length; index++)
                    metadata.AddNestedType(types[index], types[index - 1]);
                if (cycle)
                    metadata.AddNestedType(types[0], types[^1]);
            }));
            Run(audit, new Policy(), [nested], [nested],
                TypeResolutionRequest.FromAssembly(nested, AssemblyResolutionScope.Any, Name()));
        }

        var invalidParent = Descriptor(Image($"InvalidParent-{variant}", metadata =>
        {
            TypeDefinitionHandle leaf = Define(metadata);
            metadata.AddNestedType(leaf, MetadataTokens.TypeDefinitionHandle(100 + variant));
        }));
        Run(audit, new Policy(), [invalidParent], [invalidParent],
            TypeResolutionRequest.FromAssembly(invalidParent, AssemblyResolutionScope.Any, Name()));

        var unnamedModule = Descriptor(Image($"UnnamedModule-{variant}", metadata =>
            Module(metadata, "", variant != 0, [(byte)variant])));
        var moduleFailure = Assert.IsType<E.Outcome.Rejected>(Run(audit, new Policy(), [unnamedModule], [unnamedModule],
            TypeResolutionRequest.FromAssembly(unnamedModule, AssemblyResolutionScope.Any, Name())));
        Assert.Equal(MetadataTypeNameFailureMechanism.Metadata,
            Assert.IsType<E.Failure.DeclarationRejected>(moduleFailure.Failure).Rejection.Mechanism);

        byte[] invalidNamespaceImage = Image($"InvalidNamespace-{variant}", metadata =>
        {
            Reference(metadata, Identity($"NotCoreLibrary-{variant}"));
            if (variant != 0)
                Define(metadata, segments: ["Padding"]);
            Define(metadata);
        });
        using (var pe = new PEReader(ImmutableArray.Create(invalidNamespaceImage)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            Assert.True(reader.GetHeapSize(HeapIndex.String) < ushort.MaxValue);
            int row = pe.PEHeaders.MetadataStartOffset + reader.GetTableMetadataOffset(TableIndex.TypeDef)
                + (reader.TypeDefinitions.Count - 1) * reader.GetTableRowSize(TableIndex.TypeDef);
            BinaryPrimitives.WriteUInt16LittleEndian(invalidNamespaceImage.AsSpan(row + 6), (ushort)(ushort.MaxValue - variant));
        }
        var invalidNamespace = Descriptor(invalidNamespaceImage);
        var namespaceFailure = Assert.IsType<E.Outcome.Rejected>(Run(audit, new Policy(), [invalidNamespace], [invalidNamespace],
            TypeResolutionRequest.FromAssembly(invalidNamespace, AssemblyResolutionScope.Any, Name())));
        Assert.Equal(RelationshipTraversalRejectionKind.MalformedMetadata,
            Assert.IsType<E.Failure.DeclarationRejected>(namespaceFailure.Failure).Rejection.RelationshipKind);
    }

    static void ExerciseKindDependencies(WorkspaceProjectionContractAudit audit, int variant)
    {
        string dependencyName = $"Dependency-{variant}";
        AssemblyReferenceIdentity dependencyIdentity = Identity(dependencyName,
            variant == 0 ? null : "b03f5f7f11d50a3a");
        MetadataTypeDefinitionName dependencyType = Name($"DependencyNamespace-{variant}", [$"BaseType-{variant}"]);
        var owner = Descriptor(Image($"Dependent-{variant}", metadata =>
            Define(metadata, baseType: SpecificationBase(metadata, dependencyIdentity, dependencyType))));
        foreach (string ending in new[] { "unbound", "unavailable", "missing", "binding", "declaration", "cycle" })
        {
            var dependency = Descriptor(Image(dependencyName, metadata =>
            {
                if (ending is "binding" or "declaration")
                    Define(metadata, dependencyType.Namespace, dependencyType.Segments);
                if (ending == "declaration")
                    Define(metadata, dependencyType.Namespace, dependencyType.Segments, MetadataTypeDefinitionKind.Interface);
                if (ending == "cycle")
                    Define(metadata, dependencyType.Namespace, dependencyType.Segments, baseType: SpecificationBase(metadata, owner.Identity));
            }));
            var other = Descriptor(Image($"Alternative-{variant}", metadata => Define(metadata)));
            var policy = new Policy(request =>
            {
                if (request.Target is AssemblyBindingTarget.AssemblyReference { Identity: var identity }
                    && identity == owner.Identity)
                    return AssemblyBindingSelection.Found(owner);
                return ending switch
                {
                    "unbound" => AssemblyBindingSelection.NotFound(),
                    "unavailable" => AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(variant == 0
                            ? AssemblyBindingFailureKind.CandidateUnavailable : AssemblyBindingFailureKind.UnsupportedScope)),
                    "binding" => AssemblyBindingSelection.Multiple(variant == 0 ? [dependency, other] : [other, dependency]),
                    _ => AssemblyBindingSelection.Found(dependency),
                };
            });
            var projected = Assert.IsType<E.Outcome.Resolved>(Run(audit, policy, [owner, dependency, other], [owner],
                TypeResolutionRequest.FromAssembly(owner, AssemblyResolutionScope.Any, Name())));
            Assert.Equal(MetadataTypeDefinitionKind.Unknown, projected.Definition.Kind);
        }
    }

    static void ExerciseProvenanceAndAcquisitions(WorkspaceProjectionContractAudit audit, int variant)
    {
        string suffix = $"{variant}\u001b[31m\\\u202e";
        AssemblyResolutionProvenance[] provenances =
        [
            AssemblyResolutionProvenance.Package($"Package-{suffix}", $"{variant}.2.3",
                variant == 0 ? null : "net11.0", variant == 0 ? null : "browser-wasm"),
            AssemblyResolutionProvenance.Platform($"Framework-{suffix}", variant == 0 ? null : "11.0", $"Resolver-{suffix}"),
            AssemblyResolutionProvenance.Project($"Project-{suffix}", variant == 0 ? null : "net10.0",
                variant == 0 ? null : "osx-arm64"),
            AssemblyResolutionProvenance.Local($"Local-{suffix}"),
            AssemblyResolutionProvenance.Designated($"Designated-{suffix}"),
            AssemblyResolutionProvenance.Embedded($"Content-{suffix}", $"Digest-{suffix}", $"Declared-{suffix}"),
        ];
        foreach (AssemblyResolutionProvenance provenance in provenances)
        {
            byte[] bytes = Image($"Provenance-{provenance.GetType().Name}-{variant}", metadata => Define(metadata));
            var assembly = Descriptor(bytes, provenance, variant);
            Run(audit, new Policy(), [assembly], [assembly],
                TypeResolutionRequest.FromAssembly(assembly, AssemblyResolutionScope.Any, Name()));
        }
        byte[] artifactImage = Image($"Artifact-{variant}", metadata => Define(metadata));
        ArtifactAcquisitionRegistration artifact = RegisterArtifact(artifactImage);
        var first = ResolvedAssemblyReference.CreateFromArtifactIfManaged(artifact,
            () => new MemoryStream(artifactImage, writable: false), provenances[variant])!;
        var second = ResolvedAssemblyReference.CreateFromArtifactIfManaged(artifact,
            () => new MemoryStream(artifactImage, writable: false), provenances[variant])!;
        var inputs = Inputs([first, second]);
        var context = new WorkspaceProjectionContext(inputs);
        using TypeResolutionContext resolution = TypeResolutionContext.Create(new Policy(), [first, second],
            [TypeResolutionRequest.FromAssembly(first, AssemblyResolutionScope.Any, Name()),
                TypeResolutionRequest.FromAssembly(second, AssemblyResolutionScope.Any, Name())]);
        foreach (var assembly in new[] { first, second })
            audit.Observe(resolution, TypeResolutionRequest.FromAssembly(assembly, AssemblyResolutionScope.Any, Name()), context, inputs);
        Assert.NotSame(first.Registration, second.Registration);
        Assert.Same(first.Registration.ArtifactRegistration, second.Registration.ArtifactRegistration);
    }

    static void ExerciseIdentityValues(WorkspaceProjectionContractAudit audit, int variant)
    {
        foreach (int mask in Enumerable.Range(0, 8))
        {
            var identity = new AssemblyReferenceIdentity($"Identity-{variant}-{mask}",
                (mask & 1) == 0 ? null : new Version(variant + 1, 2, 3, 4),
                (mask & 2) == 0 ? null : $"culture-{variant}",
                (mask & 4) == 0 ? null : variant == 0 ? "0011223344556677" : "8899aabbccddeeff");
            Run(audit, new Policy(), [], [],
                TypeResolutionRequest.FromReference(identity, AssemblyBindingOrigin.Global(),
                    (AssemblyResolutionScope)variant, Name(variant == 0 ? "N" : "", variant == 0 ? ["A", "B"] : ["B", "A"])));
        }
    }

    static void ExerciseOrderedCollections(WorkspaceProjectionContractAudit audit)
    {
        var first = Descriptor(Image("OrderA", metadata => Define(metadata)));
        var second = Descriptor(Image("OrderB", metadata => Define(metadata)));
        foreach (bool reverse in new[] { false, true })
        {
            ResolvedAssemblyReference[] order = reverse ? [second, first] : [first, second];
            var binding = Assert.IsType<E.Outcome.Ambiguous>(Run(audit,
                new Policy(_ => AssemblyBindingSelection.Multiple([.. order])), [first, second], [],
                TypeResolutionRequest.FromReference(Identity("OrderTarget"), AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Any, Name())));
            Assert.Equal(order.Select(assembly => assembly.Identity.Name),
                Assert.IsType<E.Ambiguity.AssemblyBinding>(binding.Ambiguity).Candidates
                    .Select(candidate => candidate.Assembly.Identity.Name));
            var intrinsic = Assert.IsType<E.Outcome.Ambiguous>(Run(audit,
                new Policy(_ => AssemblyBindingSelection.Multiple([.. order])), [first, second], [first],
                TypeResolutionRequest.FromCoreLibrary(first,
                    reverse ? AssemblyResolutionScope.Platform : AssemblyResolutionScope.Any, Name())));
            Assert.Null(intrinsic.TerminalAssemblyIdentity);
            Assert.IsType<E.Target.IntrinsicCoreLibrary>(
                Assert.IsType<E.Ambiguity.AssemblyBinding>(intrinsic.Ambiguity).Target);

            ImmutableArray<string> segments = ["Outer", "Inner"];
            var terminal = Descriptor(Image($"OrderedTerminal-{reverse}", metadata => Define(metadata, segments: segments)));
            var facade = Descriptor(Image($"OrderedFacade-{reverse}", metadata =>
                Export(metadata, Reference(metadata, terminal.Identity), "N", segments, Forwarder, reverse)));
            var resolved = Assert.IsType<E.Outcome.Resolved>(Run(audit,
                new Policy(_ => AssemblyBindingSelection.Found(terminal)), [facade, terminal], [facade],
                TypeResolutionRequest.FromAssembly(facade, AssemblyResolutionScope.Any, Name("N", segments))));
            Assert.Equal(reverse ? [0x27000002, 0x27000001] : [0x27000001, 0x27000002],
                Assert.Single(resolved.Hops).Declarations.Select(token => token.Value));

            string[] moduleOrder = reverse ? ["module-b", "module-a"] : ["module-a", "module-b"];
            var moduleOwner = Descriptor(Image($"OrderedModules-{reverse}", metadata =>
            {
                foreach (string module in moduleOrder)
                {
                    AssemblyFileHandle file = metadata.AddAssemblyFile(metadata.GetOrAddString(module),
                        metadata.GetOrAddBlob(reverse ? new byte[] { 2, 1 } : [1, 2]), true);
                    Export(metadata, file, "N", segments, 0, reverse);
                }
            }));
            var ambiguous = Assert.IsType<E.Outcome.Ambiguous>(Run(audit, new Policy(), [moduleOwner], [moduleOwner],
                TypeResolutionRequest.FromAssembly(moduleOwner, AssemblyResolutionScope.Any, Name("N", segments))));
            var candidates = Assert.IsType<E.Ambiguity.TypeDeclaration>(ambiguous.Ambiguity).Candidates;
            Assert.Equal(moduleOrder, candidates.Select(candidate => Assert.IsType<E.Declaration.ModuleExport>(candidate).Module.Name.ToString()));
            Assert.Equal(reverse ? [0x27000002, 0x27000001] : [0x27000001, 0x27000002],
                Assert.IsType<E.Declaration.ModuleExport>(candidates[0]).Declarations.Select(token => token.Value));
        }
    }

    internal static void ExerciseQueryResults(WorkspaceProjectionContractAudit audit)
    {
        foreach (int variant in new[] { 0, 1 })
        {
            var policy = new Policy();
            var root = Descriptor(Image($"QueryRoot-{variant}", metadata => Define(metadata)), variant: variant);
            foreach (var failed in FailureDescriptors(variant))
            {
                using var workspace = new InspectionWorkspace();
                var participant = new AssemblyContextParticipant(root, policy);
                AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
                    [participant, new(failed.Assembly, policy)]);
                var source = Assert.IsType<AssemblyContextTypeResolutionResult.Rejected>(
                    AssemblyContextTypeResolutionQuery.Execute(group, participant, Name(), AssemblyResolutionScope.Any));
                Assert.Equal(failed.Kind, source.Failure.Kind);
                Assert.Equal(failed.RootReason, source.Failure.MetadataRootReason);
                var inputs = Inputs([root, failed.Assembly]);
                var context = new WorkspaceProjectionContext(inputs);
                var evidence = Assert.IsType<WorkspaceTypeResolutionEvidence.QueryRejected>(M.QueryResult.Project(context, source));
                audit.Compare(M.QueryResult, source, evidence, context, inputs);
                Assert.Same(inputs[failed.Assembly.Registration], evidence.Input);
            }
            using var availableWorkspace = new InspectionWorkspace();
            var rootParticipant = new AssemblyContextParticipant(root, policy);
            var availableGroup = availableWorkspace.CreateAssemblyContextGroup([rootParticipant]);
            var available = Assert.IsType<AssemblyContextTypeResolutionResult.Available>(
                AssemblyContextTypeResolutionQuery.Execute(availableGroup, rootParticipant, Name(), AssemblyResolutionScope.Any));
            var availableInputs = Inputs([root]);
            var availableContext = new WorkspaceProjectionContext(availableInputs);
            audit.Compare(M.QueryResult, available, M.QueryResult.Project(availableContext, available), availableContext, availableInputs);

            var unsupportedParticipant = new AssemblyContextParticipant(root, new UnattestedPolicy(policy));
            using var unsupportedGroup = availableWorkspace.CreateAssemblyContextGroup([unsupportedParticipant]);
            var unsupported = Assert.IsType<AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy>(
                AssemblyContextTypeResolutionQuery.Execute(
                    unsupportedGroup, unsupportedParticipant, Name(), AssemblyResolutionScope.Any));
            var unsupportedContext = new WorkspaceProjectionContext(availableInputs);
            var unsupportedEvidence = Assert.IsType<WorkspaceTypeResolutionEvidence.UnsupportedBindingPolicy>(
                M.QueryResult.Project(unsupportedContext, unsupported));
            audit.Compare(M.QueryResult, unsupported, unsupportedEvidence, unsupportedContext, availableInputs);
            Assert.Same(availableInputs[root.Registration], unsupportedEvidence.Input);
        }
    }

    internal static (ModuleFileReference Source, E.Module Destination, object Evidence) ProjectModuleHash(
        WorkspaceProjectionContractAudit audit, byte[] hash, int variant)
    {
        var assembly = Descriptor(Image($"LargeHash-{variant}", metadata =>
            Module(metadata, $"arbitrary-{variant}.netmodule", variant != 0, hash)));
        var request = TypeResolutionRequest.FromAssembly(assembly, AssemblyResolutionScope.Any, Name());
        using TypeResolutionContext resolution = TypeResolutionContext.Create(new Policy(), [assembly], [request]);
        var source = Assert.IsType<TypeResolutionOutcome.Rejected>(resolution.Resolve(request));
        ModuleFileReference module = Assert.IsType<TypeResolutionFailure.UnsupportedModuleExport>(source.Failure).Module;
        var inputs = Inputs([assembly]);
        var projection = new WorkspaceProjectionContext(inputs);
        var projected = Assert.IsType<E.Outcome.Rejected>(audit.Observe(resolution, request, projection, inputs));
        E.Module destination = Assert.IsType<E.Failure.UnsupportedModuleExport>(projected.Failure).Module;
        return (module, destination, new WorkspaceTypeResolutionEvidence.Available(projected));
    }

    internal static void ExerciseSentinelOpenRead(WorkspaceProjectionContractAudit audit)
    {
        byte[] bytes = Image("NeverOpen", metadata => Define(metadata));
        int calls = 0;
        var assembly = ResolvedAssemblyReference.Create(ReadIdentity(bytes), null, () =>
        {
            calls++;
            throw new InvalidOperationException("excluded-open-read-sentinel");
        }, AssemblyResolutionProvenance.Designated("sentinel"));
        var snapshot = Assert.IsType<AssemblyImageSnapshotResult.Ready>(
            AssemblyImageSnapshot.FromRetainedContent(assembly, [.. bytes])).Snapshot;
        using var catalog = new TypeResolutionCatalog();
        catalog.RegisterRetainedSnapshot(assembly, snapshot);
        var request = TypeResolutionRequest.FromAssembly(assembly, AssemblyResolutionScope.Any, Name());
        using TypeResolutionContext resolution = catalog.CreateContext(new Policy(), [assembly], [request]);
        var inputs = Inputs([assembly]);
        var context = new WorkspaceProjectionContext(inputs);
        Assert.IsType<E.Outcome.Resolved>(audit.Observe(resolution, request, context, inputs));
        Assert.Equal(0, calls);
    }

    static E.Outcome Run(
        WorkspaceProjectionContractAudit audit, Policy policy, ResolvedAssemblyReference[] population,
        ResolvedAssemblyReference[] roots, TypeResolutionRequest request,
        TypeResolutionRequest[]? included = null, TypeResolutionContextOptions? options = null)
    {
        var inputs = Inputs(population);
        var projection = new WorkspaceProjectionContext(inputs);
        using TypeResolutionContext resolution = TypeResolutionContext.Create(policy, roots, included ?? [request], options);
        E.Outcome result = audit.Observe(resolution, request, projection, inputs);
        foreach (AssemblyBindingRequest binding in policy.Requests)
            audit.Compare(M.BindingRequest, binding, M.BindingRequest.Project(projection, binding), projection, inputs);
        return result;
    }

    static Dictionary<AssemblyAcquisitionRegistration, QueryComparisonInputId> Inputs(ResolvedAssemblyReference[] assemblies)
    {
        var sealedPopulation = Assert.IsType<QueryComparisonPopulation<ImplementationComparisonBinding>>(
            Assert.IsType<QueryPopulationSealingOutcome.Sealed>(QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    assemblies.Select(assembly => new ImplementationComparisonBinding(assembly, new Policy(), BodyIndex)).ToArray(), [])))
                .Population);
        return sealedPopulation.Before.ToDictionary(input => input.Binding.Assembly.Registration, input => input.Id);
    }

    static IEnumerable<(ResolvedAssemblyReference Assembly, CandidateOpenFailureKind Kind, MetadataRootMalformedReason? RootReason)>
        FailureDescriptors(int variant)
    {
        byte[] good = Image($"Failure-{variant}", metadata => Define(metadata));
        var identity = ReadIdentity(good);
        yield return (ResolvedAssemblyReference.Create(identity, null,
            () => throw new IOException($"unreadable-{variant}"), AssemblyResolutionProvenance.Local($"io-{variant}")),
            CandidateOpenFailureKind.Unreadable, null);
        byte[] mismatched = Image($"Mismatched-{variant}", metadata => Define(metadata));
        yield return (ResolvedAssemblyReference.Create(identity, null,
            () => new MemoryStream(mismatched, writable: false), AssemblyResolutionProvenance.Local($"invalid-{variant}")),
            CandidateOpenFailureKind.InvalidImage, null);
        yield return (ResolvedAssemblyReference.Create(identity, null,
            () => new OversizedStream(), AssemblyResolutionProvenance.Local($"budget-{variant}")),
            CandidateOpenFailureKind.ResourceBudget, null);
        byte[] windows = Image($"Windows-{variant}", metadata => Define(metadata), "WindowsRuntime 1.4");
        yield return (ResolvedAssemblyReference.Create(identity, null,
            () => new MemoryStream(windows, writable: false), AssemblyResolutionProvenance.Local($"unsupported-{variant}")),
            CandidateOpenFailureKind.UnsupportedMetadataFormat, null);
        foreach (MetadataRootMalformedReason reason in Enum.GetValues<MetadataRootMalformedReason>())
        {
            byte[] malformed = MalformedRoot(good, reason, variant);
            yield return (ResolvedAssemblyReference.Create(identity, null,
                () => new MemoryStream(malformed, writable: false), AssemblyResolutionProvenance.Local($"root-{reason}-{variant}")),
                CandidateOpenFailureKind.InvalidImage, reason);
        }
    }

    static byte[] MalformedRoot(byte[] valid, MetadataRootMalformedReason reason, int variant)
    {
        byte[] image = (byte[])valid.Clone();
        using var pe = new PEReader(ImmutableArray.Create(image));
        int root = pe.PEHeaders.MetadataStartOffset;
        int directory = pe.PEHeaders.CorHeaderStartOffset + 8;
        switch (reason)
        {
            case MetadataRootMalformedReason.UnmappableMetadataDirectory:
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(directory), int.MaxValue - variant);
                break;
            case MetadataRootMalformedReason.TruncatedFixedPrefix:
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(directory + 4), 8 + variant);
                break;
            case MetadataRootMalformedReason.InvalidSignature:
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(root), variant);
                break;
            case MetadataRootMalformedReason.InvalidVersionLength:
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(root + 12), 1 + variant);
                break;
            case MetadataRootMalformedReason.TruncatedVersionField:
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(directory + 4), 16 + variant);
                break;
            case MetadataRootMalformedReason.MissingVersionTerminator:
                int length = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(root + 12));
                image.AsSpan(root + 16, length).Fill((byte)('A' + variant));
                break;
            default:
                throw new InvalidOperationException($"No owner fixture for {reason}.");
        }
        return image;
    }

    internal static byte[] SentinelImage(int variant)
    {
        var bytes = new byte[4096 + variant * 257];
        new Random(1701 + variant).NextBytes(bytes);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        "workspace-projection-image-sentinel"u8.CopyTo(bytes.AsSpan(64));
        return bytes;
    }

    static MetadataTypeDefinitionName Name(string ns = "N", ImmutableArray<string> segments = default) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(ns, segments.IsDefault ? ["Type"] : segments)).Name;

    static AssemblyReferenceIdentity Identity(string name, string? token = null) =>
        new(name, new Version(1, 0, 0, 0), null, token);

    static ResolvedAssemblyReference Descriptor(byte[] image, AssemblyResolutionProvenance? provenance = null, int variant = 0) =>
        ResolvedAssemblyReference.Create(ReadIdentity(image),
            variant == 0 ? null : "evidence/path\u001b[31m.dll",
            () => new MemoryStream(image, writable: false),
            provenance ?? AssemblyResolutionProvenance.Local($"fixture-{variant}"),
            variant == 0 ? null : new DateTime(2026, 9, 8, 1, 2, 3, DateTimeKind.Utc));

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var pe = new PEReader(ImmutableArray.Create(image));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader());
    }

    static byte[] Image(string name, Action<MetadataBuilder> add, string version = "v4.0.30319")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, default, default);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        add(metadata);
        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, version, suppressValidation: true), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static TypeDefinitionHandle Define(
        MetadataBuilder metadata, string ns = "N", ImmutableArray<string> segments = default,
        MetadataTypeDefinitionKind kind = MetadataTypeDefinitionKind.Class, bool coreRoot = false, EntityHandle baseType = default)
    {
        segments = segments.IsDefault ? ["Type"] : segments;
        if (coreRoot)
            Define(metadata, "System", ["Object"]);
        if (kind == MetadataTypeDefinitionKind.ValueType)
            baseType = coreRoot
                ? Define(metadata, "System", ["ValueType"])
                : metadata.AddTypeReference(Reference(metadata, Identity("System.Runtime", "b03f5f7f11d50a3a")),
                    metadata.GetOrAddString("System"), metadata.GetOrAddString("ValueType"));
        if (kind == MetadataTypeDefinitionKind.Unknown)
            baseType = metadata.AddTypeSpecification(metadata.GetOrAddBlob(new byte[] { 0x08 }));
        TypeDefinitionHandle previous = default;
        for (int index = 0; index < segments.Length; index++)
        {
            TypeDefinitionHandle current = metadata.AddTypeDefinition(
                (index == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic)
                    | (kind == MetadataTypeDefinitionKind.Interface ? TypeAttributes.Interface | TypeAttributes.Abstract : 0),
                index == 0 ? metadata.GetOrAddString(ns) : default,
                metadata.GetOrAddString(segments[index]), baseType,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            if (!previous.IsNil)
                metadata.AddNestedType(current, previous);
            previous = current;
        }
        return previous;
    }

    static AssemblyReferenceHandle Reference(MetadataBuilder metadata, AssemblyReferenceIdentity identity) =>
        metadata.AddAssemblyReference(metadata.GetOrAddString(identity.Name), identity.Version!, default,
            identity.PublicKeyToken is null ? default : metadata.GetOrAddBlob(Convert.FromHexString(identity.PublicKeyToken)),
            default, default);

    static EntityHandle SpecificationBase(
        MetadataBuilder metadata, AssemblyReferenceIdentity target, MetadataTypeDefinitionName? type = null)
    {
        type ??= Name();
        Assert.Single(type.Segments);
        TypeReferenceHandle reference = metadata.AddTypeReference(Reference(metadata, target),
            metadata.GetOrAddString(type.Namespace), metadata.GetOrAddString(type.Segments[0]));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).TypeSpecificationSignature().Type(reference, isValueType: false);
        return metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
    }

    static void Forward(
        MetadataBuilder metadata, AssemblyReferenceIdentity target, string ns = "N",
        ImmutableArray<string> segments = default, int count = 1, bool forwarderFlag = true)
    {
        AssemblyReferenceHandle reference = Reference(metadata, target);
        for (int index = 0; index < count; index++)
            Export(metadata, reference, ns, segments, forwarderFlag ? Forwarder : 0);
    }

    static void Module(
        MetadataBuilder metadata, string name, bool containsMetadata, byte[] hash,
        string ns = "N", ImmutableArray<string> segments = default)
    {
        AssemblyFileHandle file = metadata.AddAssemblyFile(metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(hash), containsMetadata);
        Export(metadata, file, ns, segments, 0);
    }

    static void Export(MetadataBuilder metadata, EntityHandle implementation, string ns,
        ImmutableArray<string> segments, TypeAttributes flags, bool reverseRows = false)
    {
        segments = segments.IsDefault ? ["Type"] : segments;
        if (reverseRows)
        {
            Assert.Equal(2, segments.Length);
            int parent = metadata.GetRowCount(TableIndex.ExportedType) + 2;
            metadata.AddExportedType(TypeAttributes.NestedPublic, default, metadata.GetOrAddString(segments[1]),
                MetadataTokens.ExportedTypeHandle(parent), 0);
            metadata.AddExportedType(TypeAttributes.Public | flags, metadata.GetOrAddString(ns),
                metadata.GetOrAddString(segments[0]), implementation, 0);
            return;
        }
        for (int index = 0; index < segments.Length; index++)
            implementation = metadata.AddExportedType(
                index == 0 ? TypeAttributes.Public | flags : TypeAttributes.NestedPublic,
                index == 0 ? metadata.GetOrAddString(ns) : default,
                metadata.GetOrAddString(segments[index]), implementation, 0);
    }

    static ArtifactAcquisitionRegistration RegisterArtifact(byte[] image)
    {
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope = authority.BeginContribution(admission))
            contribution = scope.Register(new Provenance(), _ => new MemoryStream(image, writable: false));
        authority.CreateRetainedContent(contribution.Registration, _ => new MemoryStream(image, writable: false));
        authority.CompleteAdmission(admission);
        return contribution.Registration;
    }

    sealed class Provenance : IArtifactProvenance;
    sealed class OversizedStream : MemoryStream
    {
        public override long Length => (long)int.MaxValue + 1;
    }

    sealed record ProbeLineage(AssemblyBindingPolicyVersion Issuer, int Context) : AssemblyBindingLineage(Issuer)
    {
        public object OpaquePolicyPayload => throw new InvalidOperationException("A lineage's derived payload is opaque.");
        internal AssemblyBindingOccurrence Select(ResolvedAssemblyReference assembly) => CreateOccurrence(assembly);
    }

    sealed class Policy(Func<AssemblyBindingRequest, AssemblyBindingSelection>? select = null)
        : IAcquisitionFreeAssemblyBindingPolicy, IAssemblyReferenceResolver
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        internal List<AssemblyBindingRequest> Requests { get; } = [];
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            Requests.Add(request);
            return new(Version, select?.Invoke(request) ?? AssemblyBindingSelection.NotFound());
        }
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope) =>
            throw new InvalidOperationException("The projection fixture never performs ambient resolution.");
    }

    sealed class UnattestedPolicy(IAssemblyBindingPolicy inner) : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version => inner.Version;
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
            throw new InvalidOperationException("The query must reject an unattested policy before selection.");
    }

    internal static void AssertCoverage(WorkspaceProjectionContractAudit audit)
    {
        // Kept explicit and fail-closed by the constructor-reference gate. This
        // dormant owner arm is not replaced by a directly constructed fixture.
        HashSet<Type> dormant = [typeof(ResolutionPlanRequest.Binding)];
        WorkspaceProjectionContractAudit.EqualSet(audit.Sources.Keys.Where(type => !dormant.Contains(type)),
            audit.ObservedTypes, "owner-produced source witnesses (excluding the proven dormant Binding arm)");
        audit.ValidateOccurrenceCoverage(dormant);
        foreach (Type arm in WorkspaceProjectionContractAudit.Arms(typeof(TypeResolutionOutcome)))
        {
            Assert.Contains(audit.Outcomes[arm], outcome => outcome.Hops.IsEmpty);
            Assert.Contains(audit.Outcomes[arm], outcome => outcome.Hops.Length >= 2);
        }
        AssertValueShapes(audit);
    }

    static void AssertValueShapes(WorkspaceProjectionContractAudit audit)
    {
        var failures = new List<string>();
        var nullability = new NullabilityInfoContext();
        foreach (var entry in audit.ObservedProperties)
        {
            PropertyInfo property = entry.Key.Type.GetProperty(entry.Key.Property)!;
            Type type = property.PropertyType;
            object?[] values = entry.Value.ToArray();
            Type element = Nullable.GetUnderlyingType(type) ?? type;
            string name = $"{entry.Key.Type.FullName}.{entry.Key.Property}";
            bool nullable = Nullable.GetUnderlyingType(type) is not null
                || !type.IsValueType && nullability.Create(property).ReadState == NullabilityState.Nullable;
            bool? onlyNull = NullInvariant(entry.Key.Type, entry.Key.Property);
            if (onlyNull == true)
            {
                Assert.All(values, Assert.Null);
                continue;
            }
            if (onlyNull == false || !nullable)
                Assert.All(values, Assert.NotNull);
            else if (!values.Contains(null) || values.All(value => value is null))
                failures.Add($"{name}: nullable needs null and non-null witnesses.");

            if (!HasDistinctValues(values))
                failures.Add($"{name}: needs two distinguishable owner-produced values.");
            if (element == typeof(bool))
            {
                if (!values.Contains(false) || !values.Contains(true))
                    failures.Add($"{name}: bool needs both values.");
            }
            else if (element.IsEnum && element != typeof(SignatureDecodeRejectionKind))
            {
                object[] admitted = Enum.GetValues(element).Cast<object>().ToArray();
                if (element == typeof(MetadataTypeNameFailureMechanism))
                    admitted = [MetadataTypeNameFailureMechanism.Metadata, MetadataTypeNameFailureMechanism.Relationship];
                if (element == typeof(RelationshipTraversalRejectionKind))
                    admitted = [RelationshipTraversalRejectionKind.Cycle, RelationshipTraversalRejectionKind.NodeBudget,
                        RelationshipTraversalRejectionKind.MalformedMetadata];
                foreach (object value in admitted)
                    if (!values.Contains(value))
                        failures.Add($"{name}: no {value} witness.");
                Assert.All(values.Where(value => value is not null), value => Assert.Contains(value, admitted));
            }
            else if (type == typeof(ImmutableArray<TypeForwardingHop>))
            {
                Assert.Contains(values, value => ((ImmutableArray<TypeForwardingHop>)value!).IsEmpty);
                Assert.Contains(values, value => ((ImmutableArray<TypeForwardingHop>)value!).Length >= 2);
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
            {
                object?[][] sequences = values.Select(value => ((IEnumerable)value!).Cast<object?>().ToArray()).ToArray();
                Assert.All(values, value => Assert.False((bool)type.GetProperty("IsDefault")!.GetValue(value)!));
                if (type == typeof(ImmutableArray<byte>))
                {
                    Assert.Contains(sequences, sequence => sequence.Length == 0);
                    Assert.Contains(sequences, sequence => sequence.Length >= 2);
                }
                else
                {
                    int minimum = entry.Key.Type == typeof(TypeResolutionAmbiguity.AssemblyBinding)
                        || entry.Key.Type == typeof(TypeResolutionAmbiguity.TypeDeclaration) ? 2 : 1;
                    Assert.All(sequences, sequence => Assert.True(sequence.Length >= minimum,
                        $"{name}: the owner admits only nonempty names/declaration chains or ambiguities with at least two candidates."));
                    if (entry.Key.Type == typeof(MetadataTypeDefinitionName)
                        || entry.Key.Type == typeof(TypeDeclarationCandidate.ModuleExport))
                        Assert.All(sequences, sequence =>
                            Assert.InRange(sequence.Length, 1, MetadataSafetyPolicy.MaxRelationshipNodes));
                    Assert.Contains(sequences, sequence => sequence.Length >= 2 && HasDistinctValues(sequence));
                }
            }
        }
        foreach (var entry in audit.ObservedDestinations)
        {
            WorkspaceProjectionSchema schema = audit.Sources.Values.Single(schema => schema.Destination == entry.Key.Type);
            var disposition = schema.Properties.Single(property => property.Destination == entry.Key.Member);
            if (disposition.Source is not null && NullInvariant(schema.Source, disposition.Source) == true)
                Assert.All(entry.Value, Assert.Null);
            else if (!HasDistinctValues(entry.Value))
                failures.Add($"{entry.Key.Type.FullName}.{entry.Key.Member}: destination needs two distinguishable values.");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    static bool HasDistinctValues(IReadOnlyList<object?> values) =>
        values.Skip(1).Any(value => !SameValue(values[0], value));

    static bool SameValue(object? left, object? right)
    {
        if (left is IEnumerable sequence && left is not string && right is IEnumerable other)
        {
            object?[] a = sequence.Cast<object?>().ToArray();
            object?[] b = other.Cast<object?>().ToArray();
            return a.Length == b.Length && a.Zip(b).All(pair => SameValue(pair.First, pair.Second));
        }
        return Equals(left, right);
    }

    static bool? NullInvariant(Type type, string property)
    {
        // TypeResolutionOutcome constructors always parent successful/missing
        // results by their terminal occurrence; missing bindings have none.
        if (property == nameof(TypeResolutionOutcome.TerminalOccurrence))
        {
            if (type == typeof(TypeResolutionOutcome.Resolved) || type == typeof(TypeResolutionOutcome.NotFound))
                return false;
            if (type == typeof(TypeResolutionOutcome.UnboundBinding) || type == typeof(TypeResolutionOutcome.Unavailable))
                return true;
        }
        // Intrinsic-core misses are invalid policy answers, not UnboundBinding.
        if (property == nameof(TypeResolutionOutcome.TerminalAssemblyIdentity)
            && (type == typeof(TypeResolutionOutcome.Resolved) || type == typeof(TypeResolutionOutcome.NotFound)
                || type == typeof(TypeResolutionOutcome.UnboundBinding)))
            return false;
        // The indexed declaration probe compares an already validated lookup
        // name and walks only TypeDef/ExportedType relationships. It cannot
        // issue a signature failure or a nil-subject declaration failure.
        if (type == typeof(MetadataTypeNameFailure))
        {
            if (property == nameof(MetadataTypeNameFailure.SignatureKind))
                return true;
            if (property == nameof(MetadataTypeNameFailure.SubjectToken))
                return false;
        }
        return null;
    }
}
