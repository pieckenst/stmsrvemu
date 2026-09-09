using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Master Server
    /// Handles game server discovery and heartbeat management
    /// </summary>
    public class MasterServer : ServerBase
    {
        public MasterServer(int port, Dictionary<string, string> config) 
            : base(port, config, "MasterServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("MasterServer request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildMasterServerResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in MasterServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildMasterServerResponse()
        {
            // Simplified master server response
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Success
        }
    }
}
