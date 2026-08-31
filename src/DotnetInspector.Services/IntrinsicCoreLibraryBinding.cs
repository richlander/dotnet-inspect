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

        try
        {
            using Stream stream = requestingAssembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return CandidateUnavailable();

            MetadataReader reader = peReader.GetMetadataReader();
            if (reader.IsAssembly
                && IsCoreLibraryFacade(
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        reader).Name))
            {
                return AssemblyBindingSelection.Found(
                    requestingAssembly);
            }

            AssemblyBindingSelection? firstFailure = null;
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

                firstFailure ??= selection;
            }

            return firstFailure
                ?? AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.UnsupportedScope));
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
            return CandidateUnavailable();
        }
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
