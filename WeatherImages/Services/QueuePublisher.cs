using System.Text.Json;
using Azure.Storage.Queues;

namespace WeatherImages.Services;

// Sends JSON messages to a named storage queue.
public class QueuePublisher
{
    private readonly QueueServiceClient _queueServiceClient;

    public QueuePublisher(QueueServiceClient queueServiceClient)
    {
        _queueServiceClient = queueServiceClient;
    }

    public async Task SendAsync<T>(string queueName, T message, CancellationToken ct)
    {
        var queue = _queueServiceClient.GetQueueClient(queueName);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        await queue.SendMessageAsync(JsonSerializer.Serialize(message), ct);
    }
}
