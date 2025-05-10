using VKR_Core.Enums;
using VRK_WPF.MVVM.Model;

namespace VRK_WPF.MVVM.Services
{
    /// <summary>
    /// Service to handle authentication and user management
    /// </summary>
    public class AuthService
    {
        // Mock user database - in a real app this would come from a proper database
        private static readonly List<UserModel> _users = new List<UserModel>
        {
            new UserModel 
            { 
                UserId = 1, 
                Username = "admin", 
                FullName = "Администратор системы", 
                Role = UserRole.Administrator, 
                IsActive = true 
            },
            new UserModel 
            { 
                UserId = 2, 
                Username = "specialist", 
                FullName = "Иванов Иван Иванович", 
                Role = UserRole.ITSpecialist, 
                IsActive = true 
            },
            new UserModel 
            { 
                UserId = 3, 
                Username = "user", 
                FullName = "Петров Петр Петрович", 
                Role = UserRole.ITSpecialist, 
                IsActive = true 
            },
            new UserModel 
            { 
                UserId = 4, 
                Username = "inactive", 
                FullName = "Неактивный пользователь", 
                Role = UserRole.ITSpecialist, 
                IsActive = false 
            }
        };

        // Currently logged in user
        public static UserModel? CurrentUser { get; private set; }

        /// <summary>
        /// Attempts to authenticate a user with just the provided username
        /// </summary>
        /// <param name="username">The username</param>
        /// <param name="errorMessage">Error message if authentication fails</param>
        /// <returns>True if authentication succeeds, false otherwise</returns>
        public static bool Login(string username, out string errorMessage)
        {
            errorMessage = string.Empty;
            
            // Check if username is provided
            if (string.IsNullOrWhiteSpace(username))
            {
                errorMessage = "Введите имя пользователя";
                return false;
            }
            
            // Find the user
            var user = _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            
            // Check if user exists
            if (user == null)
            {
                errorMessage = "Пользователь не найден";
                return false;
            }
            
            // Check if user is active
            if (!user.IsActive)
            {
                errorMessage = "Учетная запись отключена";
                return false;
            }
            
            // Set current user and update last login
            user.LastLogin = DateTime.Now;
            CurrentUser = user;
            
            return true;
        }
        
        /// <summary>
        /// Logs out the current user
        /// </summary>
        public static void Logout()
        {
            CurrentUser = null;
        }
        
        /// <summary>
        /// Checks if the current user has a specific role
        /// </summary>
        /// <param name="role">The role to check</param>
        /// <returns>True if the current user has the role, false otherwise</returns>
        public static bool HasRole(UserRole role)
        {
            // If no user is logged in, no permissions
            if (CurrentUser == null)
                return false;
                
            // Admin role has access to everything
            if (CurrentUser.Role == UserRole.Administrator)
                return true;
                
            // Otherwise, check the specific role
            return CurrentUser.Role == role;
        }
        
        /// <summary>
        /// Checks if a module is accessible to the current user based on their role
        /// </summary>
        /// <param name="moduleName">The module name</param>
        /// <returns>True if the user can access the module, false otherwise</returns>
        public static bool CanAccessModule(string moduleName)
        {
            // If no user is logged in, no permissions
            if (CurrentUser == null)
                return false;
                
            // Admin can access everything
            if (CurrentUser.Role == UserRole.Administrator)
                return true;
                
            // Role-based module access
            return moduleName switch
            {
                "Admin" => CurrentUser.Role == UserRole.Administrator,
                "NodeConfiguration" => CurrentUser.Role == UserRole.Administrator || CurrentUser.Role == UserRole.ITSpecialist,
                "Simulation" => true, // All users can access simulation
                "FileManagement" => true, // All users can access file management
                "NetworkStatus" => true, // All users can access network status
                "Settings" => CurrentUser.Role == UserRole.Administrator || CurrentUser.Role == UserRole.ITSpecialist,
                "Logs" => CurrentUser.Role == UserRole.Administrator || CurrentUser.Role == UserRole.ITSpecialist,
                _ => false // Unknown module
            };
        }
    }
}