using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

// Gates for Finding Unsafe Any: docs/design/unsafe-code-section.md#finding-unsafe-any.
public sealed partial class UnsafeMemberFindingsTests
{
    // Each Type is Unsafe exactly when the Library census has a finding it
    // declares, and the answer's limitations are the census's limitations
    // outside those findings' source Types.
    [Theory]
    [InlineData("new")]
    [InlineData("legacy")]
    [InlineData("analysis")]
    [InlineData("iterators")]
    public void FindingUnsafeAnyListsExactlyTheCensusTypes(string fixture)
    {
        string path = fixture switch
        {
            "new" => UnsafeFixturePath(legacy: false),
            "legacy" => UnsafeFixturePath(legacy: true),
            "analysis" => AnalysisFixturePath,
            _ => IteratorFixturePath,
        };
        UnsafeMemberCensus census = LibraryCensus(path);
        (FindingUnsafeAnyResult.Available any, _) = FindingUnsafeAnyOf(path);

        ImmutableHashSet<TypeRef> censusTypes =
        [
            .. UnsafeMemberTypeGroup.Group(census.Members)
                .Select(group => group.DeclaringType),
        ];
        Assert.NotEmpty(censusTypes);
        Assert.Equal(
            censusTypes,
            any.Types.Types
                .Where(type => type.State == UnsafeCodeTypeState.Unsafe)
                .Select(type => type.DeclaringType)
                .ToImmutableHashSet());
        Assert.DoesNotContain(
            any.Types.Types,
            type => type.State == UnsafeCodeTypeState.Unknown);
        Dictionary<int, TypeDefinitionHandle> sourceTypes = SourceTypeOfEachMethod(path);
        HashSet<TypeDefinitionHandle> settled =
        [
            .. census.Members.Select(finding => sourceTypes[finding.Member.MetadataToken]),
        ];
        // Each Type names its own definition, so a host can navigate to it.
        Assert.Equal(
            UnsafeMemberTypeGroup.Group(census.Members).ToDictionary(
                group => group.DeclaringType,
                group => (int?)MetadataTokens.GetToken(sourceTypes[group.Members[0].Member.MetadataToken])),
            any.Types.Types.ToDictionary(type => type.DeclaringType, type => type.MetadataToken));
        Assert.Equal(
            census.Limitations.Where(limitation =>
                (limitation.DeclaredMember?.MetadataToken ?? limitation.BodyToken) is not { } token
                || !settled.Contains(sourceTypes[token])),
            any.Types.Limitations);
    }

    static Dictionary<int, TypeDefinitionHandle> SourceTypeOfEachMethod(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var sourceTypes = new Dictionary<int, TypeDefinitionHandle>();
        foreach (TypeDefinitionHandle type in reader.TypeDefinitions)
        {
            TypeDefinitionHandle source = MethodDefinitionShapes.SourceType(reader, type);
            foreach (MethodDefinitionHandle method in reader.GetTypeDefinition(type).GetMethods())
                sourceTypes[MetadataTokens.GetToken(method)] = source;
        }

        return sourceTypes;
    }

    // Settlement skips a settled Type's later bodies and its nested
    // compiler-generated Types: Finding Unsafe Any attempts fewer bodies than
    // the census over a library whose Types have several unsafe members.
    [Fact]
    public void FindingUnsafeAnyAttemptsFewerBodiesThanTheCensus()
    {
        string path = UnsafeFixturePath(legacy: false);
        (_, WorkReceipt anyReceipt) = FindingUnsafeAnyOf(path);
        var census = Assert.IsType<UnsafeMemberCensusResult.Available>(
            LibraryScopeCensus(path));

        int attempted = anyReceipt.For(FindingUnsafeAnyProducer.Instance).UnitsAttempted;
        int censusAttempted =
            census.Receipt.For(UnsafeMemberCensusProducer.Instance).UnitsAttempted;
        Assert.InRange(attempted, 1, censusAttempted - 1);
    }

