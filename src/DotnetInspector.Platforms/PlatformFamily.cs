namespace DotnetInspector.Platforms;

/// <summary>Identifies one supported logical platform family.</summary>
public enum PlatformFamily
{
    /// <summary>The Microsoft.NETCore.App runtime family.</summary>
    DotNetRuntime = 0,

    /// <summary>The Microsoft.AspNetCore.App shared-framework family.</summary>
    AspNetCore = 1,
}
