using System.Collections.Concurrent;

namespace OwnCloudTool.Web;

public sealed class JobStore
{
    private readonly ConcurrentDictionary<Guid, WebJob> jobs = new();

    public WebJob Create(string type)
    {
        var job = new WebJob(Guid.NewGuid(), type, DateTimeOffset.UtcNow);
        jobs.TryAdd(job.Id, job);
        return job;
    }

    public WebJob[] GetJobs()
            => [.. jobs.Values.OrderByDescending(job => job.StartedAt)];
}

public sealed class WebJob(Guid id, string type, DateTimeOffset startedAt)
{
    private readonly Lock sync = new();
    public string? Error { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public Guid Id { get; } = id;
    public string? LastMessage { get; private set; }
    public DateTimeOffset StartedAt { get; } = startedAt;
    public string Status { get; private set; } = "Running";
    public string Type { get; } = type;

    public void Complete()
    {
        lock (sync)
        {
            Status = "Completed"; FinishedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Fail(Exception exception)
    {
        lock (sync)
        {
            Status = "Failed"; Error = exception.Message; FinishedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Report(string message)
    {
        lock (sync)
        {
            LastMessage = message;
        }
    }
}