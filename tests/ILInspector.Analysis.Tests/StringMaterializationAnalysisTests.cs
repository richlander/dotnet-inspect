using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using Inspector.Findings;

namespace ILInspector.Analysis.Tests;

public sealed class StringMaterializationAnalysisTests
{
    private static readonly TypeRef String =
        TypeRef.CoreLib("System", "String");
    private static readonly TypeRef Void =
        TypeRef.CoreLib("System", "Void");

    [Theory]
    [InlineData("Concat", StringMaterializationKind.Concatenation)]
    [InlineData("Join", StringMaterializationKind.Join)]
    [InlineData("Format", StringMaterializationKind.Format)]
    [InlineData("Create", StringMaterializationKind.Create)]
    public void Collect_ClassifiesTrustedStringOperations(
        string name,
        StringMaterializationKind expected)
    {
        DirectCall call = Call(
            TypeRef.CoreLib("System", "String"),
            name,
            String,
            CallKind.Call);

        StringMaterializationOccurrence occurrence =
            Assert.Single(
                StringMaterializationAnalysis.Collect([call]));

        Assert.Equal(expected, occurrence.Kind);
        Assert.Equal(call.ILOffset, occurrence.ILOffset);
        Assert.Equal(call.OperandToken, occurrence.OperandToken);
        Assert.Same(call.Caller, occurrence.Method);
        Assert.Same(
            call.EvidenceMethod,
            occurrence.EvidenceMethod);
    }

    [Fact]
    public void Collect_ClassifiesStringConstructor()
    {
        DirectCall call = Call(
            TypeRef.CoreLib("System", "String"),
            ".ctor",
            Void,
            CallKind.NewObject);

        Assert.Equal(
            StringMaterializationKind.Constructor,
            Assert.Single(
                StringMaterializationAnalysis.Collect([call]))
                .Kind);
    }

    [Fact]
    public void Collect_ClassifiesInterpolationBuilderAndDecodeBoundaries()
    {
        DirectCall handler = Call(
            TypeRef.CoreLib(
                "System.Runtime.CompilerServices",
                "DefaultInterpolatedStringHandler"),
            "ToStringAndClear",
            String,
            CallKind.Call);
        DirectCall builder = Call(
            TypeRef.CoreLib("System.Text", "StringBuilder"),
            "ToString",
            String,
            CallKind.CallVirtual) with
        {
            ILOffset = 8,
        };
        DirectCall decode = Call(
            TypeRef.CoreLib("System.Text", "Encoding"),
            "GetString",
            String,
            CallKind.CallVirtual) with
        {
            ILOffset = 12,
        };

        Assert.Equal(
            [
                StringMaterializationKind
                    .InterpolatedStringHandlerFinalization,
                StringMaterializationKind
                    .StringBuilderFinalization,
                StringMaterializationKind.EncodingDecode,
            ],
            StringMaterializationAnalysis
                .Collect([handler, builder, decode])
                .Select(static occurrence => occurrence.Kind));
    }

    [Fact]
    public void Collect_UsesReceiverProvenanceForObjectToString()
    {
        DirectCall constructor = Call(
            TypeRef.CoreLib("System.Text", "StringBuilder"),
            ".ctor",
            Void,
            CallKind.NewObject);
        DirectCall toString = Call(
            TypeRef.CoreLib("System", "Object"),
            "ToString",
            String,
            CallKind.CallVirtual) with
        {
            ILOffset = 12,
            ReceiverSource = new(
                [constructor.ILOffset],
                isComplete: true),
        };

        StringMaterializationOccurrence occurrence =
            Assert.Single(
                StringMaterializationAnalysis.Collect(
                    [constructor, toString]));

        Assert.Equal(
            StringMaterializationKind.StringBuilderFinalization,
            occurrence.Kind);
        Assert.Equal(toString.ILOffset, occurrence.ILOffset);
    }

    [Fact]
    public void Collect_RejectsIncompleteObjectToStringReceiver()
    {
        DirectCall toString = Call(
            TypeRef.CoreLib("System", "Object"),
            "ToString",
            String,
            CallKind.CallVirtual) with
        {
            ReceiverSource = new(
                [],
                isComplete: false),
        };

        Assert.Empty(
            StringMaterializationAnalysis.Collect([toString]));
    }

    [Fact]
    public void Collect_RejectsSpoofedFrameworkIdentity()
    {
        DirectCall spoof = Call(
            TypeRef.Definition(
                "Untrusted",
                "System",
                "String"),
            "Concat",
            String,
            CallKind.Call);

        Assert.Empty(
            StringMaterializationAnalysis.Collect([spoof]));
    }

