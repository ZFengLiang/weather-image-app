using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WeatherImages.Services;

namespace WeatherImages.Functions;

// image-queue trigger: build one weather image and store it in blob storage.
public class ProcessImageFunction
{
    private readonly ImageService _images;
    private readonly BlobImageStore _blobs;
    private readonly StatusStore _status;
    private readonly ILogger<ProcessImageFunction> _logger;

    public ProcessImageFunction(
        ImageService images,
        BlobImageStore blobs,
        StatusStore status,
        ILogger<ProcessImageFunction> logger)
    {
        _images = images;
        _blobs = blobs;
        _status = status;
        _logger = logger;
    }

    [Function("ProcessImage")]
    public async Task Run(
        [QueueTrigger(QueueNames.ProcessImage)] string message,
        CancellationToken ct)
    {
        var msg = JsonSerializer.Deserialize<ProcessImageMessage>(message)
                  ?? throw new InvalidOperationException("Invalid image-queue message.");

        byte[] png = await _images.CreateWeatherImageAsync(msg, ct);
        string blobName = await _blobs.UploadAsync(msg.JobId, msg.StationId, png, ct);
        await _status.MarkStationDoneAsync(msg.JobId, msg.StationId, blobName, ct);

        _logger.LogInformation("Stored image for station {Station}.", msg.StationName);
    }
}
