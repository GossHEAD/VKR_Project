using System.IO;
using Microsoft.EntityFrameworkCore;
using VKR_Core.Enums;
using VKR_Node.Configuration;
using VKR_Node.Persistance;
using VKR_Node.Persistance.Entities;
using VRK_WPF.MVVM.Model;

namespace VRK_WPF.MVVM.Services
{
    public class AuthService
    {
        public static UserModel? CurrentUser { get; private set; }
        
        private static NodeDbContext CreateDbContext()
        {
            // Try to find existing database
            var possiblePaths = new[] 
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "node_storage.db"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Data", "node_storage.db"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VKR_Network", "Data", "node_storage.db")
            };
            
            string? dbPath = possiblePaths.FirstOrDefault(File.Exists);
            
            if (string.IsNullOrEmpty(dbPath))
            {
                // Create default database in application directory
                dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "node_storage.db");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            }
            
            var optionsBuilder = new DbContextOptionsBuilder<NodeDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            
            var dbOptions = Microsoft.Extensions.Options.Options.Create(new DatabaseOptions 
            { 
                DatabasePath = dbPath
            });
            
            return new NodeDbContext(optionsBuilder.Options, dbOptions);
        }
        
        public static async Task<bool> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }
            
            try
            {
                using var context = CreateDbContext();
                
                // Ensure database exists and create default users if needed
                await context.Database.EnsureCreatedAsync();
                await EnsureDefaultUsersExist(context);
                
                // Simple plain text password check
                var userEntity = await context.Users
                    .FirstOrDefaultAsync(u => u.Username == username && u.PasswordHash == password);
                
                if (userEntity == null || !userEntity.IsActive)
                {
                    return false;
                }
                
                CurrentUser = new UserModel
                {
                    UserId = userEntity.UserId,
                    Username = userEntity.Username,
                    FullName = GetFullNameForUser(userEntity.Username, userEntity.Role),
                    Role = userEntity.Role,
                    IsActive = userEntity.IsActive,
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
        
        private static string GetFullNameForUser(string username, UserRole role)
        {
            return username switch
            {
                "admin" => "Администратор системы",
                "specialist" => "Иванов Иван Иванович",
                "user" => "Петров Петр Петрович",
                "inactive" => "Неактивный пользователь",
                _ => username
            };
        }
        
        private static async Task EnsureDefaultUsersExist(NodeDbContext context)
        {
            var hasAnyUsers = await context.Users.AnyAsync();
            
            if (!hasAnyUsers)
            {
                System.Diagnostics.Debug.WriteLine("Creating default users...");
                
                // Create preset users
                var defaultUsers = new List<UserEntity>
                {
                    new UserEntity
                    {
                        Username = "admin",
                        PasswordHash = "admin123", // Plain text password
                        Role = UserRole.Administrator,
                        IsActive = true,
                        CreationTime = DateTime.UtcNow
                    },
                    new UserEntity
                    {
                        Username = "specialist",
                        PasswordHash = "pass123", // Plain text password
                        Role = UserRole.ITSpecialist,
                        IsActive = true,
                        CreationTime = DateTime.UtcNow
                    },
                    new UserEntity
                    {
                        Username = "user",
                        PasswordHash = "user123", // Plain text password
                        Role = UserRole.ITSpecialist,
                        IsActive = true,
                        CreationTime = DateTime.UtcNow
                    },
                    new UserEntity
                    {
                        Username = "inactive",
                        PasswordHash = "inactive123", // Plain text password
                        Role = UserRole.ITSpecialist,
                        IsActive = false,
                        CreationTime = DateTime.UtcNow
                    }
                };
                
                context.Users.AddRange(defaultUsers);
                await context.SaveChangesAsync();
                
                System.Diagnostics.Debug.WriteLine("Default users created:");
                System.Diagnostics.Debug.WriteLine("  admin / admin123 (Administrator)");
                System.Diagnostics.Debug.WriteLine("  specialist / pass123 (IT Specialist)");
                System.Diagnostics.Debug.WriteLine("  user / user123 (IT Specialist)");
                System.Diagnostics.Debug.WriteLine("  inactive / inactive123 (Inactive IT Specialist)");
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
    }
}