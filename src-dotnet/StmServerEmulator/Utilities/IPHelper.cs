using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace StmServerEmulator.Utilities
{
    /// <summary>
    /// IP address helper functions for the Steam Server Emulator
    /// Handles IP validation, comparison, and Steam-specific IP operations
    /// </summary>
    public class IPHelper
    {
        private static readonly Regex IpRegex = new Regex(@"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$");

        /// <summary>
        /// Validate an IP address string
        /// </summary>
        public static bool IsValidIpAddress(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                return false;
            }

            Match match = IpRegex.Match(ipAddress);
            if (!match.Success)
            {
                return false;
            }

            for (int i = 1; i <= 4; i++)
            {
                int octet = int.Parse(match.Groups[i].Value);
                if (octet < 0 || octet > 255)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if an IP address is in a specific range
        /// </summary>
        public static bool IsIpInRange(string ipAddress, string startIp, string endIp)
        {
            if (!IsValidIpAddress(ipAddress) || !IsValidIpAddress(startIp) || !IsValidIpAddress(endIp))
            {
                return false;
            }

            uint ip = IpToUint(ipAddress);
            uint start = IpToUint(startIp);
            uint end = IpToUint(endIp);

            return ip >= start && ip <= end;
        }

        /// <summary>
        /// Check if an IP address is a loopback address
        /// </summary>
        public static bool IsLoopback(string ipAddress)
        {
            if (!IsValidIpAddress(ipAddress))
            {
                return false;
            }

            return ipAddress.StartsWith("127.") || ipAddress == "::1";
        }

        /// <summary>
        /// Check if an IP address is private
        /// </summary>
        public static bool IsPrivateIpAddress(string ipAddress)
        {
            if (!IsValidIpAddress(ipAddress))
            {
                return false;
            }

            // 10.0.0.0/8
            if (ipAddress.StartsWith("10."))
            {
                return true;
            }

            // 172.16.0.0/12
            if (ipAddress.StartsWith("172."))
            {
                string[] parts = ipAddress.Split('.');
                int secondOctet = int.Parse(parts[1]);
                if (secondOctet >= 16 && secondOctet <= 31)
                {
                    return true;
                }
            }

            // 192.168.0.0/16
            if (ipAddress.StartsWith("192.168."))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Convert IP address to uint
        /// </summary>
        public static uint IpToUint(string ipAddress)
        {
            if (!IsValidIpAddress(ipAddress))
            {
                throw new ArgumentException("Invalid IP address");
            }

            string[] octets = ipAddress.Split('.');
            uint result = 0;
            
            for (int i = 0; i < 4; i++)
            {
                result = (result << 8) | uint.Parse(octets[i]);
            }

            return result;
        }

        /// <summary>
        /// Convert uint to IP address
        /// </summary>
        public static string UintToIp(uint ipUint)
        {
            byte[] bytes = new byte[4];
            
            for (int i = 0; i < 4; i++)
            {
                bytes[3 - i] = (byte)((ipUint >> (i * 8)) & 0xFF);
            }

            return string.Format("{0}.{1}.{2}.{3}", bytes[0], bytes[1], bytes[2], bytes[3]);
        }

        /// <summary>
        /// Get the subnet mask for a given CIDR notation
        /// </summary>
        public static string GetSubnetMask(int cidr)
        {
            if (cidr < 0 || cidr > 32)
            {
                throw new ArgumentException("CIDR must be between 0 and 32");
            }

            uint mask = cidr == 0 ? 0 : 0xFFFFFFFF << (32 - cidr);
            return UintToIp(mask);
        }

        /// <summary>
        /// Check if two IP addresses are in the same subnet
        /// </summary>
        public static bool AreInSameSubnet(string ip1, string ip2, string subnetMask)
        {
            if (!IsValidIpAddress(ip1) || !IsValidIpAddress(ip2) || !IsValidIpAddress(subnetMask))
            {
                return false;
            }

            uint uintIp1 = IpToUint(ip1);
            uint uintIp2 = IpToUint(ip2);
            uint uintMask = IpToUint(subnetMask);

            return (uintIp1 & uintMask) == (uintIp2 & uintMask);
        }

        /// <summary>
        /// Parse an IP:port string
        /// </summary>
        public static IpPortResult ParseIpPort(string ipPortString)
        {
            if (string.IsNullOrEmpty(ipPortString))
            {
                throw new ArgumentException("IP:Port string cannot be null or empty");
            }

            string[] parts = ipPortString.Split(':');
            if (parts.Length != 2)
            {
                throw new ArgumentException("Invalid IP:Port format");
            }

            string ip = parts[0];
            int port;

            if (!IsValidIpAddress(ip) || !int.TryParse(parts[1], out port))
            {
                throw new ArgumentException("Invalid IP or port");
            }

            return new IpPortResult(ip, port);
        }

        /// <summary>
        /// Format an IP:port string
        /// </summary>
        public static string FormatIpPort(string ip, int port)
        {
            if (!IsValidIpAddress(ip))
            {
                throw new ArgumentException("Invalid IP address");
            }

            return string.Format("{0}:{1}", ip, port);
        }

        /// <summary>
        /// Get the local IP address
        /// </summary>
        public static string GetLocalIpAddress()
        {
            try
            {
                IPAddress localIp = null;
                
                // Try to get a non-loopback IPv4 address
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && 
                        !IPAddress.IsLoopback(ip))
                    {
                        localIp = ip;
                        break;
                    }
                }

                // Fallback to loopback if no other address found
                if (localIp == null)
                {
                    localIp = IPAddress.Loopback;
                }

                return localIp.ToString();
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        /// <summary>
        /// Get the public IP address (external IP)
        /// </summary>
        public static string GetPublicIpAddress()
        {
            // In a real implementation, this would call an external service
            // For now, return the local IP as a fallback
            return GetLocalIpAddress();
        }
    }

    /// <summary>
    /// Result class for IP address and port pairs
    /// </summary>
    public class IpPortResult
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }

        public IpPortResult(string ipAddress, int port)
        {
            IpAddress = ipAddress;
            Port = port;
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}", IpAddress, Port);
        }
    }
}
