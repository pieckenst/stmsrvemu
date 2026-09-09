using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam CSER (Client Stats & Error Reporting) Server
    /// Handles client statistics and error reporting
    /// </summary>
    public class CSERServer : ServerBase
    {
        public CSERServer(int port, Dictionary<string, string> config) 
            : base(port, config, "CSERServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("CSERServer request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildCSERResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in CSERServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildCSERResponse()
        {
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Success
        }
    }
}
