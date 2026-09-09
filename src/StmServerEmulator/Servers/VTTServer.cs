using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam VTT (Valve Time Tracking) Server
    /// Handles time tracking and time synchronization
    /// </summary>
    public class VTTServer : ServerBase
    {
        public VTTServer(string port, Dictionary<string, string> config) 
            : base(int.Parse(port), config, "VTTServer")
        {
        }

        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            logger.Info("VTTServer request from {0}", 
                ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] response = BuildVTTResponse();
                stream.Write(response, 0, response.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error in VTTServer: {0}", ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private byte[] BuildVTTResponse()
        {
            // Return current server time
            long ticks = DateTime.UtcNow.Ticks;
            byte[] timeBytes = BitConverter.GetBytes(ticks);
            return timeBytes;
        }
    }
}
