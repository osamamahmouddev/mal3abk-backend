using Amazon;
using Amazon.S3;

namespace Malabak.Api.Infrastructure.Media;

/// <summary>Where uploads are stored and how they are scanned (<c>Media</c> section).</summary>
public sealed class MediaStorageOptions
{
    /// <summary>"Local" (disk, development) or "S3" (private bucket, ADR-0004).</summary>
    public string Storage { get; set; } = "Local";

    public string? BucketName { get; set; }
    public string? Region { get; set; }

    /// <summary>Optional S3-compatible endpoint (MinIO locally); leave empty for AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Optional KMS key for SSE-KMS; SSE-S3 (AES-256) is used when empty.</summary>
    public string? KmsKeyId { get; set; }

    /// <summary>"None" or "ClamAv".</summary>
    public string MalwareScanner { get; set; } = "None";

    public string? ClamAvHost { get; set; }
    public int ClamAvPort { get; set; } = 3310;

    public bool UsesS3 => string.Equals(Storage, "S3", StringComparison.OrdinalIgnoreCase);
    public bool UsesClamAv => string.Equals(MalwareScanner, "ClamAv", StringComparison.OrdinalIgnoreCase);
}

public static class MediaStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers storage and scanning from configuration. Production must use the private S3 bucket and a real
    /// scanner: startup fails otherwise, instead of silently keeping uploads on an ephemeral disk unscanned.
    /// </summary>
    public static IServiceCollection AddMediaStorage(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("Media").Get<MediaStorageOptions>() ?? new MediaStorageOptions();
        if (environment.IsProduction() && (!options.UsesS3 || !options.UsesClamAv))
        {
            throw new InvalidOperationException("Production requires Media:Storage = S3 and Media:MalwareScanner = ClamAv.");
        }

        services.AddSingleton(options);

        if (options.UsesS3)
        {
            if (string.IsNullOrWhiteSpace(options.BucketName))
            {
                throw new InvalidOperationException("Media:BucketName is required when Media:Storage is S3.");
            }

            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(CreateConfig(options)));
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        }

        if (options.UsesClamAv)
        {
            if (string.IsNullOrWhiteSpace(options.ClamAvHost))
            {
                throw new InvalidOperationException("Media:ClamAvHost is required when Media:MalwareScanner is ClamAv.");
            }

            services.AddSingleton<IMalwareScanner, ClamAvMalwareScanner>();
        }
        else
        {
            services.AddSingleton<IMalwareScanner, NoOpMalwareScanner>();
        }

        return services;
    }

    private static AmazonS3Config CreateConfig(MediaStorageOptions options)
    {
        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.ForcePathStyle = true;
        }
        else if (!string.IsNullOrWhiteSpace(options.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return config;
    }
}
