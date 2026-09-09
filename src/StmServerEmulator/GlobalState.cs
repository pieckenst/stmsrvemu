using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using StmServerEmulator.Config;

namespace StmServerEmulator
{
    /// <summary>
    /// Global state and configuration for the Steam Server Emulator
    /// Contains server state variables, version strings, network configuration, and cached data
    /// </summary>
    public static class GlobalState
    {
        // =============================================================================
        // PLATFORM DETECTION
        // =============================================================================
        public static bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        public static string CurrentOS = Environment.OSVersion.Platform.ToString();

        // =============================================================================
        // VERSION STRINGS
        // =============================================================================
        public static string LocalVer = "0.90.2 -- In Memorium - Dormine (1980-2026)";
        public static string EmuVer = "0";
        public static string UiVer = "0";
        public static string MdbVer = "0";
        public static string WebVer = "0";
        public static string TgtVersion = "2";
        public static int RecordVer = 0;
        public static int SteamVer = 0;
        public static object SteamUiVer = 0; // Can be int or string like "0.6.1.0/1.1.0.0"
        public static bool BootstrapperCreated = false;

        // =============================================================================
        // SERVER STATE & LIFECYCLE
        // =============================================================================
        public static Dictionary<string, Thread> ServerThreads = new Dictionary<string, Thread>();
        public static HashSet<string> DisabledServers = new HashSet<string>();
        public static bool ShutdownRequested = false;
        public static DateTime? StartTime = null;
        public static bool StopServer = false;
        public static List<object> Servers = new List<object>();
        public static object Watchdog = null;

        // =============================================================================
        // PROCESS HANDLES
        // =============================================================================
        public static object MariaDbProcess = null;
        public static DateTime? MariaDbLaunchDt = null;
        public static bool MariaDbInitialized = false;
        public static object HttpdProcess = null;
        public static List<int> HttpdChildPidList = new List<int>();
        public static string OriginalPythonExecutable = null;
        public static string[] OriginalCmdArgs = null;

        // =============================================================================
        // NETWORK CONFIGURATION
        // =============================================================================
        public static object ServerNet = null;
        public static object ServerNetwork = null;
        public static string ServerIp = "";
        public static byte[] ServerIpBytes = new byte[4];
        public static string PublicIp = "";
        public static byte[] PublicIpBytes = new byte[4];
        public static bool UseWebserver = true;
        public static string CsRegion = "US";
        public static int CellId = 1;
        public static bool AioServer = false;
        public static string PeerPassword = "";
        public static string DirIsMaster = "false";

        // =============================================================================
        // EMAIL & AUTH CONFIGURATION
        // =============================================================================
        public static string SmtpEnable = "false";
        public static string ForceEmailVerification = "false";
        public static Dictionary<string, DateTime> AuthenticatedIps = new Dictionary<string, DateTime>();

        // =============================================================================
        // CDR & BLOB CACHE
        // =============================================================================
        public static string CddbDatetime = null;
        public static string FormattedDate = "";
        public static string FormattedUnderscoreDate = "";
        public static bool CompilingCdr = false;
        public static object FirstblobEval = null;
        public static Dictionary<byte[], object> CdrDictionary = null;
        public static object CdrObj = null;
        public static List<object> SubscriptionPassList = null;
        public static object CurrentBlobDatetime = null;

        public static byte[] CdrBlobLan = null;
        public static byte[] CdrBlobWan = null;
        public static byte[] CdrBlobLan2003 = null;
        public static byte[] CdrBlobWan2003 = null;
        public static object CdrBlobLock = new object();

        // =============================================================================
        // FILE & PATH CONFIGURATION
        // =============================================================================
        public static Dictionary<string, string> KnownBlobs = new Dictionary<string, string>();
        public static Dictionary<string, string> KnownDats = new Dictionary<string, string>();
        public static string CustomNeuterPath = "files/configs/custom_neuter";
        public static string NeuterPath = "neuter.py";
        public static object SettingsDb = null;

