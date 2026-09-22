using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Packages;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

using DocumentationHouseService =
    DotnetInspector.DocumentationHouse.DocumentationHouse;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    private const string PackageDocumentationDeserializeIdentity =
        "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)";

    private static readonly ApiSurfaceExtractionBounds
        s_packageDocumentationApiSurfaceBounds =
            new(
                maxTypes: 5_000,
                maxMembers: 100_000,
                maxInspectionFailures: 1_000,
                maxTypeForwarders: 10_000,
                maxMetadataRows: 1_000_000,
                maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public async Task
        PackageDocumentationAdapterSettlesRealSystemTextJsonMember()
    {
        InMemoryPackageContent content = CreatePackageContent(
            (
                MaterializedApiPath,
                ReadRealAsset("System.Text.Json.dll")),
            (
                MaterializedImplementationPath,
                ReadRealAsset("System.Text.Json.dll")),
            (
                MaterializedDocumentationPath,
                ReadRealAsset("System.Text.Json.xml")));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        PackageHouseLibraryMaterializationOutcome.Completed materialized =
            await MaterializeLibraryAsync(
                settlement,
                handoff);

        try
        {
            DocumentationSubjectReference subject =
                CreatePackageDocumentationSubject(materialized);
            CompiledXmlContribution contribution =
                PackageDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        materialized.Receipt,
                        subject);

            Assert.Equal(
                CompiledXmlContributionKind.Candidate,
                contribution.Kind);
            Assert.Equal(
                DocumentationSourceKind.Package,
                contribution.Source.Kind);
            Assert.Same(
                materialized.Receipt.Library,
                contribution.Library);
            Assert.Same(
                materialized.Receipt.Library.ApiAssembly,
                contribution.ApiContent);
            Assert.Same(
                materialized.Receipt.Library.ApiAssembly,
                contribution.CompiledXmlContent!
                    .AssociatedAssembly);

            DocumentationHouseOutcome.Completed completed =
                Assert.IsType<DocumentationHouseOutcome.Completed>(
                    await DocumentationHouseService.ExecuteAsync(
                        CreatePackageDocumentationRequest(
                            subject,
                            contribution),
                        IssuePackageDocumentationOperation(
                            materialized),
                        TestContext.Current.CancellationToken));
            DocumentationCompiledXmlAttempt.Available available =
                Assert.IsType<
                    DocumentationCompiledXmlAttempt.Available>(
                        completed.CompiledXmlAttempt);
            Assert.Contains(
                "Converts the JsonDocument",
                available.Documentation.Summary,
                StringComparison.Ordinal);
            Assert.Same(contribution, available.Selected);
        }
        finally
        {
            await materialized.Owner.DisposeAsync();
            await materialized.Artifacts.DisposeAsync();
        }

        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MissingPackageDocumentationIsAuthoritativeAbsence()
    {
        InMemoryPackageContent content = CreatePackageContent(
            (
                MaterializedApiPath,
                ReadRealAsset("System.Text.Json.dll")),
            (
                MaterializedImplementationPath,
                ReadRealAsset("System.Text.Json.dll")));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        PackageHouseLibraryMaterializationOutcome.Completed materialized =
            await MaterializeLibraryAsync(
                settlement,
                handoff);

        try
        {
            DocumentationSubjectReference subject =
                CreatePackageDocumentationSubject(materialized);
            CompiledXmlContribution contribution =
                PackageDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        materialized.Receipt,
                        subject);

            Assert.Equal(
                CompiledXmlContributionKind.Absent,
                contribution.Kind);
            Assert.Null(contribution.CompiledXmlContent);

            DocumentationHouseOutcome.Completed completed =
                Assert.IsType<DocumentationHouseOutcome.Completed>(
                    await DocumentationHouseService.ExecuteAsync(
                        CreatePackageDocumentationRequest(
                            subject,
                            contribution),
                        IssuePackageDocumentationOperation(
                            materialized),
                        TestContext.Current.CancellationToken));
            Assert.IsType<DocumentationCompiledXmlAttempt.Absent>(
                completed.CompiledXmlAttempt);
            Assert.False(completed.Work.ParsedCompiledXml);
            Assert.Equal(
                0,
                completed.Work.CompiledXmlBytesObserved);
        }
        finally
        {
            await materialized.Owner.DisposeAsync();
            await materialized.Artifacts.DisposeAsync();
        }

        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        CompiledDocumentationDoesNotAcquireImplementationPortablePdb()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        byte[] documentation = ReadRealAsset("System.Text.Json.xml");
        long maxContentBytes = Math.Max(
            assembly.LongLength,
            documentation.LongLength);
        byte[] portablePdb =
            GC.AllocateUninitializedArray<byte>(
                checked((int)maxContentBytes + 1));
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedApiPath, assembly),
            (MaterializedImplementationPath, assembly),
            (MaterializedDocumentationPath, documentation),
            (MaterializedPortablePdbPath, portablePdb));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        var limits = new PackageCompiledDocumentationQueryLimits
        {
            Materialization =
                new PackageHouseLibraryMaterializationLimits
                {
                    MaxContentBytes = maxContentBytes,
                    MaxRetainedBytes =
                        checked(
                            (2 * assembly.LongLength)
                            + documentation.LongLength),
                },
        };

        CompiledDocumentationOutcome.Available available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                await PackageCompiledDocumentationQuery.ExecuteAsync(
                    settlement,
                    handoff,
                    PackageDocumentationDeserializeIdentity,
                    limits,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "Converts the JsonDocument",
            available.Documentation.Summary,
            StringComparison.Ordinal);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        ByteIdenticalForeignPackageContributionCannotSatisfySubject()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        byte[] documentation =
            ReadRealAsset("System.Text.Json.xml");
        InMemoryPackageContent selectedContent = CreatePackageContent(
            (MaterializedApiPath, assembly),
            (MaterializedDocumentationPath, documentation));
        InMemoryPackageContent foreignContent = CreatePackageContent(
            (MaterializedApiPath, assembly),
            (MaterializedDocumentationPath, documentation));
        await using HouseEnvironment selectedEnvironment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        await using HouseEnvironment foreignEnvironment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired selectedSettlement,
            PackageHouseLibraryHandoff.Compile selectedHandoff) =
            await ExecuteMaterializationInputAsync(
                selectedEnvironment,
                selectedContent);
        (PackageHouseSettlement.Acquired foreignSettlement,
            PackageHouseLibraryHandoff.Compile foreignHandoff) =
            await ExecuteMaterializationInputAsync(
                foreignEnvironment,
                foreignContent);
        PackageHouseLibraryMaterializationOutcome.Completed selected =
            await MaterializeLibraryAsync(
                selectedSettlement,
                selectedHandoff);
        PackageHouseLibraryMaterializationOutcome.Completed foreign =
            await MaterializeLibraryAsync(
                foreignSettlement,
                foreignHandoff);

        try
        {
            DocumentationSubjectReference subject =
                CreatePackageDocumentationSubject(selected);
            CompiledXmlContribution contribution =
                PackageDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        foreign.Receipt,
                        subject);

            DocumentationHouseOutcome.Completed completed =
                Assert.IsType<DocumentationHouseOutcome.Completed>(
                    await DocumentationHouseService.ExecuteAsync(
                        CreatePackageDocumentationRequest(
                            subject,
                            contribution),
                        IssuePackageDocumentationOperation(selected),
                        TestContext.Current.CancellationToken));
            DocumentationCompiledXmlAttempt.Rejected rejected =
                Assert.IsType<
                    DocumentationCompiledXmlAttempt.Rejected>(
                        completed.CompiledXmlAttempt);
            Assert.Equal(
                DocumentationCompiledXmlRejectionKind
                    .LibraryMismatch,
                Assert.Single(rejected.Rejections).Kind);
            Assert.False(completed.Work.ParsedCompiledXml);
        }
        finally
        {
            await selected.Owner.DisposeAsync();
            await selected.Artifacts.DisposeAsync();
            await foreign.Owner.DisposeAsync();
            await foreign.Artifacts.DisposeAsync();
        }

        await selectedEnvironment.AssertRootSettledAsync();
        await foreignEnvironment.AssertRootSettledAsync();
    }

    private static async ValueTask<
        PackageHouseLibraryMaterializationOutcome.Completed>
        MaterializeLibraryAsync(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =>
        Assert.IsType<
            PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

    private static DocumentationSubjectReference
        CreatePackageDocumentationSubject(
            PackageHouseLibraryMaterializationOutcome.Completed
                materialized)
    {
        LibraryReference library = materialized.Receipt.Library;
        using LibraryOperationLease operation =
            IssuePackageDocumentationOperation(materialized);
        var request = new LibraryApiSurfaceInspectionRequest(
            library,
            ApiSurfaceExtractionScope.Public,
            s_packageDocumentationApiSurfaceBounds);
        LibraryApiSurfaceCorrespondence correspondence =
            Assert.IsType<
                LibraryApiSurfaceInspectionOutcome.Completed>(
                    LibraryApiSurfaceInspection.Execute(
                        request,
                        operation,
                        TestContext.Current.CancellationToken))
                .Correspondence;
        ApiType type = Assert.Single(
            correspondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        ApiMember member = Assert.Single(
            type.Members,
            candidate =>
                ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                    type,
                    candidate,
                    out XmlDocMemberIdentity identity)
                && identity.Value
                    == PackageDocumentationDeserializeIdentity);
        return DocumentationSubjectReference.ForMember(
            correspondence,
            type,
            member);
    }

    private static LibraryOperationLease
        IssuePackageDocumentationOperation(
            PackageHouseLibraryMaterializationOutcome.Completed
                materialized) =>
        Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                materialized.Owner.IssueOperationLease(
                    materialized.Receipt.Library))
            .Lease;

    private static DocumentationHouseRequest
        CreatePackageDocumentationRequest(
            DocumentationSubjectReference subject,
            CompiledXmlContribution contribution)
    {
        var limits = new DocumentationHouseLimits(
            maximumCompiledXmlContributions: 1,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);
        var plan = new DocumentationHouseOperationPlan(
            DocumentationHouseOperationPlanIdentity.Create(
                "package-compiled-plan"),
            DocumentationHousePolicyGeneration.Create(
                "package-policy-1"),
            limits,
            DateTimeOffset.UtcNow.AddMinutes(1),
            [contribution]);
        return new DocumentationHouseRequest(
            DocumentationHouseRequestIdentity.Create(
                "package-compiled-request"),
            subject,
            DocumentationDemand.CompiledXml,
            plan);
    }
}
