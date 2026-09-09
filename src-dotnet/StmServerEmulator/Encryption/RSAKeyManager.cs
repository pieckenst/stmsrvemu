using System;
using System.IO;
using System.Security.Cryptography;
using StmServerEmulator.Config;

namespace StmServerEmulator.Encryption
{
    /// <summary>
    /// RSA key management for the Steam Server Emulator
    /// Handles RSA key generation, import/export, and signing operations
    /// </summary>
    public class RSAKeyManager
    {
        private static readonly string MainKeyPath = "files/configs/main_key_1024.der";
        private static readonly string NetworkKeyPath = "files/configs/network_key_512.der";
        
        private static readonly byte[] MainKeyKnownHash = new byte[]
        {
            0xbd, 0x3e, 0xa0, 0xf0, 0x3e, 0xf4, 0x00, 0x47, 0xfa, 0xb5, 0x88, 0xce, 0x14, 0xf1, 0x5e, 0xd7,
            0x79, 0x16, 0x27, 0x71, 0x94, 0x19, 0x66, 0x74, 0xb4, 0xa0, 0xd9, 0x0d, 0xa4, 0xc7, 0x99, 0x51
        };

        private static readonly byte[] NetworkKeyKnownHash = new byte[]
        {
            0xb7, 0x7d, 0xd5, 0xf5, 0x2f, 0xef, 0xc2, 0xbd, 0x05, 0x11, 0xbb, 0xb0, 0xe8, 0x8b, 0x7b, 0x7b,
            0x31, 0x0c, 0xf4, 0x25, 0x3f, 0x4d, 0xab, 0xd0, 0xd4, 0x93, 0x42, 0x3c, 0xcd, 0xce, 0x0d, 0x2c
        };

        private static RSACryptoServiceProvider mainKey;
        private static RSACryptoServiceProvider networkKey;
        private static byte[] berString;
        private static byte[] networkKeyReply;

        /// <summary>
        /// Get the main RSA key
        /// </summary>
        public static RSACryptoServiceProvider MainKey
        {
            get { return mainKey; }
        }

        /// <summary>
        /// Get the network RSA key
        /// </summary>
        public static RSACryptoServiceProvider NetworkKey
        {
            get { return networkKey; }
        }

        /// <summary>
        /// Get the precomputed network key reply
        /// </summary>
        public static byte[] NetworkKeyReply
        {
            get { return networkKeyReply; }
        }

        /// <summary>
        /// Check if the RSA key files exist and match known hashes
        /// </summary>
        public static bool CheckFileHashes()
        {
            var config = ConfigManager.GetConfig();
            bool useRandomKeys = config["use_random_keys"].ToLower() == "true";

            bool mainKeyExists = File.Exists(MainKeyPath);
            bool networkKeyExists = File.Exists(NetworkKeyPath);

            if (useRandomKeys)
            {
                if (mainKeyExists && networkKeyExists)
                {
                    byte[] mainKeyHash = ComputeFileHash(MainKeyPath);
                    byte[] networkKeyHash = ComputeFileHash(NetworkKeyPath);

                    if (HashesMatch(mainKeyHash, MainKeyKnownHash) && 
                        HashesMatch(networkKeyHash, NetworkKeyKnownHash))
                    {
                        // Hashes match known defaults - delete and regenerate
                        DeleteKeyFiles();
                        return false;
                    }
                    else
                    {
                        // Custom keys exist - use them
                        return true;
                    }
                }
                else
                {
                    // Missing files - delete any existing and regenerate
                    DeleteKeyFiles();
                    return false;
                }
            }
            else
            {
                if (mainKeyExists && networkKeyExists)
                {
                    byte[] mainKeyHash = ComputeFileHash(MainKeyPath);
                    byte[] networkKeyHash = ComputeFileHash(NetworkKeyPath);

                    if (HashesMatch(mainKeyHash, MainKeyKnownHash) && 
                        HashesMatch(networkKeyHash, NetworkKeyKnownHash))
                    {
                        // Known default keys exist - use them
                        return true;
                    }
                    else
                    {
                        // Unknown keys - delete and regenerate
                        DeleteKeyFiles();
                        return false;
                    }
                }
                else
                {
                    // Missing files - delete any existing and regenerate
                    DeleteKeyFiles();
                    return false;
                }
            }
        }