        // =============================================================================
        // MISC STATE
        // =============================================================================
        public static bool IniChangedByServer = false;
        public static bool HideConvertPrints = false;
        public static List<string> PrependedModifiers = new List<string>();
        public static List<string> AppendedModifiers = new List<string>();
        public static string UpdateException1 = "";
        public static string UpdateException2 = "";

        // =============================================================================
        // APP ID CONSTANTS
        // =============================================================================
        public static readonly int[] DedicatedServerAppids = new int[]
        {
            4, 5, 153, 154, 203, 204, 205, 210, 310, 311, 313, 314, 524, 538, 560, 561,
            562, 601, 602, 635, 740, 1203, 1204, 1213, 1241, 1253, 1254, 1273, 2144,
            2145, 2403, 2404, 2740, 2741, 2912, 2915, 2916, 4207, 4240, 4270, 4922,
            4940, 8680, 8710, 8730, 8770, 13180, 13181, 13182, 17505, 17515, 17525,
            17535, 17575, 17585, 17705, 17718
        };

        public static readonly int[] GameEngineFileAppids = new int[]
        {
            0, 3, 7, 8, 200, 201, 212, 216, 217, 254, 255, 256, 257, 258, 264, 315,
            316, 317, 322, 521, 572, 573, 1000, 1002, 1202, 1212, 1304, 1309, 1634,
            1644, 2101, 2131, 2811, 4411, 4421, 6301, 6401, 6551, 6861, 6871, 6881,
            7001, 7011, 7221, 7801, 7811, 8001, 8011, 10622, 10644, 13141, 16422, 17712
        };

