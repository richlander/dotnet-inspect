using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using Inspector.Resources;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for the assembly inspection substrate (<see cref="AssemblyImage"/> /
/// <see cref="AssemblyInspectionSession"/>): the shared-open hub that produces assembly facets
/// over a single <c>PEReader</c>. See <c>docs/design/assembly-inspection-query.md</c>.
/// </summary>
public class AssemblyInspectionSessionTests
{
    static string SelfPath => typeof(AssemblyInspectionSessionTests).Assembly.Location;
    const string SelfName = "ILInspector.Metadata.Tests";

    [Fact]
    public void Open_FromPath_HasMetadata()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        Assert.True(session.HasMetadata);
    }

    [Fact]
    public void NoMetadataSettlesSessionBackedMetadataProducers()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    MetadataAdmissionCleanupTests.BuildNoMetadataImage(),
                    writable: false));

        Assert.False(session.HasMetadata);
        AssertNoMetadata(
            Assert.IsType<MetadataRelationInspectionOutcome.Rejected>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.AssemblyReferences],
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken))
                .Format);
        AssertNoMetadata(
            Assert.IsType<MetadataLibrarySignatureUseOutcome.Rejected>(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken))
                .Format);
        AssertNoMetadata(
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Rejected>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            count: new()),
                        TestContext.Current.CancellationToken))
                .Format);
        AssertNoMetadata(
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Rejected>(
                    session.ExtensionRelations(
                        new(
                            new(
                                new(
                                    "System.Private.CoreLib",
                                    new Version(11, 0, 0, 0),
                                    Culture: null,
                                    PublicKeyToken: null),
                                TypeName("System", "Object")),
                            MetadataOperationPolicy.Unbounded,
                            count: new()),
                        TestContext.Current.CancellationToken))
                .Format);
    }

    [Fact]
    public void OpenPrefetched_TransfersStreamOwnershipAndPostsDetachedDeclarations()
    {
        var stream =
            new DisposeCountingStream(File.OpenRead(SelfPath));
        AssemblyTypeDeclarationInventory inventory;
        using (AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream))
        {
            Assert.Equal(1, stream.DisposeCount);
            inventory = Assert.IsType<
                    AssemblyTypeDeclarationInventoryOutcome.Read>(
                    session.TypeDeclarations())
                .Inventory;
        }

        Assert.Equal(1, stream.DisposeCount);
        Assert.Equal(SelfName, inventory.Identity.Name);
        Assert.NotEmpty(inventory.Declarations);
        Assert.NotNull(
            typeof(AssemblyInspectionSession).GetMethod(
                nameof(AssemblyInspectionSession.OpenPrefetched),
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static,
                [typeof(Stream)]));
    }

    [Fact]
    public void ResourceOwnershipContract_IsDeclared()
    {
        Assert.NotNull(
            typeof(AssemblyInspectionSession)
                .GetCustomAttributes(
                    typeof(ResourceOwnershipAttribute),
                    inherit: false)
                .Single());
        Assert.Contains(
            typeof(IResourceSnapshotSource<AssemblyInspectionSession>),
            typeof(AssemblyInspectionSession).GetInterfaces());
    }

    [Fact]
    public void Snapshot_ReturnsDetachedDataFromTheLiveSession()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);

        string assemblyName = session.Snapshot(
            SelfName,
            static (snapshot, expectedName) =>
            {
                Assert.Equal(
                    expectedName,
                    snapshot.Value.AssemblyInfo().AssemblyName);
                return snapshot.Value.AssemblyInfo().AssemblyName
                    ?? throw new InvalidOperationException(
                        "The test assembly has no assembly name.");
            });

        Assert.Equal(SelfName, assemblyName);
    }

    [Fact]
    public void Snapshot_UsesLiveSessionAndReturnsDetachedProjectionWithoutCopyingIt()
    {
        MetadataTableProjection projection;
        MetadataTableProjection? callbackProjection = null;
        using (var session = AssemblyInspectionSession.Open(SelfPath))
        {
            projection = session.Snapshot(
                new MetadataProjectionOptions
                {
                    Tables = [TableIndex.Assembly],
                },
                (snapshot, options) =>
                {
                    Assert.Same(session, snapshot.Value);
                    callbackProjection =
                        snapshot.Value.MetadataTables(options);
                    return callbackProjection;
                });
        }

        Assert.Same(callbackProjection, projection);
        MetadataTableView table = Assert.Single(projection.Tables);
        Assert.Equal(TableIndex.Assembly, table.Index);
        Assert.Equal("Assembly", table.Name);
        Assert.Single(table.Rows);
    }

    [Fact]
    public void Snapshot_PropagatesFailureAndLeavesTheSessionUsable()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        var failure = new InvalidOperationException("snapshot failed");

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(
                () => session.Snapshot<object?, object?>(
                    state: null,
                    (snapshot, state) =>
                    {
                        Assert.True(snapshot.Value.HasMetadata);
                        Assert.Null(state);
                        throw failure;
                    }));

        Assert.Same(failure, thrown);
        Assert.True(session.HasMetadata);
    }

    [Fact]
    public void Snapshot_RejectsDisposedSessionBeforeInvokingCallback()
    {
        var session = AssemblyInspectionSession.Open(SelfPath);
        session.Dispose();
        bool invoked = false;

        Assert.Throws<ObjectDisposedException>(
            () => session.Snapshot(
                state: 0,
                (snapshot, state) =>
                {
                    invoked = true;
                    return state;
                }));
        Assert.False(invoked);
    }

    [Fact]
    public void SnapshotOperation_BindsExactOperationAndStableSubject()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        var firstOperation = new object();
        var secondOperation = new object();

        var first = session.SnapshotOperation(
            firstOperation,
            access =>
            {
                Assert.Same(firstOperation, access.Operation);
                Assert.True(access.HasMetadata);
                string? assemblyName = access.InspectImage(
                    reader =>
                        reader.GetMetadataReader()
                            .GetString(
                                reader.GetMetadataReader()
                                    .GetAssemblyDefinition()
                                    .Name));
                return (access.Subject, assemblyName);
            });
        AssemblyInspectionSubjectIdentity secondSubject =
            session.SnapshotOperation(
                secondOperation,
                access =>
                {
                    Assert.Same(secondOperation, access.Operation);
                    return access.Subject;
                });

        Assert.Equal(SelfName, first.assemblyName);
        Assert.Same(first.Subject, secondSubject);
    }

    [Fact]
    public void SnapshotOperation_RejectsDisposedSessionBeforeCallback()
    {
        var session = AssemblyInspectionSession.Open(SelfPath);
        session.Dispose();
        bool invoked = false;

        Assert.Throws<ObjectDisposedException>(
            () => session.SnapshotOperation(
                new object(),
                access =>
                {
                    invoked = true;
                    return access.Subject;
                }));
        Assert.False(invoked);
    }

    [Fact]
    public void BorrowedSessionSnapshot_UsesTheLenderLifetime()
    {
        using var context = PdbContext.Open(SelfPath);
        using var session = AssemblyInspectionSession.Borrow(context);

        Assert.Equal(
            SelfName,
            session.Snapshot(
                state: 0,
                static (snapshot, _) =>
                    snapshot.Value.AssemblyInfo().AssemblyName));

        context.Dispose();
        bool invoked = false;
        Assert.Throws<ObjectDisposedException>(
            () => session.Snapshot(
                state: 0,
                (snapshot, state) =>
                {
                    invoked = true;
                    return state;
                }));
        Assert.False(invoked);
    }

    [Fact]
    public void BorrowedSessionRelations_RequireTheLenderLifetime()
    {
        using var context = PdbContext.Open(SelfPath);
        using var session = AssemblyInspectionSession.Borrow(context);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => session.Relations(
                new(
                    [MetadataRelationFamily.AssemblyReferences],
                    MetadataOperationPolicy.Unbounded),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AssemblyInfo_ReturnsAssemblyName()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        Assert.Equal(SelfName, session.AssemblyInfo().AssemblyName);
    }

    [Fact]
    public void AuditMetadata_RejectsUseAfterSessionDisposal()
    {
        var session = AssemblyInspectionSession.Open(SelfPath);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.HasMetadata);
        Assert.Throws<ObjectDisposedException>(() => session.AuditMetadata());
    }

    [Fact]
    public void Facets_ProduceOverSharedReader_WithoutReopening()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);

        // Many facets, one open — none should throw, and the surface should be populated.
        Assert.NotEmpty(session.ApiSurface(includeAll: true).Types);
        Assert.NotNull(session.Resources());
        Assert.NotNull(session.CustomAttributes());
        Assert.NotNull(session.TypeForwarders());
        Assert.NotNull(session.UnionTypes());
        Assert.NotNull(session.Switches());
        Assert.NotNull(session.OpenTelemetrySignals());
        Assert.NotNull(session.EcosystemIntegrations());
        Assert.NotNull(session.AuditMetadata());
        Assert.NotNull(session.ExtensionMethods().ToList());
    }

    [Fact]
    public void DeclaresExtensionMember_RequiresExactStructuredIdentity()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        ExtensionMethodInfo declared = session
            .ExtensionMethods(includeAll: true)
            .First();
        MetadataTypeDefinitionName declaringType =
            declared.GetDeclaringTypeDefinition()
            ?? throw new InvalidOperationException(
                "Fixture extension has no declaring type.");
        MemberAnchor anchor = declared.Anchor
            ?? throw new InvalidOperationException(
                "Fixture extension has no member anchor.");

        Assert.True(
            session.DeclaresExtensionMember(
                declaringType,
                anchor));
        Assert.False(
            session.DeclaresExtensionMember(
                declaringType,
                new MemberAnchor(
                    "M~0000000000",
                    $"{declaringType}.NotDeclared()",
                    "0000000000",
                    "Missing.Explicit.Subject",
                    "NotDeclared")));
    }

    [Fact]
    public void MethodBodies_ReturnCopiedDataAndValidatedSelection()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        var fixture = Assert.Single(
            session.MethodBodies.EnumerateMethods(),
            method => method.Name == nameof(MethodBodyFixture.Echo));

        var selection = session.MethodBodies.ResolveMethod(
            fixture.DeclaringType,
            nameof(MethodBodyFixture.Echo),
            overloadIndex: 0,
            publicOnly: true);

        Assert.NotNull(selection);
        Assert.True(selection.HasBody);
        Assert.Equal(["T"], selection.GenericParameterNames);
        Assert.True(session.MethodBodies.TryRead(
            selection.MetadataToken,
            out var body,
            out var error),
            error);
        Assert.NotNull(body);
        Assert.NotEmpty(body.IL);
    }

    [Fact]
    public void MethodBodies_ResolveUniqueMethodRejectsAmbiguousName()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        string declaringType = Assert.Single(
            session.MethodBodies.EnumerateMethods(),
            method => method.Name == nameof(MethodBodyFixture.Echo))
            .DeclaringType;

        Assert.NotNull(
            session.MethodBodies.ResolveUniqueMethod(
                declaringType,
                nameof(MethodBodyFixture.Echo),
                publicOnly: true));
        Assert.Null(
            session.MethodBodies.ResolveUniqueMethod(
                declaringType,
                nameof(MethodBodyFixture.Overloaded),
                publicOnly: true));
        Assert.Null(
            session.MethodBodies.ResolveUniqueMethod(
                declaringType,
                nameof(MethodBodyFixture.Pick),
                publicOnly: true));
        Assert.Null(
            session.MethodBodies.ResolveUniqueMethod(
                declaringType,
                nameof(MethodBodyFixture.pick),
                publicOnly: true));
        Assert.Null(
            session.MethodBodies.ResolveUniqueMethod(
                declaringType,
                nameof(MethodBodyFixture.PickField),
                publicOnly: true));
    }

    [Fact]
    public void MethodBodies_ResolveAccessorMethodUsesAccessorOrdinal()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        string declaringType = Assert.Single(
            session.MethodBodies.EnumerateMethods(),
            method => method.Name == nameof(MethodBodyFixture.Echo))
            .DeclaringType;

        var getter =
            session.MethodBodies.ResolveAccessorMethod(
                declaringType,
                nameof(MethodBodyFixture.Value),
                accessorIndex: 0,
                publicOnly: true);
        var setter =
            session.MethodBodies.ResolveAccessorMethod(
                declaringType,
                nameof(MethodBodyFixture.Value),
                accessorIndex: 1,
                publicOnly: true);

        Assert.NotNull(getter);
        Assert.NotNull(setter);
        Assert.NotEqual(
            getter.MetadataToken,
            setter.MetadataToken);

        var writeOnly =
            session.MethodBodies.ResolveAccessorMethod(
                declaringType,
                nameof(MethodBodyFixture.SetterOnly),
                accessorIndex: 0,
                publicOnly: true);

        Assert.NotNull(writeOnly);
        Assert.Null(
            session.MethodBodies.ResolveAccessorMethod(
                declaringType,
                nameof(MethodBodyFixture.SetterOnly),
                accessorIndex: 1,
                publicOnly: true));
        Assert.Null(
            session.MethodBodies.ResolveAccessorMethod(
                declaringType,
                nameof(MethodBodyFixture.ValueField),
                accessorIndex: 0,
                publicOnly: true));
    }

    [Fact]
    public void MethodBodies_RejectResolverUseAfterSessionDisposal()
    {
        var session = AssemblyInspectionSession.Open(SelfPath);
        var source = session.MethodBodies;
        var fixture = Assert.Single(
            source.EnumerateMethods(),
            method => method.Name == nameof(MethodBodyFixture.Echo));
        var selection = source.ResolveMethod(
            fixture.DeclaringType,
            nameof(MethodBodyFixture.Echo),
            overloadIndex: 0,
            publicOnly: true);
        Assert.NotNull(selection);
        Assert.True(source.TryRead(selection.MetadataToken, out var body, out _));

        session.Dispose();

        Assert.NotEmpty(body!.IL);
        Assert.Throws<ObjectDisposedException>(
            () => source.ResolveMethod(selection.MetadataToken));
    }

    [Fact]
    public void Open_FromResolvedReference_MatchesPathOpen()
    {
        var reference = ResolvedAssemblyReference.Create(
            ReadIdentity(SelfPath),
            SelfPath,
            () => File.OpenRead(SelfPath),
            AssemblyResolutionProvenance.Local("test"));

        using var fromRef = AssemblyInspectionSession.Open(reference);
        using var fromPath = AssemblyInspectionSession.Open(SelfPath);

        Assert.True(fromRef.HasMetadata);
        Assert.Equal(fromPath.AssemblyInfo().AssemblyName, fromRef.AssemblyInfo().AssemblyName);
        Assert.Equal(
            fromPath.ApiSurface(includeAll: true).Types.Count,
            fromRef.ApiSurface(includeAll: true).Types.Count);
    }

    [Fact]
    public void AssemblyImage_Open_HasMetadata()
    {
        using var image = AssemblyImage.Open(SelfPath);
        Assert.True(image.HasMetadata);
    }

    [Fact]
    public void AssemblyImage_DisposesBackingStreamExactlyOnce()
    {
        using var stream = new DisposeCountingStream(File.OpenRead(SelfPath));
        var reference = ResolvedAssemblyReference.Create(
            ReadIdentity(SelfPath),
            SelfPath,
            () => stream,
            AssemblyResolutionProvenance.Local("test"));

        var image = AssemblyImage.Open(reference);
        try
        {
            Assert.True(image.HasMetadata);
            Assert.Equal(0, stream.DisposeCount);
        }
        finally
        {
            image.Dispose();
        }

        Assert.Equal(1, stream.DisposeCount);
    }

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments])).Name;

    static void AssertNoMetadata(MetadataImageFormatResult? format) =>
        Assert.IsType<MetadataImageFormatResult.NoMetadata>(format);

    sealed class DisposeCountingStream(Stream inner) : Stream
    {
        bool _innerDisposed;

        public int DisposeCount { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                if (!_innerDisposed)
                {
                    inner.Dispose();
                    _innerDisposed = true;
                }

            }

            base.Dispose(disposing);
        }
    }

    public static class MethodBodyFixture
    {
        public static T Echo<T>(T value) => value;

        public static int Overloaded(int value) => value;

        public static string Overloaded(string value) => value;

        public static int Value { get; set; }

        public static int SetterOnly
        {
            set { }
        }

        public static void Pick()
        {
        }

        public static void pick(int value)
        {
        }

        public static int PickField;

        public static void pickField()
        {
        }

        public static int ValueField;

        public static int valueField { get; set; }
    }
}
