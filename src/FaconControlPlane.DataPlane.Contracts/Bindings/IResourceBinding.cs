namespace FaconControlPlane.DataPlane.Contracts.Bindings;

public interface IResourceBinding
{
    string LogicalName { get; }
    string ResourceType { get; }
}

public sealed class PostgresResourceBinding : IResourceBinding
{
    public string LogicalName { get; set; } = "postgres";
    public string ResourceType => "postgres";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string SslMode { get; set; } = "Prefer";

    public string BuildConnectionString()
    {
        var conn = $"Host={Host};Port={Port};Database={Database};Username={Username};SSL Mode={SslMode}";
        if (!string.IsNullOrEmpty(Password))
        {
            conn += $";Password={Password}";
        }
        return conn;
    }
}

public sealed class RabbitMqResourceBinding : IResourceBinding
{
    public string LogicalName { get; set; } = "rabbitmq";
    public string ResourceType => "rabbitmq";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string? Password { get; set; }
}

public sealed class RedisResourceBinding : IResourceBinding
{
    public string LogicalName { get; set; } = "redis";
    public string ResourceType => "redis";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public string InstanceName { get; set; } = string.Empty;
    public string? Password { get; set; }

    public string BuildConnectionString()
    {
        var conn = $"{Host}:{Port}";
        if (!string.IsNullOrEmpty(Password))
        {
            conn += $",password={Password}";
        }
        return conn;
    }
}

public sealed class S3ResourceBinding : IResourceBinding
{
    public string LogicalName { get; set; } = "s3";
    public string ResourceType => "s3";
    public string Endpoint { get; set; } = "http://localhost:9000";
    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public bool UseSsl { get; set; } = false;
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}
