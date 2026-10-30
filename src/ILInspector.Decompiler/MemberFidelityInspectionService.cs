using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Inspector.Findings;

namespace ILInspector.Decompiler;

/// <summary>
/// Produces the typed fidelity-cause census for one exact MethodDef while
/// preserving caller-owned assembly binding and optional Portable PDB evidence.
/// </summary>
public static class MemberFidelityInspectionService
{
    public static FindingInspection<DecompilerFidelityCause> Inspect(
        int methodDefinitionToken,
        bool methodHasBody,
        FindingSubject subject,
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        ImmutableArray<byte>? portablePdbImage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        cancellationToken.ThrowIfCancellationRequested();

        if (!methodHasBody)
            return DecompilerFindings.InspectFidelityCauses(null, subject);

        try
        {
            using MetadataSource source =
                portablePdbImage is { } supplied
                    ? MetadataSource.OpenWithSuppliedPortablePdb(
                        assembly,
                        supplied,
                        bindingPolicy,
                        cancellationToken: cancellationToken)
                    : MetadataSource.Open(
                        assembly,
                        externalPdbPath: null,
                        bindingPolicy: bindingPolicy);
            cancellationToken.ThrowIfCancellationRequested();
            if (!source.ContainsMethodDefinition(methodDefinitionToken))
            {
                return Failed(
                    subject,
                    $"0x{methodDefinitionToken:X8} is not a MethodDef in "
                        + $"{assembly.Identity.Name}.");
            }

            IrFunction? function =
                IrImporter.Import(source, methodDefinitionToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (function is null)
            {
                return Failed(
                    subject,
                    "Decompiler import returned no method body.");
            }

            DecompilerResult projection =
                CSharpPrinter.PrintRaised(
                    function,
                    target => IrImporter.Import(source, target),
                    typesProvablyDisjoint:
                        source.AreProvablyDisjoint);
            cancellationToken.ThrowIfCancellationRequested();
            return projection.Succeeded
                ? DecompilerFindings.InspectFidelityCauses(
                    function,
                    subject)
                : Failed(
                    subject,
                    string.Join(
                        "; ",
                        projection.Diagnostics.Select(
                            static diagnostic =>
                                diagnostic.ToString())));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            return Failed(
                subject,
                $"{failure.GetType().Name}: {failure.Message}");
        }
    }

    static FindingInspection<DecompilerFidelityCause> Failed(
        FindingSubject subject,
        string detail) =>
        new FindingInspection<DecompilerFidelityCause>.Failed(
            new InspectionError(
                subject,
                DecompilerFindings.FidelityInspectionDescriptor,
                detail));
}
