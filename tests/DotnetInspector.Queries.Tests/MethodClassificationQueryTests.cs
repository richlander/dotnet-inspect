using ILInspector.Metadata.LegacyOracles;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using InertText;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Release gates for the host-neutral method classification queries
/// (docs/design/method-classification-analyzers.md#queries-and-demand):
/// equivalence with <see cref="LegacyMethodClassificationScanner.Scan(PEReader)"/>
/// on every repository fixture assembly and on real platform assemblies,
/// row orders, closings, and combined requests.
/// </summary>
public sealed class MethodClassificationQueryTests
{
    static readonly FindingSubject Subject = new("fixture.dll", "fixture.dll");

    /// <summary>
    /// Fixtures where reading classic async from StateMachineRelationshipIndex
    /// departs from legacy's attribute-name match. A rejected relationship
    /// (a null state-machine type) fails the async analyzer; an attribute
    /// naming a type that is not the method's state machine is not async.
    /// Any other departure fails these gates.
    /// </summary>
    static readonly Dictionary<string, string?> AsyncDepartures = new(StringComparer.Ordinal)
    {
        [FixtureIds.AnalysisAsyncSiblingFriend] = null,
        [FixtureIds.AnalysisLookalike] = null,
        [FixtureIds.AnalysisSpoofSystemRuntime] = "System.AsyncAttributeSpoofer::Analyze",
    };

    static bool IsRejectedDeparture(string path) =>
        FixtureIdFor(path) is { } id && AsyncDepartures.TryGetValue(id, out string? notAsync) && notAsync is null;

    /// <summary>Legacy rows, minus the attribute-only async rows the index does not confirm.</summary>
    static List<ClassifiedMethodInfo> ConfirmedLegacy(string path, List<ClassifiedMethodInfo> legacy)
    {
        if (FixtureIdFor(path) is not { } id || !AsyncDepartures.TryGetValue(id, out string? notAsync) || notAsync is null)
            return legacy;
        return legacy
            .Where(row => !(row.Classification == MethodClassification.StateMachineAsync
                && $"{row.DeclaringType}::{row.MethodName}" == notAsync))
            .ToList();
    }

    /// <summary>
    /// Real assets: every built repository fixture, the platform assemblies
    /// this suite runs on, and this test assembly.
    /// </summary>
    public static TheoryData<string> Assemblies()
    {
        var data = new TheoryData<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FixtureDefinition fixture in FixtureCatalog.All)
        {
            string path;
            try
            {
                path = fixture.AssemblyPath();
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or NotSupportedException)
            {
                continue;
            }

            if (File.Exists(path) && seen.Add(path))
                data.Add(path);
        }

