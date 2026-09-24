using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;
using Inspector.Artifacts;

namespace DotnetInspector.Queries.Tests;

public sealed class MetadataRelationGraphAdapterTests
{
    [Fact]
    public void ExtensionAndSignatureFamiliesBindTheSameMemberFocus()
    {
        string path = typeof(MetadataFindings).Assembly.Location;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Extensions,
                            MetadataRelationFamily.Signatures,
                        ],
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        ResolvedAssemblyReference assembly =
            Resolved(path, available.Result.Receipt.Assembly!);

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(
                assembly,
                available.Result);

        InspectionGraphOccurrence extension =
            Assert.Single(
                projection.Occurrences,
                occurrence =>
                    ReferenceEquals(
                        occurrence.Relationship,
                        MetadataRelationGraphCatalog.Extension)
                    && occurrence.SourceSubject
                        is InspectionGraphSubject.MemberSubject
                        {
                            Identity:
                                InspectionGraphMemberIdentity.AcquiredApi
                                {
                                    Member.CanonicalSignature:
                                        "M:ILInspector.Metadata.MetadataReaderExtensions.GetFullTypeName(System.Reflection.Metadata.MetadataReader,System.Reflection.Metadata.TypeDefinition)",
                                },
                        });
        InspectionGraphOccurrence signature =
            Assert.Single(
                projection.Occurrences,
                occurrence =>
                    ReferenceEquals(
                        occurrence.Relationship,
                        MetadataRelationGraphCatalog.Accepts)
                    && Equals(
                        occurrence.SourceSubject,
                        extension.SourceSubject)
                    && Equals(
                        occurrence.TargetSubject,
                        extension.TargetSubject));
        Assert.NotSame(extension, signature);

        StructuralSubjectTestData.PackageContext package =
            StructuralSubjectTestData.Package(
                new(
                    "ilinspector.metadata",
                    "1.0.0",
                    "local",
                    "net11.0",
                    runtimeIdentifier: null));
        SubjectRelationFocusCorrespondence correspondence =
            SubjectRelationFocusCorrespondence.Create(
                package.Subject,
                SubjectRelationPopulationAuthority.Capture(
                    package.Workspace,
                    new object()),
                extension.SourceSubject,
                InspectionGraphEndpointRole.Source,
                new object());

