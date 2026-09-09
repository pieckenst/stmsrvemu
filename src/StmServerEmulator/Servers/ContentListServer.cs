using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Logging;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>Steam2 Content Server Directory Service (CSDS) protocol.</summary>
    public sealed class ContentListServer : ServerBase
    {
        public ContentListServer(int port, Dictionary<string, string> config) : base(port, config, "ContentListServer") { }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            string clientId = DescribeClient(client);
            logger.LogFunctionEntry("ContentListServer.HandleClient", clientId);
            try
            {
                byte[] handshake = ReceiveData(client, 4, 30000);
                if (BytesEqual(handshake, new byte[] { 0, 0x4F, 0x8C, 0x12 }))
                {
                    logger.Info("{0} requested CSDS peer list", clientId);
                    SendData(client, new byte[] { 0, 0 });
                    return;
                }
                if (!BytesEqual(handshake, new byte[] { 0, 0, 0, 2 }))
                {
                    logger.Warning("{0} invalid CSDS handshake {1}", clientId, BitConverter.ToString(handshake));
                    return;
                }
                SendData(client, new byte[] { 0x01 });
                byte[] request = ReceiveLengthPrefixedPacket(client);
                byte[] response;
                if (request.Length > 0 && request[0] == 0x03)
                {
                    response = BuildClientUpdateList(IsClientOnLan((IPEndPoint)client.Client.RemoteEndPoint));
                }
                else if (request.Length >= 21 && request[0] == 0x00)
                {
                    response = BuildContentServerList(request, IsClientOnLan((IPEndPoint)client.Client.RemoteEndPoint), clientId);
                }
                else
                {
                    logger.Warning("{0} invalid CSDS request", clientId);
                    response = new byte[] { 0 };
                }
                SendLengthPrefixedPacket(client, response);
            }
            catch (Exception ex)
            {
                logger.LogException("ContentListServer.HandleClient", ex, clientId);
            }
            finally
            {
                CloseClient(client);
                logger.LogFunctionExit("ContentListServer.HandleClient");
            }
        }

        private byte[] BuildContentServerList(byte[] request, bool isLan, string clientId)
        {
            logger.LogFunctionEntry("ContentListServer.BuildContentServerList", request.Length, isLan);
            try
            {
                // Python get_general_server_list parses this 20-byte body as >HIIHII.
                ushort specificContent = ReadBigEndianUInt16(request, 1);
                uint appId = PacketHelper.ReadBigEndianUInt32(request, 3);
                uint appVersion = PacketHelper.ReadBigEndianUInt32(request, 7);
                ushort maximumAddresses = ReadBigEndianUInt16(request, 11);
                uint cellId = PacketHelper.ReadBigEndianUInt32(request, 13);
                uint unknown = PacketHelper.ReadBigEndianUInt32(request, 17);
                logger.Info("{0} CSDS list: specific={1}, app={2}, version={3}, max={4}, cell={5}, unknown={6}", clientId, specificContent, appId, appVersion, maximumAddresses, cellId, unknown);
                if (specificContent > 1)
                {
                    return new byte[] { 0 };
                }
                uint advertisedCell;
                if (!UInt32.TryParse(GetConfigValue("cellid"), out advertisedCell)) advertisedCell = 1;
                string ip = SelectAddress(isLan);
                int clupdPort = GetPort("clupd_server_port");
                int contentPort = GetPort("content_server_port");
                List<byte> response = new List<byte>();
                ushort count = maximumAddresses == 0 ? (ushort)0 : (ushort)1;
                response.Add((byte)(count >> 8)); response.Add((byte)count);
                response.AddRange(ToBigEndianUInt32(advertisedCell));
                if (count != 0)
                {
                    response.AddRange(EncodeEndpoint(ip, clupdPort));
                    response.AddRange(EncodeEndpoint(ip, contentPort));
                }
                return response.ToArray();
            }
            finally
            {
                logger.LogFunctionExit("ContentListServer.BuildContentServerList");
            }
        }

        private byte[] BuildClientUpdateList(bool isLan)
        {
            logger.LogFunctionEntry("ContentListServer.BuildClientUpdateList", isLan);
            try
            {
                List<byte> response = new List<byte>();
                response.Add(0); response.Add(1);
                response.AddRange(EncodeEndpoint(SelectAddress(isLan), GetPort("clupd_server_port")));
                return response.ToArray();
            }
            finally
            {
                logger.LogFunctionExit("ContentListServer.BuildClientUpdateList");
            }
        }

        private byte[] ReceiveLengthPrefixedPacket(TcpClient client)
        {
            byte[] lengthBytes = ReceiveData(client, 4, 30000);
            uint length = PacketHelper.ReadBigEndianUInt32(lengthBytes, 0);
            if (length > 1048576) throw new InvalidDataException("CSDS request exceeds 1 MiB");
            return ReceiveData(client, (int)length, 30000);
        }
        private void SendLengthPrefixedPacket(TcpClient client, byte[] data)
        {
            byte[] response = new byte[4 + data.Length];
            Array.Copy(ToBigEndianUInt32((uint)data.Length), response, 4);
            Array.Copy(data, 0, response, 4, data.Length);
            SendData(client, response);
        }
        private bool IsClientOnLan(IPEndPoint client)
        {
            IPAddress local; IPAddress mask;
            if (!IPAddress.TryParse(GetConfigValue("server_ip"), out local) || !IPAddress.TryParse(GetConfigValue("server_sm"), out mask)) return false;
            byte[] a = client.Address.GetAddressBytes(); byte[] b = local.GetAddressBytes(); byte[] m = mask.GetAddressBytes();
            if (a.Length != 4 || b.Length != 4 || m.Length != 4) return false;
            for (int i = 0; i < 4; i++) if ((a[i] & m[i]) != (b[i] & m[i])) return false;
            return true;
        }
        private string SelectAddress(bool isLan) { string ip = isLan ? GetConfigValue("server_ip") : GetConfigValue("public_ip"); return ip == "0.0.0.0" || String.IsNullOrEmpty(ip) ? GetConfigValue("server_ip") : ip; }
        private int GetPort(string key) { int value; return Int32.TryParse(GetConfigValue(key), out value) ? value : 0; }
        private string GetConfigValue(string key) { string value; return config.TryGetValue(key, out value) ? value : String.Empty; }
        private static ushort ReadBigEndianUInt16(byte[] data, int offset) { if (data == null || offset < 0 || data.Length - offset < 2) throw new InvalidDataException("CSDS request ended in a short field"); return (ushort)((data[offset] << 8) | data[offset + 1]); }
        private static byte[] ToBigEndianUInt32(uint value) { return new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }; }
        private static byte[] EncodeEndpoint(string ip, int port) { IPAddress address; if (!IPAddress.TryParse(ip, out address)) throw new InvalidDataException("Invalid configured CSDS address"); byte[] result = new byte[6]; Array.Copy(address.GetAddressBytes(), result, 4); result[4] = (byte)(port >> 8); result[5] = (byte)port; return result; }
        private static bool BytesEqual(byte[] left, byte[] right) { if (left == null || right == null || left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false; return true; }
    }
}
