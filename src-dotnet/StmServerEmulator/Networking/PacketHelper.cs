using System;
using System.IO;
using System.Text;

namespace StmServerEmulator.Networking
{
    /// <summary>
    /// Packet helper functions for Steam protocol
    /// Handles packet construction, parsing, and Steam-specific data formats
    /// </summary>
    public class PacketHelper
    {
        /// <summary>
        /// Build a Steam packet header
        /// </summary>
        public static byte[] BuildPacketHeader(uint messageType, uint packetSize)
        {
            byte[] header = new byte[8];
            
            // Message type (4 bytes, little-endian)
            byte[] messageTypeBytes = BitConverter.GetBytes(messageType);
            Array.Copy(messageTypeBytes, 0, header, 0, 4);
            
            // Packet size (4 bytes, little-endian)
            byte[] packetSizeBytes = BitConverter.GetBytes(packetSize);
            Array.Copy(packetSizeBytes, 0, header, 4, 4);
            
            return header;
        }

        /// <summary>
        /// Parse a Steam packet header
        /// </summary>
        public static PacketHeader ParsePacketHeader(byte[] data)
        {
            if (data.Length < 8)
            {
                throw new ArgumentException("Data must be at least 8 bytes for packet header");
            }

            PacketHeader header = new PacketHeader();
            header.MessageType = BitConverter.ToUInt32(data, 0);
            header.PacketSize = BitConverter.ToUInt32(data, 4);
            
            return header;
        }

        /// <summary>
        /// Build a complete Steam packet
        /// </summary>
        public static byte[] BuildPacket(uint messageType, byte[] payload)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] header = BuildPacketHeader(messageType, (uint)payload.Length);
                ms.Write(header, 0, header.Length);
                ms.Write(payload, 0, payload.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Read a string from binary data (null-terminated)
        /// </summary>
        public static string ReadNullTerminatedString(byte[] data, int offset)
        {
            int end = offset;
            while (end < data.Length && data[end] != 0)
            {
                end++;
            }
            
            return Encoding.ASCII.GetString(data, offset, end - offset);
        }

        /// <summary>
        /// Write a null-terminated string to binary data
        /// </summary>
        public static byte[] WriteNullTerminatedString(string str)
        {
            byte[] strBytes = Encoding.ASCII.GetBytes(str);
            byte[] result = new byte[strBytes.Length + 1];
            Array.Copy(strBytes, 0, result, 0, strBytes.Length);
            result[strBytes.Length] = 0;
            return result;
        }

        /// <summary>
        /// Read a big-endian 16-bit unsigned integer
        /// </summary>
        public static ushort ReadBigEndianUInt16(byte[] data, int offset)
        {
            if (BitConverter.IsLittleEndian)
            {
                return (ushort)((data[offset] << 8) | data[offset + 1]);
            }
            else
            {
                return BitConverter.ToUInt16(data, offset);
            }
        }

        /// <summary>
        /// Write a big-endian 16-bit unsigned integer
        /// </summary>
        public static void WriteBigEndianUInt16(byte[] data, int offset, ushort value)
        {
            if (BitConverter.IsLittleEndian)
            {
                data[offset] = (byte)((value >> 8) & 0xFF);
                data[offset + 1] = (byte)(value & 0xFF);
            }
            else
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Copy(bytes, 0, data, offset, 2);
            }
        }

        /// <summary>
        /// Read a big-endian 32-bit unsigned integer
        /// </summary>
        public static uint ReadBigEndianUInt32(byte[] data, int offset)
        {
            if (BitConverter.IsLittleEndian)
            {
                return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | 
                              (data[offset + 2] << 8) | data[offset + 3]);
            }
            else
            {
                return BitConverter.ToUInt32(data, offset);
            }
        }

        /// <summary>
        /// Write a big-endian 32-bit unsigned integer
        /// </summary>
        public static void WriteBigEndianUInt32(byte[] data, int offset, uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                data[offset] = (byte)((value >> 24) & 0xFF);
                data[offset + 1] = (byte)((value >> 16) & 0xFF);
                data[offset + 2] = (byte)((value >> 8) & 0xFF);
                data[offset + 3] = (byte)(value & 0xFF);
            }
            else
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Copy(bytes, 0, data, offset, 4);
            }
        }

        /// <summary>
        /// Read a big-endian 64-bit unsigned integer
        /// </summary>
        public static ulong ReadBigEndianUInt64(byte[] data, int offset)
        {
            if (BitConverter.IsLittleEndian)
            {
                ulong result = 0;
                for (int i = 0; i < 8; i++)
                {
                    result = (result << 8) | data[offset + i];
                }
                return result;
            }
            else
            {
                return BitConverter.ToUInt64(data, offset);
            }
        }

        /// <summary>
        /// Write a big-endian 64-bit unsigned integer
        /// </summary>
        public static void WriteBigEndianUInt64(byte[] data, int offset, ulong value)
        {
            if (BitConverter.IsLittleEndian)
            {
                for (int i = 0; i < 8; i++)
                {
                    data[offset + i] = (byte)((value >> (56 - (i * 8))) & 0xFF);
                }
            }
            else
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Copy(bytes, 0, data, offset, 8);
            }
        }

        /// <summary>
        /// Convert Steam ID to 64-bit format
        /// </summary>
        public static ulong ConvertSteamIdTo64(uint steamId, uint universe = 1)
        {
            return ((ulong)universe << 56) | ((ulong)steamId & 0xFFFFFFFF);
        }

        /// <summary>
        /// Convert 64-bit Steam ID to 32-bit format
        /// </summary>
        public static uint ConvertSteamIdTo32(ulong steamId64)
        {
            return (uint)(steamId64 & 0xFFFFFFFF);
        }

        /// <summary>
        /// Validate Steam packet format
        /// </summary>
        public static bool ValidatePacket(byte[] data)
        {
            if (data == null || data.Length < 8)
            {
                return false;
            }

            uint packetSize = BitConverter.ToUInt32(data, 4);
            return packetSize == (uint)(data.Length - 8);
        }
    }

    /// <summary>
    /// Steam packet header structure
    /// </summary>
    public class PacketHeader
    {
        public uint MessageType { get; set; }
        public uint PacketSize { get; set; }
    }
}
