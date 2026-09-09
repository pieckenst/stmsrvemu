using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;

namespace StmServerEmulator.Networking
{
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
    }

    /// <summary>
    /// Network helper functions for the Steam Server Emulator
    /// Handles IP address encoding/decoding and network operations
    /// </summary>
    public class NetworkHelper
    {
        /// <summary>
        /// Decode IP address and port from packed format
        /// </summary>
        public static IpPortResult DecodeIpPort(byte[] data)
        {
            if (data.Length < 6)
            {
                throw new ArgumentException("Data must be at least 6 bytes");
            }

            byte oct1 = data[0];
            byte oct2 = data[1];
            byte oct3 = data[2];
            byte oct4 = data[3];
            
            ushort port = (ushort)(data[4] | (data[5] << 8));
            
            string ip = string.Format("{0}.{1}.{2}.{3}", oct1, oct2, oct3, oct4);
            return new IpPortResult(ip, port);
        }

        /// <summary>
        /// Encode IP address and port to packed format (little-endian)
        /// </summary>
        public static byte[] EncodeIpPort(string ip, int port)
        {
            string[] octets = ip.Split('.');
            if (octets.Length != 4)
            {
                throw new ArgumentException("Invalid IP address format");
            }

            byte[] result = new byte[6];
            result[0] = byte.Parse(octets[0]);
            result[1] = byte.Parse(octets[1]);
            result[2] = byte.Parse(octets[2]);
            result[3] = byte.Parse(octets[3]);
            
            result[4] = (byte)(port & 0xFF);
            result[5] = (byte)((port >> 8) & 0xFF);
            
            return result;
        }

        /// <summary>
        /// Encode IP address and port to packed format (big-endian)
        /// </summary>
        public static byte[] EncodeIpPortBigEndian(string ip, int port)
        {
            string[] octets = ip.Split('.');
            if (octets.Length != 4)
            {
                throw new ArgumentException("Invalid IP address format");
            }

            byte[] result = new byte[6];
            result[0] = byte.Parse(octets[0]);
            result[1] = byte.Parse(octets[1]);
            result[2] = byte.Parse(octets[2]);
            result[3] = byte.Parse(octets[3]);
            
            result[4] = (byte)((port >> 8) & 0xFF);
            result[5] = (byte)(port & 0xFF);
            
            return result;
        }

        /// <summary>
        /// Convert IP address to integer
        /// </summary>
        public static uint IpToUint(string ipAddress)
        {
            IPAddress ip = IPAddress.Parse(ipAddress);
            byte[] bytes = ip.GetAddressBytes();
            
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            
            return BitConverter.ToUInt32(bytes, 0);
        }

        /// <summary>
        /// Convert IP address to reversed byte order DWORD
        /// </summary>
        public static uint ReverseIpBytes(string ipAddress)
        {
            string[] octets = ipAddress.Split('.');
            if (octets.Length != 4)
            {
                throw new ArgumentException("Invalid IP address format");
            }

            string reversedIp = string.Format("{0}.{1}.{2}.{3}", 
                octets[3], octets[2], octets[1], octets[0]);
            
            return IpToUint(reversedIp);
        }

        /// <summary>
        /// Convert integer to IP address
        /// </summary>
        public static string UintToIp(uint ipUint)
        {
            byte[] bytes = BitConverter.GetBytes(ipUint);
            
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            
            return new IPAddress(bytes).ToString();
        }

        /// <summary>
        /// Validate IP address format
        /// </summary>
        public static bool IsValidIpAddress(string ipAddress)
        {
            IPAddress dummy;
            return IPAddress.TryParse(ipAddress, out dummy);
        }

        /// <summary>
        /// Get local IP address
        /// </summary>
        public static string GetLocalIpAddress()
        {
            try
            {
                IPAddress localIp = null;
                
                // Try to get a non-loopback IPv4 address
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && 
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
        /// Check if a port is available
        /// </summary>
        public static bool IsPortAvailable(int port)
        {
            try
            {
                using (TcpListener listener = new TcpListener(IPAddress.Any, port))
                {
                    listener.Start();
                    listener.Stop();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create a TCP client with timeout
        /// </summary>
        public static TcpClient CreateTcpClient(string host, int port, int timeoutMs)
        {
            TcpClient client = new TcpClient();
            
            IAsyncResult result = client.BeginConnect(host, port, null, null);
            
            if (result.AsyncWaitHandle.WaitOne(timeoutMs, false))
            {
                client.EndConnect(result);
                return client;
            }
            else
            {
                client.Close();
                throw new TimeoutException("Connection timeout");
            }
        }

        /// <summary>
        /// Read data from stream with timeout
        /// </summary>
        public static byte[] ReadData(NetworkStream stream, int length, int timeoutMs)
        {
            byte[] buffer = new byte[length];
            int bytesRead = 0;
            int startTime = Environment.TickCount;

            while (bytesRead < length)
            {
                if (Environment.TickCount - startTime > timeoutMs)
                {
                    throw new TimeoutException("Read timeout");
                }

                int read = stream.Read(buffer, bytesRead, length - bytesRead);
                if (read == 0)
                {
                    throw new IOException("Connection closed");
                }
                bytesRead += read;
            }

            return buffer;
        }

        /// <summary>
        /// Read data from stream until delimiter is found
        /// </summary>
        public static byte[] ReadUntilDelimiter(NetworkStream stream, byte[] delimiter, int maxBytes, int timeoutMs)
        {
            MemoryStream ms = new MemoryStream();
            byte[] buffer = new byte[1];
            int matchPosition = 0;
            int startTime = Environment.TickCount;

            while (true)
            {
                if (Environment.TickCount - startTime > timeoutMs)
                {
                    throw new TimeoutException("Read timeout");
                }

                if (ms.Length >= maxBytes)
                {
                    throw new IOException("Maximum bytes read exceeded");
                }

                int read = stream.Read(buffer, 0, 1);
                if (read == 0)
                {
                    throw new IOException("Connection closed");
                }

                ms.Write(buffer, 0, 1);
                ms.Position = ms.Length - 1;
                int currentByte = ms.ReadByte();
                ms.Position = ms.Length;

                if (currentByte == delimiter[matchPosition])
                {
                    matchPosition++;
                    if (matchPosition == delimiter.Length)
                    {
                        return ms.ToArray();
                    }
                }
                else
                {
                    matchPosition = 0;
                }
            }
        }
    }
}
