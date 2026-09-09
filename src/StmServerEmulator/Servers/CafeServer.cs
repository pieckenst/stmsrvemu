using System.Collections.Generic;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// The historical Cafe server is the VTT protocol on a separate endpoint.
    /// </summary>
    public sealed class CafeServer : VTTServer
    {
        public CafeServer(string port, Dictionary<string, string> config)
            : base(ParseCafePort(port), config, "CafeServer")
        {
            logger.LogFunctionEntry("CafeServer.ctor", port);
            logger.LogFunctionExit("CafeServer.ctor");
        }

        private static int ParseCafePort(string value)
        {
            int port;
            if (!System.Int32.TryParse(value, out port) || port < 1 || port > 65535)
            {
                throw new System.ArgumentException("Invalid cafe server port", "value");
            }
            return port;
        }
    }
}
