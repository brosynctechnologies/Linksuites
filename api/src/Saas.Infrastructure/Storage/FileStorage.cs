using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Saas.Application.Common;

namespace Saas.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>"Local" (development) or "R2" (Cloudflare R2, S3-compatible).</summary>
    public string Provider { get; set; } = "Local";
    public string LocalPath { get; set; } = "uploads";
    public string? R2AccountId { get; set; }
    public string? R2AccessKeyId { get; set; }
    public string? R2SecretAccessKey { get; set; }
    public string? R2Bucket { get; set; }
    /// <summary>Public base URL of the bucket, e.g. https://pub-xxxx.r2.dev or https://cdn.example.com.</summary>
    public string? R2PublicBaseUrl { get; set; }
}

/// <summary>Writes files under the API's content root and serves them from /uploads.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, AppUrls urls, string contentRoot) : IFileStorage
{
    private string Root => Path.GetFullPath(Path.Combine(contentRoot, options.Value.LocalPath));

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = SafePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return $"{urls.ApiBaseUrl.TrimEnd('/')}/uploads/{key}";
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = SafePath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = SafePath(key);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true)
            : null);
    }

    /// <summary>Local files are streamed through the API.</summary>
    public string? TemporaryUrl(string key, TimeSpan lifetime) => null;

    private string SafePath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(Root, key));
        if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid storage key.");
        return path;
    }
}

/// <summary>Cloudflare R2: 10 GB free and no egress fees.</summary>
public sealed class R2FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly StorageOptions _o;

    public R2FileStorage(IOptions<StorageOptions> options)
    {
        _o = options.Value;
        _s3 = new AmazonS3Client(new BasicAWSCredentials(_o.R2AccessKeyId, _o.R2SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = $"https://{_o.R2AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
    }

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _o.R2Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true,
            UseChunkEncoding = false,
            Headers = { CacheControl = "public, max-age=31536000, immutable" },
        }, ct);
        return $"{_o.R2PublicBaseUrl!.TrimEnd('/')}/{key}";
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _s3.DeleteObjectAsync(_o.R2Bucket, key, ct);

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_o.R2Bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>A presigned GET URL, so large video files go straight from R2 to the viewer.</summary>
    public string? TemporaryUrl(string key, TimeSpan lifetime) => _s3.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = _o.R2Bucket, Key = key, Expires = DateTime.UtcNow.Add(lifetime), Verb = HttpVerb.GET,
    });

    public void Dispose() => _s3.Dispose();
}
