using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Networking;

namespace StmServerEmulator.Servers
{
    /// <summary>
    /// Steam Authentication Server
    /// Handles user authentication, session management, and ticket generation
    /// </summary>
    public class AuthServer : ServerBase
    {
        /// <summary>
        /// Constructor for AuthServer
        /// </summary>
        public AuthServer(int port, Dictionary<string, string> config) 
            : base(port, config, "AuthServer")
        {
        }

        /// <summary>
        /// Handle a client connection
        /// </summary>
        protected override void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            IPEndPoint clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string clientIp = clientEndPoint.Address.ToString();

            logger.Info("Authentication request from {0}", clientIp);

            try
            {
                NetworkStream stream = client.GetStream();
                
                // Read authentication request
                byte[] requestData = ReadAuthRequest(stream);
                
                // Process authentication
                byte[] responseData = ProcessAuthRequest(requestData, clientIp);
                
                // Send response
                stream.Write(responseData, 0, responseData.Length);
                stream.Flush();
                
                logger.Debug("Authentication completed for {0}", clientIp);
            }
            catch (Exception ex)
            {
                logger.Error("Error handling authentication request from {0}: {1}", clientIp, ex.Message);
            }
            finally
            {
                CloseClient(client);
            }
        }

        /// <summary>
        /// Read authentication request from stream
        /// </summary>
        private byte[] ReadAuthRequest(NetworkStream stream)
        {
            // Read the request header (4 bytes length + 4 bytes type)
            byte[] header = new byte[8];
            int bytesRead = stream.Read(header, 0, 8);
            
            if (bytesRead < 8)
            {
                throw new InvalidOperationException("Incomplete authentication request header");
            }

            uint requestLength = BitConverter.ToUInt32(header, 0);
            uint requestType = BitConverter.ToUInt32(header, 4);

            // Read the request body
            byte[] body = new byte[requestLength];
            bytesRead = 0;
            int totalRead = 0;

            while (totalRead < requestLength)
            {
                bytesRead = stream.Read(body, totalRead, (int)requestLength - totalRead);
                if (bytesRead == 0)
                {
                    throw new InvalidOperationException("Connection closed during request read");
                }
                totalRead += bytesRead;
            }

            // Combine header and body
            byte[] completeRequest = new byte[8 + requestLength];
            Array.Copy(header, 0, completeRequest, 0, 8);
            Array.Copy(body, 0, completeRequest, 8, (int)requestLength);

            return completeRequest;
        }

        /// <summary>
        /// Process authentication request
        /// </summary>
        private byte[] ProcessAuthRequest(byte[] requestData, string clientIp)
        {
            // Parse the request
            uint requestType = BitConverter.ToUInt32(requestData, 4);
            
            switch (requestType)
            {
                case 1: // Auth request
                    return HandleAuthRequest(requestData, clientIp);
                case 2: // Ticket request
                    return HandleTicketRequest(requestData, clientIp);
                case 3: // Logout request
                    return HandleLogoutRequest(requestData, clientIp);
                default:
                    logger.Warning("Unknown auth request type: {0}", requestType);
                    return BuildErrorResponse(0x01); // Unknown request type
            }
        }

