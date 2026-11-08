using System.Text.Json.Serialization;

namespace WeatherImages;

// Queue messages.
public record StartJobMessage(string JobId);

public record ProcessImageMessage(
    string JobId,
    string StationId,
    string StationName,
    double? Temperature,
    string? WeatherDescription,
    double? Humidity,
    double? WindSpeed,
    string? Region,
    string? Timestamp);

// Buienradar feed (only the fields we use).
public sealed class BuienradarFeed
{
    [JsonPropertyName("actual")]
    public BuienradarActual? Actual { get; set; }
}

public sealed class BuienradarActual
{
    [JsonPropertyName("stationmeasurements")]
    public List<StationMeasurement>? StationMeasurements { get; set; }
}

public sealed class StationMeasurement
{
    [JsonPropertyName("stationid")]
    public int StationId { get; set; }

    [JsonPropertyName("stationname")]
    public string? StationName { get; set; }

    [JsonPropertyName("regio")]
    public string? Region { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("humidity")]
    public double? Humidity { get; set; }

    [JsonPropertyName("windspeed")]
    public double? WindSpeed { get; set; }

    [JsonPropertyName("weatherdescription")]
    public string? WeatherDescription { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

// HTTP responses.
public record StartJobResponse(string JobId, string StatusUrl);

public record JobStatusResponse(
    string JobId,
    string Status,
    int Total,
    int Completed,
    IReadOnlyList<string> Images);
