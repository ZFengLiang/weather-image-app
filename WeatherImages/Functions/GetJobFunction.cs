using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WeatherImages.Services;

namespace WeatherImages.Functions;

// GET /api/jobs/{jobId} - returns progress and SAS links to finished images.
public class GetJobFunction
{
    private static readonly TimeSpan SasLifetime = TimeSpan.FromHours(1);

    private readonly StatusStore _status;
    private readonly BlobImageStore _blobs;
    private readonly ILogger<GetJobFunction> _logger;

    public GetJobFunction(StatusStore status, BlobImageStore blobs, ILogger<GetJobFunction> logger)
    {
        _status = status;
        _blobs = blobs;
        _logger = logger;
    }

    [Function("GetJob")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}")] HttpRequestData req,
        string jobId,
        CancellationToken ct)
    {
        var job = await _status.GetJobAsync(jobId, ct);
        if (job is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Job not found", jobId }, ct);
            return notFound;
        }

        var stations = await _status.GetStationsAsync(jobId, ct);

        var images = stations
            .Where(s => s.Status == "Done" && !string.IsNullOrEmpty(s.BlobName))
            .OrderBy(s => s.StationName)
            .Select(s => _blobs.GetReadSasUri(s.BlobName!, SasLifetime).ToString())
            .ToList();

        var result = new JobStatusResponse(
            JobId: jobId,
            Status: job.Status,
            Total: job.Total,
            Completed: stations.Count(s => s.Status == "Done"),
            Images: images);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, ct);
        return response;
    }
}