        ImmutableArray<SubjectRelationRow> rows =
            MetadataRelationGraphAdapter.BindRows(
                projection,
                correspondence);
        Assert.Single(
            rows,
            static row => row.Form == SubjectRelationForm.Extension);
        Assert.Contains(
            rows,
            static row => row.Form == SubjectRelationForm.Signature);
    }

    [Fact]
    public void GenericExtensionPropertyReceiversRetainExactTypeContext()
    {
        string path =
            typeof(TestExtensions.DualScopeExtensions).Assembly.Location;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Extensions],
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        ResolvedAssemblyReference assembly =
            Resolved(path, available.Result.Receipt.Assembly!);

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(
                assembly,
                available.Result);

        InspectionGraphOccurrence referenceItems =
            ExtensionProperty(projection, "ReferenceItems");
        InspectionGraphOccurrence valueItems =
            ExtensionProperty(projection, "ValueItems");
        var referenceEvidence =
            Assert.IsType<MetadataExtensionGraphEvidence>(
                referenceItems.Evidence).Evidence;
        var valueEvidence =
            Assert.IsType<MetadataExtensionGraphEvidence>(
                valueItems.Evidence).Evidence;
        Assert.NotEqual(
            referenceEvidence.ReceiverContextType,
            valueEvidence.ReceiverContextType);
        Assert.Equal(
            referenceEvidence.DeclaringType,
            valueEvidence.DeclaringType);

        var referenceTarget =
            Assert.IsType<InspectionGraphTypeIdentity.MetadataShape>(
                Assert.IsType<InspectionGraphSubject.TypeSubject>(
                    referenceItems.TargetSubject).Identity);
        var valueTarget =
            Assert.IsType<InspectionGraphTypeIdentity.MetadataShape>(
                Assert.IsType<InspectionGraphSubject.TypeSubject>(
                    valueItems.TargetSubject).Identity);
        Assert.NotEqual(referenceTarget, valueTarget);
        Assert.Equal(
            referenceEvidence.ReceiverContextType,
            referenceTarget.GenericContext?.DeclaringType);
        Assert.Equal(
            valueEvidence.ReceiverContextType,
            valueTarget.GenericContext?.DeclaringType);
        Assert.Null(referenceTarget.GenericContext?.DeclaringMethod);
        Assert.Null(valueTarget.GenericContext?.DeclaringMethod);
    }

    [Fact]
    public void SignatureProjectionRetainsExactShapeCoverageAndBinding()
    {
        var assemblyIdentity = new AssemblyReferenceIdentity(
            "Sample.Library",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        RealizedMemberCoordinate.Package coordinate = new(
            "sample.library",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                assemblyIdentity,
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Package(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Framework,
                    coordinate.RuntimeIdentifier));
        Guid moduleVersionId = Guid.NewGuid();
        MetadataTypeDefinitionName declaringType =
            TypeName("Sample", "Converter");
        const string canonicalSignature =
            "M:Sample.Converter.Convert(System.ReadOnlySpan{System.Byte})";
        var member = new MemberAnchor(
            "Convert~1234567890",
            canonicalSignature,
            MemberAnchor.ComputeFingerprint(canonicalSignature),
            "Sample.Converter",
            "Convert");
        var shape = new MetadataTypeIdentity.SzArray(
            new MetadataTypeIdentity.Primitive(
                new InertString(TextPolicy.Field, "byte")));
        var signatureEvidence =
            new MetadataSignatureRelationEvidence(
                MetadataTypeDefinitionAddress.FromToken(
                    moduleVersionId,
                    0x02000001),
                declaringType,
                new(
                    moduleVersionId,
                    MetadataTokens.MethodDefinitionHandle(1)),
                member,
                MetadataSignatureRelationKind.Accepts,
                ParameterIndex: 0,
                shape);
        var result = new MetadataRelationInspectionResult(
            new(
                moduleVersionId,
                assemblyIdentity,
                [MetadataRelationFamily.Signatures],
                new MetadataOperationCounters(10)),
            NotRequested<MetadataHierarchyRelationEvidence>(),
            NotRequested<MetadataExtensionRelationEvidence>(),
            NotRequested<
                MetadataAssemblyReferenceRelationEvidence>(),
            new(
                true,
                MetadataRelationFamilyDisposition.Complete,
                new(1, 1, 0, 0, 0),
                [signatureEvidence]));

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(assembly, result);

        InspectionGraphOccurrence occurrence =
            Assert.Single(projection.Occurrences);
        Assert.Same(
            MetadataRelationGraphCatalog.Accepts,
            occurrence.Relationship);
        var source = Assert.IsType<
            InspectionGraphSubject.MemberSubject>(
                occurrence.SourceSubject);
        var sourceIdentity = Assert.IsType<
            InspectionGraphMemberIdentity.AcquiredApi>(
                source.Identity);
        Assert.Same(assembly.Registration, sourceIdentity.Registration);
        Assert.Equal(member, sourceIdentity.Member);
        var target = Assert.IsType<
            InspectionGraphSubject.TypeSubject>(
                occurrence.TargetSubject);
        var targetIdentity = Assert.IsType<
            InspectionGraphTypeIdentity.MetadataShape>(
                target.Identity);
        Assert.Same(
            assembly.Registration,
            targetIdentity.Registration);
        Assert.Equal(shape, targetIdentity.Type);

        SubjectRelationProducerOutcome producer =
            Assert.Single(projection.Producers);
        Assert.Same(
            MetadataRelationGraphAdapter.SignatureQuery,
            producer.Producer);
        Assert.Equal(
            SubjectRelationProducerDisposition.Complete,
            producer.Disposition);
        Assert.Equal(1, producer.Coverage.Considered);
        Assert.Equal(1, producer.Coverage.Examined);

        StructuralSubjectTestData.PackageContext package =
            StructuralSubjectTestData.Package(coordinate);
        SubjectRelationPopulationAuthority population =
            SubjectRelationPopulationAuthority.Capture(
                package.Workspace,
                new object());
        SubjectRelationFocusCorrespondence correspondence =
            SubjectRelationFocusCorrespondence.Create(
                package.Subject,
                population,
                occurrence.SourceSubject,
                InspectionGraphEndpointRole.Source,
                new object());

        SubjectRelationRow row =
            Assert.Single(
                MetadataRelationGraphAdapter.BindRows(
                    projection,
                    correspondence));

        Assert.Equal(SubjectRelationForm.Signature, row.Form);
        Assert.Equal(SubjectRelationDirection.Outgoing, row.Direction);
        Assert.Same(occurrence.SourceSubject, row.Source);
        Assert.Same(occurrence.TargetSubject, row.Target);
        Assert.Single(row.Occurrences);
    }

    [Fact]
    public void PartialMetadataOutcomeRemainsPartialWithTypedDiagnostic()
    {
        var identity = new AssemblyReferenceIdentity(
            "Sample.Library",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Local("relation test"));
        var diagnostic = new MetadataRelationDiagnostic(
            MetadataRelationFamily.Hierarchy,
            MetadataRelationDiagnosticKind.Limit,
            MetadataToken: null,
            "limit",
            MetadataOperationDimension.RelationshipEdges,
            BudgetLimit: 0,
            AttemptedCharge: 1);
        var result = new MetadataRelationInspectionResult(
            new(
                Guid.NewGuid(),
                identity,
                [MetadataRelationFamily.Hierarchy],
                new MetadataOperationCounters(10)),
            new(
                true,
                MetadataRelationFamilyDisposition.Partial,
                new(1, 0, 0, 0, 1),
                [],
                [diagnostic]),
            NotRequested<MetadataExtensionRelationEvidence>(),
            NotRequested<
                MetadataAssemblyReferenceRelationEvidence>(),
            NotRequested<MetadataSignatureRelationEvidence>());

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(assembly, result);

        Assert.Empty(projection.Occurrences);
        SubjectRelationProducerOutcome producer =
            Assert.Single(projection.Producers);
        Assert.Equal(
            SubjectRelationProducerDisposition.Partial,
            producer.Disposition);
        Assert.Equal(1, producer.Coverage.Limited);
        SubjectRelationProducerDiagnostic mapped =
            Assert.Single(producer.Diagnostics);
        Assert.Equal(
            SubjectRelationProducerDiagnosticKind.Limit,
            mapped.Kind);
        Assert.Same(diagnostic, mapped.Evidence);
    }

    [Fact]
    public void DuplicateReferenceRowsRemainDistinctOccurrences()
    {
        var sourceIdentity = new AssemblyReferenceIdentity(
            "Sample.Library",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var targetIdentity = new AssemblyReferenceIdentity(
            "Sample.Dependency",
            new Version(2, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        RealizedMemberCoordinate.Package coordinate = new(
            "sample.library",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                sourceIdentity,
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Package(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Framework,
                    coordinate.RuntimeIdentifier));
        var result = new MetadataRelationInspectionResult(
            new(
                Guid.NewGuid(),
                sourceIdentity,
                [MetadataRelationFamily.AssemblyReferences],
                new MetadataOperationCounters(10)),
            NotRequested<MetadataHierarchyRelationEvidence>(),
            NotRequested<MetadataExtensionRelationEvidence>(),
            new(
                true,
                MetadataRelationFamilyDisposition.Complete,
                new(2, 2, 0, 0, 0),
                [
                    new(sourceIdentity, targetIdentity, 0x23000001),
                    new(sourceIdentity, targetIdentity, 0x23000002),
                ]),
            NotRequested<MetadataSignatureRelationEvidence>());

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(assembly, result);

        Assert.Equal(2, projection.Occurrences.Length);
        Assert.All(
            projection.Occurrences,
            occurrence => Assert.IsType<MetadataReferenceGraphEvidence>(
                occurrence.Evidence));
        StructuralSubjectTestData.PackageContext package =
            StructuralSubjectTestData.Package(coordinate);
        SubjectRelationPopulationAuthority population =
            SubjectRelationPopulationAuthority.Capture(
                package.Workspace,
                new object());
        SubjectRelationFocusCorrespondence correspondence =
            SubjectRelationFocusCorrespondence.Create(
                package.Subject,
                population,
                projection.Occurrences[0].SourceSubject,
                InspectionGraphEndpointRole.Source,
                new object());

        SubjectRelationRow row =
            Assert.Single(
                MetadataRelationGraphAdapter.BindRows(
                    projection,
                    correspondence));

        Assert.Equal(
            SubjectRelationForm.AssemblyReference,
            row.Form);
        Assert.Equal(2, row.Occurrences.Length);
        Assert.NotEqual(
            row.Occurrences[0].Identity,
            row.Occurrences[1].Identity);
    }

    [Fact]
    public void GenericParametersRetainNestedDeclarationContextOnly()
    {
        var identity = new AssemblyReferenceIdentity(
            "Sample.Library",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Local("relation test"));
        Guid moduleVersionId = Guid.NewGuid();
        MetadataTypeDefinitionAddress declaringType =
            MetadataTypeDefinitionAddress.FromToken(
                moduleVersionId,
                0x02000001);
        MetadataTypeDefinitionName typeName =
            TypeName("Sample", "GenericMethods");
        var nestedMethodParameter = new MetadataTypeIdentity.SzArray(
            new MetadataTypeIdentity.GenericParameter(
                IsMethodParameter: true,
                Index: 0));
        var concrete = new MetadataTypeIdentity.Primitive(
            new InertString(TextPolicy.Field, "string"));
        MetadataSignatureRelationEvidence firstGeneric =
            Signature(
                moduleVersionId,
                declaringType,
                typeName,
                methodRow: 1,
                "First",
                nestedMethodParameter);
        MetadataSignatureRelationEvidence secondGeneric =
            Signature(
                moduleVersionId,
                declaringType,
                typeName,
                methodRow: 2,
                "Second",
                nestedMethodParameter);
        MetadataSignatureRelationEvidence firstConcrete =
            Signature(
                moduleVersionId,
                declaringType,
                typeName,
                methodRow: 1,
                "First",
                concrete);
        MetadataSignatureRelationEvidence secondConcrete =
            Signature(
                moduleVersionId,
                declaringType,
                typeName,
                methodRow: 2,
                "Second",
                concrete);
        var result = new MetadataRelationInspectionResult(
            new(
                moduleVersionId,
                identity,
                [MetadataRelationFamily.Signatures],
                new MetadataOperationCounters(10)),
            NotRequested<MetadataHierarchyRelationEvidence>(),
            NotRequested<MetadataExtensionRelationEvidence>(),
            NotRequested<
                MetadataAssemblyReferenceRelationEvidence>(),
            new(
                true,
                MetadataRelationFamilyDisposition.Complete,
                new(4, 4, 0, 0, 0),
                [
                    firstGeneric,
                    secondGeneric,
                    firstConcrete,
                    secondConcrete,
                ]));

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(assembly, result);

        InspectionGraphTypeIdentity.MetadataShape[] genericTargets =
        [
            .. projection.Occurrences
                .Take(2)
                .Select(static occurrence =>
                    Assert.IsType<
                        InspectionGraphTypeIdentity.MetadataShape>(
                            Assert.IsType<
                                InspectionGraphSubject.TypeSubject>(
                                    occurrence.TargetSubject).Identity)),
        ];
        Assert.NotEqual(genericTargets[0], genericTargets[1]);
        Assert.Equal(
            firstGeneric.Method,
            genericTargets[0].GenericContext?.DeclaringMethod);
        Assert.Equal(
            secondGeneric.Method,
            genericTargets[1].GenericContext?.DeclaringMethod);

        InspectionGraphTypeIdentity.MetadataShape[] concreteTargets =
        [
            .. projection.Occurrences
                .Skip(2)
                .Select(static occurrence =>
                    Assert.IsType<
                        InspectionGraphTypeIdentity.MetadataShape>(
                            Assert.IsType<
                                InspectionGraphSubject.TypeSubject>(
                                    occurrence.TargetSubject).Identity)),
        ];
        Assert.Equal(concreteTargets[0], concreteTargets[1]);
        Assert.Null(concreteTargets[0].GenericContext);
    }

    [Fact]
    public void TypeGenericParametersBindToDeclaringTypeNotMethod()
    {
        var identity = new AssemblyReferenceIdentity(
            "Sample.Library",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Local("relation test"));
        Guid moduleVersionId = Guid.NewGuid();
        MetadataTypeDefinitionAddress firstType =
            MetadataTypeDefinitionAddress.FromToken(
                moduleVersionId,
                0x02000001);
        MetadataTypeDefinitionAddress secondType =
            MetadataTypeDefinitionAddress.FromToken(
                moduleVersionId,
                0x02000002);
        var nestedTypeParameter =
            new MetadataTypeIdentity.GenericInstance(
                new(
                    new(
                        MetadataTypeScopeKind.AssemblyReference,
                        Guid.Empty,
                        ModuleName: null,
                        new(
                            new InertString(
                                TextPolicy.Field,
                                "System.Collections"),
                            new Version(1, 0),
                            Culture: null,
                            PublicKeyToken: null)),
                    new InertString(
                        TextPolicy.Field,
                        "System.Collections.Generic"),
                    [new InertString(TextPolicy.Field, "IEnumerable`1")],
                    [1]),
                IsValueType: false,
                [
                    new MetadataTypeIdentity.GenericParameter(
                        IsMethodParameter: false,
                        Index: 0),
                ]);
        MetadataSignatureRelationEvidence firstMethod =
            Signature(
                moduleVersionId,
                firstType,
                TypeName("Sample", "FirstType"),
                methodRow: 1,
                "First",
                nestedTypeParameter);
        MetadataSignatureRelationEvidence secondMethod =
            Signature(
                moduleVersionId,
                firstType,
                TypeName("Sample", "FirstType"),
                methodRow: 2,
                "Second",
                nestedTypeParameter);
        MetadataSignatureRelationEvidence otherType =
            Signature(
                moduleVersionId,
                secondType,
                TypeName("Sample", "SecondType"),
                methodRow: 3,
                "Third",
                nestedTypeParameter);
        var result = new MetadataRelationInspectionResult(
            new(
                moduleVersionId,
                identity,
                [MetadataRelationFamily.Signatures],
                new MetadataOperationCounters(10)),
            NotRequested<MetadataHierarchyRelationEvidence>(),
            NotRequested<MetadataExtensionRelationEvidence>(),
            NotRequested<
                MetadataAssemblyReferenceRelationEvidence>(),
            new(
                true,
                MetadataRelationFamilyDisposition.Complete,
                new(3, 3, 0, 0, 0),
                [firstMethod, secondMethod, otherType]));

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(assembly, result);
        InspectionGraphTypeIdentity.MetadataShape[] targets =
        [
            .. projection.Occurrences.Select(static occurrence =>
                Assert.IsType<
                    InspectionGraphTypeIdentity.MetadataShape>(
                        Assert.IsType<
                            InspectionGraphSubject.TypeSubject>(
                                occurrence.TargetSubject).Identity)),
        ];

        Assert.Equal(targets[0], targets[1]);
        Assert.NotEqual(targets[0], targets[2]);
        Assert.Equal(firstType, targets[0].GenericContext?.DeclaringType);
        Assert.Null(targets[0].GenericContext?.DeclaringMethod);
        Assert.Equal(secondType, targets[2].GenericContext?.DeclaringType);
    }

    [Fact]
    public void ReceiptMustMatchBoundAcquisitionMvid()
    {
        string path = typeof(MetadataFindings).Assembly.Location;
        AssemblyReferenceIdentity identity = ReadIdentity(path);
        ResolvedAssemblyReference assembly = Resolved(path, identity);
        Assert.NotNull(assembly.Registration.ModuleVersionId);
        var result = new MetadataRelationInspectionResult(
            new(
                Guid.NewGuid(),
                identity,
                [MetadataRelationFamily.Hierarchy],
                new MetadataOperationCounters(0)),
            new(
                true,
                MetadataRelationFamilyDisposition.Complete,
                new(0, 0, 0, 0, 0),
                []),
            NotRequested<MetadataExtensionRelationEvidence>(),
            NotRequested<
                MetadataAssemblyReferenceRelationEvidence>(),
            NotRequested<MetadataSignatureRelationEvidence>());

        Assert.Throws<ArgumentException>(
            () => MetadataRelationGraphAdapter.Project(
                assembly,
                result));
    }

    [Fact]
    public void FailedOutcomeWithUnavailableReceiptMvidRemainsVisible()
    {
        string path = typeof(MetadataFindings).Assembly.Location;
        AssemblyReferenceIdentity identity = ReadIdentity(path);
        ResolvedAssemblyReference assembly = Resolved(path, identity);
        Assert.NotNull(assembly.Registration.ModuleVersionId);
        var diagnostic = new MetadataRelationDiagnostic(
            MetadataRelationFamily.Hierarchy,
            MetadataRelationDiagnosticKind.MalformedMetadata,
            MetadataToken: null,
            "invalid MVID");
        var result = new MetadataRelationInspectionResult(
            new(
                ModuleVersionId: null,
                identity,
                [MetadataRelationFamily.Hierarchy],
                new MetadataOperationCounters(0)),
            new(
                true,
                MetadataRelationFamilyDisposition.Failed,
                new(1, 0, 0, 1, 0),
                [],
                [diagnostic]),
            NotRequested<MetadataExtensionRelationEvidence>(),
            NotRequested<
                MetadataAssemblyReferenceRelationEvidence>(),
            NotRequested<MetadataSignatureRelationEvidence>());

        MetadataRelationGraphProjection projection =
            MetadataRelationGraphAdapter.Project(assembly, result);

        Assert.Empty(projection.Occurrences);
        SubjectRelationProducerOutcome producer =
            Assert.Single(projection.Producers);
        Assert.Equal(
            SubjectRelationProducerDisposition.Failed,
            producer.Disposition);
        Assert.Same(
            diagnostic,
            Assert.Single(producer.Diagnostics).Evidence);
    }

    [Fact]
    public void UnavailableReceiptMvidRejectsPublishedEvidence()
    {
        var sourceIdentity = new AssemblyReferenceIdentity(
            "Sample.Library",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var targetIdentity = new AssemblyReferenceIdentity(
            "Sample.Dependency",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                sourceIdentity,
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Local("relation test"));
        var result = new MetadataRelationInspectionResult(
            new(
                ModuleVersionId: null,
                sourceIdentity,
                [MetadataRelationFamily.AssemblyReferences],
                new MetadataOperationCounters(0)),
            NotRequested<MetadataHierarchyRelationEvidence>(),
            NotRequested<MetadataExtensionRelationEvidence>(),
            new(
                true,
                MetadataRelationFamilyDisposition.Complete,
                new(1, 1, 0, 0, 0),
                [new(sourceIdentity, targetIdentity, 0x23000001)]),
            NotRequested<MetadataSignatureRelationEvidence>());

        Assert.Throws<ArgumentException>(
            () => MetadataRelationGraphAdapter.Project(
                assembly,
                result));
    }

    private static MetadataRelationFamilyResult<TEvidence>
        NotRequested<TEvidence>() =>
        new(false, null, null, []);

    private static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    private static MetadataSignatureRelationEvidence Signature(
        Guid moduleVersionId,
        MetadataTypeDefinitionAddress declaringType,
        MetadataTypeDefinitionName typeName,
        int methodRow,
        string memberName,
        MetadataTypeIdentity shape)
    {
        string canonicalSignature =
            $"M:Sample.GenericMethods.{memberName}``1(``0)";
        return new(
            declaringType,
            typeName,
            new(
                moduleVersionId,
                MetadataTokens.MethodDefinitionHandle(methodRow)),
            new(
                $"{memberName}~1234567890",
                canonicalSignature,
                MemberAnchor.ComputeFingerprint(canonicalSignature),
                "Sample.GenericMethods",
                memberName),
            MetadataSignatureRelationKind.Returns,
            ParameterIndex: null,
            shape);
    }

    private static InspectionGraphOccurrence ExtensionProperty(
        MetadataRelationGraphProjection projection,
        string memberName) =>
        Assert.Single(
            projection.Occurrences,
            occurrence =>
                ReferenceEquals(
                    occurrence.Relationship,
                    MetadataRelationGraphCatalog.Extension)
                && occurrence.SourceSubject
                    is InspectionGraphSubject.MemberSubject
                    {
                        Identity:
                            InspectionGraphMemberIdentity.AcquiredApi
                            {
                                Member.MemberName: var actualName,
                            },
                    }
                && actualName == memberName);

    private static ResolvedAssemblyReference Resolved(
        string path,
        AssemblyReferenceIdentity identity)
    {
        ArtifactAcquisitionRegistration registration =
            RegisterArtifact(path);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactWithFallbackIdentity(
                registration,
                () => File.OpenRead(path),
                identity,
                AssemblyResolutionProvenance.Local("relation test"),
                out bool usedFallbackIdentity);
        Assert.False(usedFallbackIdentity);
        return assembly;
    }

    private static ArtifactAcquisitionRegistration RegisterArtifact(
        string path)
    {
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                TestArtifactProvenance.Instance,
                _ => File.OpenRead(path));
        }

        authority.CreateRetainedContent(
            contribution.Registration,
            _ => File.OpenRead(path));
        authority.CompleteAdmission(admission);
        return contribution.Registration;
    }

    private static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            image.GetMetadataReader());
    }

    private sealed class TestArtifactProvenance : IArtifactProvenance
    {
        internal static TestArtifactProvenance Instance { get; } = new();
    }
}
