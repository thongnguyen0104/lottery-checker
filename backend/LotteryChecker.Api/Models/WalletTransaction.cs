using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
/// <summary>Adjust = admin trừ bớt số dư (sửa nhầm lẫn); TopUp = cộng tiền.</summary>
public enum WalletTransactionKind { TopUp, Purchase, Win, Refund, Adjust }

/// <summary>
/// Sổ biến động số dư — mọi lần cộng/trừ <see cref="User.Balance"/> đều ghi 1 dòng trong cùng lần lưu,
/// để đối soát được số dư. Amount có dấu: + cộng, − trừ.
/// </summary>
public class WalletTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public WalletTransactionKind Kind { get; set; }
    public long Amount { get; set; }
    /// <summary>Số dư sau giao dịch.</summary>
    public long BalanceAfter { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
