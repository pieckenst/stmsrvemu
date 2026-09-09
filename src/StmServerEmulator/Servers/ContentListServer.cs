using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Content List Server
    /// Handles content list requests and app metadata
    /// </summary>
    public class ContentListServer : ServerBase
    {
        public ContentListServer(int port, Dictionary<string, string> config) 
            : base(port, config, "ContentListServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("ContentList request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildContentListResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in ContentListServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildContentListResponse()
        {
            // Simplified content list response
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Success
        }
    }
}