        /// <summary>
        /// Generate and export RSA keys
        /// </summary>
        public static void GenerateAndExportRsaKeys()
        {
            var config = ConfigManager.GetConfig();
            bool useRandomKeys = config["use_random_keys"].ToLower() == "false";

            if (useRandomKeys)
            {
                // Use keys from configuration
                mainKey = CreateRsaFromConfig(
                    config["main_key_n"],
                    config["main_key_e"],
                    1024
                );

                networkKey = CreateRsaFromConfig(
                    config["net_key_n"],
                    config["net_key_d"],
                    512
                );
            }
            else
            {
                // Generate random keys
                mainKey = new RSACryptoServiceProvider(2048);
                networkKey = new RSACryptoServiceProvider(1024);
            }

            // Ensure directory exists
            string directory = Path.GetDirectoryName(MainKeyPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Export keys to DER format
            byte[] mainKeyBytes = ExportRsaToDer(mainKey);
            byte[] networkKeyBytes = ExportRsaToDer(networkKey);

            File.WriteAllBytes(MainKeyPath, mainKeyBytes);
            File.WriteAllBytes(NetworkKeyPath, networkKeyBytes);
        }

        /// <summary>
        /// Import RSA keys from files
        /// </summary>
        public static void ImportRsaKeys()
        {
            if (!File.Exists(MainKeyPath) || !File.Exists(NetworkKeyPath))
            {
                throw new FileNotFoundException("RSA key files not found");
            }

            byte[] mainKeyBytes = File.ReadAllBytes(MainKeyPath);
            byte[] networkKeyBytes = File.ReadAllBytes(NetworkKeyPath);

            mainKey = ImportRsaFromDer(mainKeyBytes);
            networkKey = ImportRsaFromDer(networkKeyBytes);
        }

        /// <summary>
        /// Initialize the network key reply (precomputed for configserver)
        /// </summary>
        public static void InitNetworkKeyReply()
        {
            if (networkKey == null)
            {
                throw new InvalidOperationException("Network key not loaded");
            }

            // Get the network key modulus
            byte[] networkKeyN = networkKey.ExportParameters(false).Modulus;
            string networkKeyNHex = BitConverter.ToString(networkKeyN).Replace("-", "").ToLower();
            
            if (networkKeyNHex.Length % 2 != 0)
            {
                networkKeyNHex = "0" + networkKeyNHex;
            }

            byte[] networkKeyNBytes = HexStringToByteArray(networkKeyNHex);

            // Build BER/DER encoded public key structure
            byte[] berHeader = HexStringToByteArray("30819d300d06092a864886f70d010101050003818b0030818702818100");
            byte[] exponent = new byte[] { 0x02, 0x01, 0x11 };

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(berHeader, 0, berHeader.Length);
                ms.Write(networkKeyNBytes, 0, networkKeyNBytes.Length);
                ms.Write(exponent, 0, exponent.Length);
                berString = ms.ToArray();
            }

            // Sign the BER string with the main key
            byte[] signature = RsaSignMessage(mainKey, berString);

            // Build the complete reply
            using (MemoryStream ms = new MemoryStream())
            {
                // Write BER string length (big-endian short)
                byte[] berLen = BitConverter.GetBytes((ushort)berString.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(berLen);
                }
                ms.Write(berLen, 0, 2);

                ms.Write(berString, 0, berString.Length);

                // Write signature length (big-endian short)
                byte[] sigLen = BitConverter.GetBytes((ushort)signature.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(sigLen);
                }
                ms.Write(sigLen, 0, 2);

                ms.Write(signature, 0, signature.Length);
                networkKeyReply = ms.ToArray();
            }
        }

        /// <summary>
        /// Sign a message using RSA with SHA-1
        /// </summary>
        public static byte[] RsaSignMessage(RSACryptoServiceProvider rsaKey, byte[] message)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(message);
                return rsaKey.SignHash(hash, CryptoConfig.MapNameToOID("SHA1"));
            }
        }

        /// <summary>
        /// Generate a DLL signature (128-byte RSA signature)
        /// </summary>
        public static byte[] GenerateDllSignature(byte[] dataToSign)
        {
            if (networkKey == null)
            {
                throw new InvalidOperationException("Network key not loaded");
            }

            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(dataToSign);
                byte[] signature = networkKey.SignHash(hash, CryptoConfig.MapNameToOID("SHA1"));
                
                // Return first 128 bytes
                byte[] result = new byte[128];
                Array.Copy(signature, result, 128);
                return result;
            }
        }

        /// <summary>
        /// Compute SHA256 hash of a file
        /// </summary>
        private static byte[] ComputeFileHash(string filePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                using (FileStream fs = File.OpenRead(filePath))
                {
                    return sha256.ComputeHash(fs);
                }
            }
        }

        /// <summary>
        /// Check if two hash arrays match
        /// </summary>
        private static bool HashesMatch(byte[] hash1, byte[] hash2)
        {
            if (hash1.Length != hash2.Length)
            {
                return false;
            }

            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Delete RSA key files
        /// </summary>
        private static void DeleteKeyFiles()
        {
            if (File.Exists(MainKeyPath))
            {
                File.Delete(MainKeyPath);
            }
            if (File.Exists(NetworkKeyPath))
            {
                File.Delete(NetworkKeyPath);
            }
        }

        /// <summary>
        /// Create RSA key from configuration values
        /// </summary>
        private static RSACryptoServiceProvider CreateRsaFromConfig(string nHex, string eHex, int keySize)
        {
            byte[] n = HexStringToByteArray(nHex.StartsWith("0x") ? nHex.Substring(2) : nHex);
            byte[] e = HexStringToByteArray(eHex.StartsWith("0x") ? eHex.Substring(2) : eHex);

            RSAParameters rsaParams = new RSAParameters
            {
                Modulus = n,
                Exponent = e
            };

            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(keySize);
            rsa.ImportParameters(rsaParams);
            return rsa;
        }

        /// <summary>
        /// Export RSA key to DER format
        /// </summary>
        private static byte[] ExportRsaToDer(RSACryptoServiceProvider rsa)
        {
            return rsa.ExportCspBlob(true);
        }

        /// <summary>
        /// Import RSA key from DER format
        /// </summary>
        private static RSACryptoServiceProvider ImportRsaFromDer(byte[] derBytes)
        {
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
            rsa.ImportCspBlob(derBytes);
            return rsa;
        }

        /// <summary>
        /// Convert hex string to byte array
        /// </summary>
        private static byte[] HexStringToByteArray(string hex)
        {
            int numberChars = hex.Length;
            byte[] bytes = new byte[numberChars / 2];
            for (int i = 0; i < numberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }
    }
}
