using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public class MetadataDeclarationSessionSubstrateTests
{
    static string SelfPath =>
        typeof(MetadataDeclarationSessionSubstrateTests).Assembly.Location;

    [Fact]
    public void RepeatedWork_UsesOneOpenAndOneImageAdmission()
    {
        int openCount = 0;
        ResolvedAssemblyReference reference = CreateReference(
            () =>
            {
                openCount++;
                return File.OpenRead(SelfPath);
            });

        using var assemblySession = AssemblyInspectionSession.Open(reference);
        using var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);

        Assert.True(operationContext.Counters.MetadataRows > 0);
        var first = Assert.IsType<MetadataImageAdmissionResult.Admitted>(
            declarationSession.ImageAdmission);
        MetadataImageAdmissionResult second =
            declarationSession.ImageAdmission;

        Assert.Same(first, second);
        Assert.Equal(1, openCount);
        Assert.Equal(
            first.ImageMetadataRows,
            operationContext.Counters.MetadataRows);
    }

    [Fact]
    public void DuplicateAttachment_RejectsBeforeImageAdmission()
    {
        using var assemblySession =
            AssemblyInspectionSession.Open(SelfPath);
        using var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);

        long admittedRows = operationContext.Counters.MetadataRows;
        Assert.True(admittedRows > 0);
        Assert.Throws<InvalidOperationException>(
            () => assemblySession.CreateDeclarationSession(operationContext));
        Assert.Equal(
            admittedRows,
            operationContext.Counters.MetadataRows);

        var admitted =
            Assert.IsType<MetadataImageAdmissionResult.Admitted>(
                declarationSession.ImageAdmission);
        Assert.Equal(
            admitted.ImageMetadataRows,
            operationContext.Counters.MetadataRows);
    }

    [Fact]
    public void OneOperation_AggregatesIndependentSnapshotSessions()
    {
        AssemblyImageSnapshot snapshot = CreateSnapshot();
        long imageMetadataRows = CountMetadataRows(SelfPath);
        using var firstAssembly = AssemblyInspectionSession.Open(snapshot);
        using var secondAssembly = AssemblyInspectionSession.Open(snapshot);
        using var operationContext = new MetadataOperationContext(
            new MetadataOperationPolicy(imageMetadataRows * 2));
        using var firstDeclaration =
            firstAssembly.CreateDeclarationSession(operationContext);
        using var secondDeclaration =
            secondAssembly.CreateDeclarationSession(operationContext);

        var first = Assert.IsType<MetadataImageAdmissionResult.Admitted>(
            firstDeclaration.ImageAdmission);
        var second = Assert.IsType<MetadataImageAdmissionResult.Admitted>(
            secondDeclaration.ImageAdmission);

        Assert.Equal(imageMetadataRows, first.ImageMetadataRows);
        Assert.Equal(imageMetadataRows, second.ImageMetadataRows);
        Assert.Equal(
            imageMetadataRows * 2,
            operationContext.Counters.MetadataRows);
    }

    [Fact]
    public void FinitePolicy_PostsDetachedTypedFailure()
    {
        var assemblySession = AssemblyInspectionSession.Open(SelfPath);
        var operationContext = new MetadataOperationContext(
            new MetadataOperationPolicy(maxMetadataRows: 0));
        var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);

        var rejected = Assert.IsType<MetadataImageAdmissionResult.Rejected>(
            declarationSession.ImageAdmission);

        declarationSession.Dispose();
        operationContext.Dispose();
        assemblySession.Dispose();

        Assert.Equal(
            MetadataOperationFailureKind.MetadataRowsExceeded,
            rejected.Failure.Kind);
        Assert.True(rejected.Failure.ImageMetadataRows > 0);
        Assert.Equal(0, rejected.Failure.MaxMetadataRows);
        Assert.Equal(0, rejected.Failure.Counters.MetadataRows);
    }

    [Fact]
    public void PostedSuccess_RemainsUsableAfterRetirement()
    {
        var assemblySession = AssemblyInspectionSession.Open(SelfPath);
        var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);

        var admitted = Assert.IsType<MetadataImageAdmissionResult.Admitted>(
            declarationSession.ImageAdmission);

        declarationSession.Dispose();
        operationContext.Dispose();
        assemblySession.Dispose();

        Assert.True(admitted.ImageMetadataRows > 0);
        Assert.Equal(
            admitted.ImageMetadataRows,
            admitted.Counters.MetadataRows);
    }

    [Fact]
    public void CachedAccess_RequiresLiveAssemblyOwnerAndOperation()
    {
        var assemblySession = AssemblyInspectionSession.Open(SelfPath);
        var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);
        _ = declarationSession.ImageAdmission;

        assemblySession.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => declarationSession.ImageAdmission);

        declarationSession.Dispose();
        operationContext.Dispose();

        assemblySession = AssemblyInspectionSession.Open(SelfPath);
        operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);
        _ = declarationSession.ImageAdmission;

        operationContext.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => declarationSession.ImageAdmission);

        declarationSession.Dispose();
        assemblySession.Dispose();
    }

    [Fact]
    public void NewDeclarationConstruction_RequiresLiveOwnerContextAndLender()
    {
        var assemblySession = AssemblyInspectionSession.Open(SelfPath);
        using var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        assemblySession.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => assemblySession.CreateDeclarationSession(operationContext));

        assemblySession = AssemblyInspectionSession.Open(SelfPath);
        var disposedOperation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        disposedOperation.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => assemblySession.CreateDeclarationSession(
                disposedOperation));
        assemblySession.Dispose();

        var lender = PdbContext.Open(SelfPath);
        assemblySession = AssemblyInspectionSession.Borrow(lender);
        lender.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => assemblySession.CreateDeclarationSession(operationContext));
        assemblySession.Dispose();
    }

    [Fact]
    public void BorrowedSession_RequiresLiveLenderForCachedAccess()
    {
        var lender = PdbContext.Open(SelfPath);
        var assemblySession = AssemblyInspectionSession.Borrow(lender);
        var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);
        _ = declarationSession.ImageAdmission;

        lender.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => declarationSession.ImageAdmission);

        declarationSession.Dispose();
        operationContext.Dispose();
        assemblySession.Dispose();
    }

    [Fact]
    public void DeclarationRetirement_LeavesOwnersUsable()
    {
        using var lender = PdbContext.Open(SelfPath);
        var assemblySession = AssemblyInspectionSession.Borrow(lender);
        var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);
        _ = declarationSession.ImageAdmission;

        declarationSession.Dispose();

        Assert.True(assemblySession.HasMetadata);
        Assert.NotNull(lender.ExtractAssemblyInfo().AssemblyName);
        Assert.Throws<ObjectDisposedException>(
            () => declarationSession.ImageAdmission);
        Assert.True(operationContext.Counters.MetadataRows > 0);

        operationContext.Dispose();
        assemblySession.Dispose();

        Assert.NotNull(lender.ExtractAssemblyInfo().AssemblyName);
    }

    [Fact]
    public void ExercisingCoordinator_RetiresInOwnerOrder()
    {
        var coordinator = MetadataOperationHarness.Open(SelfPath);
        _ = coordinator.DeclarationSession.ImageAdmission;

        coordinator.Dispose();

        Assert.Equal(
            ["declaration", "operation", "assembly"],
            coordinator.RetirementTrace);
    }

    [Fact]
    public void OwnedSession_DisposesImageExactlyOnce()
    {
        using var stream =
            new DisposeCountingStream(File.OpenRead(SelfPath));
        ResolvedAssemblyReference reference = CreateReference(() => stream);
        var assemblySession = AssemblyInspectionSession.Open(reference);
        var operationContext = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);
        _ = declarationSession.ImageAdmission;

        declarationSession.Dispose();
        operationContext.Dispose();
        assemblySession.Dispose();
        assemblySession.Dispose();

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void Factory_OwnsConstructionAndRequiresOperationContext()
    {
        using var assemblySession =
            AssemblyInspectionSession.Open(SelfPath);
        using var operationContext =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarationSession =
            assemblySession.CreateDeclarationSession(operationContext);

        Assert.NotNull(declarationSession.ImageAdmission);
        Assert.Empty(
            typeof(MetadataDeclarationSession).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void PostedResultShapes_DoNotCarryLiveAuthority()
    {
        Type[] postedTypes =
        [
            typeof(MetadataImageAdmissionResult.Admitted),
            typeof(MetadataImageAdmissionResult.Rejected),
            typeof(MetadataOperationFailure),
            typeof(MetadataOperationCounters),
            typeof(MetadataMethodSemanticsAssociation),
            typeof(MetadataMethodSemanticsFailure),
            typeof(MetadataMethodSemanticsAssociationResult.Completed),
            typeof(MetadataMethodSemanticsAssociationResult.Rejected),
            typeof(MetadataMethodImplementationResult.Related),
            typeof(MetadataMethodImplementationResult.Absent),
            typeof(MetadataMethodImplementationResult.Rejected),
            typeof(MetadataMethodImplementationFailure),
            typeof(MetadataMethodImplementationCertificate),
            typeof(MetadataMethodSignatureIdentity),
            typeof(MetadataTypeScopeIdentity),
            typeof(MetadataNamedTypeIdentity),
            typeof(MetadataDeclarationDefinitionDisposition.LocalResolved),
            typeof(MetadataDeclarationDefinitionDisposition.ExternalUnresolved),
        ];
        Type[] forbiddenTypes =
        [
            typeof(MetadataReader),
            typeof(PEReader),
            typeof(AssemblyImage),
            typeof(AssemblyInspectionSession),
            typeof(MetadataDeclarationSession),
            typeof(MetadataOperationContext),
            typeof(Stream),
        ];

        foreach (Type postedType in postedTypes)
        {
            Assert.DoesNotContain(
                postedType.GetProperties(),
                property =>
                    forbiddenTypes.Contains(property.PropertyType)
                    || typeof(IDisposable).IsAssignableFrom(
                        property.PropertyType));
        }
    }

    static AssemblyImageSnapshot CreateSnapshot()
    {
        ImmutableArray<byte> content =
            ImmutableArray.Create(File.ReadAllBytes(SelfPath));
        var ready = Assert.IsType<AssemblyImageSnapshotResult.Ready>(
            AssemblyImageSnapshot.FromRetainedContent(
                CreateReference(
                    () => new MemoryStream(
                        content.AsSpan().ToArray(),
                        writable: false)),
                content));
        return ready.Snapshot;
    }

    static ResolvedAssemblyReference CreateReference(
        Func<Stream> openRead) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(SelfPath),
            SelfPath,
            openRead,
            AssemblyResolutionProvenance.Local(
                "metadata declaration session test"));

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    static long CountMetadataRows(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        return Enum.GetValues<TableIndex>()
            .Sum(table => (long)reader.GetTableRowCount(table));
    }

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

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            inner.SetLength(value);

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Write(buffer, offset, count);

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

    sealed class MetadataOperationHarness : IDisposable
    {
        readonly AssemblyInspectionSession _assemblySession;
        readonly MetadataOperationContext _operationContext;
        bool _disposed;

        MetadataOperationHarness(AssemblyInspectionSession assemblySession)
        {
            _assemblySession = assemblySession;
            _operationContext = new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
            DeclarationSession =
                assemblySession.CreateDeclarationSession(_operationContext);
        }

        public MetadataDeclarationSession DeclarationSession { get; }

        public List<string> RetirementTrace { get; } = [];

        public static MetadataOperationHarness Open(string path) =>
            new(AssemblyInspectionSession.Open(path));

        public void Dispose()
        {
            if (_disposed)
                return;

            DeclarationSession.Dispose();
            _ = _operationContext.Counters;
            _ = _assemblySession.HasMetadata;
            RetirementTrace.Add("declaration");

            _operationContext.Dispose();
            _ = _assemblySession.HasMetadata;
            RetirementTrace.Add("operation");

            _assemblySession.Dispose();
            RetirementTrace.Add("assembly");
            _disposed = true;
        }
    }
}
