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
            new(MethodClassificationAnalyzer.Async, ClassificationClosing.Exists),
            new(MethodClassificationAnalyzer.RuntimeAsync, ClassificationClosing.Rows),
            new(MethodClassificationAnalyzer.CompilerAsync, ClassificationClosing.Rows),
            new(MethodClassificationAnalyzer.RuntimeAsync, ClassificationClosing.Count),
            new(MethodClassificationAnalyzer.CompilerAsync, ClassificationClosing.Count),
        ];
        using var peReader = new PEReader(File.OpenRead(path));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, questions);
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
        Assert.Equal(new ClassificationAnswer.Exists(async.Count > 0), result.AnswerTo(questions[8]));

        // The two async analyzers split legacy's async rows, disjointly, and
        // each Count equals its Rows.
        var runtime = legacy.Where(static m => m.Classification == MethodClassification.RuntimeAsync).ToList();
        var compiler = legacy.Where(static m => m.Classification == MethodClassification.StateMachineAsync).ToList();
        AssertRows(runtime, result.AnswerTo(questions[9]));
        AssertRows(compiler, result.AnswerTo(questions[10]));
        Assert.Equal(new ClassificationAnswer.Count(runtime.Count), result.AnswerTo(questions[11]));
        Assert.Equal(new ClassificationAnswer.Count(compiler.Count), result.AnswerTo(questions[12]));
        Assert.Empty(
            Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(questions[9])).Methods.Select(static row => row.Token)
                .Intersect(Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(questions[10])).Methods.Select(static row => row.Token)));
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
    public void Async_ExistsStopsAtTheFirstAsyncMethodInOnePass()
    {
        // Runtime async first, then a method whose attribute type nests
        // beyond the chain bound: reading its attribute would abort.
        byte[] image = AsyncImage(runtimeAsync: true, hostileAttribute: true);
        ClassificationQuestion exists = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Exists);
        ClassificationQuestion count = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Count);

        using (var peReader = new PEReader(ImmutableArray.Create(image)))
        {
            MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [exists]);

            Assert.Equal(new ClassificationAnswer.Exists(true), result.AnswerTo(exists));
            Assert.Null(result.Critical);
            (ClassificationClosing closing, WorkReceipt receipt) = Assert.Single(result.Receipts);
            Assert.Equal(ClassificationClosing.Exists, closing);
            Assert.Equal(
                [AsyncAnalyzer.Instance.Identity],
                receipt.Producers.Select(static participation => participation.Producer));
        }

        // The same scope's Count reads the hostile attribute and aborts.
        using (var peReader = new PEReader(ImmutableArray.Create(image)))
        {
            MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [count]);
            Assert.IsType<ClassificationAnswer.Aborted>(result.AnswerTo(count));
            Assert.NotNull(result.Critical);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void Async_ExistsCountAndRowsAgree(
        bool runtimeAsync,
        bool compilerAsync,
        bool expected)
    {
        byte[] image = AsyncImage(runtimeAsync, hostileAttribute: false, compilerAsync);
        ClassificationQuestion exists = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Exists);
        ClassificationQuestion count = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Count);
        ClassificationQuestion rows = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows);

        using var peReader = new PEReader(ImmutableArray.Create(image));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [exists, count, rows]);

        Assert.Equal(new ClassificationAnswer.Exists(expected), result.AnswerTo(exists));
        var listed = Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(rows));
        Assert.Equal(new ClassificationAnswer.Count(listed.Methods.Length), result.AnswerTo(count));
    }

    [Fact]
    public void Async_RowsCarryTheirKindInTheRequestedOrder()
    {
        byte[] image = AsyncImage(runtimeAsync: true, hostileAttribute: false, compilerAsync: true);
        ClassificationQuestion metadata = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows);
        ClassificationQuestion model = new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows, ClassifiedRowOrder.Model);

        using var legacyReader = new PEReader(ImmutableArray.Create(image));
        List<ClassifiedMethodInfo> legacy = LegacyMethodClassificationScanner.Scan(legacyReader);
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [metadata, model]);

        Assert.Equal(
            legacy.Select(static row => (row.MethodName, row.Classification)),
            Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(metadata)).Methods
                .Select(static row => (row.MethodName.ToString(), row.Classification)));
        Assert.Equal(
            ["Runtime", "Compiler"],
            Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(model)).Methods.Select(static row => row.MethodName.ToString()));
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

    static IEnumerable<ClassifiedMethodObservation> Payloads(FindingInspection<ClassifiedMethodObservation> inspection) =>
        Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Complete>(inspection.Value)
            .Findings.Select(static finding => finding.Payload);

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

    /// <summary>
    /// One public type: a compiler-async method first (when asked), then a
    /// runtime-async method (when asked), then a method whose attribute type
    /// nests beyond the chain bound (when asked).
    /// </summary>
    static byte[] AsyncImage(bool runtimeAsync, bool hostileAttribute, bool compilerAsync = false)
    {
        var metadata = new System.Reflection.Metadata.Ecma335.MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Async.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Async"), new Version(1, 0, 0, 0), default, default, default, default);
        System.Reflection.Metadata.AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default, default, default, default);

        var ctorSignature = new System.Reflection.Metadata.BlobBuilder();
        new System.Reflection.Metadata.Ecma335.BlobEncoder(ctorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, static r => r.Void(), static _ => { });
        System.Reflection.Metadata.BlobHandle ctorBlob = metadata.GetOrAddBlob(ctorSignature);
        System.Reflection.Metadata.MemberReferenceHandle Constructor(System.Reflection.Metadata.EntityHandle parent) =>
            metadata.AddMemberReference(parent, metadata.GetOrAddString(".ctor"), ctorBlob);

        System.Reflection.Metadata.MemberReferenceHandle asyncConstructor = Constructor(metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("AsyncStateMachineAttribute")));
        System.Reflection.Metadata.EntityHandle deep = metadata.AddTypeReference(
            runtime, metadata.GetOrAddString("System.Runtime"), metadata.GetOrAddString("CompilerServices"));
        for (int i = 0; i < MetadataSafetyPolicy.MaxRelationshipNodes; i++)
            deep = metadata.AddTypeReference(deep, default, metadata.GetOrAddString("AsyncStateMachineAttribute"));
        System.Reflection.Metadata.MemberReferenceHandle hostileConstructor = Constructor(deep);

        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1),
            System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(System.Reflection.TypeAttributes.Public, metadata.GetOrAddString("N"), metadata.GetOrAddString("T"), default,
            System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1),
            System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(1));

        var voidSignature = new System.Reflection.Metadata.BlobBuilder();
        new System.Reflection.Metadata.Ecma335.BlobEncoder(voidSignature)
            .MethodSignature()
            .Parameters(0, static r => r.Void(), static _ => { });
        System.Reflection.Metadata.BlobHandle voidBlob = metadata.GetOrAddBlob(voidSignature);
        void Method(string name, System.Reflection.MethodImplAttributes impl, System.Reflection.Metadata.MemberReferenceHandle? attribute)
        {
            System.Reflection.Metadata.MethodDefinitionHandle method = metadata.AddMethodDefinition(
                System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                impl,
                metadata.GetOrAddString(name),
                voidBlob,
                -1,
                System.Reflection.Metadata.Ecma335.MetadataTokens.ParameterHandle(1));
            if (attribute is { } constructor)
                metadata.AddCustomAttribute(method, constructor, default);
        }

        if (compilerAsync)
            Method("Compiler", System.Reflection.MethodImplAttributes.IL, asyncConstructor);
        if (runtimeAsync)
            Method("Runtime", (System.Reflection.MethodImplAttributes)0x2000, null);
        if (hostileAttribute)
            Method("Hostile", System.Reflection.MethodImplAttributes.IL, hostileConstructor);
        Method("Plain", System.Reflection.MethodImplAttributes.IL, null);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new System.Reflection.Metadata.Ecma335.MetadataRootBuilder(metadata),
            new System.Reflection.Metadata.BlobBuilder());
        var output = new System.Reflection.Metadata.BlobBuilder();
        pe.Serialize(output);
        return output.ToArray();
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

    static void AssertRows(List<ClassifiedMethodInfo> expected, ClassificationAnswer answer)
    {
        var rows = Assert.IsType<ClassificationAnswer.Rows>(answer).Methods;
        Assert.Equal(
            expected.Select(static m => (m.DeclaringType, m.MethodName, m.Signature, m.ModuleName)),
            rows.Select(static r => (r.DeclaringType.ToString(), r.MethodName.ToString(), r.Signature.ToString(), r.ModuleName?.ToString())));
    }
}
