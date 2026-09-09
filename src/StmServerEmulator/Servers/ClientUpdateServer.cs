using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Client Update Server
    /// Handles client update checks and distribution
    /// </summary>
    public class ClientUpdateServer : ServerBase
    {
        public ClientUpdateServer(int port, Dictionary<string, string> config) 
            : base(port, config, "ClientUpdateServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("ClientUpdate request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildUpdateResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in ClientUpdateServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildUpdateResponse()
        {
            // Simplified update response - no updates available
            return new byte[] { 0x00, 0x00, 0x00, 0x00 }; // No update
        }
    }
}
