using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using StmServerEmulator.Database;
using StmServerEmulator.Encryption;
using StmServerEmulator.Logging;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>Steam2 ticket validation endpoint, including the legacy 0x0c CD-key probe.</summary>
    public sealed class ValidationServer : ServerBase
    {
        private static readonly byte[] InnerKey = new byte[] { 0x10, 0x23, 0x12, 0x30, 0x21, 0x12, 0x81, 0x23, 0x91, 0x91, 0x23, 0x85, 0x42, 0x31, 0x42, 0x33 };
        private readonly AuthDatabase authDatabase;

        public ValidationServer(int port, Dictionary<string, string> config)
            : base(port, config, "ValidationServer")
        {
            logger.LogFunctionEntry("ValidationServer.ctor", port);
            authDatabase = new AuthDatabase(config);
            logger.LogFunctionExit("ValidationServer.ctor");
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            IPEndPoint endpoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string clientId = DescribeClient(client);
            logger.LogFunctionEntry("ValidationServer.HandleClient", clientId);
            try
            {
                byte[] header = ReceiveData(client, 13, 30000);
                logger.LogPacket(LogLevel.Debug, "RECV", header, clientId + " validation header");
                if (header[0] == 0x0C)
                {
                    HandleCdKeyProbe(client, endpoint, header);
                    return;
                }
                if (!IsSupportedVersion(header))
                {
                    logger.Warning("{0} unsupported validation protocol marker {1}", clientId, BitConverter.ToString(header, 1, 4));
                    return;
                }

                List<byte> greeting = new List<byte>();
                greeting.Add(0x01);
                greeting.AddRange(endpoint.Address.GetAddressBytes());
                SendData(client, greeting.ToArray());

                byte[] ticket = ReceiveLengthPrefixedPacket(client);
                string username = ExtractUsername(ticket);
                logger.Info("{0} validating ticket for {1}", clientId, username);
                uint accountId = authDatabase.GetAccountId(username);
                if (accountId == 0)
                {
                    logger.Warning("{0} ticket user does not exist: {1}", clientId, username);
                    return;
                }
                SendData(client, BuildValidationTicket(ticket, accountId));
            }
            catch (Exception ex)
            {
                logger.LogException("ValidationServer.HandleClient", ex, clientId);
            }
            finally
            {
                CloseClient(client);
                logger.LogFunctionExit("ValidationServer.HandleClient");
            }
        }

        private void HandleCdKeyProbe(TcpClient client, IPEndPoint endpoint, byte[] header)
        {
            logger.LogFunctionEntry("ValidationServer.HandleCdKeyProbe", endpoint.ToString());
            try
            {
                uint messageType = BitConverter.ToUInt32(header, 0);
                uint field6d4 = BitConverter.ToUInt32(header, 5);
                uint field6ec = BitConverter.ToUInt32(header, 9);
                logger.Info("CD-key validation probe: type={0}, field6d4={1}, field6ec={2}", messageType, field6d4, field6ec);
                List<byte> response = new List<byte>();
                response.Add(0x01); response.Add(0x00);
                response.AddRange(endpoint.Address.GetAddressBytes());
                for (int i = 0; i < 24; i++) response.Add(0xFF);
                SendData(client, response.ToArray());
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 30000;
                byte[] body = new byte[400];
                int bytesRead = stream.Read(body, 0, body.Length);
                if (bytesRead > 0)
                {
                    byte[] received = new byte[bytesRead];
                    Array.Copy(body, received, bytesRead);
                    logger.LogPacket(LogLevel.Debug, "RECV", received, "CD-key validation payload " + endpoint);
                }
            }
            finally
            {
                logger.LogFunctionExit("ValidationServer.HandleCdKeyProbe");
            }
        }

        private static bool IsSupportedVersion(byte[] header)
        {
            return header.Length >= 5 && header[1] == 0 && header[2] == 0 && header[3] == 0 && (header[4] == 1 || header[4] == 3 || header[4] == 4);
        }

        private byte[] ReceiveLengthPrefixedPacket(TcpClient client)
        {
            logger.LogFunctionEntry("ValidationServer.ReceiveLengthPrefixedPacket", DescribeClient(client));
            try
            {
                byte[] size = ReceiveData(client, 4, 30000);
                uint length = PacketHelper.ReadBigEndianUInt32(size, 0);
                if (length > 1048576)
                {
                    throw new InvalidDataException("Validation ticket exceeds the 1 MiB protocol limit");
                }
                return ReceiveData(client, (int)length, 30000);
            }
            finally
            {
                logger.LogFunctionExit("ValidationServer.ReceiveLengthPrefixedPacket");
            }
        }

        private string ExtractUsername(byte[] ticket)
        {
            logger.LogFunctionEntry("ValidationServer.ExtractUsername", ticket == null ? 0 : ticket.Length);
            try
            {
                if (ticket == null || ticket.Length < 40)
                {
                    throw new InvalidDataException("Validation ticket is shorter than the Steam2 ticket prefix");
                }
                ushort ticketLength = ReadBigEndianUInt16(ticket, 18);
                int postTicketOffset = 20 + ticketLength;
                if (postTicketOffset + 20 > ticket.Length)
                {
                    throw new InvalidDataException("Steam2 validation ticket contains an invalid ticket length");
                }
                byte[] iv = CopyRange(ticket, postTicketOffset, 16);
                ushort cipherLength = ReadBigEndianUInt16(ticket, postTicketOffset + 18);
                byte[] cipher = CopyRange(ticket, postTicketOffset + 20, cipherLength);
                byte[] plain = EncryptionHelper.AesDecrypt(InnerKey, iv, cipher);
                if (plain.Length < 2)
                {
                    throw new InvalidDataException("Steam2 validation ticket decrypted to an empty payload");
                }
                ushort usernameLength = BitConverter.ToUInt16(plain, 0);
                if (usernameLength == 0 || usernameLength > plain.Length - 2)
                {
                    throw new InvalidDataException("Steam2 validation ticket contains an invalid username length");
                }
                return Encoding.GetEncoding("ISO-8859-1").GetString(plain, 2, usernameLength).TrimEnd('\0').ToLowerInvariant();
            }
            finally
            {
                logger.LogFunctionExit("ValidationServer.ExtractUsername");
            }
        }

        private byte[] BuildValidationTicket(byte[] requestTicket, uint accountId)
        {
            logger.LogFunctionEntry("ValidationServer.BuildValidationTicket", accountId);
            try
            {
                if (RSAKeyManager.NetworkKey == null)
                {
                    throw new InvalidOperationException("Network RSA key is unavailable while validating a Steam2 ticket");
                }
                byte[] challenge = CopyRange(requestTicket, 1, 4);
                List<byte> ticket = new List<byte>();
                ticket.AddRange(challenge);
                ticket.Add(0x01);
                ticket.AddRange(ToSteamTime(DateTime.UtcNow));
                ticket.Add((byte)(GetUniverse() >> 8));
                ticket.Add((byte)GetUniverse());
                ticket.AddRange(BitConverter.GetBytes(accountId));
                ticket.Add(0); ticket.Add(0);
                byte[] unsignedTicket = ticket.ToArray();
                byte[] signature = RSAKeyManager.RsaSignMessage(RSAKeyManager.NetworkKey, unsignedTicket);
                byte[] response = new byte[2 + unsignedTicket.Length + signature.Length];
                response[0] = 0x00; response[1] = 0x97;
                Array.Copy(unsignedTicket, 0, response, 2, unsignedTicket.Length);
                Array.Copy(signature, 0, response, 2 + unsignedTicket.Length, signature.Length);
                return response;
            }
            finally
            {
                logger.LogFunctionExit("ValidationServer.BuildValidationTicket");
            }
        }

        private ushort GetUniverse()
        {
            ushort universe;
            return UInt16.TryParse(config["universe"], out universe) ? universe : (ushort)1;
        }

        private static byte[] ToSteamTime(DateTime time)
        {
            long unixSeconds = (long)(time.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;
            ulong steamTime = (ulong)(unixSeconds + 62135596800L) * 1000000UL;
            return BitConverter.GetBytes(steamTime);
        }

        private static ushort ReadBigEndianUInt16(byte[] data, int offset)
        {
            if (offset < 0 || data == null || data.Length - offset < 2) throw new InvalidDataException("Steam2 ticket ended while reading an unsigned short");
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static byte[] CopyRange(byte[] data, int offset, int length)
        {
            if (data == null || offset < 0 || length < 0 || data.Length - offset < length) throw new InvalidDataException("Steam2 ticket field exceeds ticket bounds");
            byte[] result = new byte[length];
            Array.Copy(data, offset, result, 0, length);
            return result;
        }
    }
}
