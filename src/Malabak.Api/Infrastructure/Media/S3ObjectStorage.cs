using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace Malabak.Api.Infrastructure.Media;

/// <summary>
/// Private S3 bucket for media (ADR-0004). Objects are encrypted at rest (SSE-KMS when a key is configured,
/// otherwise SSE-S3) and never public: they are only served through the media endpoint's visibility rules.
/// Credentials come from the default AWS chain (instance/task role), never from configuration.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _s3;
    private readonly MediaStorageOptions _options;

    public S3ObjectStorage(IAmazonS3 s3, MediaStorageOptions options)
    {
        _s3 = s3;
        _options = options;
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(content, writable: false);
        var useKms = !string.IsNullOrWhiteSpace(_options.KmsKeyId);
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            ServerSideEncryptionMethod = useKms ? ServerSideEncryptionMethod.AWSKMS : ServerSideEncryptionMethod.AES256,
            ServerSideEncryptionKeyManagementServiceKeyId = useKms ? _options.KmsKeyId : null
        };
        await _s3.PutObjectAsync(request, cancellationToken);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _s3.GetObjectAsync(_options.BucketName, key, cancellationToken);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        _s3.DeleteObjectAsync(_options.BucketName, key, cancellationToken);
}
