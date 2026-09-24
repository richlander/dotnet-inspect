using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Queries.Tests;

public sealed class MetadataRelationGraphAdapterTests
{
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
}
