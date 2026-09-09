using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Content Server
    /// Handles content distribution, file downloads, and depot management
    /// </summary>
    public class ContentServer : ServerBase
    {
        private string storageDir;
        private string manifestDir;

        /// <summary>
        /// Constructor for ContentServer
        /// </summary>
        public ContentServer(int port, Dictionary<string, string> config) 
            : base(port, config, "ContentServer")
        {
            this.storageDir = config["storagedir"];
            this.manifestDir = config["manifestdir"];
        }

        /// <summary>
        /// Handle a client connection
        /// </summary>
        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            IPEndPoint clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string clientIp = clientEndPoint.Address.ToString();

            logger.Info("Content request from {0}", clientIp);

            try
            {
                NetworkStream stream = client.GetStream();
                
                // Read content request
                byte[] requestData = ReadContentRequest(stream);
                
                // Process content request
                byte[] responseData = ProcessContentRequest(requestData, clientIp);
                
                // Send response
                if (responseData != null && responseData.Length > 0)
                {
                    stream.Write(responseData, 0, responseData.Length);
                    stream.Flush();
                }
                
                logger.Debug("Content request completed for {0}", clientIp);
            }
            catch (Exception ex)
            {
                logger.Error("Error handling content request from {0}: {1}", clientIp, ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        /// <summary>
        /// Read content request from stream
        /// </summary>
        private byte[] ReadContentRequest(NetworkStream stream)
        {
            // Read request header
            byte[] header = new byte[8];
            int bytesRead = stream.Read(header, 0, 8);
            
            if (bytesRead < 8)
            {
                throw new InvalidOperationException("Incomplete content request header");
            }

            uint requestLength = BitConverter.ToUInt32(header, 0);
            
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
                }

                byte[] completeRequest = new byte[8 + requestLength];
                Array.Copy(header, 0, completeRequest, 0, 8);
                Array.Copy(body, 0, completeRequest, 8, (int)requestLength);
                return completeRequest;
            }

            return header;
        }

        /// <summary>
        /// Process content request
        /// </summary>
        private byte[] ProcessContentRequest(byte[] requestData, string clientIp)
        {
            uint requestType = BitConverter.ToUInt32(requestData, 4);
            
            switch (requestType)
            {
                case 1: // Manifest request
                    return HandleManifestRequest(requestData, clientIp);
                case 2: // Chunk request
                    return HandleChunkRequest(requestData, clientIp);
                case 3: // Depot request
                    return HandleDepotRequest(requestData, clientIp);
                default:
                    logger.Warning("Unknown content request type: {0}", requestType);
                    return BuildContentErrorResponse(0x01);
            }
        }

        /// <summary>
        /// Handle manifest request
        /// </summary>
        private byte[] HandleManifestRequest(byte[] requestData, string clientIp)
        {
            try
            {
                uint appId = BitConverter.ToUInt32(requestData, 8);
                uint manifestId = BitConverter.ToUInt32(requestData, 12);

                logger.Debug("Manifest request - AppID: {0}, ManifestID: {1}", appId, manifestId);

                // Try to load manifest file
                string manifestPath = Path.Combine(manifestDir, string.Format("{0}_{1}.manifest", appId, manifestId));
                
                if (File.Exists(manifestPath))
                {
                    byte[] manifestData = File.ReadAllBytes(manifestPath);
                    return BuildManifestResponse(manifestData);
                }
                else
                {
                    logger.Warning("Manifest not found: {0}", manifestPath);
                    return BuildContentErrorResponse(0x02); // File not found
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error processing manifest request: {0}", ex.Message);
                return BuildContentErrorResponse(0x03); // Internal error
            }
        }

        /// <summary>
        /// Handle chunk request
        /// </summary>
        private byte[] HandleChunkRequest(byte[] requestData, string clientIp)
        {
            try
            {
                uint chunkId = BitConverter.ToUInt32(requestData, 8);
                string chunkHash = BitConverter.ToString(requestData, 12, 20).Replace("-", "").ToLower();

                logger.Debug("Chunk request - ChunkID: {0}, Hash: {1}", chunkId, chunkHash);

                // Try to load chunk file
                string chunkPath = Path.Combine(storageDir, chunkHash + ".chunk");
                
                if (File.Exists(chunkPath))
                {
                    byte[] chunkData = File.ReadAllBytes(chunkPath);
                    return BuildChunkResponse(chunkData);
                }
                else
                {
                    logger.Warning("Chunk not found: {0}", chunkPath);
                    return BuildContentErrorResponse(0x02); // File not found
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error processing chunk request: {0}", ex.Message);
                return BuildContentErrorResponse(0x03); // Internal error
            }
        }

        /// <summary>
        /// Handle depot request
        /// </summary>
        private byte[] HandleDepotRequest(byte[] requestData, string clientIp)
        {
            try
            {
                uint depotId = BitConverter.ToUInt32(requestData, 8);

                logger.Debug("Depot request - DepotID: {0}", depotId);

                // Return depot information (simplified)
                byte[] depotInfo = BuildDepotInfo(depotId);
                return BuildDepotResponse(depotInfo);
            }
            catch (Exception ex)
            {
                logger.Error("Error processing depot request: {0}", ex.Message);
                return BuildContentErrorResponse(0x03); // Internal error
            }
        }

        /// <summary>
        /// Build depot information
        /// </summary>
        private byte[] BuildDepotInfo(uint depotId)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                // Depot ID
                byte[] depotIdBytes = BitConverter.GetBytes(depotId);
                ms.Write(depotIdBytes, 0, 4);

                // Depot name (simplified)
                string depotName = "Depot_" + depotId;
                byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(depotName);
                ms.WriteByte((byte)nameBytes.Length);
                ms.Write(nameBytes, 0, nameBytes.Length);

                // Available (always true in this implementation)
                ms.WriteByte(1);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build manifest response
        /// </summary>
        private byte[] BuildManifestResponse(byte[] manifestData)
        {
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

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build chunk response
        /// </summary>
        private byte[] BuildChunkResponse(byte[] chunkData)
        {
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

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build depot response
        /// </summary>
        private byte[] BuildDepotResponse(byte[] depotInfo)
        {
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

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build content error response
        /// </summary>
        private byte[] BuildContentErrorResponse(uint errorCode)
        {
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

            return response;
        }
    }
}
