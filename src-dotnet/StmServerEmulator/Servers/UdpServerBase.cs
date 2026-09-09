using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Common lifecycle and packet capture implementation for datagram services.
    /// Each datagram is logged before dispatch and every response is logged before send.
    /// </summary>
    public abstract class UdpServerBase
    {
        protected UdpClient udpClient;
        protected Thread serverThread;
        protected bool isRunning;
        protected readonly int port;
        protected readonly Dictionary<string, string> config;
        protected readonly Logger logger;
        protected readonly string serverName;

        protected UdpServerBase(int port, Dictionary<string, string> config, string serverName)
        {
            if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
            {
                throw new ArgumentOutOfRangeException("port");
            }
            this.port = port;
            this.config = config;
            this.serverName = serverName;
            this.logger = Logger.GetLogger(serverName);
        }

        public bool IsRunning { get { return isRunning; } }
        public int Port { get { return port; } }

        public virtual void Start()
        {
            logger.LogFunctionEntry("UdpServerBase.Start", serverName, port);
            try
            {
                if (isRunning)
                {
                    logger.Warning("{0} is already running", serverName);
                    return;
                }
                udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                isRunning = true;
                serverThread = new Thread(new ThreadStart(ServerLoop));
                serverThread.IsBackground = true;
                serverThread.Name = serverName + "Thread";
                serverThread.Start();
                logger.Info("{0} started on UDP port {1}", serverName, port);
            }
            catch (Exception ex)
            {
                logger.LogException("UdpServerBase.Start", ex, serverName);
                isRunning = false;
                throw;
            }
            finally
            {
                logger.LogFunctionExit("UdpServerBase.Start", isRunning);
            }
        }

        public virtual void Stop()
        {
            logger.LogFunctionEntry("UdpServerBase.Stop", serverName);
            try
            {
                isRunning = false;
                if (udpClient != null)
                {
                    udpClient.Close();
                    udpClient = null;
                }
                if (serverThread != null && serverThread.IsAlive)
                {
                    serverThread.Join(1000);
                }
                logger.Info("{0} stopped", serverName);
            }
            catch (Exception ex)
            {
                logger.LogException("UdpServerBase.Stop", ex, serverName);
            }
            finally
            {
                logger.LogFunctionExit("UdpServerBase.Stop");
            }
        }

        private void ServerLoop()
        {
            logger.LogFunctionEntry("UdpServerBase.ServerLoop", serverName, port);
            try
            {
                while (isRunning)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    try
                    {
                        byte[] request = udpClient.Receive(ref remote);
                        logger.LogPacket(LogLevel.Debug, "RECV", request, "client=" + remote.ToString());
                        byte[] response = ProcessDatagram(request, remote);
                        if (response != null)
                        {
                            SendDatagram(response, remote);
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (isRunning)
                        {
                            logger.LogException("UdpServerBase.ServerLoop", ex, "socket receive");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogException("UdpServerBase.ServerLoop", ex, "packet dispatch");
                    }
                }
            }
            finally
            {
                logger.LogFunctionExit("UdpServerBase.ServerLoop");
            }
        }

        protected void SendDatagram(byte[] data, IPEndPoint remote)
        {
            logger.LogFunctionEntry("UdpServerBase.SendDatagram", data == null ? 0 : data.Length, remote == null ? "<null>" : remote.ToString());
            try
            {
                if (data == null)
                {
                    throw new ArgumentNullException("data");
                }
                if (remote == null)
                {
                    throw new ArgumentNullException("remote");
                }
                if (udpClient == null)
                {
                    throw new InvalidOperationException("UDP listener is not available");
                }
                logger.LogPacket(LogLevel.Debug, "SEND", data, "client=" + remote.ToString());
                udpClient.Send(data, data.Length, remote);
            }
            finally
            {
                logger.LogFunctionExit("UdpServerBase.SendDatagram");
            }
        }

        protected abstract byte[] ProcessDatagram(byte[] data, IPEndPoint remote);
    }
}
