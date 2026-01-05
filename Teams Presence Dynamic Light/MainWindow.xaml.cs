using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;

using Windows.Devices.Lights;
using Windows.Devices.Lights.Effects;
using Microsoft.UI.Dispatching;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Teams_Presence_Dynamic_Light
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Fsframe.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // Power management P/Invoke declarations
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr PowerCreateRequest(ref POWER_REQUEST_CONTEXT Context);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerSetRequest(IntPtr PowerRequest, PowerRequestType RequestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerClearRequest(IntPtr PowerRequest, PowerRequestType RequestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct POWER_REQUEST_CONTEXT
        {
            public uint Version;
            public uint Flags;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string SimpleReasonString;
        }

        private enum PowerRequestType
        {
            PowerRequestDisplayRequired = 0,
            PowerRequestSystemRequired = 1,
            PowerRequestAwayModeRequired = 2
        }

        private IntPtr _powerRequest = IntPtr.Zero;

        private string _accessToken = string.Empty;
        private bool _isRunning = false;
        private System.Threading.Timer? _presenceTimer;

        private string? _lampArrayDeviceSelector;
        private Windows.Devices.Enumeration.DeviceInformationCollection? _lampArrayDevices;
        private LampArrayEffectPlaylist _playlist;

        private string availability = "NA";
        private string target_effect = null;
        private Windows.UI.Color color = Windows.UI.Color.FromArgb(255,255,255,255);
        private Windows.UI.Color _previousColor = Windows.UI.Color.FromArgb(255,255,255,255);
        private string _previous_effect = null;
        private long _lastFilePosition = 0;
        private string _lastLogFilePath = string.Empty;
        private readonly object _lockObject = new object();
        public MainWindow()
        {
            InitializeComponent();
            
            // Set default window size
            var appWindow = this.AppWindow;
            appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 400, Height = 300 });
            
            // Handle window closing to cleanup resources
            this.Closed += MainWindow_Closed;
            
            Get_Light();
            PrintAvailableLights();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine("Window closing - cleaning up resources");
            
            _isRunning = false;
            
            if (_presenceTimer != null)
            {
                _presenceTimer.Dispose();
                _presenceTimer = null;
            }
            
            AllowSystemSleep();
            ReleaseLampArrays();
        }

        private async void Get_Light()
        {
            _lampArrayDeviceSelector = LampArray.GetDeviceSelector();
            _lampArrayDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(_lampArrayDeviceSelector);
        }

        private async void PrintAvailableLights()
        {
            await System.Threading.Tasks.Task.Delay(1000); // Wait for Get_Light to complete
            
            if (_lampArrayDevices != null && _lampArrayDevices.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Found {_lampArrayDevices.Count} lamp array device(s):");
                foreach (var device in _lampArrayDevices)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {device.Name} (ID: {device.Id})");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No lamp array devices found");
            }
        }

        private void Token_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _accessToken = textBox.Text;
            }
        }

        private async void Get_Availability_Status()
        {
            try
            {
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                    var response = await httpClient.GetAsync("https://graph.microsoft.com/beta/me/presence");
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var presenceData = System.Text.Json.JsonDocument.Parse(jsonResponse);
                        availability = presenceData.RootElement.GetProperty("availability").GetString();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
            }
        }

        private async void Get_Availability_Status_From_Logs()
        {
            await Task.Run(() =>
            {
                try
                {
                    string logFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\Logs");

                    if (!Directory.Exists(logFolderPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Log folder not found: {logFolderPath}");
                        return;
                    }

                    // Get the most recent MSTeams log file
                    var logFile = Directory.GetFiles(logFolderPath, "MSTeams_*.log")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (logFile == null)
                    {
                        System.Diagnostics.Debug.WriteLine("No MSTeams log files found");
                        return;
                    }

                    string logFilePath = logFile.FullName;

                    // If the log file has changed, reset the position
                    if (logFilePath != _lastLogFilePath)
                    {
                        System.Diagnostics.Debug.WriteLine($"Log file changed to: {logFilePath}");
                        _lastLogFilePath = logFilePath;
                        _lastFilePosition = 0;
                    }

                    // Read only new lines since last position
                    var newLines = ReadNewLinesFromPosition(logFilePath, ref _lastFilePosition);

                    // Find presence status in new lines (most recent first)
                    var statusLines = newLines
                        .Where(line => line.Contains("Received Action: UserPresenceAction: "))
                        .ToList();

                    if (statusLines.Any())
                    {
                        var lastStatusLine = statusLines.Last(); // Get the most recent status
                        System.Diagnostics.Debug.WriteLine($"New presence status line: {lastStatusLine}");

                        var newAvailability = ExtractAvailabilityFromLogLine(lastStatusLine);

                        // Only update if we got a valid status
                        if (newAvailability != "NA")
                        {
                            availability = newAvailability;
                            System.Diagnostics.Debug.WriteLine($"Updated availability to: {availability}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No new presence status found in new log entries");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                }
            });
        }

        private async void Check_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ButtonClick");
            try
            {

                Get_Availability_Status_From_Logs();

                StatusTextBlock.Text = $"Current availability status: {availability}";
                System.Diagnostics.Debug.WriteLine($"Current availability status: {availability}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
            }
        }

        private async void SetLampArrayColor(Windows.UI.Color targetColor, string target_effect = null)
        {
            if (targetColor == _previousColor && target_effect == _previous_effect)
            {
                return; // No change in color or effect, exit early
            }   
            _previousColor = targetColor;
            _previous_effect = target_effect;
    
            if (_lampArrayDevices != null && _lampArrayDevices.Count > 0)
            {
                foreach (var deviceInfo in _lampArrayDevices)
                {
                    var lampArray = await LampArray.FromIdAsync(deviceInfo.Id);
                    if (lampArray != null)
                    {
                        if (_playlist != null)
                        {
                            _playlist.Stop();
                        }

                        _playlist = new LampArrayEffectPlaylist();
                        _playlist.RepetitionMode = LampArrayRepetitionMode.Forever;

                        int[] indices = Enumerable.Range(0, lampArray.LampCount).ToArray();
                        if (target_effect == null)
                        {
                            var effect = new LampArraySolidEffect(lampArray, indices)
                            {
                                Color = targetColor,
                                CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState,
                            };
                            _playlist.Append(effect);
                        }
                        else if(target_effect == "pulse")
                        {
                            var effect = new LampArrayBlinkEffect(lampArray, indices)
                            {
                                Color = targetColor,
                                AttackDuration = TimeSpan.FromMilliseconds(500),
                                DecayDuration = TimeSpan.FromMilliseconds(500),
                                SustainDuration = TimeSpan.FromMilliseconds(1000),
                                RepetitionMode = LampArrayRepetitionMode.Forever,

                            };
                            _playlist.Append(effect);

                        }
                        else
                        {
                            var effect = new LampArraySolidEffect(lampArray, indices)
                            {
                                Color = targetColor,
                                CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
                            };
                            _playlist.Append(effect);
                        }
                        
                        _playlist.Start();
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No lamp array devices found");
            }
        }

        private void ReleaseLampArrays()
        {
            _playlist?.Stop();
            _previousColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            _previous_effect = null;
            _lastFilePosition = 0;
            _lastLogFilePath = string.Empty;
        }

        private void PreventSystemSleep()
        {
            try
            {
                if (_powerRequest == IntPtr.Zero)
                {
                    var context = new POWER_REQUEST_CONTEXT
                    {
                        Version = 0,
                        Flags = 0x00000001, // POWER_REQUEST_CONTEXT_SIMPLE_STRING
                        SimpleReasonString = "Teams Presence Dynamic Light is monitoring presence status"
                    };

                    _powerRequest = PowerCreateRequest(ref context);

                    if (_powerRequest != IntPtr.Zero)
                    {
                        // Prevent system from going to sleep
                        PowerSetRequest(_powerRequest, PowerRequestType.PowerRequestSystemRequired);
                        System.Diagnostics.Debug.WriteLine("Power request created - system will stay awake");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to create power request");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error preventing system sleep: {ex.Message}");
            }
        }

        private void AllowSystemSleep()
        {
            try
            {
                if (_powerRequest != IntPtr.Zero)
                {
                    PowerClearRequest(_powerRequest, PowerRequestType.PowerRequestSystemRequired);
                    CloseHandle(_powerRequest);
                    _powerRequest = IntPtr.Zero;
                    System.Diagnostics.Debug.WriteLine("Power request cleared - system can sleep");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error allowing system sleep: {ex.Message}");
            }
        }

        private async void Toggle_Change(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.IsChecked == true)
            {
                System.Diagnostics.Debug.WriteLine("Toggle is ON - Starting presence monitoring with Timer");
                _isRunning = true;

                // Prevent system sleep to ensure background monitoring continues
                PreventSystemSleep();

                // Use System.Threading.Timer instead of Task loop for more reliable background execution
                // Timer continues to fire even when app is in background
                _presenceTimer = new System.Threading.Timer(async (state) =>
                {
                    if (!_isRunning)
                    {
                        _presenceTimer?.Dispose();
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer tick - Checking presence status...");

                    try
                    {
                        // Read logs synchronously on timer thread
                        string logFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\Logs");

                        if (!Directory.Exists(logFolderPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"Log folder not found: {logFolderPath}");
                            return;
                        }

                        var logFile = Directory.GetFiles(logFolderPath, "MSTeams_*.log")
                            .Select(f => new FileInfo(f))
                            .OrderByDescending(f => f.LastWriteTime)
                            .FirstOrDefault();

                        if (logFile == null)
                        {
                            System.Diagnostics.Debug.WriteLine("No MSTeams log files found");
                            return;
                        }

                        string logFilePath = logFile.FullName;

                        lock (_lockObject)
                        {
                            if (logFilePath != _lastLogFilePath)
                            {
                                System.Diagnostics.Debug.WriteLine($"Log file changed to: {logFilePath}");
                                _lastLogFilePath = logFilePath;
                                _lastFilePosition = 0;
                            }

                            var newLines = ReadNewLinesFromPosition(logFilePath, ref _lastFilePosition);
                            var statusLines = newLines
                                .Where(line => line.Contains("Received Action: UserPresenceAction: "))
                                .ToList();

                            if (statusLines.Any())
                            {
                                var lastStatusLine = statusLines.Last();
                                System.Diagnostics.Debug.WriteLine($"New presence status line: {lastStatusLine}");

                                var newAvailability = ExtractAvailabilityFromLogLine(lastStatusLine);

                                if (newAvailability != "NA")
                                {
                                    availability = newAvailability;
                                    System.Diagnostics.Debug.WriteLine($"Updated availability to: {availability}");
                                }
                            }
                        }

                        // Update UI on UI thread
                        try
                        {
                            await DispatcherQueue.EnqueueAsync(() =>
                            {
                                StatusTextBlock.Text = $"Current availability status: {availability} [{DateTime.Now:HH:mm:ss}]";
                            });
                        }
                        catch (Exception uiEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"UI update error: {uiEx.Message}");
                        }

                        System.Diagnostics.Debug.WriteLine($"Current availability status: {availability}");

                        Windows.UI.Color newColor;
                        string newEffect = null;

                        switch (availability)
                        {
                            case "Available":
                                newColor = Windows.UI.Color.FromArgb(255, 0, 255, 0); //Green
                                break;
                            case "Busy":
                                newColor = Windows.UI.Color.FromArgb(255, 255, 0, 0); //Red
                                break;
                            case "DoNotDisturb":
                                newColor = Windows.UI.Color.FromArgb(255, 255, 0, 0); //Red
                                newEffect = "pulse";
                                break;
                            case "Away":
                                newColor = Windows.UI.Color.FromArgb(255, 255, 165, 0); //Orange
                                break;
                            case "BeRightBack":
                                newColor = Windows.UI.Color.FromArgb(255, 255, 165, 0); //Orange
                                break;
                            case "Offline":
                                newColor = Windows.UI.Color.FromArgb(255, 128, 128, 128); //Gray
                                break;
                            default: // NA or unknown
                                newColor = Windows.UI.Color.FromArgb(255, 255, 255, 255); //White
                                break;
                        }

                        color = newColor;
                        target_effect = newEffect;

                        // Set lamp color on UI thread
                        try
                        {
                            await DispatcherQueue.EnqueueAsync(() =>
                            {
                                SetLampArrayColor(color, target_effect);
                            });
                        }
                        catch (Exception lampEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Lamp update error: {lampEx.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Exception during monitoring: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }

                }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5)); // Fire immediately, then every 5 seconds

                System.Diagnostics.Debug.WriteLine("Timer started successfully");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Toggle is OFF - Stopping presence monitoring");
                _isRunning = false;
                
                // Stop and dispose timer
                if (_presenceTimer != null)
                {
                    _presenceTimer.Dispose();
                    _presenceTimer = null;
                    System.Diagnostics.Debug.WriteLine("Timer disposed");
                }
                
                // Allow system to sleep again
                AllowSystemSleep();
                
                ReleaseLampArrays();
            }
        }

        private string ExtractAvailabilityFromLogLine(string logLine)
        {
            if (string.IsNullOrEmpty(logLine))
            {
                return "NA";
            }

            try
            {
                // Extract text after "UserPresenceAction: "
                var parts = logLine.Split(new[] { "UserPresenceAction: " }, StringSplitOptions.None);
                if (parts.Length < 2)
                {
                    return "NA";
                }

                var statusPart = parts[1].Trim();
                
                // Extract the status value (usually between colons or spaces)
                var statusParts = statusPart.Split(new[] { ':', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (statusParts.Length == 0)
                {
                    return "NA";
                }

                // Get the last part and clean it
                var status = statusParts[statusParts.Length - 1].Trim();
                
                // Remove any non-alphabetic characters
                status = new string(status.Where(char.IsLetter).ToArray());

                System.Diagnostics.Debug.WriteLine($"Extracted status: {status}");
                
                return string.IsNullOrEmpty(status) ? "NA" : status;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting status: {ex.Message}");
                return "NA";
            }
        }

        private List<string> ReadNewLinesFromPosition(string filePath, ref long lastPosition)
        {
            var newLines = new List<string>();
            
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var fileInfo = new FileInfo(filePath);
                    var currentLength = fileInfo.Length;

                    // If file was truncated or replaced, start from beginning
                    if (currentLength < lastPosition)
                    {
                        System.Diagnostics.Debug.WriteLine("Log file was reset or truncated, starting from beginning");
                        lastPosition = 0;
                    }

                    // Seek to the last read position
                    fileStream.Seek(lastPosition, SeekOrigin.Begin);

                    using (var streamReader = new StreamReader(fileStream))
                    {
                        string? line;
                        while ((line = streamReader.ReadLine()) != null)
                        {
                            newLines.Add(line);
                        }

                        // Update the last position to current position
                        lastPosition = fileStream.Position - 1;
                        if (lastPosition < 0) lastPosition = 0; // Ensure non-negative
                    }
                }

                if (newLines.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Read {newLines.Count} new lines from log");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading new lines: {ex.Message}");
            }

            return newLines;
        }
    }

    public static class DispatcherQueueExtensions
    {
        public static Task EnqueueAsync(this DispatcherQueue dispatcher, Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            return tcs.Task;
        }
    }
}