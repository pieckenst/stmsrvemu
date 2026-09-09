using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace StmServerEmulator.Utilities
{
    /// <summary>
    /// String helper functions for the Steam Server Emulator
    /// Handles string validation, manipulation, and Steam-specific string operations
    /// </summary>
    public class StringHelper
    {
        private static readonly Regex EmailRegex = new Regex(@"^[\w\.-]+@[\w\.-]+$");
        private static readonly Regex UsernameRegex = new Regex(@"^[a-zA-Z0-9_]+$");

        /// <summary>
        /// Validate an email address
        /// </summary>
        public static int CheckEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return 3; // Invalid email
            }

            return EmailRegex.IsMatch(email) ? 0 : 3;
        }

        /// <summary>
        /// Validate a username
        /// </summary>
        public static int CheckUsername(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return 2; // Invalid username
            }

            return UsernameRegex.IsMatch(username) ? 0 : 2;
        }

        /// <summary>
        /// Compare two strings (like Python's cmp function)
        /// </summary>
        public static int CompareStrings(string a, string b)
        {
            int result = string.Compare(a, b, StringComparison.Ordinal);
            return Math.Sign(result);
        }

        /// <summary>
        /// Format a byte array as a hex string
        /// </summary>
        public static string ByteArrayToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.AppendFormat("{0:x2}", b);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Convert a hex string to a byte array
        /// </summary>
        public static byte[] HexToByteArray(string hex)
        {
            if (hex.Length % 2 != 0)
            {
                throw new ArgumentException("Hex string must have an even length");
            }

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// Generate a random string of specified length
        /// </summary>
        public static string GenerateRandomString(int length, string charset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            if (length <= 0)
            {
                return string.Empty;
            }

            Random random = new Random();
            StringBuilder sb = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                int index = random.Next(charset.Length);
                sb.Append(charset[index]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generate a random alphanumeric string
        /// </summary>
        public static string GenerateRandomAlphanumeric(int length)
        {
            const string charset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return GenerateRandomString(length, charset);
        }

        /// <summary>
        /// Generate a random numeric string
        /// </summary>
        public static string GenerateRandomNumeric(int length)
        {
            const string charset = "0123456789";
            return GenerateRandomString(length, charset);
        }

        /// <summary>
        /// Calculate MD5 hash of a string
        /// </summary>
        public static string CalculateMd5(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return ByteArrayToHex(hashBytes);
            }
        }

        /// <summary>
        /// Calculate SHA1 hash of a string
        /// </summary>
        public static string CalculateSha1(string input)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = sha1.ComputeHash(inputBytes);
                return ByteArrayToHex(hashBytes);
            }
        }

        /// <summary>
        /// Calculate SHA256 hash of a string
        /// </summary>
        public static string CalculateSha256(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = sha256.ComputeHash(inputBytes);
                return ByteArrayToHex(hashBytes);
            }
        }

        /// <summary>
        /// Calculate SHA256 hash of byte array
        /// </summary>
        public static byte[] CalculateSha256(byte[] input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(input);
            }
        }

        /// <summary>
        /// Base64 encode a string
        /// </summary>
        public static string Base64Encode(string input)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(input);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Base64 decode a string
        /// </summary>
        public static string Base64Decode(string input)
        {
            byte[] bytes = Convert.FromBase64String(input);
            return Encoding.ASCII.GetString(bytes);
        }

        /// <summary>
        /// Truncate a string to a maximum length
        /// </summary>
        public static string Truncate(string input, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
            {
                return input;
            }

            return input.Substring(0, maxLength - suffix.Length) + suffix;
        }

        /// <summary>
        /// Convert string to C-style wide string (UTF-16-LE)
        /// </summary>
        public static byte[] ToCStringFormat(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return new byte[0];
            }
            return Encoding.Unicode.GetBytes(s);
        }

        /// <summary>
        /// Convert C-style wide string (UTF-16-LE) to string
        /// </summary>
        public static string FromCStringFormat(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }
            return Encoding.Unicode.GetString(bytes);
        }

        /// <summary>
        /// Convert string to Latin-1 bytes
        /// </summary>
        public static byte[] ToLatin1Bytes(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return new byte[0];
            }
            return Encoding.GetEncoding("ISO-8859-1").GetBytes(s);
        }

        /// <summary>
        /// Convert Latin-1 bytes to string
        /// </summary>
        public static string FromLatin1Bytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }
            return Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
        }

        /// <summary>
        /// Remove all whitespace from a string
        /// </summary>
        public static string RemoveWhitespace(string input)
        {
            return Regex.Replace(input, @"\s+", "");
        }

        /// <summary>
        /// Check if a string contains only alphanumeric characters
        /// </summary>
        public static bool IsAlphanumeric(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }
            return Regex.IsMatch(input, @"^[a-zA-Z0-9]+$");
        }

        /// <summary>
        /// Check if a string contains only numeric characters
        /// </summary>
        public static bool IsNumeric(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }
            return Regex.IsMatch(input, @"^[0-9]+$");
        }

        /// <summary>
        /// Capitalize the first letter of a string
        /// </summary>
        public static string Capitalize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return char.ToUpper(input[0]) + input.Substring(1);
        }

        /// <summary>
        /// Convert a string to title case
        /// </summary>
        public static string ToTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            TextInfo textInfo = new System.Globalization.CultureInfo("en-US", false).TextInfo;
            return textInfo.ToTitleCase(input.ToLower());
        }

        /// <summary>
        /// Replace all occurrences of a substring
        /// </summary>
        public static string ReplaceAll(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return input.Replace(oldValue, newValue);
        }

        /// <summary>
        /// Split a string into lines
        /// </summary>
        public static string[] SplitLines(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return new string[0];
            }
            return input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        /// <summary>
        /// Join strings with a separator
        /// </summary>
        public static string JoinStrings(string[] strings, string separator)
        {
            if (strings == null || strings.Length == 0)
            {
                return string.Empty;
            }
            return string.Join(separator, strings);
        }

        /// <summary>
        /// Escape special characters in a string for regex
        /// </summary>
        public static string EscapeRegex(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return Regex.Escape(input);
        }

        /// <summary>
        /// Format a byte size to human-readable format
        /// </summary>
        public static string FormatByteSize(long bytes)
        {
            string[] sizes = new string[] { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return string.Format("{0:0.##} {1}", size, sizes[order]);
        }

        /// <summary>
        /// Format a time duration to human-readable format
        /// </summary>
        public static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return string.Format("{0}d {1}h {2}m", timeSpan.Days, timeSpan.Hours, timeSpan.Minutes);
            }
            else if (timeSpan.TotalHours >= 1)
            {
                return string.Format("{0}h {1}m {2}s", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return string.Format("{0}m {1}s", timeSpan.Minutes, timeSpan.Seconds);
            }
            else
            {
                return string.Format("{0}s", timeSpan.Seconds);
            }
        }
    }
}
