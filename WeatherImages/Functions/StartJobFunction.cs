using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WeatherImages.Services;

namespace WeatherImages.Functions;

// POST /api/jobs - starts a job and returns its id. Work runs in the background.
public class StartJobFunction
{
    private readonly StatusStore _status;
    private readonly QueuePublisher _publisher;
    private readonly ILogger<StartJobFunction> _logger;

    public StartJobFunction(StatusStore status, QueuePublisher publisher, ILogger<StartJobFunction> logger)
    {
        _status = status;
        _publisher = publisher;
        _logger = logger;
    }

    [Function("StartJob")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs")] HttpRequestData req,
        CancellationToken ct)
    {
        string jobId = Guid.NewGuid().ToString("n");
        _logger.LogInformation("Starting job {JobId}.", jobId);

        await _status.CreateJobAsync(jobId, ct);
        await _publisher.SendAsync(QueueNames.StartJob, new StartJobMessage(jobId), ct);

        string statusUrl = $"{req.Url.GetLeftPart(UriPartial.Authority)}/api/jobs/{jobId}";

        var response = req.CreateResponse();
        await response.WriteAsJsonAsync(new StartJobResponse(jobId, statusUrl), ct);
        response.StatusCode = HttpStatusCode.Accepted;
        return response;
    }
}
