using System.Diagnostics;
using System.IO;
using System.Windows;

namespace VRK_WPF.MVVM.Services
{
    public class NodeProcessManager : IDisposable
    {
        private const string NodeExecutableName = "VKR_Node.exe";
        private Process _nodeProcess;
        private readonly NodeConfigurationManager _configManager;
        private bool _disposed = false;
        
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
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            string[] possibleLocations = {
                
                Path.Combine(baseDir, NodeExecutableName),       
                
                Path.Combine(baseDir, "bin", NodeExecutableName),
                
                Path.Combine(baseDir, "..", "VKR_Node", "bin", "Debug", "net8.0", NodeExecutableName),
                Path.Combine(baseDir, "..", "VKR_Node", "bin", "Release", "net8.0", NodeExecutableName),
                
                Path.Combine(baseDir, "..", "VKR_Node", "bin", "Debug", "net6.0", NodeExecutableName),
                Path.Combine(baseDir, "..", "VKR_Node", "bin", "Release", "net6.0", NodeExecutableName),
                Path.Combine(baseDir, "..", "VKR_Node", "bin", "Debug", "net472", NodeExecutableName),
                Path.Combine(baseDir, "..", "VKR_Node", "bin", "Release", "net472", NodeExecutableName),
                Path.Combine(baseDir, "node", NodeExecutableName),
                
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VKR_Network", NodeExecutableName)
            };
            
            foreach (string path in possibleLocations)
            {
                string normalizedPath = Path.GetFullPath(path);
                if (File.Exists(normalizedPath))
                {
                    return normalizedPath; 
                }
            }
            
            var parentDir = Path.GetDirectoryName(baseDir);
            if (!string.IsNullOrEmpty(parentDir))
            {
                try {
                    foreach (var file in Directory.GetFiles(parentDir, NodeExecutableName, SearchOption.AllDirectories))
                    {
                        return file;
                    }
                }
                catch (Exception ex) {
                    Debug.WriteLine($"Error searching for node executable: {ex.Message}");
                }
            }
            
            return string.Empty;
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
                    StopNode();
                    _nodeProcess?.Dispose();
                }
                _disposed = true;
            }
        }
    }
    
}