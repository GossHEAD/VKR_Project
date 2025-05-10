using System;
using System.Collections.Generic;
using VKR_Core.Enums;

namespace VRK_WPF.MVVM.Model
{
    public class UserModel
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? LastLogin { get; set; }
    }
}