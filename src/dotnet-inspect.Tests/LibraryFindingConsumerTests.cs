using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;
using TypeAttributes = System.Reflection.TypeAttributes;

namespace DotnetInspector.Tests;

// Mutates the process-global CoreCache root; serialize with in-process CLI/cache tests (#3471).
[Collection("Console")]
public class LibraryFindingConsumerTests
{
    [Fact]
    public void UnionTypesQueryProjection_RetainsMetadataFindingInspection()
    {
        string path = typeof(SampleDiscoveredUnion).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(path);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyUnionTypesResult(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            UnionTypesQuery.Execute(session));

        var finding = Assert.Single(
            inspection.UnionTypeInspection.Findings(),
            finding => finding.Payload.TypeName == typeof(SampleDiscoveredUnion).FullName);
        Assert.Same(MetadataFindings.UnionTypeDescriptor, finding.Descriptor);
    }

    [Fact]
    public void UnionTypesQueryProjection_PreservesIdentityUntilInertViewBoundary()
    {
        const string TypeName = "Sample.\u200B\u001b[31mForged";
        const string Kind = "str\U000E0074uct";
        const string CaseType = "Sample.Case\nError: forged";
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyUnionTypesResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            new UnionTypesResult.Available(
                ImmutableArray.Create(
                    new UnionTypeInfo(TypeName, Kind, true, [CaseType]))));

        UnionTypeInfo payload = Assert.Single(
            inspection.UnionTypeInspection.Findings()).Payload;
        UnionTypeRow row = Assert.Single(
            new LibraryInspectionView(inspection).UnionTypesSection!);

