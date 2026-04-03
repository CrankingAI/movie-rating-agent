using System.Text.Json;
using Azure.Storage.Blobs;
using MovieRatingAgent.Core.Models;

namespace MovieRatingAgent.Core.Services;

public class JobBlobService
{
    private readonly BlobContainerClient _container;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public JobBlobService(BlobServiceClient blobServiceClient)
    {
        _container = blobServiceClient.GetBlobContainerClient("jobs");
    }

    public async Task EnsureContainerExistsAsync(CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
    }

    public async Task WriteRequestAsync(string jobId, JobRequest request, CancellationToken ct = default)
    {
        await WriteBlobAsync($"{jobId}/request.json", request, ct);
    }

    public async Task WriteResponseAsync(string jobId, JobResponse response, CancellationToken ct = default)
    {
        await WriteBlobAsync($"{jobId}/response.json", response, ct);
    }

    public async Task WriteMetaAsync(string jobId, JobMeta meta, CancellationToken ct = default)
    {
        await WriteBlobAsync($"{jobId}/meta.json", meta, ct);
    }

    public async Task<JobRequest?> ReadRequestAsync(string jobId, CancellationToken ct = default)
    {
        return await ReadBlobAsync<JobRequest>($"{jobId}/request.json", ct);
    }

    public async Task<JobResponse?> ReadResponseAsync(string jobId, CancellationToken ct = default)
    {
        return await ReadBlobAsync<JobResponse>($"{jobId}/response.json", ct);
    }

    public async Task<JobMeta?> ReadMetaAsync(string jobId, CancellationToken ct = default)
    {
        return await ReadBlobAsync<JobMeta>($"{jobId}/meta.json", ct);
    }

    private async Task WriteBlobAsync<T>(string blobName, T value, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobName);
        var json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await blob.UploadAsync(new BinaryData(json), overwrite: true, cancellationToken: ct);
    }

    private async Task<T?> ReadBlobAsync<T>(string blobName, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobName);
        if (!await blob.ExistsAsync(ct))
            return default;

        var response = await blob.DownloadContentAsync(ct);
        return JsonSerializer.Deserialize<T>(response.Value.Content, JsonOptions);
    }
}
