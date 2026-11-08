using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WeatherImages.Services;

namespace WeatherImages.Functions;

// start-queue trigger: fetch stations and fan out one image message per station.
public class FanOutFunction
{
    private const int StationCount = 50;

    private readonly WeatherService _weather;
    private readonly StatusStore _status;
    private readonly QueuePublisher _publisher;
    private readonly ILogger<FanOutFunction> _logger;

    public FanOutFunction(
        WeatherService weather,
        StatusStore status,
        QueuePublisher publisher,
        ILogger<FanOutFunction> logger)
    {
        _weather = weather;
        _status = status;
        _publisher = publisher;
        _logger = logger;
    }

    [Function("FanOut")]
    public async Task Run(
        [QueueTrigger(QueueNames.StartJob)] string message,
        CancellationToken ct)
    {
        var start = JsonSerializer.Deserialize<StartJobMessage>(message)
                    ?? throw new InvalidOperationException("Invalid start-queue message.");

        var stations = await _weather.GetStationsAsync(StationCount, ct);

        foreach (var station in stations)
        {
            string stationId = station.StationId.ToString();
            await _status.AddStationAsync(start.JobId, stationId, station.StationName ?? stationId, ct);

            var imageMessage = new ProcessImageMessage(
                JobId: start.JobId,
                StationId: stationId,
                StationName: station.StationName ?? $"Station {stationId}",
                Temperature: station.Temperature,
                WeatherDescription: station.WeatherDescription,
                Humidity: station.Humidity,
                WindSpeed: station.WindSpeed,
                Region: station.Region,
                Timestamp: station.Timestamp);

            await _publisher.SendAsync(QueueNames.ProcessImage, imageMessage, ct);
        }

        await _status.SetJobTotalAsync(start.JobId, stations.Count, ct);
        _logger.LogInformation("Job {JobId}: queued {Count} images.", start.JobId, stations.Count);
    }
}
