using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace DotnetInspector.Services;

static class IntrinsicCoreLibraryBinding
{
    internal static AssemblyBindingSelection Select(
        ResolvedAssemblyReference requestingAssembly,
        Func<AssemblyReferenceIdentity, AssemblyBindingSelection>
            selectReference)
    {
        ArgumentNullException.ThrowIfNull(requestingAssembly);
        ArgumentNullException.ThrowIfNull(selectReference);

        Stream? stream = null;
        PEReader? peReader = null;
        bool rejectionEstablished = false;
        try
        {
            stream = requestingAssembly.OpenRead();
            peReader = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen);
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                rejectionEstablished = true;
                return CandidateUnavailable();
            }

            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(peReader);
            if (reader.IsAssembly
                && IsCoreLibraryFacade(
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        reader).Name))
            {
                return AssemblyBindingSelection.Found(
                    requestingAssembly);
            }

            AssemblyBindingSelection? retainedFailure = null;
            foreach (AssemblyReferenceIdentity facade
                in CoreLibraryReferences(reader))
            {
                AssemblyBindingSelection selection =
                    selectReference(facade)
                    ?? AssemblyBindingSelection.Invalid(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.InvalidPolicyResult));
                if (selection is AssemblyBindingSelection.Missing)
                    continue;
                if (selection is AssemblyBindingSelection.Selected
                    or AssemblyBindingSelection.Ambiguous)
                {
                    return selection;
                }

                if (ShouldReplaceFailure(
                        retainedFailure,
                        selection))
                {
                    retainedFailure = selection;
                }
            }

            // A retained candidate failure and an unsupported scope are both
            // established rejections, so cleanup must not replace either with
            // a disposal exception.
            rejectionEstablished = true;
            return retainedFailure
                ?? AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.UnsupportedScope));
        }
        catch (UnsupportedMetadataFormatException ex)
        {
            DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            throw;
        }
        catch (MalformedMetadataRootException ex)
        {
            DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or OverflowException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException
                or System.Security.SecurityException)
        {
            DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            return CandidateUnavailable();
        }
        finally
        {
            if (rejectionEstablished)
            {
                DisposeWithoutReplacingOutcome(ref peReader);
                DisposeWithoutReplacingOutcome(ref stream);
            }
            else
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }
    }

    static void DisposeAfterFailure<T>(
        ref T? resource,
        Exception primaryFailure)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        DisposeWithoutReplacingOutcome(ref resource);
    }

    static void DisposeAfterFailure(
        ref PEReader? peReader,
        ref Stream? stream,
        Exception primaryFailure)
    {
        DisposeAfterFailure(ref peReader, primaryFailure);
        DisposeAfterFailure(ref stream, primaryFailure);
    }

    static void DisposeWithoutReplacingOutcome<T>(
        ref T? resource)
        where T : class, IDisposable
    {
        try
        {
            resource?.Dispose();
        }
        catch
        {
        }
        resource = null;
    }

    static IEnumerable<AssemblyReferenceIdentity> CoreLibraryReferences(
        MetadataReader reader) =>
        reader.AssemblyReferences
            .Select(handle =>
                AssemblyReferenceIdentity.From(reader, handle))
            .Where(reference =>
                IsCoreLibraryFacade(reference.Name))
            .OrderBy(reference =>
                CoreLibraryFacadeOrder(reference.Name))
            .ThenBy(reference => reference.Version)
            .ThenBy(
                reference => reference.Culture,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                reference => reference.PublicKeyToken,
                StringComparer.OrdinalIgnoreCase);

    static AssemblyBindingSelection CandidateUnavailable() =>
        AssemblyBindingSelection.CannotSelect(
            new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));

    static bool ShouldReplaceFailure(
        AssemblyBindingSelection? retained,
        AssemblyBindingSelection candidate) =>
        retained is null
        || FailurePrecedence(candidate)
            > FailurePrecedence(retained);

    static int FailurePrecedence(
        AssemblyBindingSelection selection) =>
        selection is AssemblyBindingSelection.Unavailable unavailable
            ? unavailable.Failure.CandidateFailureKind switch
            {
                CandidateOpenFailureKind.ResourceBudget => 2,
                CandidateOpenFailureKind
                    .UnsupportedMetadataFormat => 1,
                CandidateOpenFailureKind.InvalidImage
                    when unavailable.Failure.MetadataRootReason
                        is not null => 1,
                _ => 0,
            }
            : 0;

    static bool IsCoreLibraryFacade(string name) =>
        name.Equals(
            "System.Private.CoreLib",
            StringComparison.OrdinalIgnoreCase)
        || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
        || name.Equals("System.Runtime", StringComparison.OrdinalIgnoreCase)
        || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase);

    static int CoreLibraryFacadeOrder(string name) =>
        name.Equals(
            "System.Private.CoreLib",
            StringComparison.OrdinalIgnoreCase)
            ? 0
            : name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
                ? 1
                : name.Equals(
                    "System.Runtime",
                    StringComparison.OrdinalIgnoreCase)
                    ? 2
                    : name.Equals(
                        "netstandard",
                        StringComparison.OrdinalIgnoreCase)
                        ? 3
                        : int.MaxValue;
}
