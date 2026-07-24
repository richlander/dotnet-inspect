using System.IO;
using System.Reflection.PortableExecutable;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Filters compile-back reference candidates to managed PE images. Native DLLs
/// (for example <c>aspnetcorev2_inprocess.dll</c>, <c>coreclr.dll</c>, or
/// <c>msquic.dll</c>) ship in the Windows shared framework and pass
/// <see cref="Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(string)"/>,
/// but Roslyn rejects them at compile time with CS0009 ("PE image doesn't contain
/// managed metadata"). Excluding them keeps the compile-back reference sets
/// runnable on Windows, matching Linux CI where those native files are absent.
/// </summary>
internal static class ManagedReferenceFilter
{
    public static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return pe.HasMetadata;
        }
        catch
        {
            return false;
        }
    }
}
