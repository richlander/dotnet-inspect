using CiChangeDetection.Planning;

namespace CiChangeDetection;

/// <summary>
/// Runs the repository CI change planner. This is the only public surface of
/// the planner; every model, policy, evidence, and serializer type stays
/// internal to this assembly.
/// </summary>
public static class ChangePlanApp
{
    /// <summary>
    /// Plans one candidate and publishes its plan, or refuses.
    /// </summary>
    /// <param name="args">Command-line arguments passed by the file-based
    /// entrypoint.</param>
    /// <returns>Zero when a plan was published.</returns>
    public static int Run(string[] args) =>
        ChangePlanCommand.Execute(args, Console.Out, Console.Error);
}
