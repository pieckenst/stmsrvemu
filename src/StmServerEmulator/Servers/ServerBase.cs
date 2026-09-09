using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Base class for all Steam server implementations
    /// Provides common functionality for server lifecycle and network operations
    /// </summary>
    public abstract class ServerBase
    {
        protected TcpListener listener;
        protected Thread serverThread;
        protected bool isRunning;
        protected int port;
        protected Dictionary<string, string> config;
        protected Logger logger;
        protected string serverName;

        /// <summary>
        /// Get the server name
        /// </summary>
        public string ServerName
        {
            get { return serverName; }
        }

        /// <summary>
        /// Get the server port
        /// </summary>
        public int Port
        {
            get { return port; }
        }

        /// <summary>
        /// Get whether the server is running
        /// </summary>
        public bool IsRunning
        {
            get { return isRunning; }
        }

        /// <summary>
        /// Constructor for ServerBase
        /// </summary>
        protected ServerBase(int port, Dictionary<string, string> config, string serverName)
        {
            this.port = port;
            this.config = config;
            this.serverName = serverName;
            this.logger = Logger.GetLogger(serverName);
            this.isRunning = false;
        }

        /// <summary>
        /// Start the server
        /// </summary>
        public virtual void Start()
        {
            if (isRunning)
            {
                logger.Warning("Server is already running");
                return;
            }

            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
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

        /// <summary>
        /// Stop the server
        /// </summary>
        public virtual void Stop()
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;

            try
            {
                if (listener != null)
                {
                    listener.Stop();
                }

                if (serverThread != null && serverThread.IsAlive)
                {
                    serverThread.Join(1000); // Wait up to 1 second for thread to stop
                }

                logger.Info("{0} stopped", serverName);
            }
            catch (Exception ex)
            {
                logger.Error("Error stopping {0}: {1}", serverName, ex.Message);
            }
        }

        /// <summary>
        /// Main server loop - accepts connections and handles them
        /// </summary>
        protected virtual void ServerLoop()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    logger.DebugPlus("Accepted connection from {0}", 
                        ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());

                    // Handle client in a separate thread
                    Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClient));
                    clientThread.Name = serverName + "ClientThread";
                    clientThread.IsBackground = true;
                    clientThread.Start(client);
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

        /// <summary>
        /// Handle a client connection - must be implemented by derived classes
        /// </summary>
        protected abstract void HandleClient(object clientObj);

        /// <summary>
        /// Send data to a client
        /// </summary>
        protected void SendData(TcpClient client, byte[] data)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                logger.Error("Error sending data to client: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Receive data from a client
        /// </summary>
        protected byte[] ReceiveData(TcpClient client, int length)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[length];
                int bytesRead = 0;
                int totalRead = 0;

                while (totalRead < length)
                {
                    bytesRead = stream.Read(buffer, totalRead, length - totalRead);
                    if (bytesRead == 0)
                    {
                        throw new InvalidOperationException("Connection closed");
                    }
                    totalRead += bytesRead;
                }

                return buffer;
            }
            catch (Exception ex)
            {
                logger.Error("Error receiving data from client: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Receive data from a client with timeout
        /// </summary>
        protected byte[] ReceiveData(TcpClient client, int length, int timeoutMs)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                byte[] buffer = new byte[length];
                int bytesRead = 0;
                int totalRead = 0;

                while (totalRead < length)
                {
                    bytesRead = stream.Read(buffer, totalRead, length - totalRead);
                    if (bytesRead == 0)
                    {
                        throw new InvalidOperationException("Connection closed");
                    }
                    totalRead += bytesRead;
                }

                return buffer;
            }
            catch (Exception ex)
            {
                logger.Error("Error receiving data from client: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Close a client connection
        /// </summary>
        protected void CloseClient(TcpClient client)
        {
            try
            {
                if (client != null)
                {
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error closing client connection: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Dispose of server resources
        /// </summary>
        public virtual void Dispose()
        {
            Stop();
            
            if (listener != null)
            {
                listener = null;
            }

            if (serverThread != null)
            {
                serverThread = null;
            }
        }
    }
}
