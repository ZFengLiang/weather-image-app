using Azure;
using Azure.Data.Tables;

namespace WeatherImages.Services;

// One row per job.
public class JobEntity : ITableEntity
{
    public const string JobRowKey = "_job";

    public string PartitionKey { get; set; } = default!; // jobId
    public string RowKey { get; set; } = JobRowKey;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Status { get; set; } = "Pending";
    public int Total { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}

// One row per station image within a job.
public class StationEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // jobId
    public string RowKey { get; set; } = default!;       // stationId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string StationName { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending | Done
    public string? BlobName { get; set; }
}

// Tracks job and per-station progress in Table Storage.
public class StatusStore
{
    private readonly TableClient _table;

    public StatusStore(TableServiceClient serviceClient)
    {
        _table = serviceClient.GetTableClient(StorageNames.StatusTable);
        _table.CreateIfNotExists();
    }

    public async Task CreateJobAsync(string jobId, CancellationToken ct)
    {
        var job = new JobEntity { PartitionKey = jobId, Status = "Running" };
        await _table.UpsertEntityAsync(job, TableUpdateMode.Replace, ct);
    }

    public async Task AddStationAsync(string jobId, string stationId, string stationName, CancellationToken ct)
    {
        var station = new StationEntity
        {
            PartitionKey = jobId,
            RowKey = stationId,
            StationName = stationName,
        };
        await _table.UpsertEntityAsync(station, TableUpdateMode.Replace, ct);
    }

    public async Task SetJobTotalAsync(string jobId, int total, CancellationToken ct)
    {
        var job = await GetJobAsync(jobId, ct) ?? new JobEntity { PartitionKey = jobId };
        job.Total = total;
        job.Status = "Running";
        await _table.UpsertEntityAsync(job, TableUpdateMode.Replace, ct);
    }

    public async Task MarkStationDoneAsync(string jobId, string stationId, string blobName, CancellationToken ct)
    {
        StationEntity station;
        try
        {
            station = (await _table.GetEntityAsync<StationEntity>(jobId, stationId, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            station = new StationEntity { PartitionKey = jobId, RowKey = stationId };
        }

        station.Status = "Done";
        station.BlobName = blobName;
        await _table.UpsertEntityAsync(station, TableUpdateMode.Replace, ct);

        await RefreshJobStatusAsync(jobId, ct);
    }

    public async Task<JobEntity?> GetJobAsync(string jobId, CancellationToken ct)
    {
        try
        {
            return (await _table.GetEntityAsync<JobEntity>(jobId, JobEntity.JobRowKey, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<StationEntity>> GetStationsAsync(string jobId, CancellationToken ct)
    {
        var results = new List<StationEntity>();
        var query = _table.QueryAsync<StationEntity>(
            e => e.PartitionKey == jobId && e.RowKey != JobEntity.JobRowKey,
            cancellationToken: ct);

        await foreach (var e in query)
            results.Add(e);

        return results;
    }

    private async Task RefreshJobStatusAsync(string jobId, CancellationToken ct)
    {
        var job = await GetJobAsync(jobId, ct);
        if (job is null) return;

        var stations = await GetStationsAsync(jobId, ct);
        int done = stations.Count(s => s.Status == "Done");

        job.Status = job.Total > 0 && done >= job.Total ? "Completed" : "Running";
        await _table.UpsertEntityAsync(job, TableUpdateMode.Replace, ct);
    }
}
