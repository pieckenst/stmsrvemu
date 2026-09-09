using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;

namespace StmServerEmulator.Config
{
    /// <summary>
    /// Configuration manager for the Steam Server Emulator
    /// Handles reading and writing configuration values from emulator.ini
    /// </summary>
    public class ConfigManager
    {
        private static Dictionary<string, string> configData = new Dictionary<string, string>();
        private static string configFilePath = "emulator.ini";
        private static bool isLoaded = false;

        /// <summary>
        /// Get the current configuration data
        /// </summary>
        public static Dictionary<string, string> GetConfig()
        {
            if (!isLoaded)
            {
                LoadConfig();
            }
            return new Dictionary<string, string>(configData);
        }

        /// <summary>
        /// Get a specific configuration value
        /// </summary>
        public static string GetValue(string key, string defaultValue = "")
        {
            if (!isLoaded)
            {
                LoadConfig();
            }

            if (configData.ContainsKey(key))
            {
                return configData[key];
            }
            return defaultValue;
        }

        /// <summary>
        /// Set a configuration value in memory
        /// </summary>
        public static void SetValue(string key, string value)
        {
            configData[key] = value;
        }

        /// <summary>
        /// Load configuration from emulator.ini file
        /// </summary>
        public static void LoadConfig()
        {
            Dictionary<string, string> defaults = ConfigDefaults.GetDefaults();
            configData = new Dictionary<string, string>(defaults);

            if (!File.Exists(configFilePath))
            {
                // Create default config file if it doesn't exist
                CreateDefaultConfigFile(defaults);
                isLoaded = true;
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(configFilePath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                    {
                        continue;
                    }

                    // Parse key=value pairs
                    int equalIndex = trimmedLine.IndexOf('=');
                    if (equalIndex > 0)
                    {
                        string key = trimmedLine.Substring(0, equalIndex).Trim();
                        string value = trimmedLine.Substring(equalIndex + 1).Trim();

                        // Handle quoted values
                        value = CleanConfigValue(value);

                        if (!string.IsNullOrEmpty(key))
                        {
                            configData[key] = value;
                        }
                    }
                }

                isLoaded = true;
            }
            catch (Exception ex)
            {
                throw new Exception("Error loading configuration file: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Clean configuration value by removing quotes and handling comments
        /// </summary>
        private static string CleanConfigValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Remove comments
            int commentIndex = value.IndexOf(';');
            if (commentIndex >= 0)
            {
                value = value.Substring(0, commentIndex).Trim();
            }

            commentIndex = value.IndexOf('#');
            if (commentIndex >= 0)
            {
                value = value.Substring(0, commentIndex).Trim();
            }

            // Handle quoted values
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
            {
                value = value.Substring(1, value.Length - 2);
            }
            else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 2)
            {
                value = value.Substring(1, value.Length - 2);
            }

            return value.Trim();
        }

        /// <summary>
        /// Save a configuration value to the emulator.ini file
        /// </summary>
        public static void SaveConfigValue(string key, string value, string oldKey = null)
        {
            if (!File.Exists(configFilePath))
            {
                throw new FileNotFoundException("Configuration file not found: " + configFilePath);
            }

            try
            {
                string[] lines = File.ReadAllLines(configFilePath);
                List<string> newLines = new List<string>(lines);
                bool keyExists = false;

                // Check if the old_key or key already exists
                for (int i = 0; i < newLines.Count; i++)
                {
                    string line = newLines[i].TrimStart();
                    
                    // Check for commented keys
                    string uncommentedLine = line.StartsWith(";") ? line.Substring(1).TrimStart() : line;

                    bool isOldKeyMatch = !string.IsNullOrEmpty(oldKey) && 
                        uncommentedLine.StartsWith(oldKey + "=");
                    bool isKeyMatch = uncommentedLine.StartsWith(key + "=");
                    bool isCommentedKey = line.StartsWith(";" + key + "=");

                    if (isOldKeyMatch || isKeyMatch || isCommentedKey)
                    {
                        // Replace the line with new key and value
                        newLines[i] = key + "=" + value;
                        keyExists = true;
                        break;
                    }
                }

                // Ensure the file ends with a newline
                if (newLines.Count > 0 && !newLines[newLines.Count - 1].EndsWith("\n"))
                {
                    newLines[newLines.Count - 1] += "\n";
                }

                // If the key doesn't exist, add it as a new line
                if (!keyExists)
                {
                    newLines.Add(key + "=" + value + "\n");
                }

                // Write the modified content back to the file
                File.WriteAllLines(configFilePath, newLines);

                // Update in-memory config
                configData[key] = value;
            }
            catch (Exception ex)
            {
                throw new Exception("Error saving configuration value: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Reload configuration from file
        /// </summary>
        public static void ReloadConfig()
        {
            LoadConfig();
        }

        /// <summary>
        /// Create a default configuration file
        /// </summary>
        private static void CreateDefaultConfigFile(Dictionary<string, string> defaults)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[config]");
                sb.AppendLine("# Steam Server Emulator Configuration File");
                sb.AppendLine("# Generated by STMServer Emulator");
                sb.AppendLine();

                foreach (KeyValuePair<string, string> kvp in defaults)
                {
                    sb.AppendLine(kvp.Key + "=" + kvp.Value);
                }

                File.WriteAllText(configFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                throw new Exception("Error creating default configuration file: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Check if configuration is loaded
        /// </summary>
        public static bool IsLoaded
        {
            get { return isLoaded; }
        }

        /// <summary>
        /// Get the configuration file path
        /// </summary>
        public static string ConfigFilePath
        {
            get { return configFilePath; }
            set { configFilePath = value; }
        }
    }
}
