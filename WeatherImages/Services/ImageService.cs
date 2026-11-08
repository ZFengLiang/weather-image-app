using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace WeatherImages.Services;

// Downloads a base image and writes the weather data onto it.
public class ImageService
{
    private const string BaseImageUrlFormat = "https://picsum.photos/seed/{0}/800/600";

    // Bundled font so rendering does not depend on host-installed fonts.
    private static readonly FontFamily FontFamily =
        new FontCollection().Add(Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"));

    private readonly HttpClient _http;
    private readonly ILogger<ImageService> _logger;

    public ImageService(HttpClient http, ILogger<ImageService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<byte[]> CreateWeatherImageAsync(ProcessImageMessage msg, CancellationToken ct)
    {
        var baseUrl = string.Format(BaseImageUrlFormat, Uri.EscapeDataString(msg.StationId));
        byte[] baseBytes = await _http.GetByteArrayAsync(baseUrl, ct);

        using var image = Image.Load<Rgba32>(baseBytes);

        var titleFont = FontFamily.CreateFont(40, FontStyle.Bold);
        var bodyFont = FontFamily.CreateFont(26, FontStyle.Regular);
        var lines = BuildLines(msg);

        image.Mutate(ctx =>
        {
            const int panelHeight = 200;
            var panel = new RectangleF(0, image.Height - panelHeight, image.Width, panelHeight);
            ctx.Fill(Color.FromRgba(0, 0, 0, 150), panel);

            float y = image.Height - panelHeight + 16;

            ctx.DrawText(
                new RichTextOptions(titleFont) { Origin = new PointF(20, y), WrappingLength = image.Width - 40 },
                msg.StationName,
                Color.White);
            y += 52;

            foreach (var line in lines)
            {
                ctx.DrawText(
                    new RichTextOptions(bodyFont) { Origin = new PointF(20, y), WrappingLength = image.Width - 40 },
                    line,
                    Color.White);
                y += 34;
            }
        });

        using var output = new MemoryStream();
        image.SaveAsPng(output);
        _logger.LogInformation("Built image for station {Station}.", msg.StationName);
        return output.ToArray();
    }

    private static List<string> BuildLines(ProcessImageMessage msg)
    {
        var lines = new List<string>();

        if (msg.Temperature is double t)
            lines.Add($"Temperature: {t:0.#} C");
        if (!string.IsNullOrWhiteSpace(msg.WeatherDescription))
            lines.Add(msg.WeatherDescription!);
        if (msg.WindSpeed is double w)
            lines.Add($"Wind: {w:0.#} m/s");
        if (msg.Humidity is double h)
            lines.Add($"Humidity: {h:0} %");
        if (!string.IsNullOrWhiteSpace(msg.Timestamp))
            lines.Add($"Measured: {msg.Timestamp}");

        return lines;
    }
}
