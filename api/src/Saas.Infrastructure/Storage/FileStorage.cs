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
    /// <summary>
    /// A second bucket with no public access, for keys under "private/" (ID documents, patients' results, pupils' report cards,
    /// receipts). Required with R2: the public bucket is served to anyone who knows a key.
    /// </summary>
    public string? R2PrivateBucket { get; set; }
    /// <summary>Public base URL of the bucket, e.g. https://pub-xxxx.r2.dev or https://cdn.example.com.</summary>
    public string? R2PublicBaseUrl { get; set; }

    /// <summary>Refuses an R2 setup that would put private files in the public bucket.</summary>
    public void ValidateR2()
    {
        if (string.IsNullOrWhiteSpace(R2PrivateBucket))
            throw new InvalidOperationException("Storage:R2PrivateBucket must be set when Storage:Provider is R2, so private files never sit in the public bucket.");
        if (string.Equals(R2PrivateBucket.Trim(), R2Bucket?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage:R2PrivateBucket must be a different bucket from Storage:R2Bucket.");
    }
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

    /// <summary>Where a key is stored: keys under "private/" go to the private bucket and never get a public URL.</summary>
    public readonly record struct R2Target(string Bucket, string? PublicUrl, string CacheControl);

    public static R2Target Target(StorageOptions o, string key) =>
        key.StartsWith("private/", StringComparison.Ordinal)
            ? new R2Target(o.R2PrivateBucket!, null, "private, no-store")
            : new R2Target(o.R2Bucket!, $"{o.R2PublicBaseUrl!.TrimEnd('/')}/{key}", "public, max-age=31536000, immutable");

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var target = Target(_o, key);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = target.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true,
            UseChunkEncoding = false,
            Headers = { CacheControl = target.CacheControl },
        }, ct);
        // Private files are only read back through the API; callers ignore the return value for them.
        return target.PublicUrl ?? key;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _s3.DeleteObjectAsync(Target(_o, key).Bucket, key, ct);

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(Target(_o, key).Bucket, key, ct);
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
        BucketName = Target(_o, key).Bucket, Key = key, Expires = DateTime.UtcNow.Add(lifetime), Verb = HttpVerb.GET,
    });

    public void Dispose() => _s3.Dispose();
}
