using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Encryption;
using StmServerEmulator.Logging;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>Legacy package/CDR distribution protocol used by Steam 2003-2007 clients.</summary>
    public sealed class ClientUpdateServer : ServerBase
    {
        private const int MaximumPacketSize = 67108864;

        public ClientUpdateServer(int port, Dictionary<string, string> config)
            : base(port, config, "ClientUpdateServer")
        {
            logger.LogFunctionEntry("ClientUpdateServer.ctor", port);
            logger.LogFunctionExit("ClientUpdateServer.ctor");
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            string clientId = DescribeClient(client);
            logger.LogFunctionEntry("ClientUpdateServer.HandleClient", clientId);
            try
            {
                byte[] handshake = ReceiveData(client, 4, 30000);
                logger.LogPacket(LogLevel.Debug, "RECV", handshake, clientId + " ClientUpdate handshake");
                if (BytesEqual(handshake, new byte[] { 0, 0, 0, 0 }) || BytesEqual(handshake, new byte[] { 0, 0, 0, 2 }) || BytesEqual(handshake, new byte[] { 0, 0, 0, 3 }))
                {
                    HandlePackageMode(client, clientId);
                }
                else if (BytesEqual(handshake, new byte[] { 0, 0, 0, 7 }))
                {
                    HandleCdrMode(client, clientId, IsClientOnLan((IPEndPoint)client.Client.RemoteEndPoint));
                }
                else
                {
                    logger.Warning("{0} invalid client-update handshake {1}", clientId, BitConverter.ToString(handshake));
                }
            }
            catch (Exception ex)
            {
                logger.LogException("ClientUpdateServer.HandleClient", ex, clientId);
            }
            finally
            {
                CloseClient(client);
                logger.LogFunctionExit("ClientUpdateServer.HandleClient");
            }
        }

        private void HandlePackageMode(TcpClient client, string clientId)
        {
            logger.LogFunctionEntry("ClientUpdateServer.HandlePackageMode", clientId);
            try
            {
                SendData(client, new byte[] { 0x01 });
                while (client.Connected)
                {
                    byte[] request = ReceiveLengthPrefixedPacket(client);
                    if (request.Length < 4)
                    {
                        throw new InvalidDataException("Client update package request is shorter than its command field");
                    }
                    uint command = PacketHelper.ReadBigEndianUInt32(request, 0);
                    logger.Info("{0} client-update package command {1}", clientId, command);
                    if (command == 2)
                    {
                        uint cellId;
                        if (!UInt32.TryParse(GetConfigValue("cellid"), out cellId)) cellId = 1;
                        SendData(client, ToBigEndianUInt32(cellId));
                        break;
                    }
                    if (command == 3)
                    {
                        logger.Info("{0} closed client-update package mode", clientId);
                        break;
                    }
                    if (command != 0)
                    {
                        logger.Warning("{0} unsupported client-update package command {1}", clientId, command);
                        continue;
                    }
                    string filename = ParsePackageFilename(request);
                    SendRequestedPackage(client, filename, clientId);
                }
            }
            finally
            {
                logger.LogFunctionExit("ClientUpdateServer.HandlePackageMode");
            }
        }

        private void HandleCdrMode(TcpClient client, string clientId, bool isLan)
        {
            logger.LogFunctionEntry("ClientUpdateServer.HandleCdrMode", clientId, isLan);
            try
            {
                SendData(client, new byte[] { 0x01 });
                while (client.Connected)
                {
                    byte[] request = ReceiveLengthPrefixedPacket(client);
                    if (request.Length == 10)
                    {
                        SendData(client, new byte[] { 0x01 });
                        return;
                    }
                    if (request.Length < 1 || request[0] != 0x02)
                    {
                        logger.Warning("{0} unknown CDR command", clientId);
                        continue;
                    }
                    byte[] blob = GetCdrBlob(isLan);
                    byte[] checksum = EncryptionHelper.CalculateSha1(blob);
                    if (BytesEqual(checksum, CopyRange(request, 1, request.Length - 1)))
                    {
                        logger.Info("{0} client already has matching CDR", clientId);
                        SendData(client, new byte[] { 0, 0, 0, 0 });
                    }
                    else
                    {
                        logger.Info("{0} sending {1}-byte CDR", clientId, blob.Length);
                        SendLengthPrefixedPacket(client, blob);
                    }
                }
            }
            finally
            {
                logger.LogFunctionExit("ClientUpdateServer.HandleCdrMode");
            }
        }

        private void SendRequestedPackage(TcpClient client, string filename, string clientId)
        {
            logger.LogFunctionEntry("ClientUpdateServer.SendRequestedPackage", filename);
            try
            {
                bool requestSignature = filename.EndsWith("_rsa_signature", StringComparison.OrdinalIgnoreCase);
                string packageName = requestSignature ? filename.Substring(0, filename.Length - "_rsa_signature".Length) : filename;
                string packagePath = ResolvePackagePath(packageName);
                byte[] package = File.ReadAllBytes(packagePath);
                if (requestSignature)
                {
                    if (RSAKeyManager.NetworkKey == null)
                    {
                        throw new InvalidOperationException("Network key has not been initialized for package signature response");
                    }
                    byte[] signature = RSAKeyManager.RsaSignMessage(RSAKeyManager.NetworkKey, package);
                    byte[] response = new byte[8 + signature.Length];
                    Array.Copy(ToBigEndianUInt32((uint)signature.Length), 0, response, 0, 4);
                    Array.Copy(ToBigEndianUInt32((uint)signature.Length), 0, response, 4, 4);
                    Array.Copy(signature, 0, response, 8, signature.Length);
                    SendData(client, response);
                    logger.Info("{0} sent RSA signature for {1}", clientId, packageName);
                    return;
                }

                byte[] header = new byte[8];
                Array.Copy(ToBigEndianUInt32((uint)package.Length), 0, header, 0, 4);
                Array.Copy(ToBigEndianUInt32((uint)package.Length), 0, header, 4, 4);
                SendData(client, header);
                for (int offset = 0; offset < package.Length; offset += 0x500)
                {
                    int count = Math.Min(0x500, package.Length - offset);
                    byte[] chunk = new byte[count];
                    Array.Copy(package, offset, chunk, 0, count);
                    SendData(client, chunk);
                }
                logger.Info("{0} sent package {1} ({2} bytes)", clientId, packageName, package.Length);
            }
            finally
            {
                logger.LogFunctionExit("ClientUpdateServer.SendRequestedPackage");
            }
        }

        private string ResolvePackagePath(string filename)
        {
            logger.LogFunctionEntry("ClientUpdateServer.ResolvePackagePath", filename);
            try
            {
                if (String.IsNullOrEmpty(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || filename.IndexOf("..", StringComparison.Ordinal) >= 0 || filename.IndexOf(Path.DirectorySeparatorChar) >= 0 || filename.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
                {
                    throw new InvalidDataException("Rejected unsafe client-update package filename");
                }
                string root = Path.GetFullPath(GetConfigValue("packagedir"));
                string candidate = Path.GetFullPath(Path.Combine(root, filename));
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
                {
                    throw new FileNotFoundException("Requested Steam package is unavailable", filename);
                }
                return candidate;
            }
            finally
            {
                logger.LogFunctionExit("ClientUpdateServer.ResolvePackagePath");
            }
        }

        private string ParsePackageFilename(byte[] request)
        {
            logger.LogFunctionEntry("ClientUpdateServer.ParsePackageFilename", request.Length);
            try
            {
                int offset = 4;
                while (offset < request.Length && request[offset] == 0) offset++;
                if (offset >= request.Length) throw new InvalidDataException("Client-update package request does not contain a filename");
                int length = request[offset++];
                if (length == 0 || request.Length - offset < length) throw new InvalidDataException("Client-update package filename length exceeds its request");
                return System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(request, offset, length);
            }
            finally
            {
                logger.LogFunctionExit("ClientUpdateServer.ParsePackageFilename");
            }
        }

        private byte[] ReceiveLengthPrefixedPacket(TcpClient client)
        {
            byte[] lengthBytes = ReceiveData(client, 4, 30000);
            uint length = PacketHelper.ReadBigEndianUInt32(lengthBytes, 0);
            if (length > MaximumPacketSize) throw new InvalidDataException("Client-update packet exceeds 64 MiB");
            return ReceiveData(client, (int)length, 30000);
        }

        private void SendLengthPrefixedPacket(TcpClient client, byte[] data)
        {
            byte[] response = new byte[4 + data.Length];
            Array.Copy(ToBigEndianUInt32((uint)data.Length), response, 4);
            Array.Copy(data, 0, response, 4, data.Length);
            SendData(client, response);
        }

        private byte[] GetCdrBlob(bool isLan)
        {
            byte[] blob = isLan ? GlobalState.CdrBlobLan : GlobalState.CdrBlobWan;
            if (blob == null || blob.Length == 0) throw new FileNotFoundException("The requested CDR is not loaded into the emulator cache");
            return blob;
        }

        private bool IsClientOnLan(IPEndPoint client)
        {
            logger.LogFunctionEntry("ClientUpdateServer.IsClientOnLan", client.ToString());
            try
            {
                IPAddress serverAddress;
                IPAddress subnetMask;
                if (!IPAddress.TryParse(GetConfigValue("server_ip"), out serverAddress) || !IPAddress.TryParse(GetConfigValue("server_sm"), out subnetMask))
                {
                    return false;
                }
                byte[] address = client.Address.GetAddressBytes();
                byte[] server = serverAddress.GetAddressBytes();
                byte[] mask = subnetMask.GetAddressBytes();
                if (address.Length != 4 || server.Length != 4 || mask.Length != 4)
                {
                    return false;
                }
                for (int i = 0; i < 4; i++)
                {
                    if ((address[i] & mask[i]) != (server[i] & mask[i])) return false;
                }
                return true;
            }
            finally
            {
                logger.LogFunctionExit("ClientUpdateServer.IsClientOnLan");
            }
        }

        private string GetConfigValue(string key) { string value; return config.TryGetValue(key, out value) ? value : String.Empty; }
        private static byte[] ToBigEndianUInt32(uint value) { return new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }; }
        private static bool BytesEqual(byte[] left, byte[] right) { if (left == null || right == null || left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false; return true; }
        private static byte[] CopyRange(byte[] data, int offset, int length) { if (offset < 0 || length < 0 || data == null || data.Length - offset < length) throw new InvalidDataException("Packet field exceeds bounds"); byte[] result = new byte[length]; Array.Copy(data, offset, result, 0, length); return result; }
    }
}
