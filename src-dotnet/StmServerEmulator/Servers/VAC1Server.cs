using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace StmServerEmulator.Servers
{
    /// <summary>Valve Anti-Cheat 1 UDP module-transfer service.</summary>
    public sealed class VAC1Server : UdpServerBase
    {
        private const uint ModuleId = 0x12345678;
        private sealed class Session { internal byte[] Header; internal byte[] Data; internal string ModuleName; }
        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        private readonly object sessionLock = new object();

        public VAC1Server(int port, Dictionary<string, string> config) : base(port, config, "VAC1Server") { logger.LogFunctionEntry("VAC1Server.ctor",port); logger.LogFunctionExit("VAC1Server.ctor"); }

        protected override byte[] ProcessDatagram(byte[] data, IPEndPoint remote)
        {
            logger.LogFunctionEntry("VAC1Server.ProcessDatagram",data == null ? 0 : data.Length,remote == null ? "<null>" : remote.ToString());
            try
            {
                if(data == null || data.Length < 6) throw new InvalidDataException("VAC1 packet is truncated");
                int offset=0; uint marker=ReadUInt32(data,ref offset); byte type=data[offset++]; byte reserved=data[offset++]; if(marker != 0xffffffff || reserved != 0) throw new InvalidDataException("VAC1 header is invalid");
                if(type==0x4d) { uint module=ReadUInt32(data,ref offset); logger.Info("VAC1 access-check endpoint={0} module={1:X8}",remote,module); return AccessGranted(module,module==ModuleId ? (byte)1 : (byte)0); }
                string command=ReadString(data,ref offset); logger.Info("VAC1 RECV endpoint={0} command={1}",remote,command);
                lock(sessionLock)
                {
                    string key=remote.ToString();
                    if(command=="CHALLENGE") { sessions[key]=new Session(); return Accept(); }
                    if(command=="ABORT") { sessions.Remove(key); return null; }
                    Session session; if(!sessions.TryGetValue(key,out session)) return Abort("Challenge required");
                    if(command=="GET") return GetModule(data,ref offset,session);
                    if(command=="NEXT") return NextBlock(data,ref offset,key,session);
                    return Abort("Protocol violation");
                }
            }
            catch(Exception ex) { logger.LogException("VAC1Server.ProcessDatagram",ex,remote == null ? String.Empty : remote.ToString()); return Abort("Protocol violation"); }
            finally { logger.LogFunctionExit("VAC1Server.ProcessDatagram"); }
        }

        private byte[] GetModule(byte[] data,ref int offset,Session session)
        {
            logger.LogFunctionEntry("VAC1Server.GetModule");
            try
            {
                uint id=ReadUInt32(data,ref offset); string fileName=ReadString(data,ref offset); if(id!=ModuleId || !IsSafeModuleName(fileName)) return Abort("Challenge error");
                string directory; if(!config.TryGetValue("vacmoduledir",out directory) || String.IsNullOrEmpty(directory)) throw new InvalidDataException("VAC module directory is not configured");
                string root=Path.GetFullPath(directory); string path=Path.GetFullPath(Path.Combine(root,fileName)); if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return Abort("file not found");
                byte[] container=File.ReadAllBytes(path); int cursor=0; ushort nameLength=ReadUInt16Big(container,ref cursor); if(cursor+nameLength+4>container.Length) throw new InvalidDataException("VAC module header is truncated"); string declared=Encoding.UTF8.GetString(container,cursor,nameLength); cursor+=nameLength; int headerLength=ReadInt32Big(container,ref cursor); if(headerLength<0 || cursor+headerLength+4>container.Length) throw new InvalidDataException("VAC module metadata is truncated"); session.Header=Slice(container,cursor,headerLength); cursor+=headerLength; int dataLength=ReadInt32Big(container,ref cursor); if(dataLength<0 || cursor+dataLength>container.Length) throw new InvalidDataException("VAC module payload is truncated"); session.Data=Slice(container,cursor,dataLength); EncodeModule(session.Data); session.ModuleName=declared; logger.Info("VAC1 module accepted name={0} bytes={1}",declared,dataLength); return FileReply(session.Data.Length,session.Header);
            }
            finally { logger.LogFunctionExit("VAC1Server.GetModule"); }
        }
        private byte[] NextBlock(byte[] data,ref int offset,string sessionKey,Session session)
        {
            logger.LogFunctionEntry("VAC1Server.NextBlock",session.ModuleName); try { int position=unchecked((int)ReadUInt32(data,ref offset)); if(position<0 || session.Data==null || position>session.Data.Length) return Abort("error"); if(position==session.Data.Length) { sessions.Remove(sessionKey); return Finish(); } int length=Math.Min(1024,session.Data.Length-position); return Block(Slice(session.Data,position,length)); } finally { logger.LogFunctionExit("VAC1Server.NextBlock"); }
        }
        private static bool IsSafeModuleName(string name) { return !String.IsNullOrEmpty(name) && name.IndexOf("..")<0 && name.IndexOf(Path.DirectorySeparatorChar)<0 && name.IndexOf(Path.AltDirectorySeparatorChar)<0; }
        private static byte[] Accept() { return Command("ACCEPT",UInt32(ModuleId)); }
        private static byte[] Finish() { return Command("FINISH",new byte[0]); }
        private static byte[] Abort(string text) { return Command("ABORT",Encoding.UTF8.GetBytes(text+"\0")); }
        private static byte[] FileReply(int size,byte[] header) { return Command("FILE",Concat(UInt32((uint)size),header)); }
        private static byte[] Block(byte[] payload) { return Command("BLOCK",Concat(UInt16((ushort)payload.Length),payload)); }
        private static byte[] AccessGranted(uint id,byte allowed) { return Concat(UInt32(0xffffffff),new byte[] {0x4e,0},UInt32(id),new byte[] {allowed,0,0,0,0,0}); }
        private static byte[] Command(string name,byte[] payload) { return Concat(UInt32(0xffffffff),new byte[] {0x4b,0},Encoding.ASCII.GetBytes(name+"\0"),payload); }
        private static void EncodeModule(byte[] data) { byte[] pattern=new byte[] {5,97,122,237,27,202,13,155,74,241,100,199,181,142,223,160}; uint inverse=ModuleId^0xffffffff; int block=0; for(int i=0;i+3<data.Length;i+=4,block++) { data[i]^=(byte)inverse;data[i+1]^=(byte)(inverse>>8);data[i+2]^=(byte)(inverse>>16);data[i+3]^=(byte)(inverse>>24); byte a=data[i],b=data[i+1],c=data[i+2],d=data[i+3];data[i]=d;data[i+1]=c;data[i+2]=b;data[i+3]=a;data[i]^=(byte)(pattern[(block)&15]|0xa5);data[i+1]^=(byte)(pattern[(block+1)&15]|0xa7);data[i+2]^=(byte)(pattern[(block+2)&15]|0xad);data[i+3]^=(byte)(pattern[(block+3)&15]|0xbf);data[i]^=(byte)ModuleId;data[i+1]^=(byte)(ModuleId>>8);data[i+2]^=(byte)(ModuleId>>16);data[i+3]^=(byte)(ModuleId>>24); } }
        private static string ReadString(byte[] data,ref int offset) { int start=offset; while(offset<data.Length && data[offset]!=0) offset++; if(offset==data.Length) throw new InvalidDataException("VAC1 command is not terminated"); string result=Encoding.GetEncoding(1252).GetString(data,start,offset-start);offset++;return result; }
        private static uint ReadUInt32(byte[] data,ref int offset) { if(offset+4>data.Length) throw new InvalidDataException("VAC1 integer is truncated"); uint result=(uint)(data[offset]|(data[offset+1]<<8)|(data[offset+2]<<16)|(data[offset+3]<<24));offset+=4;return result; }
        private static ushort ReadUInt16Big(byte[] data,ref int offset) { if(offset+2>data.Length) throw new InvalidDataException("VAC1 big-endian short is truncated"); ushort result=(ushort)((data[offset]<<8)|data[offset+1]);offset+=2;return result; }
        private static int ReadInt32Big(byte[] data,ref int offset) { if(offset+4>data.Length) throw new InvalidDataException("VAC1 big-endian integer is truncated"); int result=(data[offset]<<24)|(data[offset+1]<<16)|(data[offset+2]<<8)|data[offset+3];offset+=4;return result; }
        private static byte[] UInt32(uint value) { return new byte[] {(byte)value,(byte)(value>>8),(byte)(value>>16),(byte)(value>>24)}; }
        private static byte[] UInt16(ushort value) { return new byte[] {(byte)value,(byte)(value>>8)}; }
        private static byte[] Slice(byte[] data,int offset,int length) { byte[] result=new byte[length];Array.Copy(data,offset,result,0,length);return result; }
        private static byte[] Concat(params byte[][] parts) { int size=0;foreach(byte[] part in parts) size+=part.Length;byte[] result=new byte[size];int offset=0;foreach(byte[] part in parts){Array.Copy(part,0,result,offset,part.Length);offset+=part.Length;}return result; }
    }
}
