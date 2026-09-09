using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam VTT/Cafe protocol service.  The protocol uses 26-byte command records,
    /// including the historical short records sent by early cafe clients.
    /// </summary>
    public class VTTServer : ServerBase
    {
        protected VTTServer(int port, Dictionary<string, string> config, string name)
            : base(port, config, name)
        {
            logger.LogFunctionEntry("VTTServer.ctor", port, name);
            logger.LogFunctionExit("VTTServer.ctor");
        }

        public VTTServer(string port, Dictionary<string, string> config)
            : this(ParsePort(port), config, "VTTServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            string clientId = DescribeClient(client);
            int errorCount = 0;
            logger.LogFunctionEntry("VTTServer.HandleClient", clientId);
            try
            {
                logger.Info("{0} connected to VTT server", clientId);
                while (true)
                {
                    byte[] command = ReceiveAvailableCommand(client);
                    if (command.Length == 0)
                    {
                        logger.Info("{0} ended VTT session", clientId);
                        break;
                    }

                    logger.LogPacket(LogLevel.Debug, "RECV", command, clientId + " VTT command");
                    byte[] response = ProcessVttCommand(command, clientId);
                    if (response != null)
                    {
                        SendData(client, response);
                    }

                    if (response == null && !IsKnownNoReplyCommand(command))
                    {
                        errorCount++;
                        logger.Warning("{0} unknown VTT command ({1}/6)", clientId, errorCount);
                        if (errorCount > 5)
                        {
                            logger.Warning("{0} closing VTT session after repeated unknown commands", clientId);
                            break;
                        }
                    }
                    else
                    {
                        errorCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogException("VTTServer.HandleClient", ex, clientId);
            }
            finally
            {
                CloseClient(client);
                logger.Info("{0} disconnected from VTT server", clientId);
                logger.LogFunctionExit("VTTServer.HandleClient");
            }
        }

        private byte[] ReceiveAvailableCommand(TcpClient client)
        {
            logger.LogFunctionEntry("VTTServer.ReceiveAvailableCommand", DescribeClient(client));
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[26];
                int total = 0;
                stream.ReadTimeout = 30000;
                while (total < buffer.Length)
                {
                    int read = stream.Read(buffer, total, buffer.Length - total);
                    if (read == 0)
                    {
                        break;
                    }
                    total += read;
                    if (!stream.DataAvailable)
                    {
                        break;
                    }
                }
                byte[] command = new byte[total];
                Array.Copy(buffer, command, total);
                return command;
            }
            finally
            {
                logger.LogFunctionExit("VTTServer.ReceiveAvailableCommand");
            }
        }

        private byte[] ProcessVttCommand(byte[] command, string clientId)
        {
            logger.LogFunctionEntry("VTTServer.ProcessVttCommand", command.Length);
            try
            {
                if (StartsWith(command, "SENDMAC "))
                {
                    string macAddress = Encoding.ASCII.GetString(command, 9, Math.Max(0, command.Length - 9)).Trim('\0', ' ', '\r', '\n');
                    logger.Info("{0} SENDMAC: {1}", clientId, macAddress);
                    return IsMacAllowed(macAddress) ? new byte[] { 0x01, 0x00, 0x00, 0x00 } : new byte[] { 0xFD, 0xFF, 0xFF, 0xFF };
                }
                if (StartsWith(command, "SETMAC"))
                {
                    logger.Info("{0} SETMAC", clientId);
                    return new byte[] { 0x01, 0x00, 0x00, 0x00 };
                }
                if (StartsWith(command, new byte[] { 0x00, 0x00, 0xFF, 0xFF }))
                {
                    logger.Info("{0} VTT response confirmation", clientId);
                    return new byte[] { 0x4F, 0x4B, 0x00, 0x00 };
                }
                if (StartsWith(command, "CHALLENGE"))
                {
                    logger.Info("{0} CHALLENGE", clientId);
                    return new byte[] { 0xFF, 0xFF, 0x00, 0x00 };
                }
                if (BytesEqual(command, Encoding.ASCII.GetBytes("GETSRVSUM")))
                {
                    logger.Info("{0} GETSRVSUM", clientId);
                    return ToBigEndianUInt32(14);
                }
                if ((command.Length >= 12 && StartsWith(command, 5, "GETINFO")) || StartsWith(command, "GETINFO "))
                {
                    logger.Info("{0} GETINFO", clientId);
                    string credentials = GetConfigValue("cafeuser") + "%" + GetConfigValue("cafepass");
                    byte[] encrypted = Encoding.UTF8.GetBytes(TextXor(credentials));
                    return PrefixLittleEndianLength(encrypted);
                }
                if (StartsWith(command, "SENDMINS") || (command.Length >= 13 && StartsWith(command, 5, "SENDMINS")))
                {
                    uint minutes;
                    if (!UInt32.TryParse(GetConfigValue("cafetime"), out minutes))
                    {
                        minutes = 0;
                    }
                    logger.Info("{0} SENDMINS -> {1}", clientId, minutes);
                    return BitConverter.GetBytes(minutes);
                }
                if (StartsWith(command, "PING    "))
                {
                    logger.Debug("{0} VTT ping", clientId);
                    return null;
                }
                if (command.Length == 5)
                {
                    logger.Warning("{0} VTT client login failed packet", clientId);
                    return null;
                }
                logger.LogPacket(LogLevel.Warning, "UNKNOWN", command, clientId + " VTT command");
                return null;
            }
            finally
            {
                logger.LogFunctionExit("VTTServer.ProcessVttCommand");
            }
        }

        private bool IsMacAllowed(string macAddress)
        {
            logger.LogFunctionEntry("VTTServer.IsMacAllowed", macAddress);
            try
            {
                if (!String.Equals(GetConfigValue("cafe_use_mac_auth"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                string[] configuredMacs = GetConfigValue("cafemacs").Split(';');
                for (int i = 0; i < configuredMacs.Length; i++)
                {
                    if (String.Equals(configuredMacs[i].Trim(), macAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                logger.LogFunctionExit("VTTServer.IsMacAllowed");
            }
        }

        private string GetConfigValue(string key)
        {
            string value;
            return config != null && config.TryGetValue(key, out value) ? value : String.Empty;
        }

        private static int ParsePort(string value)
        {
            int result;
            if (!Int32.TryParse(value, out result) || result < IPEndPoint.MinPort || result > IPEndPoint.MaxPort)
            {
                throw new ArgumentException("Invalid port", "value");
            }
            return result;
        }

        private static bool StartsWith(byte[] source, string text)
        {
            return StartsWith(source, 0, text);
        }

        private static bool StartsWith(byte[] source, int offset, string text)
        {
            return StartsWith(source, offset, Encoding.ASCII.GetBytes(text));
        }

        private static bool StartsWith(byte[] source, byte[] prefix)
        {
            return StartsWith(source, 0, prefix);
        }

        private static bool StartsWith(byte[] source, int offset, byte[] prefix)
        {
            if (source == null || prefix == null || offset < 0 || source.Length - offset < prefix.Length)
            {
                return false;
            }
            for (int i = 0; i < prefix.Length; i++)
            {
                if (source[offset + i] != prefix[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsKnownNoReplyCommand(byte[] command)
        {
            return command.Length == 5 || StartsWith(command, "PING    ");
        }

        private static string TextXor(string value)
        {
            const string key = "@#$%^&*(}]{;:<>?*&^+_-=";
            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                result.Append((char)(value[i] ^ key[i % key.Length]));
            }
            return result.ToString();
        }

        private static byte[] PrefixLittleEndianLength(byte[] body)
        {
            byte[] response = new byte[4 + body.Length];
            Array.Copy(BitConverter.GetBytes((uint)body.Length), response, 4);
            Array.Copy(body, 0, response, 4, body.Length);
            return response;
        }

        private static byte[] ToBigEndianUInt32(uint value)
        {
            return new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }
    }
}