    // A limitation in a Type with a finding cannot change its answer, so
    // the answer drops it, whether it names a member or only a body; one in
    // a Type with no finding leaves that Type Unknown when its member is
    // known, and stays an unsettled limitation otherwise.
    [Fact]
    public void FindingUnsafeAnyKeepsOnlyLimitationsThatLeaveATypeUnsettled()
    {
        TypeRef reader = TypeRef.Definition("Test", "Sample", "Reader");
        TypeRef writer = TypeRef.Definition("Test", "Sample", "Writer");
        MethodIdentity read = Method(reader, "Read", 0x06000001);
        MethodIdentity readCopy = Method(reader, "Copy", 0x06000002);
        MethodIdentity write = Method(writer, "Write", 0x06000003);
        MethodIdentity lambda = Method(
            TypeRef.Definition("Test", "Sample", "<>c"),
            "<Write>b__0_0",
            0x06000004);
        var readerHandle = MetadataTokens.TypeDefinitionHandle(2);
        var writerHandle = MetadataTokens.TypeDefinitionHandle(3);
        UnsafeMemberLimitation Failed(MethodIdentity member) =>
            new(UnsafeMemberLimitationReason.BodyAnalysisFailed, member.MetadataToken, member, member, []);
        UnsafeMemberLimitation unattributed = new(
            UnsafeMemberLimitationReason.UnattributedGeneratedBody,
            lambda.MetadataToken,
            lambda,
            DeclaredMember: null,
            []);
        MethodIdentity readLambda = Method(
            TypeRef.Definition("Test", "Sample", "<>c"),
            "<Read>b__0_0",
            0x06000005);
        UnsafeMemberLimitation settledUnattributed = new(
            UnsafeMemberLimitationReason.UnattributedGeneratedBody,
            readLambda.MetadataToken,
            readLambda,
            DeclaredMember: null,
            []);
        UnsafeMemberLimitation image = new(
            UnsafeMemberLimitationReason.ReferenceAssemblyUndecidable,
            BodyToken: null,
            Body: null,
            DeclaredMember: null,
            []);
        var census = new UnsafeMemberCensus(
            [
                new UnsafeMemberFinding(
                    read,
                    HasExplicitUnsafeContract: false,
                    [new UnsafeMemberFindingEvidence(UnsafeMemberUseKind.PointerDereference, read, 0, "ldind.i4")],
                    [],
                    UnsafeMemberExposure.NonPublic),
            ],
            [Failed(readCopy), Failed(write), unattributed, settledUnattributed, image]);

        UnsafeCodeTypes answer = FindingUnsafeAnyProducer.Answer(
            census,
            new Dictionary<int, TypeDefinitionHandle>
            {
                [read.MetadataToken] = readerHandle,
                [readCopy.MetadataToken] = readerHandle,
                [write.MetadataToken] = writerHandle,
                [lambda.MetadataToken] = writerHandle,
                [readLambda.MetadataToken] = readerHandle,
            });

        Assert.Equal(
            [
                new UnsafeCodeType(reader, UnsafeCodeTypeState.Unsafe, MetadataTokens.GetToken(readerHandle)),
                new UnsafeCodeType(writer, UnsafeCodeTypeState.Unknown, MetadataTokens.GetToken(writerHandle)),
            ],
            answer.Types);
        Assert.Equal([Failed(write), unattributed, image], answer.Limitations);
        Assert.False(answer.IsComplete);
    }

    static MethodIdentity Method(TypeRef type, string name, int token) =>
        new(
            "Test",
            Guid.Empty,
            type,
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            token,
            IsStatic: true);

    static (FindingUnsafeAnyResult.Available Answer, WorkReceipt Receipt) FindingUnsafeAnyOf(
        string path)
    {
        var operation = AssemblyAnalysisOperation<UnsafeCodeTypes>.Create(
            path,
            FindingUnsafeAny.ForLibrary().AsAssemblyAnalysisSource());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(File.ReadAllBytes(path), writable: false));
        AssemblyAnalysisExecution<UnsafeCodeTypes> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<UnsafeCodeTypes>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access => AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
                .Execution;
        var answer = Assert.IsType<FindingUnsafeAnyResult.Available>(
            FindingUnsafeAny.Project(execution));
        return (answer, answer.Receipt);
    }
}
