using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Encryption;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Configuration Server
    /// Handles client configuration distribution and key exchange
    /// </summary>
    public class ConfigServer : ServerBase
    {
        /// <summary>
        /// Constructor for ConfigServer
        /// </summary>
        public ConfigServer(int port, Dictionary<string, string> config) 
            : base(port, config, "ConfigServer")
        {
        }

        /// <summary>
        /// Handle a client connection
        /// </summary>
        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            IPEndPoint clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string clientIp = clientEndPoint.Address.ToString();

            logger.Info("Config request from {0}", clientIp);

            try
            {
                NetworkStream stream = client.GetStream();
                
                // Read config request
                byte[] requestData = new byte[512];
                int bytesRead = stream.Read(requestData, 0, requestData.Length);
                
                // Process config request
                byte[] responseData = ProcessConfigRequest(requestData, bytesRead, clientIp);
                
                // Send response
                stream.Write(responseData, 0, responseData.Length);
                stream.Flush();
                
                logger.Debug("Config request completed for {0}", clientIp);
            }
            catch (Exception ex)
            {
                logger.Error("Error handling config request from {0}: {1}", clientIp, ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        /// <summary>
        /// Process config request
        /// </summary>
        private byte[] ProcessConfigRequest(byte[] requestData, int bytesRead, string clientIp)
        {
            // Check if this is a key exchange request
            if (bytesRead >= 4 && BitConverter.ToUInt32(requestData, 0) == 0x01)
            {
                // Key exchange request - send network key
                return BuildKeyExchangeResponse();
            }
            else
            {
                // Regular config request
                return BuildConfigResponse();
            }
        }

        /// <summary>
        /// Build key exchange response
        /// </summary>
        private byte[] BuildKeyExchangeResponse()
        {
            byte[] networkKeyReply = RSAKeyManager.NetworkKeyReply;
            
            if (networkKeyReply == null)
            {
                logger.Error("Network key reply not initialized");
                return new byte[0];
            }

            return networkKeyReply;
        }

        /// <summary>
        /// Build configuration response
        /// </summary>
        private byte[] BuildConfigResponse()
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Server IP
                string serverIp = config["server_ip"];
                byte[] ipBytes = System.Net.IPAddress.Parse(serverIp).GetAddressBytes();
                ms.Write(ipBytes, 0, 4);
                
                // Server ports
                ushort authPort = ushort.Parse(config["auth_server_port"]);
                ushort contentPort = ushort.Parse(config["content_server_port"]);
                
                byte[] authPortBytes = BitConverter.GetBytes(authPort);
                byte[] contentPortBytes = BitConverter.GetBytes(contentPort);
                
                ms.Write(authPortBytes, 0, 2);
                ms.Write(contentPortBytes, 0, 2);
                
                // Cell ID
                uint cellId = uint.Parse(config["cellid"]);
                byte[] cellIdBytes = BitConverter.GetBytes(cellId);
                ms.Write(cellIdBytes, 0, 4);

                return ms.ToArray();
            }
        }
    }
}
