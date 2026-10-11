using System;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Encryption
{
    /// <summary>ICE block cipher used by the Steam2 tracker transport.</summary>
    public sealed class IceKey
    {
        private static readonly int[][] SMod = new int[][] { new int[] {333,313,505,369}, new int[] {379,375,319,391}, new int[] {361,445,451,397}, new int[] {397,425,395,505} };
        private static readonly int[][] SXor = new int[][] { new int[] {0x83,0x85,0x9b,0xcd}, new int[] {0xcc,0xa7,0xad,0x41}, new int[] {0x4b,0x2e,0xd4,0x33}, new int[] {0xea,0xcb,0x2e,0x04} };
        private static readonly uint[] PBox = new uint[] { 0x00000001,0x00000080,0x00000400,0x00002000,0x00080000,0x00200000,0x01000000,0x40000000,0x00000008,0x00000020,0x00000100,0x00004000,0x00010000,0x00800000,0x04000000,0x20000000,0x00000004,0x00000010,0x00000200,0x00008000,0x00020000,0x00800000,0x08000000,0x10000000,0x00000002,0x00000040,0x00000800,0x00001000,0x00040000,0x00100000,0x02000000,0x80000000 };
        private static readonly int[] KeyRot = new int[] { 0,1,2,3,2,1,3,0,1,3,2,0,3,1,0,2 };
        private static readonly object SBoxLock = new object();
        private static uint[][] sBox;
        private readonly uint[][] keySchedule;
        private readonly int rounds;
        private readonly Logger logger;

        public IceKey(int level, byte[] key)
        {
            logger = Logger.GetLogger("IceKey");
            logger.LogFunctionEntry("IceKey.ctor", level, key == null ? 0 : key.Length);
            try
            {
                if (key == null) throw new ArgumentNullException("key");
                BuildSBoxes();
                int size = level < 1 ? 1 : level;
                rounds = level < 1 ? 8 : level * 16;
                if (key.Length < size * 8) throw new ArgumentException("ICE key is shorter than its selected level", "key");
                keySchedule = new uint[rounds][];
                for (int i = 0; i < rounds; i++) keySchedule[i] = new uint[3];
                if (rounds == 8) ScheduleBuild(key, 0, 0);
                else
                {
                    for (int i = 0; i < size; i++)
                    {
                        ScheduleBuild(key, i * 8, i * 8);
                        ScheduleBuild(key, i * 8, rounds - 8 - i * 8, true);
                    }
                }
            }
            finally { logger.LogFunctionExit("IceKey.ctor"); }
        }

        public byte[] Encrypt(byte[] data) { return Transform(data, true); }
        public byte[] Decrypt(byte[] data) { return Transform(data, false); }

        private byte[] Transform(byte[] data, bool encrypt)
        {
            logger.LogFunctionEntry("IceKey.Transform", data == null ? 0 : data.Length, encrypt);
            try
            {
                if (data == null) throw new ArgumentNullException("data");
                byte[] output = new byte[data.Length];
                int offset = 0;
                while (offset + 8 <= data.Length)
                {
                    TransformBlock(data, offset, output, offset, encrypt);
                    offset += 8;
                }
                if (offset < data.Length) Array.Copy(data, offset, output, offset, data.Length - offset);
                return output;
            }
            finally { logger.LogFunctionExit("IceKey.Transform"); }
        }

        private void TransformBlock(byte[] input, int inputOffset, byte[] output, int outputOffset, bool encrypt)
        {
            uint l = ReadBigEndian(input, inputOffset);
            uint r = ReadBigEndian(input, inputOffset + 4);
            if (encrypt)
            {
                for (int i = 0; i < rounds; i += 2) { l ^= IceFunction(r, keySchedule[i]); r ^= IceFunction(l, keySchedule[i + 1]); }
            }
            else
            {
                for (int i = rounds - 1; i > 0; i -= 2) { l ^= IceFunction(r, keySchedule[i]); r ^= IceFunction(l, keySchedule[i - 1]); }
            }
            WriteBigEndian(output, outputOffset, r); WriteBigEndian(output, outputOffset + 4, l);
        }

        private void ScheduleBuild(byte[] key, int keyOffset, int scheduleOffset) { ScheduleBuild(key, keyOffset, scheduleOffset, false); }
        private void ScheduleBuild(byte[] key, int keyOffset, int scheduleOffset, bool secondHalf)
        {
            ushort[] kb = new ushort[4];
            for (int i = 0; i < 4; i++) kb[3 - i] = (ushort)((key[keyOffset + i * 2] << 8) | key[keyOffset + i * 2 + 1]);
            int rotationBase = secondHalf ? 8 : 0;
            for (int i = 0; i < 8; i++)
            {
                uint[] subKey = keySchedule[scheduleOffset + i];
                int rotation = KeyRot[rotationBase + i];
                for (int j = 0; j < 15; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        int index = (rotation + k) & 3;
                        uint bit = (uint)(kb[index] & 1);
                        subKey[j % 3] = (subKey[j % 3] << 1) | bit;
                        kb[index] = (ushort)((kb[index] >> 1) | ((bit ^ 1) << 15));
                    }
                }
            }
        }

        private static void BuildSBoxes()
        {
            lock (SBoxLock)
            {
                if (sBox != null) return;
                sBox = new uint[4][];
                for (int i = 0; i < 4; i++) sBox[i] = new uint[1024];
                for (int i = 0; i < 1024; i++)
                {
                    int column = (i >> 1) & 0xff; int row = (i & 1) | ((i & 0x200) >> 8);
                    sBox[0][i] = Perm32(GfExp7((uint)(column ^ SXor[0][row]), (uint)SMod[0][row]) << 24);
                    sBox[1][i] = Perm32(GfExp7((uint)(column ^ SXor[1][row]), (uint)SMod[1][row]) << 16);
                    sBox[2][i] = Perm32(GfExp7((uint)(column ^ SXor[2][row]), (uint)SMod[2][row]) << 8);
                    sBox[3][i] = Perm32(GfExp7((uint)(column ^ SXor[3][row]), (uint)SMod[3][row]));
                }
            }
        }

        private static uint IceFunction(uint p, uint[] sk)
        {
            uint tl = ((p >> 16) & 0x3ff) | (((p >> 14) | (p << 18)) & 0xffc00);
            uint tr = (p & 0x3ff) | ((p << 2) & 0xffc00);
            uint al = sk[2] & (tl ^ tr); uint ar = al ^ tr; al ^= tl; al ^= sk[0]; ar ^= sk[1];
            return sBox[0][al >> 10] | sBox[1][al & 0x3ff] | sBox[2][ar >> 10] | sBox[3][ar & 0x3ff];
        }
        private static uint GfExp7(uint b, uint modulus) { if (b == 0) return 0; uint x = GfMult(b, b, modulus); x = GfMult(b, x, modulus); x = GfMult(x, x, modulus); return GfMult(b, x, modulus); }
        private static uint GfMult(uint a, uint b, uint modulus) { uint result = 0; while (b != 0) { if ((b & 1) != 0) result ^= a; a <<= 1; b >>= 1; if (a >= 256) a ^= modulus; } return result; }
        private static uint Perm32(uint value) { uint result = 0; int index = 0; while (value != 0) { if ((value & 1) != 0) result |= PBox[index]; index++; value >>= 1; } return result; }
        private static uint ReadBigEndian(byte[] data, int offset) { return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3]; }
        private static void WriteBigEndian(byte[] data, int offset, uint value) { data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16); data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value; }
    }
}
