using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Servers;

namespace StmServerEmulator.Steam3
{
    /// <summary>
    /// Steam3 Connection Manager (CM) TCP Server
    /// Handles Steam3 protocol client connections via TCP
    /// </summary>
    public class CMServerTCP : ServerBase
    {
        public CMServerTCP(int port, Dictionary<string, string> config, object masterServer) 
            : base(port, config, "CMServerTCP_" + port)
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("CM TCP request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildCMResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in CMServerTCP: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildCMResponse()
        {
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Success
        }
    }
}
