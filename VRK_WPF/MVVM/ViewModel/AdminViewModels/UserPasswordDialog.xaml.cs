using System.Windows;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class UserPasswordDialog : Window
    {
        public string Username => UsernameTextBox.Text;
        public string Password => PasswordBox.Password;
        
        public UserPasswordDialog()
        {
            InitializeComponent();
            UsernameTextBox.Focus();
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Введите имя пользователя", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show("Введите пароль", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            DialogResult = true;
        }
    }
}