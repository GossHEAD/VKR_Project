using VKR_Core.Enums;

namespace VKR_Core.Models;

public record UserModel
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.ITSpecialist;
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}