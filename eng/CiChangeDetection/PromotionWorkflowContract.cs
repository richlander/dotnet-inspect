using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class PromotionWorkflowContract
{
    internal static void AssertMutations(string repository)
    {
        string path = Path.Combine(
            repository,
            ".github",
            "workflows",
            "promote-inspect-web.yml");
        string workflow = File.ReadAllText(path);
        Validate(workflow);

        const string trustedCheckout =
            """
                steps:
                  - uses: actions/checkout@v6

                  - name: Setup .NET
            """;
        const string candidateCheckout =
            """
                steps:
                  - uses: actions/checkout@v6
                    with:
                      ref: ${{ needs.resolve.outputs.sha }}

                  - name: Setup .NET
            """;
        string mutated = workflow.Replace(
            trustedCheckout,
            candidateCheckout,
            StringComparison.Ordinal);
        if (mutated == workflow)
            throw new InvalidOperationException("Promotion checkout mutation did not apply.");

        try
        {
            Validate(mutated);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Promotion workflow contract accepted candidate-controlled production checkout.");
    }

    private static void Validate(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one promotion workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "promotion workflow root");
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "promotion workflow");
        YamlMappingNode deploy = GetRequiredMapping(jobs, "deploy", "promotion jobs");
        RequireScalarValue(deploy, "needs", "resolve", "jobs.deploy");
        RequireScalarValue(deploy, "runs-on", "ubuntu-26.04", "jobs.deploy");

        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "jobs.deploy");
        RequireScalarValue(
            environment,
            "name",
            "inspect-web-production",
            "jobs.deploy.environment");

        YamlSequenceNode steps = GetRequiredSequence(deploy, "steps", "jobs.deploy");
        if (steps.Children.Count != 6)
        {
            throw new InvalidOperationException(
                "Production deployment must contain checkout, setup, revalidation, " +
                "artifact download, artifact verification, and deploy steps.");
        }

        YamlMappingNode checkout = RequireStep(steps, 0, null);
        RequireExactKeys(checkout, ["uses"], "jobs.deploy checkout");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v6",
            "jobs.deploy checkout");

        RequireStep(steps, 1, "Setup .NET");
        YamlMappingNode revalidate =
            RequireStep(steps, 2, "Revalidate staged site");
        RequireScalarValue(revalidate, "shell", "bash", "revalidation step");
        string revalidationCommand = GetRequiredScalar(
            revalidate,
            "run",
            "revalidation step");
        if (!revalidationCommand.Contains(
                "bash eng/validate-inspect-web-promotion.sh",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Production revalidation must execute the trusted promotion validator.");
        }

        YamlMappingNode download =
            RequireStep(steps, 3, "Download staged site artifact");
        RequireScalarValue(
            download,
            "uses",
            "actions/download-artifact@v8",
            "artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "artifact download step");
        RequireScalarValue(
            downloadWith,
            "artifact-ids",
            "${{ needs.resolve.outputs.artifact_id }}",
            "artifact download step.with");
        RequireScalarValue(
            downloadWith,
            "digest-mismatch",
            "error",
            "artifact download step.with");

        YamlMappingNode verify =
            RequireStep(steps, 4, "Verify staged site artifact");
        RequireScalarValue(
            verify,
            "run",
            "test -f artifacts/inspect-web-publish/wwwroot/index.html",
            "artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(steps, 5, "Deploy to production");
        RequireScalarValue(
            deployStep,
            "uses",
            "Azure/static-web-apps-deploy@4d27395796ac319302594769cfe812bd207490b1",
            "production deploy step");
    }

    private static YamlMappingNode RequireStep(
        YamlSequenceNode steps,
        int index,
        string? name)
    {
        YamlMappingNode step = RequireMapping(
            steps.Children[index],
            $"jobs.deploy step {index}");
        if (name is not null)
            RequireScalarValue(step, "name", name, $"jobs.deploy step {index}");
        return step;
    }
}
