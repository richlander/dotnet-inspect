using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Findings;

namespace ILInspector.ILDiff.Tests;

public class IlMemberEndpointComparisonTests
{
    [Fact]
    public void CompareMemberEndpoints_BodyfulPair_RetainsFindingAndNativeResults()
    {
        using var image = OpenCurrentAssembly();
        var method = image.FindMethod(nameof(MemberA));
        var legacy = IlAssemblyDiff.CompareMembers(
            image.Pe,
            image.Reader,
            method,
            image.Pe,
            image.Reader,
            method);

        var result = IlAssemblyDiff.CompareMemberEndpoints(
            Present(legacy.Old, image, method),
            Present(legacy.New, image, method));

        var comparison = Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.New);
        Assert.Equal(legacy.Old, result.Old);
        Assert.Equal(legacy.New, result.New);
        Assert.NotNull(result.MemberDiff);
        Assert.Equal(legacy.Old, result.MemberDiff.Old);
        Assert.Equal(legacy.New, result.MemberDiff.New);
        Assert.Equal(legacy.Diff, result.MemberDiff.Diff);
        Assert.Empty(result.MemberDiff.IdentityFailures);
    }

    [Fact]
    public void CompareMemberEndpoints_BodyfulAndBodyless_UsesNoApplicableInputWithoutPairDiff()
    {
        using var image = OpenCurrentAssembly();
        var bodyful = image.FindMethod(nameof(MemberA));
        var bodyless = image.FindMethod(nameof(Bodyless.Missing));

        var result = IlAssemblyDiff.CompareMemberEndpoints(
            Present(Subject("old"), image, bodyful),
            Present(Subject("new"), image, bodyless));

        var comparison = Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.NoApplicableInput, comparison.Transition.New);
        Assert.Null(result.MemberDiff);
    }

    [Fact]
    public void CompareMemberEndpoints_BodyfulAndSubjectAbsent_RetainsExplicitAbsenceWithoutPairDiff()
    {
        using var image = OpenCurrentAssembly();
        var bodyful = image.FindMethod(nameof(MemberA));
        var absentSubject = Subject("new");

        var result = IlAssemblyDiff.CompareMemberEndpoints(
            Present(Subject("old"), image, bodyful),
            new IlMemberDiffEndpoint.SubjectAbsent(absentSubject, "Exact subject is absent."));

        var comparison = Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.SubjectAbsent, comparison.Transition.New);
        var absent = Assert.IsType<FindingInspection<CanonicalIlOperation>.Absent>(
            result.Findings.NewInspection.Value);
        Assert.Equal(FindingInspectionAbsenceKind.SubjectAbsent, absent.Kind);
        Assert.Equal("Exact subject is absent.", absent.Detail);
        Assert.Equal(absentSubject, result.New);
        Assert.Null(result.MemberDiff);
    }

    [Fact]
    public void CompareMemberEndpoints_BothSubjectAbsent_IsExactWithoutPairDiff()
    {
        var oldSubject = Subject("old");
        var newSubject = Subject("new");

        var result = IlAssemblyDiff.CompareMemberEndpoints(
            new IlMemberDiffEndpoint.SubjectAbsent(oldSubject),
            new IlMemberDiffEndpoint.SubjectAbsent(newSubject));

        var comparison = Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.SubjectAbsent, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.SubjectAbsent, comparison.Transition.New);
        Assert.True(result.Findings.IsExact);
        Assert.Equal(oldSubject, result.Old);
        Assert.Equal(newSubject, result.New);
        Assert.Null(result.MemberDiff);
    }

    [Fact]
    public void CompareMemberEndpoints_DecodeFailure_RetainsFailedInspectionWithoutPairDiff()
    {
        using var failedImage = new AssemblyImage(BuildDecodeFailureImage());
        using var validImage = OpenCurrentAssembly();

        var result = IlAssemblyDiff.CompareMemberEndpoints(
            Present(Subject("old"), failedImage, failedImage.FindMethod("M")),
            Present(Subject("new"), validImage, validImage.FindMethod(nameof(MemberA))));

        Assert.IsType<FindingComparison<CanonicalIlOperation>.Failed>(result.Findings.Value);
        var failed = Assert.IsType<FindingInspection<CanonicalIlOperation>.Failed>(
            result.Findings.OldInspection.Value);
        Assert.NotEmpty(failed.Error.Reason);
        Assert.IsType<FindingInspection<CanonicalIlOperation>.Complete>(
            result.Findings.NewInspection.Value);
        Assert.Null(result.MemberDiff);
    }

    [Fact]
    public void PresentEndpoint_RejectsNullAndNilEvidence()
    {
        using var image = OpenCurrentAssembly();
        var subject = Subject("member");
        var method = image.FindMethod(nameof(MemberA));

        Assert.Throws<ArgumentNullException>(
            () => new IlMemberDiffEndpoint.Present(null!, image.Pe, image.Reader, method));
        Assert.Throws<ArgumentNullException>(
            () => new IlMemberDiffEndpoint.Present(subject, null!, image.Reader, method));
        Assert.Throws<ArgumentNullException>(
            () => new IlMemberDiffEndpoint.Present(subject, image.Pe, null!, method));
        Assert.Throws<ArgumentException>(
            () => new IlMemberDiffEndpoint.Present(subject, image.Pe, image.Reader, default));
        Assert.Throws<ArgumentNullException>(
            () => new IlMemberDiffEndpoint.SubjectAbsent(null!));
        Assert.Throws<ArgumentNullException>(
            () => IlAssemblyDiff.CompareMemberEndpoints(
                null!,
                new IlMemberDiffEndpoint.SubjectAbsent(subject)));
        Assert.Throws<ArgumentNullException>(
            () => IlAssemblyDiff.CompareMemberEndpoints(
                new IlMemberDiffEndpoint.SubjectAbsent(subject),
                null!));
    }

    static IlMemberDiffEndpoint.Present Present(
        IlMemberDiffSubject subject,
        AssemblyImage image,
        MethodDefinitionHandle method)
        => new(subject, image.Pe, image.Reader, method);

    static IlMemberDiffSubject Subject(string identity)
        => new(identity, identity);

    static AssemblyImage OpenCurrentAssembly()
        => new(File.ReadAllBytes(typeof(IlMemberEndpointComparisonTests).Assembly.Location));

    static int MemberA() => 1;

    abstract class Bodyless
    {
        public abstract void Missing();
    }

    static byte[] BuildDecodeFailureImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(instructions, new ControlFlowBuilder());
        instructions.WriteByte(0xff);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            VoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobHandle VoidMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
        return metadata.GetOrAddBlob(signature);
    }

    sealed class AssemblyImage : IDisposable
    {
        readonly MemoryStream _stream;

        public AssemblyImage(byte[] bytes)
        {
            _stream = new MemoryStream(bytes);
            Pe = new PEReader(_stream);
            Reader = Pe.GetMetadataReader();
        }

        public PEReader Pe { get; }
        public MetadataReader Reader { get; }

        public MethodDefinitionHandle FindMethod(string name)
        {
            foreach (var handle in Reader.MethodDefinitions)
            {
                if (Reader.GetString(Reader.GetMethodDefinition(handle).Name) == name)
                    return handle;
            }

            throw new InvalidOperationException($"Method {name} was not found.");
        }

        public void Dispose()
        {
            Pe.Dispose();
            _stream.Dispose();
        }
    }
}
