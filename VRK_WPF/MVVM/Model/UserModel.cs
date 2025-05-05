using System;
using System.Collections.Generic;
using VKR_Core.Enums;

namespace VRK_WPF.MVVM.Model
{
    /// <summary>
    /// Represents a user in the system
    /// </summary>
    public class UserModel
    {
        /// <summary>
        /// Gets or sets the user ID
        /// </summary>
        public int UserId { get; set; }
        
        /// <summary>
        /// Gets or sets the username
        /// </summary>
        public string Username { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the user's full name
        /// </summary>
        public string FullName { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the user's role
        /// </summary>
        public UserRole Role { get; set; }
        
        /// <summary>
        /// Gets or sets whether the user is active
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// Gets or sets last login date
        /// </summary>
        public DateTime? LastLogin { get; set; }
    }
}