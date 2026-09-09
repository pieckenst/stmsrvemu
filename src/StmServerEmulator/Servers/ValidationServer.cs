using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Validation Server
    /// Handles user validation and account verification
    /// </summary>
    public class ValidationServer : ServerBase
    {
        public ValidationServer(int port, Dictionary<string, string> config) 
            : base(port, config, "ValidationServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("Validation request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildValidationResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in ValidationServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildValidationResponse()
        {
            // Simplified validation response - success
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Valid
        }
    }
}
