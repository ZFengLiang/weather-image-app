using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace WeatherImages.Services;

// Stores images in a private container and hands out read-only SAS links.
public class BlobImageStore
{
    private readonly BlobContainerClient _container;

    public BlobImageStore(BlobServiceClient blobServiceClient)
    {
        _container = blobServiceClient.GetBlobContainerClient(StorageNames.ImagesContainer);
        _container.CreateIfNotExists(PublicAccessType.None);
    }

    public async Task<string> UploadAsync(string jobId, string stationId, byte[] png, CancellationToken ct)
    {
        string blobName = $"{jobId}/{stationId}.png";
        var blob = _container.GetBlobClient(blobName);

        using var stream = new MemoryStream(png);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" } },
            ct);

        return blobName;
    }

    public Uri GetReadSasUri(string blobName, TimeSpan validFor)
    {
        var blob = _container.GetBlobClient(blobName);
        if (!blob.CanGenerateSasUri)
            return blob.Uri;

        var builder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(validFor),
        };
        builder.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(builder);
    }
}
