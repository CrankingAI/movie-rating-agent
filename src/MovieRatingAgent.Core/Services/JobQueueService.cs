using Azure.Storage.Queues;

namespace MovieRatingAgent.Core.Services;

public class JobQueueService
{
    private readonly QueueClient _queue;

    public JobQueueService(QueueServiceClient queueServiceClient)
    {
        _queue = queueServiceClient.GetQueueClient("job-requests");
    }

    public async Task EnsureQueueExistsAsync(CancellationToken ct = default)
    {
        await _queue.CreateIfNotExistsAsync(cancellationToken: ct);
    }

    public async Task EnqueueJobAsync(string jobId, CancellationToken ct = default)
    {
        await _queue.SendMessageAsync(jobId, cancellationToken: ct);
    }
}
