using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Cafe Server
    /// Handles cybercafe authentication and management
    /// </summary>
    public class CafeServer : ServerBase
    {
        public CafeServer(string port, Dictionary<string, string> config) 
            : base(int.Parse(port), config, "CafeServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("CafeServer request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildCafeResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in CafeServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildCafeResponse()
        {
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Success
        }
    }
}
