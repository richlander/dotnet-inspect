using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Sections.Tests;

public sealed class LibraryAddressInspectionOperationTests
{
    [Fact]
    public async Task ExactIlAddressUsesRealImplementation()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        (int methodToken, int ilOffset) =
            FirstMethodAddress(implementation);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.InstructionContext));

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(envelope.Content);
        var document =
            Assert.IsType<LibraryAddressDocument.IlPoint>(
                completed.Document);
        var resolved =
            Assert.IsType<LibraryIlAddressOutcome.Resolved>(
                document.Result);
        Assert.Equal(methodToken, resolved.MethodToken);
        Assert.Equal(ilOffset, resolved.ILOffset);
        Assert.NotNull(resolved.Projection.MemberContext);
        Assert.NotNull(resolved.Projection.InstructionContext);
        Assert.Empty(envelope.Diagnostics);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
    }

    [Fact]
    public async Task ExactHeapAddressRetainsRootIdentity()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        int address = FirstTypeNameAddress(implementation);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.HeapPoint(
                    MetadataRootKind.Cli,
                    HeapKind.String,
                    address));

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(envelope.Content);
        var document =
            Assert.IsType<LibraryAddressDocument.HeapPoint>(
                completed.Document);
        var resolved =
            Assert.IsType<LibraryHeapAddressOutcome.Resolved>(
                document.Result);
        Assert.Equal(MetadataRootKind.Cli, resolved.RequestedRoot);
        Assert.Equal(MetadataRootKind.Cli, resolved.Root.Kind);
        Assert.Equal(HeapKind.String, resolved.Heap);
        Assert.Equal(address, resolved.Address);
        Assert.IsNotType<MetadataValue.Malformed>(resolved.Value);

        await library.RetireAsync();
        Assert.Equal(address, resolved.Address);
        Assert.NotNull(resolved.Value);
    }

    [Fact]
    public async Task ReadyToRunManifestAbsenceDoesNotFallBack()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.HeapPoint(
                    MetadataRootKind.ReadyToRunManifest,
                    HeapKind.String,
                    0));

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(envelope.Content);
        var document =
            Assert.IsType<LibraryAddressDocument.HeapPoint>(
                completed.Document);
        var unresolved =
            Assert.IsType<LibraryHeapAddressOutcome.Unresolved>(
                document.Result);
        Assert.Equal(
            LibraryHeapAddressFailure.RootUnavailable,
            unresolved.Reason);
        Assert.NotEmpty(envelope.Diagnostics);
    }

    [Fact]
    public async Task MalformedCliRootIsTypedFailure()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity =
            LibraryInspectionTestLibrary.Identity(implementation);
        implementation = [.. implementation];
        using (var reader = new PEReader(
            new MemoryStream(implementation, writable: false)))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                implementation.AsSpan(
                    reader.PEHeaders.MetadataStartOffset,
                    sizeof(uint)),
                0xDEADBEEF);
        }
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                identity);

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.HeapPoint(
                    MetadataRootKind.Cli,
                    HeapKind.String,
                    0));

        Assert.Equal(
            LibraryAddressInspectionFailure.MalformedMetadata,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    envelope.Content)
                .Reason);
    }

    [Fact]
    public async Task OutOfRangeHeapAddressIsMalformedValue()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.HeapPoint(
                    MetadataRootKind.Cli,
                    HeapKind.String,
                    int.MaxValue));

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(envelope.Content);
        var unresolved =
            Assert.IsType<LibraryHeapAddressOutcome.Unresolved>(
                Assert.IsType<LibraryAddressDocument.HeapPoint>(
                        completed.Document)
                    .Result);
        Assert.Equal(
            LibraryHeapAddressFailure.MalformedValue,
            unresolved.Reason);
        Assert.Single(envelope.Diagnostics);
    }

    [Fact]
    public async Task DistinctApiAndImplementationTargetsImplementation()
    {
        byte[] api =
            await LibraryInspectionTestLibrary.PinnedNet11Async(
                "ref",
                "System.Net.Sockets.dll");
        byte[] implementation =
            await LibraryInspectionTestLibrary.PinnedNet11Async(
                "runtime",
                "System.Net.Sockets.dll");
        (int methodToken, int ilOffset) =
            FirstMethodAddress(implementation);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                api,
                LibraryInspectionTestLibrary.Identity(api),
                implementation);

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.InstructionContext));

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(envelope.Content);
        var document =
            Assert.IsType<LibraryAddressDocument.IlPoint>(
                completed.Document);
        var resolved =
            Assert.IsType<LibraryIlAddressOutcome.Resolved>(
                document.Result);
        Assert.Equal(
            "System.Net.Sockets",
            resolved.Projection.MemberContext!.Assembly);
    }

    [Fact]
    public async Task MissingImplementationAndMismatchedLeaseAreRejected()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary apiOnly =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content),
                implementation: null);
        var point = new LibraryAddressIntent.HeapPoint(
            MetadataRootKind.Cli,
            HeapKind.String,
            0);

        InspectionEnvelope<LibraryAddressInspectionOutcome> missing =
            Execute(apiOnly, point);
        Assert.Equal(
            LibraryAddressInspectionRejection
                .MissingImplementationAssembly,
            Assert.IsType<
                    LibraryAddressInspectionOutcome.Rejected>(
                    missing.Content)
                .Reason);

        await using LibraryInspectionTestLibrary first =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        await using LibraryInspectionTestLibrary second =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        InspectionEnvelope<LibraryAddressInspectionOutcome> mismatched =
            LibraryAddressInspectionOperation.Execute(
                new(first.Reference, point),
                second.IssueOperation(),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            LibraryAddressInspectionRejection.LeaseReferenceMismatch,
            Assert.IsType<
                    LibraryAddressInspectionOutcome.Rejected>(
                    mismatched.Content)
                .Reason);

        await first.RetireAsync();
        await second.RetireAsync();
    }

    [Fact]
    public async Task IdentityMismatchIsRejected()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.HeapPoint(
                    MetadataRootKind.Cli,
                    HeapKind.String,
                    0));

        Assert.Equal(
            LibraryAddressInspectionRejection.AssemblyIdentityMismatch,
            Assert.IsType<
                    LibraryAddressInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);
    }

    [Fact]
    public async Task PopulationPreservesMixedRecordOrder()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        (int methodToken, int ilOffset) =
            FirstMethodAddress(implementation);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));
        var intent = new LibraryAddressIntent.Population(
            [
                new LibraryAddressPopulationRecord.Coordinate(
                    2,
                    $"0x{methodToken:X8}+0x{ilOffset:X}",
                    "valid",
                    methodToken,
                    ilOffset),
                new LibraryAddressPopulationRecord.Malformed(
                    3,
                    "broken",
                    "Invalid coordinate."),
                new LibraryAddressPopulationRecord.Coordinate(
                    4,
                    "0x0600FFFF+0x0",
                    "missing",
                    0x0600FFFF,
                    0),
            ],
            ILOffsetProjectionCapabilities.InstructionContext);

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(library, intent);

        var partial = Assert.IsType<
            LibraryAddressInspectionOutcome.Partial>(envelope.Content);
        LibraryAddressPopulationResult result =
            partial.Document.Result;
        Assert.Collection(
            result.Rows,
            row => Assert.IsType<
                LibraryAddressPopulationRow.Resolved>(row),
            row => Assert.IsType<
                LibraryAddressPopulationRow.Malformed>(row),
            row => Assert.IsType<
                LibraryAddressPopulationRow.Unresolved>(row));
        Assert.False(result.IsComplete);
        Assert.Equal(2, envelope.Diagnostics.Length);
    }

    [Fact]
    public async Task FullyResolvedPopulationCompletes()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        (int methodToken, int ilOffset) =
            FirstMethodAddress(implementation);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));
        var intent = new LibraryAddressIntent.Population(
            [
                new LibraryAddressPopulationRecord.Coordinate(
                    1,
                    $"0x{methodToken:X8}+0x{ilOffset:X}",
                    label: null,
                    methodToken,
                    ilOffset),
            ],
            ILOffsetProjectionCapabilities.InstructionContext);

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(library, intent);

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(envelope.Content);
        LibraryAddressPopulationResult result =
            Assert.IsType<LibraryAddressDocument.Population>(
                    completed.Document)
                .Result;
        Assert.True(result.IsComplete);
        Assert.IsType<LibraryAddressPopulationRow.Resolved>(
            Assert.Single(result.Rows));
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public void PopulationEnforcesBoundAtConstruction()
    {
        LibraryAddressPopulationRecord[] accepted =
        [
            .. Enumerable.Range(
                    1,
                    LibraryAddressIntent.Population.MaximumRecords)
                .Select(index =>
                    new LibraryAddressPopulationRecord.Coordinate(
                        index,
                        "0x06000001+0x0",
                        label: null,
                        0x06000001,
                        0)),
        ];
        var intent = new LibraryAddressIntent.Population(
            accepted,
            ILOffsetProjectionCapabilities.None);
        Assert.Equal(
            LibraryAddressIntent.Population.MaximumRecords,
            intent.Records.Length);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryAddressIntent.Population(
                accepted.Append(
                    new LibraryAddressPopulationRecord.Malformed(
                        accepted.Length + 1,
                        label: null,
                        "extra")),
                ILOffsetProjectionCapabilities.None));
    }

    [Fact]
    public async Task RetainedPortablePdbContributesSourceEvidence()
    {
        string assemblyPath =
            typeof(LibraryAddressInspectionOperationTests)
                .Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assembly =
            await File.ReadAllBytesAsync(
                assemblyPath,
                TestContext.Current.CancellationToken);
        byte[] pdb =
            await File.ReadAllBytesAsync(
                pdbPath,
                TestContext.Current.CancellationToken);
        (int methodToken, int ilOffset) =
            FirstSequencePointAddress(pdb);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                assembly,
                LibraryInspectionTestLibrary.Identity(assembly),
                assembly,
                pdb);

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.SourceLocation));

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(envelope.Content);
        var document =
            Assert.IsType<LibraryAddressDocument.IlPoint>(
                completed.Document);
        var resolved =
            Assert.IsType<LibraryIlAddressOutcome.Resolved>(
                document.Result);
        Assert.NotNull(resolved.Projection.File);
        Assert.NotNull(resolved.Projection.Line);
    }

    [Fact]
    public async Task MalformedPortablePdbIsFormatFailureForExactAndPopulation()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        (int methodToken, int ilOffset) =
            FirstMethodAddress(implementation);
        byte[] malformedPdb =
            [(byte)'B', (byte)'S', (byte)'J', (byte)'B'];

        await using LibraryInspectionTestLibrary exactLibrary =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation),
                implementation,
                malformedPdb);
        InspectionEnvelope<LibraryAddressInspectionOutcome> exact =
            Execute(
                exactLibrary,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.SourceLocation));
        Assert.Equal(
            LibraryAddressInspectionFailure.MalformedMetadata,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    exact.Content)
                .Reason);
        Assert.NotEmpty(exact.Diagnostics);

        await using LibraryInspectionTestLibrary populationLibrary =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation),
                implementation,
                malformedPdb);
        InspectionEnvelope<LibraryAddressInspectionOutcome> population =
            Execute(
                populationLibrary,
                new LibraryAddressIntent.Population(
                    [
                        new LibraryAddressPopulationRecord.Coordinate(
                            1,
                            $"0x{methodToken:X8}+0x{ilOffset:X}",
                            label: null,
                            methodToken,
                            ilOffset),
                    ],
                    ILOffsetProjectionCapabilities.SourceLocation));
        Assert.Equal(
            LibraryAddressInspectionFailure.MalformedMetadata,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    population.Content)
                .Reason);
        Assert.NotEmpty(population.Diagnostics);
    }

    [Fact]
    public async Task MismatchedPortablePdbIsCorrespondenceRejection()
    {
        byte[] implementation =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        byte[] mismatchedPdb =
            await File.ReadAllBytesAsync(
                Path.ChangeExtension(
                    typeof(LibraryAddressInspectionOperationTests)
                        .Assembly.Location,
                    ".pdb"),
                TestContext.Current.CancellationToken);
        (int methodToken, int ilOffset) =
            FirstMethodAddress(implementation);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation),
                implementation,
                mismatchedPdb);

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            Execute(
                library,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.SourceLocation));

        Assert.Equal(
            LibraryAddressInspectionRejection
                .PortablePdbCorrespondenceMismatch,
            Assert.IsType<LibraryAddressInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);
    }

    [Fact]
    public async Task NonSourceRequestsDoNotLoadEmbeddedPortablePdb()
    {
        byte[] implementation =
            await File.ReadAllBytesAsync(
                FixtureCatalog.InspectWebSourceComparisonV1
                    .AssemblyPath(),
                TestContext.Current.CancellationToken);
        (int methodToken, int ilOffset) =
            FirstMethodAddress(implementation);
        await using LibraryInspectionTestLibrary exactLibrary =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));
        var limits = new LibraryAddressInspectionLimits(
            maximumPortablePdbBytes: 0);

        InspectionEnvelope<LibraryAddressInspectionOutcome> exact =
            LibraryAddressInspectionOperation.Execute(
                new(
                    exactLibrary.Reference,
                    new LibraryAddressIntent.IlPoint(
                        methodToken,
                        ilOffset,
                        ILOffsetProjectionCapabilities.InstructionContext),
                    limits),
                exactLibrary.IssueOperation(),
                TestContext.Current.CancellationToken);
        Assert.IsType<LibraryAddressInspectionOutcome.Completed>(
            exact.Content);

        await using LibraryInspectionTestLibrary populationLibrary =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));
        InspectionEnvelope<LibraryAddressInspectionOutcome> population =
            LibraryAddressInspectionOperation.Execute(
                new(
                    populationLibrary.Reference,
                    new LibraryAddressIntent.Population(
                        [
                            new LibraryAddressPopulationRecord.Coordinate(
                                1,
                                $"0x{methodToken:X8}+0x{ilOffset:X}",
                                label: null,
                                methodToken,
                                ilOffset),
                        ],
                        ILOffsetProjectionCapabilities
                            .InstructionContext),
                    limits),
                populationLibrary.IssueOperation(),
                TestContext.Current.CancellationToken);
        Assert.IsType<LibraryAddressInspectionOutcome.Completed>(
            population.Content);
    }

    [Fact]
    public async Task DebugDirectoryLimitIsAnInspectionFailure()
    {
        byte[] original =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity =
            LibraryInspectionTestLibrary.Identity(original);
        byte[] implementation = SetDebugDirectorySize(original, 1793);
        (int methodToken, int ilOffset) =
            FirstMethodAddress(original);

        await using LibraryInspectionTestLibrary exactLibrary =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                identity);
        InspectionEnvelope<LibraryAddressInspectionOutcome> exact =
            Execute(
                exactLibrary,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.InstructionContext));
        Assert.Equal(
            LibraryAddressInspectionFailure.Inspection,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    exact.Content)
                .Reason);

        await using LibraryInspectionTestLibrary populationLibrary =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                identity);
        InspectionEnvelope<LibraryAddressInspectionOutcome> population =
            Execute(
                populationLibrary,
                new LibraryAddressIntent.Population(
                    [
                        new LibraryAddressPopulationRecord.Coordinate(
                            1,
                            $"0x{methodToken:X8}+0x{ilOffset:X}",
                            label: null,
                            methodToken,
                            ilOffset),
                    ],
                    ILOffsetProjectionCapabilities.InstructionContext));
        Assert.Equal(
            LibraryAddressInspectionFailure.Inspection,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    population.Content)
                .Reason);
    }

    [Fact]
    public async Task CancellationAndInvalidRequestSettleLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary cancelled =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => LibraryAddressInspectionOperation.Execute(
                new(
                    cancelled.Reference,
                    new LibraryAddressIntent.HeapPoint(
                        MetadataRootKind.Cli,
                        HeapKind.String,
                        0)),
                cancelled.IssueOperation(),
                cancellation.Token));
        await cancelled.RetireAsync();

        await using LibraryInspectionTestLibrary invalid =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        Assert.Throws<ArgumentNullException>(
            () => LibraryAddressInspectionOperation.Execute(
                null!,
                invalid.IssueOperation(),
                TestContext.Current.CancellationToken));
        await invalid.RetireAsync();
    }

    [Fact]
    public async Task AssemblySnapshotLimitIsTypedAndSettlesLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var request = new LibraryAddressInspectionRequest(
            library.Reference,
            new LibraryAddressIntent.HeapPoint(
                MetadataRootKind.Cli,
                HeapKind.String,
                0),
            new LibraryAddressInspectionLimits(
                maximumAssemblyBytes: 1));

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            LibraryAddressInspectionOperation.Execute(
                request,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            LibraryAddressInspectionOutcome.Incomplete>(
                envelope.Content);
        Assert.Equal(
            LibraryAddressInspectionBound.AssemblyBytes,
            incomplete.Bound);
        await library.RetireAsync();
    }

    [Fact]
    public async Task PortablePdbSnapshotLimitIsTypedAndSettlesLease()
    {
        string assemblyPath =
            typeof(LibraryAddressInspectionOperationTests)
                .Assembly.Location;
        byte[] assembly =
            await File.ReadAllBytesAsync(
                assemblyPath,
                TestContext.Current.CancellationToken);
        byte[] pdb =
            await File.ReadAllBytesAsync(
                Path.ChangeExtension(assemblyPath, ".pdb"),
                TestContext.Current.CancellationToken);
        (int methodToken, int ilOffset) =
            FirstSequencePointAddress(pdb);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                assembly,
                LibraryInspectionTestLibrary.Identity(assembly),
                assembly,
                pdb);
        var request = new LibraryAddressInspectionRequest(
            library.Reference,
            new LibraryAddressIntent.IlPoint(
                methodToken,
                ilOffset,
                ILOffsetProjectionCapabilities.SourceLocation),
            new LibraryAddressInspectionLimits(
                maximumPortablePdbBytes: 1));

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            LibraryAddressInspectionOperation.Execute(
                request,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            LibraryAddressInspectionOutcome.Incomplete>(
                envelope.Content);
        Assert.Equal(
            LibraryAddressInspectionBound.PortablePdbBytes,
            incomplete.Bound);
        await library.RetireAsync();
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Execute(
            LibraryInspectionTestLibrary library,
            LibraryAddressIntent intent) =>
        LibraryAddressInspectionOperation.Execute(
            new(library.Reference, intent),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static (int MethodToken, int ILOffset)
        FirstMethodAddress(byte[] assembly)
    {
        using var reader = new PEReader(
            new MemoryStream(assembly, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        foreach (MethodDefinitionHandle handle
            in metadata.MethodDefinitions)
        {
            MethodDefinition method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            MethodBodyBlock body =
                reader.GetMethodBody(method.RelativeVirtualAddress);
            if (body.GetILBytes() is not { Length: > 0 })
                continue;
            return (MetadataTokens.GetToken(handle), 0);
        }

        throw new InvalidOperationException(
            "The real Library asset contains no method body.");
    }

    private static int FirstTypeNameAddress(byte[] assembly)
    {
        using var reader = new PEReader(
            new MemoryStream(assembly, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        TypeDefinitionHandle handle =
            metadata.TypeDefinitions.First(
                candidate =>
                    !metadata.GetTypeDefinition(candidate).Name.IsNil);
        return MetadataTokens.GetHeapOffset(
            metadata.GetTypeDefinition(handle).Name);
    }

    private static (int MethodToken, int ILOffset)
        FirstSequencePointAddress(byte[] pdb)
    {
        using MetadataReaderProvider provider =
            MetadataReaderProvider.FromPortablePdbStream(
                new MemoryStream(pdb, writable: false));
        MetadataReader reader = provider.GetMetadataReader();
        foreach (MethodDebugInformationHandle handle
            in reader.MethodDebugInformation)
        {
            foreach (SequencePoint point
                in reader.GetMethodDebugInformation(handle)
                    .GetSequencePoints())
            {
                if (point.IsHidden)
                    continue;
                MethodDefinitionHandle method =
                    MetadataTokens.MethodDefinitionHandle(
                        MetadataTokens.GetRowNumber(handle));
                return (MetadataTokens.GetToken(method), point.Offset);
            }
        }

        throw new InvalidOperationException(
            "The test Portable PDB contains no visible sequence point.");
    }

    private static byte[] SetDebugDirectorySize(
        byte[] image,
        uint size)
    {
        byte[] patched = [.. image];
        using var reader = new PEReader(
            new MemoryStream(image, writable: false));
        PEHeader peHeader = Assert.IsType<PEHeader>(
            reader.PEHeaders.PEHeader);
        int directoryBase =
            reader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        const int DebugDirectoryIndex = 6;
        int sizeOffset =
            directoryBase
            + DebugDirectoryIndex * 8
            + sizeof(int);
        BinaryPrimitives.WriteUInt32LittleEndian(
            patched.AsSpan(sizeOffset, sizeof(uint)),
            size);
        return patched;
    }
}
