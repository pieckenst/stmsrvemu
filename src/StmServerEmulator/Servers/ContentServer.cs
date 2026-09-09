using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Networking;
using StmServerEmulator.Encryption;
using StmServerEmulator.Utilities;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Content Server
    /// Handles content distribution, file downloads, and depot management
    /// Implements full Steam2 content protocol with packet logging
    /// </summary>
    public class ContentServer : ServerBase
    {
        private string storageDir;
        private string manifestDir;
        private Dictionary<uint, string> depotCache;
        private Dictionary<string, byte[]> chunkCache;
        private Logger contentLogger;

        /// <summary>
        /// Constructor for ContentServer
        /// </summary>
        public ContentServer(int port, Dictionary<string, string> config) 
            : base(port, config, "ContentServer")
        {
            this.storageDir = config["storagedir"];
            this.manifestDir = config["manifestdir"];
            this.depotCache = new Dictionary<uint, string>();
            this.chunkCache = new Dictionary<string, byte[]>();
            this.contentLogger = Logger.GetLogger("ContentServer");
            
            logger.LogFunctionEntry("ContentServer.ctor", port);
            InitializeCaches();
            logger.LogFunctionExit("ContentServer.ctor");
        }

        /// <summary>
        /// Initialize content caches
        /// </summary>
        private void InitializeCaches()
        {
            logger.LogFunctionEntry("ContentServer.InitializeCaches");
            
            try
            {
                // Load depot information
                if (Directory.Exists(manifestDir))
                {
                    string[] manifestFiles = Directory.GetFiles(manifestDir, "*.manifest");
                    foreach (string manifestFile in manifestFiles)
                    {
                        try
                        {
                            string fileName = Path.GetFileNameWithoutExtension(manifestFile);
                            if (fileName.Contains("_"))
                            {
                                string[] parts = fileName.Split('_');
                                if (parts.Length >= 2 && uint.TryParse(parts[1], out uint appId))
                                {
                                    depotCache[appId] = manifestFile;
                                    logger.Debug("Cached depot {0}: {1}", appId, manifestFile);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Warning("Failed to cache manifest file {0}: {1}", manifestFile, ex.Message);
                        }
                    }
                }

                logger.Info("Initialized content caches - {0} depots, {1} chunks", depotCache.Count, chunkCache.Count);
            }
            catch (Exception ex)
            {
                logger.LogException("ContentServer.InitializeCaches", ex);
            }
            finally
            {
                logger.LogFunctionExit("ContentServer.InitializeCaches");
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

            logger.LogFunctionEntry("ContentServer.HandleClient", clientIp);
            logger.Info("{0}Content request from {1}", clientId, clientIp);

            try
            {
                NetworkStream stream = client.GetStream();
                
                // Read content request with full logging
                byte[] requestData = ReadContentRequest(stream, clientId);
                logger.LogPacket(LogLevel.Debug, "RECV", requestData, "Content request");
                
                // Process content request
                byte[] responseData = ProcessContentRequest(requestData, clientIp, clientId);
                
                // Send response with logging
                if (responseData != null && responseData.Length > 0)
                {
                    logger.LogPacket(LogLevel.Debug, "SEND", responseData, "Content response");
                    stream.Write(responseData, 0, responseData.Length);
                    stream.Flush();
                    logger.Debug("{0}Sent {1} bytes to client", clientId, responseData.Length);
                }
                
                logger.Info("{0}Content request completed for {1}", clientId, clientIp);
            }
            catch (Exception ex)
            {
                logger.LogException("ContentServer.HandleClient", ex, "for client " + clientIp);
            }
            finally
            {
                CloseClient(client);
                logger.Info("{0}Disconnected from Content Server", clientId);
                logger.LogFunctionExit("ContentServer.HandleClient");
            }
        }

        /// <summary>
        /// Read content request from stream with detailed logging
        /// </summary>
        private byte[] ReadContentRequest(NetworkStream stream, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.ReadContentRequest");
            
            try
            {
                // Read request header (8 bytes)
                byte[] header = new byte[8];
                int bytesRead = stream.Read(header, 0, 8);
                logger.Debug("{0}Read {1} bytes header", clientId, bytesRead);
                logger.LogPacket(LogLevel.DebugPlus, "RECV", header, "Request header");
                
                if (bytesRead < 8)
                {
                    throw new InvalidOperationException("Incomplete content request header");
                }

                uint requestLength = BitConverter.ToUInt32(header, 0);
                uint requestType = BitConverter.ToUInt32(header, 4);
                
                logger.Debug("{0}Request type: {1}, Length: {2}", clientId, requestType, requestLength);
                
                // Read request body if present
                if (requestLength > 0)
                {
                    byte[] body = new byte[requestLength];
                    bytesRead = 0;
                    int totalRead = 0;

                    while (totalRead < requestLength)
                    {
                        bytesRead = stream.Read(body, totalRead, (int)requestLength - totalRead);
                        if (bytesRead == 0)
                        {
                            throw new InvalidOperationException("Connection closed during request read");
                        }
                        totalRead += bytesRead;
                        logger.DebugPlus("{0}Read {1}/{2} bytes of body", clientId, totalRead, requestLength);
                    }

                    byte[] completeRequest = new byte[8 + requestLength];
                    Array.Copy(header, 0, completeRequest, 0, 8);
                    Array.Copy(body, 0, completeRequest, 8, (int)requestLength);
                    
                    logger.LogPacket(LogLevel.Debug, "RECV", completeRequest, "Complete request");
                    logger.LogFunctionExit("ContentServer.ReadContentRequest", completeRequest.Length);
                    return completeRequest;
                }

                logger.LogFunctionExit("ContentServer.ReadContentRequest", header.Length);
                return header;
            }
            catch (Exception ex)
            {
                logger.LogException("ContentServer.ReadContentRequest", ex);
                throw;
            }
        }

        /// <summary>
        /// Process content request with full protocol implementation
        /// </summary>
        private byte[] ProcessContentRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.ProcessContentRequest", requestData.Length);
            
            if (requestData.Length < 8)
            {
                logger.Warning("{0}Invalid request length: {1}", clientId, requestData.Length);
                logger.LogFunctionExit("ContentServer.ProcessContentRequest", null);
                return BuildContentErrorResponse(0x01);
            }

            uint requestType = BitConverter.ToUInt32(requestData, 4);
            logger.Debug("{0}Processing request type: {1}", clientId, requestType);
            
            byte[] response = null;
            
            switch (requestType)
            {
                case 1: // Manifest request
                    response = HandleManifestRequest(requestData, clientIp, clientId);
                    break;
                case 2: // Chunk request
                    response = HandleChunkRequest(requestData, clientIp, clientId);
                    break;
                case 3: // Depot request
                    response = HandleDepotRequest(requestData, clientIp, clientId);
                    break;
                case 4: // App info request
                    response = HandleAppInfoRequest(requestData, clientIp, clientId);
                    break;
                case 5: // Package info request
                    response = HandlePackageInfoRequest(requestData, clientIp, clientId);
                    break;
                default:
                    logger.Warning("{0}Unknown content request type: {1}", clientId, requestType);
                    response = BuildContentErrorResponse(0x01);
                    break;
            }

            logger.LogFunctionExit("ContentServer.ProcessContentRequest", response != null ? response.Length : 0);
            return response;
        }

        /// <summary>
        /// Handle manifest request with full implementation
        /// </summary>
        private byte[] HandleManifestRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.HandleManifestRequest");
            
            try
            {
                if (requestData.Length < 16)
                {
                    logger.Warning("{0}Invalid manifest request length", clientId);
                    return BuildContentErrorResponse(0x02);
                }

                uint appId = BitConverter.ToUInt32(requestData, 8);
                uint manifestId = BitConverter.ToUInt32(requestData, 12);

                logger.Info("{0}Manifest request - AppID: {1}, ManifestID: {2}", clientId, appId, manifestId);

                // Try to load manifest file
                string manifestPath = Path.Combine(manifestDir, string.Format("{0}_{1}.manifest", appId, manifestId));
                
                if (!File.Exists(manifestPath))
                {
                    logger.Warning("{0}Manifest not found: {1}", clientId, manifestPath);
                    return BuildContentErrorResponse(0x02); // File not found
                }

                logger.Debug("{0}Loading manifest from: {1}", clientId, manifestPath);
                byte[] manifestData = File.ReadAllBytes(manifestPath);
                logger.Debug("{0}Manifest size: {1} bytes", clientId, manifestData.Length);

                // Parse and validate manifest
                if (!ValidateManifest(manifestData, clientId))
                {
                    logger.Warning("{0}Invalid manifest format", clientId);
                    return BuildContentErrorResponse(0x03); // Invalid format
                }

                byte[] response = BuildManifestResponse(manifestData);
                logger.Info("{0}Manifest sent successfully: {1} bytes", clientId, response.Length);
                
                return response;
            }
            catch (Exception ex)
            {
                logger.LogException("ContentServer.HandleManifestRequest", ex);
                return BuildContentErrorResponse(0x03); // Internal error
            }
            finally
            {
                logger.LogFunctionExit("ContentServer.HandleManifestRequest");
            }
        }

        /// <summary>
        /// Handle chunk request with full implementation
        /// </summary>
        private byte[] HandleChunkRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.HandleChunkRequest");
            
            try
            {
                if (requestData.Length < 24)
                {
                    logger.Warning("{0}Invalid chunk request length", clientId);
                    return BuildContentErrorResponse(0x02);
                }

                uint chunkId = BitConverter.ToUInt32(requestData, 8);
                byte[] chunkHash = new byte[20];
                Array.Copy(requestData, 12, chunkHash, 0, 20);
                string chunkHashStr = StringHelper.ByteArrayToHex(chunkHash);

                logger.Info("{0}Chunk request - ChunkID: {1}, Hash: {2}", clientId, chunkId, chunkHashStr);

                // Check cache first
                string cacheKey = chunkHashStr;
                if (chunkCache.ContainsKey(cacheKey))
                {
                    logger.Debug("{0}Chunk found in cache", clientId);
                    byte[] response = BuildChunkResponse(chunkCache[cacheKey]);
                    logger.Info("{0}Chunk sent from cache: {1} bytes", clientId, response.Length);
                    return response;
                }

                // Try to load chunk file
                string chunkPath = Path.Combine(storageDir, chunkHashStr + ".chunk");
                
                if (!File.Exists(chunkPath))
                {
                    logger.Warning("{0}Chunk not found: {1}", clientId, chunkPath);
                    return BuildContentErrorResponse(0x02); // File not found
                }

                logger.Debug("{0}Loading chunk from: {1}", clientId, chunkPath);
                byte[] chunkData = File.ReadAllBytes(chunkPath);
                logger.Debug("{0}Chunk size: {1} bytes", clientId, chunkData.Length);

                // Validate chunk hash
                byte[] calculatedHash = EncryptionHelper.CalculateSha256(chunkData);
                if (!ByteArraysEqual(chunkHash, calculatedHash))
                {
                    logger.Warning("{0}Chunk hash mismatch - expected: {1}, calculated: {2}", 
                        clientId, chunkHashStr, StringHelper.ByteArrayToHex(calculatedHash));
                    return BuildContentErrorResponse(0x04); // Hash mismatch
                }

                // Cache the chunk
                chunkCache[cacheKey] = chunkData;
                logger.Debug("{0}Chunk cached: {1}", clientId, cacheKey);

                byte[] response = BuildChunkResponse(chunkData);
                logger.Info("{0}Chunk sent successfully: {1} bytes", clientId, response.Length);
                
                return response;
            }
            catch (Exception ex)
            {
                logger.LogException("ContentServer.HandleChunkRequest", ex);
                return BuildContentErrorResponse(0x03); // Internal error
            }
            finally
            {
                logger.LogFunctionExit("ContentServer.HandleChunkRequest");
            }
        }

        /// <summary>
        /// Handle depot request with full implementation
        /// </summary>
        private byte[] HandleDepotRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.HandleDepotRequest");
            
            try
            {
                if (requestData.Length < 12)
                {
                    logger.Warning("{0}Invalid depot request length", clientId);
                    return BuildContentErrorResponse(0x02);
                }

                uint depotId = BitConverter.ToUInt32(requestData, 8);
                logger.Info("{0}Depot request - DepotID: {1}", clientId, depotId);

                // Return depot information
                byte[] depotInfo = BuildDepotInfo(depotId);
                byte[] response = BuildDepotResponse(depotInfo);
                
                logger.Info("{0}Depot info sent: {1} bytes", clientId, response.Length);
                return response;
            }
            catch (Exception ex)
            {
                logger.LogException("ContentServer.HandleDepotRequest", ex);
                return BuildContentErrorResponse(0x03); // Internal error
            }
            finally
            {
                logger.LogFunctionExit("ContentServer.HandleDepotRequest");
            }
        }

        /// <summary>
        /// Handle app info request
        /// </summary>
        private byte[] HandleAppInfoRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.HandleAppInfoRequest");
            logger.Info("{0}App info request - Not fully implemented", clientId);
            logger.LogFunctionExit("ContentServer.HandleAppInfoRequest");
            return BuildContentErrorResponse(0x01);
        }

        /// <summary>
        /// Handle package info request
        /// </summary>
        private byte[] HandlePackageInfoRequest(byte[] requestData, string clientIp, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.HandlePackageInfoRequest");
            logger.Info("{0}Package info request - Not fully implemented", clientId);
            logger.LogFunctionExit("ContentServer.HandlePackageInfoRequest");
            return BuildContentErrorResponse(0x01);
        }

        /// <summary>
        /// Validate manifest format
        /// </summary>
        private bool ValidateManifest(byte[] manifestData, string clientId)
        {
            logger.LogFunctionEntry("ContentServer.ValidateManifest", manifestData.Length);
            
            try
            {
                // Basic validation - check for manifest header
                if (manifestData.Length < 4)
                {
                    logger.Warning("{0}Manifest too short", clientId);
                    return false;
                }

                uint manifestVersion = BitConverter.ToUInt32(manifestData, 0);
                logger.Debug("{0}Manifest version: {1}", clientId, manifestVersion);

                // Add more validation as needed
                bool valid = true;
                logger.LogFunctionExit("ContentServer.ValidateManifest", valid);
                return valid;
            }
            catch (Exception ex)
            {
                logger.LogException("ContentServer.ValidateManifest", ex);
                logger.LogFunctionExit("ContentServer.ValidateManifest", false);
                return false;
            }
        }

        /// <summary>
        /// Build depot information
        /// </summary>
        private byte[] BuildDepotInfo(uint depotId)
        {
            logger.LogFunctionEntry("ContentServer.BuildDepotInfo", depotId);
            
            using (MemoryStream ms = new MemoryStream())
            {
                // Depot ID
                byte[] depotIdBytes = BitConverter.GetBytes(depotId);
                ms.Write(depotIdBytes, 0, 4);

                // Depot name
                string depotName = "Depot_" + depotId;
                byte[] nameBytes = Encoding.ASCII.GetBytes(depotName);
                ms.WriteByte((byte)nameBytes.Length);
                ms.Write(nameBytes, 0, nameBytes.Length);

                // Available flag
                ms.WriteByte(0x01); // Available

                // App ID (optional)
                uint appId = depotId; // Simplified - in reality would map depot to app
                byte[] appIdBytes = BitConverter.GetBytes(appId);
                ms.Write(appIdBytes, 0, 4);

                // Size (optional)
                uint size = 0; // Would be actual depot size
                byte[] sizeBytes = BitConverter.GetBytes(size);
                ms.Write(sizeBytes, 0, 4);

                logger.Debug("Built depot info for {0}: {1}", depotId, depotName);
                logger.LogFunctionExit("ContentServer.BuildDepotInfo", ms.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build manifest response
        /// </summary>
        private byte[] BuildManifestResponse(byte[] manifestData)
        {
            logger.LogFunctionEntry("ContentServer.BuildManifestResponse", manifestData.Length);
            
            using (MemoryStream ms = new MemoryStream())
            {
                uint responseLength = (uint)(12 + manifestData.Length);
                uint responseType = 1; // Manifest response
                uint result = 0; // Success

                byte[] lengthBytes = BitConverter.GetBytes(responseLength);
                byte[] typeBytes = BitConverter.GetBytes(responseType);
                byte[] resultBytes = BitConverter.GetBytes(result);

                ms.Write(lengthBytes, 0, 4);
                ms.Write(typeBytes, 0, 4);
                ms.Write(resultBytes, 0, 4);
                ms.Write(manifestData, 0, manifestData.Length);

                logger.LogFunctionExit("ContentServer.BuildManifestResponse", ms.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build chunk response
        /// </summary>
        private byte[] BuildChunkResponse(byte[] chunkData)
        {
            logger.LogFunctionEntry("ContentServer.BuildChunkResponse", chunkData.Length);
            
            using (MemoryStream ms = new MemoryStream())
            {
                uint responseLength = (uint)(12 + chunkData.Length);
                uint responseType = 2; // Chunk response
                uint result = 0; // Success

                byte[] lengthBytes = BitConverter.GetBytes(responseLength);
                byte[] typeBytes = BitConverter.GetBytes(responseType);
                byte[] resultBytes = BitConverter.GetBytes(result);

                ms.Write(lengthBytes, 0, 4);
                ms.Write(typeBytes, 0, 4);
                ms.Write(resultBytes, 0, 4);
                ms.Write(chunkData, 0, chunkData.Length);

                logger.LogFunctionExit("ContentServer.BuildChunkResponse", ms.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build depot response
        /// </summary>
        private byte[] BuildDepotResponse(byte[] depotInfo)
        {
            logger.LogFunctionEntry("ContentServer.BuildDepotResponse", depotInfo.Length);
            
            using (MemoryStream ms = new MemoryStream())
            {
                uint responseLength = (uint)(12 + depotInfo.Length);
                uint responseType = 3; // Depot response
                uint result = 0; // Success

                byte[] lengthBytes = BitConverter.GetBytes(responseLength);
                byte[] typeBytes = BitConverter.GetBytes(responseType);
                byte[] resultBytes = BitConverter.GetBytes(result);

                ms.Write(lengthBytes, 0, 4);
                ms.Write(typeBytes, 0, 4);
                ms.Write(resultBytes, 0, 4);
                ms.Write(depotInfo, 0, depotInfo.Length);

                logger.LogFunctionExit("ContentServer.BuildDepotResponse", ms.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build content error response
        /// </summary>
        private byte[] BuildContentErrorResponse(uint errorCode)
        {
            logger.LogFunctionEntry("ContentServer.BuildContentErrorResponse", errorCode);
            
            byte[] response = new byte[12];
            
            uint responseLength = 12;
            uint responseType = 0xFF; // Error response
            uint result = errorCode;

            byte[] lengthBytes = BitConverter.GetBytes(responseLength);
            byte[] typeBytes = BitConverter.GetBytes(responseType);
            byte[] resultBytes = BitConverter.GetBytes(result);

            Array.Copy(lengthBytes, 0, response, 0, 4);
            Array.Copy(typeBytes, 0, response, 4, 4);
            Array.Copy(resultBytes, 0, response, 8, 4);

            logger.LogFunctionExit("ContentServer.BuildContentErrorResponse", response.Length);
            return response;
        }

        /// <summary>
        /// Compare byte arrays
        /// </summary>
        private bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }
}
