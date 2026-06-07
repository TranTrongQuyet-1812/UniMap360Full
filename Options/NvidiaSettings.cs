namespace UniMap360.Options;

public class NvidiaSettings
{
    public string ApiKey { get; set; } = null!;
    public string Model { get; set; } = "nvidia/llama-3.1-nemotron-70b-instruct";
    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";
    public bool Enabled { get; set; } = false;
}
