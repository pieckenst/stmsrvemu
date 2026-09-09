using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam VAC1 (Valve Anti-Cheat V1) Server
    /// Handles anti-cheat verification and ban management
    /// </summary>
    public class VAC1Server : ServerBase
    {
        public VAC1Server(int port, Dictionary<string, string> config) 
            : base(port, config, "VAC1Server")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("VAC1Server request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildVACResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in VAC1Server: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildVACResponse()
        {
            // Return VAC status - not banned
            return new byte[] { 0x00, 0x00, 0x00, 0x00 }; // Not banned
        }
    }
}