        Assert.Equal(TypeName, payload.TypeName);
        Assert.Equal(Kind, payload.Kind);
        Assert.Equal([CaseType], payload.CaseTypes);
        Assert.NotEqual(TypeName, row.Type);
        Assert.NotEqual(Kind, row.Kind);
        Assert.NotEqual(CaseType, row.Cases);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Type));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Kind));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Cases));
        Assert.DoesNotContain("\u200B", row.Type, StringComparison.Ordinal);
        Assert.DoesNotContain("\U000E0074", row.Kind, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchesQueryProjection_RetainsFindingSemanticsAndDisplayProjection()
    {
        string path =
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(path);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplySwitchesResult(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            SwitchesQuery.Execute(session));

        var finding = Assert.Single(
            inspection.SwitchInspection.Findings(),
            item => item.Payload.Switch == "DotnetInspector.Fixtures.AppContextOnly");
        var row = Assert.Single(
            new LibraryInspectionView(inspection).SwitchesSection!,
            item => item.Switch.Contains(
                "DotnetInspector.Fixtures.AppContextOnly",
                StringComparison.Ordinal));

        Assert.Same(MetadataFindings.SwitchDescriptor, finding.Descriptor);
        Assert.Equal("AppContext", finding.Payload.Kind);
        Assert.Equal("<code>DotnetInspector.Fixtures.AppContextOnly</code>", row.Switch);
    }

    [Fact]
    public void SwitchesQueryProjection_PreservesIdentityUntilInertViewBoundary()
    {
        const string Kind = "App\u200BContext";
        const string Switch = "Sample\U000E0074.Switch";
        const string Api = "Sample.Api\nError: forged";
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplySwitchesResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            new SwitchesResult.Available(
                ImmutableArray.Create(
                    new SwitchInfo(Kind, Switch, Api))));

        SwitchInfo payload = Assert.Single(
            inspection.SwitchInspection.Findings()).Payload;
        SwitchRow row = Assert.Single(
            new LibraryInspectionView(inspection).SwitchesSection!);

        Assert.Equal(Kind, payload.Kind);
        Assert.Equal(Switch, payload.Switch);
        Assert.Equal(Api, payload.Api);
        Assert.NotEqual(Kind, row.Kind);
        Assert.DoesNotContain("\U000E0074", row.Switch, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', row.Api);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Kind));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Switch));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Api));
        Assert.StartsWith("<code>", row.Switch, StringComparison.Ordinal);
        Assert.StartsWith("<code>", row.Api, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassifiedMethodsQueryProjection_RetainsFindingSemanticsAndDisplayProjection()
    {
        string path = typeof(SampleUnsafeClass).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(path);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyClassifiedMethodsResult(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            ClassifiedMethodsQuery.Execute(session));

        var finding = Assert.Single(
            inspection.ClassifiedMethodInspection.Findings(),
            finding => finding.Payload.Anchor.MemberName == nameof(SampleUnsafeClass.UnsafePointerMethod));
        Assert.Same(MetadataFindings.ClassifiedMethodDescriptor, finding.Descriptor);
        Assert.Equal(MethodClassification.Unsafe, finding.Payload.Classification);
        Assert.Contains(
            inspection.UnsafeMethods!,
            method => method.MethodName == nameof(SampleUnsafeClass.UnsafePointerMethod)
                      && method.Signature.Contains('*', StringComparison.Ordinal));
    }

    [Fact]
    public void ClassifiedMethodsQueryProjection_PreservesIdentityUntilInertViewBoundary()
    {
        const string Name = "Method\u202EName";
        const string DeclaringType = "Namespace.Type\U000E0074";
        const string Signature = "void Method(\n)";
        const string Module = "native\u200B.dll";
        var anchor = new MemberAnchor(
            "Method()",
            "void Namespace.Type.Method()",
            "0123456789",
            "Namespace.Type",
            "Method");
        var method = new ClassifiedMethodInfo(
            Name,
            DeclaringType,
            "Namespace",
            Signature,
            MethodClassification.PInvoke,
            Module)
        {
            Anchor = anchor,
            ReturnType = "void",
        };
        var result = new ClassifiedMethodsResult.Available([method]);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyClassifiedMethodsResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            result);

        ClassifiedMethodInfo queryMethod = Assert.Single(result.Methods);
        ClassifiedMethodObservation payload = Assert.Single(
            inspection.ClassifiedMethodInspection.Findings()).Payload;
        ClassifiedMethodSummary summary = Assert.Single(inspection.PInvokeMethods!);
        PInvokeMethodRow row = Assert.Single(
            new LibraryInspectionView(inspection).PInvokeMethodsSection!);

        Assert.Equal(Name, queryMethod.MethodName);
        Assert.Equal(DeclaringType, queryMethod.DeclaringType);
        Assert.Equal(Signature, queryMethod.Signature);
        Assert.Equal(Module, queryMethod.ModuleName);
        Assert.Equal(anchor, payload.Anchor);
        Assert.Equal(Name, summary.MethodName);
        Assert.Equal(DeclaringType, summary.DeclaringType);
        Assert.Equal(Signature, summary.Signature);
        Assert.Equal(Module, summary.ModuleName);
        Assert.NotEqual(Name, row.Name);
        Assert.DoesNotContain("\U000E0074", row.DeclaringType, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', row.Signature);
        Assert.DoesNotContain("\u200B", row.Module, StringComparison.Ordinal);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Name));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.DeclaringType));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Module));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Signature));
        Assert.StartsWith("<code>", row.DeclaringType, StringComparison.Ordinal);
        Assert.StartsWith("<code>", row.Signature, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeEvidenceQueryProjection_PreservesIdentityUntilInertViewBoundary()
    {
        const string MethodName = "Method\u202EName";
        const string Reason = "Unsafe\nsignature";
        const string Detail = "int*\nError: forged";
        const string Kind = "sign\nature";
        var method = new MethodIdentity(
            "Hostile",
            Guid.Empty,
            TypeRef.Definition("Hostile", "Namespace", "Type\u202EName"),
            MethodName,
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
        var evidence = new UnsafeEvidence(
            method,
            Reason,
            Detail,
            Kind,
            ILOffset: 1,
            OperandToken: 0x01000001);
        var result = new UnsafeEvidenceResult.Available(
            [evidence],
            [new AnalysisDiagnostic(0x06000001, MethodName, Detail)]);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyUnsafeEvidenceResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            result);

        UnsafeEvidence queryEvidence = Assert.Single(result.Evidence);
        UnsafeEvidence findingPayload = Assert.Single(
            inspection.UnsafeEvidenceInspection.Findings()).Payload;
        UnsafeMemberSummary summary = Assert.Single(inspection.UnsafeMembers!);
        UnsafeMemberRow row = Assert.Single(
            new LibraryInspectionView(inspection).UnsafeMembersSection!);

        Assert.Same(evidence, queryEvidence);
        Assert.Same(evidence, findingPayload);
        Assert.Equal(MethodName, findingPayload.Member.Name);
        Assert.Equal(Reason, summary.Reason);
        Assert.Equal(Detail, summary.Detail);
        Assert.Equal(Kind, summary.Kind);
        Assert.Equal(result.Diagnostics, inspection.UnsafeEvidenceDiagnostics);

        Assert.NotEqual(Reason, row.Reason);
        Assert.NotEqual(Detail, row.Detail);
        Assert.NotEqual(Kind, row.Kind);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Member));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Reason));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Detail));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Kind));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.IL!));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Token!));
        Assert.StartsWith("<code>", row.Member, StringComparison.Ordinal);
        Assert.StartsWith("<code>", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TopLeverageQueryProjection_PreservesIdentityUntilInertViewBoundary()
    {
        const string MethodName = "Method\u202EName";
        const string Stable = "Method\u202EName~1234567890";
        const string Selector = "Method\u202EName";
        var method = new MethodIdentity(
            "Hostile",
            Guid.Empty,
            TypeRef.Definition("Hostile", "Namespace", "Type\u202EName"),
            MethodName,
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
        var leverage = new MethodLeverage(
            method,
            DirectCallerCount: 5,
            Fanout: 4,
            MaxDepth: 3,
            LoopCallCount: 2,
            RootReach: 6);
        var result = new TopLeverageResult.Available(
            [leverage],
            ImmutableHashSet<TypeRef>.Empty,
            [new AnalysisDiagnostic(0x06000001, MethodName, "decode\nfailed")]);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyTopLeverageResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            result,
            () => new Dictionary<int, (string? Stable, string Visibility, string Selector)>
            {
                [method.MetadataToken] = (Stable, "public", Selector),
            });

        var available = Assert.IsType<TopLeverageResult.Available>(
            inspection.TopLeverageQueryResult);
        MethodLeverage queryMethod = Assert.Single(available.Methods);
        MethodLeverageSummary summary = Assert.Single(inspection.TopLeverage!);
        TopLeverageRow row = Assert.Single(
            new LibraryInspectionView(inspection).TopLeverageSection!);

        Assert.Same(leverage, queryMethod);
        Assert.Same(method, queryMethod.Method);
        Assert.Equal(MethodName, queryMethod.Method.Name);
        Assert.Contains(MethodName, summary.Member, StringComparison.Ordinal);
        Assert.Equal(Stable, summary.Stable);
        Assert.Equal(Selector, summary.Selector);
        Assert.Equal(result.Diagnostics, available.Diagnostics);

        Assert.DoesNotContain('\u202E', row.Member);
        Assert.DoesNotContain('\u202E', row.Stable!);
        Assert.DoesNotContain('\u202E', row.Selector!);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Member));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Stable!));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Selector!));
        Assert.StartsWith("<code>", row.Member, StringComparison.Ordinal);
        Assert.StartsWith("<code>", row.Stable, StringComparison.Ordinal);
        Assert.StartsWith("<code>", row.Selector, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunitiesQueryProjection_PreservesIdentityUntilInertViewBoundary()
    {
        const string MethodName = "Method\u202EName";
        const string Evidence = "allocates\u202E\n| injected |";
        const string Allocation = "Type\u202EName";
        var method = new MethodIdentity(
            "Hostile",
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TypeRef.Definition("Hostile", "Namespace", "Type\u202EName"),
            MethodName,
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
        var opportunity = new OptimizationOpportunity(
            method,
            "small-array",
            Evidence,
            "Use stack allocation.",
            "high",
            InLoop: false,
            ILOffset: 0,
            Caveat: null,
            RootReach: 3)
        {
            RuntimeAllocationType = Allocation,
        };
        var result = new OptimizationOpportunitiesResult.Available(
            [opportunity],
            [],
            ImmutableHashSet<TypeRef>.Empty,
            [new AnalysisDiagnostic(0x06000001, MethodName, "decode failed")]);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyOptimizationOpportunitiesResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            result);

        var available =
            Assert.IsType<OptimizationOpportunitiesResult.Available>(
                inspection.OptimizationOpportunitiesQueryResult);
        OptimizationOpportunity queryOpportunity =
            Assert.Single(available.Opportunities);
        OptimizationOpportunity selectedOpportunity =
            Assert.Single(inspection.PerformanceTriageOpportunities);
        OptimizationOpportunitySummary summary =
            Assert.Single(inspection.OptimizationOpportunities!);
        PerformanceRow row = Assert.Single(
            new LibraryInspectionView(inspection).PerformanceArraysSection!);

        Assert.Same(opportunity, queryOpportunity);
        Assert.Same(opportunity, selectedOpportunity);
        Assert.Same(method, queryOpportunity.Method);
        Assert.Contains(MethodName, summary.Member, StringComparison.Ordinal);
        Assert.Equal(method.ModuleVersionId, summary.ModuleVersionId);
        Assert.Equal(Evidence, summary.Evidence);
        Assert.Equal(Allocation, summary.Allocation);
        Assert.Equal(result.Diagnostics, available.Diagnostics);

        Assert.DoesNotContain('\u202E', row.Member);
        Assert.DoesNotContain('\u202E', row.Evidence);
        Assert.DoesNotContain('\u202E', row.Allocation!);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Member));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Evidence));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.Allocation!));
        Assert.StartsWith("<code>", row.Member, StringComparison.Ordinal);
        Assert.StartsWith("<code>", row.Evidence, StringComparison.Ordinal);
        Assert.StartsWith("<code>", row.Allocation, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeEvidenceQueryProjection_UsesDistinctPerMethodFindingSubjects()
    {
        static MethodIdentity Method(string name, int token) => new(
            "Hostile",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TypeRef.Definition("Hostile", "Namespace", "Type"),
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            token,
            IsStatic: true);

        var first = new UnsafeEvidence(
            Method("First", 0x06000001),
            "Unsafe signature",
            "int*",
            "signature",
            ILOffset: null,
            OperandToken: null);
        var second = new UnsafeEvidence(
            Method("Second", 0x06000002),
            "Unsafe signature",
            "int*",
            "signature",
            ILOffset: null,
            OperandToken: null);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyUnsafeEvidenceResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            new UnsafeEvidenceResult.Available([first, second], []));

        var findings = inspection.UnsafeEvidenceInspection.Findings();
        Assert.Equal(2, findings.Length);
        Assert.Equal(2, findings.Select(finding => finding.Subject.Key).Distinct().Count());
        Assert.Contains(findings, finding => ReferenceEquals(finding.Payload, first));
        Assert.Contains(findings, finding => ReferenceEquals(finding.Payload, second));
    }

    [Fact]
    public void UnsafeEvidenceTypedView_PreservesLegacyFormattedIlOrdering()
    {
        var method = new MethodIdentity(
            "Hostile",
            Guid.Empty,
            TypeRef.Definition("Hostile", "Namespace", "Type"),
            "Method",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyUnsafeEvidenceResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            new UnsafeEvidenceResult.Available(
                [
                    new UnsafeEvidence(
                        method,
                        "Unsafe operation",
                        "first",
                        "opcode",
                        ILOffset: 0xFFFF,
                        OperandToken: null),
                    new UnsafeEvidence(
                        method,
                        "Unsafe operation",
                        "second",
                        "opcode",
                        ILOffset: 0x10000,
                        OperandToken: null),
                ],
                []));

        Assert.Equal(
            ["<code>IL_10000</code>", "<code>IL_FFFF</code>"],
            new LibraryInspectionView(inspection).UnsafeMembersSection!
                .Select(row => row.IL));
    }

    [Fact]
    public void ExtensionMethodsQueryProjection_RetainsFindingSemanticsAndDisplayProjection()
    {
        var inspection = new LibraryInspection();
        string path = typeof(ExtensionsCommandTests).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(path);

        LibraryMetadataService.ApplyExtensionMethodsResult(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            ExtensionMethodsQuery.Execute(session));

        var finding = Assert.Single(
            inspection.ExtensionMemberInspection.Findings(),
            finding => finding.Payload.Anchor.MemberName == "ToUpperCase");
        var row = Assert.Single(
            inspection.ExtensionMethods!,
            row => row.MethodName == "ToUpperCase");

        Assert.Same(MetadataFindings.ExtensionMemberDescriptor, finding.Descriptor);
        Assert.Equal(ExtensionMemberKind.Method, finding.Payload.Kind);
        Assert.Contains("string", row.ExtendedType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomAttributesQueryProjection_RetainsFindingSemanticsAndJsonOrder()
    {
        var inspection = new LibraryInspection();
        string path = typeof(AssemblyInspectionSession).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(path);
        var result = Assert.IsType<CustomAttributesResult.Available>(
            CustomAttributesQuery.Execute(session));

        LibraryMetadataService.ApplyCustomAttributesResult(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            result);

        var findings = inspection.AssemblyAttributeInspection.Findings();
        Assert.Contains(
            findings,
            finding => finding.Payload.Name == "InternalsVisibleTo");
        var finding = findings.First(
            finding => finding.Payload.Name == "InternalsVisibleTo");

        Assert.Same(MetadataFindings.AssemblyAttributeDescriptor, finding.Descriptor);
        Assert.Equal(
            result.Attributes.Select(attribute => attribute.Name),
            inspection.CustomAttributes!.Select(attribute => attribute.Name));
    }

    [Fact]
    public void ResourcesQueryProjection_RetainsFindingSemanticsAndDisplayProjection()
    {
        var inspection = new LibraryInspection();
        string path = typeof(LibraryInspection).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(path);

        LibraryMetadataService.ApplyResourcesResult(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            ResourcesQuery.Execute(session));

        var finding = Assert.Single(
            inspection.ResourceInspection.Findings(),
            finding => finding.Payload.Name.Contains("SKILL.md", StringComparison.Ordinal));
        var row = Assert.Single(
            inspection.Resources!,
            resource => resource.Name.Contains("SKILL.md", StringComparison.Ordinal));

        Assert.Same(MetadataFindings.ResourceDescriptor, finding.Descriptor);
        Assert.True(finding.Payload.IsEmbedded);
        Assert.True(row.Size > 0);
    }

    [Fact]
    public void TypeForwardersQueryProjection_RetainsFindingSemanticsAndDisplayProjection()
    {
        var inspection = new LibraryInspection();
        string path = typeof(AssemblyInspectionSession).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(path);

        LibraryMetadataService.ApplyTypeForwardersResult(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            TypeForwardersQuery.Execute(session));

        var finding = Assert.Single(
            inspection.TypeForwarderInspection.Findings(),
            finding => finding.Payload.TypeName == "ILInspector.Metadata.SignatureBlobGuard");
        var row = Assert.Single(
            inspection.TypeForwarders!,
            forwarder => forwarder.TypeName == "ILInspector.Metadata.SignatureBlobGuard");

        Assert.Same(MetadataFindings.TypeForwarderDescriptor, finding.Descriptor);
        Assert.Equal("ILInspector.MetadataPrimitives", finding.Payload.TargetAssembly);
        Assert.Equal(finding.Payload, row);
    }

    [Fact]
    public void TypeForwardersQueryProjection_PreservesIdentityUntilInertViewBoundary()
    {
        const string TypeName = "Sample.\u200B\u001b[31mForged";
        const string TargetAssembly = "Target\U000E0074\nError: forged";
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyTypeForwardersResult(
            "hostile.dll",
            inspection,
            new VerboseLogger(enabled: false),
            new TypeForwardersResult.Available(
                ImmutableArray.Create(
                    new TypeForwarderInfo(TypeName, TargetAssembly))));

        TypeForwarderInfo payload = Assert.Single(
            inspection.TypeForwarderInspection.Findings()).Payload;
        TypeForwarderRow row = Assert.Single(
            new LibraryInspectionView(inspection).TypeForwardersSection!);

        Assert.Equal(TypeName, payload.TypeName);
        Assert.Equal(TargetAssembly, payload.TargetAssembly);
        Assert.NotEqual(TypeName, row.TypeName);
        Assert.NotEqual(TargetAssembly, row.TargetAssembly);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.TypeName));
        Assert.True(InertString.IsPermitted(TextPolicy.Field, row.TargetAssembly));
        Assert.DoesNotContain("\u200B", row.TypeName, StringComparison.Ordinal);
        Assert.DoesNotContain("\U000E0074", row.TargetAssembly, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryJson_ProjectsFindingPayloadsWithExistingShape()
    {
        AssemblyAttributeInfo[] attributes =
        [
            new("AssemblyMetadata(Serviceable)", "Assembly", "True"),
            new("Marker", "Module", null),
        ];
        ManifestResourceInfo[] resources =
        [
            new("SKILL.md", IsPublic: true, IsEmbedded: true, Size: 42),
            new("release-notes.md", IsPublic: true, IsEmbedded: true, Size: 43),
        ];
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll",
            ResourceInspection = MetadataFindings.InspectResources(
                resources,
                FindingTestData.Subject),
            TypeForwarderInspection = MetadataFindings.InspectTypeForwarders(
                [new TypeForwarderInfo("Test.Forwarded", "Test.Target")],
                FindingTestData.Subject),
            UnionTypeInspection = MetadataFindings.InspectUnionTypes(
                [new UnionTypeInfo("Test.Union", "struct", true, ["Test.Case"])],
                FindingTestData.Subject),
            SwitchInspection = MetadataFindings.InspectSwitches(
                [new SwitchInfo("Feature Switch", "Test.Switch", "Test.Api")],
                FindingTestData.Subject),
            EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
                [new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.AI, "Chat", "Test.ChatClient")],
                FindingTestData.Subject),
            OpenTelemetryInspection = MetadataFindings.InspectOpenTelemetrySignals(
                [new OpenTelemetrySignalInfo("Tracing", "Test.ActivitySource")],
                FindingTestData.Subject),
        };
        inspection.SetAssemblyAttributeInspection(
            MetadataFindings.InspectAssemblyAttributes(
                attributes,
                FindingTestData.Subject),
            attributes);
        ExtensionMethodInfo[] extensionMembers =
        [
            FindingTestData.ExtensionMember("Ext", "Test.Target"),
        ];
        inspection.SetExtensionMemberInspection(
            MetadataFindings.InspectExtensionMembers(
                extensionMembers,
                FindingTestData.Subject),
            extensionMembers);

        var json = JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var customAttributes = root.GetProperty("custom_attributes").EnumerateArray().ToArray();
        var resourcesJson = root.GetProperty("resources").EnumerateArray().ToArray();
        var expectedResourceOrder = resources.OrderBy(resource => resource.Name).ToArray();

        Assert.Equal(
            expectedResourceOrder.Select(resource => resource.Name),
            resourcesJson.Select(resource => resource.GetProperty("name").GetString()));
        Assert.Equal("public", resourcesJson[0].GetProperty("visibility").GetString());
        Assert.Equal("AssemblyMetadata(Serviceable)", customAttributes[0].GetProperty("name").GetString());
        Assert.Equal("True", customAttributes[0].GetProperty("value").GetString());
        Assert.Equal("Marker", customAttributes[1].GetProperty("name").GetString());
        Assert.False(customAttributes[1].TryGetProperty("value", out _));
        Assert.Equal("Test.Forwarded", root.GetProperty("type_forwarders")[0].GetProperty("type_name").GetString());
        Assert.Equal("Test.Union", root.GetProperty("union_types")[0].GetProperty("type_name").GetString());
        Assert.Equal("Test.Switch", root.GetProperty("switches")[0].GetProperty("switch").GetString());
        Assert.Equal("Ext", root.GetProperty("extension_methods")[0].GetProperty("method_name").GetString());
        Assert.Equal("Test.Target", root.GetProperty("extension_methods")[0].GetProperty("extended_type").GetString());
        Assert.Equal(EcosystemIntegrationNames.AI, root.GetProperty("integrations")[0].GetProperty("integration").GetString());
        Assert.Equal("Test.ChatClient", root.GetProperty("ai")[0].GetProperty("name").GetString());
        Assert.Equal("Test.ActivitySource", root.GetProperty("open_telemetry")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("inspection", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssemblyAttributeInspection_RequiresExplicitJsonOrder()
    {
        var attributeProperty = typeof(LibraryInspection).GetProperty(
            nameof(LibraryInspection.AssemblyAttributeInspection));
        var extensionProperty = typeof(LibraryInspection).GetProperty(
            nameof(LibraryInspection.ExtensionMemberInspection));

        Assert.NotNull(attributeProperty);
        Assert.False(attributeProperty.CanWrite);
        Assert.NotNull(extensionProperty);
        Assert.False(extensionProperty.CanWrite);
    }

    [Fact]
    public void FailedScannerAcquisition_RetainsFindingFailures()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        var logger = new VerboseLogger(enabled: false);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyClassifiedMethodsResult(
            missingPath,
            inspection,
            logger,
            new ClassifiedMethodsResult.Failed(
                new FileNotFoundException(
                    "Classified method input was not found.",
                    missingPath)));
        LibraryMetadataService.ApplyExtensionMethodsResult(
            missingPath,
            inspection,
            logger,
            new ExtensionMethodsResult.Failed(
                new FileNotFoundException("Extension method input was not found.", missingPath)));
        LibraryMetadataService.ApplyCustomAttributesResult(
            missingPath,
            inspection,
            logger,
            new CustomAttributesResult.Failed(
                new FileNotFoundException("Custom attribute input was not found.", missingPath)));
        LibraryMetadataService.ApplyResourcesResult(
            missingPath,
            inspection,
            logger,
            new ResourcesResult.Failed(
                new FileNotFoundException("Resource input was not found.", missingPath)));
        LibraryMetadataService.ApplyTypeForwardersResult(
            missingPath,
            inspection,
            logger,
            new TypeForwardersResult.Failed(
                new FileNotFoundException("Type forwarder input was not found.", missingPath)));
        LibraryMetadataService.ApplyUnionTypesResult(
            missingPath,
            inspection,
            logger,
            new UnionTypesResult.Failed(
                new FileNotFoundException("Union type input was not found.", missingPath)));
        LibraryMetadataService.ApplySwitchesResult(
            missingPath,
            inspection,
            logger,
            new SwitchesResult.Failed(
                new FileNotFoundException("Switch input was not found.", missingPath)));

        AssertFailure(inspection.ClassifiedMethodInspection, MetadataFindings.ClassifiedMethodDescriptor);
        AssertFailure(inspection.ExtensionMemberInspection, MetadataFindings.ExtensionMemberDescriptor);
        AssertFailure(inspection.ResourceInspection, MetadataFindings.ResourceDescriptor);
        AssertFailure(inspection.AssemblyAttributeInspection, MetadataFindings.AssemblyAttributeDescriptor);
        AssertFailure(inspection.TypeForwarderInspection, MetadataFindings.TypeForwarderDescriptor);
        AssertFailure(inspection.UnionTypeInspection, MetadataFindings.UnionTypeDescriptor);
        AssertFailure(inspection.SwitchInspection, MetadataFindings.SwitchDescriptor);
        Assert.Equal(7, inspection.InspectionFailures!.Count);
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_ExecutesOneGroupAndRetainsProvenance()
    {
        string firstPath = typeof(LibraryFindingConsumerTests).Assembly.Location;
        string secondPath = typeof(LibraryInspection).Assembly.Location;
        HashSet<InspectionQueryDefinition> queries =
            [AssemblyContextIntegrationsQuery.Definition];
        var trace = new DotnetInspector.Sections.InspectionTrace();

        AssemblyContextIntegrationsBatch batch =
            Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries,
                    LibrarySections.CreateGroupQueryRegistry(),
                    [
                        new AssemblyContextIntegrationsInput(
                            firstPath,
                            AssemblyResolutionProvenance.Local("first test input")),
                        new AssemblyContextIntegrationsInput(
                            secondPath,
                            AssemblyResolutionProvenance.Local("second test input")),
                    ],
                    trace));

        var first = Assert.IsType<AssemblyIntegrationsEntry.Available>(
            batch.EntryFor(firstPath));
        var second = Assert.IsType<AssemblyIntegrationsEntry.Available>(
            batch.EntryFor(secondPath));
        Assert.Equal(
            "first test input",
            Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(
                first.Subject.Provenance).ResolverSource);
        Assert.Equal(
            "second test input",
            Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(
                second.Subject.Provenance).ResolverSource);
        Assert.Empty(queries);
        Assert.Same(
            AssemblyContextIntegrationsQuery.Definition,
            Assert.Single(trace.QueryExecutions).Query);
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_ExecutesOpportunityClosureOnce()
    {
        string path = typeof(LibraryFindingConsumerTests).Assembly.Location;
        HashSet<InspectionQueryDefinition> queries =
            [AssemblyContextIntegrationOpportunitiesQuery.Definition];
        var trace = new DotnetInspector.Sections.InspectionTrace();

        AssemblyContextIntegrationsBatch batch =
            Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries,
                    LibrarySections.CreateGroupQueryRegistry(),
                    [
                        new AssemblyContextIntegrationsInput(
                            path,
                            AssemblyResolutionProvenance.Local(
                                "opportunity closure test")),
                    ],
                    trace));

        Assert.IsType<AssemblyIntegrationsEntry.Available>(
            batch.EntryFor(path));
        var opportunities = Assert.IsType<
            AssemblyIntegrationOpportunitiesEntry.Available>(
                batch.OpportunitiesEntryFor(path));
        var inspection = new LibraryInspection();
        LibraryMetadataService.ApplyAssemblyIntegrationOpportunitiesEntry(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            opportunities);

        Assert.Same(
            opportunities,
            inspection.AssemblyIntegrationOpportunitiesEntry);
        Assert.Empty(queries);
        Assert.Equal(
            [
                AssemblyContextIntegrationsQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
            ],
            trace.QueryExecutions.Select(execution => execution.Query));
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_ProjectsBudgetFailureBesideAvailableEntry()
    {
        string firstPath = typeof(LibraryFindingConsumerTests).Assembly.Location;
        string secondPath = typeof(LibraryInspection).Assembly.Location;
        HashSet<InspectionQueryDefinition> queries =
            [AssemblyContextIntegrationOpportunitiesQuery.Definition];

        AssemblyContextIntegrationsBatch batch =
            Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries,
                    LibrarySections.CreateGroupQueryRegistry(),
                    [
                        new AssemblyContextIntegrationsInput(
                            firstPath,
                            AssemblyResolutionProvenance.Local("available test input")),
                        new AssemblyContextIntegrationsInput(
                            secondPath,
                            AssemblyResolutionProvenance.Local("rejected test input")),
                    ],
                    groupOptions: new AssemblyContextGroupOptions
                    {
                        MaxRetainedImageBytes = new FileInfo(firstPath).Length,
                    }));

        Assert.IsType<AssemblyIntegrationsEntry.Available>(
            batch.EntryFor(firstPath));
        var rejected = Assert.IsType<AssemblyIntegrationsEntry.Rejected>(
            batch.EntryFor(secondPath));
        var rejectedOpportunities = Assert.IsType<
            AssemblyIntegrationOpportunitiesEntry.Rejected>(
                batch.OpportunitiesEntryFor(secondPath));
        Assert.Equal(CandidateOpenFailureKind.ResourceBudget, rejected.Failure.Kind);

        var inspection = new LibraryInspection();
        LibraryMetadataService.ApplyAssemblyIntegrationsEntry(
            secondPath,
            inspection,
            new VerboseLogger(enabled: false),
            rejected);

        Assert.Same(rejected, inspection.AssemblyIntegrationsEntry);
        LibraryMetadataService.ApplyAssemblyIntegrationOpportunitiesEntry(
            secondPath,
            inspection,
            new VerboseLogger(enabled: false),
            rejectedOpportunities);
        Assert.Equal(
            [
                LibraryIntegrationCatalog.RollupName,
                EcosystemIntegrationNames.OpenTelemetry,
                IntegrationSectionNames.Opportunities,
            ],
            inspection.InspectionFailures!.Select(failure => failure.Section));
    }

    [Fact]
    public void AssemblyIntegrationOpportunitiesFailure_ProjectsToItsSection()
    {
        string path = typeof(LibraryFindingConsumerTests).Assembly.Location;
        HashSet<InspectionQueryDefinition> queries =
            [AssemblyContextIntegrationsQuery.Definition];
        AssemblyContextIntegrationsBatch batch =
            Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries,
                    LibrarySections.CreateGroupQueryRegistry(),
                    [
                        new AssemblyContextIntegrationsInput(
                            path,
                            AssemblyResolutionProvenance.Local(
                                "opportunity failure projection test")),
                    ]));
        var integrations = Assert.IsType<AssemblyIntegrationsEntry.Available>(
            batch.EntryFor(path));
        var error = new BadImageFormatException("opportunity scan failed");
        var failed = new AssemblyIntegrationOpportunitiesEntry.Failed(
            integrations.Subject,
            error);
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyAssemblyIntegrationOpportunitiesEntry(
            path,
            inspection,
            new VerboseLogger(enabled: false),
            failed);

        Assert.Same(failed, inspection.AssemblyIntegrationOpportunitiesEntry);
        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal(IntegrationSectionNames.Opportunities, failure.Section);
        Assert.Equal(error.Message, failure.Reason);

        string json = JsonSerializer.Serialize(
            inspection,
            JsonContext.Default.LibraryInspection);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement jsonFailure = Assert.Single(
            document.RootElement
                .GetProperty("inspection_failures")
                .EnumerateArray());
        Assert.Equal(
            IntegrationSectionNames.Opportunities,
            jsonFailure.GetProperty("section").GetString());
        Assert.Equal(
            error.Message,
            jsonFailure.GetProperty("reason").GetString());
        Assert.False(
            document.RootElement.TryGetProperty(
                "integration_opportunities",
                out _));
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_PreservesNonManagedInputBehavior()
    {
        string path = Path.GetTempFileName();
        try
        {
            HashSet<InspectionQueryDefinition> queries =
                [AssemblyContextIntegrationOpportunitiesQuery.Definition];

            AssemblyContextIntegrationsBatch batch =
                Assert.IsType<AssemblyContextIntegrationsBatch>(
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        LibrarySections.CreateGroupQueryRegistry(),
                        [
                            new AssemblyContextIntegrationsInput(
                                path,
                                AssemblyResolutionProvenance.Local(
                                    "non-managed compatibility test")),
                        ]));

            Assert.Null(batch.EntryFor(path));
            Assert.Null(batch.AssemblyForInspection(path));
            Assert.Empty(queries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_SkipsInvalidFileBesideManagedInput()
    {
        string invalidPath = Path.GetTempFileName();
        string managedPath =
            typeof(LibraryFindingConsumerTests).Assembly.Location;
        try
        {
            HashSet<InspectionQueryDefinition> queries =
                [AssemblyContextIntegrationOpportunitiesQuery.Definition];

            AssemblyContextIntegrationsBatch batch =
                Assert.IsType<AssemblyContextIntegrationsBatch>(
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        LibrarySections.CreateGroupQueryRegistry(),
                        [
                            new AssemblyContextIntegrationsInput(
                                invalidPath,
                                AssemblyResolutionProvenance.Local(
                                    "invalid compatibility test")),
                            new AssemblyContextIntegrationsInput(
                                managedPath,
                                AssemblyResolutionProvenance.Local(
                                    "managed compatibility test")),
                        ]));

            Assert.Null(batch.EntryFor(invalidPath));
            Assert.Null(batch.AssemblyForInspection(invalidPath));
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                batch.EntryFor(managedPath));
            Assert.NotNull(batch.AssemblyForInspection(managedPath));
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_SkipsUnsupportedMetadataBesideManagedInput()
    {
        string unsupportedPath = Path.Combine(
            Path.GetTempPath(),
            $"unsupported-metadata-{Guid.NewGuid():N}.dll");
        string managedPath =
            typeof(LibraryFindingConsumerTests).Assembly.Location;
        File.WriteAllBytes(
            unsupportedPath,
            CreateUnsupportedMetadataImage());
        try
        {
            Assert.Throws<UnsupportedMetadataFormatException>(
                () => ResolvedAssemblyReference.CreateFromPathIfManaged(
                    unsupportedPath,
                    AssemblyResolutionProvenance.Local(
                        "unsupported compatibility test")));
            HashSet<InspectionQueryDefinition> queries =
                [AssemblyContextIntegrationsQuery.Definition];

            AssemblyContextIntegrationsBatch batch =
                Assert.IsType<AssemblyContextIntegrationsBatch>(
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        LibrarySections.CreateGroupQueryRegistry(),
                        [
                            new AssemblyContextIntegrationsInput(
                                unsupportedPath,
                                AssemblyResolutionProvenance.Local(
                                    "unsupported compatibility test")),
                            new AssemblyContextIntegrationsInput(
                                managedPath,
                                AssemblyResolutionProvenance.Local(
                                    "managed compatibility test")),
                        ]));

            Assert.Null(batch.EntryFor(unsupportedPath));
            Assert.Null(batch.AssemblyForInspection(unsupportedPath));
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                batch.EntryFor(managedPath));
            Assert.NotNull(batch.AssemblyForInspection(managedPath));
        }
        finally
        {
            File.Delete(unsupportedPath);
        }
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_SkipsMissingFileBesideManagedInput()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        string managedPath =
            typeof(LibraryFindingConsumerTests).Assembly.Location;
        HashSet<InspectionQueryDefinition> queries =
            [AssemblyContextIntegrationsQuery.Definition];

        AssemblyContextIntegrationsBatch batch =
            Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries,
                    LibrarySections.CreateGroupQueryRegistry(),
                    [
                        new AssemblyContextIntegrationsInput(
                            missingPath,
                            AssemblyResolutionProvenance.Local(
                                "missing compatibility test")),
                        new AssemblyContextIntegrationsInput(
                            managedPath,
                            AssemblyResolutionProvenance.Local(
                                "managed compatibility test")),
                    ]));

        Assert.Null(batch.EntryFor(missingPath));
        Assert.Null(batch.AssemblyForInspection(missingPath));
        Assert.IsType<AssemblyIntegrationsEntry.Available>(
            batch.EntryFor(managedPath));
        Assert.NotNull(batch.AssemblyForInspection(managedPath));
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_SkipsMalformedManagedFileBesideManagedInput()
    {
        string malformedPath = Path.GetTempFileName();
        string managedPath =
            typeof(LibraryFindingConsumerTests).Assembly.Location;
        try
        {
            File.WriteAllBytes(
                malformedPath,
                CorruptTableStream(File.ReadAllBytes(managedPath)));
            Assert.Throws<BadImageFormatException>(
                () => ResolvedAssemblyReference.CreateFromPathIfManaged(
                    malformedPath,
                    AssemblyResolutionProvenance.Local(
                        "malformed compatibility test")));
            HashSet<InspectionQueryDefinition> queries =
                [AssemblyContextIntegrationsQuery.Definition];

            AssemblyContextIntegrationsBatch batch =
                Assert.IsType<AssemblyContextIntegrationsBatch>(
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        LibrarySections.CreateGroupQueryRegistry(),
                        [
                            new AssemblyContextIntegrationsInput(
                                malformedPath,
                                AssemblyResolutionProvenance.Local(
                                    "malformed compatibility test")),
                            new AssemblyContextIntegrationsInput(
                                managedPath,
                                AssemblyResolutionProvenance.Local(
                                    "managed compatibility test")),
                        ]));

            Assert.Null(batch.EntryFor(malformedPath));
            Assert.Null(batch.AssemblyForInspection(malformedPath));
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                batch.EntryFor(managedPath));
            Assert.NotNull(batch.AssemblyForInspection(managedPath));
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    [Fact]
    public void AssemblyContextIntegrationsRunner_SkipsMetadataOverflowBesideManagedInput()
    {
        string malformedPath = Path.GetTempFileName();
        string managedPath =
            typeof(LibraryFindingConsumerTests).Assembly.Location;
        try
        {
            File.WriteAllBytes(
                malformedPath,
                CorruptMetadataStreamCount(
                    File.ReadAllBytes(managedPath)));
            Assert.Throws<OverflowException>(
                () => ResolvedAssemblyReference.CreateFromPathIfManaged(
                    malformedPath,
                    AssemblyResolutionProvenance.Local(
                        "metadata overflow compatibility test")));
            HashSet<InspectionQueryDefinition> queries =
                [AssemblyContextIntegrationsQuery.Definition];

            AssemblyContextIntegrationsBatch batch =
                Assert.IsType<AssemblyContextIntegrationsBatch>(
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        LibrarySections.CreateGroupQueryRegistry(),
                        [
                            new AssemblyContextIntegrationsInput(
                                malformedPath,
                                AssemblyResolutionProvenance.Local(
                                    "metadata overflow compatibility test")),
                            new AssemblyContextIntegrationsInput(
                                managedPath,
                                AssemblyResolutionProvenance.Local(
                                    "managed compatibility test")),
                        ]));

            Assert.Null(batch.EntryFor(malformedPath));
            Assert.Null(batch.AssemblyForInspection(malformedPath));
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                batch.EntryFor(managedPath));
            Assert.NotNull(batch.AssemblyForInspection(managedPath));
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    [Fact]
    public async Task AssemblyContextIntegrationsRunner_LendsTheQueriedSnapshotToLibraryInspection()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-integrations-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string targetPath = Path.Combine(tempDir, "Target.dll");
        string originalPath = typeof(LibraryFindingConsumerTests).Assembly.Location;
        string replacementPath = typeof(LibraryInspection).Assembly.Location;
        File.Copy(originalPath, targetPath);
        DateTime originalTimestamp =
            new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime replacementTimestamp =
            new(2025, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(targetPath, originalTimestamp);

        try
        {
            HashSet<InspectionQueryDefinition> queries =
                [AssemblyContextIntegrationOpportunitiesQuery.Definition];
            AssemblyContextIntegrationsBatch batch =
                Assert.IsType<AssemblyContextIntegrationsBatch>(
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        LibrarySections.CreateGroupQueryRegistry(),
                        [
                            new AssemblyContextIntegrationsInput(
                                targetPath,
                                AssemblyResolutionProvenance.Local(
                                    "snapshot reuse test")),
                        ]));
            AssemblyIntegrationsEntry entry =
                Assert.IsAssignableFrom<AssemblyIntegrationsEntry>(
                    batch.EntryFor(targetPath));
            AssemblyIntegrationOpportunitiesEntry opportunitiesEntry =
                Assert.IsAssignableFrom<
                    AssemblyIntegrationOpportunitiesEntry>(
                        batch.OpportunitiesEntryFor(targetPath));

            File.Copy(replacementPath, targetPath, overwrite: true);
            File.SetLastWriteTimeUtc(targetPath, replacementTimestamp);

            CoreCache.Initialize("dotnet-inspect-test");
            using var httpClient = new HttpClient();
            LibraryInspection inspection = Assert.IsType<LibraryInspection>(
                await LibraryMetadataService.InspectAsync(
                    targetPath,
                    new DotnetInspector.Options.LibraryOptions(),
                    new VerboseLogger(enabled: false),
                    packageName: null,
                    packageVersion: null,
                    httpClient,
                    assemblyReference: Assert.IsType<ResolvedAssemblyReference>(
                        batch.AssemblyForInspection(targetPath)),
                    integrationsEntry: entry,
                    integrationOpportunitiesEntry: opportunitiesEntry));

            Assert.Equal(
                entry.Subject.Identity.Name,
                inspection.AssemblyInfo!.AssemblyName);
            Assert.NotEqual(
                Path.GetFileNameWithoutExtension(replacementPath),
                inspection.AssemblyInfo.AssemblyName);
            Assert.Equal(originalTimestamp, inspection.LastModified);
            Assert.Same(entry, inspection.AssemblyIntegrationsEntry);
            Assert.Same(
                opportunitiesEntry,
                inspection.AssemblyIntegrationOpportunitiesEntry);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FailedFindingInspection_DoesNotRenderAsEmpty()
    {
        var inspection = new LibraryInspection
        {
            SwitchInspection = new FindingInspection<SwitchInfo>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.SwitchDescriptor,
                    "scan failed")),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.SwitchInspection.Findings());
        Assert.Contains("scan failed", exception.Message);
        Assert.Null(inspection.Switches);

        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal("Switches", failure.Section);
        Assert.Equal("scan failed", failure.Reason);

        var json = JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection);
        Assert.Contains("\"inspection_failures\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"switches\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedClassifiedMethodInspection_DoesNotRenderPresentationRows()
    {
        var inspection = new LibraryInspection
        {
            ClassifiedMethodInspection = new FindingInspection<ClassifiedMethodObservation>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.ClassifiedMethodDescriptor,
                    "method scan failed")),
            UnsafeMethods =
            [
                new ClassifiedMethodSummary
                {
                    MethodName = "M",
                    DeclaringType = "Test.Type",
                    Signature = "void M()",
                },
            ],
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.ClassifiedMethodInspection.Findings());
        Assert.Contains("method scan failed", exception.Message);
        Assert.Null(inspection.UnsafeMethods);
        Assert.Equal(0, inspection.UnsafeMethodCount);

        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal("Classified Methods", failure.Section);
        Assert.Equal("method scan failed", failure.Reason);
    }

    [Fact]
    public void FailedExtensionInspection_DoesNotRenderPresentationRows()
    {
        var member = FindingTestData.ExtensionMember("Ext", "Test.Target");
        var inspection = new LibraryInspection();
        inspection.SetExtensionMemberInspection(
            new FindingInspection<ExtensionMemberObservation>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.ExtensionMemberDescriptor,
                    "extension scan failed")),
            [member]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.ExtensionMemberInspection.Findings());
        Assert.Contains("extension scan failed", exception.Message);
        Assert.Null(inspection.ExtensionMethods);

        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal("Extension Methods", failure.Section);
        Assert.Equal("extension scan failed", failure.Reason);
    }

    [Fact]
    public void FindingJsonProjections_AreCachedAndInvalidatedWithTheirInspection()
    {
        var inspection = new LibraryInspection
        {
            ResourceInspection = MetadataFindings.InspectResources(
                [new ManifestResourceInfo("First", true, true, 1)],
                FindingTestData.Subject),
            EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
                [new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.AI, "Chat", "Test.Chat")],
                FindingTestData.Subject),
            SwitchInspection = new FindingInspection<SwitchInfo>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.SwitchDescriptor,
                    "first failure")),
        };

        var resources = inspection.Resources;
        var ai = inspection.AI;
        var failures = inspection.InspectionFailures;

        Assert.Same(resources, inspection.Resources);
        Assert.Same(ai, inspection.AI);
        Assert.Same(failures, inspection.InspectionFailures);

        inspection.ResourceInspection = MetadataFindings.InspectResources(
            [new ManifestResourceInfo("Second", true, true, 2)],
            FindingTestData.Subject);
        inspection.EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
            [new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Logging, "Logger", "Test.Logger")],
            FindingTestData.Subject);
        inspection.SwitchInspection = MetadataFindings.InspectSwitches([], FindingTestData.Subject);

        Assert.NotSame(resources, inspection.Resources);
        Assert.Equal("Second", Assert.Single(inspection.Resources!).Name);
        Assert.Null(inspection.AI);
        Assert.Null(inspection.InspectionFailures);
    }

    [Fact]
    public void ExplicitSelectionFailureWarnings_AreCorrelatedToAffectedSections()
    {
        Assert.True(LibraryCommand.FailureAffectsSection("Classified Methods", "P/Invoke Methods"));
        Assert.True(LibraryCommand.FailureAffectsSection("Extension Methods", "Library Info"));
        Assert.True(LibraryCommand.FailureAffectsSection("Switches", "Library Info"));
        Assert.True(LibraryCommand.FailureAffectsSection(
            LibraryIntegrationCatalog.RollupName,
            IntegrationSectionNames.AI));
        Assert.False(LibraryCommand.FailureAffectsSection("Custom Attributes", "Type Forwarders"));
    }

    static void AssertFailure<T>(
        FindingInspection<T>? inspection,
        FindingDescriptor expectedDescriptor)
        where T : notnull
    {
        var failure = Assert.IsType<FindingInspection<T>.Failed>(inspection?.Value);
        Assert.Same(expectedDescriptor, failure.Error.Descriptor);
        Assert.False(string.IsNullOrWhiteSpace(failure.Error.Reason));
    }

    static byte[] CorruptTableStream(byte[] bytes)
    {
        int metadataStart;
        using (var peReader =
               new PEReader(new MemoryStream(bytes, writable: false)))
        {
            metadataStart = peReader.PEHeaders.MetadataStartOffset;
        }

        int versionLength = BitConverter.ToInt32(
            bytes,
            metadataStart + 12);
        int cursor = metadataStart + 16 + AlignTo4(versionLength);
        int streamCount = BitConverter.ToUInt16(bytes, cursor + 2);
        cursor += 4;

        for (int i = 0; i < streamCount; i++)
        {
            int sizeOffset = cursor + 4;
            int nameStart = cursor + 8;
            int nameEnd = Array.IndexOf(bytes, (byte)0, nameStart);
            string name = Encoding.ASCII.GetString(
                bytes,
                nameStart,
                nameEnd - nameStart);
            if (name is "#~" or "#-")
            {
                BitConverter.GetBytes(4).CopyTo(bytes, sizeOffset);
                return bytes;
            }

            cursor = nameStart + AlignTo4(nameEnd - nameStart + 1);
        }

        throw new InvalidOperationException(
            "The test assembly has no metadata table stream.");
    }

    static byte[] CreateUnsupportedMetadataImage()
    {
        const int fixedMetadataRootPrefixLength = 16;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var imageBuilder = new BlobBuilder();
        peBuilder.Serialize(imageBuilder);
        byte[] image = imageBuilder.ToArray();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(
                peReader.PEHeaders.CorHeaderStartOffset + 12,
                sizeof(int)),
            fixedMetadataRootPrefixLength + versionLength);
        return image;
    }

    static byte[] CorruptMetadataStreamCount(byte[] bytes)
    {
        using var peReader = new PEReader(
            new MemoryStream(bytes, writable: false));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(metadataStart + 12, sizeof(int)));
        int streamCountOffset =
            metadataStart
            + 16
            + versionLength
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(streamCountOffset, sizeof(ushort)),
            ushort.MaxValue);
        return bytes;
    }

    static int AlignTo4(int value)
        => (value + 3) & ~3;
}
