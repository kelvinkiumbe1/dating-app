using Amazon.S3;
using Amazon.S3.Model;

namespace DatingApp.Api.Storage;

public class S3StorageOptions
{
    /// Cloudflare R2: https://<account-id>.r2.cloudflarestorage.com
    /// Any other S3-compatible endpoint works too; leave blank for real AWS S3.
    public string ServiceUrl { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Region { get; set; } = "auto";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);
}

/// Persistent photo storage on any S3-compatible service (Cloudflare R2, Backblaze B2,
/// MinIO, AWS S3). Survives container restarts and redeploys.
public class S3PhotoStorage : IPhotoStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly bool _https;

    public S3PhotoStorage(S3StorageOptions options)
    {
        _bucket = options.Bucket;
        _https = !options.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        var config = new AmazonS3Config
        {
            // R2 and most S3-compatible services require path-style addressing.
            ForcePathStyle = true,
            AuthenticationRegion = options.Region,
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            config.ServiceURL = options.ServiceUrl;
        else
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);

        _s3 = new AmazonS3Client(options.AccessKey, options.SecretKey, config);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        // Buffer to a seekable stream: the SDK needs a known length to sign the request,
        // and an incoming form-file stream is not always seekable.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = buffer,
            ContentType = contentType,
            // R2 rejects streaming chunked signatures. The SDK only allows unsigned payloads
            // over TLS, so a plain-HTTP endpoint (local MinIO/SeaweedFS) keeps signing.
            DisablePayloadSigning = _https,
        }, ct);
    }

    public async Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _s3.DeleteObjectAsync(_bucket, key, ct);
}
