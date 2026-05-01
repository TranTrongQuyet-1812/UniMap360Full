namespace UniMap360.Options;

public sealed class CloudinarySettings
{
    public bool Enabled { get; set; }
    public bool RequireSuccess { get; set; }
    public string? CloudinaryUrl { get; set; }
    public string? UploadPrefix { get; set; }
    public string? CloudName { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string BaseFolder { get; set; } = "Anh_Cho_Chu_Tro";
}
