using System;
using System.Net;
using System.Net.Sockets;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Servers;

namespace StmServerEmulator.Steam3
{
    /// <summary>
    /// Steam3 Connection Manager (CM) UDP Server
    /// Handles Steam3 protocol client connections via UDP
    /// </summary>
    public class CMServerUDP
    {
        private UdpClient udpClient;
        private Thread serverThread;
        private bool isRunning;
        private int port;
        private Dictionary<string, string> config;
        private Logger logger;
        private string serverName;

        public CMServerUDP(int port, Dictionary<string, string> config, object masterServer)
        {
            this.port = port;
            this.config = config;
            this.serverName = "CMServerUDP_" + port;
            this.logger = Logger.GetLogger(serverName);
            this.isRunning = false;
        }

        public void Start()
        {
            if (isRunning)
            {
                logger.Warning("Server is already running");
                return;
            }

            try
            {
                udpClient = new UdpClient(port);
                isRunning = true;

                serverThread = new Thread(new ThreadStart(ServerLoop));
                serverThread.Name = serverName + "Thread";
                serverThread.IsBackground = true;
                serverThread.Start();

                logger.Info("{0} started on port {1}", serverName, port);
            }
            catch (Exception ex)
            {
                logger.Error("Failed to start {0}: {1}", serverName, ex.Message);
                throw;
            }
        }

        public void Stop()
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;

            try
            {
                if (udpClient != null)
                {
                    udpClient.Close();
                }

                if (serverThread != null && serverThread.IsAlive)
                {
                    serverThread.Join(1000);
                }

                logger.Info("{0} stopped", serverName);
            }
            catch (Exception ex)
            {
                logger.Error("Error stopping {0}: {1}", serverName, ex.Message);
            }
        }

        private void ServerLoop()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (isRunning)
            {
                try
                {
                    byte[] receivedData = udpClient.Receive(ref remoteEndPoint);
                    logger.DebugPlus("Received UDP packet from {0}", remoteEndPoint.Address.ToString());

                    // Process the packet
                    byte[] responseData = ProcessUdpPacket(receivedData, remoteEndPoint);

                    // Send response
                    if (responseData != null)
                    {
                        udpClient.Send(responseData, responseData.Length, remoteEndPoint);
                    }
                }
                catch (SocketException ex)
                {
                    if (isRunning)
                    {
                        logger.Error("Socket error in {0}: {1}", serverName, ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        logger.Error("Error in {0} server loop: {1}", serverName, ex.Message);
                    }
                }
            }
        }

        private byte[] ProcessUdpPacket(byte[] data, IPEndPoint remoteEndPoint)
        {
            // Simplified UDP packet processing
            return new byte[] { 0x00, 0x00, 0x00, 0x01 }; // Success
        }
    }
}