        /// <summary>
        /// Handle authentication request
        /// </summary>
        private byte[] HandleAuthRequest(byte[] requestData, string clientIp)
        {
            try
            {
                // Parse username and password from request
                string username = ExtractUsername(requestData);
                string password = ExtractPassword(requestData);

                logger.Debug("Auth attempt - Username: {0}, IP: {1}", username, clientIp);

                // Validate credentials (simplified - in real implementation would check database)
                bool authSuccess = ValidateCredentials(username, password);

                if (authSuccess)
                {
                    // Generate authentication ticket
                    byte[] ticket = GenerateAuthTicket(username, clientIp);
                    return BuildAuthSuccessResponse(ticket);
                }
                else
                {
                    logger.Warning("Failed authentication attempt for user: {0}", username);
                    return BuildAuthFailureResponse();
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error processing auth request: {0}", ex.Message);
                return BuildErrorResponse(0x02); // Internal error
            }
        }

        /// <summary>
        /// Handle ticket request
        /// </summary>
        private byte[] HandleTicketRequest(byte[] requestData, string clientIp)
        {
            try
            {
                string username = ExtractUsername(requestData);
                byte[] ticket = GenerateAuthTicket(username, clientIp);
                return BuildTicketResponse(ticket);
            }
            catch (Exception ex)
            {
                logger.Error("Error processing ticket request: {0}", ex.Message);
                return BuildErrorResponse(0x02);
            }
        }

        /// <summary>
        /// Handle logout request
        /// </summary>
        private byte[] HandleLogoutRequest(byte[] requestData, string clientIp)
        {
            try
            {
                string username = ExtractUsername(requestData);
                logger.Info("Logout request for user: {0} from {1}", username, clientIp);
                
                // Invalidate session (simplified)
                return BuildLogoutResponse();
            }
            catch (Exception ex)
            {
                logger.Error("Error processing logout request: {0}", ex.Message);
                return BuildErrorResponse(0x02);
            }
        }

        /// <summary>
        /// Extract username from request data
        /// </summary>
        private string ExtractUsername(byte[] requestData)
        {
            // Skip header (8 bytes) and extract username
            int usernameOffset = 8;
            int usernameLength = requestData[usernameOffset];
            
            if (usernameLength == 0 || usernameOffset + 1 + usernameLength > requestData.Length)
            {
                return string.Empty;
            }

            return Encoding.ASCII.GetString(requestData, usernameOffset + 1, usernameLength);
        }

        /// <summary>
        /// Extract password from request data
        /// </summary>
        private string ExtractPassword(byte[] requestData)
        {
            // This is a simplified implementation
            // In reality, password would be encrypted/hashed
            int usernameOffset = 8;
            int usernameLength = requestData[usernameOffset];
            int passwordOffset = usernameOffset + 1 + usernameLength;
            
            if (passwordOffset >= requestData.Length)
            {
                return string.Empty;
            }

            int passwordLength = requestData[passwordOffset];
            
            if (passwordLength == 0 || passwordOffset + 1 + passwordLength > requestData.Length)
            {
                return string.Empty;
            }

            return Encoding.ASCII.GetString(requestData, passwordOffset + 1, passwordLength);
        }

        /// <summary>
        /// Validate user credentials
        /// </summary>
        private bool ValidateCredentials(string username, string password)
        {
            // This is a simplified implementation
            // In reality, this would check against a database
            // For now, accept any non-empty username/password
            return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        }

        /// <summary>
        /// Generate authentication ticket
        /// </summary>
        private byte[] GenerateAuthTicket(string username, string clientIp)
        {
            // Generate a simple ticket (in reality, this would be cryptographically secure)
            Random random = new Random();
            byte[] ticket = new byte[32];
            random.NextBytes(ticket);
            
            // Add timestamp
            byte[] timestamp = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
            Array.Copy(timestamp, 0, ticket, 0, 8);
            
            return ticket;
        }

        /// <summary>
        /// Build authentication success response
        /// </summary>
        private byte[] BuildAuthSuccessResponse(byte[] ticket)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                // Response header: length (4 bytes) + type (4 bytes) + result (4 bytes)
                uint responseLength = (uint)(12 + ticket.Length);
                uint responseType = 1; // Auth response
                uint result = 0; // Success

                byte[] lengthBytes = BitConverter.GetBytes(responseLength);
                byte[] typeBytes = BitConverter.GetBytes(responseType);
                byte[] resultBytes = BitConverter.GetBytes(result);

                ms.Write(lengthBytes, 0, 4);
                ms.Write(typeBytes, 0, 4);
                ms.Write(resultBytes, 0, 4);
                ms.Write(ticket, 0, ticket.Length);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build authentication failure response
        /// </summary>
        private byte[] BuildAuthFailureResponse()
        {
            byte[] response = new byte[12];
            
            uint responseLength = 12;
            uint responseType = 1; // Auth response
            uint result = 1; // Failure

            byte[] lengthBytes = BitConverter.GetBytes(responseLength);
            byte[] typeBytes = BitConverter.GetBytes(responseType);
            byte[] resultBytes = BitConverter.GetBytes(result);

            Array.Copy(lengthBytes, 0, response, 0, 4);
            Array.Copy(typeBytes, 0, response, 4, 4);
            Array.Copy(resultBytes, 0, response, 8, 4);

            return response;
        }

        /// <summary>
        /// Build ticket response
        /// </summary>
        private byte[] BuildTicketResponse(byte[] ticket)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                uint responseLength = (uint)(12 + ticket.Length);
                uint responseType = 2; // Ticket response
                uint result = 0; // Success

                byte[] lengthBytes = BitConverter.GetBytes(responseLength);
                byte[] typeBytes = BitConverter.GetBytes(responseType);
                byte[] resultBytes = BitConverter.GetBytes(result);

                ms.Write(lengthBytes, 0, 4);
                ms.Write(typeBytes, 0, 4);
                ms.Write(resultBytes, 0, 4);
                ms.Write(ticket, 0, ticket.Length);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build logout response
        /// </summary>
        private byte[] BuildLogoutResponse()
        {
            byte[] response = new byte[12];
            
            uint responseLength = 12;
            uint responseType = 3; // Logout response
            uint result = 0; // Success

            byte[] lengthBytes = BitConverter.GetBytes(responseLength);
            byte[] typeBytes = BitConverter.GetBytes(responseType);
            byte[] resultBytes = BitConverter.GetBytes(result);

            Array.Copy(lengthBytes, 0, response, 0, 4);
            Array.Copy(typeBytes, 0, response, 4, 4);
            Array.Copy(resultBytes, 0, response, 8, 4);

            return response;
        }

        /// <summary>
        /// Build error response
        /// </summary>
        private byte[] BuildErrorResponse(uint errorCode)
        {
            byte[] response = new byte[12];
            
            uint responseLength = 12;
            uint responseType = 0xFF; // Error response
            uint result = errorCode;

            byte[] lengthBytes = BitConverter.GetBytes(responseLength);
            byte[] typeBytes = BitConverter.GetBytes(responseType);
            byte[] resultBytes = BitConverter.GetBytes(result);

            Array.Copy(lengthBytes, 0, response, 0, 4);
            Array.Copy(typeBytes, 0, response, 4, 4);
            Array.Copy(resultBytes, 0, response, 8, 4);

            return response;
        }
    }
}
