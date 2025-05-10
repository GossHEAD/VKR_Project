using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using VKR_Core.Enums;

namespace VKR_Node.Persistance.Entities;

[Table("Users")]
[Index(nameof(Username), IsUnique = true)] 
public class UserEntity
{
    [Key]
    public int UserId { get; set; } 

    [Required]
    [MaxLength(100)]
    public required string Username { get; set; }

    [Required]
    public required string PasswordHash { get; set; } 

    [Required]
    public UserRole Role { get; set; } = UserRole.ITSpecialist;
    public DateTime CreationTime { get; set; }
    public bool IsActive { get; set; } = true;
}