        // =============================================================================
        // IP ADDRESSES FOR NEUTERING (Valve/Steam IPs to replace)
        // =============================================================================
        public static readonly byte[][] IpAddresses = new byte[][]
        {
            Encoding.ASCII.GetBytes("63.251.171.132"),
            Encoding.ASCII.GetBytes("65.122.178.71"),
            Encoding.ASCII.GetBytes("67.132.200.140"),
            Encoding.ASCII.GetBytes("68.142.64.162"),
            Encoding.ASCII.GetBytes("68.142.64.163"),
            Encoding.ASCII.GetBytes("68.142.64.164"),
            Encoding.ASCII.GetBytes("68.142.64.165"),
            Encoding.ASCII.GetBytes("68.142.64.166"),
            Encoding.ASCII.GetBytes("68.142.72.250"),
            Encoding.ASCII.GetBytes("68.142.88.34"),
            Encoding.ASCII.GetBytes("68.142.91.34"),
            Encoding.ASCII.GetBytes("68.142.91.35"),
            Encoding.ASCII.GetBytes("69.28.140.245"),
            Encoding.ASCII.GetBytes("69.28.140.246"),
            Encoding.ASCII.GetBytes("69.28.140.247"),
            Encoding.ASCII.GetBytes("69.28.145.170"),
            Encoding.ASCII.GetBytes("69.28.145.171"),
            Encoding.ASCII.GetBytes("69.28.148.250"),
            Encoding.ASCII.GetBytes("69.28.151.178"),
            Encoding.ASCII.GetBytes("69.28.152.198"),
            Encoding.ASCII.GetBytes("69.28.153.82"),
            Encoding.ASCII.GetBytes("69.28.156.250"),
            Encoding.ASCII.GetBytes("69.28.191.84"),
            Encoding.ASCII.GetBytes("72.165.61.141"),
            Encoding.ASCII.GetBytes("72.165.61.142"),
            Encoding.ASCII.GetBytes("72.165.61.143"),
            Encoding.ASCII.GetBytes("72.165.61.161"),
            Encoding.ASCII.GetBytes("72.165.61.162"),
            Encoding.ASCII.GetBytes("72.165.61.185"),
            Encoding.ASCII.GetBytes("72.165.61.186"),
            Encoding.ASCII.GetBytes("72.165.61.187"),
            Encoding.ASCII.GetBytes("72.165.61.188"),
            Encoding.ASCII.GetBytes("72.165.61.189"),
            Encoding.ASCII.GetBytes("72.165.61.190"),
            Encoding.ASCII.GetBytes("79.141.174.10"),
            Encoding.ASCII.GetBytes("81.171.115.6"),
            Encoding.ASCII.GetBytes("86.148.72.250"),
            Encoding.ASCII.GetBytes("87.248.196.117"),
            Encoding.ASCII.GetBytes("87.248.196.194"),
            Encoding.ASCII.GetBytes("87.248.196.195"),
            Encoding.ASCII.GetBytes("87.248.196.196"),
            Encoding.ASCII.GetBytes("87.248.196.197"),
            Encoding.ASCII.GetBytes("87.248.196.198"),
            Encoding.ASCII.GetBytes("87.248.196.199"),
            Encoding.ASCII.GetBytes("87.248.196.200"),
            Encoding.ASCII.GetBytes("99.233.135.72"),
            Encoding.ASCII.GetBytes("103.76.126.199"),
            Encoding.ASCII.GetBytes("193.34.50.6"),
            Encoding.ASCII.GetBytes("194.124.229.14"),
            Encoding.ASCII.GetBytes("207.173.176.210"),
            Encoding.ASCII.GetBytes("207.173.176.215"),
            Encoding.ASCII.GetBytes("207.173.176.216"),
            Encoding.ASCII.GetBytes("207.173.177.11"),
            Encoding.ASCII.GetBytes("207.173.177.12"),
            Encoding.ASCII.GetBytes("207.173.178.127"),
            Encoding.ASCII.GetBytes("207.173.178.178"),
            Encoding.ASCII.GetBytes("207.173.178.194"),
            Encoding.ASCII.GetBytes("207.173.178.196"),
            Encoding.ASCII.GetBytes("207.173.178.198"),
            Encoding.ASCII.GetBytes("207.173.178.214"),
            Encoding.ASCII.GetBytes("207.173.178.216"),
            Encoding.ASCII.GetBytes("207.173.179.14"),
            Encoding.ASCII.GetBytes("207.173.179.87"),
            Encoding.ASCII.GetBytes("207.173.179.151"),
            Encoding.ASCII.GetBytes("207.173.179.168"),
            Encoding.ASCII.GetBytes("207.173.179.179"),
            Encoding.ASCII.GetBytes("208.111.133.84"),
            Encoding.ASCII.GetBytes("208.111.133.85"),
            Encoding.ASCII.GetBytes("208.111.158.52"),
            Encoding.ASCII.GetBytes("208.111.158.53"),
            Encoding.ASCII.GetBytes("208.111.171.82"),
            Encoding.ASCII.GetBytes("208.111.171.83"),
            Encoding.ASCII.GetBytes("213.202.254.131"),
            Encoding.ASCII.GetBytes("217.64.127.2"),
            Encoding.ASCII.GetBytes("888.888.888.888"),
            Encoding.ASCII.GetBytes("888.888.888.889"),
            Encoding.ASCII.GetBytes("888.888.888.890"),
            Encoding.ASCII.GetBytes("888.888.888.891"),
        };

        public static readonly byte[][] ExtraIps = new byte[][]
        {
            Encoding.ASCII.GetBytes("207.173.177.12:27010"),
            Encoding.ASCII.GetBytes("207.173.177.11:27010"),
            Encoding.ASCII.GetBytes("207.173.177.10:27010"),
            Encoding.ASCII.GetBytes("207.173.177.12:27011"),
            Encoding.ASCII.GetBytes("207.173.177.11:27011"),
            Encoding.ASCII.GetBytes("207.173.177.10:27011")
        };

