using System;
using System.IO;
using System.Collections.Generic;
using StmServerEmulator.Config;

namespace StmServerEmulator.Utilities
{
    /// <summary>
    /// Directory helper functions for the Steam Server Emulator
    /// Handles directory creation, validation, and file system operations
    /// </summary>
    public class DirectoryHelper
    {
        /// <summary>
        /// Create all required directories for the emulator
        /// </summary>
        public static void CreateRequiredDirectories()
        {
            var config = ConfigManager.GetConfig();
            
            List<string> directoriesToCreate = new List<string>
            {
                config["v3storagedir2"],
                config["v3manifestdir2"],
                config["v2storagedir"],
                config["v4storagedir"],
                config["v2manifestdir"],
                config["v4manifestdir"],
                config["storagedir"],
                config["manifestdir"],
                config["betastoragedir"],
                config["betamanifestdir"],
                config["packagedir"],
                config["configsdir"],
                Path.Combine(config["configsdir"], "custom_neuter"),
                config["steam2sdkdir"],
                "debug",
                "logs",
                "client",
                "clientstats",
                Path.Combine("clientstats", "clientstats"),
                Path.Combine("clientstats", "gamestats"),
                Path.Combine("clientstats", "bugreports"),
                Path.Combine("clientstats", "steamstats"),
                Path.Combine("clientstats", "phonehome"),
                Path.Combine("clientstats", "exceptionlogs"),
                Path.Combine("clientstats", "crashdump"),
                Path.Combine("clientstats", "surveys"),
                Path.Combine("clientstats", "downloadstats"),
                "files",
                Path.Combine("files", "temp"),
                Path.Combine("files", "appcache"),
                Path.Combine("files", "appcache", "2008"),
                Path.Combine("files", "appcache", "2009_2010"),
                Path.Combine("files", "appcache", "2010_2011"),
                Path.Combine("files", "cache"),
                Path.Combine("files", "cache", "appinfo"),
                Path.Combine("files", "cache", "appinfo", "2008"),
                Path.Combine("files", "cache", "appinfo", "2008", "lan"),
                Path.Combine("files", "cache", "appinfo", "2008", "wan"),
                Path.Combine("files", "cache", "appinfo", "2009_2010"),
                Path.Combine("files", "cache", "appinfo", "2009_2010", "lan"),
                Path.Combine("files", "cache", "appinfo", "2009_2010", "wan"),
                Path.Combine("files", "cache", "appinfo", "2010_2011"),
                Path.Combine("files", "cache", "appinfo", "2010_2011", "lan"),
                Path.Combine("files", "cache", "appinfo", "2010_2011", "wan"),
                Path.Combine("files", "cache", "internal"),
                Path.Combine("files", "cache", "external"),
                Path.Combine("files", "beta1_ftp"),
                Path.Combine("files", "email_tpl"),
                Path.Combine("files", "sql"),
                Path.Combine("files", "mdb"),
                Path.Combine("files", "mdb", "data"),
                Path.Combine("files", "mod_blob"),
                Path.Combine("files", "mod_package"),
                Path.Combine("files", "mod_package", "2013"),
                Path.Combine("files", "mod_package", "2016"),
                Path.Combine("files", "mod_pkg", "steam", "global"),
                Path.Combine("files", "mod_pkg", "steamui", "global"),
            };

            // Rename custom folders if they exist
            RenameCustomFolders();

            // Create directories
            foreach (string dirPath in directoriesToCreate)
            {
                try
                {
                    if (!Directory.Exists(dirPath))
                    {
                        Directory.CreateDirectory(dirPath);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to create directory: " + dirPath, ex);
                }
            }
        }

        /// <summary>
        /// Rename specific folders to new names if they exist
        /// </summary>
        private static void RenameCustomFolders()
        {
            Dictionary<string, string> foldersToRename = new Dictionary<string, string>
            {
                { "files/custom", "files/mod_blob" },
                { "files/pkg_add", "files/mod_pkg" }
            };

            foreach (KeyValuePair<string, string> kvp in foldersToRename)
            {
                try
                {
                    if (Directory.Exists(kvp.Key))
                    {
                        Directory.Move(kvp.Key, kvp.Value);
                    }
                }
                catch
                {
                    // Ignore errors during renaming
                }
            }
        }

        /// <summary>
        /// Ensure a directory exists, create if it doesn't
        /// </summary>
        public static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// Copy a directory recursively
        /// </summary>
        public static void CopyDirectory(string sourcePath, string destinationPath)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException("Source directory not found: " + sourcePath);
            }

            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourcePath))
            {
                string fileName = Path.GetFileName(filePath);
                string destFilePath = Path.Combine(destinationPath, fileName);
                File.Copy(filePath, destFilePath, true);
            }

            // Copy all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourcePath))
            {
                string dirName = Path.GetFileName(dirPath);
                string destDirPath = Path.Combine(destinationPath, dirName);
                CopyDirectory(dirPath, destDirPath);
            }
        }

        /// <summary>
        /// Delete a directory recursively
        /// </summary>
        public static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        /// <summary>
        /// Get directory size in bytes
        /// </summary>
        public static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            long size = 0;

            foreach (string filePath in Directory.GetFiles(path))
            {
                size += new FileInfo(filePath).Length;
            }

            foreach (string dirPath in Directory.GetDirectories(path))
            {
                size += GetDirectorySize(dirPath);
            }

            return size;
        }

        /// <summary>
        /// Clean up temporary files in a directory
        /// </summary>
        public static void CleanupTempFiles(string path, string pattern = "*.tmp")
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (string filePath in Directory.GetFiles(path, pattern))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }

        /// <summary>
        /// Sanitize a filename to prevent directory traversal
        /// </summary>
        public static string SanitizeFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException("Filename cannot be null or empty");
            }

            // Check for valid extensions
            if (!filename.EndsWith(".pkg") && !filename.EndsWith(".pkg_rsa_signature"))
            {
                throw new ArgumentException("Invalid file extension. Filename must end with '.pkg' or '.pkg_rsa_signature'");
            }

            // Normalize the path
            string normalized = Path.GetFullPath(filename);

            // Check for directory traversal attempts
            if (normalized.StartsWith("/") || normalized.StartsWith("\\") || normalized.Contains(".."))
            {
                throw new ArgumentException("Directory traversal attempt detected");
            }

            return normalized;
        }

        /// <summary>
        /// Get the application directory
        /// </summary>
        public static string GetApplicationDirectory()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Combine paths safely
        /// </summary>
        public static string SafePathCombine(params string[] paths)
        {
            string result = paths[0];
            for (int i = 1; i < paths.Length; i++)
            {
                result = Path.Combine(result, paths[i]);
            }
            return result;
        }
    }
}
