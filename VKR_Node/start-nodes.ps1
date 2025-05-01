<#
.SYNOPSIS
Launches multiple VKR_Node instances, each with its own configuration file.

.DESCRIPTION
This script finds the VKR_Node DLL in the standard build output path relative
to the script location, then starts multiple instances using 'dotnet exec'.
Each instance is launched in a new window with a distinct title and is passed
its specific configuration file via the '--ConfigPath' argument.

Assumes the script is located in the VKR_Node project directory.
Adjust $nodeProjectName, $buildConfig, and $targetFramework if needed.
#>

# --- Configuration ---
$nodeProjectName = "VKR_Node" # Should match the DLL name (without .dll)
$buildConfig = "Debug"       # Or "Release"
$targetFramework = "net8.0"   # Your target framework
$numberOfNodes = 4           # How many nodes to start
$configDir = "bin\$buildConfig\$targetFramework\configs" # Relative path to config files from script location
$configFileNamePrefix = "node"  # Config files are named node1.json, node2.json, etc.

# --- Script Logic ---

# Get the directory where the script is located
$scriptRoot = $PSScriptRoot

# Construct the path to the node's DLL
$dllPath = Join-Path -Path $scriptRoot -ChildPath "bin\$buildConfig\$targetFramework\$($nodeProjectName).dll"

# Check if the DLL exists
if (-not (Test-Path -Path $dllPath -PathType Leaf)) {
    Write-Error "ERROR: Cannot find the node DLL at '$dllPath'. Build the '$nodeProjectName' project first."
    exit 1
} else {
    Write-Host "Found node DLL: $dllPath"
}

# Loop to start each node instance
for ($i = 1; $i -le $numberOfNodes; $i++) {
    $nodeId = "Node $i"
    $configFileName = "$($configFileNamePrefix)$i.json"
    $configFilePath = Join-Path -Path $scriptRoot -ChildPath $configDir -ChildPath $configFileName

    # Check if the config file exists
    if (-not (Test-Path -Path $configFilePath -PathType Leaf)) {
        Write-Warning "WARNING: Config file for $nodeId not found at '$configFilePath'. Skipping this node."
        continue # Skip to the next node
    }

    Write-Host "Starting $nodeId with config: $configFilePath"
    
    # --- CORRECTED ARGUMENT HANDLING ---
    # Prepare arguments as an ARRAY for Start-Process
    $argumentsArray = @(
        "exec"              # Command for dotnet
        "`"$dllPath`""      # Quoted path to the DLL
        "--"                # Separator for application arguments
        "--ConfigPath"      # Argument name
        "`"$configFilePath`"" # Quoted path to the config file
    )
    # --- END CORRECTION ---
    
    # Start the process in a new window using the argument array
    try {
        # Pass the array to ArgumentList
        $process = Start-Process -FilePath "dotnet" -ArgumentList $argumentsArray -PassThru -ErrorAction Stop
    
        # Wait a very short time to allow the window to initialize, then set the title
        Start-Sleep -Milliseconds 500
        if ($process -and -not $process.HasExited) {
            try {
                 if ($process.MainWindowTitle -ne $null) {
                    $process.MainWindowTitle = "$nodeProjectName - $nodeId ($configFileName)"
                    Write-Host "  Set window title for $nodeId (PID: $($process.Id))"
                 } else {
                      Write-Warning "  Could not set window title for $nodeId (PID: $($process.Id)) - Main window might not be ready or accessible."
                 }
            } catch {
                 Write-Warning "  Error setting window title for $nodeId (PID: $($process.Id)): $($_.Exception.Message)"
            }
        } else {
             Write-Warning "  Process for $nodeId exited quickly or could not be retrieved. Cannot set title."
        }
    
    } catch {
        Write-Error "ERROR: Failed to start $nodeId. Command: dotnet $($argumentsArray -join ' '). Error: $($_.Exception.Message)"
    }
}

Write-Host "$numberOfNodes node launch sequence initiated."