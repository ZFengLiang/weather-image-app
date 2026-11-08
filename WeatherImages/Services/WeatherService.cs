using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace WeatherImages.Services;

// Fetches weather station data from Buienradar.
public class WeatherService
{
    private const string FeedUrl = "https://data.buienradar.nl/2.0/feed/json";

    private readonly HttpClient _http;
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(HttpClient http, ILogger<WeatherService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StationMeasurement>> GetStationsAsync(int max, CancellationToken ct)
    {
        var feed = await _http.GetFromJsonAsync<BuienradarFeed>(FeedUrl, ct);
        var stations = feed?.Actual?.StationMeasurements ?? new List<StationMeasurement>();

        var result = stations
            .Where(s => !string.IsNullOrWhiteSpace(s.StationName))
            .Take(max)
            .ToList();

        _logger.LogInformation("Fetched {Count} weather stations.", result.Count);
        return result;
    }
}
