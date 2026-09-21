using System.Text;

using CSharpText;
using DotnetInspector.DocumentationHouse.Source;
using DotnetInspector.Libraries;
using DotnetInspector.SourceHouse;
using DotnetInspector.SourceHouse.BuildAttestation;

namespace DotnetInspector.DocumentationHouse.Tests;

public sealed partial class CompiledXmlDocumentationHouseTests
{
    [Fact]
    public async Task
        RealCombinedDemand_RetainsBothAttemptsConflictAndSourceHouseSettlement()
    {
        SourceHouseBuildAttestation attestation =
            s_authoredBuildAttestation.Value;
        var capability = new CountingAttestationCapability(attestation);
        string identity;
        await using (LibraryFixture probe =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray()))
        {
            identity = AuthoredScenario
                .Create(probe, capability)
                .Binding
                .Subject
                .CompiledXmlIdentity
                .Value;
        }

        byte[] xml = Xml(identity, "compiled-channel-summary");
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray(),
                xml);
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);
        DocumentationHouseRequest request = AuthoredRequest(
            scenario,
            DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation,
            [Candidate(library, scenario.Binding.Subject, xmlIndex: 0)],
            authoredOperation);
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    request,
                    operation,
                    TestContext.Current.CancellationToken));

        DocumentationCompiledXmlAttempt.Available compiled =
            Assert.IsType<DocumentationCompiledXmlAttempt.Available>(
                completed.CompiledXmlAttempt);
        DocumentationAuthoredSourceAttempt.Available authored =
            Assert.IsType<DocumentationAuthoredSourceAttempt.Available>(
                completed.AuthoredSourceAttempt);
        Assert.Equal(
            "compiled-channel-summary",
            compiled.Documentation.Summary);
        Assert.Contains(
            "Locates the declaration",
            ((CSharpAuthoredDocumentationOutcome.Available)
                authored.Contribution.Documentation)
                .Documentation
                .Summary,
            StringComparison.Ordinal);
        Assert.Equal(
            DocumentationFieldEvidenceKind.Conflict,
            completed.Fields.Summary.Kind);
        Assert.Collection(
            completed.Fields.Summary.Contributions,
            contribution =>
            {
                Assert.Equal(
                    DocumentationChannel.CompiledXml,
                    contribution.Channel);
                Assert.Equal(
                    "compiled-channel-summary",
                    contribution.Value);
            },
            contribution =>
            {
                Assert.Equal(
                    DocumentationChannel.AuthoredSource,
                    contribution.Channel);
                Assert.Contains(
                    "Locates the declaration",
                    contribution.Value,
                    StringComparison.Ordinal);
            });
        Assert.Equal(1, capability.SourceReads);
        Assert.Equal(1, capability.AttestationReads);
        Assert.True(completed.Work.ParsedCompiledXml);
        Assert.NotNull(completed.Work.AuthoredSourceWork);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.SourceHouse,
            completed.LeaseSettlement.Consumer);
        Assert.Same(
            authored.OperationOutcome!.LeaseSettlement,
            completed.LeaseSettlement.AuthoredSourceSettlement);
        Assert.Same(
            completed.CompiledXmlAttempt,
            completed.Receipt.CompiledXmlAttempt);
        Assert.Same(
            completed.AuthoredSourceAttempt,
            completed.Receipt.AuthoredSourceAttempt);
        Assert.Same(completed.Fields, completed.Receipt.Fields);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        AuthoredDemandWithoutOperation_IsUnavailableAndSettlesInHouse()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        var plan = new DocumentationHouseOperationPlan(
            DocumentationHouseOperationPlanIdentity.Create(
                "authored-unavailable-plan"),
            DocumentationHousePolicyGeneration.Create("policy-1"),
            HouseLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            compiledXmlContributions: []);
        var request = new DocumentationHouseRequest(
            DocumentationHouseRequestIdentity.Create(
                "authored-unavailable-request"),
            subject,
            DocumentationDemand.AuthoredSourceDocumentation,
            plan);
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    request,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Null(completed.CompiledXmlAttempt);
        DocumentationAuthoredSourceAttempt.Unavailable unavailable =
            Assert.IsType<
                DocumentationAuthoredSourceAttempt.Unavailable>(
                    completed.AuthoredSourceAttempt);
        Assert.Equal(
            DocumentationAuthoredSourceUnavailableKind
                .OperationUnavailable,
            unavailable.UnavailableKind);
        Assert.Null(unavailable.OperationOutcome);
        Assert.Equal(
            DocumentationFieldEvidenceKind.Absent,
            completed.Fields.Summary.Kind);
        Assert.Equal(
            [DocumentationChannel.AuthoredSource],
            completed.Fields.Summary.RequestedChannels);
        Assert.Empty(completed.Fields.Summary.Contributions);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.DocumentationHouse,
            completed.LeaseSettlement.Consumer);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        ForeignAuthoredBinding_RejectsBeforeCompiledOrAuthoredWork()
    {
        byte[] xml = Xml(DeserializeIdentity, "compiled");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        DocumentationHouseRequestIdentity requestIdentity =
            DocumentationHouseRequestIdentity.Create("combined-request");
        DocumentationHouseOperationPlanIdentity planIdentity =
            DocumentationHouseOperationPlanIdentity.Create(
                "combined-plan");
        DocumentationHousePolicyGeneration policy =
            DocumentationHousePolicyGeneration.Create("policy-1");
        var binding = new DocumentationAuthoredSourceOperationBinding(
            DocumentationHouseRequestIdentity.Create("foreign-request"),
            planIdentity,
            policy,
            subject,
            library.Reference.ImplementationAssembly!);
        var operation = new NeverInvokedOperation();
        var plan = new DocumentationHouseOperationPlan(
            planIdentity,
            policy,
            HouseLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            [Candidate(library, subject, xmlIndex: 0)],
            new(
                binding,
                AuthoredLimits(),
                operation));
        var request = new DocumentationHouseRequest(
            requestIdentity,
            subject,
            DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation,
            plan);
        LibraryOperationLease lease = library.IssueOperation();

        DocumentationHouseOutcome.Rejected rejected =
            Assert.IsType<DocumentationHouseOutcome.Rejected>(
                await DocumentationHouse.ExecuteAsync(
                    request,
                    lease,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationHouseRejectionKind
                .AuthoredSourceBindingMismatch,
            rejected.Rejection.Kind);
        Assert.Equal(0, operation.InvocationCount);
        Assert.Equal(0, rejected.Work.CompiledXmlBytesObserved);
        Assert.False(rejected.Work.ParsedCompiledXml);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.DocumentationHouse,
            rejected.LeaseSettlement.Consumer);
        AssertOperationSettled(
            lease,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        FieldSettlement_PreservesSelectedCorroboratedConflictAndAbsent()
    {
        DocumentationHouseOutcome.Completed selected =
            await ExecuteScriptedCombinedAsync(
                compiledSummary: null,
                authoredSummary: "authored");
        AssertField(
            selected.Fields.Summary,
            DocumentationFieldEvidenceKind.Selected,
            (DocumentationChannel.AuthoredSource, "authored"));

        DocumentationHouseOutcome.Completed corroborated =
            await ExecuteScriptedCombinedAsync(
                compiledSummary: "shared",
                authoredSummary: "shared");
        AssertField(
            corroborated.Fields.Summary,
            DocumentationFieldEvidenceKind.Corroborated,
            (DocumentationChannel.CompiledXml, "shared"),
            (DocumentationChannel.AuthoredSource, "shared"));

        DocumentationHouseOutcome.Completed conflict =
            await ExecuteScriptedCombinedAsync(
                compiledSummary: "compiled",
                authoredSummary: "authored");
        AssertField(
            conflict.Fields.Summary,
            DocumentationFieldEvidenceKind.Conflict,
            (DocumentationChannel.CompiledXml, "compiled"),
            (DocumentationChannel.AuthoredSource, "authored"));

        DocumentationHouseOutcome.Completed absent =
            await ExecuteScriptedCombinedAsync(
                compiledSummary: null,
                authoredSummary: null);
        AssertField(
            absent.Fields.Summary,
            DocumentationFieldEvidenceKind.Absent);
    }

    [Fact]
    public async Task
        TerminalCompiledAttempts_DoNotSuppressRequestedAuthoredAttempt()
    {
        await using (LibraryFixture library =
            await LibraryFixture.CreateAsync())
        {
            DocumentationSubjectReference subject = Subject(library);
            await AssertContinuesAsync(
                library,
                subject,
                [],
                static attempt =>
                    Assert.IsType<
                        DocumentationCompiledXmlAttempt.Unavailable>(
                            attempt));
            await AssertContinuesAsync(
                library,
                subject,
                [
                    CompiledXmlContribution.Partial(
                        subject,
                        library.Reference,
                        library.Reference.ApiAssembly,
                        Source("partial")),
                ],
                static attempt =>
                    Assert.IsType<
                        DocumentationCompiledXmlAttempt.Incomplete>(
                            attempt));
        }

        byte[] first = Xml(DeserializeIdentity, "first");
        byte[] second = Xml(DeserializeIdentity, "second");
        await using (LibraryFixture library =
            await LibraryFixture.CreateAsync(first, second))
        {
            DocumentationSubjectReference subject = Subject(library);
            await AssertContinuesAsync(
                library,
                subject,
                [
                    Candidate(library, subject, xmlIndex: 0),
                    Candidate(library, subject, xmlIndex: 1),
                ],
                static attempt =>
                    Assert.IsType<
                        DocumentationCompiledXmlAttempt.Ambiguous>(
                            attempt));
        }

        await using (LibraryFixture library =
            await LibraryFixture.CreateAsync(
                Encoding.UTF8.GetBytes("<doc>")))
        {
            DocumentationSubjectReference subject = Subject(library);
            await AssertContinuesAsync(
                library,
                subject,
                [Candidate(library, subject, xmlIndex: 0)],
                static attempt =>
                    Assert.IsType<
                        DocumentationCompiledXmlAttempt.Failed>(
                            attempt));
        }

        await using (LibraryFixture selected =
            await LibraryFixture.CreateAsync())
        await using (LibraryFixture foreign =
            await LibraryFixture.CreateAsync())
        {
            DocumentationSubjectReference selectedSubject =
                Subject(selected);
            DocumentationSubjectReference foreignSubject =
                Subject(foreign);
            await AssertContinuesAsync(
                selected,
                selectedSubject,
                [
                    CompiledXmlContribution.Unavailable(
                        foreignSubject,
                        foreign.Reference,
                        foreign.Reference.ApiAssembly,
                        Source("foreign")),
                ],
                static attempt =>
                    Assert.IsType<
                        DocumentationCompiledXmlAttempt.Rejected>(
                            attempt));
        }
    }

    [Fact]
    public async Task AuthoredOperationNonSuccesses_MapWithoutWeakening()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);

        await AssertAttemptAsync(
            invocation =>
                new DocumentationAuthoredSourceOperationOutcome.Unavailable(
                    invocation,
                    DocumentationAuthoredUnavailableKind
                        .PhysicalDeclarationConflict,
                    EmptyAuthoredWork(),
                    OperationSettlement()),
            DocumentationAuthoredSourceAttemptKind.Ambiguous);
        await AssertAttemptAsync(
            invocation =>
                new DocumentationAuthoredSourceOperationOutcome.Unavailable(
                    invocation,
                    DocumentationAuthoredUnavailableKind
                        .DeclarationUncertain,
                    EmptyAuthoredWork(),
                    OperationSettlement()),
            DocumentationAuthoredSourceAttemptKind.Incomplete);
        await AssertAttemptAsync(
            invocation =>
                new DocumentationAuthoredSourceOperationOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.SourceRejected,
                    EmptyAuthoredWork(),
                    OperationSettlement()),
            DocumentationAuthoredSourceAttemptKind.Rejected);
        await AssertAttemptAsync(
            invocation =>
                new DocumentationAuthoredSourceOperationOutcome.Failed(
                    invocation,
                    DocumentationAuthoredFailureKind.SourceFailed,
                    EmptyAuthoredWork(),
                    OperationSettlement()),
            DocumentationAuthoredSourceAttemptKind.Failed);
        await AssertAttemptAsync(
            invocation =>
                new DocumentationAuthoredSourceOperationOutcome.Incomplete(
                    invocation,
                    DocumentationAuthoredIncompleteBoundary.SourceHouse,
                    EmptyAuthoredWork(),
                    OperationSettlement()),
            DocumentationAuthoredSourceAttemptKind.Incomplete);

        async Task AssertAttemptAsync(
            Func<
                DocumentationAuthoredSourceOperationInvocation,
                DocumentationAuthoredSourceOperationOutcome> outcome,
            DocumentationAuthoredSourceAttemptKind expected)
        {
            ScriptedRequest scripted = CreateScriptedRequest(
                library,
                subject,
                DocumentationDemand.AuthoredSourceDocumentation,
                compiledXmlContributions: [],
                outcome);
            DocumentationHouseOutcome.Completed completed =
                Assert.IsType<DocumentationHouseOutcome.Completed>(
                    await DocumentationHouse.ExecuteAsync(
                        scripted.Request,
                        library.IssueOperation(),
                        TestContext.Current.CancellationToken));

            Assert.Equal(
                expected,
                completed.AuthoredSourceAttempt!.Kind);
            Assert.Equal(1, scripted.Operation.InvocationCount);
            Assert.Equal(
                DocumentationLibraryLeaseConsumer.Operation,
                completed.LeaseSettlement.Consumer);
        }
    }

    [Fact]
    public async Task
        ForeignProducedBinding_BecomesChannelRejectedAfterLeaseTransfer()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        ScriptedRequest scripted = CreateScriptedRequest(
            library,
            subject,
            DocumentationDemand.AuthoredSourceDocumentation,
            compiledXmlContributions: [],
            invocation =>
            {
                var produced =
                    (DocumentationAuthoredSourceOperationOutcome.Produced)
                        Produced(invocation, "authored");
                var foreignBinding =
                    new DocumentationAuthoredSourceOperationBinding(
                        DocumentationHouseRequestIdentity.Create(
                            "foreign-produced-request"),
                        invocation.Binding.OperationPlan,
                        invocation.Binding.PolicyGeneration,
                        invocation.Binding.Subject,
                        invocation.Binding.ImplementationContent);
                return new DocumentationAuthoredSourceOperationOutcome
                    .Produced(
                        invocation,
                        new(
                            foreignBinding,
                            produced.Contribution.Evidence,
                            produced.Contribution.Documentation),
                        produced.Work,
                        produced.LeaseSettlement);
            });

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    scripted.Request,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        DocumentationAuthoredSourceAttempt.Rejected rejected =
            Assert.IsType<DocumentationAuthoredSourceAttempt.Rejected>(
                completed.AuthoredSourceAttempt);
        Assert.Equal(
            DocumentationAuthoredSourceAttemptRejectionKind
                .OperationEvidenceMismatch,
            rejected.Rejection);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.Operation,
            completed.LeaseSettlement.Consumer);
    }

    [Fact]
    public async Task RepeatedHouseExecution_PreservesAlreadyInvokedRejection()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        ScriptedRequest scripted = CreateScriptedRequest(
            library,
            subject,
            DocumentationDemand.AuthoredSourceDocumentation,
            compiledXmlContributions: [],
            invocation => Produced(invocation, "authored"));

        DocumentationHouseOutcome.Completed first =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    scripted.Request,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        DocumentationHouseOutcome.Completed second =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    scripted.Request,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.IsType<DocumentationAuthoredSourceAttempt.Available>(
            first.AuthoredSourceAttempt);
        DocumentationAuthoredSourceAttempt.Rejected rejected =
            Assert.IsType<DocumentationAuthoredSourceAttempt.Rejected>(
                second.AuthoredSourceAttempt);
        Assert.Equal(
            DocumentationAuthoredSourceAttemptRejectionKind
                .OperationRejected,
            rejected.Rejection);
        var operationOutcome =
            Assert.IsType<
                DocumentationAuthoredSourceOperationOutcome.Rejected>(
                    rejected.OperationOutcome);
        Assert.Equal(
            DocumentationAuthoredRejectionKind.AlreadyInvoked,
            operationOutcome.Rejection);
        Assert.Equal(2, scripted.Operation.InvocationCount);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.Operation,
            second.LeaseSettlement.Consumer);
    }

    [Fact]
    public async Task
        CompiledOnlyRequestCannotRetainAuthoredOperationCapability()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        DocumentationHouseRequestIdentity requestIdentity =
            DocumentationHouseRequestIdentity.Create("compiled-request");
        DocumentationHouseOperationPlanIdentity planIdentity =
            DocumentationHouseOperationPlanIdentity.Create("compiled-plan");
        DocumentationHousePolicyGeneration policy =
            DocumentationHousePolicyGeneration.Create("policy-1");
        var binding = new DocumentationAuthoredSourceOperationBinding(
            requestIdentity,
            planIdentity,
            policy,
            subject,
            library.Reference.ImplementationAssembly!);
        var plan = new DocumentationHouseOperationPlan(
            planIdentity,
            policy,
            HouseLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            compiledXmlContributions: [],
            new(
                binding,
                AuthoredLimits(),
                new NeverInvokedOperation()));

        Assert.Throws<ArgumentException>(
            () => new DocumentationHouseRequest(
                requestIdentity,
                subject,
                DocumentationDemand.CompiledXml,
                plan));
    }

    [Fact]
    public async Task ExecuteManyRejectsAuthoredDemandBeforeLeaseTransfer()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        var request = new DocumentationHouseRequest(
            DocumentationHouseRequestIdentity.Create("authored-batch"),
            subject,
            DocumentationDemand.AuthoredSourceDocumentation,
            new(
                DocumentationHouseOperationPlanIdentity.Create(
                    "authored-batch"),
                DocumentationHousePolicyGeneration.Create("policy-1"),
                HouseLimits(),
                DateTimeOffset.UtcNow.AddMinutes(1),
                compiledXmlContributions: []));
        LibraryOperationLease operation = library.IssueOperation();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await DocumentationHouse.ExecuteManyAsync(
                [request],
                operation,
                TestContext.Current.CancellationToken));

        _ = operation.Snapshot(
            library.Reference.ApiAssembly,
            static (_, _) => true,
            TestContext.Current.CancellationToken);
        operation.Dispose();
    }

    private static DocumentationHouseRequest AuthoredRequest(
        AuthoredScenario scenario,
        DocumentationDemand demand,
        IReadOnlyList<CompiledXmlContribution> contributions,
        IDocumentationAuthoredSourceOperation operation)
    {
        var plan = new DocumentationHouseOperationPlan(
            scenario.Binding.OperationPlan,
            scenario.Binding.PolicyGeneration,
            HouseLimits(),
            scenario.Invocation.Deadline,
            contributions,
            new(
                scenario.Binding,
                scenario.Invocation.RemainingLimits,
                operation));
        return new(
            scenario.Binding.Request,
            scenario.Binding.Subject,
            demand,
            plan);
    }

    private static async Task<DocumentationHouseOutcome.Completed>
        ExecuteScriptedCombinedAsync(
            string? compiledSummary,
            string? authoredSummary)
    {
        byte[][] xml = compiledSummary is null
            ? []
            : [Xml(DeserializeIdentity, compiledSummary)];
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        IReadOnlyList<CompiledXmlContribution> contributions =
            compiledSummary is null
                ?
                [
                    CompiledXmlContribution.Absent(
                        subject,
                        library.Reference,
                        library.Reference.ApiAssembly,
                        Source("compiled-absent")),
                ]
                : [Candidate(library, subject, xmlIndex: 0)];
        ScriptedRequest scripted = CreateScriptedRequest(
            library,
            subject,
            DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation,
            contributions,
            invocation => Produced(invocation, authoredSummary));
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    scripted.Request,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, scripted.Operation.InvocationCount);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
        return completed;
    }

    private static async Task AssertContinuesAsync(
        LibraryFixture library,
        DocumentationSubjectReference subject,
        IReadOnlyList<CompiledXmlContribution> contributions,
        Action<DocumentationCompiledXmlAttempt?> assertCompiled)
    {
        ScriptedRequest scripted = CreateScriptedRequest(
            library,
            subject,
            DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation,
            contributions,
            invocation => Produced(invocation, summary: null));
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    scripted.Request,
                    operation,
                    TestContext.Current.CancellationToken));

        assertCompiled(completed.CompiledXmlAttempt);
        Assert.IsType<DocumentationAuthoredSourceAttempt.Absent>(
            completed.AuthoredSourceAttempt);
        Assert.Equal(1, scripted.Operation.InvocationCount);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.Operation,
            completed.LeaseSettlement.Consumer);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    private static ScriptedRequest CreateScriptedRequest(
        LibraryFixture library,
        DocumentationSubjectReference subject,
        DocumentationDemand demand,
        IReadOnlyList<CompiledXmlContribution> compiledXmlContributions,
        Func<
            DocumentationAuthoredSourceOperationInvocation,
            DocumentationAuthoredSourceOperationOutcome> outcome)
    {
        DocumentationHouseRequestIdentity requestIdentity =
            DocumentationHouseRequestIdentity.Create(
                $"scripted-request-{Guid.NewGuid():N}");
        DocumentationHouseOperationPlanIdentity planIdentity =
            DocumentationHouseOperationPlanIdentity.Create(
                $"scripted-plan-{Guid.NewGuid():N}");
        DocumentationHousePolicyGeneration policy =
            DocumentationHousePolicyGeneration.Create("policy-1");
        var binding = new DocumentationAuthoredSourceOperationBinding(
            requestIdentity,
            planIdentity,
            policy,
            subject,
            library.Reference.ImplementationAssembly!);
        var operation = new ScriptedOperation(outcome);
        var plan = new DocumentationHouseOperationPlan(
            planIdentity,
            policy,
            HouseLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            compiledXmlContributions,
            new(binding, AuthoredLimits(), operation));
        return new(
            new(
                requestIdentity,
                subject,
                demand,
                plan),
            operation);
    }

    private static DocumentationAuthoredSourceOperationOutcome Produced(
        DocumentationAuthoredSourceOperationInvocation invocation,
        string? summary)
    {
        const string declaration = "public static void M() { }";
        string source = summary is null
            ? $"class C {{ {declaration} }}"
            : $"class C {{\n/// <summary>{summary}</summary>\n{declaration}\n}}";
        CSharpAuthoredDocumentationOutcome documentation =
            CSharpAuthoredDocumentation.Read(
                new(
                    source,
                    new(
                        source.IndexOf(
                            declaration,
                            StringComparison.Ordinal),
                        declaration.Length)));
        if (summary is null)
        {
            Assert.IsType<
                CSharpAuthoredDocumentationOutcome.Absent>(
                    documentation);
        }
        else
        {
            Assert.IsType<
                CSharpAuthoredDocumentationOutcome.Available>(
                    documentation);
        }

        var evidence = new DocumentationAuthoredSourceOperationEvidence(
            DocumentationSourceReference.Create(
                DocumentationSourceKind.SourceHouse,
                "scripted-source"),
            new ScriptedSourceEvidence(),
            new ScriptedPhysicalDeclarationEvidence());
        return new DocumentationAuthoredSourceOperationOutcome.Produced(
            invocation,
            new(
                invocation.Binding,
                evidence,
                documentation),
            new(
                SourceBytesObserved: source.Length,
                SourceTextCharactersObserved: source.Length,
                SourceDocumentsObserved: 1,
                AttestationContributionsObserved: 1,
                documentation.Work),
            OperationSettlement());
    }

    private static void AssertField(
        DocumentationFieldEvidence<string> field,
        DocumentationFieldEvidenceKind kind,
        params (DocumentationChannel Channel, string Value)[] expected)
    {
        Assert.Equal(kind, field.Kind);
        Assert.Equal(
            [
                DocumentationChannel.CompiledXml,
                DocumentationChannel.AuthoredSource,
            ],
            field.RequestedChannels);
        Assert.Equal(expected.Length, field.Contributions.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(
                expected[index].Channel,
                field.Contributions[index].Channel);
            Assert.Equal(
                expected[index].Value,
                field.Contributions[index].Value);
        }
    }

    private static DocumentationHouseLimits HouseLimits() =>
        new(
            maximumCompiledXmlContributions: 8,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);

    private static DocumentationAuthoredSourceOperationLimits
        AuthoredLimits() =>
        new(
            maximumSourceDocuments: 1_000,
            maximumSourceBytes: 1024 * 1024,
            CSharpAuthoredDocumentationLimits.Default);

    private static DocumentationAuthoredSourceOperationWorkCharge
        EmptyAuthoredWork() =>
        new(
            SourceBytesObserved: 0,
            SourceTextCharactersObserved: 0,
            SourceDocumentsObserved: 0,
            AttestationContributionsObserved: 0,
            DocumentationWork: null);

    private static DocumentationAuthoredLeaseSettlement
        OperationSettlement() =>
        new(DocumentationAuthoredLeaseConsumer.Operation);

    private sealed class NeverInvokedOperation
        : IDocumentationAuthoredSourceOperation
    {
        public int InvocationCount { get; private set; }

        public ValueTask<DocumentationAuthoredSourceOperationOutcome>
            InvokeAsync(
                DocumentationAuthoredSourceOperationInvocation invocation,
                LibraryOperationLease operationLease,
                CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw new InvalidOperationException(
                "The invalid binding must reject before operation invocation.");
        }
    }

    private sealed class ScriptedOperation(
        Func<
            DocumentationAuthoredSourceOperationInvocation,
            DocumentationAuthoredSourceOperationOutcome> outcome)
        : IDocumentationAuthoredSourceOperation
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public ValueTask<DocumentationAuthoredSourceOperationOutcome>
            InvokeAsync(
                DocumentationAuthoredSourceOperationInvocation invocation,
                LibraryOperationLease operationLease,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int invocationCount =
                Interlocked.Increment(ref _invocationCount);
            operationLease.Dispose();
            if (invocationCount > 1)
            {
                return ValueTask.FromResult<
                    DocumentationAuthoredSourceOperationOutcome>(
                        new DocumentationAuthoredSourceOperationOutcome
                            .Rejected(
                                invocation,
                                DocumentationAuthoredRejectionKind
                                    .AlreadyInvoked,
                                EmptyAuthoredWork(),
                                OperationSettlement()));
            }
            return ValueTask.FromResult(outcome(invocation));
        }
    }

    private sealed class ScriptedSourceEvidence
        : DocumentationAuthoredSourceEvidenceReference;

    private sealed class ScriptedPhysicalDeclarationEvidence
        : DocumentationPhysicalDeclarationEvidenceReference;

    private sealed record ScriptedRequest(
        DocumentationHouseRequest Request,
        ScriptedOperation Operation);
}
