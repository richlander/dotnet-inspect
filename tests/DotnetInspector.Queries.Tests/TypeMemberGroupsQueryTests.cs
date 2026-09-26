using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries.Tests;

public sealed class TypeMemberGroupsQueryTests
{
    private static readonly TypeMemberGroupPopulationQueryBounds Bounds =
        new(
            maximumAssemblyBytes: 16 * 1024 * 1024,
            maximumMetadataRows: 1_000_000,
            maximumRetainedGroups: 10_000,
            maximumNameWorkBytes: 16 * 1024 * 1024,
            maximumRetainedTextCharacters: 1_000_000);

    [Fact]
    public async Task
        JsonSerializer_ProducesMethodGroupsAndNestedOverloadCounts()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        using LibraryOperationLease operation = library.IssueOperation();

        TypeMemberGroupPopulationOutcome.Rows rows =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Rows>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Rows,
                        includeExactMemberCount: true),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(10, rows.Items.Length);
        Assert.Same(
            library.Reference,
            rows.Binding.Type.ApiContent.Library);
        Assert.Equal(
            107,
            rows.Items.Sum(
                static row =>
                    Assert.IsType<
                            ExactMemberPopulationCountOutcome.Counted>(
                            row.ExactMemberCount)
                        .Value));
        TypeMemberGroupRow deserialize =
            Assert.Single(
                rows.Items,
                static row => row.Identity.Name.ToString()
                    == "Deserialize");
        Assert.Equal(
            TypeMemberGroupRole.Declared,
            deserialize.Identity.Role);
        Assert.Equal(
            TypeMemberReceiverKinds.Static
                | TypeMemberReceiverKinds.Extension,
            deserialize.ReceiverKinds);
        Assert.Equal(
            40,
            Assert.IsType<
                    ExactMemberPopulationCountOutcome.Counted>(
                    deserialize.ExactMemberCount)
                .Value);
        Assert.Equal(0, rows.Work.ExactMemberRowsMaterialized);
        Assert.True(rows.Work.SignaturesDecoded > 0);
        Assert.Equal(
            0,
            rows.Work.FormattedSignaturesMaterialized);

        using LibraryOperationLease propertyOperation =
            library.IssueOperation();
        TypeMemberGroupPopulationOutcome.Counted properties =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Counted>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        TypeMemberGroupCategory.Property,
                        QuerySpaceTerminalRequirement.Count),
                    propertyOperation,
                    TestContext.Current.CancellationToken));
        Assert.Equal(1, properties.Value);
    }

    [Fact]
    public async Task
        JsonSerializer_ReceiverSelectionFiltersChildrenBeforeGrouping()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");

        TypeMemberGroupPopulationOutcome.Rows extensionRows;
        using (LibraryOperationLease operation =
            library.IssueOperation())
        {
            extensionRows =
                Assert.IsType<TypeMemberGroupPopulationOutcome.Rows>(
                    TypeMemberGroupsQuery.Execute(
                        Request(
                            library.Reference,
                            type,
                            TypeMemberGroupCategory.Method,
                            QuerySpaceTerminalRequirement.Rows,
                            includeExactMemberCount: true,
                            TypeMemberGroupsQuery.CreateRowIntent(
                                TypeMemberReceiver.Extension)),
                        operation,
                        TestContext.Current.CancellationToken));
        }

        TypeMemberGroupPopulationOutcome.Rows nonExtensionRows;
        using (LibraryOperationLease operation =
            library.IssueOperation())
        {
            nonExtensionRows =
                Assert.IsType<TypeMemberGroupPopulationOutcome.Rows>(
                    TypeMemberGroupsQuery.Execute(
                        Request(
                            library.Reference,
                            type,
                            TypeMemberGroupCategory.Method,
                            QuerySpaceTerminalRequirement.Rows,
                            includeExactMemberCount: true,
                            TypeMemberGroupsQuery.CreateRowIntent(
                                TypeMemberReceiver.Extension,
                                exclude: true)),
                        operation,
                        TestContext.Current.CancellationToken));
        }

        TypeMemberGroupRow extension =
            Assert.Single(
                extensionRows.Items,
                static row => row.Identity.Name.ToString()
                    == "Deserialize");
        TypeMemberGroupRow nonExtension =
            Assert.Single(
                nonExtensionRows.Items,
                static row => row.Identity.Name.ToString()
                    == "Deserialize");
        Assert.Equal(extension.Identity, nonExtension.Identity);
        Assert.NotEqual(
            extension.ExactMembers,
            nonExtension.ExactMembers);
        Assert.Equal(
            15,
            Assert.IsType<
                    ExactMemberPopulationCountOutcome.Counted>(
                    extension.ExactMemberCount)
                .Value);
        Assert.Equal(
            25,
            Assert.IsType<
                    ExactMemberPopulationCountOutcome.Counted>(
                    nonExtension.ExactMemberCount)
                .Value);
        Assert.Equal(
            TypeMemberReceiverKinds.Extension,
            extension.ReceiverKinds);
        Assert.Equal(
            TypeMemberReceiverKinds.Static,
            nonExtension.ReceiverKinds);
    }

    [Fact]
    public async Task
        JsonElement_AttachedExtensionGroupRetainsDeclaringType()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        using LibraryOperationLease operation = library.IssueOperation();

        TypeMemberGroupPopulationOutcome.Rows rows =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Rows>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name("System.Text.Json", "JsonElement"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Rows,
                        includeExactMemberCount: true,
                        TypeMemberGroupsQuery.CreateRowIntent(
                            TypeMemberReceiver.Extension)),
                    operation,
                    TestContext.Current.CancellationToken));

        TypeMemberGroupRow deserialize =
            Assert.Single(
                rows.Items,
                static row => row.Identity.Name.ToString()
                    == "Deserialize");
        Assert.Equal(
            TypeMemberGroupRole.AttachedExtension,
            deserialize.Identity.Role);
        Assert.Equal(
            Name("System.Text.Json", "JsonSerializer"),
            deserialize.Identity.AttachedDeclaringType!.Definition);
        Assert.Equal(
            5,
            Assert.IsType<
                    ExactMemberPopulationCountOutcome.Counted>(
                    deserialize.ExactMemberCount)
                .Value);
    }

    [Fact]
    public async Task CountAndBoundedRowsUseTerminalSpecificWork()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");

        TypeMemberGroupPopulationOutcome.Counted count;
        using (LibraryOperationLease operation =
            library.IssueOperation())
        {
            count =
                Assert.IsType<TypeMemberGroupPopulationOutcome.Counted>(
                    TypeMemberGroupsQuery.Execute(
                        Request(
                            library.Reference,
                            type,
                            TypeMemberGroupCategory.Method,
                            QuerySpaceTerminalRequirement.Count),
                        operation,
                        TestContext.Current.CancellationToken));
        }

        TypeMemberGroupPopulationOutcome.Rows rows;
        using (LibraryOperationLease operation =
            library.IssueOperation())
        {
            rows =
                Assert.IsType<TypeMemberGroupPopulationOutcome.Rows>(
                    TypeMemberGroupsQuery.Execute(
                        Request(
                            library.Reference,
                            type,
                            TypeMemberGroupCategory.Method,
                            QuerySpaceTerminalRequirement.Rows,
                            includeExactMemberCount: true,
                            TypeMemberGroupsQuery.CreateRowIntent(
                                selection:
                                    RowSelectionIntent<string>.Create(
                                    [
                                        RowSelectionIntentOperation<string>
                                            .Head(1),
                                    ]))),
                        operation,
                        TestContext.Current.CancellationToken));
        }

        Assert.Equal(10, count.Value);
        Assert.Equal(0, count.Work.RowsMaterialized);
        Assert.Equal(0, count.Work.ExactMemberRowsMaterialized);
        Assert.True(count.Work.SignaturesDecoded > 0);
        Assert.Equal(
            0,
            count.Work.FormattedSignaturesMaterialized);
        Assert.Single(rows.Items);
        Assert.Equal(1, rows.Work.RowsMaterialized);
        Assert.Equal(10, rows.Work.GroupsAggregated);
        Assert.Equal(0, rows.Work.ExactMemberRowsMaterialized);
        Assert.True(rows.Work.SignaturesDecoded > 0);
        Assert.Equal(
            0,
            rows.Work.FormattedSignaturesMaterialized);
    }

    [Fact]
    public void QuerySpaceRejectsForeignRequestBeforeSourceExecution()
    {
        QuerySpaceRequest invalid =
            DocumentationQuery.CreateRequest(
                DocumentationDemand.CompiledXml);

        Assert.Equal(
            TypeMemberGroupsQueryRequestRejectionKind
                .QuerySpaceMismatch,
            Assert.IsType<TypeMemberGroupsQueryRequestResult.Rejected>(
                    TypeMemberGroupsQuery.ResolveRequest(
                        invalid,
                        TestContext.Current.CancellationToken))
                .Kind);
    }

    [Fact]
    public async Task MissingTypeIsTypedUnavailability()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        using LibraryOperationLease operation = library.IssueOperation();

        TypeMemberGroupPopulationOutcome.Unavailable unavailable =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Unavailable>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name("System.Text.Json", "MissingType"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Rows),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            TypeMemberGroupPopulationUnavailableReason.TypeNotFound,
            unavailable.Reason);
    }

    [Fact]
    public async Task RetainedGroupBoundIsTypedIncomplete()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        using LibraryOperationLease operation = library.IssueOperation();
        var bounds = new TypeMemberGroupPopulationQueryBounds(
            maximumAssemblyBytes: 16 * 1024 * 1024,
            maximumMetadataRows: 1_000_000,
            maximumRetainedGroups: 0,
            maximumNameWorkBytes: 16 * 1024 * 1024,
            maximumRetainedTextCharacters: 1_000_000);

        TypeMemberGroupPopulationOutcome.Incomplete incomplete =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Incomplete>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Rows,
                        bounds: bounds),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            TypeMemberGroupPopulationIncompleteReason.RetainedGroups,
            incomplete.Reason);
        Assert.Equal(1, incomplete.Measured);
    }

    [Fact]
    public async Task NameWorkBoundIsTypedIncomplete()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        using LibraryOperationLease operation = library.IssueOperation();
        var bounds = new TypeMemberGroupPopulationQueryBounds(
            maximumAssemblyBytes: 16 * 1024 * 1024,
            maximumMetadataRows: 1_000_000,
            maximumRetainedGroups: 10_000,
            maximumNameWorkBytes: 0,
            maximumRetainedTextCharacters: 1_000_000);

        TypeMemberGroupPopulationOutcome.Incomplete incomplete =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Incomplete>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Count,
                        bounds: bounds),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            TypeMemberGroupPopulationIncompleteReason.NameWorkBytes,
            incomplete.Reason);
        Assert.True(incomplete.Measured > 0);
    }

    [Fact]
    public async Task OutOfRangeWindowPreservesSelectionFailure()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        using LibraryOperationLease operation = library.IssueOperation();

        TypeMemberGroupPopulationOutcome.Rejected rejected =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Rejected>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Rows,
                        rowIntent:
                            TypeMemberGroupsQuery.CreateRowIntent(
                                selection:
                                    RowSelectionIntent<string>.Create(
                                    [
                                        RowSelectionIntentOperation<string>
                                            .Window(11, null),
                                    ]))),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            TypeMemberGroupPopulationRejectionReason
                .SelectionOutOfRange,
            rejected.Reason);
        Assert.Equal(
            new TypeMemberGroupSelectionFailure(
                StageNumber: 1,
                RequiredPosition: 11,
                AvailableCount: 10),
            rejected.SelectionFailure);
    }

    [Fact]
    public async Task ForeignLibraryLeaseIsTypedRejection()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(JsonSerializer).Assembly.Location,
            TestContext.Current.CancellationToken);
        await using LibraryFixture requested =
            await LibraryFixture.CreateAsync(content);
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync(content);
        using LibraryOperationLease foreignOperation =
            foreign.IssueOperation();

        TypeMemberGroupPopulationOutcome.Rejected rejected =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Rejected>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        requested.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Rows),
                    foreignOperation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            TypeMemberGroupPopulationRejectionReason.Library,
            rejected.Reason);
        Assert.Equal(
            TypeMemberGroupPopulationLibraryRejection
                .LeaseReferenceMismatch,
            rejected.Library);
    }

    [Fact]
    public async Task AssemblyIdentityMismatchIsTypedRejection()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(JsonSerializer).Assembly.Location,
            TestContext.Current.CancellationToken);
        var wrongIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "System.Text.Json",
                new Version(99, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                content,
                wrongIdentity);
        using LibraryOperationLease operation = library.IssueOperation();

        TypeMemberGroupPopulationOutcome.Rejected rejected =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Rejected>(
                TypeMemberGroupsQuery.Execute(
                    Request(
                        library.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        TypeMemberGroupCategory.Method,
                        QuerySpaceTerminalRequirement.Rows),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            TypeMemberGroupPopulationRejectionReason.Library,
            rejected.Reason);
        Assert.Equal(
            TypeMemberGroupPopulationLibraryRejection
                .AssemblyIdentityMismatch,
            rejected.Library);
    }

    [Fact]
    public async Task SameAssemblyInDifferentLibrariesHasDistinctBindings()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(JsonSerializer).Assembly.Location,
            TestContext.Current.CancellationToken);
        await using LibraryFixture first =
            await LibraryFixture.CreateAsync(content);
        await using LibraryFixture second =
            await LibraryFixture.CreateAsync(content);

        TypeMemberGroupPopulationOutcome.Rows firstRows;
        using (LibraryOperationLease operation = first.IssueOperation())
        {
            firstRows =
                Assert.IsType<TypeMemberGroupPopulationOutcome.Rows>(
                    TypeMemberGroupsQuery.Execute(
                        Request(
                            first.Reference,
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            TypeMemberGroupCategory.Method,
                            QuerySpaceTerminalRequirement.Rows,
                            rowIntent:
                                TypeMemberGroupsQuery.CreateRowIntent(
                                    selection:
                                        RowSelectionIntent<string>.Create(
                                        [
                                            RowSelectionIntentOperation<string>
                                                .Head(1),
                                        ]))),
                        operation,
                        TestContext.Current.CancellationToken));
        }

        TypeMemberGroupPopulationOutcome.Rows secondRows;
        using (LibraryOperationLease operation = second.IssueOperation())
        {
            secondRows =
                Assert.IsType<TypeMemberGroupPopulationOutcome.Rows>(
                    TypeMemberGroupsQuery.Execute(
                        Request(
                            second.Reference,
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            TypeMemberGroupCategory.Method,
                            QuerySpaceTerminalRequirement.Rows,
                            rowIntent:
                                TypeMemberGroupsQuery.CreateRowIntent(
                                    selection:
                                        RowSelectionIntent<string>.Create(
                                        [
                                            RowSelectionIntentOperation<string>
                                                .Head(1),
                                        ]))),
                        operation,
                        TestContext.Current.CancellationToken));
        }

        Assert.NotEqual(
            firstRows.Binding.Type,
            secondRows.Binding.Type);
        Assert.NotEqual(
            Assert.Single(firstRows.Items).Identity,
            Assert.Single(secondRows.Items).Identity);
    }

    [Fact]
    public async Task QueryRejectionRetainsStructuralReason()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    TestContext.Current.CancellationToken));
        using LibraryOperationLease operation = library.IssueOperation();
        QuerySpaceRequest invalid =
            DocumentationQuery.CreateRequest(
                DocumentationDemand.CompiledXml);

        TypeMemberGroupPopulationOutcome.Rejected rejected =
            Assert.IsType<TypeMemberGroupPopulationOutcome.Rejected>(
                TypeMemberGroupsQuery.Execute(
                    new(
                        library.Reference,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        invalid,
                        Bounds),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            TypeMemberGroupPopulationRejectionReason.Query,
            rejected.Reason);
        Assert.Equal(
            TypeMemberGroupsQueryRequestRejectionKind.QuerySpaceMismatch,
            rejected.QueryRequest);
    }

    [Fact]
    public void PublicResultClosureIsResourceFree()
    {
        Type[] roots =
        [
            typeof(TypeMemberGroupTypeIdentity),
            typeof(TypeMemberGroupIdentity),
            typeof(ExactMemberPopulationBinding),
            typeof(ExactMemberPopulationCountOutcome),
            typeof(TypeMemberGroupRow),
            typeof(TypeMemberGroupPopulationBinding),
            typeof(TypeMemberGroupPopulationWork),
            typeof(TypeMemberGroupSelectionFailure),
            typeof(TypeMemberGroupPopulationOutcome),
        ];
        var seen = new HashSet<Type>();
        foreach (Type root in roots)
            Visit(root);

        void Visit(Type candidate)
        {
            candidate = Nullable.GetUnderlyingType(candidate)
                ?? candidate;
            if (candidate.IsGenericParameter || !seen.Add(candidate))
                return;

            Assert.False(
                typeof(IDisposable).IsAssignableFrom(candidate),
                candidate.FullName);
            Assert.False(
                typeof(IAsyncDisposable).IsAssignableFrom(candidate),
                candidate.FullName);
            Assert.False(
                typeof(Stream).IsAssignableFrom(candidate),
                candidate.FullName);
            Assert.False(
                typeof(Delegate).IsAssignableFrom(candidate),
                candidate.FullName);
            Assert.False(
                typeof(Exception).IsAssignableFrom(candidate),
                candidate.FullName);
            Assert.False(candidate.IsByRefLike, candidate.FullName);

            if (candidate.IsArray)
            {
                Visit(candidate.GetElementType()!);
                return;
            }
            if (candidate.IsGenericType)
            {
                foreach (Type argument in candidate.GetGenericArguments())
                    Visit(argument);
            }
            if (candidate.IsPrimitive
                || candidate.IsEnum
                || candidate == typeof(string)
                || candidate.Namespace?.StartsWith(
                    "System",
                    StringComparison.Ordinal) is true)
            {
                return;
            }

            foreach (Type nested in candidate.GetNestedTypes(
                BindingFlags.Public))
            {
                Visit(nested);
            }
            foreach (PropertyInfo property in candidate.GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            {
                Visit(property.PropertyType);
            }
        }
    }

    private static TypeMemberGroupPopulationQueryRequest Request(
        LibraryReference library,
        MetadataTypeDefinitionName type,
        TypeMemberGroupCategory category,
        QuerySpaceTerminalRequirement terminal,
        bool includeExactMemberCount = false,
        PortableQueryIntent? rowIntent = null,
        TypeMemberGroupPopulationQueryBounds? bounds = null) =>
        new(
            library,
            type,
            TypeMemberGroupsQuery.CreateRequest(
                category,
                terminal,
                rowIntent),
            bounds ?? Bounds,
            includeExactMemberCount);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private sealed class LibraryFixture : IAsyncDisposable
    {
        private readonly ArtifactSetSession _session;
        private readonly LibraryContentOwner _owner;

        private LibraryFixture(
            ArtifactSetSession session,
            LibraryContentOwner owner)
        {
            _session = session;
            _owner = owner;
        }

        internal LibraryReference Reference => _owner.Reference;

        internal LibraryOperationLease IssueOperation() =>
            Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                    _owner.IssueOperationLease(Reference))
                .Lease;

        internal static async Task<LibraryFixture> CreateAsync(
            byte[] content,
            ManagedMetadataIdentity.Assembly? declaredIdentity = null)
        {
            var session = new ArtifactSetSession();
            try
            {
                ArtifactContribution? contribution = null;
                await session.AddRequiredAcquisitionAsync(
                    (scope, cancellationToken) =>
                    {
                        contribution = scope.Register(
                            new Provenance(
                                "type-member-group-query-test"),
                            token =>
                            {
                                token.ThrowIfCancellationRequested();
                                return new MemoryStream(
                                    content,
                                    writable: false);
                            });
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    [contribution],
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken:
                        TestContext.Current.CancellationToken);
                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(
                        TestContext.Current.CancellationToken));

                using ArtifactQueryLease queryLease =
                    session.IssueLease(
                        session.CreateQueryAuthorization());
                ArtifactContentReference reference =
                    session.GetContentReference(
                        contribution!.Descriptor.Identity,
                        queryLease);
                ArtifactContentLease contentLease =
                    session.IssueContentLease(
                        reference,
                        queryLease);
                try
                {
                    ManagedMetadataIdentity.Assembly identity =
                        declaredIdentity ?? Identity(content);
                    LibraryReference library =
                        LibraryReference.CreateDirect(
                            new LibraryAssemblyCorrespondence(
                                reference,
                                identity,
                                reference,
                                identity));
                    var owner = new LibraryContentOwner(
                        library,
                        [contentLease]);
                    contentLease = null!;
                    return new(session, owner);
                }
                finally
                {
                    contentLease?.Dispose();
                }
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _owner.DisposeAsync();
            await _session.DisposeAsync();
        }

        private static ManagedMetadataIdentity.Assembly Identity(
            byte[] content)
        {
            using var peReader = new PEReader(
                new MemoryStream(content, writable: false));
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(peReader);
            return new(
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
        }
    }

    private sealed record Provenance(string Name) :
        IArtifactProvenance;
}
