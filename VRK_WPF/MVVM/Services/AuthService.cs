using VKR_Core.Enums;
using VRK_WPF.MVVM.Model;

namespace VRK_WPF.MVVM.Services
{
    public class AuthService
    {
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

        public static UserModel? CurrentUser { get; private set; }
        
        public static bool Login(string username, out string errorMessage)
        {
            errorMessage = string.Empty;
            
            if (string.IsNullOrWhiteSpace(username))
            {
                errorMessage = "Введите имя пользователя";
                return false;
            }
            
            var user = _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user == null)
            {
                errorMessage = "Пользователь не найден";
                return false;
            }
            if (!user.IsActive)
            {
                errorMessage = "Учетная запись отключена";
                return false;
            }
            
            user.LastLogin = DateTime.Now;
            CurrentUser = user;
            
            return true;
        }
        public static void Logout()
        {
            CurrentUser = null;
        }
        
        public static bool HasRole(UserRole role)
        {
            if (CurrentUser == null)
                return false;
                
            if (CurrentUser.Role == UserRole.Administrator)
                return true;
                
            return CurrentUser.Role == role;
        }
        public static bool CanAccessModule(string moduleName)
        {
            if (CurrentUser == null)
                return false;
                
            if (CurrentUser.Role == UserRole.Administrator)
                return true;
                
            return moduleName switch
            {
                "Admin" => CurrentUser.Role == UserRole.Administrator,
                "NodeConfiguration" => CurrentUser.Role == UserRole.Administrator || CurrentUser.Role == UserRole.ITSpecialist,
                "Simulation" => true,
                "FileManagement" => true, 
                "NetworkStatus" => true, 
                "Settings" => CurrentUser.Role == UserRole.Administrator || CurrentUser.Role == UserRole.ITSpecialist,
                "Logs" => CurrentUser.Role == UserRole.Administrator || CurrentUser.Role == UserRole.ITSpecialist,
                _ => false 
            };
        }
    }
}