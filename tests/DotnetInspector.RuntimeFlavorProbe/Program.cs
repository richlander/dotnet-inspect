using System.Runtime.CompilerServices;
using DotnetInspector;

if (args is not (["CoreCLR" or "NativeAOT"] or ["CoreCLR", "--disable-dynamic-code"]))
{
    Console.Error.WriteLine("usage: RuntimeFlavorProbe CoreCLR [--disable-dynamic-code] | NativeAOT");
    return 2;
}

if (args.Length == 2)
{
    AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);
    if (RuntimeFeature.IsDynamicCodeSupported)
    {
        Console.Error.WriteLine("The probe did not disable dynamic-code support.");
        return 1;
    }
}

string expected = $"{args[0]}; .NET {Environment.Version.Major}.{Environment.Version.Minor}";
string actual = VersionInfo.FlavorVersion;
Console.WriteLine(actual);
if (actual != expected)
{
    Console.Error.WriteLine($"Expected '{expected}', got '{actual}'.");
    return 1;
}

return 0;
