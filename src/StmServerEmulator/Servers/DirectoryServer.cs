using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Directory Server
    /// Handles server discovery, load balancing, and server list management
    /// </summary>
    public class DirectoryServer : ServerBase
    {
        /// <summary>
        /// Constructor for DirectoryServer
        /// </summary>
        public DirectoryServer(int port, Dictionary<string, string> config) 
            : base(port, config, "DirectoryServer")
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

            logger.Info("Directory request from {0}", clientIp);

            try
            {
                NetworkStream stream = client.GetStream();
                
                // Read directory request
                byte[] requestData = new byte[1024];
                int bytesRead = stream.Read(requestData, 0, requestData.Length);
                
                // Process directory request
                byte[] responseData = ProcessDirectoryRequest(requestData, bytesRead, clientIp);
                
                // Send response
                stream.Write(responseData, 0, responseData.Length);
                stream.Flush();
                
                logger.Debug("Directory request completed for {0}", clientIp);
            }
            catch (Exception ex)
            {
                logger.Error("Error handling directory request from {0}: {1}", clientIp, ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        /// <summary>
        /// Process directory request
        /// </summary>
        private byte[] ProcessDirectoryRequest(byte[] requestData, int bytesRead, string clientIp)
        {
            // Simplified directory server implementation
            // In reality, this would return a list of available servers
            
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Build server list response
                string serverIp = config["server_ip"];
                string publicIp = config["public_ip"];
                
                // Add directory server info
                byte[] dirServerInfo = BuildServerInfo("DirectoryServer", int.Parse(config["dir_server_port"]), serverIp);
                ms.Write(dirServerInfo, 0, dirServerInfo.Length);
                
                // Add auth server info
                byte[] authServerInfo = BuildServerInfo("AuthServer", int.Parse(config["auth_server_port"]), serverIp);
                ms.Write(authServerInfo, 0, authServerInfo.Length);
                
                // Add content server info
                byte[] contentServerInfo = BuildServerInfo("ContentServer", int.Parse(config["content_server_port"]), serverIp);
                ms.Write(contentServerInfo, 0, contentServerInfo.Length);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build server information block
        /// </summary>
        private byte[] BuildServerInfo(string serverName, int port, string ip)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Server name
                byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(serverName);
                ms.WriteByte((byte)nameBytes.Length);
                ms.Write(nameBytes, 0, nameBytes.Length);
                
                // Port
                byte[] portBytes = BitConverter.GetBytes((ushort)port);
                ms.Write(portBytes, 0, 2);
                
                // IP address
                byte[] ipBytes = System.Net.IPAddress.Parse(ip).GetAddressBytes();
                ms.Write(ipBytes, 0, 4);
                
                return ms.ToArray();
            }
        }
    }
}
