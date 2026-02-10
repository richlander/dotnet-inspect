using DotnetInspector.Models;
using DotnetInspector.Output;

namespace DotnetInspector.Sections;

/// <summary>
/// Context passed to each scanner during data collection.
/// </summary>
public sealed class ScannerContext
{
    public required string AssemblyPath { get; init; }
    public required LibraryInspection Model { get; init; }
    public required VerboseLogger Logger { get; init; }
}

/// <summary>
/// Registry of named scanners for the library command.
/// Each scanner is a function that populates part of a <see cref="LibraryInspection"/>
/// model. Scanners are registered by key and invoked only when needed.
/// </summary>
public sealed class ScannerRegistry
{
    private readonly Dictionary<string, Action<ScannerContext>> _scanners = [];

    /// <summary>
    /// Registers a scanner by key. The action populates the model with data.
    /// </summary>
    public ScannerRegistry Add(string key, Action<ScannerContext> scan)
    {
        _scanners[key] = scan;
        return this;
    }

    /// <summary>
    /// Runs all scanners whose keys are in the <paramref name="requiredScanners"/> set.
    /// </summary>
    public void RunScanners(HashSet<string> requiredScanners, ScannerContext context)
    {
        foreach (var (key, scan) in _scanners)
        {
            if (requiredScanners.Contains(key))
                scan(context);
        }
    }
}
