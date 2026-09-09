using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Harvest Server
    /// Handles telemetry and statistics collection
    /// </summary>
    public class HarvestServer : ServerBase
    {
        public HarvestServer(int port, Dictionary<string, string> config) 
            : base(port, config, "HarvestServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("HarvestServer request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildHarvestResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in HarvestServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildHarvestResponse()
        {
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Success
        }
    }
}
