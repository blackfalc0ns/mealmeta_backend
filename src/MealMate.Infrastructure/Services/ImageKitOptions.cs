namespace MealMate.Infrastructure.Services;

public sealed class ImageKitOptions
{
    public const string SectionName = "Storage:ImageKit";

    public string? PublicKey { get; set; }

    public string? PrivateKey { get; set; }

    /// <summary>CDN origin, e.g. https://ik.imagekit.io/your_id</summary>
    public string? UrlEndpoint { get; set; }

    public string UploadEndpoint { get; set; } = "https://upload.imagekit.io/api/v1/files/upload";
}
