using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.View.UserPages
{
    public partial class SettingsPage : Page, IDisposable
    {
        private NodeConfigurationManager _configManager;
        private NodeProcessManager _processManager;
        private bool _isNodeManagerSet = false;
        private bool _disposed = false;
        
        public SettingsPage()
        {
            InitializeComponent();
        }
        
        public void SetNodeManager(NodeConfigurationManager configManager, NodeProcessManager processManager)
        {
            _configManager = configManager;
            _processManager = processManager;
            _isNodeManagerSet = true;
            
            UpdateNodeStatusUI();
            PopulateConfigDropdown();
        }
        
        private void UpdateNodeStatusUI()
        {
            if (!_isNodeManagerSet)
                return;
                
            bool isRunning = _processManager.IsNodeRunning;
            
            txtNodeStatus.Text = isRunning ? "Запущен" : "Не запущен";
            txtNodeStatus.Foreground = isRunning ? Brushes.Green : Brushes.Red;
            
            btnStartNode.IsEnabled = !isRunning;
            btnStopNode.IsEnabled = isRunning;
            
            txtNodeId.Text = _configManager.CurrentNodeId;
        }
        
        private void PopulateConfigDropdown()
        {
            if (!_isNodeManagerSet)
                return;
                
            var configs = _configManager.GetAvailableConfigs();
            var configsCollection = new ObservableCollection<NodeConfig>(configs);
            cmbNodeConfigs.ItemsSource = configsCollection;
            
            //var currentConfig = configs.Find(c => c.IsCurrentNode);
            var currentSelection = cmbNodeConfigs.SelectedItem as NodeConfig;
            
            if (currentSelection != null)
            {
                var matchingConfig = configsCollection.FirstOrDefault(c => c.ConfigPath == currentSelection.ConfigPath);
                if (matchingConfig != null)
                {
                    cmbNodeConfigs.SelectedItem = matchingConfig;
                }
            }
            // if (currentConfig != null)
            // {
            //     cmbNodeConfigs.SelectedItem = currentConfig;
            // }
        }
        
        private void CmbNodeConfigs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isNodeManagerSet || cmbNodeConfigs.SelectedItem == null)
                return;
                
            if (cmbNodeConfigs.SelectedItem is NodeConfig config)
            {
                _configManager.SetCurrentConfig(config.ConfigPath);
                txtNodeId.Text = _configManager.CurrentNodeId;
                txtNodeStatusBar.Text = $"Выбрана конфигурация: {config.NodeId}";
            }
        }
        
        private void BtnStartNode_Click(object sender, RoutedEventArgs e)
        {
            if (!_isNodeManagerSet)
                return;
                
            if (_processManager.StartNode())
            {
                txtNodeStatusBar.Text = "Узел успешно запущен";
                UpdateNodeStatusUI();
            }
            else
            {
                txtNodeStatusBar.Text = "Ошибка запуска узла";
            }
        }
        
        private void BtnStopNode_Click(object sender, RoutedEventArgs e)
        {
            if (!_isNodeManagerSet)
                return;
                
            _processManager.StopNode();
            txtNodeStatusBar.Text = "Узел остановлен";
            UpdateNodeStatusUI();
        }
        
        private void BtnCreateConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!_isNodeManagerSet)
                return;
                
            string configPath = _configManager.CreateDefaultConfig();
            txtNodeStatusBar.Text = $"Создана новая конфигурация: {configPath}";
            PopulateConfigDropdown();
        }
        
        private void BtnEditConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!_isNodeManagerSet || cmbNodeConfigs.SelectedItem == null)
                return;
                
            if (cmbNodeConfigs.SelectedItem is NodeConfig config)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = config.ConfigPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка открытия конфигурации: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        public void AddNodeOutput(string output)
        {
            txtNodeOutput.AppendText(output + Environment.NewLine);
            txtNodeOutput.ScrollToEnd();
        }
        
        public void AddNodeError(string error)
        {
            txtNodeOutput.AppendText($"ERROR: {error}{Environment.NewLine}");
            txtNodeOutput.ScrollToEnd();
        }
        
        public void NotifyNodeExited()
        {
            txtNodeOutput.AppendText("Процесс узла завершился." + Environment.NewLine);
            UpdateNodeStatusUI();
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _configManager = null;
                    _processManager = null;
                }
                _disposed = true;
            }
        }
    }
}