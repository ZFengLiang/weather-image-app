namespace WeatherImages;

// Queue names. Two queues: one to start a job, one per station image.
public static class QueueNames
{
    public const string StartJob = "start-queue";
    public const string ProcessImage = "image-queue";
}

// Blob container and table names.
public static class StorageNames
{
    public const string ImagesContainer = "images";
    public const string StatusTable = "jobstatus";
}
