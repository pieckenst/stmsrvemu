using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Encryption;
using StmServerEmulator.Utilities;
using StmServerEmulator.Servers;
using StmServerEmulator.Steam3;
using StmServerEmulator.Database;

namespace StmServerEmulator
{
    /// <summary>
    /// Main program entry point for the Steam Server Emulator
    /// </summary>
    class Program
    {
        private static Logger logger;
        private static Dictionary<string, string> config;
        private static bool shutdownRequested = false;

        static void Main(string[] args)
        {
            try
            {
                // Initialize logging
                LogLevel logLevel = LogLevel.Info;
                Logger.Initialize(logLevel, true, true, "logs");
                logger = Logger.GetLogger("Emulator");

                logger.Info("Steam 2002-2011 Server Emulator v{0}", GlobalState.LocalVer);
                logger.Info("Initializing...");

                // Load configuration
                ConfigManager.LoadConfig();
                config = ConfigManager.GetConfig();

                // Initialize global state
                GlobalState.Initialize();

                // Create required directories
                DirectoryHelper.CreateRequiredDirectories();

                // Check for conflicting configurations
                if (config["http_ip"] != "" && config["http_domainname"] != "")
                {
                    logger.Error("Detected conflicting configurations: 'http_ip' AND 'http_domainname' are set at the same time.");
                    logger.Error("Please comment one out in emulator.ini to run the server.");
                    return;
                }

                // Check and generate RSA keys
                if (!RSAKeyManager.CheckFileHashes())
                {
                    logger.Info("Generating RSA keys...");
                    RSAKeyManager.GenerateAndExportRsaKeys();
                }

                // Import RSA keys
                RSAKeyManager.ImportRsaKeys();
                RSAKeyManager.InitNetworkKeyReply();

                // Initialize database
                DatabaseManager.Initialize();

                // Start servers
                StartServers();

                // Set up console event handlers for graceful shutdown
                Console.CancelKeyPress += new ConsoleCancelEventHandler(OnConsoleCancel);
                
                logger.Info("Steam Server ready. Press Ctrl+C to shutdown.");

                // Keep the main thread alive
                while (!shutdownRequested)
                {
                    Thread.Sleep(1000);
                }

                // Shutdown
                ShutdownServers();
                DatabaseManager.Shutdown();
                Logger.Shutdown();

                logger.Info("Steam Server shutdown complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fatal error: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Start all Steam servers
        /// </summary>
        private static void StartServers()
        {
            logger.Info("Starting Steam servers...");

            try
            {
                // Directory Server
                DirectoryServer dirServer = new DirectoryServer(
                    int.Parse(config["dir_server_port"]), config);
                dirServer.Start();
                RegisterManagedServer(dirServer);
                logger.Info("Directory Server started on port {0}", config["dir_server_port"]);

                // CSER Server
                CSERServer cserServer = new CSERServer(
                    int.Parse(config["cser_server_port"]), config);
                cserServer.Start();
                RegisterManagedServer(cserServer);
                logger.Info("CSER Server started on port {0}", config["cser_server_port"]);

                // Content List Server
                ContentListServer contentListServer = new ContentListServer(
                    int.Parse(config["contentdir_server_port"]), config);
                contentListServer.Start();
                RegisterManagedServer(contentListServer);
                logger.Info("Content List Server started on port {0}", config["contentdir_server_port"]);

                // Content Server
                ContentServer contentServer = new ContentServer(
                    int.Parse(config["content_server_port"]), config);
                contentServer.Start();
                RegisterManagedServer(contentServer);
                logger.Info("Content Server started on port {0}", config["content_server_port"]);

                // Client Update Server
                ClientUpdateServer clientUpdateServer = new ClientUpdateServer(
                    int.Parse(config["clupd_server_port"]), config);
                clientUpdateServer.Start();
                RegisterManagedServer(clientUpdateServer);
                logger.Info("Client Update Server started on port {0}", config["clupd_server_port"]);

                // Config Server
                ConfigServer configServer = new ConfigServer(
                    int.Parse(config["config_server_port"]), config);
                configServer.Start();
                RegisterManagedServer(configServer);
                logger.Info("Config Server started on port {0}", config["config_server_port"]);

                // Auth Server
                AuthServer authServer = new AuthServer(
                    int.Parse(config["auth_server_port"]), config);
                authServer.Start();
                RegisterManagedServer(authServer);
                logger.Info("Auth Server started on port {0}", config["auth_server_port"]);

                // Validation Server
                ValidationServer validationServer = new ValidationServer(
                    int.Parse(config["validation_port"]), config);
                validationServer.Start();
                RegisterManagedServer(validationServer);
                logger.Info("Validation Server started on port {0}", config["validation_port"]);

                // Master Server
                MasterServer masterServer = new MasterServer(
                    int.Parse(config["masterhl1_server_port"]), config);
                masterServer.Start();
                RegisterManagedServer(masterServer);
                logger.Info("Master Server started on port {0}", config["masterhl1_server_port"]);

                // Harvest Server
                HarvestServer harvestServer = new HarvestServer(
                    int.Parse(config["harvest_server_port"]), config);
                harvestServer.Start();
                RegisterManagedServer(harvestServer);
                logger.Info("Harvest Server started on port {0}", config["harvest_server_port"]);

                // VTT Server
                VTTServer vttServer = new VTTServer(
                    config["vtt_server_port"], config);
                vttServer.Start();
                RegisterManagedServer(vttServer);
                logger.Info("VTT Server started on port {0}", config["vtt_server_port"]);

                // Cafe Server
                CafeServer cafeServer = new CafeServer(
                    config["cafe_server_port"], config);
                cafeServer.Start();
                RegisterManagedServer(cafeServer);
                logger.Info("Cafe Server started on port {0}", config["cafe_server_port"]);

                // Start Steam3 servers if enabled
                if (config["enable_steam3_servers"].ToLower() == "true")
                {
                    StartSteam3Servers(masterServer);
                }

                // Start additional servers if running all servers
                if (config["run_all_servers"].ToLower() == "true")
                {
                    StartAdditionalServers();
                }

                // Admin Server
                AdminServer adminServer = new AdminServer(
                    config["admin_server_port"], config);
                adminServer.Start();
                RegisterManagedServer(adminServer);
                logger.Info("Admin Server started on port {0}", config["admin_server_port"]);

                logger.Info("All servers started successfully");
            }
            catch (Exception ex)
            {
                logger.Error("Error starting servers: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Start Steam3 servers
        /// </summary>
        private static void StartSteam3Servers(object masterServer)
        {
            logger.Info("Starting Steam3 servers...");

            try
            {
                bool runAllServers = config["run_all_servers"].ToLower() == "true";

                if (runAllServers)
                {
                    // CM TCP 27014
                    CMServerTCP cmTcp27014 = new CMServerTCP(27014, config, masterServer);
                    cmTcp27014.Start();
                    RegisterManagedServer(cmTcp27014);
                    logger.Info("CM TCP Server started on port 27014");

                    // CM UDP 27014
                    CMServerUDP cmUdp27014 = new CMServerUDP(27014, config, masterServer);
                    cmUdp27014.Start();
                    RegisterManagedServer(cmUdp27014);
                    logger.Info("CM UDP Server started on port 27014");

                    // CM TCP 27017
                    CMServerTCP cmTcp27017 = new CMServerTCP(27017, config, masterServer);
                    cmTcp27017.Start();
                    RegisterManagedServer(cmTcp27017);
                    logger.Info("CM TCP Server started on port 27017");

                    // CM UDP 27017
                    CMServerUDP cmUdp27017 = new CMServerUDP(27017, config, masterServer);
                    cmUdp27017.Start();
                    RegisterManagedServer(cmUdp27017);
                    logger.Info("CM UDP Server started on port 27017");
                }
                else
                {
                    // Only start appropriate servers based on version
                    // For simplicity, we'll start 27017
                    CMServerTCP cmTcp27017 = new CMServerTCP(27017, config, masterServer);
                    cmTcp27017.Start();
                    RegisterManagedServer(cmTcp27017);
                    logger.Info("CM TCP Server started on port 27017");

                    CMServerUDP cmUdp27017 = new CMServerUDP(27017, config, masterServer);
                    cmUdp27017.Start();
                    RegisterManagedServer(cmUdp27017);
                    logger.Info("CM UDP Server started on port 27017");
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error starting Steam3 servers: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Start additional servers
        /// </summary>
        private static void StartAdditionalServers()
        {
            logger.Info("Starting additional servers...");

            try
            {
                // Tracker Server
                TrackerServer trackerServer = new TrackerServer(1200, config);
                trackerServer.Start();
                RegisterManagedServer(trackerServer);
                logger.Info("Tracker Server started on port 1200");

                // VAC1 Server
                VAC1Server vacServer = new VAC1Server(
                    int.Parse(config["vac_server_port"]), config);
                vacServer.Start();
                RegisterManagedServer(vacServer);
                logger.Info("VAC1 Server started on port {0}", config["vac_server_port"]);

                // Beta1 Auth Server
                BetaAuthServer beta1AuthServer = new BetaAuthServer(5273, config);
                beta1AuthServer.Start();
                RegisterManagedServer(beta1AuthServer);
                logger.Info("Beta1 Auth Server started on port 5273");

                logger.Info("Made by ymgve Modified by STMServer Team");
            }
            catch (Exception ex)
            {
                logger.Error("Error starting additional servers: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Shutdown all servers
        /// </summary>
        private static void ShutdownServers()
        {
            logger.Info("Shutting down servers...");

            // Stop all listener instances before joining their worker threads.  The old
            // rewrite only held a never-started DirectoryServer thread, leaving every
            // socket open during shutdown.
            for (int i = 0; i < GlobalState.Servers.Count; i++)
            {
                StopManagedServer(GlobalState.Servers[i]);
            }

            foreach (var kvp in GlobalState.ServerThreads)
            {
                try
                {
                    if (kvp.Value != null && kvp.Value.IsAlive)
                    {
                        logger.Info("Stopping {0}", kvp.Key);
                        kvp.Value.Join(1000);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("Error stopping {0}: {1}", kvp.Key, ex.Message);
                }
            }

            GlobalState.ServerThreads.Clear();
            GlobalState.Servers.Clear();
            logger.Info("All servers stopped");
        }

        private static void RegisterManagedServer(object server)
        {
            if (server == null)
            {
                throw new ArgumentNullException("server");
            }
            GlobalState.Servers.Add(server);
            logger.Debug("Registered managed server instance: {0}", server.GetType().FullName);
        }

        private static void StopManagedServer(object server)
        {
            logger.LogFunctionEntry("Program.StopManagedServer", server == null ? "<null>" : server.GetType().FullName);
            try
            {
                ServerBase tcpServer = server as ServerBase;
                if (tcpServer != null)
                {
                    tcpServer.Stop();
                    return;
                }
                UdpServerBase udpServer = server as UdpServerBase;
                if (udpServer != null)
                {
                    udpServer.Stop();
                    return;
                }
                CMServerUDP cmUdpServer = server as CMServerUDP;
                if (cmUdpServer != null)
                {
                    cmUdpServer.Stop();
                    return;
                }
                logger.Warning("No shutdown handler registered for {0}", server.GetType().FullName);
            }
            catch (Exception ex)
            {
                logger.LogException("Program.StopManagedServer", ex);
            }
            finally
            {
                logger.LogFunctionExit("Program.StopManagedServer");
            }
        }

        /// <summary>
        /// Handle console cancel event (Ctrl+C)
        /// </summary>
        private static void OnConsoleCancel(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            shutdownRequested = true;
            logger.Info("Shutdown requested");
        }
    }
}
