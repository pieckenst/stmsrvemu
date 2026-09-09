using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Directory Server
    /// Handles server discovery, load balancing, and server list management
    /// Implements full Steam directory protocol with comprehensive logging
    /// </summary>
    public class DirectoryServer : ServerBase
    {
        private List<ServerInfo> directoryServers;
        private Dictionary<string, ServerInfo> serverCache;
        private bool isMaster;
        private List<string> peerServers;

        /// <summary>
        /// Constructor for DirectoryServer
        /// </summary>
        public DirectoryServer(int port, Dictionary<string, string> config) 
            : base(port, config, "DirectoryServer")
        {
            logger.LogFunctionEntry("DirectoryServer.ctor", port);
            
            this.directoryServers = new List<ServerInfo>();
            this.serverCache = new Dictionary<string, ServerInfo>();
            this.isMaster = config["dir_ismaster"].ToLower() == "true";
            this.peerServers = new List<string>();
            
            InitializeServerList();
            logger.LogFunctionExit("DirectoryServer.ctor");
        }

        /// <summary>
        /// Initialize server list from configuration
        /// </summary>
        private void InitializeServerList()
        {
            logger.LogFunctionEntry("DirectoryServer.InitializeServerList");
            
            try
            {
                // Add this server to the list
                string serverIp = config["server_ip"];
                directoryServers.Add(new ServerInfo
                {
                    Name = "DirectoryServer",
                    Port = int.Parse(config["dir_server_port"]),
                    IpAddress = serverIp,
                    Type = "Directory",
                    Load = 0,
                    Region = config["override_ip_country_region"]
                });

                // Add auth server
                directoryServers.Add(new ServerInfo
                {
                    Name = "AuthServer",
                    Port = int.Parse(config["auth_server_port"]),
                    IpAddress = serverIp,
                    Type = "Auth",
                    Load = 0,
                    Region = config["override_ip_country_region"]
                });

                // Add content server
                directoryServers.Add(new ServerInfo
                {
                    Name = "ContentServer",
                    Port = int.Parse(config["content_server_port"]),
                    IpAddress = serverIp,
                    Type = "Content",
                    Load = 0,
                    Region = config["override_ip_country_region"]
                });

                // Add config server
                directoryServers.Add(new ServerInfo
                {
                    Name = "ConfigServer",
                    Port = int.Parse(config["config_server_port"]),
                    IpAddress = serverIp,
                    Type = "Config",
                    Load = 0,
                    Region = config["override_ip_country_region"]
                });

                // Add client update server
                directoryServers.Add(new ServerInfo
                {
                    Name = "ClientUpdateServer",
                    Port = int.Parse(config["clupd_server_port"]),
                    IpAddress = serverIp,
                    Type = "ClientUpdate",
                    Load = 0,
                    Region = config["override_ip_country_region"]
                });

                // Add master server
                directoryServers.Add(new ServerInfo
                {
                    Name = "MasterServer",
                    Port = int.Parse(config["masterhl1_server_port"]),
                    IpAddress = serverIp,
                    Type = "Master",
                    Load = 0,
                    Region = config["override_ip_country_region"]
                });

                // Parse peer servers if configured
                string peers = config["dir_peers"];
                if (!string.IsNullOrEmpty(peers))
                {
                    string[] peerList = peers.Split(',');
                    foreach (string peer in peerList)
                    {
                        peerServers.Add(peer.Trim());
                        logger.Debug("Added peer server: {0}", peer.Trim());
                    }
                }

                logger.Info("Initialized directory server list - {0} servers, {1} peers", 
                    directoryServers.Count, peerServers.Count);
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.InitializeServerList", ex);
            }
            finally
            {
                logger.LogFunctionExit("DirectoryServer.InitializeServerList");
            }
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

            logger.LogFunctionEntry("DirectoryServer.HandleClient", clientIp);
            logger.Info("{0}Directory request from {1}", clientId, clientIp);

            try
            {
                NetworkStream stream = client.GetStream();
                
                // Read directory request
                byte[] requestData = ReadDirectoryRequest(stream, clientId);
                logger.LogPacket(LogLevel.Debug, "RECV", requestData, "Directory request");
                
                // Process directory request
                byte[] responseData = ProcessDirectoryRequest(requestData, bytesRead, clientIp, clientId);
                
                // Send response
                if (responseData != null)
                {
                    logger.LogPacket(LogLevel.Debug, "SEND", responseData, "Directory response");
                    stream.Write(responseData, 0, responseData.Length);
                    stream.Flush();
                    logger.Debug("{0}Sent {1} bytes to client", clientId, responseData.Length);
                }
                
                logger.Info("{0}Directory request completed for {1}", clientId, clientIp);
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.HandleClient", ex, "for client " + clientIp);
            }
            finally
            {
                CloseClient(client);
                logger.Info("{0}Disconnected from Directory Server", clientId);
                logger.LogFunctionExit("DirectoryServer.HandleClient");
            }
        }

        /// <summary>
        /// Read directory request from stream
        /// </summary>
        private byte[] ReadDirectoryRequest(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.ReadDirectoryRequest");
            
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
                    case 0x01: // Server list request
                        byte[] serverList = ReadServerListRequest(stream, clientId);
                        return CombineArrays(requestData, serverList);
                    
                    case 0x02: // Heartbeat
                        byte[] heartbeat = ReadHeartbeat(stream, clientId);
                        return CombineArrays(requestData, heartbeat);
                    
                    case 0x03: // Get server by type
                        byte[] getByType = ReadGetByTypeRequest(stream, clientId);
                        return CombineArrays(requestData, getByType);
                    
                    default:
                        logger.Warning("{0}Unknown directory request type: 0x{1:X2}", clientId, requestType[0]);
                        return requestData;
                }
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.ReadDirectoryRequest", ex);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("DirectoryServer.ReadDirectoryRequest");
            }
        }

        /// <summary>
        /// Read server list request
        /// </summary>
        private byte[] ReadServerListRequest(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.ReadServerListRequest");
            
            try
            {
                // Read server type filter (4 bytes)
                byte[] serverTypeFilter = new byte[4];
                stream.Read(serverTypeFilter, 0, 4);
                string serverType = Encoding.ASCII.GetString(serverTypeFilter).Trim('\0');
                logger.Debug("{0}Server type filter: {1}", clientId, serverType);

                // Read region filter (4 bytes)
                byte[] regionFilter = new byte[4];
                stream.Read(regionFilter, 0, 4);
                string region = Encoding.ASCII.GetString(regionFilter).Trim('\0');
                logger.Debug("{0}Region filter: {1}", clientId, region);

                // Return the filter data
                byte[] filterData = new byte[8];
                Array.Copy(serverTypeFilter, 0, filterData, 0, 4);
                Array.Copy(regionFilter, 0, filterData, 4, 4);
                
                logger.LogFunctionExit("DirectoryServer.ReadServerListRequest", filterData.Length);
                return filterData;
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.ReadServerListRequest", ex);
                throw;
            }
        }

        /// <summary>
        /// Read heartbeat request
        /// </summary>
        private byte[] ReadHeartbeat(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.ReadHeartbeat");
            
            try
            {
                // Read server info (IP, port, type)
                byte[] serverInfo = new byte[9]; // 4 bytes IP + 2 bytes port + 3 bytes type
                stream.Read(serverInfo, 0, 9);
                
                string heartbeatIp = IPAddress.Parse(new byte[] { serverInfo[0], serverInfo[1], serverInfo[2], serverInfo[3] }).ToString();
                ushort heartbeatPort = BitConverter.ToUInt16(serverInfo, 4);
                string heartbeatType = Encoding.ASCII.GetString(serverInfo, 6, 3).Trim('\0');
                
                logger.Debug("{0}Heartbeat from {1}:{2} ({3})", clientId, heartbeatIp, heartbeatPort, heartbeatType);

                logger.LogFunctionExit("DirectoryServer.ReadHeartbeat", serverInfo.Length);
                return serverInfo;
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.ReadHeartbeat", ex);
                throw;
            }
        }

        /// <summary>
        /// Read get by type request
        /// </summary>
        private byte[] ReadGetByTypeRequest(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.ReadGetByTypeRequest");
            
            try
            {
                // Read server type (4 bytes)
                byte[] serverType = new byte[4];
                stream.Read(serverType, 0, 4);
                string type = Encoding.ASCII.GetString(serverType).Trim('\0');
                logger.Debug("{0}Get by type: {1}", clientId, type);

                logger.LogFunctionExit("DirectoryServer.ReadGetByTypeRequest", serverType.Length);
                return serverType;
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.ReadGetByTypeRequest", ex);
                throw;
            }
        }

        /// <summary>
        /// Process directory request
        /// </summary>
        private byte[] ProcessDirectoryRequest(byte[] requestData, int bytesRead, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.ProcessDirectoryRequest", requestData.Length);
            
            if (requestData.Length == 0)
            {
                logger.Warning("{0}Empty directory request", clientId);
                logger.LogFunctionExit("DirectoryServer.ProcessDirectoryRequest", null);
                return null;
            }

            byte requestType = requestData[0];
            logger.Debug("{0}Processing directory request type: 0x{1:X2}", clientId, requestType);
            
            byte[] response = null;
            
            switch (requestType)
            {
                case 0x01: // Server list request
                    response = HandleServerListRequest(requestData, clientIp, clientId);
                    break;
                case 0x02: // Heartbeat
                    response = HandleHeartbeat(requestData, clientIp, clientId);
                    break;
                case 0x03: // Get server by type
                    response = HandleGetByTypeRequest(requestData, clientIp, clientId);
                    break;
                default:
                    logger.Warning("{0}Unknown directory request type: 0x{1:X2}", clientId, requestType);
                    response = BuildDirectoryErrorResponse(0x01);
                    break;
            }

            logger.LogFunctionExit("DirectoryServer.ProcessDirectoryRequest", response != null ? response.Length : 0);
            return response;
        }

        /// <summary>
        /// Handle server list request
        /// </summary>
        private byte[] HandleServerListRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.HandleServerListRequest");
            
            try
            {
                string serverTypeFilter = "";
                string regionFilter = "";

                if (requestData.Length >= 9)
                {
                    serverTypeFilter = Encoding.ASCII.GetString(requestData, 1, 4).Trim('\0');
                    regionFilter = Encoding.ASCII.GetString(requestData, 5, 4).Trim('\0');
                }

                logger.Debug("{0}Server list filter - Type: {1}, Region: {2}", clientId, serverTypeFilter, regionFilter);

                // Filter servers based on request
                List<ServerInfo> filteredServers = FilterServers(serverTypeFilter, regionFilter);
                logger.Debug("{0}Returning {1} servers", clientId, filteredServers.Count);

                // Build server list response
                byte[] response = BuildServerListResponse(filteredServers, isMaster);
                
                logger.Info("{0}Server list sent: {1} servers", clientId, filteredServers.Count);
                return response;
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.HandleServerListRequest", ex);
                return BuildDirectoryErrorResponse(0x02);
            }
            finally
            {
                logger.LogFunctionExit("DirectoryServer.HandleServerListRequest");
            }
        }

        /// <summary>
        /// Handle heartbeat request
        /// </summary>
        private byte[] HandleHeartbeat(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.HandleHeartbeat");
            
            try
            {
                if (requestData.Length >= 10)
                {
                    string heartbeatIp = IPAddress.Parse(new byte[] { requestData[1], requestData[2], requestData[3], requestData[4] }).ToString();
                    ushort heartbeatPort = BitConverter.ToUInt16(requestData, 5);
                    string heartbeatType = Encoding.ASCII.GetString(requestData, 7, 3).Trim('\0');
                    
                    logger.Info("{0}Heartbeat from {1}:{2} ({3})", clientId, heartbeatIp, heartbeatPort, heartbeatType);

                    // Update server cache
                    string cacheKey = heartbeatIp + ":" + heartbeatPort;
                    if (!serverCache.ContainsKey(cacheKey))
                    {
                        serverCache[cacheKey] = new ServerInfo
                        {
                            Name = heartbeatType,
                            Port = heartbeatPort,
                            IpAddress = heartbeatIp,
                            Type = heartbeatType,
                            Load = 0,
                            Region = config["override_ip_country_region"]
                        };
                        logger.Debug("{0}Added server to cache: {1}", clientId, cacheKey);
                    }
                }

                // Return heartbeat acknowledgment
                byte[] response = new byte[] { 0x00 }; // Success
                logger.LogFunctionExit("DirectoryServer.HandleHeartbeat", response.Length);
                return response;
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.HandleHeartbeat", ex);
                return BuildDirectoryErrorResponse(0x03);
            }
        }

        /// <summary>
        /// Handle get by type request
        /// </summary>
        private byte[] HandleGetByTypeRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("DirectoryServer.HandleGetByTypeRequest");
            
            try
            {
                string serverType = "";
                if (requestData.Length >= 5)
                {
                    serverType = Encoding.ASCII.GetString(requestData, 1, 4).Trim('\0');
                }

                logger.Debug("{0}Get by type: {1}", clientId, serverType);

                // Filter servers by type
                List<ServerInfo> filteredServers = FilterServers(serverType, "");
                logger.Debug("{0}Returning {1} servers of type {2}", clientId, filteredServers.Count, serverType);

                byte[] response = BuildServerListResponse(filteredServers, isMaster);
                
                logger.LogFunctionExit("DirectoryServer.HandleGetByTypeRequest", response.Length);
                return response;
            }
            catch (Exception ex)
            {
                logger.LogException("DirectoryServer.HandleGetByTypeRequest", ex);
                return BuildDirectoryErrorResponse(0x02);
            }
        }

        /// <summary>
        /// Filter servers based on type and region
        /// </summary>
        private List<ServerInfo> FilterServers(string serverType, string region)
        {
            logger.LogFunctionEntry("DirectoryServer.FilterServers", serverType, region);
            
            List<ServerInfo> filtered = new List<ServerInfo>();

            foreach (ServerInfo server in directoryServers)
            {
                bool typeMatch = string.IsNullOrEmpty(serverType) || server.Type == serverType;
                bool regionMatch = string.IsNullOrEmpty(region) || server.Region == region;

                if (typeMatch && regionMatch)
                {
                    filtered.Add(server);
                }
            }

            logger.LogFunctionExit("DirectoryServer.FilterServers", filtered.Count);
            return filtered;
        }

        /// <summary>
        /// Build server list response
        /// </summary>
        private byte[] BuildServerListResponse(List<ServerInfo> servers, bool masterMode)
        {
            logger.LogFunctionEntry("DirectoryServer.BuildServerListResponse", servers.Count);
            
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Response header
                ms.WriteByte(0x00); // Success
                
                // Server count
                ms.WriteByte((byte)servers.Count);
                
                // Server entries
                foreach (ServerInfo server in servers)
                {
                    // Server info block
                    byte[] serverInfo = BuildServerInfoBlock(server);
                    ms.Write(serverInfo, 0, serverInfo.Length);
                }

                // Master flag
                ms.WriteByte((byte)(masterMode ? 1 : 0));

                // Peer server count
                ms.WriteByte((byte)peerServers.Count);

                // Peer server entries
                foreach (string peer in peerServers)
                {
                    string[] peerParts = peer.Split(':');
                    if (peerParts.Length == 2)
                    {
                        byte[] peerIp = IPAddress.Parse(peerParts[0]).GetAddressBytes();
                        ushort peerPort = ushort.Parse(peerParts[1]);
                        
                        ms.Write(peerIp, 0, 4);
                        byte[] portBytes = BitConverter.GetBytes(peerPort);
                        ms.Write(portBytes, 0, 2);
                    }
                }

                logger.Debug("Built server list response: {0} servers, {1} peers", servers.Count, peerServers.Count);
                logger.LogFunctionExit("DirectoryServer.BuildServerListResponse", ms.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build server info block
        /// </summary>
        private byte[] BuildServerInfoBlock(ServerInfo server)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Server name
                byte[] nameBytes = Encoding.ASCII.GetBytes(server.Name);
                ms.WriteByte((byte)nameBytes.Length);
                ms.Write(nameBytes, 0, nameBytes.Length);
                
                // Port
                byte[] portBytes = BitConverter.GetBytes((ushort)server.Port);
                ms.Write(portBytes, 0, 2);
                
                // IP address
                byte[] ipBytes = IPAddress.Parse(server.IpAddress).GetAddressBytes();
                ms.Write(ipBytes, 0, 4);
                
                // Server type
                byte[] typeBytes = Encoding.ASCII.GetBytes(server.Type);
                ms.WriteByte((byte)typeBytes.Length);
                ms.Write(typeBytes, 0, typeBytes.Length);
                
                // Load
                ms.WriteByte((byte)server.Load);
                
                // Region
                byte[] regionBytes = Encoding.ASCII.GetBytes(server.Region);
                ms.WriteByte((byte)regionBytes.Length);
                ms.Write(regionBytes, 0, regionBytes.Length);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build directory error response
        /// </summary>
        private byte[] BuildDirectoryErrorResponse(uint errorCode)
        {
            logger.LogFunctionEntry("DirectoryServer.BuildDirectoryErrorResponse", errorCode);
            
            byte[] response = new byte[] { (byte)errorCode };
            
            logger.LogFunctionExit("DirectoryServer.BuildDirectoryErrorResponse", response.Length);
            return response;
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

    /// <summary>
    /// Server information structure
    /// </summary>
    public class ServerInfo
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public string IpAddress { get; set; }
        public string Type { get; set; }
        public int Load { get; set; }
        public string Region { get; set; }
    }
}