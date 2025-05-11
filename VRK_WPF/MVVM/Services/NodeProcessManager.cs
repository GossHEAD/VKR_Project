using System.Diagnostics;
using System.IO;
using System.Windows;

namespace VRK_WPF.MVVM.Services
{
    public class NodeProcessManager
    {
        private const string NodeExecutableName = "VKR_Node.exe";
        private Process _nodeProcess;
        private readonly NodeConfigurationManager _configManager;
        
        public bool IsNodeRunning => _nodeProcess != null && !_nodeProcess.HasExited;
        public event EventHandler<string> NodeOutputReceived;
        public event EventHandler<string> NodeErrorReceived;
        public event EventHandler NodeExited;
        
        public NodeProcessManager(NodeConfigurationManager configManager)
        {
            _configManager = configManager;
        }
        
        public bool StartNode()
        {
            if (IsNodeRunning)
            {
                return true; 
            }
            
            try
            {
                string nodePath = FindNodeExecutable();
                if (string.IsNullOrEmpty(nodePath))
                {
                    MessageBox.Show("Could not find VKR_Node executable.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = nodePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(nodePath)
                };
                
                foreach (var param in _configManager.GetNodeStartupParameters())
                {
                    startInfo.ArgumentList.Add(param.Key);
                    startInfo.ArgumentList.Add(param.Value);
                }
                
                _nodeProcess = new Process { StartInfo = startInfo };
                _nodeProcess.OutputDataReceived += (s, e) => 
                {
                    if (e.Data != null)
                        NodeOutputReceived?.Invoke(this, e.Data);
                };
                _nodeProcess.ErrorDataReceived += (s, e) => 
                {
                    if (e.Data != null)
                        NodeErrorReceived?.Invoke(this, e.Data);
                };
                _nodeProcess.Exited += (s, e) => NodeExited?.Invoke(this, EventArgs.Empty);
                _nodeProcess.EnableRaisingEvents = true;
                
                bool started = _nodeProcess.Start();
                if (started)
                {
                    _nodeProcess.BeginOutputReadLine();
                    _nodeProcess.BeginErrorReadLine();
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting node: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        
        public void StopNode()
        {
            if (!IsNodeRunning) return;
            
            try
            {
                if (!_nodeProcess.CloseMainWindow())
                {
                    _nodeProcess.Kill(true);
                }
                
                _nodeProcess = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping node: {ex.Message}");
            }
        }
        
        private string FindNodeExecutable()
        {
            string[] possibleLocations = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NodeExecutableName),
                
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "VKR_Node", "bin", "Debug", "net8.0", NodeExecutableName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "VKR_Node", "bin", "Release", "net8.0", NodeExecutableName),
                
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VKR_Network", NodeExecutableName)
            };
            
            foreach (string path in possibleLocations)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string parentDir = Path.GetDirectoryName(baseDir);
            
            if (!string.IsNullOrEmpty(parentDir))
            {
                foreach (string file in Directory.GetFiles(parentDir, NodeExecutableName, SearchOption.AllDirectories))
                {
                    return file;
                }
            }
            
            return string.Empty;
        }
    }
}