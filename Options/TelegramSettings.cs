namespace UniMap360.Options;

public class TelegramSettings
{
    public string BotToken { get; set; } = null!;
    public string ChatId { get; set; } = null!;
    public bool Enabled { get; set; } = false;
}
