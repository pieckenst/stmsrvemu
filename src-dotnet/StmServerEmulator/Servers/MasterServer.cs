using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Half-Life/Steam master-server datagram protocol used by Steam2-era games.
    /// It keeps the old 0xffffffff command header and produces legacy M2A replies.
    /// </summary>
    public sealed class MasterServer : UdpServerBase
    {
        private readonly Dictionary<string, RegisteredServer> servers;
        private readonly Dictionary<string, Challenge> challenges;
        private readonly Random random;
        private uint nextUniqueId;

        public MasterServer(int port, Dictionary<string, string> config)
            : base(port, config, "MasterServer")
        {
            logger.LogFunctionEntry("MasterServer.ctor", port);
            servers = new Dictionary<string, RegisteredServer>();
            challenges = new Dictionary<string, Challenge>();
            random = new Random();
            nextUniqueId = 100;
            logger.LogFunctionExit("MasterServer.ctor");
        }

        protected override byte[] ProcessDatagram(byte[] data, IPEndPoint remote)
        {
            logger.LogFunctionEntry("MasterServer.ProcessDatagram", remote.ToString(), data == null ? 0 : data.Length);
            try
            {
                if (data == null || data.Length < 5 || data[0] != 0xFF || data[1] != 0xFF || data[2] != 0xFF || data[3] != 0xFF)
                {
                    logger.Warning("Ignoring master packet without legacy header from {0}", remote);
                    return null;
                }
                byte command = data[4];
                byte[] payload = CopyPayload(data, 5);
                logger.Debug("Master command 0x{0:X2} from {1}", command, remote);
                switch (command)
                {
                    case (byte)'q': return BuildChallengeReply(remote);
                    case (byte)'a': RegisterClassicHeartbeat(payload, remote); return null;
                    case (byte)'0': RegisterInfoHeartbeat(payload, remote); return null;
                    case (byte)'b': RemoveServer(remote); return null;
                    case (byte)'c': return BuildServerList(false, 0, String.Empty);
                    case (byte)'e': return BuildBatchReply(payload, false);
                    case (byte)'1': return BuildBatchReply(payload, true);
                    case (byte)'g': return BuildMotdReply();
                    case (byte)'i': return new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, (byte)'j' };
                    case (byte)'k': logger.Info("Master A2A_NACK from {0}", remote); return null;
                    case (byte)'v': return BuildMasterList(remote);
                    default:
                        logger.LogPacket(StmServerEmulator.Logging.LogLevel.Warning, "UNKNOWN", data, "Master client=" + remote);
                        return null;
                }
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.ProcessDatagram");
            }
        }

        private byte[] BuildChallengeReply(IPEndPoint remote)
        {
            logger.LogFunctionEntry("MasterServer.BuildChallengeReply", remote.ToString());
            try
            {
                string key = EndpointKey(remote);
                Challenge challenge;
                if (!challenges.TryGetValue(key, out challenge) || challenge.ExpiresAtUtc < DateTime.UtcNow)
                {
                    challenge = new Challenge();
                    challenge.Value = random.Next(1, Int32.MaxValue);
                    challenge.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
                    challenges[key] = challenge;
                }
                byte[] response = new byte[10];
                response[0] = 0xFF; response[1] = 0xFF; response[2] = 0xFF; response[3] = 0xFF;
                response[4] = (byte)'s'; response[5] = 0x0A;
                Array.Copy(BitConverter.GetBytes(challenge.Value), 0, response, 6, 4);
                logger.Info("Issued master challenge {0} to {1}", challenge.Value, remote);
                return response;
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.BuildChallengeReply");
            }
        }

        private void RegisterClassicHeartbeat(byte[] payload, IPEndPoint remote)
        {
            logger.LogFunctionEntry("MasterServer.RegisterClassicHeartbeat", remote.ToString());
            try
            {
                LegacyPacketReader reader = new LegacyPacketReader(payload);
                int challenge = reader.ReadDecimalString();
                reader.ReadString(); // sequence
                int players = reader.ReadDecimalString();
                reader.ReadString(); // protocol
                if (!ValidateChallenge(remote, challenge))
                {
                    SendDatagram(BuildReject("Bad challenge."), remote);
                    return;
                }
                string gameDir = reader.ReadString();
                string info = reader.ReadString();
                RegisteredServer server = GetOrCreateServer(remote);
                server.Players = players;
                server.GameDirectory = gameDir;
                server.Info = info;
                server.LastHeartbeatUtc = DateTime.UtcNow;
                logger.Info("Registered classic heartbeat {0}: players={1}, game={2}", remote, players, gameDir);
            }
            catch (Exception ex)
            {
                logger.LogException("MasterServer.RegisterClassicHeartbeat", ex, remote.ToString());
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.RegisterClassicHeartbeat");
            }
        }

        private void RegisterInfoHeartbeat(byte[] payload, IPEndPoint remote)
        {
            logger.LogFunctionEntry("MasterServer.RegisterInfoHeartbeat", remote.ToString());
            try
            {
                string info = Encoding.GetEncoding("ISO-8859-1").GetString(payload);
                int challenge = GetInfoInt(info, "challenge", -1);
                if (!ValidateChallenge(remote, challenge))
                {
                    SendDatagram(BuildReject("Reject connection: Bad challenge."), remote);
                    return;
                }
                RegisteredServer server = GetOrCreateServer(remote);
                server.Players = GetInfoInt(info, "players", 0);
                server.MaximumPlayers = GetInfoInt(info, "max", 0);
                server.Bots = GetInfoInt(info, "bots", 0);
                server.GameDirectory = GetInfoValue(info, "gamedir").ToLowerInvariant();
                server.Map = GetInfoValue(info, "map").ToLowerInvariant();
                server.OperatingSystem = GetInfoValue(info, "os").ToLowerInvariant();
                server.Password = GetInfoInt(info, "password", 0) != 0;
                server.Dedicated = GetInfoInt(info, "dedicated", 0) != 0;
                server.Secure = GetInfoInt(info, "secure", 0) != 0;
                server.Info = info;
                server.LastHeartbeatUtc = DateTime.UtcNow;
                logger.Info("Registered info heartbeat {0}: {1}/{2}, game={3}, map={4}", remote, server.Players, server.MaximumPlayers, server.GameDirectory, server.Map);
            }
            catch (Exception ex)
            {
                logger.LogException("MasterServer.RegisterInfoHeartbeat", ex, remote.ToString());
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.RegisterInfoHeartbeat");
            }
        }

        private byte[] BuildServerList(bool includeInfo, uint nextId, string criteria)
        {
            logger.LogFunctionEntry("MasterServer.BuildServerList", includeInfo, nextId, criteria);
            try
            {
                PurgeExpiredServers();
                List<byte> response = new List<byte>();
                response.Add(0xFF); response.Add(0xFF); response.Add(0xFF); response.Add(0xFF); response.Add((byte)'f'); response.Add(0x0A);
                List<RegisteredServer> eligible = new List<RegisteredServer>();
                foreach (RegisteredServer server in servers.Values)
                {
                    if (server.UniqueId > nextId && MatchesCriteria(server, criteria))
                    {
                        eligible.Add(server);
                    }
                }
                eligible.Sort(new ServerIdComparer());
                uint lastId = 0;
                for (int i = 0; i < eligible.Count; i++)
                {
                    RegisteredServer server = eligible[i];
                    byte[] ip = server.Endpoint.Address.GetAddressBytes();
                    byte[] info = includeInfo ? Encoding.GetEncoding("ISO-8859-1").GetBytes(server.Info ?? String.Empty) : new byte[0];
                    int entrySize = 6 + (includeInfo ? info.Length + 1 : 0);
                    if (response.Count + entrySize + 6 > 1024)
                    {
                        break;
                    }
                    response.AddRange(ip);
                    response.Add((byte)(server.Endpoint.Port >> 8));
                    response.Add((byte)server.Endpoint.Port);
                    if (includeInfo)
                    {
                        response.AddRange(info);
                        response.Add(0);
                    }
                    lastId = server.UniqueId;
                }
                response.Add(0); response.Add(0); response.Add(0); response.Add(0); response.Add(0); response.Add(0);
                logger.Info("Master list response: {0} bytes, continuation={1}", response.Count, lastId);
                return response.ToArray();
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.BuildServerList");
            }
        }

        private byte[] BuildBatchReply(byte[] payload, bool hasCriteria)
        {
            logger.LogFunctionEntry("MasterServer.BuildBatchReply", hasCriteria, payload == null ? 0 : payload.Length);
            try
            {
                if (payload == null || payload.Length < 4)
                {
                    throw new InvalidDataException("Master batch request is missing continuation id");
                }
                uint continuation = PacketHelper.ReadBigEndianUInt32(payload, 0);
                bool includeInfo = (continuation & 0x80000000U) != 0;
                uint nextId = continuation & 0x7FFFFFFFU;
                string criteria = String.Empty;
                if (hasCriteria && payload.Length > 4)
                {
                    criteria = ReadNullTerminatedString(payload, 4);
                }
                return BuildServerList(includeInfo, nextId, criteria);
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.BuildBatchReply");
            }
        }

        private byte[] BuildMotdReply()
        {
            logger.LogFunctionEntry("MasterServer.BuildMotdReply");
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "motd.txt");
                byte[] motd = File.Exists(path) ? File.ReadAllBytes(path) : new byte[0];
                byte[] response = new byte[7 + motd.Length];
                response[0] = 0xFF; response[1] = 0xFF; response[2] = 0xFF; response[3] = 0xFF; response[4] = (byte)'h'; response[5] = 0x0D; response[6] = 0x0A;
                Array.Copy(motd, 0, response, 7, motd.Length);
                return response;
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.BuildMotdReply");
            }
        }

        private byte[] BuildMasterList(IPEndPoint client)
        {
            logger.LogFunctionEntry("MasterServer.BuildMasterList", client.ToString());
            try
            {
                IPAddress ip = SelectAdvertisedAddress(client);
                List<byte> result = new List<byte>();
                result.Add(0xFF); result.Add(0xFF); result.Add(0xFF); result.Add(0xFF); result.Add((byte)'w'); result.Add(0x0D); result.Add(0x0A);
                AddMasterEndpoint(result, ip, GetPort("validation_port"));
                AddMasterEndpoint(result, ip, GetPort("validation_port"));
                AddMasterEndpoint(result, ip, GetPort("masterhl2_server_port"));
                return result.ToArray();
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.BuildMasterList");
            }
        }

        private void AddMasterEndpoint(List<byte> result, IPAddress ip, int endpointPort)
        {
            result.Add(0xFF); result.Add(0xFF); result.Add(0xFF); result.Add(0xFF);
            result.AddRange(ip.GetAddressBytes());
            result.Add((byte)(endpointPort >> 8));
            result.Add((byte)endpointPort);
        }

        private byte[] BuildReject(string reason)
        {
            byte[] message = Encoding.GetEncoding("ISO-8859-1").GetBytes("\xFF\xFF\xFF\xFFl\n" + reason);
            return message;
        }

        private RegisteredServer GetOrCreateServer(IPEndPoint endpoint)
        {
            string key = EndpointKey(endpoint);
            RegisteredServer value;
            if (!servers.TryGetValue(key, out value))
            {
                value = new RegisteredServer();
                value.Endpoint = new IPEndPoint(endpoint.Address, endpoint.Port);
                value.UniqueId = ++nextUniqueId;
                servers[key] = value;
                logger.Info("Registered new dedicated server {0} as id {1}", endpoint, value.UniqueId);
            }
            return value;
        }

        private void RemoveServer(IPEndPoint endpoint)
        {
            logger.LogFunctionEntry("MasterServer.RemoveServer", endpoint.ToString());
            try
            {
                servers.Remove(EndpointKey(endpoint));
                challenges.Remove(EndpointKey(endpoint));
                logger.Info("Removed dedicated server {0}", endpoint);
            }
            finally
            {
                logger.LogFunctionExit("MasterServer.RemoveServer");
            }
        }

        private bool ValidateChallenge(IPEndPoint endpoint, int value)
        {
            Challenge challenge;
            bool valid = challenges.TryGetValue(EndpointKey(endpoint), out challenge) && challenge.Value == value && challenge.ExpiresAtUtc >= DateTime.UtcNow;
            logger.Debug("Master challenge validation for {0}: {1}", endpoint, valid);
            return valid;
        }

        private void PurgeExpiredServers()
        {
            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, RegisteredServer> entry in servers)
            {
                if (entry.Value.LastHeartbeatUtc < DateTime.UtcNow.AddMinutes(-5))
                {
                    expired.Add(entry.Key);
                }
            }
            for (int i = 0; i < expired.Count; i++)
            {
                servers.Remove(expired[i]);
            }
        }

        private IPAddress SelectAdvertisedAddress(IPEndPoint client)
        {
            string configured = GetConfigValue("public_ip");
            IPAddress parsed;
            if (configured == "0.0.0.0" || !IPAddress.TryParse(configured, out parsed))
            {
                configured = GetConfigValue("server_ip");
                if (!IPAddress.TryParse(configured, out parsed))
                {
                    parsed = IPAddress.Loopback;
                }
            }
            return parsed;
        }

        private int GetPort(string key)
        {
            int portValue;
            return Int32.TryParse(GetConfigValue(key), out portValue) ? portValue : 0;
        }

        private string GetConfigValue(string key)
        {
            string value;
            return config.TryGetValue(key, out value) ? value : String.Empty;
        }

        private static bool MatchesCriteria(RegisteredServer server, string criteria)
        {
            if (String.IsNullOrEmpty(criteria))
            {
                return true;
            }
            string gameDirectory = GetInfoValue(criteria, "gamedir");
            return String.IsNullOrEmpty(gameDirectory) || String.Equals(server.GameDirectory, gameDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetInfoValue(string info, string key)
        {
            if (String.IsNullOrEmpty(info))
            {
                return String.Empty;
            }
            string marker = "\\" + key + "\\";
            int start = info.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return String.Empty;
            }
            start += marker.Length;
            int end = info.IndexOf('\\', start);
            return end < 0 ? info.Substring(start) : info.Substring(start, end - start);
        }

        private static int GetInfoInt(string info, string key, int defaultValue)
        {
            int value;
            return Int32.TryParse(GetInfoValue(info, key), out value) ? value : defaultValue;
        }

        private static string EndpointKey(IPEndPoint endpoint)
        {
            return endpoint.Address.ToString() + ":" + endpoint.Port;
        }

        private static byte[] CopyPayload(byte[] data, int offset)
        {
            byte[] result = new byte[data.Length - offset];
            Array.Copy(data, offset, result, 0, result.Length);
            return result;
        }

        private static string ReadNullTerminatedString(byte[] data, int offset)
        {
            int end = offset;
            while (end < data.Length && data[end] != 0)
            {
                end++;
            }
            return Encoding.GetEncoding("ISO-8859-1").GetString(data, offset, end - offset);
        }

        private sealed class Challenge { public int Value; public DateTime ExpiresAtUtc; }
        private sealed class RegisteredServer
        {
            public IPEndPoint Endpoint; public uint UniqueId; public DateTime LastHeartbeatUtc;
            public int Players; public int MaximumPlayers; public int Bots; public string GameDirectory = String.Empty;
            public string Map = String.Empty; public string OperatingSystem = String.Empty; public bool Password; public bool Dedicated; public bool Secure; public string Info = String.Empty;
        }
        private sealed class ServerIdComparer : IComparer<RegisteredServer>
        {
            public int Compare(RegisteredServer left, RegisteredServer right) { return left.UniqueId.CompareTo(right.UniqueId); }
        }
        private sealed class LegacyPacketReader
        {
            private readonly byte[] data; private int position;
            public LegacyPacketReader(byte[] data) { this.data = data ?? new byte[0]; }
            public string ReadString() { int start = position; while (position < data.Length && data[position] != 0) position++; string value = Encoding.GetEncoding("ISO-8859-1").GetString(data, start, position - start); if (position < data.Length) position++; return value; }
            public int ReadDecimalString() { int value; return Int32.TryParse(ReadString(), out value) ? value : 0; }
        }
    }
}
