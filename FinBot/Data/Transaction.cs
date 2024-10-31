namespace FinBot.Data;

public class Transaction : ICloneable
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Narration { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public DateTime TimeStamp { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public object Clone() => MemberwiseClone();
}


public enum TransactionType
{
    Credit = 0,
    Debit = 1
}