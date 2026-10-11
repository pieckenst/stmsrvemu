using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StmServerEmulator.Encryption;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    internal sealed class TrackerPacket
    {
        internal bool IsBeta; internal bool IsEncrypted; internal uint ClientId; internal uint SessionId; internal uint Sequence; internal uint Acknowledgement; internal byte PacketNumber; internal byte TotalPackets; internal ushort Command; internal Dictionary<string, TrackerValue> Values;
    }
    internal sealed class TrackerValue { internal byte Type; internal byte[] Data; }

    internal static class TrackerProtocol
    {
        private static readonly IceKey Ice = new IceKey(1, new byte[] { 13,85,243,211,173,6,87,71 });
        private static readonly Logger Logger = Logger.GetLogger("TrackerProtocol");

        internal static TrackerPacket Parse(byte[] wire)
        {
            Logger.LogFunctionEntry("TrackerProtocol.Parse", wire == null ? 0 : wire.Length);
            try
            {
                if (wire == null || wire.Length == 0) throw new ArgumentException("Tracker datagram is empty", "wire");
                if (wire.Length >= 2 && wire[0] == 0x04 && wire[1] == 0x16) return ParseBeta(wire, false);
                bool beta = wire.Length >= 4 && wire[0] == 0xfe && wire[1] == 0xff && wire[2] == 0xff && wire[3] == 0xff;
                int encryptedOffset = beta ? 4 : 1;
                if (wire.Length <= encryptedOffset) throw new InvalidDataException("Tracker datagram has no encrypted body");
                byte[] encrypted = Slice(wire, encryptedOffset, wire.Length - encryptedOffset);
                byte[] body = Ice.Decrypt(encrypted);
                if (beta) return ParseBeta(body, true);
                return ParseRetail(body, true);
            }
            finally { Logger.LogFunctionExit("TrackerProtocol.Parse"); }
        }

        internal static byte[] Build(TrackerPacket packet, bool acknowledgement)
        {
            Logger.LogFunctionEntry("TrackerProtocol.Build", packet == null ? 0 : packet.Command, acknowledgement);
            try
            {
                if (packet == null) throw new ArgumentNullException("packet");
                byte[] keyValues = SerializeValues(packet.Values);
                MemoryStream plain = new MemoryStream();
                if (packet.IsBeta)
                {
                    WriteUInt32(plain, packet.ClientId); WriteUInt32(plain, packet.SessionId); WriteUInt32(plain, packet.Sequence); WriteUInt32(plain, packet.Acknowledgement); plain.WriteByte(1); plain.WriteByte(1);
                    WriteUInt32Value(plain, "_id", packet.Command); plain.Write(keyValues, 0, keyValues.Length);
                    if (!acknowledgement) while (plain.Length % 8 != 4) plain.WriteByte(0);
                    byte[] data = plain.ToArray(); MemoryStream header = new MemoryStream(); header.WriteByte(0x04); header.WriteByte(0x16); WriteUInt16(header, (ushort)(data.Length + 4)); header.Write(data, 0, data.Length);
                    data = header.ToArray();
                    if (acknowledgement) return data;
                    byte[] encrypted = Ice.Encrypt(data); byte[] result = new byte[encrypted.Length + 4]; result[0] = 0xfe; result[1] = 0xff; result[2] = 0xff; result[3] = 0xff; Array.Copy(encrypted, 0, result, 4, encrypted.Length); return result;
                }
                plain.WriteByte(0x05); WriteUInt32(plain, packet.ClientId); WriteUInt32(plain, packet.SessionId); plain.WriteByte((byte)packet.Sequence); plain.WriteByte((byte)packet.Acknowledgement); plain.WriteByte(packet.TotalPackets); WriteUInt16(plain, packet.Command); plain.Write(keyValues, 0, keyValues.Length);
                while (plain.Length % 16 != 0) plain.WriteByte(0);
                byte[] retail = plain.ToArray(); if (acknowledgement) return retail;
                byte[] retailEncrypted = Ice.Encrypt(retail); byte[] retailResult = new byte[retailEncrypted.Length + 1]; retailResult[0] = 0xfe; Array.Copy(retailEncrypted, 0, retailResult, 1, retailEncrypted.Length); return retailResult;
            }
            finally { Logger.LogFunctionExit("TrackerProtocol.Build"); }
        }

        private static TrackerPacket ParseBeta(byte[] data, bool encrypted)
        {
            if (data.Length < 22 || data[0] != 0x04 || data[1] != 0x16) throw new InvalidDataException("Invalid beta tracker header");
            ushort size = ReadUInt16(data, 2); if (size != data.Length) throw new InvalidDataException("Beta tracker size mismatch");
            TrackerPacket packet = new TrackerPacket(); packet.IsBeta = true; packet.IsEncrypted = encrypted; packet.ClientId = ReadUInt32(data, 4); packet.SessionId = ReadUInt32(data, 8); packet.Sequence = ReadUInt32(data, 12); packet.Acknowledgement = ReadUInt32(data, 16); packet.PacketNumber = data[20]; packet.TotalPackets = data[21]; packet.Values = ParseValues(data, 22, out packet.Command, true); return packet;
        }
        private static TrackerPacket ParseRetail(byte[] data, bool encrypted)
        {
            if (data.Length < 14 || data[0] != 0x05) throw new InvalidDataException("Invalid retail tracker header");
            TrackerPacket packet = new TrackerPacket(); packet.IsBeta = false; packet.IsEncrypted = encrypted; packet.ClientId = ReadUInt32(data, 1); packet.SessionId = ReadUInt32(data, 5); packet.Sequence = data[9]; packet.Acknowledgement = data[10]; packet.TotalPackets = data[11]; packet.PacketNumber = 1; packet.Command = ReadUInt16(data, 12); ushort ignoredCommand; packet.Values = ParseValues(data, 14, out ignoredCommand, false); return packet;
        }
        private static Dictionary<string, TrackerValue> ParseValues(byte[] data, int offset, out ushort command, bool commandInValues)
        {
            command = 0; Dictionary<string, TrackerValue> values = new Dictionary<string, TrackerValue>();
            while (offset < data.Length && data[offset] != 0)
            {
                byte type = data[offset++]; int keyEnd = Array.IndexOf<byte>(data, 0, offset); if (keyEnd < offset) throw new InvalidDataException("Tracker key has no terminator"); string key = Encoding.UTF8.GetString(data, offset, keyEnd - offset); offset = keyEnd + 1;
                if (offset + 2 > data.Length) throw new InvalidDataException("Tracker value length is truncated"); ushort length = ReadUInt16(data, offset); offset += 2; if (offset + length > data.Length) throw new InvalidDataException("Tracker value is truncated"); byte[] value = Slice(data, offset, length); offset += length;
                if ((type & 4) == 0 && value.Length > 0 && value[value.Length - 1] == 0) value = Slice(value, 0, value.Length - 1);
                TrackerValue parsedValue = new TrackerValue(); parsedValue.Type = type; parsedValue.Data = value; values[key] = parsedValue;
                if (commandInValues && key == "_id" && value.Length == 4) command = (ushort)ReadUInt32(value, 0);
            }
            return values;
        }
        private static byte[] SerializeValues(Dictionary<string, TrackerValue> values)
        {
            MemoryStream output = new MemoryStream(); if (values != null) foreach (KeyValuePair<string, TrackerValue> pair in values) { TrackerValue value = pair.Value; byte[] data = value.Data ?? new byte[0]; output.WriteByte(value.Type); byte[] key = Encoding.UTF8.GetBytes(pair.Key); output.Write(key,0,key.Length); output.WriteByte(0); if ((value.Type & 4) == 0) { byte[] terminated = new byte[data.Length + 1]; Array.Copy(data,terminated,data.Length); data = terminated; } WriteUInt16(output,(ushort)data.Length); output.Write(data,0,data.Length); } output.WriteByte(0); return output.ToArray();
        }
        internal static uint GetUInt32(Dictionary<string, TrackerValue> values, string key) { TrackerValue value; if (values == null || !values.TryGetValue(key, out value) || value.Data == null || value.Data.Length != 4) throw new InvalidDataException("Missing or invalid tracker integer '" + key + "'"); return ReadUInt32(value.Data, 0); }
        internal static string GetString(Dictionary<string, TrackerValue> values, string key) { TrackerValue value; if (values == null || !values.TryGetValue(key, out value)) throw new InvalidDataException("Missing tracker string '" + key + "'"); return Encoding.UTF8.GetString(value.Data ?? new byte[0]); }
        internal static void SetUInt32(Dictionary<string, TrackerValue> values, string key, uint value) { byte[] data = new byte[4]; data[0]=(byte)value; data[1]=(byte)(value>>8); data[2]=(byte)(value>>16); data[3]=(byte)(value>>24); TrackerValue item = new TrackerValue(); item.Type=5; item.Data=data; values[key] = item; }
        internal static void SetString(Dictionary<string, TrackerValue> values, string key, string value) { TrackerValue item = new TrackerValue(); item.Type=1; item.Data=Encoding.UTF8.GetBytes(value ?? String.Empty); values[key] = item; }
        internal static void SetBinary(Dictionary<string, TrackerValue> values, string key, byte[] value) { TrackerValue item = new TrackerValue(); item.Type=5; item.Data=value ?? new byte[0]; values[key] = item; }
        private static byte[] Slice(byte[] source, int offset, int count) { byte[] value = new byte[count]; Array.Copy(source,offset,value,0,count); return value; }
        private static ushort ReadUInt16(byte[] data, int offset) { return (ushort)(data[offset] | (data[offset+1]<<8)); }
        private static uint ReadUInt32(byte[] data, int offset) { return (uint)(data[offset] | (data[offset+1]<<8) | (data[offset+2]<<16) | (data[offset+3]<<24)); }
        private static void WriteUInt16(Stream stream, ushort value) { stream.WriteByte((byte)value); stream.WriteByte((byte)(value>>8)); }
        private static void WriteUInt32(Stream stream, uint value) { stream.WriteByte((byte)value); stream.WriteByte((byte)(value>>8)); stream.WriteByte((byte)(value>>16)); stream.WriteByte((byte)(value>>24)); }
        private static void WriteUInt32Value(Stream stream, string key, ushort value) { byte[] bytes = new byte[4]; bytes[0]=(byte)value; stream.WriteByte(5); byte[] k=Encoding.UTF8.GetBytes(key); stream.Write(k,0,k.Length); stream.WriteByte(0); WriteUInt16(stream,4); stream.Write(bytes,0,4); }
    }
}
