using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using StmServerEmulator.Logging;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Implements the legacy Harvest file upload handshake used by SteamUI.
    /// Payloads are streamed to disk so a malformed client cannot exhaust memory.
    /// </summary>
    public class HarvestServer : ServerBase
    {
        private const uint MinimumProtocolVersion = 1;
        private const uint MaximumProtocolVersion = 2;
        private const long MaximumUploadBytes = 536870912L;

        public HarvestServer(int port, Dictionary<string, string> config)
            : base(port, config, "HarvestServer")
        {
            logger.LogFunctionEntry("HarvestServer.ctor", port);
            logger.LogFunctionExit("HarvestServer.ctor");
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            string clientId = DescribeClient(client);
            logger.LogFunctionEntry("HarvestServer.HandleClient", clientId);
            try
            {
                logger.Info("{0} connected to Harvest server", clientId);
                uint version = ReadBigEndianUInt32(client);
                logger.Info("{0} harvest protocol version {1}", clientId, version);
                if (version < MinimumProtocolVersion || version > MaximumProtocolVersion)
                {
                    logger.Warning("{0} rejected unsupported harvest version {1}", clientId, version);
                    SendData(client, new byte[] { 0x00 });
                    return;
                }
                SendData(client, new byte[] { 0x01 });

                uint commandLength = ReadBigEndianUInt32(client);
                if (commandLength < 17 || commandLength > 1048576)
                {
                    logger.Warning("{0} rejected malformed harvest command length {1}", clientId, commandLength);
                    SendData(client, new byte[] { 0x01 });
                    return;
                }
                byte[] command = ReceiveData(client, (int)commandLength, 30000);
                HarvestRequest request = ParseUploadRequest(command, version);
                logger.Info("{0} harvest upload command={1}, size={2}, message={3}", clientId, request.CommandType, request.FileSize, request.Message);

                if (!AllowFileUpload(request))
                {
                    logger.Warning("{0} denied harvest upload", clientId);
                    SendData(client, new byte[] { 0x01 });
                    return;
                }
                SendData(client, new byte[] { 0x00 });

                string outputPath = CreateUploadPath(client);
                StreamUpload(client, outputPath, request.FileSize, clientId);
                SendData(client, new byte[] { 0x01 });
                logger.Info("{0} completed Harvest upload: {1}", clientId, outputPath);
            }
            catch (EndOfStreamException ex)
            {
                logger.LogException("HarvestServer.HandleClient", ex, clientId + " incomplete upload");
                TrySendFailure(client);
            }
            catch (Exception ex)
            {
                logger.LogException("HarvestServer.HandleClient", ex, clientId);
                TrySendFailure(client);
            }
            finally
            {
                CloseClient(client);
                logger.LogFunctionExit("HarvestServer.HandleClient");
            }
        }

        private HarvestRequest ParseUploadRequest(byte[] command, uint version)
        {
            logger.LogFunctionEntry("HarvestServer.ParseUploadRequest", command == null ? 0 : command.Length, version);
            try
            {
                if (command == null || command.Length < 17)
                {
                    throw new InvalidDataException("Harvest command is shorter than its required 17-byte header");
                }
                HarvestRequest request = new HarvestRequest();
                request.CommandType = command[0];
                request.FileSize = PacketHelper.ReadBigEndianUInt32(command, 1);
                request.Reserved1 = PacketHelper.ReadBigEndianUInt32(command, 5);
                request.Reserved2 = PacketHelper.ReadBigEndianUInt32(command, 9);
                request.Reserved3 = PacketHelper.ReadBigEndianUInt32(command, 13);
                request.Message = String.Empty;
                if (version >= 2)
                {
                    if (command.Length < 21)
                    {
                        throw new InvalidDataException("Harvest v2 command is missing its message length");
                    }
                    uint messageLength = PacketHelper.ReadBigEndianUInt32(command, 17);
                    if (messageLength > command.Length - 21)
                    {
                        throw new InvalidDataException("Harvest v2 command message exceeds packet size");
                    }
                    request.Message = Encoding.GetEncoding("ISO-8859-1").GetString(command, 21, (int)messageLength);
                }
                return request;
            }
            finally
            {
                logger.LogFunctionExit("HarvestServer.ParseUploadRequest");
            }
        }

        private bool AllowFileUpload(HarvestRequest request)
        {
            logger.LogFunctionEntry("HarvestServer.AllowFileUpload", request.CommandType, request.FileSize);
            try
            {
                string enabled;
                bool uploadEnabled = !config.TryGetValue("allow_harvest_upload", out enabled) || String.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
                bool valid = uploadEnabled && request.CommandType == 1 && request.FileSize <= MaximumUploadBytes;
                logger.Debug("Harvest upload validation: enabled={0}, command={1}, size={2}, allowed={3}", uploadEnabled, request.CommandType, request.FileSize, valid);
                return valid;
            }
            finally
            {
                logger.LogFunctionExit("HarvestServer.AllowFileUpload");
            }
        }

        private string CreateUploadPath(TcpClient client)
        {
            logger.LogFunctionEntry("HarvestServer.CreateUploadPath", DescribeClient(client));
            try
            {
                string root = Path.Combine("clientstats", "harvest_gamestats");
                Directory.CreateDirectory(root);
                string endpoint = client.Client.RemoteEndPoint.ToString().Replace(':', '_').Replace('[', '_').Replace(']', '_');
                string filename = endpoint + "_" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".cmd1.upload.bin";
                return Path.Combine(root, filename);
            }
            finally
            {
                logger.LogFunctionExit("HarvestServer.CreateUploadPath");
            }
        }

        private void StreamUpload(TcpClient client, string outputPath, uint fileSize, string clientId)
        {
            logger.LogFunctionEntry("HarvestServer.StreamUpload", outputPath, fileSize);
            try
            {
                NetworkStream input = client.GetStream();
                input.ReadTimeout = 30000;
                byte[] buffer = new byte[65536];
                uint remaining = fileSize;
                using (FileStream output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    while (remaining > 0)
                    {
                        int readSize = (int)Math.Min((uint)buffer.Length, remaining);
                        int read = input.Read(buffer, 0, readSize);
                        if (read == 0)
                        {
                            throw new EndOfStreamException("Harvest client closed during upload");
                        }
                        output.Write(buffer, 0, read);
                        remaining -= (uint)read;
                        logger.Debug("{0} Harvest bytes remaining: {1}", clientId, remaining);
                    }
                    output.Flush();
                }
                logger.LogPacket(LogLevel.Debug, "RECV-FILE", Encoding.ASCII.GetBytes(outputPath), clientId + " saved " + fileSize + " bytes");
            }
            finally
            {
                logger.LogFunctionExit("HarvestServer.StreamUpload");
            }
        }

        private uint ReadBigEndianUInt32(TcpClient client)
        {
            logger.LogFunctionEntry("HarvestServer.ReadBigEndianUInt32", DescribeClient(client));
            try
            {
                byte[] data = ReceiveData(client, 4, 30000);
                return PacketHelper.ReadBigEndianUInt32(data, 0);
            }
            finally
            {
                logger.LogFunctionExit("HarvestServer.ReadBigEndianUInt32");
            }
        }

        private void TrySendFailure(TcpClient client)
        {
            logger.LogFunctionEntry("HarvestServer.TrySendFailure");
            try
            {
                if (client != null && client.Connected)
                {
                    SendData(client, new byte[] { 0x00 });
                }
            }
            catch (Exception ex)
            {
                logger.LogException("HarvestServer.TrySendFailure", ex);
            }
            finally
            {
                logger.LogFunctionExit("HarvestServer.TrySendFailure");
            }
        }

        private sealed class HarvestRequest
        {
            public byte CommandType;
            public uint FileSize;
            public uint Reserved1;
            public uint Reserved2;
            public uint Reserved3;
            public string Message;
        }
    }
}
