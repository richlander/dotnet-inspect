using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextMethodAddressQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IssuesExactOwnerAddressIncludingBodyless(bool bodyless)
    {
        using var fixture = new Fixture();
        var method = bodyless
            ? typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!
            : typeof(string).GetMethod(nameof(string.ToString), Type.EmptyTypes)!;
        var address = Assert.IsType<AssemblyContextEntry<MetadataMethodAddress>.Available>(
            AssemblyContextMethodAddressQuery.ExecuteParticipant(
                fixture.Group, fixture.Participant, method.MetadataToken)).Value;
        Assert.Equal(method.MetadataToken, address.Token);
        Assert.Equal(method.Module.ModuleVersionId, address.ModuleVersionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x02000001)]
    [InlineData(0x06000000)]
    [InlineData(0x06ffffff)]
    [InlineData(-1)]
    public void InvalidMethodDefIsFailed(int token)
    {
        using var fixture = new Fixture();
        var failed = Assert.IsType<AssemblyContextEntry<MetadataMethodAddress>.Failed>(
            AssemblyContextMethodAddressQuery.ExecuteParticipant(fixture.Group, fixture.Participant, token));
        Assert.Contains("not a MethodDef", failed.Error.Message);
    }

    [Fact]
    public void MissingImageIsRejected()
    {
        using var fixture = new Fixture(unavailable: true);
        Assert.IsType<AssemblyContextEntry<MetadataMethodAddress>.Rejected>(
            AssemblyContextMethodAddressQuery.ExecuteParticipant(fixture.Group, fixture.Participant, 0x06000001));
    }

    [Fact]
    public void ForeignParticipantCannotBorrowAnotherContext()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        Assert.Throws<ArgumentException>(() => AssemblyContextMethodAddressQuery.ExecuteParticipant(
            first.Group, second.Participant, 0x06000001));
    }

    [Fact]
    public void DisposedContextPreservesExistingAccessFailure()
    {
        using var fixture = new Fixture();
        fixture.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            AssemblyContextMethodAddressQuery.ExecuteParticipant(
                fixture.Group, fixture.Participant, 0x06000001));
    }

    sealed class Fixture : IDisposable
    {
        readonly InspectionWorkspace _workspace = new();
        internal Fixture(bool unavailable = false)
        {
            byte[] image = File.ReadAllBytes(typeof(string).Assembly.Location);
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            Participant = new(ResolvedAssemblyReference.Create(
                AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()), null,
                () => unavailable ? throw new FileNotFoundException("Fixture image unavailable.")
                    : new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local("method-address-fixture")), new MissingPolicy());
            Group = _workspace.CreateAssemblyContextGroup([Participant]);
        }
        internal AssemblyContextGroup Group { get; }
        internal AssemblyContextParticipant Participant { get; }
        public void Dispose() => _workspace.Dispose();
    }

    sealed class MissingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
            new(Version, AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
