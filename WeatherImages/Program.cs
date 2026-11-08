using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WeatherImages.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

string storageConnection = builder.Configuration["AzureWebJobsStorage"]!;

// Storage clients. Base64 encoding matches the Functions host queue default.
builder.Services.AddSingleton(new QueueServiceClient(
    storageConnection,
    new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 }));
builder.Services.AddSingleton(new BlobServiceClient(storageConnection));
builder.Services.AddSingleton(new TableServiceClient(storageConnection));

// App services.
builder.Services.AddHttpClient<WeatherService>();
builder.Services.AddHttpClient<ImageService>();
builder.Services.AddSingleton<QueuePublisher>();
builder.Services.AddSingleton<StatusStore>();
builder.Services.AddSingleton<BlobImageStore>();

builder.Build().Run();
