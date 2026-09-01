using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Fixtures;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research.Tests;

/// <summary>
/// Gates for the Research-owned side-local target request and terminal attempt
/// boundary described by <c>docs/design/implementation-diff.md</c> under
/// <c>Side-local requests and attempts</c>.
/// </summary>
public class ResearchTargetResolverTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
    const string SampleType = "ILInspector.Research.TargetFixtures.TargetSample";
    const string NestedType =
        "ILInspector.Research.TargetFixtures.TargetOuter.TargetInner";
    const string ForwardedType = "System.Uri";
    const string AbsentType = "ILInspector.Research.TargetFixtures.NotHere";
    const string DiffType = "DiffFixtureSample.DiffSample";

    // --------------------------------------------------------------- requests

    [Fact]
    public void ResearchTargetRequests_AreStrictlySideInputAndScopeLocal()
    {
        // Two questions, each with one Before and one After sample input, plus
        // a second unrelated domain in the first question. Two selection
        // occurrences share the first question.
        TargetFixture fixture = TargetFixture.Create(
            [
                (Sample(), Sample(), Diff(FixtureCatalog.DiffV1)),
                (Sample(), Sample(), null),
            ]);

        ResearchCarriedMemberSelection first = fixture.Carried(0, SampleType, "Method");
        ResearchCarriedMemberSelection second = fixture.Carried(0, SampleType, "Value");
        ResearchCarriedMemberSelection third = fixture.Carried(1, SampleType, "Method");
        ResearchTargetResolution resolution = fixture.Resolve(first, second, third);

        Assert.Same(fixture.Population.Operation, resolution.Operation);
        Assert.Equal(3, resolution.Scopes.Length);

        foreach (ResearchTargetScope scope in resolution.Scopes)
        {
            foreach (ResearchTargetDomain domain in scope.Domains)
            {
                Assert.Same(scope.Id, domain.Id.Scope);
                foreach (ResearchTargetRequest request in domain.Requests)
                {
                    // Every parent identity is the request's own.
                    Assert.Same(domain.Id, request.Id.Domain);
                    Assert.Same(scope.Id, request.Scope);
                    Assert.Same(scope.Question, request.Question);
                    Assert.Same(resolution.Operation, request.Operation);
                    Assert.Same(request.Input.Question, request.Question);
                    Assert.Equal(request.Input.Side, request.Side);

                    // The request retains no admitted input, occurrence,
                    // descriptor, resolver, or body index; only its intent and
                    // the pinned surface scope.
                    Assert.Same(
                        ResearchTargetSurfaceScope.MetadataApiSurface,
                        request.Surface);
                    Assert.True(request.Surface.IncludeNonPublic);
                    Assert.True(
                        request.Surface
                            .IncludeCompilerGeneratedTypesAndFields);
                    Assert.False(request.Surface.TypesOnly);
                    Assert.Null(request.Surface.KindFilter);
                    Assert.Equal(
                        scope.DeclaringTypeFullName,
                        request.DeclaringTypeFullName);
                    Assert.Equal(scope.Selector, request.Selector);
                }
            }
        }

        // No request fans across scopes, and identities never repeat.
        Assert.Equal(
            resolution.Requests.Length,
            Distinct(resolution.Requests.Select(request => request.Id)));
        Assert.Equal(
            resolution.Domains.Length,
            Distinct(resolution.Domains.Select(domain => domain.Id)));

        // Domain-side planning is total for every scope: one closed
        // disposition per admitted input of that scope's question.
        foreach ((ResearchTargetScope scope, int questionIndex) in
            resolution.Scopes.Select(
                (scope, index) => (scope, index switch { 2 => 1, _ => 0 })))
        {
            ResearchAdmittedQuestion question =
                fixture.Population.Questions[questionIndex];
            Assert.Equal(
                question.Inputs.Select(input => input.Id).ToHashSet(),
                scope.Domains
                    .SelectMany(domain => domain.Inputs)
                    .Select(disposition => disposition.Input)
                    .ToHashSet());
            Assert.All(
                scope.Domains.SelectMany(domain => domain.Inputs),
                disposition => Assert.Equal(
                    ResearchTargetDispositionKind.Requested,
                    disposition.Kind));
        }

        // An empty side is distinguishable from a side with an input: the diff
        // domain of question 0 has a Before input and no After input.
        ResearchTargetDomain diffDomain = resolution.Scopes[0].Domains
            .Single(domain => domain.Key.Identity.Name == "DiffFixtureSample");
        Assert.Single(diffDomain.Side(ResearchComparisonSide.Before));
        Assert.Empty(diffDomain.Side(ResearchComparisonSide.After));
    }

    [Fact]
    public void ResearchTargetRequests_CarriedRoleIsDerivedOnlyAfterResolution()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), Sample(), null)]);

        // Carried requests assert no role at all.
        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Value:2"));
        Assert.All(
            resolution.Requests,
            request =>
            {
                Assert.Equal(ResearchTargetRequestKind.Carried, request.Kind);
                Assert.Null(request.AssertedRole);
                Assert.Null(request.AssertedAddress);
            });

        // The role appears only on the terminal outcome, derived from the
        // selected member's setter MethodDef token, and both sides derive it
        // independently rather than borrowing across the question.
        Assert.Equal(2, resolution.Attempts.Length);
        Assert.All(
            resolution.Attempts,
            attempt => Assert.Equal(
                ResearchTargetRelationshipRole.Setter,
                Resolved(attempt).Role));

        // An ordinal never becomes a role: the same ordinal on a member with no
        // accessors resolves to Method, not Getter.
        ResearchTargetResolution methodResolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Method"));
        Assert.All(
            methodResolution.Attempts,
            attempt => Assert.Equal(
                ResearchTargetRelationshipRole.Method,
                Resolved(attempt).Role));
    }

    // --------------------------------------------------------------- attempts

    [Fact]
    public void ResearchTargetAttempts_AccountForEveryRequestExactlyOnce()
    {
        TargetFixture fixture = TargetFixture.Create(
            [
                (Sample(), Sample(), Diff(FixtureCatalog.DiffV1)),
                (Sample(), null, null),
            ]);

        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Method"),
            fixture.Carried(1, SampleType, "Method"));

        Assert.Equal(resolution.Requests.Length, resolution.Attempts.Length);
        Assert.Equal(
            resolution.Attempts.Length,
            Distinct(resolution.Attempts.Select(attempt => attempt.Id)));

        HashSet<ResearchTargetRequestId> claimed = new(
            ReferenceEqualityComparer.Instance);
        foreach (ResearchTargetAttempt attempt in resolution.Attempts)
        {
            Assert.Same(attempt.Request.Id, attempt.Id.Request);
            Assert.True(claimed.Add(attempt.Request.Id));
            Assert.Same(attempt, resolution.GetAttempt(attempt.Request.Id));
            Assert.NotNull(attempt.Outcome);
        }

        Assert.Equal(
            resolution.Requests.Select(request => request.Id).ToHashSet(
                ReferenceEqualityComparer.Instance
                    as IEqualityComparer<ResearchTargetRequestId>),
            claimed);

        // A request from another resolution has no attempt here.
        ResearchTargetResolution other = TargetFixture
            .Create([(Sample(), null, null)])
            .ResolveDefault(SampleType, "Method");
        Assert.False(
            resolution.TryGetAttempt(other.Requests[0].Id, out _));
        Assert.Throws<ArgumentException>(
            () => resolution.GetAttempt(other.Requests[0].Id));
    }

    [Fact]
    public void ResearchTargetFinalValidation_RejectsBrokenSemanticBindings()
    {
        TargetFixture fixture = TargetFixture.Create([(Sample(), null, null)]);
        ResearchCarriedMemberSelection selection =
            fixture.Carried(0, SampleType, "Method");
        ResearchTargetPlanningRequest planning = new(
            fixture.Population,
            fixture.Roles,
            [selection]);
        ResearchTargetResolution valid =
            Assert.IsType<ResearchTargetPlanningOutcome.Planned>(
                ResearchTargetResolver.Resolve(
                    planning,
                    TestContext.Current.CancellationToken)).Resolution;

        ResearchTargetScope scope = Assert.Single(valid.Scopes);
        ResearchTargetDomain domain = Assert.Single(scope.Domains);
        ResearchTargetRequest request = Assert.Single(domain.Requests);
        ResearchTargetOutcome.Resolved resolved =
            Resolved(Assert.Single(domain.Attempts));
        ResearchAdmittedInput input = Assert.Single(fixture.Population.Inputs);
        MemberTargetResolution metadata = new(
            resolved.Target,
            Diagnostic: null,
            resolved.Candidates);
        var surface = new ApiSurface();
        surface.Types.Add(resolved.Target.ApiType);
        ResearchTargetInputValidationEvidence inputEvidence = new(
            ReadFailed: false,
            IsAssembly: true,
            resolved.Module.AssemblyIdentity,
            resolved.Module.ModuleVersionId,
            ArtifactModuleVersionId: null,
            MethodDefinitionCount: int.MaxValue,
            surface);

        Rejects(
            new ResearchTargetOutcome.Resolved(
                resolved.Target,
                resolved.Address,
                ResearchTargetRelationshipRole.Getter,
                resolved.Module,
                resolved.Candidates));
        Rejects(
            new ResearchTargetOutcome.Resolved(
                resolved.Target,
                resolved.Address,
                resolved.Role,
                LibraryBodyIndex.Open(
                    FixtureCatalog.DiffV1.AssemblyPath()).ModuleIdentity,
                resolved.Candidates));
        Rejects(
            new ResearchTargetOutcome.Resolved(
                resolved.Target,
                resolved.Address,
                resolved.Role,
                resolved.Module,
                []));
        Rejects(
            new ResearchTargetOutcome.NotFound(
                new MemberTargetDiagnostic(
                    MemberTargetDiagnosticKind.MissingMember,
                    "synthetic mismatch",
                    []),
                researchDiagnostic: null,
                candidates: []));
        Rejects(resolved, corruptDispositionRequest: true);
        resolved.Target.ApiMember.Member.GetterToken =
            resolved.Target.ApiMember.Member.MetadataToken;
        Rejects(resolved);
        resolved.Target.ApiMember.Member.GetterToken = null;
        Rejects(
            new ResearchTargetOutcome.Failed(
                Diagnostic(ResearchTargetDiagnosticKind.ResolutionFailed)),
            evidenceOverride: new(
                ReadFailed: true,
                IsAssembly: false,
                LiveAssemblyIdentity: null,
                LiveModuleVersionId: Guid.Empty,
                ArtifactModuleVersionId: null,
                MethodDefinitionCount: 0,
                Surface: null),
            omitMetadataEvidence: true);

        ImmutableArray<ResearchTargetValidationEvidence> validEvidence =
        [
            new(
                request,
                input,
                ResearchTargetInputRole.Implementation,
                inputEvidence,
                resolved.Target.ApiType,
                metadata,
                TargetResolutionFailed: false,
                resolved),
        ];
        var beforeOnly = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.BeforeOnly>(
                Assert.Single(valid.Correspondences));

        RejectsProjection(
            valid.Censuses.RemoveAt(valid.Censuses.Length - 1),
            valid.Correspondences);
        RejectsProjection(valid.Censuses, []);
        RejectsProjection(
            valid.Censuses,
            [.. valid.Correspondences, beforeOnly]);

        var staleKey = new ResearchTargetCorrespondenceKey(
            beforeOnly.Scope,
            beforeOnly.DomainId,
            beforeOnly.Before.CorrespondenceKey.Role,
            beforeOnly.Before.CorrespondenceKey.CanonicalIdentity + "-stale");
        RejectsProjection(
            valid.Censuses,
            [
                new ResearchTargetCorrespondenceOutcome.BeforeOnly(
                    beforeOnly.Domain,
                    beforeOnly.Before,
                    new ResearchTargetKeyAbsenceProof(
                        beforeOnly.AfterAbsence.Census,
                        staleKey,
                        ResearchTargetAbsenceEvidenceKind.NoAdmittedInput,
                        notFoundAttempt: null)),
            ]);

        ResearchTargetDomainSideCensus wrongCensus =
            valid.Censuses.Single(
                census =>
                    census.Side == ResearchComparisonSide.Before);
        RejectsProjection(
            valid.Censuses,
            [
                new ResearchTargetCorrespondenceOutcome.BeforeOnly(
                    beforeOnly.Domain,
                    beforeOnly.Before,
                    new ResearchTargetKeyAbsenceProof(
                        wrongCensus,
                        beforeOnly.Before.CorrespondenceKey,
                        ResearchTargetAbsenceEvidenceKind.NoAdmittedInput,
                        notFoundAttempt: null)),
            ]);

        void RejectsProjection(
            ImmutableArray<ResearchTargetDomainSideCensus> censuses,
            ImmutableArray<ResearchTargetCorrespondenceOutcome>
                correspondences)
        {
            ResearchTargetResolution corrupted = new(
                valid.Operation,
                valid.Scopes,
                censuses,
                correspondences);
            Assert.Throws<InvalidOperationException>(
                () => ResearchTargetResolutionValidator.Validate(
                    planning,
                    corrupted,
                    validEvidence));
        }

        void Rejects(
            ResearchTargetOutcome corrupted,
            bool corruptDispositionRequest = false,
            ResearchTargetInputValidationEvidence? evidenceOverride = null,
            bool omitMetadataEvidence = false)
        {
            ResearchTargetAttempt attempt = new(
                new ResearchTargetAttemptId(request.Id),
                request,
                corrupted);
            ImmutableArray<ResearchTargetInputDisposition> dispositions =
                domain.Inputs;
            if (corruptDispositionRequest)
            {
                dispositions =
                [
                    new(
                        input.Id,
                        ResearchTargetInputRole.Implementation,
                        ResearchTargetDispositionKind.Requested,
                        notRequestedReason: null,
                        new ResearchTargetRequestId(domain.Id, input.Id)),
                ];
            }

            ResearchTargetDomain corruptedDomain = new(
                domain.Id,
                domain.Key,
                dispositions,
                domain.ConflictingInputs,
                [request],
                [attempt]);
            ResearchTargetScope corruptedScope = new(
                scope.Id,
                scope.DeclaringTypeFullName,
                scope.Selector,
                scope.Kind,
                [corruptedDomain]);
            ResearchTargetResolution corruptedResolution = new(
                valid.Operation,
                [corruptedScope]);
            ImmutableArray<ResearchTargetValidationEvidence> evidence =
            [
                new(
                    request,
                    input,
                    ResearchTargetInputRole.Implementation,
                    evidenceOverride ?? inputEvidence,
                    omitMetadataEvidence ? null : resolved.Target.ApiType,
                    omitMetadataEvidence ? null : metadata,
                    TargetResolutionFailed: false,
                    corrupted),
            ];

            Assert.Throws<InvalidOperationException>(
                () => ResearchTargetResolutionValidator.Validate(
                    planning,
                    corruptedResolution,
                    evidence));
        }
    }

    [Fact]
    public void ResearchTargetAttempts_MapEveryMetadataDiagnosticKind()
    {
        // The expected set is derived from the Metadata declaration, so a new
        // or removed diagnostic kind fails this gate.
        Dictionary<MemberTargetDiagnosticKind, ResearchTargetOutcomeKind> expected =
            new()
            {
                [MemberTargetDiagnosticKind.MissingMember] =
                    ResearchTargetOutcomeKind.NotFound,
                [MemberTargetDiagnosticKind.DigestNotFound] =
                    ResearchTargetOutcomeKind.NotFound,
                [MemberTargetDiagnosticKind.AmbiguousMember] =
                    ResearchTargetOutcomeKind.Ambiguous,
                [MemberTargetDiagnosticKind.DigestAmbiguous] =
                    ResearchTargetOutcomeKind.Ambiguous,
                [MemberTargetDiagnosticKind.ConflictingSelectors] =
                    ResearchTargetOutcomeKind.Rejected,
                [MemberTargetDiagnosticKind.OverloadOutOfRange] =
                    ResearchTargetOutcomeKind.Rejected,
            };
        Assert.Equal(
            Enum.GetValues<MemberTargetDiagnosticKind>().ToHashSet(),
            expected.Keys.ToHashSet());

        foreach ((MemberTargetDiagnosticKind kind,
            ResearchTargetOutcomeKind arm) in expected)
        {
            Assert.Equal(
                arm,
                ResearchTargetResolver.MapDiagnosticKind(kind));
        }

        // The mapping is closed: an undeclared value is not silently degraded.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ResearchTargetResolver.MapDiagnosticKind((MemberTargetDiagnosticKind)999));

        // Every declared kind is reachable end to end against a real image, and
        // reaches exactly the mapped arm with the exact Metadata diagnostic.
        TargetFixture fixture = TargetFixture.Create([(Sample(), null, null)]);
        string methodDigest = fixture.Fingerprint(SampleType, "Method");
        string ambiguousDigest = fixture.AmbiguousDigestPrefix(SampleType, "Many");

        Dictionary<MemberTargetDiagnosticKind, MemberTargetSelector> selectors =
            new()
            {
                [MemberTargetDiagnosticKind.MissingMember] =
                    new MemberTargetSelector("Absent", "Absent"),
                [MemberTargetDiagnosticKind.DigestNotFound] =
                    new MemberTargetSelector(
                        "Method~deadbeefdeadbeef",
                        "Method",
                        DigestPrefix: "deadbeefdeadbeef"),
                [MemberTargetDiagnosticKind.AmbiguousMember] =
                    new MemberTargetSelector("Overloaded", "Overloaded"),
                [MemberTargetDiagnosticKind.DigestAmbiguous] =
                    new MemberTargetSelector(
                        $"Many~{ambiguousDigest}",
                        "Many",
                        DigestPrefix: ambiguousDigest),
                [MemberTargetDiagnosticKind.ConflictingSelectors] =
                    new MemberTargetSelector(
                        $"Method~{methodDigest}:1",
                        "Method",
                        OverloadIndex: 1,
                        DigestPrefix: methodDigest),
                [MemberTargetDiagnosticKind.OverloadOutOfRange] =
                    new MemberTargetSelector(
                        "Overloaded:9",
                        "Overloaded",
                        OverloadIndex: 9),
            };
        Assert.Equal(
            Enum.GetValues<MemberTargetDiagnosticKind>().ToHashSet(),
            selectors.Keys.ToHashSet());

        foreach ((MemberTargetDiagnosticKind kind, MemberTargetSelector selector)
            in selectors)
        {
            ResearchTargetAttempt attempt = Assert.Single(
                fixture.Resolve(
                    fixture.Carried(0, SampleType, selector)).Attempts);
            MemberTargetDiagnostic diagnostic = MetadataDiagnostic(attempt.Outcome);
            Assert.Equal(kind, diagnostic.Kind);
            Assert.Equal(expected[kind], attempt.Outcome.Kind);
        }
    }

    [Fact]
    public void ResearchTargetResolution_PreservesMetadataDiagnosticsAndAccessorRoles()
    {
        TargetFixture fixture = TargetFixture.Create([(Sample(), null, null)]);

        // Accessor roles come only from the selected member's MethodDef tokens.
        (string Selector, ResearchTargetRelationshipRole Role)[] roles =
        [
            ("Method", ResearchTargetRelationshipRole.Method),
            ("Value:1", ResearchTargetRelationshipRole.Getter),
            ("Value:2", ResearchTargetRelationshipRole.Setter),
            ("ReadOnlyValue", ResearchTargetRelationshipRole.Getter),
            ("Changed:1", ResearchTargetRelationshipRole.Adder),
            ("Changed:2", ResearchTargetRelationshipRole.Remover),
            ("Field", ResearchTargetRelationshipRole.None),
        ];

        // Every declared role except None comes with a durable address; the
        // declared role set is derived from the declaration.
        Assert.Equal(
            Enum.GetValues<ResearchTargetRelationshipRole>().ToHashSet(),
            roles.Select(entry => entry.Role).ToHashSet());

        ApiSurface surface = fixture.Surface(FixtureCatalog.ResearchTargetSample);
        ApiType sample = surface.Types.Single(
            type => type.DefinitionName?.ToMetadataFullName() == SampleType);

        foreach ((string selector, ResearchTargetRelationshipRole role) in roles)
        {
            ResearchTargetAttempt attempt = Assert.Single(
                fixture.Resolve(fixture.Carried(0, SampleType, selector)).Attempts);
            ResearchTargetOutcome.Resolved resolved = Resolved(attempt);
            Assert.Equal(role, resolved.Role);

            // Exact Metadata evidence is retained, not reconstructed.
            Assert.Same(resolved.Target.Anchor, resolved.Anchor);
            Assert.Contains(
                resolved.Candidates,
                candidate => ReferenceEquals(candidate.Anchor, resolved.Anchor));
            Assert.Same(fixture.ModuleIdentity(0), resolved.Module);

            if (role == ResearchTargetRelationshipRole.None)
            {
                Assert.Null(resolved.Address);
                continue;
            }

            MetadataMethodAddress address = Assert.NotNull(resolved.Address);
            Assert.Equal(
                fixture.ModuleIdentity(0).ModuleVersionId,
                address.ModuleVersionId);
            Assert.Equal(
                ExpectedToken(sample, resolved.Target, role),
                address.Token);
        }

        // A missing member keeps the exact Metadata diagnostic and candidates.
        ResearchTargetAttempt missing = Assert.Single(
            fixture.Resolve(fixture.Carried(0, SampleType, "Absent")).Attempts);
        var notFound = Assert.IsType<ResearchTargetOutcome.NotFound>(missing.Outcome);
        Assert.NotNull(notFound.MetadataDiagnostic);
        Assert.Null(notFound.ResearchDiagnostic);
        Assert.Equal(
            MemberTargetDiagnosticKind.MissingMember,
            notFound.MetadataDiagnostic.Kind);

        // An ambiguous selector keeps the exact candidate references.
        ResearchTargetAttempt ambiguous = Assert.Single(
            fixture.Resolve(fixture.Carried(0, SampleType, "Overloaded")).Attempts);
        var ambiguousOutcome =
            Assert.IsType<ResearchTargetOutcome.Ambiguous>(ambiguous.Outcome);
        Assert.Equal(2, ambiguousOutcome.Candidates.Length);
        Assert.Equal(
            ambiguousOutcome.Diagnostic.Candidates.Count,
            ambiguousOutcome.Candidates.Length);
        for (int index = 0;
            index < ambiguousOutcome.Diagnostic.Candidates.Count;
            index++)
        {
            Assert.Same(
                ambiguousOutcome.Diagnostic.Candidates[index],
                ambiguousOutcome.Candidates[index]);
        }
    }

    [Theory]
    [InlineData(false, "Value:2")]
    [InlineData(true, "Changed:2")]
    public void ResearchTargetResolution_RejectsNonUniqueAccessorRoles(
        bool eventMember,
        string selector)
    {
        byte[] image = BuildSharedAccessorImage(eventMember);
        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);

        ResearchTargetAttempt attempt = Assert.Single(
            fixture.ResolveDefault("N.C", selector).Attempts);
        var failed =
            Assert.IsType<ResearchTargetOutcome.Failed>(attempt.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.RelationshipRoleEvidenceMismatch,
            failed.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetRejectedSelector_PreservesDiagnosticAndBlocksAbsence()
    {
        TargetFixture fixture = TargetFixture.Create([(Sample(), Sample(), null)]);

        // An out-of-range positional selector is Rejected, not NotFound: it is
        // not absence-safe, because ordinal movement can leave the same stable
        // target at another position.
        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Overloaded:9"));

        Assert.Equal(2, resolution.Attempts.Length);
        foreach (ResearchTargetAttempt attempt in resolution.Attempts)
        {
            var rejected =
                Assert.IsType<ResearchTargetOutcome.Rejected>(attempt.Outcome);
            Assert.Equal(
                MemberTargetDiagnosticKind.OverloadOutOfRange,
                rejected.Diagnostic.Kind);
            Assert.NotEmpty(rejected.Candidates);

            // Typed rejected evidence is not absence evidence: no arm of a
            // Rejected outcome can be read as NotFound or Resolved.
            Assert.Equal(ResearchTargetOutcomeKind.Rejected, attempt.Outcome.Kind);
            Assert.IsNotType<ResearchTargetOutcome.NotFound>(attempt.Outcome);
            Assert.IsNotType<ResearchTargetOutcome.Resolved>(attempt.Outcome);
        }

        // Its domain is blocked on both sides, so no side is left looking empty.
        ResearchTargetDomain domain = Assert.Single(resolution.Scopes[0].Domains);
        Assert.False(domain.IsAmbiguous);
        Assert.Equal(2, domain.Inputs.Length);
        Assert.All(
            domain.Inputs,
            disposition => Assert.Equal(
                ResearchTargetDispositionKind.Requested,
                disposition.Kind));
    }

    // ---------------------------------------------------------------- domains

    [Fact]
    public void ResearchTargetDomains_EraseOnlyAssemblyVersion()
    {
        // Two versions of one signed, cultured assembly share a domain; a
        // same-named assembly with another public key token, another culture,
        // or another simple name does not.
        AssemblyReferenceIdentity before = new(
            "Domain.Fixture",
            new Version(1, 0, 0, 0),
            "en-US",
            "0123456789abcdef");
        AssemblyReferenceIdentity after = before with { Version = new Version(9, 9) };
        AssemblyReferenceIdentity otherToken =
            before with { PublicKeyToken = "fedcba9876543210" };
        AssemblyReferenceIdentity otherCulture = before with { Culture = "fr-FR" };
        AssemblyReferenceIdentity otherName = before with { Name = "Domain.Other" };

        TargetFixture fixture = TargetFixture.Reference(
            [before, otherToken, otherCulture, otherName],
            [after]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(SampleType, "Method");

        ResearchTargetScope scope = Assert.Single(resolution.Scopes);
        Assert.Equal(4, scope.Domains.Length);

        ResearchTargetDomain shared = scope.Domains[0];
        Assert.Null(shared.Key.Identity.Version);
        Assert.Equal(before.Name, shared.Key.Identity.Name);
        Assert.Equal(before.Culture, shared.Key.Identity.Culture);
        Assert.Equal(before.PublicKeyToken, shared.Key.Identity.PublicKeyToken);
        Assert.Single(shared.Side(ResearchComparisonSide.Before));
        Assert.Single(shared.Side(ResearchComparisonSide.After));

        // The three near misses are their own single-sided domains.
        Assert.All(
            scope.Domains.Skip(1),
            domain =>
            {
                Assert.Null(domain.Key.Identity.Version);
                Assert.Single(domain.Side(ResearchComparisonSide.Before));
                Assert.Empty(domain.Side(ResearchComparisonSide.After));
            });

        // Domain keys are Metadata-equivalent, never display-derived.
        Assert.Equal(
            scope.Domains.Length,
            scope.Domains.Select(domain => domain.Key).Distinct().Count());
        Assert.NotEqual(shared.Key, scope.Domains[1].Key);
    }

    [Fact]
    public void ResearchTargetDomains_RejectDuplicateSameSideCandidates()
    {
        // Two Before-side occurrences of the same assembly cannot be
        // distinguished by domain, so every request in that domain blocks.
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), Sample(), Sample())]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(SampleType, "Method");

        ResearchTargetDomain domain = Assert.Single(resolution.Scopes[0].Domains);
        Assert.True(domain.IsAmbiguous);

        // The complete conflicting set is retained, and it is exactly the two
        // Before-side inputs.
        Assert.Equal(2, domain.ConflictingInputs.Length);
        Assert.Equal(
            fixture.Population.Questions[0]
                .Side(ResearchComparisonSide.Before)
                .Select(input => input.Id)
                .ToHashSet(),
            domain.ConflictingInputs.ToHashSet());

        // Every affected request blocks with DomainAmbiguous rather than
        // pairing arbitrarily, including the unambiguous After side.
        Assert.Equal(3, resolution.Attempts.Length);
        Assert.All(
            resolution.Attempts,
            attempt =>
            {
                var unavailable = Assert.IsType<ResearchTargetOutcome.Unavailable>(
                    attempt.Outcome);
                Assert.Equal(
                    ResearchTargetDiagnosticKind.DomainAmbiguous,
                    unavailable.Diagnostic.Kind);
            });
    }

    [Fact]
    public void ResearchTargetDomains_BlockOnlyTheirOwnCensus()
    {
        // One ambiguous sample domain beside one healthy diff domain.
        TargetFixture fixture = TargetFixture.Create(
            [
                (Sample(), Sample(), Sample()),
                (Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null),
            ]);

        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Method"),
            fixture.Carried(1, DiffType, "Stable"));

        ResearchTargetDomain blocked =
            Assert.Single(resolution.Scopes[0].Domains);
        Assert.True(blocked.IsAmbiguous);
        Assert.All(
            blocked.Attempts,
            attempt => Assert.Equal(
                ResearchTargetOutcomeKind.Unavailable,
                attempt.Outcome.Kind));

        ResearchTargetDomain healthy =
            Assert.Single(resolution.Scopes[1].Domains);
        Assert.False(healthy.IsAmbiguous);
        Assert.Empty(healthy.ConflictingInputs);
        Assert.Equal(2, healthy.Attempts.Length);
        Assert.All(
            healthy.Attempts,
            attempt => Assert.Equal(
                ResearchTargetRelationshipRole.Method,
                Resolved(attempt).Role));

        // The healthy domain's own key is the diff assembly, and the blocked
        // domain's conflicting set never leaks into it.
        Assert.Equal("DiffFixtureSample", healthy.Key.Identity.Name);
        Assert.All(
            healthy.Inputs,
            disposition => Assert.DoesNotContain(
                disposition.Input,
                blocked.ConflictingInputs));
    }

    // ------------------------------------------- census and correspondence

    [Fact]
    public void ResearchTargetKeys_AreOwnerIssuedAndNotDisplayDerived()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null)]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(DiffType, "Stable");
        var paired = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(resolution.Correspondences));

        Assert.Empty(
            typeof(ResearchStrictTargetKey).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(ResearchTargetCorrespondenceKey).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));
        Assert.Same(paired.Scope, paired.Before.StrictKey.Scope);
        Assert.Same(paired.DomainId, paired.Before.StrictKey.Domain);
        Assert.Same(
            paired.Before.Attempt.Request.Input,
            paired.Before.StrictKey.Input);
        Assert.DoesNotContain(
            paired.Before.Attempt.Request.Input.ToString()!,
            paired.Before.CorrespondenceKey.CanonicalIdentity,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResearchTargetKeys_EraseOnlyAddressAndSideLocalIdentity()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null)]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(DiffType, "GenericIdentity");
        var paired = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(resolution.Correspondences));

        Assert.NotSame(
            paired.Before.StrictKey.Input,
            paired.After.StrictKey.Input);
        Assert.NotEqual(
            paired.Before.StrictKey.Address,
            paired.After.StrictKey.Address);
        Assert.NotEqual(paired.Before.StrictKey, paired.After.StrictKey);
        Assert.Equal(
            paired.Before.CorrespondenceKey,
            paired.After.CorrespondenceKey);
        Assert.Null(
            typeof(ResearchTargetCorrespondenceKey).GetProperty("Input"));
        Assert.Null(
            typeof(ResearchTargetCorrespondenceKey).GetProperty("Side"));
        Assert.Null(
            typeof(ResearchTargetCorrespondenceKey).GetProperty("Address"));
        Assert.Null(
            typeof(ResearchTargetCorrespondenceKey).GetProperty("Module"));
    }

    [Fact]
    public void ResearchTargetKeys_PreserveDomainSignatureExtensionAndRelationshipRole()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null)]);
        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, DiffType, "Stable"),
            fixture.Carried(0, DiffType, "GenericIdentity"),
            fixture.Carried(
                0,
                "DiffFixtureSample.ExtensionSample",
                "Twice"));

        ResearchTargetCorrespondenceKey stable =
            Assert.IsType<ResearchTargetCorrespondenceOutcome.Paired>(
                resolution.Correspondences.Single(
                    outcome => ReferenceEquals(
                        outcome.Scope,
                        resolution.Scopes[0].Id)))
                .Before.CorrespondenceKey;
        var generic = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                resolution.Correspondences.Single(
                    outcome => ReferenceEquals(
                        outcome.Scope,
                        resolution.Scopes[1].Id)));
        var extension = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                resolution.Correspondences.Single(
                    outcome => ReferenceEquals(
                        outcome.Scope,
                        resolution.Scopes[2].Id)));

        Assert.NotEqual(
            stable.CanonicalIdentity,
            generic.Before.CorrespondenceKey.CanonicalIdentity);
        Assert.Equal(
            ResearchTargetRelationshipRole.Method,
            stable.Role);
        Assert.Equal(
            ResearchTargetRelationshipRole.Method,
            extension.Before.CorrespondenceKey.Role);
        Assert.Contains(
            "DiffFixtureSample.ExtensionSample.Twice",
            extension.Before.CorrespondenceKey.CanonicalIdentity,
            StringComparison.Ordinal);
        Assert.True(
            extension.Before.Target.Target.ApiMember.Member.IsExtension);

        TargetFixture nestedFixture =
            TargetFixture.Create([(Sample(), Sample(), null)]);
        var nested = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(
                    nestedFixture.ResolveDefault(NestedType, "Method")
                        .Correspondences));
        Assert.Contains(
            "TargetOuter.TargetInner",
            nested.Before.CorrespondenceKey.CanonicalIdentity,
            StringComparison.Ordinal);

        TargetFixture accessorFixture =
            TargetFixture.Create([(Sample(), Sample(), null)]);
        var getter = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(
                    accessorFixture.ResolveDefault(SampleType, "Value:1")
                        .Correspondences));
        var setter = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(
                    accessorFixture.ResolveDefault(SampleType, "Value:2")
                        .Correspondences));
        Assert.Equal(
            ResearchTargetRelationshipRole.Getter,
            getter.Before.CorrespondenceKey.Role);
        Assert.Equal(
            ResearchTargetRelationshipRole.Setter,
            setter.Before.CorrespondenceKey.Role);
        Assert.Equal(
            getter.Before.CorrespondenceKey.CanonicalIdentity,
            setter.Before.CorrespondenceKey.CanonicalIdentity);
        Assert.NotEqual(
            getter.Before.CorrespondenceKey,
            setter.Before.CorrespondenceKey);

        var field = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(
                    accessorFixture.ResolveDefault(SampleType, "Field")
                        .Correspondences));
        Assert.Equal(
            ResearchTargetRelationshipRole.None,
            field.Before.CorrespondenceKey.Role);
        Assert.Null(field.Before.StrictKey.Address);
        Assert.Same(
            field.Before.Target.Anchor,
            field.Before.StrictKey.Anchor);
        Assert.Equal(
            field.Before.Target.Anchor.CanonicalSignature,
            field.Before.CorrespondenceKey.CanonicalIdentity);

        var firstConversion = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(
                    fixture.ResolveDefault(
                            "DiffFixtureSample.ConversionSample",
                            "op_Implicit:1")
                        .Correspondences));
        var secondConversion = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Paired>(
                Assert.Single(
                    fixture.ResolveDefault(
                            "DiffFixtureSample.ConversionSample",
                            "op_Implicit:2")
                        .Correspondences));
        Assert.NotEqual(
            firstConversion.Before.CorrespondenceKey.CanonicalIdentity,
            secondConversion.Before.CorrespondenceKey.CanonicalIdentity);
    }

    [Fact]
    public void ResearchTargetKeys_UseTupleErasedCanonicalTypes()
    {
        TargetFixture sampleFixture =
            TargetFixture.Create([(Sample(), null, null)]);
        ResolvedMemberTarget method = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.BeforeOnly>(
                Assert.Single(
                    sampleFixture.ResolveDefault(
                            SampleType,
                            "Overloaded:1")
                        .Correspondences))
            .Before.Target.Target;
        ApiParameter parameter =
            Assert.Single(method.ApiMember.Member.SignatureModel!.Parameters);
        parameter.Type = "(int left, int right)";
        parameter.CanonicalType = "System.ValueTuple<int, int>";
        string first =
            ResearchMemberIdentity.CanonicalBodyIdentity(method);
        parameter.Type = "(int x, int y)";
        string second =
            ResearchMemberIdentity.CanonicalBodyIdentity(method);

        Assert.Equal(first, second);
        Assert.Contains(
            "System.ValueTuple",
            first,
            StringComparison.Ordinal);
        Assert.DoesNotContain("left", first, StringComparison.Ordinal);

        TargetFixture conversionFixture = TargetFixture.Create(
            [(Diff(FixtureCatalog.DiffV1), null, null)]);
        ResolvedMemberTarget conversion = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.BeforeOnly>(
                Assert.Single(
                    conversionFixture.ResolveDefault(
                            "DiffFixtureSample.ConversionSample",
                            "op_Implicit:1")
                        .Correspondences))
            .Before.Target.Target;
        ApiSignature? signature =
            conversion.ApiMember.Member.SignatureModel;
        Assert.NotNull(signature);
        signature.ReturnType = "(int left, int right)";
        signature.CanonicalReturnType = "System.ValueTuple<int, int>";
        first = ResearchMemberIdentity.CanonicalBodyIdentity(conversion);
        signature.ReturnType = "(int x, int y)";
        second = ResearchMemberIdentity.CanonicalBodyIdentity(conversion);

        Assert.Equal(first, second);
        Assert.Contains(
            "~System.ValueTuple",
            first,
            StringComparison.Ordinal);
        Assert.DoesNotContain("left", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ResearchTargetCensus_DerivesCompleteAttemptAndCorrespondenceDomains()
    {
        TargetFixture fixture = TargetFixture.Create(
            [
                (Sample(), Sample(), Diff(FixtureCatalog.DiffV1)),
                (Diff(FixtureCatalog.DiffV1), null, null),
            ]);
        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Method"),
            fixture.Carried(1, DiffType, "Stable"));

        Assert.Equal(resolution.Domains.Length * 2, resolution.Censuses.Length);
        foreach (ResearchTargetDomain domain in resolution.Domains)
        {
            ResearchTargetDomainSideCensus[] censuses =
                resolution.Censuses.Where(
                    census => ReferenceEquals(census.Domain, domain)).ToArray();
            Assert.Equal(2, censuses.Length);
            Assert.Equal(
                Enum.GetValues<ResearchComparisonSide>().ToHashSet(),
                censuses.Select(census => census.Side).ToHashSet());
            foreach (ResearchTargetDomainSideCensus census in censuses)
            {
                Assert.Equal(
                    domain.Side(census.Side),
                    census.Inputs);
                Assert.Equal(
                    domain.Attempts.Where(
                        attempt => attempt.Request.Side == census.Side),
                    census.Attempts);
            }

            Assert.Contains(
                resolution.Correspondences,
                outcome => ReferenceEquals(outcome.Domain, domain));
        }
    }

    [Fact]
    public void ResearchTargetCensus_BlockedDomainTaintsResolvedTargetsOnBothSides()
    {
        TargetFixture fixture = TargetFixture.Create(
            [
                (Sample(), SampleWithForeignModule(), null),
                (SampleWithForeignModule(), Sample(), null),
            ]);
        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Method"),
            fixture.Carried(1, SampleType, "Method"));

        Assert.Equal(2, resolution.Correspondences.Length);
        Assert.Equal(
            Enum.GetValues<ResearchComparisonSide>().ToHashSet(),
            resolution.Correspondences
                .Cast<ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>()
                .Select(outcome => outcome.Attempt.Request.Side)
                .ToHashSet());
        Assert.All(
            resolution.Correspondences,
            outcome =>
            {
                var unavailable = Assert.IsType<
                    ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(
                        outcome);
                Assert.Equal(
                    ResearchTargetTaintKind.BlockedDomain,
                    unavailable.Taint.Kind);
                Assert.Single(unavailable.Taint.Attempts);
                Assert.Null(unavailable.StrictKey);
                Assert.Null(unavailable.CorrespondenceKey);
            });
    }

    [Fact]
    public void ResearchTargetCensus_BlockedDomainWithoutResolvedTargetsIsVisible()
    {
        TargetFixture fixture =
            TargetFixture.Create([(Unreadable(), Unreadable(), null)]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(SampleType, "Method");

        var unavailable = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.DomainUnavailable>(
                Assert.Single(resolution.Correspondences));
        Assert.Equal(
            ResearchTargetTaintKind.BlockedDomain,
            unavailable.Taint.Kind);
        Assert.Equal(2, unavailable.Taint.Attempts.Length);
        Assert.All(
            unavailable.Taint.Attempts,
            attempt => Assert.Equal(
                ResearchTargetOutcomeKind.Failed,
                attempt.Outcome.Kind));
    }

    [Fact]
    public void ResearchTargetCensus_BlockedDomainPrecedesKeyConstruction()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), SampleWithForeignModule(), null)]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(SampleType, "Method");

        var unavailable = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(
                Assert.Single(resolution.Correspondences));
        Assert.IsType<ResearchTargetOutcome.Resolved>(
            unavailable.Attempt.Outcome);
        Assert.Null(unavailable.StrictKey);
        Assert.Null(unavailable.CorrespondenceKey);
        Assert.Empty(unavailable.Taint.StrictKeys);
        Assert.DoesNotContain(
            resolution.Correspondences,
            outcome => outcome.Kind
                is ResearchTargetCorrespondenceKind.Paired
                    or ResearchTargetCorrespondenceKind.BeforeOnly
                    or ResearchTargetCorrespondenceKind.AfterOnly
                    or ResearchTargetCorrespondenceKind.Absent);
    }

    [Fact]
    public void ResearchTargetCensus_DivergentResolvedKeysAreUnavailable()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null)]);
        ResearchTargetResolution resolution = fixture.ResolveDefault(
            "DiffFixtureSample.ConstructorRemovalSample",
            ".ctor:1");

        Assert.Equal(2, resolution.Correspondences.Length);
        Assert.All(
            resolution.Correspondences,
            outcome =>
            {
                var unavailable = Assert.IsType<
                    ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(
                        outcome);
                Assert.Equal(
                    ResearchTargetTaintKind.SelectionDrift,
                    unavailable.Taint.Kind);
                Assert.NotNull(unavailable.StrictKey);
                Assert.NotNull(unavailable.CorrespondenceKey);
                Assert.Equal(2, unavailable.Taint.Attempts.Length);
                Assert.Equal(2, unavailable.Taint.StrictKeys.Length);
            });
        Assert.Equal(
            Enum.GetValues<ResearchComparisonSide>().ToHashSet(),
            resolution.Correspondences
                .Cast<ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>()
                .Select(outcome => outcome.Attempt.Request.Side)
                .ToHashSet());
    }

    [Fact]
    public void ResearchTargetKeyAbsence_RequiresCompleteHealthyKeyLocalCensus()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), SampleWithForeignModule(), null)]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(SampleType, "Method");

        Assert.Contains(
            resolution.Censuses,
            census => census.Health == ResearchTargetCensusHealth.Blocked);
        Assert.DoesNotContain(
            resolution.Correspondences,
            outcome => outcome
                is ResearchTargetCorrespondenceOutcome.BeforeOnly
                    or ResearchTargetCorrespondenceOutcome.AfterOnly);
    }

    [Fact]
    public void ResearchTargetKeyAbsence_RequiresPositiveSelectorCoverage()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null)]);
        ResearchTargetResolution ordinal = fixture.ResolveDefault(
            "DiffFixtureSample.ConstructorRemovalSample",
            ".ctor:1");
        MemberAnchor anchor = Assert.Single(
            ordinal.Correspondences
                .OfType<
                    ResearchTargetCorrespondenceOutcome
                        .CounterpartUnavailable>(),
            outcome => outcome.Attempt.Request.Side
                == ResearchComparisonSide.Before)
            .Target.Anchor;
        string fingerprint = anchor.Fingerprint;
        ResearchTargetResolution positive = fixture.Resolve(
            fixture.Carried(
                0,
                "DiffFixtureSample.ConstructorRemovalSample",
                new MemberTargetSelector(
                    anchor.StableSelector,
                    ".ctor",
                    DigestPrefix: fingerprint)));
        var beforeOnly = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.BeforeOnly>(
                Assert.Single(positive.Correspondences));
        Assert.Equal(
            MemberTargetDiagnosticKind.DigestNotFound,
            Assert.IsType<ResearchTargetOutcome.NotFound>(
                beforeOnly.AfterAbsence.NotFoundAttempt!.Outcome)
                .MetadataDiagnostic!.Kind);
        Assert.StartsWith(
            fingerprint,
            beforeOnly.Before.Target.Anchor.Fingerprint,
            StringComparison.OrdinalIgnoreCase);

        ResearchTargetResolution nonAbsence = fixture.ResolveDefault(
            "DiffFixtureSample.MethodRemovalSample",
            "Removed:3");
        Assert.IsType<ResearchTargetCorrespondenceOutcome.DomainUnavailable>(
            Assert.Single(nonAbsence.Correspondences));
        Assert.DoesNotContain(
            nonAbsence.Correspondences,
            outcome => outcome
                is ResearchTargetCorrespondenceOutcome.BeforeOnly
                    or ResearchTargetCorrespondenceOutcome.AfterOnly
                    or ResearchTargetCorrespondenceOutcome.Absent);
    }

    [Fact]
    public void ResearchTargetDomainAbsence_RequiresCompleteHealthyEmptySide()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Diff(FixtureCatalog.DiffV1), null, null)]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(DiffType, "NotPresent");
        var absent = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.Absent>(
                Assert.Single(resolution.Correspondences));

        Assert.Equal(
            ResearchTargetAbsenceEvidenceKind.NotFound,
            absent.BeforeAbsence.EvidenceKind);
        Assert.IsType<ResearchTargetOutcome.NotFound>(
            absent.BeforeAbsence.NotFoundAttempt!.Outcome);
        Assert.Equal(
            ResearchTargetAbsenceEvidenceKind.NoAdmittedInput,
            absent.AfterAbsence.EvidenceKind);
        Assert.Null(absent.AfterAbsence.NotFoundAttempt);
        Assert.All(
            resolution.Censuses,
            census => Assert.Equal(
                ResearchTargetCensusHealth.Healthy,
                census.Health));
    }

    [Fact]
    public void ResearchTargetFailure_NeverBecomesAbsenceEvidence()
    {
        TargetFixture fixture =
            TargetFixture.Create([(Unreadable(), null, null)]);
        ResearchTargetResolution resolution =
            fixture.ResolveDefault(SampleType, "Method");

        var unavailable = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.DomainUnavailable>(
                Assert.Single(resolution.Correspondences));
        Assert.Single(unavailable.Taint.Attempts);
        Assert.IsType<ResearchTargetOutcome.Failed>(
            unavailable.Taint.Attempts[0].Outcome);
        Assert.DoesNotContain(
            resolution.Correspondences,
            outcome => outcome.Kind
                is ResearchTargetCorrespondenceKind.BeforeOnly
                    or ResearchTargetCorrespondenceKind.AfterOnly
                    or ResearchTargetCorrespondenceKind.Absent);
    }

    [Fact]
    public void ResearchProducerHandoff_CompleteOutcomesRetainExactEndpointOrAbsenceEvidence()
    {
        TargetFixture fixture = TargetFixture.Create(
            [
                (Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null),
                (Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null),
                (Diff(FixtureCatalog.DiffV2), Diff(FixtureCatalog.DiffV1), null),
                (Diff(FixtureCatalog.DiffV1), Diff(FixtureCatalog.DiffV2), null),
            ]);
        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, DiffType, "Stable"),
            fixture.Carried(
                1,
                "DiffFixtureSample.MethodRemovalSample",
                "Removed:1"),
            fixture.Carried(
                2,
                "DiffFixtureSample.MethodRemovalSample",
                "Removed:1"),
            fixture.Carried(3, DiffType, "NotPresent"));

        Assert.Equal(
            Enum.GetValues<ResearchTargetCorrespondenceKind>()
                .Except(
                [
                    ResearchTargetCorrespondenceKind.CounterpartUnavailable,
                    ResearchTargetCorrespondenceKind.DomainUnavailable,
                ])
                .ToHashSet(),
            resolution.Correspondences.Select(outcome => outcome.Kind)
                .ToHashSet());

        var paired = Assert.Single(
            resolution.Correspondences
                .OfType<ResearchTargetCorrespondenceOutcome.Paired>());
        Assert.Same(
            paired.Before.Attempt,
            paired.Domain.Attempts.Single(
                attempt => attempt.Request.Side
                    == ResearchComparisonSide.Before));
        Assert.Same(
            paired.After.Attempt,
            paired.Domain.Attempts.Single(
                attempt => attempt.Request.Side
                    == ResearchComparisonSide.After));

        var beforeOnly = Assert.Single(
            resolution.Correspondences
                .OfType<ResearchTargetCorrespondenceOutcome.BeforeOnly>());
        Assert.Same(
            beforeOnly.AfterAbsence.NotFoundAttempt,
            beforeOnly.AfterAbsence.Census.Attempts.Single());

        var afterOnly = Assert.Single(
            resolution.Correspondences
                .OfType<ResearchTargetCorrespondenceOutcome.AfterOnly>());
        Assert.Same(
            afterOnly.BeforeAbsence.NotFoundAttempt,
            afterOnly.BeforeAbsence.Census.Attempts.Single());

        var absent = Assert.Single(
            resolution.Correspondences
                .OfType<ResearchTargetCorrespondenceOutcome.Absent>());
        Assert.Same(
            absent.BeforeAbsence.NotFoundAttempt,
            absent.BeforeAbsence.Census.Attempts.Single());
        Assert.Same(
            absent.AfterAbsence.NotFoundAttempt,
            absent.AfterAbsence.Census.Attempts.Single());
    }

    [Fact]
    public void ResearchProducerHandoff_BlockedOutcomesExposeNoCompletedEndpointSet()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), SampleWithForeignModule(), null)]);
        var unavailable = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(
                Assert.Single(
                    fixture.ResolveDefault(SampleType, "Method")
                        .Correspondences));

        Assert.Null(unavailable.StrictKey);
        Assert.Null(unavailable.CorrespondenceKey);
        Assert.Null(
            unavailable.GetType().GetProperty(
                nameof(ResearchCorrespondingTarget)));
        Assert.DoesNotContain(
            unavailable.GetType().GetProperties(),
            property => property.PropertyType
                == typeof(ResearchCorrespondingTarget));
    }

    [Fact]
    public void ResearchProducerHandoff_DoesNotClassifyInspectionTopology()
    {
        string[] forbidden =
        [
            "Bodyful",
            "Bodyless",
            "NotMethodLike",
            "BodyAdded",
            "BodyRemoved",
            "NoBody",
            "TargetAbsent",
            "ProducerEligible",
            "Complete",
            "NoApplicableInput",
        ];
        Type[] types =
        [
            typeof(ResearchTargetResolution),
            typeof(ResearchTargetDomainSideCensus),
            typeof(ResearchTargetCorrespondenceOutcome),
            .. typeof(ResearchTargetCorrespondenceOutcome)
                .GetNestedTypes(BindingFlags.Public),
        ];

        foreach (Type type in types)
        {
            Assert.DoesNotContain(
                forbidden,
                term => type.Name.Contains(term, StringComparison.Ordinal));
            Assert.All(
                type.GetProperties(),
                property => Assert.DoesNotContain(
                    forbidden,
                    term => property.Name.Contains(
                        term,
                        StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void ResearchImplementationTargetPath_HasNoStringKeyedIdentityBag()
    {
        Type[] types =
        [
            typeof(ResearchTargetResolution),
            typeof(ResearchTargetDomainSideCensus),
            typeof(ResearchStrictTargetKey),
            typeof(ResearchTargetCorrespondenceKey),
            typeof(ResearchCorrespondingTarget),
            typeof(ResearchTargetKeyAbsenceProof),
            typeof(ResearchTargetDomainAbsenceProof),
            typeof(ResearchTargetTaintEvidence),
            typeof(ResearchTargetCorrespondenceOutcome),
            .. typeof(ResearchTargetCorrespondenceOutcome)
                .GetNestedTypes(BindingFlags.Public),
        ];

        foreach (Type type in types)
        {
            IEnumerable<Type> signatureTypes =
                type.GetFields(
                        BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.Instance
                            | BindingFlags.Static)
                    .Select(field => field.FieldType)
                    .Concat(type.GetProperties().Select(
                        property => property.PropertyType))
                    .Concat(type.GetConstructors(
                            BindingFlags.Public
                                | BindingFlags.NonPublic
                                | BindingFlags.Instance)
                        .SelectMany(constructor => constructor.GetParameters())
                        .Select(parameter => parameter.ParameterType));
            Assert.DoesNotContain(
                signatureTypes,
                IsStringKeyedDictionary);
        }

        static bool IsStringKeyedDictionary(Type type)
        {
            if (type.IsGenericType
                && type.GetGenericArguments()[0] == typeof(string)
                && type.GetGenericTypeDefinition()
                    is var definition
                && (definition == typeof(Dictionary<,>)
                    || definition == typeof(IDictionary<,>)
                    || definition == typeof(IReadOnlyDictionary<,>)
                    || definition == typeof(ImmutableDictionary<,>)))
            {
                return true;
            }

            return type.GetInterfaces().Any(
                candidate => candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition()
                        == typeof(IReadOnlyDictionary<,>)
                    && candidate.GetGenericArguments()[0] == typeof(string));
        }
    }

    // ----------------------------------------------------------------- scopes

    [Fact]
    public void ResearchTargetScopes_DeriveBijectivelyFromSelectionOccurrences()
    {
        // The second question admits no input at all, so its scope has no
        // domain; the bijection must still hold.
        TargetFixture fixture = TargetFixture.Create(
            [
                (Sample(), Sample(), null),
                (null, null, null),
            ]);

        // Two occurrences with identical intent, plus one empty-question
        // occurrence.
        ResearchCarriedMemberSelection first = fixture.Carried(0, SampleType, "Method");
        ResearchCarriedMemberSelection repeat = fixture.Carried(0, SampleType, "Method");
        ResearchCarriedMemberSelection empty = fixture.Carried(1, SampleType, "Method");

        ResearchTargetResolution resolution = fixture.Resolve(first, repeat, empty);

        Assert.Equal(3, resolution.Scopes.Length);
        Assert.Equal(
            3,
            Distinct(resolution.Scopes.Select(scope => scope.Id)));

        // Occurrence order maps onto scope order, and identical intent still
        // mints distinct scopes.
        Assert.Same(first.Question, resolution.Scopes[0].Question);
        Assert.Same(repeat.Question, resolution.Scopes[1].Question);
        Assert.Same(empty.Question, resolution.Scopes[2].Question);
        Assert.NotSame(resolution.Scopes[0].Id, resolution.Scopes[1].Id);
        Assert.Equal(
            resolution.Scopes[0].DeclaringTypeFullName,
            resolution.Scopes[1].DeclaringTypeFullName);

        // A scope with no admitted input on either side still exists.
        Assert.Empty(resolution.Scopes[2].Domains);
        Assert.NotEmpty(resolution.Scopes[0].Domains);

        // No scope is shared and no request crosses one.
        foreach (ResearchTargetScope scope in resolution.Scopes)
        {
            Assert.All(
                scope.Domains.SelectMany(domain => domain.Requests),
                request => Assert.Same(scope.Id, request.Scope));
        }

        // A duplicated occurrence instance is rejected before any scope exists.
        ResearchTargetPlanningRejection rejection = fixture.Reject(first, first);
        Assert.Equal(
            ResearchTargetPlanningRejectionKind.DuplicateSelection,
            rejection.Kind);
    }

    // ----------------------------------------------------- exact-address gate

    [Fact]
    public void ResearchTargetAttempt_AddressEvidenceMismatchBlocksBeforeCensus()
    {
        TargetFixture fixture = TargetFixture.Create([(Sample(), Sample(), null)]);
        ResearchAdmittedInput before =
            fixture.Population.Questions[0].Before[0];
        ResearchAdmittedInput after = fixture.Population.Questions[0].After[0];

        MetadataMethodAddress truth =
            Assert.NotNull(fixture.ResolveDefault(SampleType, "Method")
                .Attempts
                .Select(Resolved)
                .First()
                .Address);

        // Matching evidence resolves, and only the designated input is
        // evaluated.
        ResearchTargetResolution matched = fixture.Resolve(
            fixture.Exact(
                0,
                before,
                SampleType,
                "Method",
                truth,
                ResearchTargetRelationshipRole.Method));
        ResearchTargetDomain domain = Assert.Single(matched.Scopes[0].Domains);
        Assert.Equal(2, domain.Inputs.Length);
        ResearchTargetAttempt attempt = Assert.Single(domain.Attempts);
        Assert.Same(before.Id, attempt.Request.Input);
        Assert.Equal(truth, Resolved(attempt).Address);

        ResearchTargetInputDisposition unevaluated = Assert.Single(
            domain.Inputs.Where(
                disposition => ReferenceEquals(disposition.Input, after.Id)));
        Assert.Equal(
            ResearchTargetDispositionKind.NotRequested,
            unevaluated.Kind);
        Assert.Equal(
            ResearchTargetNotRequestedReason.ExactAddressDesignatesAnotherInput,
            unevaluated.NotRequestedReason);
        Assert.Null(unevaluated.Request);
        var incomplete = Assert.IsType<
            ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(
                Assert.Single(matched.Correspondences));
        Assert.Same(attempt, incomplete.Attempt);
        Assert.Equal(
            ResearchTargetTaintKind.BlockedDomain,
            incomplete.Taint.Kind);
        Assert.Empty(incomplete.Taint.Attempts);
        Assert.Equal([unevaluated], incomplete.Taint.IncompleteInputs);
        Assert.Null(incomplete.StrictKey);
        Assert.Null(incomplete.CorrespondenceKey);

        // A wrong MVID blocks before any census can see a resolved target.
        MetadataMethodAddress foreignModule =
            truth with { ModuleVersionId = Guid.NewGuid() };
        AssertBlocked(
            fixture,
            before,
            foreignModule,
            ResearchTargetRelationshipRole.Method,
            ResearchTargetDiagnosticKind.AddressEvidenceMismatch);

        // So does a wrong MethodDef row in the right module.
        MetadataMethodAddress foreignRow = new(
            truth.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(
                MetadataTokens.GetRowNumber(truth.Handle) + 1));
        AssertBlocked(
            fixture,
            before,
            foreignRow,
            ResearchTargetRelationshipRole.Method,
            ResearchTargetDiagnosticKind.AddressEvidenceMismatch);

        // So does a role that the derived accessor evidence contradicts.
        AssertBlocked(
            fixture,
            before,
            truth,
            ResearchTargetRelationshipRole.Getter,
            ResearchTargetDiagnosticKind.RelationshipRoleEvidenceMismatch);

        // An exact selection that designates an input from another question is
        // rejected before any identity exists.
        TargetFixture twoQuestions = TargetFixture.Create(
            [(Sample(), null, null), (Sample(), null, null)]);
        ResearchTargetPlanningRejection rejection = twoQuestions.Reject(
            twoQuestions.Exact(
                0,
                twoQuestions.Population.Questions[1].Before[0],
                SampleType,
                "Method",
                truth,
                ResearchTargetRelationshipRole.Method));
        Assert.Equal(
            ResearchTargetPlanningRejectionKind.ForeignInput,
            rejection.Kind);
    }

    static void AssertBlocked(
        TargetFixture fixture,
        ResearchAdmittedInput input,
        MetadataMethodAddress address,
        ResearchTargetRelationshipRole role,
        ResearchTargetDiagnosticKind expected)
    {
        ResearchTargetAttempt attempt = Assert.Single(
            fixture.Resolve(
                fixture.Exact(0, input, SampleType, "Method", address, role))
                .Attempts);
        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(attempt.Outcome);
        Assert.Equal(expected, failed.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetMethodAddressBinding_RequiresAnInRangeMethodDef()
    {
        using FileStream stream = File.OpenRead(
            FixtureCatalog.ResearchTargetSample.AssemblyPath());
        using var pe =
            new System.Reflection.PortableExecutable.PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();

        MethodDefinitionHandle method = reader.MethodDefinitions.First();
        Assert.Equal(
            MetadataMethodAddress.Create(reader, method),
            ResearchTargetResolver.TryCreateAddress(
                reader,
                MetadataTokens.GetToken(method)));

        Assert.Null(
            ResearchTargetResolver.TryCreateAddress(
                reader,
                MetadataTokens.GetToken(reader.TypeDefinitions.First())));
        Assert.Null(
            ResearchTargetResolver.TryCreateAddress(
                reader,
                MetadataTokens.GetToken(
                    MetadataTokens.MethodDefinitionHandle(
                        reader.MethodDefinitions.Count + 1))));
    }

    [Fact]
    public void ResearchTargetResolution_StagesEachAdmittedInputOnce()
    {
        // Two selection occurrences in one question produce four requests over
        // two admitted inputs. Each borrowed input must be opened once for all
        // of its requests, not once per request.
        int beforeOpens = 0;
        int afterOpens = 0;
        TargetFixture fixture = TargetFixture.Create(
            [
                (
                    Sample(() => Interlocked.Increment(ref beforeOpens)),
                    Sample(() => Interlocked.Increment(ref afterOpens)),
                    null
                ),
            ]);

        ResearchTargetResolution resolution = fixture.Resolve(
            fixture.Carried(0, SampleType, "Method"),
            fixture.Carried(0, SampleType, "Value:1"));

        Assert.Equal(4, resolution.Attempts.Length);
        Assert.All(
            resolution.Attempts,
            attempt => Assert.IsType<ResearchTargetOutcome.Resolved>(
                attempt.Outcome));
        Assert.Equal(1, beforeOpens);
        Assert.Equal(1, afterOpens);

        // A reference-only input is never opened at all.
        int referenceOpens = 0;
        TargetFixture referenceOnly = TargetFixture.Create(
            [(Sample(() => Interlocked.Increment(ref referenceOpens)), null, null)],
            referenceOnly: static _ => true);
        referenceOnly.ResolveDefault(SampleType, "Method");
        Assert.Equal(0, referenceOpens);
    }

    // ------------------------------------------------------------- inertness

    [Fact]
    public void ResearchTargetResolution_RetainsNoBorrowedResourcesOrPresentation()
    {
        // The exposed surface closure of the result may not reach a borrowed
        // resource, an admission occurrence, a raw exception, or a callback.
        Type[] forbidden =
        [
            typeof(ResearchAdmittedPopulation),
            typeof(ResearchAdmittedQuestion),
            typeof(ResearchAdmittedInput),
            typeof(ResearchComparisonInputOccurrence),
            typeof(ResearchMemberSelectionOccurrence),
            typeof(ResearchTargetPlanningRequest),
            typeof(ResolvedAssemblyReference),
            typeof(IAssemblyReferenceResolver),
            typeof(LibraryBodyIndex),
            typeof(ImplementationAssemblyInput),
            typeof(MetadataReader),
            typeof(System.Reflection.PortableExecutable.PEReader),
            typeof(Stream),
            typeof(Exception),
            typeof(Delegate),
            typeof(IDisposable),
        ];

        IReadOnlyCollection<Type> closure =
            SurfaceClosure(typeof(ResearchTargetResolution));

        // The walk is non-vacuous: it reaches every evidence-bearing type the
        // result exposes, including the terminal outcome arms.
        foreach (Type reached in (Type[])
            [
                typeof(ResearchTargetScope),
                typeof(ResearchTargetDomain),
                typeof(ResearchTargetInputDisposition),
                typeof(ResearchTargetRequest),
                typeof(ResearchTargetAttempt),
                typeof(ResearchTargetOutcome),
                typeof(ResearchTargetOutcome.Resolved),
                typeof(ResearchTargetOutcome.NotFound),
                typeof(ResearchTargetOutcome.Unavailable),
                typeof(ResearchTargetOutcome.Failed),
                typeof(ResearchTargetDiagnostic),
            ])
        {
            Assert.Contains(reached, closure);
        }

        Assert.Empty(Violations(closure, forbidden));

        // The same walk reports a deliberate violation, so it cannot pass by
        // reaching nothing.
        Assert.NotEmpty(
            Violations(SurfaceClosure(typeof(BorrowedExposureProbe)), forbidden));

        // The bounded Research diagnostics carry Research-owned text only.
        TargetFixture fixture = TargetFixture.Create([(Unreadable(), null, null)]);
        ResearchTargetAttempt attempt = Assert.Single(
            fixture.ResolveDefault(SampleType, "Method").Attempts);
        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(attempt.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.InputUnreadable,
            failed.Diagnostic.Kind);
        Assert.DoesNotContain(
            TargetFixture.UnreadableMarker,
            failed.Diagnostic.Summary,
            StringComparison.Ordinal);

        // Every declared diagnostic kind has bounded Research-owned text and
        // exactly one terminal arm.
        Dictionary<ResearchTargetDiagnosticKind, ResearchTargetOutcomeKind>
            expectedArms = new()
            {
                [ResearchTargetDiagnosticKind.DeclaringTypeAbsent] =
                    ResearchTargetOutcomeKind.NotFound,
                [ResearchTargetDiagnosticKind.DeclaringTypeForwarded] =
                    ResearchTargetOutcomeKind.Unavailable,
                [ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.IncompleteMetadataSurface] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.ReferenceOnlyInput] =
                    ResearchTargetOutcomeKind.Unavailable,
                [ResearchTargetDiagnosticKind.DomainAmbiguous] =
                    ResearchTargetOutcomeKind.Unavailable,
                [ResearchTargetDiagnosticKind.AssemblyIdentityMismatch] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.ModuleIdentityMismatch] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.StandaloneModule] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.InvalidMethodDefinitionToken] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.AddressEvidenceMismatch] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind
                    .RelationshipRoleEvidenceMismatch] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.InputUnreadable] =
                    ResearchTargetOutcomeKind.Failed,
                [ResearchTargetDiagnosticKind.ResolutionFailed] =
                    ResearchTargetOutcomeKind.Failed,
            };
        Assert.Equal(
            Enum.GetValues<ResearchTargetDiagnosticKind>().ToHashSet(),
            expectedArms.Keys.ToHashSet());
        foreach ((ResearchTargetDiagnosticKind kind,
            ResearchTargetOutcomeKind arm) in expectedArms)
        {
            Assert.NotEmpty(Diagnostic(kind).Summary);
            Assert.Equal(
                arm,
                ResearchTargetResolutionValidator.ExpectedArm(kind));
        }
    }

    // ---------------------------------------------------------- cancellation

    [Fact]
    public void ResearchTargetCancellation_ExposesNoPartialPopulationOrResult()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), Sample(), null), (Sample(), null, null)]);

        using CancellationTokenSource source = new();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => fixture.ResolveRaw(
                [fixture.Carried(0, SampleType, "Method")],
                source.Token));

        // Cancellation observed after partial internal work still exposes
        // nothing: no result, and no attempt evidence.
        using CancellationTokenSource staged = new();
        int observed = 0;
        ImplementationComparisonInputOccurrence tripwire = Sample(
            () =>
            {
                if (Interlocked.Increment(ref observed) == 1)
                    staged.Cancel();
            });
        TargetFixture partial = TargetFixture.Create(
            [(Sample(), tripwire, null)]);
        Assert.Throws<OperationCanceledException>(
            () => partial.ResolveRaw(
                [partial.Carried(0, SampleType, "Method")],
                staged.Token));

        // A rejection also exposes no resolution.
        ResearchTargetPlanningOutcome rejected =
            ResearchTargetResolver.Resolve(
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    fixture.Roles,
                    []),
                TestContext.Current.CancellationToken);
        Assert.IsType<ResearchTargetPlanningOutcome.Rejected>(rejected);
    }

    [Fact]
    public void ResearchTargetCancellation_RetryPreservesAdmissionAndMintsFreshTargets()
    {
        using CancellationTokenSource source = new();
        int observed = 0;
        ImplementationComparisonInputOccurrence tripwire = Sample(
            () =>
            {
                if (Interlocked.Increment(ref observed) == 1)
                    source.Cancel();
            });
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), tripwire, null)]);

        ResearchComparisonOperationId operation = fixture.Population.Operation;
        ImmutableArray<ResearchComparisonQuestionId> questions =
            [.. fixture.Population.Questions.Select(question => question.Id)];
        ImmutableArray<ResearchComparisonInputId> inputs =
            [.. fixture.Population.Inputs.Select(input => input.Id)];

        Assert.Throws<OperationCanceledException>(
            () => fixture.ResolveRaw(
                [fixture.Carried(0, SampleType, "Method")],
                source.Token));

        // The admitted identities survive the cancelled attempt untouched.
        Assert.Same(operation, fixture.Population.Operation);
        Assert.Equal(
            questions,
            fixture.Population.Questions.Select(question => question.Id));
        Assert.Equal(
            inputs,
            fixture.Population.Inputs.Select(input => input.Id));

        // The retry reuses those identities and mints fresh target identities.
        ResearchTargetResolution first =
            fixture.ResolveDefault(SampleType, "Method");
        ResearchTargetResolution second =
            fixture.ResolveDefault(SampleType, "Method");

        Assert.Same(operation, first.Operation);
        Assert.Same(operation, second.Operation);
        Assert.All(
            first.Requests,
            request => Assert.Contains(request.Input, inputs));

        Assert.NotSame(first.Scopes[0].Id, second.Scopes[0].Id);
        Assert.NotSame(first.Domains[0].Id, second.Domains[0].Id);
        Assert.Empty(
            first.Requests.Select(request => request.Id)
                .Intersect(second.Requests.Select(request => request.Id)));
        Assert.Empty(
            first.Attempts.Select(attempt => attempt.Id)
                .Intersect(second.Attempts.Select(attempt => attempt.Id)));

        // Both retries resolve the same physical target.
        Assert.Equal(
            Resolved(first.Attempts[0]).Address,
            Resolved(second.Attempts[0]).Address);
    }

    // ------------------------------------------------- declaring-type outcomes

    [Fact]
    public void ResearchTargetDeclaringType_DistinguishesAbsentFromForwarded()
    {
        TargetFixture fixture = TargetFixture.Create([(Sample(), null, null)]);

        // No TypeDef and no forwarder: a Research-owned bounded NotFound.
        ResearchTargetAttempt absent = Assert.Single(
            fixture.ResolveDefault(AbsentType, "Method").Attempts);
        var notFound = Assert.IsType<ResearchTargetOutcome.NotFound>(absent.Outcome);
        Assert.Null(notFound.MetadataDiagnostic);
        Assert.NotNull(notFound.ResearchDiagnostic);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeAbsent,
            notFound.ResearchDiagnostic.Kind);
        Assert.Empty(notFound.Candidates);

        // No TypeDef but an exact forwarder: Unavailable, never absence.
        ResearchTargetAttempt forwarded = Assert.Single(
            fixture.ResolveDefault(ForwardedType, "ToString").Attempts);
        var unavailable =
            Assert.IsType<ResearchTargetOutcome.Unavailable>(forwarded.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeForwarded,
            unavailable.Diagnostic.Kind);

        // A nested declaring type resolves only under its exact metadata full
        // name.
        Assert.IsType<ResearchTargetOutcome.Resolved>(
            Assert.Single(
                fixture.ResolveDefault(NestedType, "Method").Attempts).Outcome);
        Assert.IsType<ResearchTargetOutcome.NotFound>(
            Assert.Single(
                fixture.ResolveDefault(
                    NestedType.Replace('.', '+'),
                    "Method").Attempts).Outcome);
    }

    [Fact]
    public void ResearchTargetDeclaringType_DoesNotInferAbsenceUnderForwarder()
    {
        byte[] image = BuildResearchSurfaceImage(
            cyclicTypeName: null,
            duplicateTypeName: null,
            forwarderTypeName: "Outer",
            nestedForwarderTypeName: "Inner");
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataTypeDefinitionName structuredName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["Outer", "Inner"])).Name;
        Assert.IsType<TypeDeclarationResult.Forwarded>(
            MetadataTypeDeclarationProbe.Probe(
                pe.GetMetadataReader(),
                structuredName));

        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);

        ResearchTargetAttempt forwarded = Assert.Single(
            fixture.ResolveDefault("N.Outer.Inner", "Method").Attempts);
        var unavailable =
            Assert.IsType<ResearchTargetOutcome.Unavailable>(forwarded.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeForwarded,
            unavailable.Diagnostic.Kind);

        ResearchTargetAttempt unrelated = Assert.Single(
            fixture.ResolveDefault("N.Other.Inner", "Method").Attempts);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeAbsent,
            Assert.IsType<ResearchTargetOutcome.NotFound>(unrelated.Outcome)
                .ResearchDiagnostic!.Kind);
    }

    [Fact]
    public void ResearchTargetDeclaringType_DoesNotInferAbsenceFromPartialSurface()
    {
        byte[] image = BuildResearchSurfaceImage(
            cyclicTypeName: "Rejected",
            duplicateTypeName: null);
        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);

        ResearchTargetAttempt attempt = Assert.Single(
            fixture.ResolveDefault("Rejected", "Method").Attempts);
        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            attempt.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.IncompleteMetadataSurface,
            failed.Diagnostic.Kind);

        ResearchTargetAttempt missingMember = Assert.Single(
            fixture.ResolveDefault("N.Sibling", "Missing").Attempts);
        var memberFailure = Assert.IsType<ResearchTargetOutcome.Failed>(
            missingMember.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.IncompleteMetadataSurface,
            memberFailure.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetDeclaringType_DoesNotInferAbsenceFromMalformedExport()
    {
        byte[] image = BuildResearchSurfaceImage(
            cyclicTypeName: null,
            duplicateTypeName: null,
            malformedAssemblyRefExportTypeName: "Malformed");
        ApiSurface surface = ExtractSurface(image);
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            ApiSurfaceInspectionFailure.TypeForwarderIdentityOperation,
            failure.Operation);
        Assert.Equal(0x27000001, failure.SubjectToken);

        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);
        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                fixture.ResolveDefault("N.Malformed", "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.IncompleteMetadataSurface,
            failed.Diagnostic.Kind);

        TargetFixture complete = TargetFixture.Create(
            [(Sample(), null, null)]);
        var notFound = Assert.IsType<ResearchTargetOutcome.NotFound>(
            Assert.Single(
                complete.ResolveDefault(AbsentType, "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeAbsent,
            notFound.ResearchDiagnostic!.Kind);
    }

    [Fact]
    public void ResearchTargetDeclaringType_RejectsDuplicateExactDeclarations()
    {
        byte[] image = BuildResearchSurfaceImage(
            cyclicTypeName: null,
            duplicateTypeName: "Duplicate");
        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);

        ResearchTargetAttempt attempt = Assert.Single(
            fixture.ResolveDefault("N.Duplicate", "Method").Attempts);
        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            attempt.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous,
            failed.Diagnostic.Kind);

        byte[] mixedImage = BuildResearchSurfaceImage(
            cyclicTypeName: null,
            duplicateTypeName: "Mixed",
            forwarderTypeName: "Mixed");
        TargetFixture mixed = TargetFixture.Create(
            [(Occurrence(mixedImage), null, null)]);
        var mixedFailure = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                mixed.ResolveDefault("N.Mixed", "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous,
            mixedFailure.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetDeclaringType_RejectsFailedExactDuplicate()
    {
        byte[] image = BuildFailedExactDuplicateImage();
        ApiSurface surface = ExtractSurface(image);
        Assert.Single(
            surface.Types,
            type => type.DefinitionName?.ToMetadataFullName() == "N.C");
        Assert.Single(
            surface.InspectionFailures,
            failure =>
                failure.OwningTypeDefinition?.ToMetadataFullName() == "N.C");

        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);

        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                fixture.ResolveDefault("N.C", "M").Attempts).Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous,
            failed.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetAbsence_UnscopedForwarderFailureBlocksOnlyAbsence()
    {
        byte[] image = BuildMalformedForwarderImage();
        ApiSurface surface = ExtractSurface(image);
        Assert.Empty(surface.TypeForwarders);
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            ApiSurfaceInspectionFailure.TypeForwarderIdentityOperation,
            failure.Operation);
        Assert.Null(failure.OwningTypeDefinition);

        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);

        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                fixture.ResolveDefault("N.C", "Missing").Attempts).Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.IncompleteMetadataSurface,
            failed.Diagnostic.Kind);

        Assert.IsType<ResearchTargetOutcome.Resolved>(
            Assert.Single(
                fixture.ResolveDefault("N.C", "M").Attempts).Outcome);
    }

    [Fact]
    public void ResearchTargetForwarder_RetainedEvidencePrecedesUnscopedFailure()
    {
        byte[] image = BuildMalformedForwarderImage(
            includeLocalType: false,
            validForwarder: "Forwarded");
        ApiSurface surface = ExtractSurface(image);
        Assert.Equal(
            "N.Forwarded",
            Assert.Single(surface.TypeForwarders).TypeName);
        Assert.Equal(
            ApiSurfaceInspectionFailure.TypeForwarderIdentityOperation,
            Assert.Single(surface.InspectionFailures).Operation);

        TargetFixture fixture = TargetFixture.Create(
            [(Occurrence(image), null, null)]);
        var unavailable = Assert.IsType<ResearchTargetOutcome.Unavailable>(
            Assert.Single(
                fixture.ResolveDefault(
                    "N.Forwarded",
                    "M").Attempts).Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeForwarded,
            unavailable.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetAbsence_FailedExtensionContainerBlocksProjectedMember()
    {
        byte[] healthyImage = BuildExtensionProjectionImage(
            includeBrokenMethod: false);
        ApiType healthyReceiver = ExtractSurface(healthyImage).Types.Single(
            type => type.DefinitionName?.ToMetadataFullName() == "N.Y");
        Assert.Contains(
            healthyReceiver.Members,
            member => member is { Name: "M", Kind: "extension-method" });
        Assert.IsType<ResearchTargetOutcome.Resolved>(
            Assert.Single(
                TargetFixture.Create(
                        [(Occurrence(healthyImage), null, null)])
                    .ResolveDefault("N.Y", "M")
                    .Attempts)
                .Outcome);

        byte[] brokenImage = BuildExtensionProjectionImage(
            includeBrokenMethod: true);
        ApiSurface brokenSurface = ExtractSurface(brokenImage);
        ApiType brokenReceiver = brokenSurface.Types.Single(
            type => type.DefinitionName?.ToMetadataFullName() == "N.Y");
        Assert.DoesNotContain(
            brokenReceiver.Members,
            member => member.Name == "M");
        Assert.Single(
            brokenSurface.InspectionFailures,
            failure =>
                failure.OwningTypeDefinition?.ToMetadataFullName() == "N.X");

        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                TargetFixture.Create(
                        [(Occurrence(brokenImage), null, null)])
                    .ResolveDefault("N.Y", "M")
                    .Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.IncompleteMetadataSurface,
            failed.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetReferenceOnlyInput_TerminatesWithoutOpening()
    {
        // A reference-only input is never opened: its descriptor throws if it
        // is, and its body index would fail module validation.
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), Unreadable(), null)],
            referenceOnly: input => input.Side == ResearchComparisonSide.After);

        ResearchTargetResolution resolution =
            fixture.ResolveDefault(SampleType, "Method");
        Assert.Equal(2, resolution.Attempts.Length);

        ResearchTargetAttempt reference = resolution.Attempts.Single(
            attempt => attempt.Request.Side == ResearchComparisonSide.After);
        var unavailable =
            Assert.IsType<ResearchTargetOutcome.Unavailable>(reference.Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.ReferenceOnlyInput,
            unavailable.Diagnostic.Kind);

        // Its disposition is still Requested: the input was evaluated and
        // terminated, not silently skipped.
        ResearchTargetDomain domain = Assert.Single(resolution.Scopes[0].Domains);
        Assert.All(
            domain.Inputs,
            disposition => Assert.Equal(
                ResearchTargetDispositionKind.Requested,
                disposition.Kind));
        Assert.Contains(
            domain.Inputs,
            disposition =>
                disposition.Role == ResearchTargetInputRole.ReferenceOnly);

        // The implementation side still resolves.
        Assert.IsType<ResearchTargetOutcome.Resolved>(
            resolution.Attempts
                .Single(attempt =>
                    attempt.Request.Side == ResearchComparisonSide.Before)
                .Outcome);
    }

    [Fact]
    public void ResearchTargetInputValidation_RejectsMismatchedModuleEvidence()
    {
        // A body index from another module fails before selection runs.
        TargetFixture mismatched = TargetFixture.Create(
            [(SampleWithForeignModule(), null, null)]);
        var moduleFailure = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                mismatched.ResolveDefault(SampleType, "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.ModuleIdentityMismatch,
            moduleFailure.Diagnostic.Kind);

        // A descriptor identity that does not name the live image fails too.
        TargetFixture renamed = TargetFixture.Create(
            [(SampleWithForeignIdentity(), null, null)]);
        var identityFailure = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                renamed.ResolveDefault(SampleType, "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.AssemblyIdentityMismatch,
            identityFailure.Diagnostic.Kind);

        // A body index with no assembly identity disagrees with the live
        // assembly rather than proving that the live image is standalone.
        TargetFixture standalone = TargetFixture.Create(
            [(SampleWithStandaloneModule(), null, null)]);
        var standaloneFailure = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                standalone.ResolveDefault(SampleType, "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.AssemblyIdentityMismatch,
            standaloneFailure.Diagnostic.Kind);

        // Malformed content is a bounded input failure rather than an escaping
        // Metadata exception or a partial attempt set.
        TargetFixture malformed = TargetFixture.Create(
            [(Malformed(), null, null)]);
        var malformedFailure = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                malformed.ResolveDefault(SampleType, "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.InputUnreadable,
            malformedFailure.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetInputValidation_RejectsArtifactMvidReplacement()
    {
        byte[] selected = File.ReadAllBytes(
            FixtureCatalog.ResearchTargetSample.AssemblyPath());
        Guid selectedMvid = ReadModuleVersionId(selected);
        ArtifactAcquisitionRegistration registration =
            RegisterArtifact(
                () => new MemoryStream(selected, writable: false));
        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                registration,
                () => new MemoryStream(selected, writable: false),
                AssemblyResolutionProvenance.Project(
                    "ArtifactReplacement",
                    tfm: null,
                    rid: null))
            ?? throw new InvalidOperationException(
                "The selected fixture must be a managed assembly.");

        Guid replacementMvid = Guid.NewGuid();
        ReplaceGuid(selected, selectedMvid, replacementMvid);
        AssemblyReferenceIdentity identity = ReadAssemblyIdentity(selected);
        LibraryBodyIndex replacementIndex = LibraryBodyIndex.FromEvidence(
            [],
            [],
            moduleIdentity: new(identity, replacementMvid));
        var occurrence = new ImplementationComparisonInputOccurrence(
            descriptor,
            new NullResolver(),
            replacementIndex);
        TargetFixture fixture = TargetFixture.Create(
            [(occurrence, null, null)]);

        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(
                fixture.ResolveDefault(SampleType, "Method").Attempts)
                .Outcome);
        Assert.Equal(
            ResearchTargetDiagnosticKind.ModuleIdentityMismatch,
            failed.Diagnostic.Kind);
    }

    [Fact]
    public void ResearchTargetPlanning_RejectsEveryDeclaredInvalidShape()
    {
        TargetFixture fixture = TargetFixture.Create(
            [(Sample(), Sample(), null), (Sample(), null, null)]);
        ResearchAdmittedInput admitted = fixture.Population.Inputs[0];
        ResearchAdmittedPopulation stranger = TargetFixture
            .Create([(Sample(), null, null)])
            .Population;
        MetadataMethodAddress address = new(Guid.NewGuid(), default);

        Dictionary<ResearchTargetPlanningRejectionKind,
            ResearchTargetPlanningRequest> shapes = new()
        {
            [ResearchTargetPlanningRejectionKind.UnsupportedProfile] =
                new ResearchTargetPlanningRequest(
                    BodySignalPopulation(),
                    [],
                    [fixture.Carried(0, SampleType, "Method")]),
            [ResearchTargetPlanningRejectionKind.MissingSelections] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    fixture.Roles,
                    []),
            [ResearchTargetPlanningRejectionKind.MissingSelection] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    fixture.Roles,
                    [null]),
            [ResearchTargetPlanningRejectionKind.DuplicateSelection] =
                Duplicate(fixture),
            [ResearchTargetPlanningRejectionKind.ForeignQuestion] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    fixture.Roles,
                    [
                        new ResearchCarriedMemberSelection(
                            stranger.Questions[0].Id,
                            SampleType,
                            MemberTargetSelector.Parse("Method")),
                    ]),
            [ResearchTargetPlanningRejectionKind.ForeignInput] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    fixture.Roles,
                    [
                        new ResearchExactAddressMemberSelection(
                            fixture.Population.Questions[0].Id,
                            stranger.Inputs[0],
                            SampleType,
                            MemberTargetSelector.Parse("Method"),
                            address,
                            ResearchTargetRelationshipRole.Method),
                    ]),
            [ResearchTargetPlanningRejectionKind.MissingInputRole] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    [null],
                    [fixture.Carried(0, SampleType, "Method")]),
            [ResearchTargetPlanningRejectionKind.DuplicateInputRole] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    [
                        .. fixture.Roles,
                        new ResearchTargetInputRoleAssignment(
                            admitted,
                            ResearchTargetInputRole.ReferenceOnly),
                    ],
                    [fixture.Carried(0, SampleType, "Method")]),
            [ResearchTargetPlanningRejectionKind.ForeignInputRole] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    [
                        .. fixture.Roles,
                        new ResearchTargetInputRoleAssignment(
                            stranger.Inputs[0],
                            ResearchTargetInputRole.Implementation),
                    ],
                    [fixture.Carried(0, SampleType, "Method")]),
            [ResearchTargetPlanningRejectionKind.UndeclaredInputRole] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    [
                        new ResearchTargetInputRoleAssignment(
                            admitted,
                            (ResearchTargetInputRole)77),
                    ],
                    [fixture.Carried(0, SampleType, "Method")]),
            [ResearchTargetPlanningRejectionKind.UndeclaredRelationshipRole] =
                new ResearchTargetPlanningRequest(
                    fixture.Population,
                    fixture.Roles,
                    [
                        new ResearchExactAddressMemberSelection(
                            fixture.Population.Questions[0].Id,
                            admitted,
                            SampleType,
                            MemberTargetSelector.Parse("Method"),
                            address,
                            (ResearchTargetRelationshipRole)77),
                    ]),
        };

        // The expected set is derived from the declaration, so a missing or
        // stale rejection kind fails this gate.
        Assert.Equal(
            Enum.GetValues<ResearchTargetPlanningRejectionKind>().ToHashSet(),
            shapes.Keys.ToHashSet());

        foreach ((ResearchTargetPlanningRejectionKind kind,
            ResearchTargetPlanningRequest invalid) in shapes)
        {
            var rejected = Assert.IsType<ResearchTargetPlanningOutcome.Rejected>(
                ResearchTargetResolver.Resolve(
                    invalid,
                    TestContext.Current.CancellationToken));
            Assert.Equal(kind, rejected.Rejection.Kind);
            Assert.NotEmpty(rejected.Rejection.Summary);
            Assert.NotNull(rejected.Rejection.Location);
        }

        static ResearchTargetPlanningRequest Duplicate(TargetFixture fixture)
        {
            ResearchCarriedMemberSelection selection =
                fixture.Carried(0, SampleType, "Method");
            return new ResearchTargetPlanningRequest(
                fixture.Population,
                fixture.Roles,
                [selection, selection]);
        }
    }

    [Fact]
    public void ResearchTargetIdentities_AreOwnerIssuedReferenceIdentities()
    {
        foreach (Type type in (Type[])
            [
                typeof(ResearchTargetScopeId),
                typeof(ResearchTargetDomainId),
                typeof(ResearchTargetRequestId),
                typeof(ResearchTargetAttemptId),
            ])
        {
            Assert.True(type.IsSealed, type.Name);
            Assert.Empty(
                type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

            // No parsing, string conversion, or ordinal surrogate.
            Assert.Empty(
                type.GetMethods(BindingFlags.Public | BindingFlags.Static));
            Assert.All(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                property => Assert.False(
                    property.PropertyType == typeof(string)
                        || property.PropertyType == typeof(int)
                        || property.PropertyType == typeof(Guid),
                    $"{type.Name}.{property.Name}"));

            // Equality is reference identity, not structural.
            Assert.Same(
                typeof(object).GetMethod(nameof(Equals), [typeof(object)]),
                type.GetMethod(nameof(Equals), [typeof(object)])!
                    .GetBaseDefinition());
        }
    }

    // ---------------------------------------------------------------- helpers

    static ResearchTargetOutcome.Resolved Resolved(ResearchTargetAttempt attempt)
        => Assert.IsType<ResearchTargetOutcome.Resolved>(attempt.Outcome);

    static MemberTargetDiagnostic MetadataDiagnostic(ResearchTargetOutcome outcome)
        => outcome switch
        {
            ResearchTargetOutcome.NotFound { MetadataDiagnostic: { } value } => value,
            ResearchTargetOutcome.Ambiguous ambiguous => ambiguous.Diagnostic,
            ResearchTargetOutcome.Rejected rejected => rejected.Diagnostic,
            _ => throw new InvalidOperationException(
                $"Expected a Metadata diagnostic, found {outcome.Kind}."),
        };

    static ResearchTargetDiagnostic Diagnostic(ResearchTargetDiagnosticKind kind)
        => (ResearchTargetDiagnostic)Activator.CreateInstance(
            typeof(ResearchTargetDiagnostic),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [kind],
            culture: null)!;

    static int ExpectedToken(
        ApiType type,
        ResolvedMemberTarget target,
        ResearchTargetRelationshipRole role)
    {
        ApiMember member = type.Members.Single(
            candidate =>
                candidate.Name == target.ApiMember.Member.Name
                && candidate.Kind == target.ApiMember.Member.Kind);
        return role switch
        {
            ResearchTargetRelationshipRole.Method =>
                Assert.NotNull(member.MetadataToken),
            ResearchTargetRelationshipRole.Getter =>
                Assert.NotNull(member.GetterToken),
            ResearchTargetRelationshipRole.Setter =>
                Assert.NotNull(member.SetterToken),
            ResearchTargetRelationshipRole.Adder =>
                Assert.NotNull(member.AdderToken),
            ResearchTargetRelationshipRole.Remover =>
                Assert.NotNull(member.RemoverToken),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    static int Distinct<T>(IEnumerable<T> values)
        where T : class
        => values
            .Distinct((IEqualityComparer<T>)ReferenceEqualityComparer.Instance)
            .Count();

    static IReadOnlyList<string> Violations(
        IReadOnlyCollection<Type> closure,
        IReadOnlyList<Type> forbidden)
    =>
        [
            .. from type in closure
               from member in ExposedMembers(type)
               from exposed in SignatureTypes(member)
               where forbidden.Any(banned => banned.IsAssignableFrom(exposed))
               select $"{type.Name}.{member.Name}: {exposed.Name}",
        ];

    /// <summary>
    /// Every indirect shape through which a result surface could retain a
    /// borrowed resource, an admission occurrence, a raw exception, or a
    /// callback. The retention walk must report all of them.
    /// </summary>
    sealed class BorrowedExposureProbe
    {
        BorrowedExposureProbe()
        {
        }

        public LibraryBodyIndex BodyIndex => null!;

        public ImmutableArray<ResearchAdmittedInput> Inputs => [];

        public IReadOnlyList<ResolvedAssemblyReference> Assemblies => null!;

        public Func<Stream> Open => null!;

        public Exception Failure => null!;

        public MetadataReader Read() => default!;
    }

    static IReadOnlyCollection<Type> SurfaceClosure(Type root)
    {
        HashSet<Type> closure = [];
        Queue<Type> pending = new();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();
            if (!closure.Add(type))
                continue;

            foreach (MemberInfo member in ExposedMembers(type))
            {
                foreach (Type exposed in SignatureTypes(member))
                {
                    if (IsResearchOwned(exposed))
                        pending.Enqueue(exposed);
                }
            }

            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
                pending.Enqueue(nested);
        }

        return closure;
    }

    static IEnumerable<MemberInfo> ExposedMembers(Type type)
        => type.GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly);

    static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
                foreach (Type type in ComponentTypes(property.PropertyType))
                    yield return type;
                break;

            case FieldInfo field:
                foreach (Type type in ComponentTypes(field.FieldType))
                    yield return type;
                break;

            case MethodInfo method:
                foreach (Type type in ComponentTypes(method.ReturnType))
                    yield return type;
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    foreach (Type type in ComponentTypes(parameter.ParameterType))
                        yield return type;
                }

                break;
        }
    }

    static IEnumerable<Type> ComponentTypes(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
            type = type.GetElementType()!;

        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        yield return underlying;
        if (!underlying.IsGenericType)
            yield break;

        foreach (Type argument in underlying.GetGenericArguments())
        {
            foreach (Type component in ComponentTypes(argument))
                yield return component;
        }
    }

    static bool IsResearchOwned(Type type)
        => type.Assembly == typeof(ResearchTargetResolution).Assembly;

    static ResearchAdmittedPopulation BodySignalPopulation()
        => Assert.IsType<ResearchAdmissionOutcome.Admitted>(
            ResearchComparisonAdmission.Admit(
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.BodySignal,
                    [
                        new ResearchComparisonAdmissionQuestion(
                            [
                                new BodySignalComparisonInputOccurrence(
                                    LibraryBodyIndex.Open(
                                        FixtureCatalog.ResearchTargetSample
                                            .AssemblyPath())),
                            ],
                            []),
                    ]))).Population;

    // ------------------------------------------------------ occurrence builders

    static ImplementationComparisonInputOccurrence Sample(Action? onOpen = null)
        => Occurrence(
            FixtureCatalog.ResearchTargetSample.AssemblyPath(),
            onOpen);

    static ImplementationComparisonInputOccurrence Diff(FixtureDefinition fixture)
        => Occurrence(fixture.AssemblyPath());

    static ImplementationComparisonInputOccurrence Occurrence(
        string path,
        Action? onOpen = null)
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(path);
        return new ImplementationComparisonInputOccurrence(
            ResolvedAssemblyReference.Create(
                index.ModuleIdentity.AssemblyIdentity!,
                path,
                () =>
                {
                    onOpen?.Invoke();
                    return File.OpenRead(path);
                },
                AssemblyResolutionProvenance.Project(
                    Path.GetFileNameWithoutExtension(path),
                    tfm: null,
                    rid: null)),
            new NullResolver(),
            index);
    }

    static ImplementationComparisonInputOccurrence Occurrence(byte[] image)
    {
        AssemblyReferenceIdentity identity = ReadAssemblyIdentity(image);
        Guid mvid = ReadModuleVersionId(image);
        return new ImplementationComparisonInputOccurrence(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Project(
                    identity.Name,
                    tfm: null,
                    rid: null)),
            new NullResolver(),
            LibraryBodyIndex.FromEvidence(
                [],
                [],
                moduleIdentity: new(identity, mvid)));
    }

    static AssemblyReferenceIdentity ReadAssemblyIdentity(byte[] image)
    {
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static Guid ReadModuleVersionId(byte[] image)
    {
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    static ArtifactAcquisitionRegistration RegisterArtifact(
        Func<Stream> openRead)
    {
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                TestArtifactProvenance.Instance,
                openRead);
        }

        authority.CreateRetainedContent(
            contribution.Registration,
            openRead);
        authority.CompleteAdmission(admission);
        return contribution.Registration;
    }

    static void ReplaceGuid(byte[] image, Guid oldValue, Guid newValue)
    {
        ReadOnlySpan<byte> oldBytes = oldValue.ToByteArray();
        int found = -1;
        for (int index = 0; index <= image.Length - oldBytes.Length; index++)
        {
            if (!image.AsSpan(index, oldBytes.Length).SequenceEqual(oldBytes))
                continue;

            Assert.Equal(-1, found);
            found = index;
        }

        Assert.True(found >= 0);
        newValue.TryWriteBytes(image.AsSpan(found, oldBytes.Length));
    }

    static byte[] BuildResearchSurfaceImage(
        string? cyclicTypeName,
        string? duplicateTypeName,
        string? forwarderTypeName = null,
        string? nestedForwarderTypeName = null,
        string? malformedAssemblyRefExportTypeName = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("ResearchSurface.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ResearchSurface"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        if (cyclicTypeName is not null)
        {
            TypeDefinitionHandle cyclic = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString(cyclicTypeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(cyclic, cyclic);
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Sibling"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        if (duplicateTypeName is not null)
        {
            StringHandle ns = metadata.GetOrAddString("N");
            for (int index = 0; index < 2; index++)
            {
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    ns,
                    metadata.GetOrAddString(duplicateTypeName),
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: MetadataTokens.MethodDefinitionHandle(1));
            }
        }

        if (forwarderTypeName is not null)
        {
            AssemblyReferenceHandle target = metadata.AddAssemblyReference(
                metadata.GetOrAddString("ForwarderTarget"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            ExportedTypeHandle root = metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(forwarderTypeName),
                target,
                typeDefinitionId: 0);
            if (nestedForwarderTypeName is not null)
            {
                metadata.AddExportedType(
                    TypeAttributes.NestedPublic,
                    default,
                    metadata.GetOrAddString(nestedForwarderTypeName),
                    root,
                    typeDefinitionId: 0);
            }
        }

        if (malformedAssemblyRefExportTypeName is not null)
        {
            AssemblyReferenceHandle target = metadata.AddAssemblyReference(
                metadata.GetOrAddString("MalformedTarget"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            metadata.AddExportedType(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    malformedAssemblyRefExportTypeName),
                target,
                typeDefinitionId: 0);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildSharedAccessorImage(bool eventMember)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("SharedAccessor.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SharedAccessor"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(10, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var accessorSignature = new BlobBuilder();
        new BlobEncoder(accessorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(eventMember ? "change_Changed" : "Value"),
            metadata.GetOrAddBlob(accessorSignature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        if (eventMember)
        {
            TypeReferenceHandle eventType = metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("EventHandler"));
            EventDefinitionHandle @event = metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString("Changed"),
                eventType);
            metadata.AddEventMap(type, @event);
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Adder,
                accessor);
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Remover,
                accessor);
        }
        else
        {
            var propertySignature = new BlobBuilder();
            new BlobEncoder(propertySignature).PropertySignature(
                isInstanceProperty: true).Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    _ => { });
            PropertyDefinitionHandle property = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(propertySignature));
            metadata.AddPropertyMap(type, property);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Getter,
                accessor);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Setter,
                accessor);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildFailedExactDuplicateImage()
    {
        MetadataBuilder metadata = CreatePartialSurfaceMetadata();
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        AddAbstractMethod(metadata, "M", ValidMethodSignature(metadata));
        var malformedSignature = new BlobBuilder();
        malformedSignature.WriteByte(0xff);
        AddAbstractMethod(
            metadata,
            "Broken",
            metadata.GetOrAddBlob(malformedSignature));
        return Serialize(metadata);
    }

    static byte[] BuildMalformedForwarderImage(
        bool includeLocalType = true,
        string? validForwarder = null)
    {
        MetadataBuilder metadata = CreatePartialSurfaceMetadata();
        if (includeLocalType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("C"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            AddAbstractMethod(metadata, "M", ValidMethodSignature(metadata));
        }

        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("ForwarderTarget"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        if (validForwarder is not null)
        {
            metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(validForwarder),
                target,
                typeDefinitionId: 0);
        }

        metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Broken"),
            target,
            typeDefinitionId: 0);

        byte[] image = Serialize(metadata);
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        int typeNameOffset =
            pe.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.ExportedType)
            + ((reader.GetTableRowCount(TableIndex.ExportedType) - 1)
                * reader.GetTableRowSize(TableIndex.ExportedType))
            + sizeof(uint)
            + sizeof(uint);
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(typeNameOffset, sizeof(ushort)),
            ushort.MaxValue);
        return image;
    }

    static byte[] BuildExtensionProjectionImage(bool includeBrokenMethod)
    {
        MetadataBuilder metadata = CreatePartialSurfaceMetadata();
        AssemblyReferenceHandle coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(10, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle extensionAttribute = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("ExtensionAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle extensionConstructor =
            metadata.AddMemberReference(
                extensionAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(constructorSignature));
        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(1);
        attributeValue.WriteUInt16(0);
        BlobHandle extensionValue = metadata.GetOrAddBlob(attributeValue);

        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Y"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle extensionClass = metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("X"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(
            extensionClass,
            extensionConstructor,
            extensionValue);

        var extensionSignature = new BlobBuilder();
        new BlobEncoder(extensionSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: false)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters
                    .AddParameter()
                    .Type()
                    .Type(
                        MetadataTokens.TypeDefinitionHandle(2),
                        isValueType: false));
        MethodDefinitionHandle extensionMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(extensionSignature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);
        metadata.AddCustomAttribute(
            extensionMethod,
            extensionConstructor,
            extensionValue);

        if (includeBrokenMethod)
        {
            var brokenSignature = new BlobBuilder();
            brokenSignature.WriteByte(0xff);
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Broken"),
                metadata.GetOrAddBlob(brokenSignature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(2));
        }

        return Serialize(metadata);
    }

    static MetadataBuilder CreatePartialSurfaceMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("PartialSurface.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PartialSurface"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static BlobHandle ValidMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        return metadata.GetOrAddBlob(signature);
    }

    static void AddAbstractMethod(
        MetadataBuilder metadata,
        string name,
        BlobHandle signature)
        => metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            signature,
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ApiSurface ExtractSurface(byte[] image)
    {
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        return ApiSurfaceExtractor.Extract(
            pe,
            includeAll: true,
            typesOnly: false,
            includeCompilerGenerated: true);
    }

    sealed class TestArtifactProvenance : IArtifactProvenance
    {
        public static TestArtifactProvenance Instance { get; } = new();
    }

    /// <summary>
    /// A real image whose Analysis body index names another module, so the
    /// live MVID and the body-index MVID disagree.
    /// </summary>
    static ImplementationComparisonInputOccurrence SampleWithForeignModule()
    {
        string path = FixtureCatalog.ResearchTargetSample.AssemblyPath();
        LibraryBodyIndex foreign =
            LibraryBodyIndex.Open(FixtureCatalog.DiffV1.AssemblyPath());
        LibraryBodyIndex spoofed = LibraryBodyIndex.FromEvidence(
            [],
            [],
            moduleIdentity: new(
                LibraryBodyIndex.Open(path).ModuleIdentity.AssemblyIdentity,
                foreign.ModuleIdentity.ModuleVersionId));
        return new ImplementationComparisonInputOccurrence(
            Descriptor(path, LibraryBodyIndex.Open(path).ModuleIdentity.AssemblyIdentity!),
            new NullResolver(),
            spoofed);
    }

    /// <summary>
    /// A real image whose acquisition descriptor names another assembly.
    /// </summary>
    static ImplementationComparisonInputOccurrence SampleWithForeignIdentity()
    {
        string path = FixtureCatalog.ResearchTargetSample.AssemblyPath();
        LibraryBodyIndex index = LibraryBodyIndex.Open(path);
        return new ImplementationComparisonInputOccurrence(
            Descriptor(
                path,
                new AssemblyReferenceIdentity(
                    "NotTheFixture",
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            new NullResolver(),
            index);
    }

    /// <summary>
    /// A real image whose Analysis body index carries no assembly identity.
    /// </summary>
    static ImplementationComparisonInputOccurrence SampleWithStandaloneModule()
    {
        string path = FixtureCatalog.ResearchTargetSample.AssemblyPath();
        LibraryBodyIndex index = LibraryBodyIndex.Open(path);
        LibraryBodyIndex standalone = LibraryBodyIndex.FromEvidence(
            [],
            [],
            moduleIdentity: new(
                assemblyIdentity: null,
                index.ModuleIdentity.ModuleVersionId));
        return new ImplementationComparisonInputOccurrence(
            Descriptor(path, index.ModuleIdentity.AssemblyIdentity!),
            new NullResolver(),
            standalone);
    }

    /// <summary>
    /// A descriptor whose content cannot be read, so opening it becomes a
    /// bounded Research failure rather than an escaping exception.
    /// </summary>
    static ImplementationComparisonInputOccurrence Unreadable()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixtureCatalog.ResearchTargetSample.AssemblyPath());
        return new ImplementationComparisonInputOccurrence(
            ResolvedAssemblyReference.Create(
                index.ModuleIdentity.AssemblyIdentity!,
                path: null,
                () => throw new IOException(TargetFixture.UnreadableMarker),
                AssemblyResolutionProvenance.Project(
                    "Unreadable",
                    tfm: null,
                    rid: null)),
            new NullResolver(),
            index);
    }

    static ImplementationComparisonInputOccurrence Malformed()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixtureCatalog.ResearchTargetSample.AssemblyPath());
        return new ImplementationComparisonInputOccurrence(
            ResolvedAssemblyReference.Create(
                index.ModuleIdentity.AssemblyIdentity!,
                path: null,
                static () => new MemoryStream([0, 1, 2, 3]),
                AssemblyResolutionProvenance.Project(
                    "Malformed",
                    tfm: null,
                    rid: null)),
            new NullResolver(),
            index);
    }

    static ResolvedAssemblyReference Descriptor(
        string path,
        AssemblyReferenceIdentity identity)
        => ResolvedAssemblyReference.Create(
            identity,
            path,
            () => File.OpenRead(path),
            AssemblyResolutionProvenance.Project(
                Path.GetFileNameWithoutExtension(path),
                tfm: null,
                rid: null));

    sealed class NullResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => null;
    }

    /// <summary>
    /// A purpose-built admitted population plus the Research-owned role
    /// assignment and selection helpers every target gate needs.
    /// </summary>
    sealed class TargetFixture
    {
        internal const string UnreadableMarker = "unreadable-fixture-content";

        TargetFixture(
            ResearchAdmittedPopulation population,
            ImmutableArray<ResearchTargetInputRoleAssignment?> roles)
        {
            Population = population;
            Roles = roles;
        }

        public ResearchAdmittedPopulation Population { get; }

        public ImmutableArray<ResearchTargetInputRoleAssignment?> Roles { get; }

        /// <summary>
        /// One admitted population per question tuple. Each tuple supplies one
        /// Before occurrence, one After occurrence, and one optional second
        /// Before occurrence; a null entry leaves that slot empty.
        /// </summary>
        public static TargetFixture Create(
            IReadOnlyList<(
                ImplementationComparisonInputOccurrence? Before,
                ImplementationComparisonInputOccurrence? After,
                ImplementationComparisonInputOccurrence? SecondBefore)> questions,
            Func<ResearchAdmittedInput, bool>? referenceOnly = null)
        {
            List<ResearchComparisonAdmissionQuestion?> requested = [];
            foreach ((ImplementationComparisonInputOccurrence? before,
                ImplementationComparisonInputOccurrence? after,
                ImplementationComparisonInputOccurrence? second) in questions)
            {
                List<ResearchComparisonInputOccurrence?> beforeSide = [];
                if (before is not null)
                    beforeSide.Add(before);
                if (second is not null)
                    beforeSide.Add(second);
                List<ResearchComparisonInputOccurrence?> afterSide = [];
                if (after is not null)
                    afterSide.Add(after);
                requested.Add(
                    new ResearchComparisonAdmissionQuestion(beforeSide, afterSide));
            }

            return From(
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.ImplementationComparison,
                    requested),
                referenceOnly);
        }

        /// <summary>
        /// One admitted population whose inputs are reference-only descriptors
        /// with caller-chosen assembly identities, for inert domain planning.
        /// </summary>
        public static TargetFixture Reference(
            IReadOnlyList<AssemblyReferenceIdentity> before,
            IReadOnlyList<AssemblyReferenceIdentity> after)
            => From(
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.ImplementationComparison,
                    [
                        new ResearchComparisonAdmissionQuestion(
                            [.. before.Select(Synthetic)],
                            [.. after.Select(Synthetic)]),
                    ]),
                static _ => true);

        static TargetFixture From(
            ResearchComparisonAdmissionRequest request,
            Func<ResearchAdmittedInput, bool>? referenceOnly)
        {
            ResearchAdmittedPopulation population =
                Assert.IsType<ResearchAdmissionOutcome.Admitted>(
                    ResearchComparisonAdmission.Admit(request)).Population;
            return new TargetFixture(
                population,
                [
                    .. population.Inputs.Select(
                        input => new ResearchTargetInputRoleAssignment(
                            input,
                            referenceOnly?.Invoke(input) == true
                                ? ResearchTargetInputRole.ReferenceOnly
                                : ResearchTargetInputRole.Implementation)),
                ]);
        }

        static ImplementationComparisonInputOccurrence Synthetic(
            AssemblyReferenceIdentity identity)
            => new(
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    static () => throw new InvalidOperationException(
                        "A reference-only input must never be opened."),
                    AssemblyResolutionProvenance.Project(
                        identity.Name,
                        tfm: null,
                        rid: null)),
                new NullResolver(),
                LibraryBodyIndex.FromEvidence(
                    [],
                    [],
                    moduleIdentity: new(identity, Guid.NewGuid())));

        public ResearchCarriedMemberSelection Carried(
            int questionIndex,
            string declaringType,
            string selector)
            => Carried(
                questionIndex,
                declaringType,
                MemberTargetSelector.Parse(selector));

        public ResearchCarriedMemberSelection Carried(
            int questionIndex,
            string declaringType,
            MemberTargetSelector selector)
            => new(
                Population.Questions[questionIndex].Id,
                declaringType,
                selector);

        public ResearchExactAddressMemberSelection Exact(
            int questionIndex,
            ResearchAdmittedInput input,
            string declaringType,
            string selector,
            MetadataMethodAddress address,
            ResearchTargetRelationshipRole role)
            => new(
                Population.Questions[questionIndex].Id,
                input,
                declaringType,
                MemberTargetSelector.Parse(selector),
                address,
                role);

        public ResearchTargetResolution Resolve(
            params ResearchMemberSelectionOccurrence?[] selections)
            => Assert.IsType<ResearchTargetPlanningOutcome.Planned>(
                ResolveRaw(selections, CancellationToken.None)).Resolution;

        public ResearchTargetResolution ResolveDefault(
            string declaringType,
            string selector)
            => Resolve(Carried(0, declaringType, selector));

        public ResearchTargetPlanningOutcome ResolveRaw(
            IEnumerable<ResearchMemberSelectionOccurrence?> selections,
            CancellationToken cancellationToken)
            => ResearchTargetResolver.Resolve(
                new ResearchTargetPlanningRequest(Population, Roles, selections),
                cancellationToken);

        public ResearchTargetPlanningRejection Reject(
            params ResearchMemberSelectionOccurrence?[] selections)
            => Assert.IsType<ResearchTargetPlanningOutcome.Rejected>(
                ResolveRaw(selections, CancellationToken.None)).Rejection;

        public LibraryBodyModuleIdentity ModuleIdentity(int inputIndex)
            => ((ImplementationComparisonInputOccurrence)
                Population.Inputs[inputIndex].Occurrence)
                .BodyIndex.ModuleIdentity;

        public ApiSurface Surface(FixtureDefinition fixture)
        {
            using var reader = new System.Reflection.PortableExecutable.PEReader(
                File.OpenRead(fixture.AssemblyPath()));
            return ApiSurfaceExtractor.Extract(
                reader,
                includeAll: true,
                typesOnly: false,
                includeCompilerGenerated: true);
        }

        public string Fingerprint(string declaringType, string memberName)
            => Candidates(declaringType, memberName).Single().Anchor.Fingerprint;

        /// <summary>
        /// A one-character digest prefix shared by at least two candidates.
        /// The fixture declares more than sixteen overloads of one name, so
        /// pigeonhole over the hex alphabet guarantees one exists.
        /// </summary>
        public string AmbiguousDigestPrefix(
            string declaringType,
            string memberName)
        {
            IReadOnlyList<MemberTargetCandidate> candidates =
                Candidates(declaringType, memberName);
            Assert.True(candidates.Count > 16);
            return candidates
                .GroupBy(candidate => candidate.Anchor.Fingerprint[..1])
                .First(group => group.Count() > 1)
                .Key;
        }

        IReadOnlyList<MemberTargetCandidate> Candidates(
            string declaringType,
            string memberName)
        {
            ApiSurface surface = Surface(FixtureCatalog.ResearchTargetSample);
            ApiType type = surface.Types.Single(
                candidate =>
                    candidate.DefinitionName?.ToMetadataFullName()
                        == declaringType);
            return MemberTargetResolver.GetCandidates(
                type,
                new MemberTargetSelector(memberName, memberName));
        }
    }
}
