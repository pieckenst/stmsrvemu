using System;
using System.IO;
using System.Net;
using System.Text;
using StmServerEmulator.Database;
using StmServerEmulator.Encryption;

namespace StmServerEmulator.Servers
{
    /// <summary>Steam Client Statistics and Error Reporting UDP endpoint.</summary>
    public sealed class CSERServer : UdpServerBase
    {
        private readonly ClientStatisticsDatabase database;
        private readonly object sessionLock = new object();
        private readonly Random random = new Random();

        public CSERServer(int port, System.Collections.Generic.Dictionary<string, string> config) : base(port, config, "CSERServer")
        {
            logger.LogFunctionEntry("CSERServer.ctor", port); try { database = new ClientStatisticsDatabase(); } finally { logger.LogFunctionExit("CSERServer.ctor"); }
        }

        protected override byte[] ProcessDatagram(byte[] data, IPEndPoint remote)
        {
            logger.LogFunctionEntry("CSERServer.ProcessDatagram", data == null ? 0 : data.Length, remote == null ? "<null>" : remote.ToString());
            try
            {
                if (data == null || data.Length < 2) throw new InvalidDataException("CSER datagram is shorter than command and delimiter");
                if (data[1] != 0x0a) throw new InvalidDataException("CSER command delimiter is not LF");
                byte command = data[0]; byte[] body = Slice(data, 2, data.Length - 2); string eventType = CommandName(command);
                logger.Info("CSER RECV endpoint={0} command={1} ({2}) length={3}", remote, (char)command, eventType, data.Length);
                switch (command)
                {
                    case (byte)'a': StoreInstall(remote, body, data); return null;
                    case (byte)'c': return StoreCrash(remote, body, data);
                    case (byte)'e': StoreSteamStats(remote, body, data); return null;
                    case (byte)'g': return StoreSurvey(remote, body, data);
                    case (byte)'i': StoreBandwidth(remote, body, data); return null;
                    case (byte)'k': return StoreGameStats(remote, body, data);
                    case (byte)'m': return StorePhoneHome(remote, body, data);
                    case (byte)'o': return StoreBugReport(remote, body, data);
                    case (byte)'q': StoreUploadData(remote, body, data); return null;
                    case (byte)'u': StoreEncryptedGameStats(remote, body, data); return null;
                    default: database.Store(eventType, remote.Address.ToString(), remote.Port, data, "Unknown CSER command retained without a reply"); logger.Warning("CSER unsupported command {0:X2} from {1}", command, remote); return null;
                }
            }
            catch (Exception ex) { logger.LogException("CSERServer.ProcessDatagram", ex, remote == null ? String.Empty : remote.ToString()); return null; }
            finally { logger.LogFunctionExit("CSERServer.ProcessDatagram"); }
        }

