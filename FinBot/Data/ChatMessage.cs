namespace FinBot.Data;

public class ChatMessage
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime TimeStamp { get; set; }
    public bool IsBotMessage { get; set; }
    
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
