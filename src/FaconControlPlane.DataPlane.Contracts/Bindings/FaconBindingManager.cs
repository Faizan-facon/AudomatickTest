using Microsoft.Extensions.Configuration;

namespace FaconControlPlane.DataPlane.Contracts.Bindings;

public sealed class FaconBindingManager
{
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, IResourceBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

    public FaconBindingManager(IConfiguration configuration)
    {
        _configuration = configuration;
        DiscoverBindings();
    }

    public IReadOnlyCollection<string> BindingNames => _bindings.Keys;

    public T? GetBinding<T>(string logicalName) where T : class, IResourceBinding
    {
        if (_bindings.TryGetValue(logicalName, out var b) && b is T typed)
        {
            return typed;
        }
        return null;
    }

    private void DiscoverBindings()
    {
        // 1. Postgres
        var pgHost = GetValue("POSTGRES", "HOST");
        if (!string.IsNullOrEmpty(pgHost))
        {
            _bindings["postgres"] = new PostgresResourceBinding
            {
                Host = pgHost,
                Port = int.TryParse(GetValue("POSTGRES", "PORT"), out var p) ? p : 5432,
                Database = GetValue("POSTGRES", "DATABASE") ?? "facon",
                Username = GetValue("POSTGRES", "USER") ?? GetValue("POSTGRES", "USERNAME") ?? "postgres",
                Password = GetValue("POSTGRES", "PASSWORD"),
                SslMode = GetValue("POSTGRES", "SSLMODE") ?? "Prefer"
            };
        }

        // 2. RabbitMQ
        var rmqHost = GetValue("RABBITMQ", "HOST");
        if (!string.IsNullOrEmpty(rmqHost))
        {
            _bindings["rabbitmq"] = new RabbitMqResourceBinding
            {
                Host = rmqHost,
                Port = int.TryParse(GetValue("RABBITMQ", "PORT"), out var p) ? p : 5672,
                VirtualHost = GetValue("RABBITMQ", "VHOST") ?? "/",
                Username = GetValue("RABBITMQ", "USER") ?? GetValue("RABBITMQ", "USERNAME") ?? "guest",
                Password = GetValue("RABBITMQ", "PASSWORD")
            };
        }

        // 3. Redis
        var redisHost = GetValue("REDIS", "HOST");
        if (!string.IsNullOrEmpty(redisHost))
        {
            _bindings["redis"] = new RedisResourceBinding
            {
                Host = redisHost,
                Port = int.TryParse(GetValue("REDIS", "PORT"), out var p) ? p : 6379,
                InstanceName = GetValue("REDIS", "INSTANCE") ?? string.Empty,
                Password = GetValue("REDIS", "PASSWORD")
            };
        }

        // 4. S3
        var s3Endpoint = GetValue("S3", "ENDPOINT");
        if (!string.IsNullOrEmpty(s3Endpoint))
        {
            _bindings["s3"] = new S3ResourceBinding
            {
                Endpoint = s3Endpoint,
                Bucket = GetValue("S3", "BUCKET") ?? string.Empty,
                Region = GetValue("S3", "REGION") ?? "us-east-1",
                UseSsl = bool.TryParse(GetValue("S3", "USESSL"), out var ssl) && ssl,
                AccessKey = GetValue("S3", "ACCESS_KEY") ?? GetValue("S3", "ACCESSKEY"),
                SecretKey = GetValue("S3", "SECRET_KEY") ?? GetValue("S3", "SECRETKEY")
            };
        }
    }

    private string? GetValue(string resource, string key)
    {
        // Check FACON_BINDING_{RESOURCE}_{KEY} env var
        var envVarName = $"FACON_BINDING_{resource}_{key}";
        var envVal = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envVal)) return envVal;

        // Check ASP.NET configuration: Facon:Bindings:{resource}:{key}
        var configVal = _configuration[$"Facon:Bindings:{resource}:{key}"];
        if (!string.IsNullOrEmpty(configVal)) return configVal;

        // Check fallback flat env var FACON_{RESOURCE}_{KEY}
        var flatEnv = Environment.GetEnvironmentVariable($"FACON_{resource}_{key}");
        if (!string.IsNullOrEmpty(flatEnv)) return flatEnv;

        return null;
    }
}