    [Fact]
    public void InspectStringMaterializations_UsesStableIdentityAndIlOrder()
    {
        StringMaterializationOccurrence later =
            Occurrence(
                Call(
                    TypeRef.CoreLib("System", "String"),
                    "Join",
                    String,
                    CallKind.Call) with
                {
                    ILOffset = 12,
                },
                StringMaterializationKind.Join);
        StringMaterializationOccurrence earlier =
            Occurrence(
                Call(
                    TypeRef.CoreLib("System", "String"),
                    "Concat",
                    String,
                    CallKind.Call),
                StringMaterializationKind.Concatenation);
        var subject = new FindingSubject(
            "method:test",
            "Test.Method()");

        ImmutableArray<
            Finding<StringMaterializationOccurrence>> findings =
                AnalysisFindings
                    .InspectStringMaterializations(
                        [later, earlier],
                        subject);

        Assert.Collection(
            findings,
            finding =>
            {
                Assert.Same(earlier, finding.Payload);
                Assert.Equal(0, finding.Ordinal);
                Assert.Equal(
                    AnalysisFindings
                        .StringMaterializationDescriptor,
                    finding.Descriptor);
            },
            finding =>
            {
                Assert.Same(later, finding.Payload);
                Assert.Equal(1, finding.Ordinal);
            });
    }

    [Fact]
    public void OptimizationOpportunities_ProjectCompiledOperations()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisStringMaterialization
                .AssemblyPath());
        var opportunities = index.OptimizationOpportunities
            .Where(candidate =>
                candidate.Method.DeclaringType.Name
                == "StringMaterializationSamples"
                && candidate.Shape
                    == "string-materialization")
            .ToArray();
        var expected = new Dictionary<string, string>
        {
            ["Concatenate"] =
                "string.concat",
            ["Interpolate"] =
                "string.interpolation-handler",
            ["Join"] =
                "string.join",
            ["Build"] =
                "string.builder-finalization",
            ["BuildStored"] =
                "string.builder-finalization",
            ["Construct"] =
                "string.constructor",
            ["Decode"] =
                "string.encoding-decode",
        };

        foreach ((string methodName, string expectedOperation)
            in expected)
        {
            OptimizationOpportunity opportunity = Assert.Single(
                opportunities,
                candidate =>
                    candidate.Method.Name == methodName);
            Assert.Equal(
                AnalysisFindings
                    .StringMaterializationDescriptor.Id,
                opportunity.SourceFinding);
            Assert.Equal(
                expectedOperation,
                opportunity.Operation);
            Assert.Equal(
                PerformanceTriageProvenance.Exact,
                opportunity.Provenance);
            Assert.NotNull(opportunity.ILOffset);
            Assert.NotNull(opportunity.OperandToken);
            Assert.Null(opportunity.RuntimeAllocationType);
            Assert.False(
                OptimizationOpportunityRanking
                    .IncludeInMemberTriage(opportunity));
        }

        Assert.Contains(
            "(object::ToString)",
            Assert.Single(
                opportunities,
                candidate =>
                    candidate.Method.Name == "BuildStored")
                .Evidence,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            opportunities,
            candidate =>
                candidate.Method.Name is
                    "Encode"
                    or "WriteUtf8");
    }

    private static StringMaterializationOccurrence Occurrence(
        DirectCall call,
        StringMaterializationKind kind)
        => new(
            call.Caller,
            call.EvidenceMethod,
            call.Callee,
            kind,
            call.ILOffset,
            call.OperandToken,
            call.Opcode,
            call.InLoop,
            call.Multiplicity);

    private static DirectCall Call(
        TypeRef declaringType,
        string name,
        TypeRef returnType,
        CallKind kind)
    {
        MethodIdentity method = Method();
        return new(
            method,
            new(
                declaringType,
                name,
                [],
                returnType,
                name == ".ctor"
                    ? MemberKind.Constructor
                    : MemberKind.Method),
            ILOffset: 4,
            OperandToken: 0x0A000001,
            CalleeDefinitionToken: 0x0A000001,
            kind)
        {
            EvidenceMethod = method,
            Opcode = kind == CallKind.NewObject
                ? "newobj"
                : "call",
        };
    }

    private static MethodIdentity Method()
        => new(
            "Fixture",
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"),
            TypeRef.Definition(
                "Fixture",
                "Fixture",
                "TextProducer"),
            "Render",
            [],
            String,
            0x06000001,
            IsStatic: true);
}
