// LogViewer.xaml.cs

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.View
{
    /// <summary>
    /// Interaction logic for LogViewer.xaml
    /// </summary>
    public partial class LogViewer : UserControl
    {
        private readonly LogManager _logManager;
        private readonly CollectionViewSource _viewSource;
        private bool _autoScroll = true;
        private ScrollViewer _logScrollViewer;
        
        public LogViewer()
        {
            _logManager = new LogManager(Dispatcher);
            _viewSource = new CollectionViewSource { Source = _logManager.Logs };
    
            InitializeComponent();
    
            lvLogs.ItemsSource = _viewSource.View;
    
            this.Loaded += (s, e) => 
            {
                ApplyFilters();
        
                _logScrollViewer = FindScrollViewer(lvLogs);
        
                if (_logScrollViewer != null)
                {
                    _logScrollViewer.ScrollChanged += LogScrollViewer_ScrollChanged;
                }
        
                StartMonitoring();
            };
    
            ((ICollectionView)_viewSource.View).CollectionChanged += (s, e) =>
            {
                if (_autoScroll && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    lvLogs.ScrollIntoView(lvLogs.Items[lvLogs.Items.Count - 1]);
                }
            };

            this.Unloaded += (s, e) => 
            {
                StopMonitoring();
        
                if (_logScrollViewer != null)
                {
                    _logScrollViewer.ScrollChanged -= LogScrollViewer_ScrollChanged;
                }
            };
        }
        
        private ScrollViewer FindScrollViewer(DependencyObject depObj)
        {
            if (depObj == null) return null;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                
                ScrollViewer childViewer = FindScrollViewer(child);
                if (childViewer != null)
                {
                    return childViewer;
                }
            }
            
            return null;
        }
        
        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange < 0)
            {
                _autoScroll = false;
            }
            
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1)
            {
                _autoScroll = true;
            }
        }
        
        private async void StartMonitoring()
        {
            await _logManager.StartMonitoringAsync();
        }
        
        private void StopMonitoring()
        {
            _logManager.StopMonitoring();
        }
        
        private void FilterLogs(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                ApplyFilters();
            }
        }
        
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }
        
        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                txtSearch.Clear();
            }
        }
        
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }
        
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _logManager.ClearLogs();
        }
        
        private void ApplyFilters()
        {
            if (_viewSource == null || _viewSource.View == null)
            {
                return;
            }

            string searchText = txtSearch?.Text ?? string.Empty;
    
            bool includeInfo = cbInfo?.IsChecked == true;
            bool includeWarning = cbWarning?.IsChecked == true;
            bool includeError = cbError?.IsChecked == true;
            bool includeDebug = cbDebug?.IsChecked == true;
    
            _viewSource.View.Filter = item =>
            {
                if (item is Model.LogEntry log)
                {
                    return (string.IsNullOrWhiteSpace(searchText) || 
                            log.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) || 
                            log.FullText.Contains(searchText, StringComparison.OrdinalIgnoreCase)) &&
                           ((includeInfo && log.IsInfo) ||
                            (includeWarning && log.IsWarning) ||
                            (includeError && log.IsError) ||
                            (includeDebug && log.IsDebug));
                }
        
                return false;
            };
        }
    }
}