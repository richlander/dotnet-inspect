using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.Tests.SpellabilityConsumer;
using ILInspector.Metadata.Tests.SpellabilityReference;

namespace ILInspector.Metadata.Tests;

public sealed class SignatureSpellabilityAggregateTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public void SignatureSpellability_BindsSubjectToSourceModule()
    {
        using CompiledMember fixture = Compiled("VisibleMethod");
        using var catalog = new TypeResolutionCatalog();

        SignatureSpellabilitySubject subject =
            Subject(catalog, fixture.Source, fixture.Coordinates);
        Assert.Equal(catalog.Id, subject.Catalog);
        Assert.Same(fixture.Source, subject.Source);
        Assert.Equal(
            fixture.Coordinates.ModuleVersionId,
            subject.SourceModuleVersionId);
        Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
            catalog.PlanSignatureSpellability(subject));

        using CompiledMember field = Compiled(
            "VisibleField",
            SignatureSpellabilityMemberKind.Field);
        using CompiledMember property = Compiled(
            "VisibleProperty",
            SignatureSpellabilityMemberKind.Property);
        Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
            catalog.PlanSignatureSpellability(
                Subject(catalog, field.Source, field.Coordinates)));
        Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
            catalog.PlanSignatureSpellability(
                Subject(catalog, property.Source, property.Coordinates)));

        Assert.IsType<
            SignatureSpellabilityPlanFailure.SourceModuleMismatch>(
                SubjectFailure(
                    catalog,
                    fixture.Source,
                    fixture.Coordinates with
                    {
                        ModuleVersionId = Guid.NewGuid(),
                    }));
        Assert.IsType<SignatureSpellabilityPlanFailure.InvalidMember>(
            SubjectFailure(
                catalog,
                fixture.Source,
                fixture.Coordinates with
                {
                    MemberKind = SignatureSpellabilityMemberKind.Field,
                }));
        Assert.IsType<
            SignatureSpellabilityPlanFailure.InvalidDeclaringType>(
                SubjectFailure(
                    catalog,
                    fixture.Source,
                    fixture.Coordinates with
                    {
                        DeclaringTypeToken = 0x0200FFFF,
                    }));

        using CompiledMember other = Compiled("LocalMethod");
        Assert.IsType<
            SignatureSpellabilityPlanFailure.DeclaringTypeMismatch>(
                SubjectFailure(
                    catalog,
                    fixture.Source,
                    fixture.Coordinates with
                    {
                        DeclaringTypeToken = other.OtherDeclaringTypeToken,
                    }));
    }

    [Fact]
    public void SignatureSpellability_BindsSubjectToExactRegistration()
    {
        // Two registrations of two distinct images that publish one MVID. A
        // subject minted for one must decode only that registration's rows.
        Guid shared = Guid.NewGuid();
        SyntheticAssembly first = BuildSharedModuleSource(shared, "First");
        SyntheticAssembly second = BuildSharedModuleSource(shared, "Second");
        ResolvedAssemblyReference firstSource = Descriptor(first.Image);
        ResolvedAssemblyReference secondSource = Descriptor(second.Image);
        Assert.Equal(
            first.Coordinates.ModuleVersionId,
            second.Coordinates.ModuleVersionId);
        Assert.NotSame(
            firstSource.Registration,
            secondSource.Registration);

        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilitySubject firstSubject =
            Subject(catalog, firstSource, first.Coordinates);
        SignatureSpellabilitySubject secondSubject =
            Subject(catalog, secondSource, second.Coordinates);
        Assert.NotSame(firstSubject.Source, secondSubject.Source);
        Assert.Same(firstSource, firstSubject.Source);
        Assert.Same(secondSource, secondSubject.Source);

        SignatureSpellabilityPlan firstPlan =
            Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
                catalog.PlanSignatureSpellability(firstSubject)).Plan;
        SignatureSpellabilityPlan secondPlan =
            Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
                catalog.PlanSignatureSpellability(secondSubject)).Plan;
        Assert.Same(firstSubject.SourceCandidate, firstPlan.SourceCandidate);
        Assert.Same(secondSubject.SourceCandidate, secondPlan.SourceCandidate);
        Assert.Equal(
            "N.First",
            Assert.Single(firstPlan.Occurrences)
                .Reference.Type.ToMetadataFullName());
        Assert.Equal(
            "N.Second",
            Assert.Single(secondPlan.Occurrences)
                .Reference.Type.ToMetadataFullName());

        // A context holding only the other same-MVID registration cannot
        // stand in for the plan's own source.
        using TypeResolutionContext secondOnly = catalog.CreateContext(
            new MapPolicy(),
            [secondSource],
            firstPlan.Requests.Select(request => request.Request));
        Assert.IsType<
            SignatureSpellabilityEvaluationFailure.SourceUnavailable>(
                Assert.IsType<SignatureSpellabilityAggregate.Rejected>(
                    secondOnly.EvaluateSignatureSpellability(firstPlan))
                .Failure);

        using var otherCatalog = new TypeResolutionCatalog();
        Assert.Throws<ArgumentException>(() =>
            otherCatalog.PlanSignatureSpellability(firstSubject));
    }

    [Fact]
    public void SignatureSpellability_CollectsFieldAndPropertyOccurrences()
    {
        string path = typeof(VisibleReferenceType).Assembly.Location;
        ResolvedAssemblyReference target =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("compiled fixture"));

        AssertHiddenMember(
            target,
            "HiddenField",
            SignatureSpellabilityMemberKind.Field);
        AssertHiddenMember(
            target,
            "HiddenProperty",
            SignatureSpellabilityMemberKind.Property);
        AssertHiddenMember(
            target,
            "Item",
            SignatureSpellabilityMemberKind.Property);

        static void AssertHiddenMember(
            ResolvedAssemblyReference target,
            string memberName,
            SignatureSpellabilityMemberKind kind)
        {
            using CompiledMember fixture = Compiled(memberName, kind);
            using var catalog = new TypeResolutionCatalog();
            SignatureSpellabilityPlan plan = Plan(catalog, fixture);
            Assert.Single(plan.Requests);
            Assert.Contains(
                plan.Occurrences,
                occurrence =>
                    occurrence.Reference.Type.ToMetadataFullName()
                    == "ILInspector.Metadata.Tests.SpellabilityReference"
                        + ".HiddenReferenceType");
            using TypeResolutionContext context =
                catalog.CreateSignatureSpellabilityContext(
                    new MapPolicy(target),
                    plan,
                    [target]);

            var aggregate =
                Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                    context.EvaluateSignatureSpellability(plan));
            var external = Assert.Single(
                aggregate.Evidence.OfType<
                    SignatureSpellabilityEvidence.ExternalDefinition>());
            Assert.IsType<TypeDefinitionAccessibilityOutcome.Inaccessible>(
                external.Accessibility);
            Assert.False(aggregate.CanSpell());
        }
    }

    [Fact]
    public void SignatureSpellability_CollectsEveryNamedChildOnce()
    {
        using CompiledMember fixture = Compiled("ComplexMethod");
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(catalog, fixture);

        Assert.Equal(
            [
                "ILInspector.Metadata.Tests.SpellabilityReference.VisibleGeneric`1",
                "ILInspector.Metadata.Tests.SpellabilityReference.VisibleReferenceType",
                "ILInspector.Metadata.Tests.SpellabilityReference.VisibleValueType",
                "ILInspector.Metadata.Tests.SpellabilityReference.VisibleGeneric`1",
                "ILInspector.Metadata.Tests.SpellabilityReference.VisibleReferenceType",
                "ILInspector.Metadata.Tests.SpellabilityReference.VisibleReferenceType",
            ],
            plan.Occurrences
                .Select(occurrence =>
                    occurrence.Reference.Type.ToMetadataFullName()));
        Assert.Equal(3, plan.Requests.Length);

        using CompiledMember generic = Compiled("GenericMethod");
        Assert.Empty(Plan(catalog, generic).Occurrences);

        SyntheticAssembly malformed = BuildMalformedSignature();
        Assert.IsType<
            SignatureSpellabilityPlanFailure.SignatureRejected>(
                Assert.IsType<SignatureSpellabilityPlanOutcome.Rejected>(
                    catalog.PlanSignatureSpellability(
                        Subject(
                            catalog,
                            Descriptor(malformed.Image),
                            malformed.Coordinates))).Failure);
    }

    [Fact]
    public void SignatureSpellability_MapsClosedReferenceScopes()
    {
        SyntheticAssembly source = BuildScopeSignature();
        ResolvedAssemblyReference descriptor = Descriptor(source.Image);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            descriptor,
            source.Coordinates);

        Assert.Equal(
            [
                typeof(MetadataTypeReferenceScope.IntrinsicCoreLibrary),
                typeof(MetadataTypeReferenceScope.IntrinsicCoreLibrary),
                typeof(MetadataTypeReferenceScope.CurrentAssembly),
                typeof(MetadataTypeReferenceScope.AssemblyReference),
                typeof(MetadataTypeReferenceScope.ModuleReference),
            ],
            plan.Occurrences.Select(
                occurrence => occurrence.Reference.Scope.GetType()));
        Assert.Contains(
            plan.Requests,
            request => request.Request.Start
                is TypeResolutionStart.Assembly);
        Assert.Contains(
            plan.Requests,
            request => request.Request.Start
                is TypeResolutionStart.Reference);
        Assert.Contains(
            plan.Requests,
            request => request.Request.Start
                is TypeResolutionStart.Module);
        Assert.Equal(
            2,
            plan.Occurrences.Count(occurrence =>
                occurrence.Request is null));
    }

    [Fact]
    public void SignatureSpellability_ResolvesCurrentAssemblyForwarder()
    {
        byte[] targetImage = BuildNestedTarget(
            "Target",
            nestedPublic: true);
        AssemblyReferenceIdentity targetIdentity = ReadIdentity(targetImage);
        SyntheticAssembly facade = BuildCurrentForwarderSource(
            "Facade",
            targetIdentity);
        ResolvedAssemblyReference source = Descriptor(facade.Image);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan =
            Plan(catalog, source, facade.Coordinates);
        Assert.IsType<TypeResolutionStart.Assembly>(
            Assert.Single(plan.Requests).Request.Start);

        var policy = new MapPolicy(target);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                policy,
                plan,
                [target]);
        var aggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                context.EvaluateSignatureSpellability(plan));
        var external = Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(aggregate.Evidence));
        Assert.Equal(
            "N.Outer.Inner",
            external.Definition.Type.ToMetadataFullName());
        Assert.True(aggregate.CanSpell());
    }

    [Fact]
    public void SignatureSpellability_RequiresLocalArtifactProof()
    {
        using CompiledMember fixture = Compiled("MultipleLocalMethod");
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(catalog, fixture);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(),
                plan,
                []);

        var aggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                context.EvaluateSignatureSpellability(plan));
        SignatureSpellabilityEvidence.LocalRequirement[] locals =
            aggregate.Evidence
                .OfType<SignatureSpellabilityEvidence.LocalRequirement>()
                .ToArray();
        Assert.Equal(2, locals.Length);

        using CompiledMember unrelatedFixture = Compiled("LocalMethod");
        using var unrelatedCatalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan unrelatedPlan =
            Plan(unrelatedCatalog, unrelatedFixture);
        using TypeResolutionContext unrelatedContext =
            unrelatedCatalog.CreateSignatureSpellabilityContext(
                new MapPolicy(),
                unrelatedPlan,
                []);
        var unrelated = Assert.IsType<
            SignatureSpellabilityEvidence.LocalRequirement>(
                Assert.Single(
                    Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                        unrelatedContext.EvaluateSignatureSpellability(
                            unrelatedPlan))
                    .Evidence));

        Assert.False(aggregate.CanSpell());
        Assert.False(
            aggregate.CanSpell(
                new SignatureLocalRequirementProof(
                    [unrelated.Definition.Key])));
        Assert.False(
            aggregate.CanSpell(
                new SignatureLocalRequirementProof(
                    [locals[0].Definition.Key])));
        Assert.True(
            aggregate.CanSpell(
                new SignatureLocalRequirementProof(
                    locals.Select(local => local.Definition.Key))));
    }

    [Fact]
    public void SignatureSpellability_RetainsUnsupportedModuleReference()
    {
        SyntheticAssembly source = BuildScopeSignature();
        ResolvedAssemblyReference descriptor = Descriptor(source.Image);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan full =
            Plan(catalog, descriptor, source.Coordinates);
        SignatureSpellabilityPlan plan = full;
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(),
                plan,
                []);

        var aggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                context.EvaluateSignatureSpellability(plan));
        var module = Assert.Single(
            aggregate.Evidence.OfType<
                SignatureSpellabilityEvidence.Unresolved>(),
            entry => entry.Request.Start is TypeResolutionStart.Module);
        Assert.IsType<
            TypeResolutionFailure.UnsupportedModuleReference>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    module.Outcome).Failure);
        Assert.False(aggregate.CanSpell());
    }

    [Fact]
    public void SignatureSpellability_DerivesInitialScopePerReference()
    {
        SyntheticAssembly source = BuildTwoReferenceSignature();
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(source.Image),
            source.Coordinates);
        TypeResolutionStart.Reference[] requests = plan.Requests
            .Select(request =>
                Assert.IsType<TypeResolutionStart.Reference>(
                    request.Request.Start))
            .ToArray();

        Assert.Equal(AssemblyResolutionScope.Platform, requests[0].Scope);
        Assert.Equal(AssemblyResolutionScope.Any, requests[1].Scope);

        AssemblyReferenceIdentity platformTarget = new(
            "PlatformTarget",
            new Version(1, 0, 0, 0),
            null,
            "b03f5f7f11d50a3a");
        byte[] facadeImage = BuildForwarder("Facade", platformTarget);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        SyntheticAssembly forwardingSource = BuildReferenceSource(
            "ForwardingSource",
            ReadIdentity(facadeImage));
        SignatureSpellabilityPlan forwardingPlan = Plan(
            catalog,
            Descriptor(forwardingSource.Image),
            forwardingSource.Coordinates);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(facade),
                forwardingPlan,
                [facade]);
        TypeResolutionOutcome.UnboundBinding unbound =
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                Unresolved(context, forwardingPlan).Outcome);
        Assert.Equal(
            AssemblyResolutionScope.Platform,
            Assert.Single(unbound.Hops).Scope);
    }

    [Fact]
    public void SignatureSpellability_MergesModifierParticipation()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Modifiers",
            publicType: false);
        AssemblyReferenceIdentity identity = ReadIdentity(targetImage);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        using var catalog = new TypeResolutionCatalog();

        SyntheticAssembly optionalSource =
            BuildModifierSource(
                "OptionalSource",
                identity,
                ModifierShape.OptionalOnly);
        SignatureSpellabilityPlan optional = Plan(
            catalog,
            Descriptor(optionalSource.Image),
            optionalSource.Coordinates);
        Assert.False(Assert.Single(optional.Requests)
            .AccessibilityParticipates);
        using (TypeResolutionContext optionalContext =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                optional,
                [target]))
        {
            var aggregate =
                Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                    optionalContext.EvaluateSignatureSpellability(optional));
            Assert.True(aggregate.CanSpell());
        }

        SyntheticAssembly requiredSource =
            BuildModifierSource(
                "RequiredSource",
                identity,
                ModifierShape.RequiredOnly);
        SignatureSpellabilityPlan required = Plan(
            catalog,
            Descriptor(requiredSource.Image),
            requiredSource.Coordinates);
        Assert.True(Assert.Single(required.Requests)
            .AccessibilityParticipates);
        Assert.Equal(
            SignatureSpellabilityOccurrenceRole.RequiredModifier,
            Assert.Single(
                required.Occurrences,
                occurrence => occurrence.Request is not null).Role);
        using (TypeResolutionContext requiredContext =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                required,
                [target]))
        {
            Assert.False(
                Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                    requiredContext.EvaluateSignatureSpellability(required))
                .CanSpell());
        }

        SyntheticAssembly mixedSource =
            BuildModifierSource(
                "MixedSource",
                identity,
                ModifierShape.Mixed);
        SignatureSpellabilityPlan mixed = Plan(
            catalog,
            Descriptor(mixedSource.Image),
            mixedSource.Coordinates);
        Assert.True(Assert.Single(mixed.Requests)
            .AccessibilityParticipates);
        Assert.Equal(
            [
                SignatureSpellabilityOccurrenceRole.OptionalModifier,
                SignatureSpellabilityOccurrenceRole.RequiredModifier,
                SignatureSpellabilityOccurrenceRole.Ordinary,
            ],
            mixed.Occurrences
                .Where(occurrence => occurrence.Request is not null)
                .Select(occurrence => occurrence.Role));
        using TypeResolutionContext mixedContext =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                mixed,
                [target]);
        Assert.False(
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                mixedContext.EvaluateSignatureSpellability(mixed))
            .CanSpell());
    }

    [Fact]
    public void SignatureSpellability_ResolvesNestedForwarderToAccessibleDefinition()
    {
        AssertNestedAccessibility(nestedPublic: true, expected: true);
    }

    [Fact]
    public void SignatureSpellability_RejectsMissingForwarderTarget()
    {
        SyntheticAssembly facade = BuildCurrentForwarderSource(
            "Facade",
            Identity("Missing"));
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(facade.Image),
            facade.Coordinates);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(),
                plan,
                []);

        var aggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                context.EvaluateSignatureSpellability(plan));
        var unresolved = Assert.IsType<
            SignatureSpellabilityEvidence.Unresolved>(
                Assert.Single(aggregate.Evidence));
        Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
            unresolved.Outcome);
        Assert.False(aggregate.CanSpell());
    }

    [Fact]
    public void SignatureSpellability_RejectsForwarderTargetMissingType()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true,
            typeName: "Other");
        SyntheticAssembly facade = BuildCurrentForwarderSource(
            "Facade",
            ReadIdentity(targetImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(facade.Image),
            facade.Coordinates);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                plan,
                [target]);

        var aggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                context.EvaluateSignatureSpellability(plan));
        Assert.IsType<TypeResolutionOutcome.NotFound>(
            Assert.IsType<SignatureSpellabilityEvidence.Unresolved>(
                Assert.Single(aggregate.Evidence)).Outcome);
        Assert.False(aggregate.CanSpell());
    }

    [Fact]
    public void SignatureSpellability_RejectsInaccessibleTerminalDefinition()
    {
        AssertNestedAccessibility(nestedPublic: false, expected: false);
        AssertNestedAccessibility(
            nestedPublic: true,
            expected: false,
            outerPublic: false);

        byte[] targetImage = BuildVisibilityTarget(
            "TopLevelTarget",
            publicType: false);
        SyntheticAssembly source = BuildReferenceSource(
            "TopLevelSource",
            ReadIdentity(targetImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(source.Image),
            source.Coordinates);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                plan,
                [target]);
        var external = Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(
                    Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                        context.EvaluateSignatureSpellability(plan))
                    .Evidence));
        Assert.IsType<TypeDefinitionAccessibilityOutcome.Inaccessible>(
            external.Accessibility);
    }

    [Fact]
    public void SignatureSpellability_RejectsInvalidAccessibilityKey()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            target,
            AssemblyResolutionScope.Any,
            TypeName("Type"));
        using var firstCatalog = new TypeResolutionCatalog();
        using var secondCatalog = new TypeResolutionCatalog();
        using TypeResolutionContext first =
            firstCatalog.CreateContext(
                new MapPolicy(),
                [target],
                [request]);
        ResolvedTypeDefinitionKey key =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                first.Resolve(request)).Definition.Key;
        using TypeResolutionContext other =
            secondCatalog.CreateContext(
                new MapPolicy(),
                [target],
                [request]);

        Assert.IsType<
            TypeDefinitionAccessibilityFailure.IncomparableCatalog>(
                Assert.IsType<TypeDefinitionAccessibilityOutcome.Rejected>(
                    other.GetTerminalDefinitionAccessibility(key)).Failure);

        using TypeResolutionContext replacement =
            firstCatalog.CreateContext(
                new MapPolicy(),
                [target],
                [request]);
        Assert.IsType<
            TypeDefinitionAccessibilityFailure.StaleGeneration>(
                Assert.IsType<TypeDefinitionAccessibilityOutcome.Rejected>(
                    first.GetTerminalDefinitionAccessibility(key)).Failure);

        byte[] malformedImage = BuildInvalidVisibilityTarget();
        ResolvedAssemblyReference malformed = Descriptor(malformedImage);
        SyntheticAssembly malformedSource = BuildReferenceSource(
            "MalformedSource",
            ReadIdentity(malformedImage));
        SignatureSpellabilityPlan malformedPlan = Plan(
            firstCatalog,
            Descriptor(malformedSource.Image),
            malformedSource.Coordinates);
        using TypeResolutionContext malformedContext =
            firstCatalog.CreateSignatureSpellabilityContext(
                new MapPolicy(malformed),
                malformedPlan,
                [malformed]);
        var malformedAggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                malformedContext.EvaluateSignatureSpellability(
                    malformedPlan));
        var external = Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(malformedAggregate.Evidence));
        Assert.IsType<
            TypeDefinitionAccessibilityFailure.InvalidDeclaringChain>(
                Assert.IsType<TypeDefinitionAccessibilityOutcome.Rejected>(
                    external.Accessibility).Failure);
        Assert.False(malformedAggregate.CanSpell());
    }

    [Fact]
    public void SignatureSpellability_AccessibilityReusesResolvedSession()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true);
        int opens = 0;
        bool rejectFurtherOpens = false;
        ResolvedAssemblyReference target = Descriptor(
            targetImage,
            () =>
            {
                if (rejectFurtherOpens)
                {
                    throw new IOException(
                        "Accessibility reopened the resolved candidate.");
                }

                opens++;
            });
        SyntheticAssembly source = BuildReferenceSource(
            "Source",
            ReadIdentity(targetImage));
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(source.Image),
            source.Coordinates);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                plan,
                [target]);
        int afterResolution = opens;
        rejectFurtherOpens = true;

        var external = Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(
                    Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                        context.EvaluateSignatureSpellability(plan))
                    .Evidence));
        Assert.IsType<TypeDefinitionAccessibilityOutcome.Accessible>(
            external.Accessibility);
        Assert.Equal(afterResolution, opens);
    }

    [Fact]
    public void SignatureSpellability_RetainsResolutionFailureKinds()
    {
        SyntheticAssembly source = BuildReferenceSource(
            "Source",
            Identity("Target"));
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(source.Image),
            source.Coordinates);
        var unavailable = new MapPolicy(
            _ => AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)));
        using TypeResolutionContext unavailableContext =
            catalog.CreateSignatureSpellabilityContext(
                unavailable,
                plan,
                []);
        Assert.IsType<TypeResolutionOutcome.Unavailable>(
            Unresolved(unavailableContext, plan).Outcome);

        byte[] firstImage = BuildVisibilityTarget("Target", true);
        byte[] secondImage = BuildVisibilityTarget("Target", true);
        ResolvedAssemblyReference first = Descriptor(firstImage);
        ResolvedAssemblyReference second = Descriptor(secondImage);
        var ambiguous = new MapPolicy(
            _ => AssemblyBindingSelection.Multiple([first, second]));
        using TypeResolutionContext ambiguousContext =
            catalog.CreateSignatureSpellabilityContext(
                ambiguous,
                plan,
                [first, second]);
        Assert.IsType<TypeResolutionOutcome.Ambiguous>(
            Unresolved(ambiguousContext, plan).Outcome);

        byte[] duplicateImage = BuildDuplicateTarget("Target");
        ResolvedAssemblyReference duplicate = Descriptor(duplicateImage);
        using TypeResolutionContext declarationAmbiguous =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(duplicate),
                plan,
                [duplicate]);
        Assert.IsType<TypeResolutionAmbiguity.TypeDeclaration>(
            Assert.IsType<TypeResolutionOutcome.Ambiguous>(
                Unresolved(declarationAmbiguous, plan).Outcome)
            .Ambiguity);

        ResolvedAssemblyReference broken = ResolvedAssemblyReference.Create(
            Identity("Target"),
            path: null,
            openRead: () => throw new IOException("broken"),
            AssemblyResolutionProvenance.Local("broken"));
        using TypeResolutionContext candidateOpen =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(broken),
                plan,
                [broken]);
        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                Unresolved(candidateOpen, plan).Outcome).Failure);

        byte[] aImage = BuildForwarder(
            "A",
            Identity("B"));
        byte[] bImage = BuildForwarder(
            "B",
            Identity("A"));
        ResolvedAssemblyReference a = Descriptor(aImage);
        ResolvedAssemblyReference b = Descriptor(bImage);
        SyntheticAssembly cycleSource = BuildReferenceSource(
            "CycleSource",
            ReadIdentity(aImage));
        SignatureSpellabilityPlan cyclePlan = Plan(
            catalog,
            Descriptor(cycleSource.Image),
            cycleSource.Coordinates);
        using TypeResolutionContext cycle =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(a, b),
                cyclePlan,
                [a, b]);
        Assert.IsType<TypeResolutionFailure.ForwarderCycle>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                Unresolved(cycle, cyclePlan).Outcome).Failure);

        using var boundedCatalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions { MaxForwarderHops = 0 });
        SignatureSpellabilityPlan boundedPlan = Plan(
            boundedCatalog,
            Descriptor(cycleSource.Image),
            cycleSource.Coordinates);
        using TypeResolutionContext bounded =
            boundedCatalog.CreateSignatureSpellabilityContext(
                new MapPolicy(a, b),
                boundedPlan,
                [a, b]);
        Assert.IsType<TypeResolutionFailure.HopBudgetExceeded>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                Unresolved(bounded, boundedPlan).Outcome).Failure);

        byte[] malformedDeclarationImage =
            BuildCyclicDeclarationTarget("MalformedTarget");
        ResolvedAssemblyReference malformedDeclaration =
            Descriptor(malformedDeclarationImage);
        SyntheticAssembly malformedDeclarationSource =
            BuildReferenceSource(
                "MalformedDeclarationSource",
                ReadIdentity(malformedDeclarationImage),
                typeName: "First");
        SignatureSpellabilityPlan malformedDeclarationPlan = Plan(
            catalog,
            Descriptor(malformedDeclarationSource.Image),
            malformedDeclarationSource.Coordinates);
        using TypeResolutionContext malformedDeclarationContext =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(malformedDeclaration),
                malformedDeclarationPlan,
                [malformedDeclaration]);
        var declarationFailure =
            Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    Unresolved(
                        malformedDeclarationContext,
                        malformedDeclarationPlan).Outcome).Failure);
        Assert.Contains(
            "ExportedType relationship",
            declarationFailure.Failure.Detail,
            StringComparison.OrdinalIgnoreCase);

        byte[] relationshipBudgetImage =
            BuildRelationshipBudgetTarget("BudgetTarget");
        ResolvedAssemblyReference relationshipBudget =
            Descriptor(relationshipBudgetImage);
        SyntheticAssembly relationshipBudgetSource =
            BuildReferenceSource(
                "RelationshipBudgetSource",
                ReadIdentity(relationshipBudgetImage),
                typeName: "Type1");
        SignatureSpellabilityPlan relationshipBudgetPlan = Plan(
            catalog,
            Descriptor(relationshipBudgetSource.Image),
            relationshipBudgetSource.Coordinates);
        using TypeResolutionContext relationshipBudgetContext =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(relationshipBudget),
                relationshipBudgetPlan,
                [relationshipBudget]);
        var budgetFailure =
            Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    Unresolved(
                        relationshipBudgetContext,
                        relationshipBudgetPlan).Outcome).Failure);
        Assert.Contains(
            "ExportedType relationship",
            budgetFailure.Failure.Detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureSpellability_UsesCatalogBindingPolicy()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true,
            version: new Version(2, 0, 0, 0));
        SyntheticAssembly source = BuildReferenceSource(
            "Source",
            Identity("Target", version: new Version(1, 0, 0, 0)));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(source.Image),
            source.Coordinates);
        using TypeResolutionContext unified =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(_ => AssemblyBindingSelection.Found(target)),
                plan,
                [target]);
        Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(
                    Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                        unified.EvaluateSignatureSpellability(plan))
                    .Evidence));

        var nullVersionOnly = new MapPolicy(request =>
            request.Target is AssemblyBindingTarget.AssemblyReference reference
                && reference.Identity.Version is null
                    ? AssemblyBindingSelection.Found(target)
                    : AssemblyBindingSelection.NotFound());
        using TypeResolutionContext exact =
            catalog.CreateSignatureSpellabilityContext(
                nullVersionOnly,
                plan,
                [target]);
        Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
            Unresolved(exact, plan).Outcome);
        Assert.All(
            nullVersionOnly.Requests,
            request => Assert.NotNull(
                Assert.IsType<AssemblyBindingTarget.AssemblyReference>(
                    request.Target).Identity.Version));
    }

    [Fact]
    public void SignatureSpellability_ExpandsPlanBeforeVerdict()
    {
        using CompiledMember fixture = Compiled("VisibleMethod");
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(catalog, fixture);
        using TypeResolutionContext context = catalog.CreateContext(
            new MapPolicy(),
            [fixture.Source],
            []);

        var rejected =
            Assert.IsType<SignatureSpellabilityAggregate.Rejected>(
                context.EvaluateSignatureSpellability(plan));
        Assert.IsType<
            SignatureSpellabilityEvaluationFailure.PlanExpansionRequired>(
                rejected.Failure);
    }

    [Fact]
    public void SignatureSpellability_CachesResolutionPerRequestAndAccessibilityPerDefinition()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        SyntheticAssembly source = BuildDuplicateReferenceSource(
            ReadIdentity(targetImage));
        ResolvedAssemblyReference sourceDescriptor =
            Descriptor(source.Image);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            sourceDescriptor,
            source.Coordinates);
        Assert.Single(plan.Requests);
        var policy = new MapPolicy(target);
        using TypeResolutionContext first =
            catalog.CreateSignatureSpellabilityContext(
                policy,
                plan,
                [target]);
        var firstAggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                first.EvaluateSignatureSpellability(plan));
        var firstExternal = Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(firstAggregate.Evidence));
        Assert.Single(policy.Requests);
        Assert.Same(
            firstExternal.Accessibility,
            first.GetTerminalDefinitionAccessibility(
                firstExternal.Definition.Key));

        using TypeResolutionContext replacement =
            catalog.CreateSignatureSpellabilityContext(
                policy,
                plan,
                [target]);
        var replacementExternal = Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(
                    Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                        replacement.EvaluateSignatureSpellability(plan))
                    .Evidence));
        Assert.NotSame(
            firstExternal.Accessibility,
            replacementExternal.Accessibility);
        Assert.IsType<
            TypeDefinitionAccessibilityFailure.StaleGeneration>(
                Assert.IsType<TypeDefinitionAccessibilityOutcome.Rejected>(
                    first.GetTerminalDefinitionAccessibility(
                        firstExternal.Definition.Key)).Failure);

        byte[] firstFacadeImage = BuildForwarder(
            "FirstFacade",
            ReadIdentity(targetImage));
        byte[] secondFacadeImage = BuildForwarder(
            "SecondFacade",
            ReadIdentity(targetImage));
        ResolvedAssemblyReference firstFacade =
            Descriptor(firstFacadeImage);
        ResolvedAssemblyReference secondFacade =
            Descriptor(secondFacadeImage);
        SyntheticAssembly aliases = BuildAliasSource(
            ReadIdentity(firstFacadeImage),
            ReadIdentity(secondFacadeImage));
        SignatureSpellabilityPlan aliasPlan = Plan(
            catalog,
            Descriptor(aliases.Image),
            aliases.Coordinates);
        Assert.Equal(2, aliasPlan.Requests.Length);
        using TypeResolutionContext aliasContext =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(firstFacade, secondFacade, target),
                aliasPlan,
                [firstFacade, secondFacade, target]);
        SignatureSpellabilityEvidence.ExternalDefinition[] definitions =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                aliasContext.EvaluateSignatureSpellability(aliasPlan))
            .Evidence
            .Select(entry =>
                Assert.IsType<
                    SignatureSpellabilityEvidence.ExternalDefinition>(
                        entry))
            .ToArray();
        Assert.Equal(2, definitions.Length);
        Assert.Same(
            definitions[0].Accessibility,
            definitions[1].Accessibility);
    }

    [Fact]
    public void SignatureSpellability_RejectsMismatchedOrUnregisteredSource()
    {
        using CompiledMember fixture = Compiled("LocalMethod");
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(catalog, fixture);
        using TypeResolutionContext context =
            catalog.CreateContext(new MapPolicy(), [], []);

        Assert.IsType<
            SignatureSpellabilityEvaluationFailure.SourceUnavailable>(
                Assert.IsType<SignatureSpellabilityAggregate.Rejected>(
                    context.EvaluateSignatureSpellability(plan)).Failure);

        using TypeResolutionContext registered =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(),
                plan,
                []);
        using TypeResolutionContext replacement =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(),
                plan,
                []);
        Assert.IsType<
            SignatureSpellabilityEvaluationFailure.StaleSource>(
                Assert.IsType<SignatureSpellabilityAggregate.Rejected>(
                    registered.EvaluateSignatureSpellability(plan)).Failure);

        using var otherCatalog = new TypeResolutionCatalog();
        using TypeResolutionContext other =
            otherCatalog.CreateContext(new MapPolicy(), [], []);
        Assert.IsType<
            SignatureSpellabilityEvaluationFailure.IncomparableCatalog>(
                Assert.IsType<SignatureSpellabilityAggregate.Rejected>(
                    other.EvaluateSignatureSpellability(plan)).Failure);
    }

    [Fact]
    public void SignatureSpellability_RetainsAuthorizedPlatformScope()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true);
        AssemblyReferenceIdentity targetIdentity = ReadIdentity(targetImage);
        Assert.False(PlatformKeys.IsPlatform(targetIdentity.PublicKeyToken));
        SyntheticAssembly platform = BuildPlatformKeySource(
            "PlatformSource",
            targetIdentity);
        Assert.True(
            PlatformKeys.IsPlatform(
                ReadIdentity(platform.Image).PublicKeyToken));

        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilitySubject platformSubject = Subject(
            catalog,
            Descriptor(platform.Image),
            platform.Coordinates);
        Assert.Equal(
            AssemblyResolutionScope.Platform,
            platformSubject.AuthorizedScope);

        // A caller asking for Any cannot widen the authorized baseline.
        SignatureSpellabilityPlan plan =
            Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
                catalog.PlanSignatureSpellability(
                    platformSubject,
                    AssemblyResolutionScope.Any)).Plan;
        Assert.Equal(AssemblyResolutionScope.Platform, plan.SourceScope);
        Assert.Equal(
            AssemblyResolutionScope.Platform,
            Assert.IsType<TypeResolutionStart.Reference>(
                Assert.Single(plan.Requests).Request.Start).Scope);

        // A confusable local copy is offered only to an Any-scoped request, so
        // a loosened plan would have selected the non-public copy instead.
        byte[] confusableImage = BuildVisibilityTarget(
            "Target",
            publicType: false);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference confusable = Descriptor(confusableImage);
        var scoped = new MapPolicy(request =>
            request.Scope == AssemblyResolutionScope.Platform
                ? AssemblyBindingSelection.Found(target)
                : AssemblyBindingSelection.Found(confusable));
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                scoped,
                plan,
                [target, confusable]);
        var aggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                context.EvaluateSignatureSpellability(plan));
        Assert.IsType<TypeDefinitionAccessibilityOutcome.Accessible>(
            Assert.IsType<
                SignatureSpellabilityEvidence.ExternalDefinition>(
                    Assert.Single(aggregate.Evidence)).Accessibility);
        Assert.True(aggregate.CanSpell());
        Assert.All(
            scoped.Requests,
            request => Assert.Equal(
                AssemblyResolutionScope.Platform,
                request.Scope));

        // An ordinary source keeps Any, and a caller may still tighten it.
        SyntheticAssembly ordinary = BuildReferenceSource(
            "OrdinarySource",
            targetIdentity);
        SignatureSpellabilitySubject ordinarySubject = Subject(
            catalog,
            Descriptor(ordinary.Image),
            ordinary.Coordinates);
        Assert.Equal(
            AssemblyResolutionScope.Any,
            ordinarySubject.AuthorizedScope);
        Assert.Equal(
            AssemblyResolutionScope.Any,
            Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
                catalog.PlanSignatureSpellability(ordinarySubject))
            .Plan.SourceScope);
        Assert.Equal(
            AssemblyResolutionScope.Platform,
            Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
                catalog.PlanSignatureSpellability(
                    ordinarySubject,
                    AssemblyResolutionScope.Platform))
            .Plan.SourceScope);

        // Platform acquisition provenance authorizes the same baseline.
        ResolvedAssemblyReference platformProvenance =
            ResolvedAssemblyReference.Create(
                ReadIdentity(ordinary.Image),
                path: null,
                openRead: () =>
                    new MemoryStream(ordinary.Image, writable: false),
                provenance: AssemblyResolutionProvenance.Platform(
                    "net10.0",
                    "10.0.0",
                    "test"));
        Assert.Equal(
            AssemblyResolutionScope.Platform,
            Subject(catalog, platformProvenance, ordinary.Coordinates)
                .AuthorizedScope);
    }

    [Fact]
    public void SignatureSpellability_RejectsNestedTypeSpecTrailingData()
    {
        using var catalog = new TypeResolutionCatalog();
        SyntheticAssembly complete = BuildTypeSpecSource(
            "CompleteSpec",
            TypeSpecShape.Complete);
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(complete.Image),
            complete.Coordinates);
        SignatureSpellabilityOccurrence element = Assert.Single(
            plan.Occurrences,
            occurrence => occurrence.Role
                == SignatureSpellabilityOccurrenceRole.RequiredModifier);
        Assert.Equal(
            "N.Element",
            element.Reference.Type.ToMetadataFullName());

        // The same TypeSpec plus one byte the grammar does not consume, and a
        // TypeSpec truncated mid-token, are both rejected rather than decoded
        // as their safe prefix.
        AssertRejectedSpec(catalog, "TrailingSpec", TypeSpecShape.Trailing);
        AssertRejectedSpec(catalog, "TruncatedSpec", TypeSpecShape.Truncated);

        static void AssertRejectedSpec(
            TypeResolutionCatalog catalog,
            string assemblyName,
            TypeSpecShape shape)
        {
            SyntheticAssembly source =
                BuildTypeSpecSource(assemblyName, shape);
            Assert.IsType<
                SignatureSpellabilityPlanFailure.SignatureRejected>(
                    Assert.IsType<
                        SignatureSpellabilityPlanOutcome.Rejected>(
                            catalog.PlanSignatureSpellability(
                                Subject(
                                    catalog,
                                    Descriptor(source.Image),
                                    source.Coordinates))).Failure);
        }
    }

    [Fact]
    public void SignatureSpellability_BoundsTypeSpecDagExpansion()
    {
        using var catalog = new TypeResolutionCatalog();
        SyntheticAssembly bounded = BuildTypeSpecDagSource(
            "BoundedDag",
            branchingLevels: 8);
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(bounded.Image),
            bounded.Coordinates);
        Assert.Equal(512, plan.Occurrences.Length);

        SyntheticAssembly zeroOccurrenceDag = BuildTypeSpecDagSource(
            "ZeroOccurrenceDag",
            branchingLevels: 16,
            namedOccurrences: false);
        Assert.IsType<
            SignatureSpellabilityPlanFailure.SignatureRejected>(
                Assert.IsType<SignatureSpellabilityPlanOutcome.Rejected>(
                    catalog.PlanSignatureSpellability(
                        Subject(
                            catalog,
                            Descriptor(zeroOccurrenceDag.Image),
                            zeroOccurrenceDag.Coordinates))).Failure);

        SyntheticAssembly materializationDag = BuildTypeSpecDagSource(
            "MaterializationDag",
            branchingLevels: 13,
            wrapperLevels: 8);
        Assert.IsType<
            SignatureSpellabilityPlanFailure.SignatureRejected>(
                Assert.IsType<SignatureSpellabilityPlanOutcome.Rejected>(
                    catalog.PlanSignatureSpellability(
                        Subject(
                            catalog,
                            Descriptor(materializationDag.Image),
                            materializationDag.Coordinates))).Failure);
    }

    [Fact]
    public void SignatureSpellability_RejectsAccessibilityKeyFromAnotherGeneration()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            target,
            AssemblyResolutionScope.Any,
            TypeName("Type"));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext first = catalog.CreateContext(
            new MapPolicy(),
            [target],
            [request]);
        using TypeResolutionContext second = catalog.CreateContext(
            new MapPolicy(),
            [target],
            [request]);
        ResolvedTypeDefinitionKey secondKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                second.Resolve(request)).Definition.Key;

        Assert.IsType<TypeDefinitionAccessibilityOutcome.Accessible>(
            second.GetTerminalDefinitionAccessibility(secondKey));

        // The key is current for the catalog, but the older context never
        // observed that generation and must not classify it.
        var stale = Assert.IsType<
            TypeDefinitionAccessibilityFailure.StaleGeneration>(
                Assert.IsType<TypeDefinitionAccessibilityOutcome.Rejected>(
                    first.GetTerminalDefinitionAccessibility(secondKey))
                .Failure);
        Assert.Same(second.Generation, stale.KeyGeneration);
        Assert.Same(first.Generation, stale.CurrentGeneration);
    }

    [Fact]
    public void SignatureSpellability_HoldsOneGenerationAcrossEvaluation()
    {
        byte[] targetImage = BuildVisibilityTarget(
            "Target",
            publicType: true);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        SyntheticAssembly source = BuildReferenceSource(
            "Source",
            ReadIdentity(targetImage));
        ResolvedAssemblyReference sourceDescriptor =
            Descriptor(source.Image);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            sourceDescriptor,
            source.Coordinates);
        using TypeResolutionContext leased =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                plan,
                [target]);

        // One evaluation takes the generation lease for its whole run: while a
        // lease is held elsewhere it cannot start, and once released it
        // completes without deadlocking.
        Assert.Equal(
            GenerationLeaseStatus.Acquired,
            catalog.TryAcquireGenerationLease(
                leased.Generation,
                out IDisposable? acquired));
        SignatureSpellabilityAggregate? held = null;
        var evaluator = new Thread(() =>
            held = leased.EvaluateSignatureSpellability(plan))
        {
            IsBackground = true,
        };
        using (IDisposable? lease = acquired)
        {
            Assert.NotNull(lease);
            evaluator.Start();
            Assert.False(evaluator.Join(TimeSpan.FromMilliseconds(250)));
        }

        Assert.True(evaluator.Join(TimeSpan.FromMinutes(1)));
        Assert.IsType<SignatureSpellabilityAggregate.Complete>(held);

        // Under a concurrent publisher, every evaluation is either wholly
        // current or wholly rejected; evidence is never mixed across
        // generations.
        using var stop = new ManualResetEventSlim(false);
        Exception? workerFailure = null;
        var worker = new Thread(() =>
        {
            try
            {
                var policy = new MapPolicy(target);
                while (!stop.IsSet)
                {
                    using TypeResolutionContext replacement =
                        catalog.CreateSignatureSpellabilityContext(
                            policy,
                            plan,
                            [target]);
                }
            }
            catch (Exception ex)
            {
                workerFailure = ex;
            }
        })
        {
            IsBackground = true,
        };
        worker.Start();

        int complete = 0;
        int stale = 0;
        try
        {
            for (int iteration = 0; iteration < 1_000; iteration++)
            {
                var policy = new MapPolicy(target);
                using TypeResolutionContext context =
                    catalog.CreateSignatureSpellabilityContext(
                        policy,
                        plan,
                        [target]);
                switch (context.EvaluateSignatureSpellability(plan))
                {
                    case SignatureSpellabilityAggregate.Complete aggregate:
                        complete++;
                        // A complete aggregate never mixes generations: its
                        // accessibility answer came from the same generation
                        // that resolved the definition.
                        Assert.IsType<
                            TypeDefinitionAccessibilityOutcome.Accessible>(
                                Assert.IsType<
                                    SignatureSpellabilityEvidence
                                        .ExternalDefinition>(
                                            Assert.Single(
                                                aggregate.Evidence))
                                .Accessibility);
                        break;
                    case SignatureSpellabilityAggregate.Rejected rejected:
                        stale++;
                        Assert.IsType<
                            SignatureSpellabilityEvaluationFailure
                                .StaleSource>(rejected.Failure);
                        break;
                }
            }
        }
        finally
        {
            stop.Set();
            Assert.True(worker.Join(TimeSpan.FromMinutes(1)));
        }

        Assert.Null(workerFailure);
        Assert.Equal(1_000, complete + stale);
        Assert.NotEqual(0, complete);
    }

    static void AssertNestedAccessibility(
        bool nestedPublic,
        bool expected,
        bool outerPublic = true)
    {
        byte[] targetImage = BuildNestedTarget(
            "Target",
            nestedPublic,
            outerPublic);
        SyntheticAssembly source = BuildReferenceSource(
            "Source",
            ReadIdentity(targetImage),
            nested: true);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        using var catalog = new TypeResolutionCatalog();
        SignatureSpellabilityPlan plan = Plan(
            catalog,
            Descriptor(source.Image),
            source.Coordinates);
        using TypeResolutionContext context =
            catalog.CreateSignatureSpellabilityContext(
                new MapPolicy(target),
                plan,
                [target]);

        var aggregate =
            Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                context.EvaluateSignatureSpellability(plan));
        var external = Assert.IsType<
            SignatureSpellabilityEvidence.ExternalDefinition>(
                Assert.Single(aggregate.Evidence));
        if (expected)
        {
            Assert.IsType<TypeDefinitionAccessibilityOutcome.Accessible>(
                external.Accessibility);
        }
        else
        {
            Assert.IsType<TypeDefinitionAccessibilityOutcome.Inaccessible>(
                external.Accessibility);
        }
        Assert.Equal(expected, aggregate.CanSpell());
    }

    static SignatureSpellabilityEvidence.Unresolved Unresolved(
        TypeResolutionContext context,
        SignatureSpellabilityPlan plan) =>
        Assert.IsType<SignatureSpellabilityEvidence.Unresolved>(
            Assert.Single(
                Assert.IsType<SignatureSpellabilityAggregate.Complete>(
                    context.EvaluateSignatureSpellability(plan))
                .Evidence));

    static SignatureSpellabilityPlan Plan(
        TypeResolutionCatalog catalog,
        CompiledMember member) =>
        Plan(catalog, member.Source, member.Coordinates);

    static SignatureSpellabilityPlan Plan(
        TypeResolutionCatalog catalog,
        ResolvedAssemblyReference source,
        SubjectCoordinates coordinates,
        AssemblyResolutionScope scope = AssemblyResolutionScope.Any) =>
        Assert.IsType<SignatureSpellabilityPlanOutcome.Planned>(
            catalog.PlanSignatureSpellability(
                Subject(catalog, source, coordinates),
                scope)).Plan;

    static SignatureSpellabilitySubject Subject(
        TypeResolutionCatalog catalog,
        ResolvedAssemblyReference source,
        SubjectCoordinates coordinates) =>
        Assert.IsType<SignatureSpellabilitySubjectOutcome.Created>(
            catalog.CreateSignatureSpellabilitySubject(
                source,
                coordinates.DeclaringTypeToken,
                coordinates.MemberToken,
                coordinates.MemberKind,
                coordinates.ModuleVersionId)).Subject;

    static SignatureSpellabilityPlanFailure SubjectFailure(
        TypeResolutionCatalog catalog,
        ResolvedAssemblyReference source,
        SubjectCoordinates coordinates) =>
        Assert.IsType<SignatureSpellabilitySubjectOutcome.Rejected>(
            catalog.CreateSignatureSpellabilitySubject(
                source,
                coordinates.DeclaringTypeToken,
                coordinates.MemberToken,
                coordinates.MemberKind,
                coordinates.ModuleVersionId)).Failure;

    static CompiledMember Compiled(
        string memberName,
        SignatureSpellabilityMemberKind kind =
            SignatureSpellabilityMemberKind.Method)
    {
        string path =
            typeof(SignatureSpellabilityConsumerFixtures).Assembly.Location;
        var stream = File.OpenRead(path);
        var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle declaring = FindType(
            reader,
            typeof(SignatureSpellabilityConsumerFixtures)
                .FullName!.Replace('+', '.'));
        int memberToken = kind switch
        {
            SignatureSpellabilityMemberKind.Field =>
                MetadataTokens.GetToken(
                    FindField(reader, declaring, memberName)),
            SignatureSpellabilityMemberKind.Property =>
                MetadataTokens.GetToken(
                    FindProperty(reader, declaring, memberName)),
            SignatureSpellabilityMemberKind.Method =>
                MetadataTokens.GetToken(
                    FindMethod(reader, declaring, memberName)),
            _ => throw new InvalidOperationException(),
        };
        TypeDefinitionHandle otherDeclaring = FindType(
            reader,
            typeof(ConstructedVisibleString)
                .FullName!.Replace('+', '.'));
        Guid mvid =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        return new CompiledMember(
            stream,
            pe,
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("compiled fixture")),
            new SubjectCoordinates(
                mvid,
                MetadataTokens.GetToken(declaring),
                memberToken,
                kind),
            MetadataTokens.GetToken(otherDeclaring));
    }

    static TypeDefinitionHandle FindType(
        MetadataReader reader,
        string fullName)
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (reader.GetFullTypeName(reader.GetTypeDefinition(handle))
                == fullName)
            {
                return handle;
            }
        }
        throw new InvalidOperationException($"Type '{fullName}' not found.");
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        TypeDefinitionHandle type,
        string name)
    {
        foreach (MethodDefinitionHandle handle
            in reader.GetTypeDefinition(type).GetMethods())
        {
            if (reader.GetString(
                    reader.GetMethodDefinition(handle).Name)
                == name)
            {
                return handle;
            }
        }
        throw new InvalidOperationException($"Method '{name}' not found.");
    }

    static FieldDefinitionHandle FindField(
        MetadataReader reader,
        TypeDefinitionHandle type,
        string name)
    {
        foreach (FieldDefinitionHandle handle
            in reader.GetTypeDefinition(type).GetFields())
        {
            if (reader.GetString(
                    reader.GetFieldDefinition(handle).Name)
                == name)
            {
                return handle;
            }
        }
        throw new InvalidOperationException($"Field '{name}' not found.");
    }

    static PropertyDefinitionHandle FindProperty(
        MetadataReader reader,
        TypeDefinitionHandle type,
        string name)
    {
        foreach (PropertyDefinitionHandle handle
            in reader.GetTypeDefinition(type).GetProperties())
        {
            if (reader.GetString(
                    reader.GetPropertyDefinition(handle).Name)
                == name)
            {
                return handle;
            }
        }
        throw new InvalidOperationException(
            $"Property '{name}' not found.");
    }

    static SyntheticAssembly BuildScopeSignature()
    {
        var metadata = Base("Scopes", out Guid mvid);
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("External"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        ModuleReferenceHandle module = metadata.AddModuleReference(
            metadata.GetOrAddString("other.netmodule"));
        TypeDefinitionHandle local = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Local"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeReferenceHandle external = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("External"));
        TypeReferenceHandle moduleType = metadata.AddTypeReference(
            module,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Module"));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        var signature = MethodSignature(
            Primitive(0x01),
            Primitive(0x08),
            Class(local),
            Class(external),
            Class(moduleType));
        MethodDefinitionHandle method =
            AddMethod(metadata, signature);
        return Synthetic(
            metadata,
            mvid,
            declaring,
            method);
    }

    static SyntheticAssembly BuildTwoReferenceSignature()
    {
        var metadata = Base("Scopes", out Guid mvid);
        AssemblyReferenceHandle platform = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Platform"),
            new Version(1, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                Convert.FromHexString("b03f5f7f11d50a3a")),
            default,
            default);
        AssemblyReferenceHandle package = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Package"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle first = metadata.AddTypeReference(
            platform,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("First"));
        TypeReferenceHandle second = metadata.AddTypeReference(
            package,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Second"));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(Primitive(0x01), Class(first), Class(second)));
        return Synthetic(metadata, mvid, declaring, method);
    }

    static SyntheticAssembly BuildReferenceSource(
        string assemblyName,
        AssemblyReferenceIdentity reference,
        bool nested = false,
        string typeName = "Type")
    {
        var metadata = Base(assemblyName, out Guid mvid);
        AssemblyReferenceHandle assembly =
            AddAssemblyReference(metadata, reference);
        TypeReferenceHandle type = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(
                nested ? "Outer" : typeName));
        if (nested)
        {
            type = metadata.AddTypeReference(
                type,
                default,
                metadata.GetOrAddString("Inner"));
        }
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(Class(type)));
        return Synthetic(metadata, mvid, declaring, method);
    }

    /// <summary>
    /// Two calls with one MVID produce two distinct images whose identically
    /// numbered method rows reference differently named types.
    /// </summary>
    static SyntheticAssembly BuildSharedModuleSource(
        Guid moduleVersionId,
        string typeName)
    {
        MetadataBuilder metadata = Base(
            typeName + "Source",
            out Guid mvid,
            moduleVersionId: moduleVersionId);
        AssemblyReferenceHandle assembly = AddAssemblyReference(
            metadata,
            Identity("Ext"));
        TypeReferenceHandle type = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(typeName));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(Class(type)));
        return Synthetic(metadata, mvid, declaring, method);
    }

    /// <summary>
    /// A source assembly whose own <c>AssemblyDef</c> carries the ECMA public
    /// key, so the catalog authorizes it under
    /// <see cref="AssemblyResolutionScope.Platform"/> even though the type it
    /// references is not itself platform-signed.
    /// </summary>
    static SyntheticAssembly BuildPlatformKeySource(
        string assemblyName,
        AssemblyReferenceIdentity reference)
    {
        MetadataBuilder metadata = Base(
            assemblyName,
            out Guid mvid,
            publicKey: Convert.FromHexString(
                "00000000000000000400000000000000"));
        AssemblyReferenceHandle assembly =
            AddAssemblyReference(metadata, reference);
        TypeReferenceHandle type = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(Class(type)));
        return Synthetic(metadata, mvid, declaring, method);
    }

    /// <summary>
    /// A method signature whose return type carries a required custom modifier
    /// naming a nested <c>TypeSpec</c>. The TypeSpec is either fully consumed
    /// by the single-Type grammar, followed by one unconsumed byte, or
    /// truncated after its <c>SZARRAY</c> prefix.
    /// </summary>
    static SyntheticAssembly BuildTypeSpecSource(
        string assemblyName,
        TypeSpecShape shape)
    {
        MetadataBuilder metadata = Base(assemblyName, out Guid mvid);
        AssemblyReferenceHandle assembly = AddAssemblyReference(
            metadata,
            Identity("Ext"));
        TypeReferenceHandle element = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Element"));
        var specification = new BlobBuilder();
        specification.WriteByte(0x1d);              // ELEMENT_TYPE_SZARRAY
        if (shape != TypeSpecShape.Truncated)
            specification.LinkSuffix(Class(element));
        if (shape == TypeSpecShape.Trailing)
            specification.WriteByte(0x00);
        TypeSpecificationHandle spec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(specification));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        var returnType = new BlobBuilder();
        returnType.WriteByte(0x1f);                 // ELEMENT_TYPE_CMOD_REQD
        returnType.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(spec));
        returnType.WriteByte(0x08);                 // ELEMENT_TYPE_I4
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(returnType));
        return Synthetic(metadata, mvid, declaring, method);
    }

    static SyntheticAssembly BuildTypeSpecDagSource(
        string assemblyName,
        int branchingLevels,
        bool namedOccurrences = true,
        int wrapperLevels = 0)
    {
        MetadataBuilder metadata = Base(assemblyName, out Guid mvid);
        AssemblyReferenceHandle assembly = AddAssemblyReference(
            metadata,
            Identity("Ext"));
        TypeReferenceHandle leaf = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Leaf"));
        var terminal = new BlobBuilder();
        if (namedOccurrences)
        {
            terminal.LinkSuffix(Class(leaf));
        }
        else
        {
            terminal.WriteByte(0x13);
            terminal.WriteCompressedInteger(0);
        }
        TypeSpecificationHandle child = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(terminal));
        for (int level = 0; level < branchingLevels; level++)
        {
            var branch = new BlobBuilder();
            branch.WriteByte(0x1f);
            branch.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(child));
            branch.WriteByte(0x1f);
            branch.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(child));
            if (namedOccurrences)
            {
                branch.WriteByte(0x08);
            }
            else
            {
                branch.WriteByte(0x13);
                branch.WriteCompressedInteger(0);
            }
            child = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(branch));
        }
        for (int level = 0; level < wrapperLevels; level++)
        {
            var wrapper = new BlobBuilder();
            wrapper.WriteByte(0x1f);
            wrapper.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(child));
            wrapper.WriteByte(0x08);
            child = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(wrapper));
        }

        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        var returnType = new BlobBuilder();
        returnType.WriteByte(0x1f);
        returnType.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(child));
        returnType.WriteByte(0x08);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(returnType));
        return Synthetic(metadata, mvid, declaring, method);
    }

    enum TypeSpecShape
    {
        Complete,
        Trailing,
        Truncated,
    }

    enum ModifierShape
    {
        OptionalOnly,
        RequiredOnly,
        Mixed,
    }

    static SyntheticAssembly BuildDuplicateReferenceSource(
        AssemblyReferenceIdentity reference)
    {
        var metadata = Base("Duplicate", out Guid mvid);
        AssemblyReferenceHandle assembly =
            AddAssemblyReference(metadata, reference);
        TypeReferenceHandle type = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(Class(type), Class(type)));
        return Synthetic(metadata, mvid, declaring, method);
    }

    static SyntheticAssembly BuildAliasSource(
        AssemblyReferenceIdentity firstIdentity,
        AssemblyReferenceIdentity secondIdentity)
    {
        var metadata = Base("Aliases", out Guid mvid);
        AssemblyReferenceHandle firstAssembly =
            AddAssemblyReference(metadata, firstIdentity);
        AssemblyReferenceHandle secondAssembly =
            AddAssemblyReference(metadata, secondIdentity);
        TypeReferenceHandle first = metadata.AddTypeReference(
            firstAssembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"));
        TypeReferenceHandle second = metadata.AddTypeReference(
            secondAssembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(Class(first), Class(second)));
        return Synthetic(metadata, mvid, declaring, method);
    }

    static SyntheticAssembly BuildModifierSource(
        string assemblyName,
        AssemblyReferenceIdentity reference,
        ModifierShape shape)
    {
        var metadata = Base(assemblyName, out Guid mvid);
        AssemblyReferenceHandle assembly =
            AddAssemblyReference(metadata, reference);
        TypeReferenceHandle modifier = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        BlobBuilder Optional()
        {
            var value = new BlobBuilder();
            value.WriteByte(0x20);
            value.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(modifier));
            value.WriteByte(0x08);
            return value;
        }
        BlobBuilder Required()
        {
            var value = new BlobBuilder();
            value.WriteByte(0x1f);
            value.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(modifier));
            value.WriteByte(0x08);
            return value;
        }
        MethodDefinitionHandle method = AddMethod(
            metadata,
            shape switch
            {
                ModifierShape.OptionalOnly => MethodSignature(Optional()),
                ModifierShape.RequiredOnly => MethodSignature(Required()),
                ModifierShape.Mixed => MethodSignature(
                    Optional(),
                    Required(),
                    Class(modifier)),
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            });
        return Synthetic(metadata, mvid, declaring, method);
    }

    static SyntheticAssembly BuildCurrentForwarderSource(
        string assemblyName,
        AssemblyReferenceIdentity target)
    {
        var metadata = Base(assemblyName, out Guid mvid);
        AssemblyReferenceHandle assembly =
            AddAssemblyReference(metadata, target);
        ExportedTypeHandle outer = metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer"),
            assembly,
            0);
        metadata.AddExportedType(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Inner"),
            outer,
            0);
        TypeReferenceHandle outerReference = metadata.AddTypeReference(
            default(EntityHandle),
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer"));
        TypeReferenceHandle innerReference = metadata.AddTypeReference(
            outerReference,
            default,
            metadata.GetOrAddString("Inner"));
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        MethodDefinitionHandle method = AddMethod(
            metadata,
            MethodSignature(Class(innerReference)));
        return Synthetic(metadata, mvid, declaring, method);
    }

    static SyntheticAssembly BuildMalformedSignature()
    {
        var metadata = Base("Malformed", out Guid mvid);
        TypeDefinitionHandle declaring = AddDeclaringType(metadata);
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        MethodDefinitionHandle method =
            AddMethod(metadata, signature);
        return Synthetic(
            metadata,
            mvid,
            declaring,
            method,
            suppressValidation: true);
    }

    static byte[] BuildVisibilityTarget(
        string assemblyName,
        bool publicType,
        string typeName = "Type",
        Version? version = null)
    {
        var metadata = Base(assemblyName, out _, version);
        metadata.AddTypeDefinition(
            publicType ? TypeAttributes.Public : TypeAttributes.NotPublic,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(typeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildNestedTarget(
        string assemblyName,
        bool nestedPublic,
        bool outerPublic = true)
    {
        var metadata = Base(assemblyName, out _);
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            outerPublic ? TypeAttributes.Public : TypeAttributes.NotPublic,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            nestedPublic
                ? TypeAttributes.NestedPublic
                : TypeAttributes.NestedPrivate,
            default,
            metadata.GetOrAddString("Inner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(inner, outer);
        return Serialize(metadata);
    }

    static byte[] BuildInvalidVisibilityTarget()
    {
        var metadata = Base("MalformedVisibility", out _);
        metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata, suppressValidation: true);
    }

    static byte[] BuildDuplicateTarget(string assemblyName)
    {
        var metadata = Base(assemblyName, out _);
        for (int i = 0; i < 2; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildForwarder(
        string assemblyName,
        AssemblyReferenceIdentity target)
    {
        var metadata = Base(assemblyName, out _);
        AssemblyReferenceHandle assembly =
            AddAssemblyReference(metadata, target);
        metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            assembly,
            0);
        return Serialize(metadata);
    }

    static byte[] BuildCyclicDeclarationTarget(string assemblyName)
    {
        var metadata = Base(assemblyName, out _);
        metadata.AddExportedType(
            Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("First"),
            MetadataTokens.ExportedTypeHandle(2),
            0);
        metadata.AddExportedType(
            Forwarder,
            default,
            metadata.GetOrAddString("Second"),
            MetadataTokens.ExportedTypeHandle(1),
            0);
        return Serialize(metadata, suppressValidation: true);
    }

    static byte[] BuildRelationshipBudgetTarget(string assemblyName)
    {
        var metadata = Base(assemblyName, out _);
        for (int row = 1;
            row <= MetadataSafetyPolicy.MaxRelationshipNodes + 1;
            row++)
        {
            metadata.AddExportedType(
                Forwarder,
                row == MetadataSafetyPolicy.MaxRelationshipNodes + 1
                    ? metadata.GetOrAddString("N")
                    : default,
                metadata.GetOrAddString($"Type{row}"),
                row == MetadataSafetyPolicy.MaxRelationshipNodes + 1
                    ? AddAssemblyReference(
                        metadata,
                        Identity("Terminal"))
                    : MetadataTokens.ExportedTypeHandle(row + 1),
                0);
        }
        return Serialize(metadata, suppressValidation: true);
    }

    static MetadataBuilder Base(
        string assemblyName,
        out Guid mvid,
        Version? version = null,
        Guid? moduleVersionId = null,
        byte[]? publicKey = null)
    {
        mvid = moduleVersionId ?? Guid.NewGuid();
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            version ?? new Version(1, 0, 0, 0),
            default,
            publicKey is null
                ? default
                : metadata.GetOrAddBlob(publicKey),
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static TypeDefinitionHandle AddDeclaringType(
        MetadataBuilder metadata) =>
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static MethodDefinitionHandle AddMethod(
        MetadataBuilder metadata,
        BlobBuilder signature) =>
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            -1,
            MetadataTokens.ParameterHandle(1));

    static BlobBuilder MethodSignature(params BlobBuilder[] types)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(Math.Max(0, types.Length - 1));
        foreach (BlobBuilder type in types)
            signature.LinkSuffix(type);
        return signature;
    }

    static BlobBuilder Primitive(byte elementType)
    {
        var value = new BlobBuilder();
        value.WriteByte(elementType);
        return value;
    }

    static BlobBuilder Class(EntityHandle handle)
    {
        var value = new BlobBuilder();
        value.WriteByte(0x12);
        value.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(handle));
        return value;
    }

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        AssemblyReferenceIdentity identity) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(identity.Name),
            identity.Version ?? new Version(1, 0, 0, 0),
            string.IsNullOrEmpty(identity.Culture)
                ? default
                : metadata.GetOrAddString(identity.Culture),
            identity.PublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(
                    Convert.FromHexString(identity.PublicKeyToken)),
            default,
            default);

    static SyntheticAssembly Synthetic(
        MetadataBuilder metadata,
        Guid mvid,
        TypeDefinitionHandle declaring,
        MethodDefinitionHandle method,
        bool suppressValidation = false) =>
        new(
            Serialize(metadata, suppressValidation),
            new SubjectCoordinates(
                mvid,
                MetadataTokens.GetToken(declaring),
                MetadataTokens.GetToken(method),
                SignatureSpellabilityMemberKind.Method));

    static byte[] Serialize(
        MetadataBuilder metadata,
        bool suppressValidation = false)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: suppressValidation),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ResolvedAssemblyReference Descriptor(
        byte[] image,
        Action? opened = null) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
            {
                opened?.Invoke();
                return new MemoryStream(image, writable: false);
            },
            provenance: AssemblyResolutionProvenance.Local("test"));

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static AssemblyReferenceIdentity Identity(
        string name,
        Version? version = null) =>
        new(name, version ?? new Version(1, 0, 0, 0), null, null);

    static MetadataTypeDefinitionName TypeName(string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", [name])).Name;

    sealed class MapPolicy : IAssemblyBindingPolicy
    {
        readonly Func<AssemblyBindingRequest, AssemblyBindingSelection>
            _selection;

        internal MapPolicy(params ResolvedAssemblyReference[] assemblies)
        {
            var byName = assemblies.ToDictionary(
                assembly => assembly.Identity.Name,
                StringComparer.Ordinal);
            _selection = request =>
                request.Target
                    is AssemblyBindingTarget.AssemblyReference reference
                    && byName.TryGetValue(
                        reference.Identity.Name,
                        out ResolvedAssemblyReference? assembly)
                    ? AssemblyBindingSelection.Found(assembly)
                    : AssemblyBindingSelection.NotFound();
        }

        internal MapPolicy(
            Func<AssemblyBindingRequest, AssemblyBindingSelection> selection)
            => _selection = selection;

        public AssemblyBindingPolicyVersion Version { get; } = new();
        public List<AssemblyBindingRequest> Requests { get; } = [];

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            Requests.Add(request);
            return _selection(request);
        }
    }

    sealed record SyntheticAssembly(
        byte[] Image,
        SubjectCoordinates Coordinates);

    sealed record SubjectCoordinates(
        Guid ModuleVersionId,
        int DeclaringTypeToken,
        int MemberToken,
        SignatureSpellabilityMemberKind MemberKind);

    sealed record CompiledMember(
        Stream Stream,
        PEReader Pe,
        ResolvedAssemblyReference Source,
        SubjectCoordinates Coordinates,
        int OtherDeclaringTypeToken) : IDisposable
    {
        public void Dispose()
        {
            Pe.Dispose();
            Stream.Dispose();
        }
    }
}