        public static readonly byte[][] LoopbackIps = new byte[][]
        {
            Encoding.ASCII.GetBytes("127.0.0.2")
        };

        // =============================================================================
        // HELPER FUNCTIONS
        // =============================================================================

        /// <summary>
        /// Convert string to C-style wide string (UTF-16-LE). Used by Beta1 clients.
        /// </summary>
        public static byte[] ToCStringFormat(string s)
        {
            if (s == null)
            {
                return new byte[0];
            }
            return Encoding.Unicode.GetBytes(s);
        }

        /// <summary>
        /// Get appropriate IP address based on LAN/WAN and community settings.
        /// Priority order:
        /// 1. http_domainname (if set) - used for BOTH LAN and WAN
        /// 2. http_ip (if set) - used for BOTH LAN and WAN
        /// 3. server_ip for LAN, public_ip for WAN
        /// </summary>
        public static string GetOctalIp(bool isLan, bool isCommunity = false)
        {
            var config = ConfigManager.GetConfig();
            
            string httpDomainname = isCommunity ? config["community_domainname"] : config["http_domainname"];
            string httpIp = isCommunity ? config["community_ip"] : config["http_ip"];
            string serverIpCfg = config["server_ip"];
            string publicIpCfg = config["public_ip"];

            // http_domainname takes highest precedence - applies to both LAN and WAN
            if (!string.IsNullOrEmpty(httpDomainname))
            {
                return httpDomainname;
            }

            // http_ip is second priority - applies to both LAN and WAN
            if (!string.IsNullOrEmpty(httpIp))
            {
                return httpIp;
            }

            // Default: server_ip for LAN, public_ip for WAN
            return isLan ? serverIpCfg : publicIpCfg;
        }

        /// <summary>
        /// Get appropriate IP address as bytes based on LAN/WAN and community settings.
        /// </summary>
        public static byte[] GetOctalIpBytes(bool isLan, bool isCommunity = false)
        {
            string ip = GetOctalIp(isLan, isCommunity);
            return Encoding.GetEncoding("ISO-8859-1").GetBytes(ip);
        }

        /// <summary>
        /// Get connection IP as bytes based on LAN/WAN setting.
        /// </summary>
        public static byte[] GetConnIpBytes(bool isLan)
        {
            var config = ConfigManager.GetConfig();
            string ip = isLan ? config["server_ip"] : config["public_ip"];
            return Encoding.GetEncoding("ISO-8859-1").GetBytes(ip);
        }

        /// <summary>
        /// Clear all cached replacement data. Call when config changes.
        /// </summary>
        public static void ClearReplacementCache()
        {
            // Clear any cached replacement data here
            // This would be implemented when replacement functions are added
        }

        // =============================================================================
        // INITIALIZATION
        // =============================================================================

        /// <summary>
        /// Initialize global state from configuration
        /// </summary>
        public static void Initialize()
        {
            var config = ConfigManager.GetConfig();
            
            // Initialize network configuration
            UseWebserver = config["use_webserver"].ToLower() == "true";
            CsRegion = config["override_ip_country_region"];
            CellId = int.Parse(config["cellid"]);
            DirIsMaster = config["dir_ismaster"];
            
            // Initialize IP addresses
            ServerIp = config["server_ip"];
            PublicIp = config["public_ip"];
            
            try
            {
                ServerIpBytes = IPAddress.Parse(ServerIp).GetAddressBytes();
                PublicIpBytes = IPAddress.Parse(PublicIp).GetAddressBytes();
            }
            catch
            {
                // Handle invalid IP addresses
                ServerIpBytes = new byte[] { 0, 0, 0, 0 };
                PublicIpBytes = new byte[] { 0, 0, 0, 0 };
            }

            // Initialize email configuration
            SmtpEnable = config["smtp_enabled"];
            ForceEmailVerification = config["force_email_verification"];
        }
    }
}