        private void StoreInstall(IPEndPoint remote, byte[] body, byte[] raw)
        {
            logger.LogFunctionEntry("CSERServer.StoreInstall", remote); try { if (body.Length < 10) throw new InvalidDataException("Install-statistics body is truncated"); uint appId=ReadUInt32(body,1); uint duration=ReadUInt32(body,5); byte operation=body[9]; byte count=body.Length > 10 ? body[10] : (byte)0; database.Store("app_install_stats",remote.Address.ToString(),remote.Port,raw,"version="+body[0]+";appId="+appId+";durationSeconds="+duration+";operation="+operation+";gcfCount="+count); } finally { logger.LogFunctionExit("CSERServer.StoreInstall"); }
        }
        private byte[] StoreCrash(IPEndPoint remote, byte[] body, byte[] raw)
        {
            logger.LogFunctionEntry("CSERServer.StoreCrash", remote); try { database.Store("crashreport",remote.Address.ToString(),remote.Port,raw,"protocol="+(body.Length == 0 ? -1 : body[0])+";rawBody="+ToHex(body)); return BuildUploadReply((byte)'d'); } finally { logger.LogFunctionExit("CSERServer.StoreCrash"); }
        }
        private void StoreSteamStats(IPEndPoint remote, byte[] body, byte[] raw)
        {
            logger.LogFunctionEntry("CSERServer.StoreSteamStats", remote); try { string detail="rawBody="+ToHex(body); if(body.Length>1 && body[0]==1) { int offset=1; string client=ReadNullString(body,ref offset); if(offset+28<=body.Length) detail="version=1;client="+client+";success="+ReadUInt32(body,offset)+";unknownFailure="+ReadUInt32(body,offset+4)+";shutdownFailure="+ReadUInt32(body,offset+8)+";uptimeCleanCounter="+ReadUInt32(body,offset+12)+";uptimeCleanTotal="+ReadUInt32(body,offset+16)+";uptimeFailureCounter="+ReadUInt32(body,offset+20)+";uptimeFailureTotal="+ReadUInt32(body,offset+24); } database.Store("steamstats",remote.Address.ToString(),remote.Port,raw,detail); } finally { logger.LogFunctionExit("CSERServer.StoreSteamStats"); }
        }
        private byte[] StoreSurvey(IPEndPoint remote, byte[] body, byte[] raw)
        {
            logger.LogFunctionEntry("CSERServer.StoreSurvey", remote); try { byte[] decoded=Decrypt(body,3,new byte[] {27,200,13,14,83,45,184,54}); database.Store("surveyresults",remote.Address.ToString(),remote.Port,raw,"encrypted="+ToHex(body)+";decrypted="+ToHex(decoded)); return new byte[] {0xff,0xff,0xff,0xff,(byte)'h',1,0,0,0,(byte)'t',(byte)'h',(byte)'a',(byte)'n',(byte)'k',(byte)' ',(byte)'y',(byte)'o',(byte)'u',0}; } finally { logger.LogFunctionExit("CSERServer.StoreSurvey"); }
        }
        private void StoreBandwidth(IPEndPoint remote, byte[] body, byte[] raw)
        {
            logger.LogFunctionEntry("CSERServer.StoreBandwidth", remote); try { if(body.Length<6) throw new InvalidDataException("Bandwidth report is truncated"); int offset=0; byte version=body[offset++]; uint cell=ReadUInt32(body,offset); offset+=4; byte count=body[offset++]; StringBuilder detail=new StringBuilder("version="); detail.Append(version).Append(";cell=").Append(cell).Append(";records=").Append(count); for(int i=0;i<count;i++) { if(offset+10>body.Length) throw new InvalidDataException("Bandwidth record is truncated"); ushort server=ReadUInt16(body,offset); uint bytes=ReadUInt32(body,offset+2); float seconds=BitConverter.ToSingle(new byte[] {body[offset+9],body[offset+8],body[offset+7],body[offset+6]},0); offset+=10; detail.Append(";server=").Append(server).Append(",bytes=").Append(bytes).Append(",seconds=").Append(seconds); } database.Store("bandwidth_stats",remote.Address.ToString(),remote.Port,raw,detail.ToString()); } finally { logger.LogFunctionExit("CSERServer.StoreBandwidth"); }
        }
        private byte[] StoreGameStats(IPEndPoint remote, byte[] body, byte[] raw) { logger.LogFunctionEntry("CSERServer.StoreGameStats",remote); try { database.Store("gamestats",remote.Address.ToString(),remote.Port,raw,"rawBody="+ToHex(body)); return BuildUploadReply((byte)'l'); } finally { logger.LogFunctionExit("CSERServer.StoreGameStats"); } }
        private byte[] StorePhoneHome(IPEndPoint remote, byte[] body, byte[] raw)
        {
            logger.LogFunctionEntry("CSERServer.StorePhoneHome", remote); try { if(body.Length<7) throw new InvalidDataException("Phone-home report is truncated"); uint session=ReadUInt32(body,1); ushort encryptedLength=ReadUInt16(body,5); if(7+encryptedLength>body.Length) throw new InvalidDataException("Phone-home encrypted body is truncated"); byte[] encrypted=Slice(body,7,encryptedLength); byte[] decoded=new IceKey(1,new byte[] {191,1,0,222,85,39,154,1}).Decrypt(encrypted); if(session==0) session=NextSession(); database.Store("phonehome",remote.Address.ToString(),remote.Port,raw,"protocol="+body[0]+";session="+session+";decrypted="+ToHex(decoded)); byte[] reply=new byte[] {0xff,0xff,0xff,0xff,(byte)'n',1,0,0,0,0}; WriteUInt32(reply,6,session); return reply; } finally { logger.LogFunctionExit("CSERServer.StorePhoneHome"); }
        private byte[] StoreBugReport(IPEndPoint remote, byte[] body, byte[] raw) { logger.LogFunctionEntry("CSERServer.StoreBugReport",remote); try { byte[] decoded=Decrypt(body,3,new byte[] {200,145,10,149,195,190,108,243}); database.Store("bugreport",remote.Address.ToString(),remote.Port,raw,"encrypted="+ToHex(body)+";decrypted="+ToHex(decoded)); return BuildUploadReply((byte)'p'); } finally { logger.LogFunctionExit("CSERServer.StoreBugReport"); } }
        private void StoreUploadData(IPEndPoint remote, byte[] body, byte[] raw) { logger.LogFunctionEntry("CSERServer.StoreUploadData",remote); try { byte[] decoded=Decrypt(body,3,new byte[] {54,175,165,5,76,251,29,113}); database.Store("uploaddata",remote.Address.ToString(),remote.Port,raw,"encrypted="+ToHex(body)+";decrypted="+ToHex(decoded)); } finally { logger.LogFunctionExit("CSERServer.StoreUploadData"); } }
        private void StoreEncryptedGameStats(IPEndPoint remote, byte[] body, byte[] raw) { logger.LogFunctionEntry("CSERServer.StoreEncryptedGameStats",remote); try { byte[] decoded=Decrypt(body,3,new byte[] {54,175,165,5,76,251,29,113}); database.Store("upload_gamestats",remote.Address.ToString(),remote.Port,raw,"encrypted="+ToHex(body)+";decrypted="+ToHex(decoded)); } finally { logger.LogFunctionExit("CSERServer.StoreEncryptedGameStats"); } }
        private byte[] BuildUploadReply(byte type) { logger.LogFunctionEntry("CSERServer.BuildUploadReply",(char)type); try { return new byte[] {type,1,1}; } finally { logger.LogFunctionExit("CSERServer.BuildUploadReply"); } }
        private static byte[] Decrypt(byte[] body,int offset,byte[] key) { if(body.Length<offset) throw new InvalidDataException("Encrypted CSER payload is truncated"); return new IceKey(1,key).Decrypt(Slice(body,offset,body.Length-offset)); }
        private uint NextSession() { logger.LogFunctionEntry("CSERServer.NextSession"); try { lock(sessionLock) { byte[] bytes=new byte[4]; random.NextBytes(bytes); return ReadUInt32(bytes,0); } } finally { logger.LogFunctionExit("CSERServer.NextSession"); } }
        private static string CommandName(byte command) { switch(command) { case (byte)'a':return "app_install_stats"; case (byte)'c':return "crashreport"; case (byte)'e':return "steamstats"; case (byte)'g':return "surveyresults"; case (byte)'i':return "bandwidth_stats"; case (byte)'k':return "gamestats"; case (byte)'m':return "phonehome"; case (byte)'o':return "bugreport"; case (byte)'q':return "uploaddata"; case (byte)'u':return "upload_gamestats"; default:return "unknown_"+command.ToString("X2"); } }
        private static string ReadNullString(byte[] data,ref int offset) { int start=offset; while(offset<data.Length && data[offset]!=0) offset++; if(offset==data.Length) throw new InvalidDataException("CSER string is not null terminated"); string value=Encoding.GetEncoding(1252).GetString(data,start,offset-start); offset++; return value; }
        private static byte[] Slice(byte[] value,int offset,int length) { byte[] result=new byte[length]; Array.Copy(value,offset,result,0,length); return result; }
        private static ushort ReadUInt16(byte[] value,int offset) { return (ushort)(value[offset]|(value[offset+1]<<8)); }
        private static uint ReadUInt32(byte[] value,int offset) { return (uint)(value[offset]|(value[offset+1]<<8)|(value[offset+2]<<16)|(value[offset+3]<<24)); }
        private static void WriteUInt32(byte[] value,int offset,uint number) { value[offset]=(byte)number;value[offset+1]=(byte)(number>>8);value[offset+2]=(byte)(number>>16);value[offset+3]=(byte)(number>>24); }
        private static string ToHex(byte[] value) { return BitConverter.ToString(value ?? new byte[0]).Replace("-",String.Empty); }
        public override void Stop() { logger.LogFunctionEntry("CSERServer.Stop"); try { base.Stop(); database.Dispose(); } finally { logger.LogFunctionExit("CSERServer.Stop"); } }
    }
}
