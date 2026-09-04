using System.Reflection.Metadata;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

internal static class ResearchInputImageValidation
{
    internal static ResearchTargetInputValidationEvidence Capture(
        MetadataReader reader,
        ImplementationComparisonInputOccurrence occurrence)
    {
        bool isAssembly = reader.IsAssembly;
        AssemblyReferenceIdentity? identity = isAssembly
            ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
            : null;
        Guid moduleVersionId =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        return new ResearchTargetInputValidationEvidence(
            ReadFailed: false,
            isAssembly,
            identity,
            moduleVersionId,
            occurrence.Assembly.Registration.ModuleVersionId,
            reader.MethodDefinitions.Count,
            Surface: null);
    }

    internal static ResearchTargetDiagnosticKind? Validate(
        ResearchTargetInputValidationEvidence evidence,
        ImplementationComparisonInputOccurrence occurrence)
    {
        LibraryBodyModuleIdentity analysis = occurrence.BodyIndex.ModuleIdentity;
        if (!evidence.IsAssembly)
            return ResearchTargetDiagnosticKind.StandaloneModule;
        if (analysis.AssemblyIdentity is null)
            return ResearchTargetDiagnosticKind.AssemblyIdentityMismatch;

        AssemblyReferenceIdentity live = evidence.LiveAssemblyIdentity!;
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                live,
                occurrence.Assembly.Identity)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                live,
                analysis.AssemblyIdentity))
        {
            return ResearchTargetDiagnosticKind.AssemblyIdentityMismatch;
        }

        return evidence.LiveModuleVersionId == analysis.ModuleVersionId
                && (evidence.ArtifactModuleVersionId is not Guid artifact
                    || artifact == evidence.LiveModuleVersionId)
            ? null
            : ResearchTargetDiagnosticKind.ModuleIdentityMismatch;
    }
}
