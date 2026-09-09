using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Encryption;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Configuration Server
    /// Handles client configuration distribution and key exchange
    /// Implements full Steam config protocol with comprehensive logging
    /// </summary>
    public class ConfigServer : ServerBase
    {
        private byte[] innerKey;
        private byte[] innerIV;
        private byte[] networkKeyReply;

        /// <summary>
        /// Constructor for ConfigServer
        /// </summary>
        public ConfigServer(int port, Dictionary<string, string> config) 
            : base(port, config, "ConfigServer")
        {
            logger.LogFunctionEntry("ConfigServer.ctor", port);
            
            // Initialize encryption keys
            this.innerKey = StringHelper.HexToByteArray("10231230211281239191238542314233");
            this.innerIV = GenerateRandomIV();
            this.networkKeyReply = RSAKeyManager.NetworkKeyReply;
            
            logger.LogFunctionExit("ConfigServer.ctor");
        }

        /// <summary>
        /// Handle a client connection
        /// </summary>
        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            IPEndPoint clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string clientIp = clientEndPoint.Address.ToString();
            string clientId = clientIp + ": ";

            logger.LogFunctionEntry("ConfigServer.HandleClient", clientIp);
            logger.Info("{0}Config request from {1}", clientId, clientIp);

            try
            {
                NetworkStream stream = client.GetStream();
                
                // Read config request
                byte[] requestData = ReadConfigRequest(stream, clientId);
                logger.LogPacket(LogLevel.Debug, "RECV", requestData, "Config request");
                
                // Process config request
                byte[] responseData = ProcessConfigRequest(requestData, clientIp, clientId);
                
                // Send response
                if (responseData != null)
                {
                    logger.LogPacket(LogLevel.Debug, "SEND", responseData, "Config response");
                    stream.Write(responseData, 0, responseData.Length);
                    stream.Flush();
                    logger.Debug("{0}Sent {1} bytes to client", clientId, responseData.Length);
                }
                
                logger.Info("{0}Config request completed for {1}", clientId, clientIp);
            }
            catch (Exception ex)
            {
                logger.LogException("ConfigServer.HandleClient", ex, "for client " + clientIp);
            }
            finally
            {
                CloseClient(client);
                logger.Info("{0}Disconnected from Config Server", clientId);
                logger.LogFunctionExit("ConfigServer.HandleClient");
            }
        }

        /// <summary>
        /// Read config request from stream
        /// </summary>
        private byte[] ReadConfigRequest(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("ConfigServer.ReadConfigRequest");
            
            try
            {
                // Read request type (1 byte)
                byte[] requestType = new byte[1];
                stream.Read(requestType, 0, 1);
                logger.Debug("{0}Request type: 0x{1:X2}", clientId, requestType[0]);

                // Read request data based on type
                byte[] requestData = new byte[1];
                requestData[0] = requestType[0];

                switch (requestType[0])
                {
                    case 0x01: // Key exchange request
                        byte[] keyExchange = new byte[0]; // No additional data for key exchange
                        return CombineArrays(requestData, keyExchange);
                    
                    case 0x02: // Config data request
                        byte[] configData = ReadConfigDataRequest(stream, clientId);
                        return CombineArrays(requestData, configData);
                    
                    case 0x03: // Version info request
                        byte[] versionInfo = ReadVersionInfoRequest(stream, clientId);
                        return CombineArrays(requestData, versionInfo);
                    
                    default:
                        logger.Warning("{0}Unknown config request type: 0x{1:X2}", clientId, requestType[0]);
                        return requestData;
                }
            }
            catch (Exception ex)
            {
                logger.LogException("ConfigServer.ReadConfigRequest", ex);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("ConfigServer.ReadConfigRequest");
            }
        }

        /// <summary>
        /// Read config data request
        /// </summary>
        private byte[] ReadConfigDataRequest(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("ConfigServer.ReadConfigDataRequest");
            
            try
            {
                // Read client version (4 bytes)
                byte[] clientVersion = new byte[4];
                stream.Read(clientVersion, 0, 4);
                uint version = BitConverter.ToUInt32(clientVersion, 0);
                logger.Debug("{0}Client version: {1}", clientId, version);

                // Read platform (4 bytes)
                byte[] platform = new byte[4];
                stream.Read(platform, 0, 4);
                string platformStr = Encoding.ASCII.GetString(platform).Trim('\0');
                logger.Debug("{0}Platform: {1}", clientId, platformStr);

                // Return the data
                byte[] data = new byte[8];
                Array.Copy(clientVersion, 0, data, 0, 4);
                Array.Copy(platform, 0, data, 4, 4);
                
                logger.LogFunctionExit("ConfigServer.ReadConfigDataRequest", data.Length);
                return data;
            }
            catch (Exception ex)
            {
                logger.LogException("ConfigServer.ReadConfigDataRequest", ex);
                throw;
            }
        }

        /// <summary>
        /// Read version info request
        /// </summary>
        private byte[] ReadVersionInfoRequest(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("ConfigServer.ReadVersionInfoRequest");
            
            try
            {
                // Read requested version (4 bytes)
                byte[] requestedVersion = new byte[4];
                stream.Read(requestedVersion, 0, 4);
                uint version = BitConverter.ToUInt32(requestedVersion, 0);
                logger.Debug("{0}Requested version: {1}", clientId, version);

                logger.LogFunctionExit("ConfigServer.ReadVersionInfoRequest", requestedVersion.Length);
                return requestedVersion;
            }
            catch (Exception ex)
            {
                logger.LogException("ConfigServer.ReadVersionInfoRequest", ex);
                throw;
            }
        }

        /// <summary>
        /// Process config request
        /// </summary>
        private byte[] ProcessConfigRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ConfigServer.ProcessConfigRequest", requestData.Length);
            
            if (requestData.Length == 0)
            {
                logger.Warning("{0}Empty config request", clientId);
                logger.LogFunctionExit("ConfigServer.ProcessConfigRequest", null);
                return null;
            }

            byte requestType = requestData[0];
            logger.Debug("{0}Processing config request type: 0x{1:X2}", clientId, requestType);
            
            byte[] response = null;
            
            switch (requestType)
            {
                case 0x01: // Key exchange request
                    response = HandleKeyExchangeRequest(clientIp, clientId);
                    break;
                case 0x02: // Config data request
                    response = HandleConfigDataRequest(requestData, clientIp, clientId);
                    break;
                case 0x03: // Version info request
                    response = HandleVersionInfoRequest(requestData, clientIp, clientId);
                    break;
                default:
                    logger.Warning("{0}Unknown config request type: 0x{{1:X2}", clientId, requestType);
                    response = BuildConfigErrorResponse(0x01);
                    break;
            }

            logger.LogFunctionExit("ConfigServer.ProcessConfigRequest", response != null ? response.Length : 0);
            return response;
        }

        /// <summary>
        /// Handle key exchange request
        /// </summary>
        private byte[] HandleKeyExchangeRequest(string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ConfigServer.HandleKeyExchangeRequest");
            
            try
            {
                if (networkKeyReply == null)
                {
                    logger.Error("Network key reply not initialized");
                    logger.LogFunctionExit("ConfigServer.HandleKeyExchangeRequest", null);
                    return BuildConfigErrorResponse(0x02);
                }

                logger.Info("{0}Sending network key exchange response: {1} bytes", clientId, networkKeyReply.Length);
                logger.LogPacket(LogLevel.Debug, "SEND", networkKeyReply, "Network key exchange");
                
                logger.LogFunctionExit("ConfigServer.HandleKeyExchangeRequest", networkKeyReply.Length);
                return networkKeyReply;
            }
            catch (Exception ex)
            {
                logger.LogException("ConfigServer.HandleKeyExchangeRequest", ex);
                return BuildConfigErrorResponse(0x02);
            }
        }

        /// <summary>
        /// Handle config data request
        /// </summary>
        private byte[] HandleConfigDataRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ConfigServer.HandleConfigDataRequest");
            
            try
            {
                if (requestData.Length < 9)
                {
                    logger.Warning("{0}Invalid config data request length", clientId);
                    return BuildConfigErrorResponse(0x02);
                }

                uint clientVersion = BitConverter.ToUInt32(requestData, 1);
                string platform = Encoding.ASCII.GetString(requestData, 5, 4).Trim('\0');

                logger.Debug("{0}Config data request - Version: {1}, Platform: {2}", clientId, clientVersion, platform);

                // Build configuration response
                byte[] configResponse = BuildConfigResponse(clientVersion, platform);
                
                logger.Info("{0}Config data sent: {1} bytes", clientId, configResponse.Length);
                logger.LogFunctionExit("ConfigServer.HandleConfigDataRequest", configResponse.Length);
                return configResponse;
            }
            catch (Exception ex)
            {
                logger.LogException("ConfigServer.HandleConfigDataRequest", ex);
                return BuildConfigErrorResponse(0x02);
            }
        }

        /// <summary>
        /// Handle version info request
        /// </summary>
        private byte[] HandleVersionInfoRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ConfigServer.HandleVersionInfoRequest");
            
            try
            {
                if (requestData.Length < 5)
                {
                    logger.Warning("{0}Invalid version info request length", clientId);
                    return BuildConfigErrorResponse(0x02);
                }

                uint requestedVersion = BitConverter.ToUInt32(requestData, 1);
                logger.Debug("{0}Version info request - Requested: {1}", clientId, requestedVersion);

                // Build version info response
                byte[] versionResponse = BuildVersionInfoResponse(requestedVersion);
                
                logger.Info("{0}Version info sent: {1} bytes", clientId, versionResponse.Length);
                logger.LogFunctionExit("ConfigServer.HandleVersionInfoRequest", versionResponse.Length);
                return versionResponse;
            }
            catch (Exception ex)
            {
                logger.LogException("ConfigServer.HandleVersionInfoRequest", ex);
                return BuildConfigErrorResponse(0x02);
            }
        }

        /// <summary>
        /// Build configuration response
        /// </summary>
        private byte[] BuildConfigResponse(uint clientVersion, string platform)
        {
            logger.LogFunctionEntry("ConfigServer.BuildConfigResponse", clientVersion, platform);
            
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Server IP
                string serverIp = config["server_ip"];
                byte[] ipBytes = IPAddress.Parse(serverIp).GetAddressBytes();
                ms.Write(ipBytes, 0, 4);
                
                // Server ports
                ushort authPort = ushort.Parse(config["auth_server_port"]);
                ushort contentPort = ushort.Parse(config["content_server_port"]);
                ushort configPort = ushort.Parse(config["config_server_port"]);
                ushort dirPort = ushort.Parse(config["dir_server_port"]);
                
                byte[] authPortBytes = BitConverter.GetBytes(authPort);
                byte[] contentPortBytes = BitConverter.GetBytes(contentPort);
                byte[] configPortBytes = BitConverter.GetBytes(configPort);
                byte[] dirPortBytes = BitConverter.GetBytes(dirPort);
                
                ms.Write(authPortBytes, 0, 2);
                ms.Write(contentPortBytes, 0, 2);
                ms.Write(configPortBytes, 0, 2);
                ms.Write(dirPortBytes, 0, 2);
                
                // Cell ID
                uint cellId = uint.Parse(config["cellid"]);
                byte[] cellIdBytes = BitConverter.GetBytes(cellId);
                ms.Write(cellIdBytes, 0, 4);

                // Region
                string region = config["override_ip_country_region"];
                byte[] regionBytes = Encoding.ASCII.GetBytes(region);
                ms.WriteByte((byte)regionBytes.Length);
                ms.Write(regionBytes, 0, regionBytes.Length);

                // Universe
                uint universe = uint.Parse(config["universe"]);
                byte[] universeBytes = BitConverter.GetBytes(universe);
                ms.Write(universeBytes, 0, 4);

                // Steam date/time
                string steamDate = config["steam_date"];
                string steamTime = config["steam_time"];
                byte[] steamDateBytes = Encoding.ASCII.GetBytes(steamDate);
                byte[] steamTimeBytes = Encoding.ASCII.GetBytes(steamTime);
                ms.WriteByte((byte)steamDateBytes.Length);
                ms.Write(steamDateBytes, 0, steamDateBytes.Length);
                ms.WriteByte((byte)steamTimeBytes.Length);
                ms.Write(steamTimeBytes, 0, steamTimeBytes.Length);

                // Use webserver flag
                string useWebserver = config["use_webserver"];
                ms.WriteByte((byte)(useWebserver.ToLower() == "true" ? 1 : 0));

                // Web server config
                string httpIp = config["http_ip"];
                string httpDomainname = config["http_domainname"];
                string httpPort = config["community_port"];
                
                byte[] httpIpBytes = Encoding.ASCII.GetBytes(httpIp);
                byte[] httpDomainnameBytes = Encoding.ASCII.GetBytes(httpDomainname);
                byte[] httpPortBytes = Encoding.ASCII.GetBytes(httpPort);
                
                ms.WriteByte((byte)httpIpBytes.Length);
                ms.Write(httpIpBytes, 0, httpIpBytes.Length);
                ms.WriteByte((byte)httpDomainnameBytes.Length);
                ms.Write(httpDomainnameBytes, 0, httpDomainnameBytes.Length);
                ms.WriteByte((byte)httpPortBytes.Length);
                ms.Write(httpPortBytes, 0, httpPortBytes.Length);

                logger.Debug("Built config response for version {0}, platform {1}", clientVersion, platform);
                logger.LogFunctionExit("ConfigServer.BuildConfigResponse", ms.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build version info response
        /// </summary>
        private byte[] BuildVersionInfoResponse(uint requestedVersion)
        {
            logger.LogFunctionEntry("ConfigServer.BuildVersionInfoResponse", requestedVersion);
            
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Current version
                string currentVersion = GlobalState.LocalVer;
                byte[] versionBytes = Encoding.ASCII.GetBytes(currentVersion);
                ms.WriteByte((byte)versionBytes.Length);
                ms.Write(versionBytes, 0, versionBytes.Length);

                // Supported versions
                uint[] supportedVersions = new uint[] { 1, 2, 3, 4, 5 };
                ms.WriteByte((byte)supportedVersions.Length);
                
                foreach (uint version in supportedVersions)
                {
                    byte[] versionBytes2 = BitConverter.GetBytes(version);
                    ms.Write(versionBytes2, 0, 4);
                }

                // Required flags
                uint requiredFlags = 0x00000001; // Basic support
                byte[] flagsBytes = BitConverter.GetBytes(requiredFlags);
                ms.Write(flagsBytes, 0, 4);

                logger.Debug("Built version info response for requested version {0}", requestedVersion);
                logger.LogFunctionExit("ConfigServer.BuildVersionInfoResponse", ms.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build config error response
        /// </summary>
        private byte[] BuildConfigErrorResponse(uint errorCode)
        {
            logger.LogFunctionEntry("ConfigServer.BuildConfigErrorResponse", errorCode);
            
            byte[] response = new byte[] { (byte)errorCode };
            
            logger.LogFunctionExit("ConfigServer.BuildConfigErrorResponse", response.Length);
            return response;
        }

        /// <summary>
        /// Generate random IV
        /// </summary>
        private byte[] GenerateRandomIV()
        {
            byte[] iv = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }
            return iv;
        }

        /// <summary>
        /// Combine byte arrays
        /// </summary>
        private byte[] CombineArrays(byte[] array1, byte[] array2)
        {
            byte[] result = new byte[array1.Length + array2.Length];
            Array.Copy(array1, 0, result, 0, array1.Length);
            Array.Copy(array2, 0, result, array1.Length, array2.Length);
            return result;
        }
    }
}