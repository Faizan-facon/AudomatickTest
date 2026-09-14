using FaconControlPlane.DataPlane.Contracts.Bindings;
using FaconControlPlane.DataPlane.Contracts.Metadata;
using FaconControlPlane.DataPlane.Contracts.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FaconControlPlane.DataPlane.Contracts.Endpoints;

public static class FaconEndpointExtensions
{
    public static IEndpointRouteBuilder MapFaconDataPlaneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<FaconDataPlaneOptions>>().Value;

        // 1. Liveness Probe
        endpoints.MapGet(options.LiveEndpoint, () => Results.Ok(new { status = "Healthy" }))
            .WithDisplayName("FACON Liveness Probe")
            .AllowAnonymous();

        // 2. Readiness Probe
        endpoints.MapGet(options.ReadyEndpoint, async (
            IEnumerable<IFaconReadinessCheck> checks,
            CancellationToken ct) =>
        {
            var results = new List<object>();
            var allReady = true;

            foreach (var check in checks)
            {
                var checkResult = await check.CheckReadinessAsync(ct);
                results.Add(new
                {
                    name = checkResult.Name,
                    isReady = checkResult.IsReady,
                    description = checkResult.Description,
                    data = checkResult.Data
                });

                if (!checkResult.IsReady)
                {
                    allReady = false;
                }
            }

            var response = new
            {
                status = allReady ? "Ready" : "Unhealthy",
                checks = results
            };

            return allReady ? Results.Ok(response) : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithDisplayName("FACON Readiness Probe")
        .AllowAnonymous();

        // 3. Runtime Metadata Endpoint (Active Observation Evidence)
        endpoints.MapGet(options.MetadataEndpoint, (
            FaconBindingManager bindingManager) =>
        {
            var metadata = new FaconRuntimeMetadata
            {
                SchemaVersion = "1.0",
                ApplicationId = options.ApplicationId,
                ApplicationVersion = options.ApplicationVersion,
                ReleaseId = options.ReleaseId,
                DeploymentId = options.DeploymentId,
                Environment = options.Environment,
                CommitSha = options.CommitSha,
                BuildTimestamp = options.BuildTimestamp,
                Endpoints = new Dictionary<string, string>
                {
                    ["liveness"] = options.LiveEndpoint,
                    ["readiness"] = options.ReadyEndpoint
                },
                ActiveBindings = bindingManager.BindingNames.ToList()
            };

            // ZERO-SECRET INVARIANT ASSERTION
            var (isClean, violations) = ZeroSecretValidator.ValidateZeroSecrets(metadata);
            if (!isClean)
            {
                return Results.Problem(
                    detail: $"Zero-secret invariant violated in runtime metadata: {string.Join("; ", violations)}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(metadata);
        })
        .WithDisplayName("FACON Runtime Metadata")
        .AllowAnonymous();

        return endpoints;
    }
}
