using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System;

namespace StmServerEmulator.Encryption
{
    /// <summary>
    /// Encryption helper functions for the Steam Server Emulator
    /// Handles AES, RSA, hashing, and Steam-specific encryption operations
    /// </summary>
    public class EncryptionHelper
    {
        private static readonly byte[] DefaultIV = new byte[] 
        { 
            0x92, 0x18, 0x31, 0x29, 0x53, 0x42, 0x34, 0x23, 
            0x12, 0x31, 0x31, 0x21, 0x23, 0x12, 0x33, 0x53 
        };

        /// <summary>
        /// Encrypt data using AES with PKCS7 padding
        /// </summary>
        public static byte[] AesEncrypt(byte[] key, byte[] iv, byte[] message)
        {
            using (RijndaelManaged aes = new RijndaelManaged())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aes.CreateEncryptor();
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(message, 0, message.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Decrypt data using AES with PKCS7 padding
        /// </summary>
        public static byte[] AesDecrypt(byte[] key, byte[] iv, byte[] message)
        {
            using (RijndaelManaged aes = new RijndaelManaged())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform decryptor = aes.CreateDecryptor();
                using (MemoryStream ms = new MemoryStream(message))
                {
                    using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    {
                        using (MemoryStream output = new MemoryStream())
                        {
                            byte[] buffer = new byte[1024];
                            int bytesRead;
                            while ((bytesRead = cs.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, bytesRead);
                            }
                            return output.ToArray();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Encrypt data with padding for Steam protocol
        /// </summary>
        public static byte[] EncryptWithPad(byte[] key, byte[] iv, byte[] plaintext)
        {
            int padSize = 16 - (plaintext.Length % 16);
            byte[] paddedText = new byte[plaintext.Length + padSize];
            Array.Copy(plaintext, paddedText, plaintext.Length);
            
            // Add PKCS7 padding
            for (int i = plaintext.Length; i < paddedText.Length; i++)
            {
                paddedText[i] = (byte)padSize;
            }

            return AesEncrypt(key, iv, paddedText);
        }

        /// <summary>
        /// Encrypt message in Steam format (IV + length info + ciphertext)
        /// </summary>
        public static byte[] EncryptMessage(byte[] plaintext, byte[] key)
        {
            byte[] iv = DefaultIV;
            byte[] ciphertext = EncryptWithPad(key, iv, plaintext);

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(iv, 0, iv.Length);
                
                // Write plaintext length (big-endian short)
                byte[] ptextLen = BitConverter.GetBytes((ushort)plaintext.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(ptextLen);
                }
                ms.Write(ptextLen, 0, 2);

                // Write ciphertext length (big-endian short)
                byte[] ctextLen = BitConverter.GetBytes((ushort)ciphertext.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(ctextLen);
                }
                ms.Write(ctextLen, 0, 2);

                ms.Write(ciphertext, 0, ciphertext.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Decrypt message in Steam format
        /// </summary>
        public static byte[] DecryptMessage(byte[] message, byte[] key)
        {
            using (MemoryStream ms = new MemoryStream(message))
            {
                BinaryReader reader = new BinaryReader(ms);

                byte[] iv = reader.ReadBytes(16);
                ushort ptextSize = ReadBigEndianUInt16(reader);
                ushort ctextSize = ReadBigEndianUInt16(reader);
                byte[] ctext = reader.ReadBytes(ctextSize);

                byte[] decrypted = AesDecrypt(key, iv, ctext);
                
                // Remove padding
                byte[] result = new byte[ptextSize];
                Array.Copy(decrypted, result, ptextSize);
                
                return result;
            }
        }

        /// <summary>
        /// Decrypt message in Beta 1 format (v1)
        /// </summary>
        public static byte[] BetaDecryptMessageV1(byte[] message, byte[] key)
        {
            using (MemoryStream ms = new MemoryStream(message))
            {
                BinaryReader reader = new BinaryReader(ms);

                ushort ptextSize = reader.ReadUInt16(); // little-endian
                ushort ctextSize = reader.ReadUInt16(); // little-endian
                byte[] iv = reader.ReadBytes(16);
                byte[] ctext = reader.ReadBytes((int)ms.Length - 18);

                // Crop off misaligned data
                int alignedLength = ctext.Length & 0xFFFFFFF0;
                Array.Resize(ref ctext, alignedLength);

                if (ptextSize + 1 > ctext.Length)
                {
                    throw new InvalidOperationException("Badly misaligned data");
                }

                byte[] decrypted = AesDecrypt(key, iv, ctext);
                
                // Remove padding
                byte[] result = new byte[ptextSize];
                Array.Copy(decrypted, result, ptextSize);
                
                return result;
            }
        }

        /// <summary>
        /// Encrypt message in Beta 1 format
        /// </summary>
        public static byte[] BetaEncryptMessage(byte[] plaintext, byte[] key)
        {
            byte[] iv = DefaultIV;
            byte[] ciphertext = EncryptWithPad(key, iv, plaintext);

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(iv, 0, iv.Length);
                
                // Write plaintext length (big-endian short)
                byte[] ptextLen = BitConverter.GetBytes((ushort)plaintext.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(ptextLen);
                }
                ms.Write(ptextLen, 0, 2);

                // Write ciphertext length (big-endian short)
                byte[] ctextLen = BitConverter.GetBytes((ushort)ciphertext.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(ctextLen);
                }
                ms.Write(ctextLen, 0, 2);

                ms.Write(ciphertext, 0, ciphertext.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Perform binary XOR operation on two byte arrays
        /// </summary>
        public static byte[] BinaryXor(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                throw new ArgumentException("BinaryXor: array lengths don't match!");
            }

            byte[] result = new byte[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                result[i] = (byte)(a[i] ^ b[i]);
            }
            return result;
        }

        /// <summary>
        /// XOR data with a repeating 4-byte pattern
        /// </summary>
        public static byte[] XorData(byte[] data, uint xorValue)
        {
            byte[] xorBytes = BitConverter.GetBytes(xorValue);
            byte[] result = new byte[data.Length];
            
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ xorBytes[i % 4]);
            }
            return result;
        }

        /// <summary>
        /// Simple XOR encryption for peer packets
        /// </summary>
        public static byte[] EncryptBytes(byte[] message, byte[] password)
        {
            byte[] encrypted = new byte[message.Length];
            for (int i = 0; i < message.Length; i++)
            {
                encrypted[i] = (byte)(message[i] ^ password[i % password.Length]);
            }
            return encrypted;
        }

        /// <summary>
        /// Simple XOR decryption for peer packets
        /// </summary>
        public static byte[] DecryptBytes(byte[] encrypted, byte[] password)
        {
            byte[] decrypted = new byte[encrypted.Length];
            for (int i = 0; i < encrypted.Length; i++)
            {
                decrypted[i] = (byte)(encrypted[i] ^ password[i % password.Length]);
            }
            return decrypted;
        }

        /// <summary>
        /// Calculate CRC32 checksum of data
        /// </summary>
        public static byte[] CalculateCrc32Bytes(byte[] inputBytes)
        {
            uint crcHash = Crc32.Compute(inputBytes);
            return BitConverter.GetBytes(crcHash);
        }

        /// <summary>
        /// Calculate SHA1 hash of data
        /// </summary>
        public static byte[] CalculateSha1(byte[] data)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                return sha1.ComputeHash(data);
            }
        }

        /// <summary>
        /// Calculate SHA256 hash of data
        /// </summary>
        public static byte[] CalculateSha256(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data);
            }
        }

        /// <summary>
        /// Calculate HMAC of data
        /// </summary>
        public static byte[] CalculateHmac(byte[] key, byte[] data)
        {
            using (HMACSHA1 hmac = new HMACSHA1(key))
            {
                return hmac.ComputeHash(data);
            }
        }

        /// <summary>
        /// Validate MAC (Message Authentication Code)
        /// </summary>
        public static bool ValidateMac(byte[] message, byte[] key)
        {
            byte[] data = new byte[message.Length - 20];
            byte[] receivedMac = new byte[20];
            
            Array.Copy(message, 0, data, 0, data.Length);
            Array.Copy(message, data.Length, receivedMac, 0, 20);

            byte[] calculatedMac = CalculateHmac(key, data);
            
            if (calculatedMac.Length != receivedMac.Length)
            {
                return false;
            }

            for (int i = 0; i < calculatedMac.Length; i++)
            {
                if (calculatedMac[i] != receivedMac[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Read a big-endian 16-bit unsigned integer
        /// </summary>
        private static ushort ReadBigEndianUInt16(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt16(bytes, 0);
        }

        /// <summary>
        /// Derive key using PBKDF2 (simplified version of scrypt)
        /// </summary>
        public static byte[] DeriveKey(byte[] sharedSecret, byte[] salt)
        {
            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(
                sharedSecret, salt, 10000)) // iterations
            {
                return pbkdf2.GetBytes(32); // 32-byte key
            }
        }
    }

    /// <summary>
    /// CRC32 implementation for checksums
    /// </summary>
    public static class Crc32
    {
        private static readonly uint[] CrcTable = new uint[256];

        static Crc32()
        {
            // Initialize CRC table
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc = (crc >> 1) ^ 0xEDB88320;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
                CrcTable[i] = crc;
            }
        }

        public static uint Compute(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }
            return ~crc;
        }
    }
}
