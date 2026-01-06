using Windows.Storage;
using System;

namespace Teams_Presence_Dynamic_Light
{
    /// <summary>
    /// Settings manager compatible with WinUI 3 / .NET 8
    /// Replaces the traditional Settings.Designer.cs pattern
    /// </summary>
    public static class Settings
    {
        private static readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        /// <summary>
        /// Default settings instance (mimics the traditional Settings.Default pattern)
        /// </summary>
        public static class Default
        {
            /// <summary>
            /// Gets or sets the Teams API token
            /// </summary>
            public static string Token
            {
                get
                {
                    try
                    {
                        if (_localSettings.Values.ContainsKey("Token"))
                        {
                            return _localSettings.Values["Token"] as string ?? string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading Token setting: {ex.Message}");
                    }
                    return string.Empty;
                }
                set
                {
                    try
                    {
                        _localSettings.Values["Token"] = value ?? string.Empty;
                        System.Diagnostics.Debug.WriteLine($"Token setting saved: {(string.IsNullOrEmpty(value) ? "Empty" : "Has value")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error saving Token setting: {ex.Message}");
                    }
                }
            }

            /// <summary>
            /// Gets or sets whether monitoring starts automatically on app launch
            /// </summary>
            public static bool AutoStart
            {
                get
                {
                    try
                    {
                        if (_localSettings.Values.ContainsKey("AutoStart"))
                        {
                            return (bool)_localSettings.Values["AutoStart"];
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading AutoStart setting: {ex.Message}");
                    }
                    return false;
                }
                set
                {
                    try
                    {
                        _localSettings.Values["AutoStart"] = value;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error saving AutoStart setting: {ex.Message}");
                    }
                }
            }

            /// <summary>
            /// Gets or sets the polling interval in seconds
            /// </summary>
            public static int PollingInterval
            {
                get
                {
                    try
                    {
                        if (_localSettings.Values.ContainsKey("PollingInterval"))
                        {
                            return (int)_localSettings.Values["PollingInterval"];
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading PollingInterval setting: {ex.Message}");
                    }
                    return 5; // Default 5 seconds
                }
                set
                {
                    try
                    {
                        _localSettings.Values["PollingInterval"] = value;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error saving PollingInterval setting: {ex.Message}");
                    }
                }
            }

            /// <summary>
            /// Clears all saved settings
            /// </summary>
            public static void Clear()
            {
                try
                {
                    _localSettings.Values.Clear();
                    System.Diagnostics.Debug.WriteLine("All settings cleared");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error clearing settings: {ex.Message}");
                }
            }

            /// <summary>
            /// Save method for compatibility (not needed with ApplicationDataContainer but kept for API compatibility)
            /// </summary>
            public static void Save()
            {
                // ApplicationDataContainer automatically persists changes
                System.Diagnostics.Debug.WriteLine("Settings are automatically saved");
            }

            /// <summary>
            /// Reload method for compatibility (not needed with ApplicationDataContainer but kept for API compatibility)
            /// </summary>
            public static void Reload()
            {
                // ApplicationDataContainer automatically reflects current values
                System.Diagnostics.Debug.WriteLine("Settings are automatically reloaded");
            }
        }
    }
}