        foreach (Type type in new[]
                 {
                     typeof(object),
                     typeof(System.Text.Json.JsonSerializer),
                     typeof(System.Runtime.InteropServices.Marshal),
                     typeof(System.Net.Http.HttpClient),
                     typeof(System.Linq.Enumerable),
                     typeof(MethodClassificationQueryTests),
                 })
        {
            if (seen.Add(type.Assembly.Location))
                data.Add(type.Assembly.Location);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Equivalence_MergedRowsEqualTheLegacyScan(string path)
    {
        using var legacyReader = new PEReader(File.OpenRead(path));
        List<ClassifiedMethodInfo> legacy;
        try
        {
            legacy = LegacyMethodClassificationScanner.Scan(legacyReader);
        }
        catch (BadImageFormatException)
        {
            // A legacy budget failure fails every question; the new request must abort too.
            using var abortReader = new PEReader(File.OpenRead(path));
            MethodClassificationResult aborted = MethodClassificationQuery.Execute(abortReader, [], Subject);
            Assert.NotNull(aborted.Critical);
            return;
        }

        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [], Subject);
        Assert.Null(result.Critical);

        if (result.MergedRows.IsDefault)
        {
            // A rejected state-machine relationship fails the async analyzer
            // instead of counting as async.
            Assert.True(IsRejectedDeparture(path), $"Unexpected async failure in {FixtureIdFor(path) ?? path}.");
            return;
        }

        legacy = ConfirmedLegacy(path, legacy);

        Assert.Equal(legacy.Count, result.MergedRows.Length);
        for (int i = 0; i < legacy.Count; i++)
        {
            ClassifiedMethodInfo expected = legacy[i];
            ClassifiedMethodRow actual = result.MergedRows[i];
            Assert.Equal(expected.MethodName, actual.MethodName.ToString());
            Assert.Equal(expected.DeclaringType, actual.DeclaringType.ToString());
            Assert.Equal(expected.Namespace, actual.Namespace.ToString());
            Assert.Equal(expected.Signature, actual.Signature.ToString());
            Assert.Equal(expected.Classification, actual.Classification);
            Assert.Equal(expected.ModuleName, actual.ModuleName?.ToString());
            Assert.Equal(expected.Anchor, actual.Anchor?.Key);
            Assert.Equal(expected.ReturnType, actual.ReturnType?.ToString());
        }

        Assert.Equal(
            Payloads(MetadataFindings.InspectClassifiedMethods(legacy, Subject)),
            Payloads(result.Finding!));
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Equivalence_EveryClosingAndOrderEqualsLegacyAsConsumersSortedIt(string path)
    {
        List<ClassifiedMethodInfo> legacy;
        using (var legacyReader = new PEReader(File.OpenRead(path)))
        {
            try
            {
                legacy = LegacyMethodClassificationScanner.Scan(legacyReader);
            }
            catch (BadImageFormatException)
            {
                return;
            }
        }

        ClassificationQuestion[] questions =
        [
            new(MethodClassificationAnalyzer.PInvoke, ClassificationClosing.Rows, ClassifiedRowOrder.Model),
            new(MethodClassificationAnalyzer.PInvoke, ClassificationClosing.Rows, ClassifiedRowOrder.Display),
            new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows, ClassifiedRowOrder.Model),
            new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows, ClassifiedRowOrder.Display),
            new(MethodClassificationAnalyzer.PointerSignature, ClassificationClosing.Rows, ClassifiedRowOrder.Model),
            new(MethodClassificationAnalyzer.PInvoke, ClassificationClosing.Count),
            new(MethodClassificationAnalyzer.Async, ClassificationClosing.Count),
            new(MethodClassificationAnalyzer.PointerSignature, ClassificationClosing.Exists),
        ];
        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, questions);
        if (result.AnswerTo(questions[2]) is ClassificationAnswer.Failed)
        {
            Assert.True(IsRejectedDeparture(path), $"Unexpected async failure in {FixtureIdFor(path) ?? path}.");
            return;
        }

        legacy = ConfirmedLegacy(path, legacy);

        // Legacy orders, exactly as LibraryMetadataService and LibraryInspectionView sorted them.
        var pinvoke = legacy.Where(static m => m.Classification == MethodClassification.PInvoke)
            .OrderBy(static m => m.DeclaringType).ThenBy(static m => m.MethodName).ToList();
        var async = legacy.Where(static m => m.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync)
            .OrderBy(static m => MethodClassificationQuery.AsyncKind(m.Classification), StringComparer.Ordinal)
            .ThenBy(static m => m.DeclaringType).ThenBy(static m => m.MethodName).ToList();
        var pointer = legacy.Where(static m => m.Classification == MethodClassification.Unsafe)
            .OrderBy(static m => m.DeclaringType).ThenBy(static m => m.MethodName).ToList();
        var pinvokeDisplay = pinvoke
            .OrderBy(static m => m.DeclaringType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static m => m.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static m => m.Signature, StringComparer.OrdinalIgnoreCase).ToList();
        var asyncDisplay = async
            .OrderBy(static m => m.DeclaringType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static m => m.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static m => m.Signature, StringComparer.OrdinalIgnoreCase).ToList();

        AssertRows(pinvoke, result.AnswerTo(questions[0]));
        AssertRows(pinvokeDisplay, result.AnswerTo(questions[1]));
        AssertRows(async, result.AnswerTo(questions[2]));
        AssertRows(asyncDisplay, result.AnswerTo(questions[3]));
        AssertRows(pointer, result.AnswerTo(questions[4]));
        Assert.Equal(new ClassificationAnswer.Count(pinvoke.Count), result.AnswerTo(questions[5]));
        Assert.Equal(new ClassificationAnswer.Count(async.Count), result.AnswerTo(questions[6]));
        Assert.Equal(new ClassificationAnswer.Exists(pointer.Count > 0), result.AnswerTo(questions[7]));
    }

    [Fact]
    public void Signals_CountsOnlyEqualLegacyAndDecodeNoIdentityText()
    {
        string path = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var legacyReader = new PEReader(File.OpenRead(path));
        List<ClassifiedMethodInfo> legacy = LegacyMethodClassificationScanner.Scan(legacyReader);
        ClassificationQuestion[] signals =
        [
            new(MethodClassificationAnalyzer.PointerSignature, ClassificationClosing.Count),
            new(MethodClassificationAnalyzer.PInvoke, ClassificationClosing.Count),
        ];

        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, signals);

        int pointerCount = legacy.Count(static m => m.Classification == MethodClassification.Unsafe);
        Assert.True(pointerCount > 1);
        Assert.Equal(new ClassificationAnswer.Count(pointerCount), result.AnswerTo(signals[0]));
        Assert.Equal(
            new ClassificationAnswer.Count(legacy.Count(static m => m.Classification == MethodClassification.PInvoke)),
            result.AnswerTo(signals[1]));
        Assert.True(result.MergedRows.IsDefault);
        Assert.Null(result.Finding);

        using var planReader = new PEReader(File.OpenRead(path));
        WorkDescription plan = Assert.IsType<ProducerPlanResult.Accepted>(ProducerPlanner.Plan(
        [
            new ProducerRequest(PointerSignatureAnalyzer.Instance),
            new ProducerRequest(PInvokeAnalyzer.Instance),
        ])).Description;
        Assert.Equal((MethodDefinitionLayers)0, MethodDefinitionExecution.FieldsRead(plan) & MethodDefinitionLayers.IdentityText);
        Assert.False(MethodDefinitionExecution.Execute(plan, "fixture", planReader).Receipt.IdentityBudgetArmed);
    }

    [Fact]
    public void Combined_RowsCountAndExistsForOneAnalyzerExecuteIndependentlyAndAgree()
    {
        string path = FixtureCatalog.DecompilerClassicAsync.AssemblyPath();
        ClassificationQuestion rows = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows, ClassifiedRowOrder.Display);
        ClassificationQuestion count = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Count);
        ClassificationQuestion exists = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Exists);

        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [rows, count, exists]);

        var listed = Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(rows));
        Assert.NotEmpty(listed.Methods);
        Assert.Equal(new ClassificationAnswer.Count(listed.Methods.Length), result.AnswerTo(count));
        Assert.Equal(new ClassificationAnswer.Exists(true), result.AnswerTo(exists));

        // One execution per closing; Count and Exists read no identity text.
        Assert.Equal(
            [ClassificationClosing.Rows, ClassificationClosing.Count, ClassificationClosing.Exists],
            result.Receipts.Keys.Order());
        Assert.True(result.Receipts[ClassificationClosing.Rows].IdentityBudgetArmed);
        Assert.False(result.Receipts[ClassificationClosing.Count].IdentityBudgetArmed);
        Assert.False(result.Receipts[ClassificationClosing.Exists].IdentityBudgetArmed);
        Assert.Equal(0, result.Receipts[ClassificationClosing.Count].IdentityWorkCharged);
    }

    [Fact]
    public void LegacyOrder_IsANamedOrderWithAsyncBeforePointerWithinOneMethod()
    {
        static ClassifiedMethodRow Row(int ordinal, MethodClassification classification) =>
            new(ordinal, ordinal, InertString.Empty, InertString.Empty, InertString.Empty, InertString.Empty,
                classification, null, null, null);

        ClassifiedMethodRow[] rows =
        [
            Row(1, MethodClassification.Unsafe),
            Row(0, MethodClassification.Unsafe),
            Row(1, MethodClassification.RuntimeAsync),
            Row(0, MethodClassification.PInvoke),
        ];

        Assert.Equal(
            [(0, MethodClassification.PInvoke), (0, MethodClassification.Unsafe), (1, MethodClassification.RuntimeAsync), (1, MethodClassification.Unsafe)],
            ClassifiedMethodRowOrders.Apply(rows, ClassifiedMethodRowOrders.Legacy).Select(static row => (row.Ordinal, row.Classification)));
    }

    [Fact]
    public void Async_RejectedStateMachineRelationshipFailsTheAnalyzer()
    {
        string path = FixtureCatalog.AnalysisAsyncSiblingFriend.AssemblyPath();
        ClassificationQuestion count = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Count);
        ClassificationQuestion pointer = new(MethodClassificationAnalyzer.PointerSignature, ClassificationClosing.Count);

        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [count, pointer]);

        var failed = Assert.IsType<ClassificationAnswer.Failed>(result.AnswerTo(count));
        Assert.Equal(TokenLabel(path, "MalformedAsyncSourceFixture", "AnalyzeAsync"), failed.Failure.Unit);
        Assert.IsType<ClassificationAnswer.Count>(result.AnswerTo(pointer));
        Assert.Null(result.Critical);
    }

    static IEnumerable<ClassifiedMethodObservation> Payloads(FindingInspection<ClassifiedMethodObservation> inspection) =>
        Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Complete>(inspection.Value)
            .Findings.Select(static finding => finding.Payload);

    [Theory]
    [InlineData(FixtureIds.AnalysisAsyncSiblingFriend, "MalformedAsyncSourceFixture", "AnalyzeAsync")]
    [InlineData(FixtureIds.AnalysisLookalike, null, null)]
    public void Async_AcceptedDeparture_RejectedRelationshipFails(string fixtureId, string? type, string? method)
    {
        string path = FixtureCatalog.Get(fixtureId).AssemblyPath();
        ClassificationQuestion count = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Count);

        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [count]);

        var failed = Assert.IsType<ClassificationAnswer.Failed>(result.AnswerTo(count));
        if (type is not null)
            Assert.Equal(TokenLabel(path, type, method!), failed.Failure.Unit);

        Assert.Null(result.Critical);
    }

    [Fact]
    public void Async_AcceptedDeparture_SpoofedAttributeIsNotAsync()
    {
        string path = FixtureCatalog.Get(FixtureIds.AnalysisSpoofSystemRuntime).AssemblyPath();
        using (var legacyReader = new PEReader(File.OpenRead(path)))
        {
            Assert.Contains(
                LegacyMethodClassificationScanner.Scan(legacyReader),
                static row => row.MethodName == "Analyze"
                    && row.DeclaringType == "System.AsyncAttributeSpoofer"
                    && row.Classification == MethodClassification.StateMachineAsync);
        }

        ClassificationQuestion rows = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows);
        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [rows]);

        var listed = Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(rows));
        Assert.DoesNotContain(
            listed.Methods,
            static row => row.MethodName.ToString() == "Analyze"
                && row.DeclaringType.ToString() == "System.AsyncAttributeSpoofer");
    }

    [Fact]
    public void Finding_FailedAnalyzerIsAFailedInspectionNamingIt()
    {
        string path = FixtureCatalog.AnalysisAsyncSiblingFriend.AssemblyPath();
        using var peReader = new PEReader(File.OpenRead(path));

        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [], Subject);

        Assert.True(result.MergedRows.IsDefault);
        var failed = Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Failed>(result.Finding!.Value);
        Assert.Contains("MethodClassification.Async", failed.Error.Reason);
        Assert.Contains(TokenLabel(path, "MalformedAsyncSourceFixture", "AnalyzeAsync"), failed.Error.Reason);
        Assert.Null(result.Critical);

        using var again = new PEReader(File.OpenRead(path));
        Assert.Null(MethodClassificationQuery.Execute(again, []).Finding);
    }

    [Fact]
    public void Finding_MalformedPointerSignatureIsAFailedInspectionNamingIt()
    {
        byte[] image = TruncatedSignatureImage();
        using var peReader = new PEReader(ImmutableArray.Create(image));

        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [], Subject);

        var failed = Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Failed>(result.Finding!.Value);
        Assert.Contains("MethodClassification.PointerSignature", failed.Error.Reason);
        Assert.Contains("MethodDef 0x06000001", failed.Error.Reason);
    }

    [Fact]
    public void Equivalence_ReorderedMethodPtrKeepsTraversalOrder()
    {
        byte[] image = MetadataMethodPtrFixture.BuildPointerMethods(2, 1);
        using var legacyReader = new PEReader(ImmutableArray.Create(image));
        List<ClassifiedMethodInfo> legacy = LegacyMethodClassificationScanner.Scan(legacyReader);
        Assert.True(legacy.Count >= 2, $"The reordered fixture yields {legacy.Count} legacy rows.");

        using var peReader = new PEReader(ImmutableArray.Create(image));
        MethodClassificationResult result = MethodClassificationQuery.Execute(
            peReader,
            [new(MethodClassificationAnalyzer.PointerSignature, ClassificationClosing.Rows)],
            Subject);

        Assert.Equal(
            legacy.Select(static row => (row.DeclaringType, row.MethodName, row.Classification)),
            result.MergedRows.Select(static row => (row.DeclaringType.ToString(), row.MethodName.ToString(), row.Classification)));
    }

    /// <summary>One public type with one public method whose signature is truncated (00 01 01).</summary>
    static byte[] TruncatedSignatureImage()
    {
        var metadata = new System.Reflection.Metadata.Ecma335.MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Truncated.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Truncated"), new Version(1, 0, 0, 0), default, default, default, default);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1),
            System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(System.Reflection.TypeAttributes.Public, metadata.GetOrAddString("N"), metadata.GetOrAddString("T"), default,
            System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1),
            System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(1));
        var signature = new System.Reflection.Metadata.BlobBuilder();
        signature.WriteBytes(new byte[] { 0x00, 0x01, 0x01 });
        metadata.AddMethodDefinition(
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            System.Reflection.MethodImplAttributes.IL,
            metadata.GetOrAddString("Short"),
            metadata.GetOrAddBlob(signature),
            -1,
            System.Reflection.Metadata.Ecma335.MetadataTokens.ParameterHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new System.Reflection.Metadata.Ecma335.MetadataRootBuilder(metadata),
            new System.Reflection.Metadata.BlobBuilder());
        var output = new System.Reflection.Metadata.BlobBuilder();
        pe.Serialize(output);
        return output.ToArray();
    }

    /// <summary>The content-free label of <c>type::method</c> in the assembly at <paramref name="path"/>.</summary>
    static string TokenLabel(string path, string typeName, string methodName)
    {
        using var peReader = new PEReader(File.OpenRead(path));
        System.Reflection.Metadata.MetadataReader reader = peReader.GetMetadataReader();
        foreach (System.Reflection.Metadata.TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            System.Reflection.Metadata.TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (System.Reflection.Metadata.MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return $"MethodDef 0x{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(methodHandle):X8}";
            }
        }

        throw new InvalidOperationException($"{typeName}::{methodName} was not found.");
    }

    static void AssertRows(List<ClassifiedMethodInfo> expected, ClassificationAnswer answer)
    {
        var rows = Assert.IsType<ClassificationAnswer.Rows>(answer).Methods;
        Assert.Equal(
            expected.Select(static m => (m.DeclaringType, m.MethodName, m.Signature, m.ModuleName)),
            rows.Select(static r => (r.DeclaringType.ToString(), r.MethodName.ToString(), r.Signature.ToString(), r.ModuleName?.ToString())));
    }

    static string? FixtureIdFor(string path)
    {
        foreach (FixtureDefinition fixture in FixtureCatalog.All)
        {
            try
            {
                if (string.Equals(fixture.AssemblyPath(), path, StringComparison.OrdinalIgnoreCase))
                    return fixture.Id;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or NotSupportedException)
            {
            }
        }

        return null;
    }
}
