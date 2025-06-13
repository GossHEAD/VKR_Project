using VKR_Core.Enums;
using VRK_WPF.MVVM.Model;

namespace VRK_WPF.MVVM.Services
{
    public class AuthService
    {
        public static UserModel? CurrentUser { get; private set; }
        
        private static readonly List<HardcodedUser> _users = new List<HardcodedUser>
        {
            new HardcodedUser
            {
                UserId = 1,
                Username = "admin",
                Password = "admin123",
                FullName = "Администратор системы",
                Role = UserRole.Administrator,
                IsActive = true
            },
            new HardcodedUser
            {
                UserId = 2,
                Username = "specialist",
                Password = "pass123",
                FullName = "Иванов Иван Иванович",
                Role = UserRole.ITSpecialist,
                IsActive = true
            },
            new HardcodedUser
            {
                UserId = 3,
                Username = "user",
                Password = "user123",
                FullName = "Петров Петр Петрович",
                Role = UserRole.ITSpecialist,
                IsActive = true
            },
            new HardcodedUser
            {
                UserId = 4,
                Username = "inactive",
                Password = "inactive123",
                FullName = "Неактивный пользователь",
                Role = UserRole.ITSpecialist,
                IsActive = false
            }
        };
        
        public static async Task<bool> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }
            
            try
            {
                await Task.Delay(100);
                
                var user = _users.FirstOrDefault(u => 
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && 
                    u.Password == password);
                
                if (user == null || !user.IsActive)
                {
                    return false;
                }
                
                CurrentUser = new UserModel
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    FullName = user.FullName,
                    Role = user.Role,
                    IsActive = user.IsActive,
                    LastLogin = DateTime.Now
                };
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
                return false;
            }
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
        
        public static IReadOnlyList<HardcodedUser> GetAvailableUsers()
        {
            return _users.AsReadOnly();
        }
    }
    
    public class HardcodedUser
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; } = true;
        
        public override string ToString()
        {
            return $"{Username} ({Role}) - {(IsActive ? "Активен" : "Неактивен")}";
        }
    }
}