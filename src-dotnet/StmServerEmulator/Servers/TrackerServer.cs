using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using StmServerEmulator.Database;
using StmServerEmulator.Encryption;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Servers
{
    /// <summary>Steam2 Friends/Tracker UDP service for beta and retail ICE-encrypted tracker packets.</summary>
    public sealed class TrackerServer : UdpServerBase
    {
        private sealed class ClientState
        {
            internal IPEndPoint EndPoint; internal uint ClientId; internal uint SessionId; internal uint ExpectedSequence; internal uint LastClientSequence; internal uint ServerSequence; internal uint UserId; internal uint Status; internal uint Challenge; internal bool Initialized; internal bool IsBeta; internal DateTime ExpiresAt;
        }
        private readonly object clientLock = new object();
        private readonly Dictionary<string, ClientState> clients = new Dictionary<string, ClientState>();
        private readonly Dictionary<uint, ClientState> clientsByUser = new Dictionary<uint, ClientState>();
        private readonly TrackerDatabase database;
        private readonly Random random = new Random();
        private readonly byte[] networkKey;

        public TrackerServer(int port, Dictionary<string, string> config) : base(port, config, "TrackerServer")
        {
            logger.LogFunctionEntry("TrackerServer.ctor", port);
            try { database = new TrackerDatabase(); networkKey = BuildNetworkKey(); }
            finally { logger.LogFunctionExit("TrackerServer.ctor"); }
        }

        protected override byte[] ProcessDatagram(byte[] data, IPEndPoint remote)
        {
            logger.LogFunctionEntry("TrackerServer.ProcessDatagram", data == null ? 0 : data.Length, remote == null ? "<null>" : remote.ToString());
            try
            {
                TrackerPacket packet = TrackerProtocol.Parse(data);
                logger.Info("Tracker RECV endpoint={0} beta={1} client={2:X8} session={3:X8} seq={4} ack={5} command={6}", remote, packet.IsBeta, packet.ClientId, packet.SessionId, packet.Sequence, packet.Acknowledgement, packet.Command);
                lock (clientLock) { RemoveExpiredClients(); HandlePacket(packet, remote); }
                return null;
            }
            catch (Exception ex) { logger.LogException("TrackerServer.ProcessDatagram", ex, remote == null ? String.Empty : remote.ToString()); return null; }
            finally { logger.LogFunctionExit("TrackerServer.ProcessDatagram"); }
        }

        private void HandlePacket(TrackerPacket packet, IPEndPoint remote)
        {
            logger.LogFunctionEntry("TrackerServer.HandlePacket", packet.Command, remote);
            try
            {
                ClientState client = GetOrCreateClient(packet, remote); client.ExpiresAt = DateTime.UtcNow.AddSeconds(60);
                if (packet.ClientId != client.ClientId)
                {
                    logger.Warning("Tracker connection reset endpoint={0} oldClient={1:X8} newClient={2:X8}", remote, client.ClientId, packet.ClientId);
                    client.ClientId=packet.ClientId; client.SessionId=packet.SessionId; client.ExpectedSequence=1; client.ServerSequence=1; client.Initialized=false; client.UserId=0;
                }
                if (packet.Sequence != 0 && packet.Sequence < client.ExpectedSequence) { logger.Warning("Tracker duplicate endpoint={0} sequence={1} expected={2}",remote,packet.Sequence,client.ExpectedSequence); SendAck(client, packet); return; }
                if (packet.Sequence != 0 && packet.Sequence > client.ExpectedSequence) { logger.Warning("Tracker out-of-order endpoint={0} sequence={1} expected={2}",remote,packet.Sequence,client.ExpectedSequence); SendReset(client, packet, "Out of order tracker packet."); return; }
                client.LastClientSequence=packet.Sequence; if (packet.Sequence != 0) client.ExpectedSequence++;
                Dispatch(client, packet);
            }
            finally { logger.LogFunctionExit("TrackerServer.HandlePacket"); }
        }

        private ClientState GetOrCreateClient(TrackerPacket packet, IPEndPoint remote)
        {
            logger.LogFunctionEntry("TrackerServer.GetOrCreateClient", remote);
            try
            {
                string key = remote.ToString(); ClientState client;
                if (!clients.TryGetValue(key, out client))
                {
                    client = new ClientState(); client.EndPoint=remote; client.ClientId=packet.ClientId; client.SessionId=packet.SessionId; client.ExpectedSequence=packet.Sequence == 0 ? 1 : packet.Sequence; client.ServerSequence=1; client.IsBeta=packet.IsBeta; client.ExpiresAt=DateTime.UtcNow.AddSeconds(60); clients.Add(key,client);
                    logger.Info("Tracker client created endpoint={0} client={1:X8}",remote,packet.ClientId);
                }
                return client;
            }
            finally { logger.LogFunctionExit("TrackerServer.GetOrCreateClient"); }
        }

        private void Dispatch(ClientState client, TrackerPacket packet)
        {
            logger.LogFunctionEntry("TrackerServer.Dispatch", client.UserId, packet.Command);
            try
            {
                if (packet.Command != 2001 && packet.Command != 1 && !ValidateSession(client, packet)) return;
                switch (packet.Command)
                {
                    case 1: logger.Debug("Tracker ACK uid={0} ack={1}",client.UserId,packet.Acknowledgement); break;
                    case 2001: PreLogin(client, packet); break;
                    case 2002: Login(client, packet); break;
                    case 2004: Search(client, packet); break;
                    case 2005: StatusChange(client, packet); break;
                    case 2006: ResolveFriend(client, packet); break;
                    case 2007: RequestFriend(client, packet); break;
                    case 2008: FriendInfo(client, packet); break;
                    case 2009: UpdateUserInfo(client, packet); break;
                    case 2010: ForwardMessage(client, packet); break;
                    case 3001: ChatMessage(client, packet); break;
                    case 3002: SendAck(client, packet); logger.Warning("Tracker block-status command logged uid={0}; legacy client supplied no persistent block contract",client.UserId); break;
                    case 3006: Typing(client, packet); break;
                    default: SendReset(client, packet, "Unsupported tracker command " + packet.Command); logger.Warning("Tracker unsupported command={0} uid={1}",packet.Command,client.UserId); break;
                }
            }
            finally { logger.LogFunctionExit("TrackerServer.Dispatch"); }
        }

        private bool ValidateSession(ClientState client, TrackerPacket packet)
        {
            logger.LogFunctionEntry("TrackerServer.ValidateSession",client.UserId,packet.SessionId);
            try { if (!client.Initialized || client.SessionId != packet.SessionId) { SendReset(client,packet,"Tracker session is not initialized or has expired."); return false; } return true; }
            finally { logger.LogFunctionExit("TrackerServer.ValidateSession"); }
        }

        private void PreLogin(ClientState client, TrackerPacket packet)
        {
            logger.LogFunctionEntry("TrackerServer.PreLogin",client.EndPoint);
            try
            {
                if (packet.SessionId != 0) { SendReset(client,packet,"Pre-login requires a zero session id."); return; }
                uint requestedId=TrackerProtocol.GetUInt32(packet.Values,"uid"); string email=TrackerProtocol.GetString(packet.Values,"email"); string userName = packet.Values.ContainsKey("UserName") ? TrackerProtocol.GetString(packet.Values,"UserName") : email;
                TrackerProfile profile=database.FindOrCreateProfile(requestedId,email,userName); if (profile == null) { SendReset(client,packet,"Unknown account."); return; }
                client.UserId=profile.AccountId; client.Status=TrackerProtocol.GetUInt32(packet.Values,"status"); client.SessionId=NextUInt32(); client.Challenge=client.IsBeta ? NextUInt32() : 0; client.Initialized=true; clientsByUser[client.UserId]=client;
                TrackerPacket reply=NewPacket(client,1001,packet); TrackerProtocol.SetUInt32(reply.Values,"sessionID",client.SessionId); TrackerProtocol.SetUInt32(reply.Values,"challenge",client.Challenge); TrackerProtocol.SetBinary(reply.Values,"key",networkKey); Send(client,reply,false);
                logger.Info("Tracker pre-login accepted uid={0} email={1} session={2:X8}",client.UserId,profile.Email,client.SessionId);
            }
            finally { logger.LogFunctionExit("TrackerServer.PreLogin"); }
        }

        private void Login(ClientState client, TrackerPacket packet)
        {
            logger.LogFunctionEntry("TrackerServer.Login",client.UserId);
            try
            {
                if (TrackerProtocol.GetUInt32(packet.Values,"sessionID") != client.SessionId || TrackerProtocol.GetUInt32(packet.Values,"challenge") != client.Challenge) { SendReset(client,packet,"Invalid tracker challenge or session."); return; }
                Send(client,NewPacket(client,1002,packet),false); SendInitialState(client,packet);
            }
            finally { logger.LogFunctionExit("TrackerServer.Login"); }
        }

        private void SendInitialState(ClientState client, TrackerPacket packet)
        {
            logger.LogFunctionEntry("TrackerServer.SendInitialState",client.UserId);
            try
            {
                IList<uint> pending=database.GetPending(client.UserId); foreach(uint friendId in pending) SendFriendRequest(client,friendId,packet);
                IList<uint> friends=database.GetFriends(client.UserId); foreach(uint friendId in friends) { SendUserInfo(client,friendId,packet); SendSingleStatus(client.UserId,friendId,packet); ClientState friend; if(clientsByUser.TryGetValue(friendId,out friend)) SendSingleStatus(friendId,client.UserId,packet); }
                foreach(TrackerOfflineMessage message in database.GetUndeliveredMessages(client.UserId)) { TrackerPacket reply=NewPacket(client,3001,packet); TrackerProtocol.SetUInt32(reply.Values,"uid",message.SenderId); TrackerProtocol.SetUInt32(reply.Values,"targetID",client.UserId); TrackerProtocol.SetString(reply.Values,"UserName",message.UserName); TrackerProtocol.SetUInt32(reply.Values,"status",message.Status); TrackerProtocol.SetString(reply.Values,"Text",message.Text); Send(client,reply,false); database.MarkMessageDelivered(message.MessageId); }
            }
            finally { logger.LogFunctionExit("TrackerServer.SendInitialState"); }
        }

        private void Search(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.Search",client.UserId); try { IList<TrackerProfile> profiles=database.SearchProfiles(client.UserId); foreach(TrackerProfile profile in profiles) { TrackerPacket reply=NewPacket(client,1011,packet); AddProfile(reply,profile); Send(client,reply,false); } if(profiles.Count==0) SendAck(client,packet); } finally { logger.LogFunctionExit("TrackerServer.Search"); } }
        private void RequestFriend(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.RequestFriend",client.UserId); try { uint target=TrackerProtocol.GetUInt32(packet.Values,"uid"); if(!database.RequestFriend(client.UserId,target)) { SendReset(client,packet,"Friend request rejected."); return; } SendAck(client,packet); ClientState targetClient; if(clientsByUser.TryGetValue(target,out targetClient)) SendFriendRequest(targetClient,client.UserId,packet); } finally { logger.LogFunctionExit("TrackerServer.RequestFriend"); } }
        private void ResolveFriend(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.ResolveFriend",client.UserId); try { uint target=TrackerProtocol.GetUInt32(packet.Values,"targetID"); bool accepted=TrackerProtocol.GetUInt32(packet.Values,"auth") == 1; if(!database.ResolveFriend(client.UserId,target,accepted)) { SendReset(client,packet,"Friend request cannot be resolved."); return; } SendAck(client,packet); SendSingleStatus(client.UserId,target,packet); SendSingleStatus(target,client.UserId,packet); } finally { logger.LogFunctionExit("TrackerServer.ResolveFriend"); } }
        private void FriendInfo(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.FriendInfo",client.UserId); try { SendUserInfo(client,TrackerProtocol.GetUInt32(packet.Values,"uid"),packet); } finally { logger.LogFunctionExit("TrackerServer.FriendInfo"); } }
        private void UpdateUserInfo(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.UpdateUserInfo",client.UserId); try { if(TrackerProtocol.GetUInt32(packet.Values,"uid") != client.UserId) { SendReset(client,packet,"Cannot update another tracker user."); return; } database.UpdateProfile(client.UserId,TrackerProtocol.GetString(packet.Values,"UserName"),TrackerProtocol.GetString(packet.Values,"FirstName"),TrackerProtocol.GetString(packet.Values,"LastName")); SendAck(client,packet); foreach(uint friend in database.GetFriends(client.UserId)) SendUserInfoToOnline(friend,client.UserId,packet); } finally { logger.LogFunctionExit("TrackerServer.UpdateUserInfo"); } }
        private void StatusChange(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.StatusChange",client.UserId); try { client.Status=TrackerProtocol.GetUInt32(packet.Values,"status"); SendAck(client,packet); foreach(uint friend in database.GetFriends(client.UserId)) SendSingleStatus(friend,client.UserId,packet); if(client.Status==0) RemoveClient(client); } finally { logger.LogFunctionExit("TrackerServer.StatusChange"); } }
        private void Typing(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.Typing",client.UserId); try { uint target=TrackerProtocol.GetUInt32(packet.Values,"targetID"); SendAck(client,packet); ClientState targetClient; if(clientsByUser.TryGetValue(target,out targetClient)) { TrackerPacket reply=NewPacket(targetClient,3006,packet); Copy(packet,reply,"state","ChatID","UID","status","targetID"); Send(targetClient,reply,false); } } finally { logger.LogFunctionExit("TrackerServer.Typing"); } }
        private void ChatMessage(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.ChatMessage",client.UserId); try { uint target=TrackerProtocol.GetUInt32(packet.Values,"targetID"); string name=TrackerProtocol.GetString(packet.Values,"UserName"); string text=TrackerProtocol.GetString(packet.Values,"Text"); uint status=TrackerProtocol.GetUInt32(packet.Values,"status"); SendAck(client,packet); ClientState targetClient; if(clientsByUser.TryGetValue(target,out targetClient)) { TrackerPacket reply=NewPacket(targetClient,3001,packet); TrackerProtocol.SetUInt32(reply.Values,"uid",client.UserId); TrackerProtocol.SetUInt32(reply.Values,"targetID",target); TrackerProtocol.SetString(reply.Values,"UserName",name); TrackerProtocol.SetUInt32(reply.Values,"status",status); TrackerProtocol.SetString(reply.Values,"Text",text); Send(targetClient,reply,false); } else database.StoreOfflineMessage(client.UserId,target,name,status,text); } finally { logger.LogFunctionExit("TrackerServer.ChatMessage"); } }
        private void ForwardMessage(ClientState client, TrackerPacket packet) { logger.LogFunctionEntry("TrackerServer.ForwardMessage",client.UserId); try { uint target=TrackerProtocol.GetUInt32(packet.Values,"rUserID"); SendAck(client,packet); ClientState targetClient; if(clientsByUser.TryGetValue(target,out targetClient)) { TrackerPacket reply=NewPacket(targetClient,(ushort)TrackerProtocol.GetUInt32(packet.Values,"rID"),packet); TrackerValue body; if(packet.Values.TryGetValue("rData",out body)) reply.Values["rData"]=body; Send(targetClient,reply,false); } } finally { logger.LogFunctionExit("TrackerServer.ForwardMessage"); } }
        private void SendFriendRequest(ClientState client,uint friendId,TrackerPacket received) { logger.LogFunctionEntry("TrackerServer.SendFriendRequest",client.UserId,friendId); try { TrackerProfile profile=database.FindProfile(friendId); if(profile==null) return; TrackerPacket reply=NewPacket(client,1015,received); AddProfile(reply,profile); TrackerProtocol.SetUInt32(reply.Values,"uid",profile.AccountId); Send(client,reply,false); } finally { logger.LogFunctionExit("TrackerServer.SendFriendRequest"); } }
        private void SendUserInfo(ClientState client,uint userId,TrackerPacket received) { logger.LogFunctionEntry("TrackerServer.SendUserInfo",client.UserId,userId); try { TrackerProfile profile=database.FindProfile(userId); if(profile==null) { SendReset(client,received,"Requested tracker user does not exist."); return; } TrackerPacket reply=NewPacket(client,1009,received); AddProfile(reply,profile); TrackerProtocol.SetUInt32(reply.Values,"uid",profile.AccountId); TrackerProtocol.SetString(reply.Values,"email",profile.Email); Send(client,reply,false); } finally { logger.LogFunctionExit("TrackerServer.SendUserInfo"); } }
        private void SendUserInfoToOnline(uint recipient,uint subject,TrackerPacket received) { ClientState client; if(clientsByUser.TryGetValue(recipient,out client)) SendUserInfo(client,subject,received); }
        private void SendSingleStatus(uint recipient,uint subject,TrackerPacket received) { logger.LogFunctionEntry("TrackerServer.SendSingleStatus",recipient,subject); try { ClientState recipientClient; if(!clientsByUser.TryGetValue(recipient,out recipientClient)) return; ClientState subjectClient; uint status=clientsByUser.TryGetValue(subject,out subjectClient) ? subjectClient.Status : 0; TrackerPacket reply=NewPacket(recipientClient,1006,received); TrackerProtocol.SetUInt32(reply.Values,"userID",subject); TrackerProtocol.SetUInt32(reply.Values,"status",status); TrackerProtocol.SetUInt32(reply.Values,"sessionID",recipientClient.SessionId); TrackerProtocol.SetUInt32(reply.Values,"serverID",1); TrackerProtocol.SetBinary(reply.Values,"IP",GetServerAddressBytes()); TrackerProtocol.SetUInt32(reply.Values,"Port",(uint)port); Send(recipientClient,reply,false); } finally { logger.LogFunctionExit("TrackerServer.SendSingleStatus"); } }
        private void SendAck(ClientState client,TrackerPacket received) { Send(client,NewPacket(client,1,received),true); }
        private void SendReset(ClientState client,TrackerPacket received,string error) { TrackerPacket reply=NewPacket(client,1004,received); TrackerProtocol.SetUInt32(reply.Values,"minTime",0); TrackerProtocol.SetUInt32(reply.Values,"maxTime",1); TrackerProtocol.SetString(reply.Values,"error",error); Send(client,reply,false); }
        private TrackerPacket NewPacket(ClientState client,ushort command,TrackerPacket received) { TrackerPacket packet=new TrackerPacket(); packet.IsBeta=client.IsBeta; packet.ClientId=client.ClientId; packet.SessionId=client.SessionId; packet.Sequence=client.ServerSequence; packet.Acknowledgement=received.Sequence; packet.TotalPackets=1; packet.Command=command; packet.Values=new Dictionary<string,TrackerValue>(); return packet; }
        private void Send(ClientState client,TrackerPacket packet,bool acknowledgement) { logger.LogFunctionEntry("TrackerServer.Send",client.UserId,packet.Command,acknowledgement); try { byte[] data=TrackerProtocol.Build(packet,acknowledgement); logger.Info("Tracker SEND endpoint={0} uid={1} command={2} seq={3} ack={4}",client.EndPoint,client.UserId,packet.Command,packet.Sequence,packet.Acknowledgement); SendDatagram(data,client.EndPoint); if(!acknowledgement) client.ServerSequence++; } finally { logger.LogFunctionExit("TrackerServer.Send"); } }
        private void RemoveExpiredClients() { DateTime now=DateTime.UtcNow; List<ClientState> expired=new List<ClientState>(); foreach(ClientState client in clients.Values) if(client.ExpiresAt<now) expired.Add(client); foreach(ClientState client in expired) RemoveClient(client); }
        private void RemoveClient(ClientState client) { logger.LogFunctionEntry("TrackerServer.RemoveClient",client.UserId); try { clients.Remove(client.EndPoint.ToString()); if(client.UserId!=0) clientsByUser.Remove(client.UserId); logger.Info("Tracker client removed endpoint={0} uid={1}",client.EndPoint,client.UserId); } finally { logger.LogFunctionExit("TrackerServer.RemoveClient"); } }
        private uint NextUInt32() { byte[] data=new byte[4]; random.NextBytes(data); return (uint)(data[0]|(data[1]<<8)|(data[2]<<16)|(data[3]<<24)); }
        private byte[] BuildNetworkKey() { logger.LogFunctionEntry("TrackerServer.BuildNetworkKey"); try { RSAParameters parameters=RSAKeyManager.NetworkKey.ExportParameters(false); byte[] header=new byte[] {0x30,0x81,0x9d,0x30,0x0d,0x06,0x09,0x2a,0x86,0x48,0x86,0xf7,0x0d,0x01,0x01,0x01,0x05,0x00,0x03,0x81,0x8b,0x00,0x30,0x81,0x87,0x02,0x81,0x81,0x00}; byte[] result=new byte[header.Length+parameters.Modulus.Length+3]; Array.Copy(header,result,header.Length); Array.Copy(parameters.Modulus,0,result,header.Length,parameters.Modulus.Length); result[result.Length-3]=0x02; result[result.Length-2]=0x01; result[result.Length-1]=0x11; return result; } finally { logger.LogFunctionExit("TrackerServer.BuildNetworkKey"); } }
        private byte[] GetServerAddressBytes() { string address; if(config.TryGetValue("server_ip",out address) && !String.IsNullOrEmpty(address)) { IPAddress parsed; if(IPAddress.TryParse(address,out parsed)) return parsed.GetAddressBytes(); } return IPAddress.Loopback.GetAddressBytes(); }
        private static void AddProfile(TrackerPacket packet,TrackerProfile profile) { TrackerProtocol.SetUInt32(packet.Values,"uid",profile.AccountId); TrackerProtocol.SetString(packet.Values,"UserName",profile.UserName); TrackerProtocol.SetString(packet.Values,"FirstName",profile.FirstName); TrackerProtocol.SetString(packet.Values,"LastName",profile.LastName); }
        private static void Copy(TrackerPacket source,TrackerPacket destination,params string[] names) { foreach(string name in names) { TrackerValue value; if(source.Values.TryGetValue(name,out value)) destination.Values[name]=value; } }
        public override void Stop() { logger.LogFunctionEntry("TrackerServer.Stop"); try { base.Stop(); database.Dispose(); } finally { logger.LogFunctionExit("TrackerServer.Stop"); } }
    }
}
