using FaconControlPlane.DataPlane.Contracts.Bindings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FaconControlPlane.DataPlane.Contracts.DependencyInjection;

public static class FaconDataPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddFaconDataPlane(
        this IServiceCollection services,
        Action<FaconDataPlaneOptions>? configure = null)
    {
        services.AddOptions<FaconDataPlaneOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection("Facon:DataPlane").Bind(options);

                // Environment variable overrides
                if (Guid.TryParse(Environment.GetEnvironmentVariable("FACON_APPLICATION_ID"), out var appId))
                {
                    options.ApplicationId = appId;
                }

                var appName = Environment.GetEnvironmentVariable("FACON_APPLICATION_NAME");
                if (!string.IsNullOrWhiteSpace(appName))
                {
                    options.ApplicationName = appName;
                }

                var appVersion = Environment.GetEnvironmentVariable("FACON_APPLICATION_VERSION");
                if (!string.IsNullOrWhiteSpace(appVersion))
                {
                    options.ApplicationVersion = appVersion;
                }

                if (Guid.TryParse(Environment.GetEnvironmentVariable("FACON_RELEASE_ID"), out var relId))
                {
                    options.ReleaseId = relId;
                }

                if (Guid.TryParse(Environment.GetEnvironmentVariable("FACON_DEPLOYMENT_ID"), out var depId))
                {
                    options.DeploymentId = depId;
                }

                var env = Environment.GetEnvironmentVariable("FACON_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (!string.IsNullOrWhiteSpace(env))
                {
                    options.Environment = env;
                }

                var commitSha = Environment.GetEnvironmentVariable("FACON_COMMIT_SHA")
                    ?? Environment.GetEnvironmentVariable("GIT_COMMIT");
                if (!string.IsNullOrWhiteSpace(commitSha))
                {
                    options.CommitSha = commitSha;
                }
            });

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<FaconBindingManager>();

        return services;
    }

    public static IServiceCollection AddFaconMetering(
        this IServiceCollection services,
        Action<Usage.FaconEnforcementOptions>? configure = null)
    {
        services.AddHttpClient<Usage.IFaconLimitClient, Usage.FaconLimitClient>();
        services.AddHttpClient<Usage.IFaconUsageReporter, Usage.FaconUsageReporter>();

        if (configure != null)
        {
            services.Configure(configure);
        }

        return services;
    }
}
