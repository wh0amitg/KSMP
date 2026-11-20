using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace KioskMP
{
	public enum NetRole : byte
	{
		None = 0,
		Host = 1,
		Client = 2
	}

	public sealed class PeerInfo
	{
		public int id;
		public string name = "?";
		public Channel channel;
		public float lastSeen;
		public float rtt;
	}

	public static class Session
	{
		public const float HelloTimeout = 15f;
		public const float IdleTimeout = 45f;

		public static NetRole Role = NetRole.None;
		public static int SelfId;
		public static string SelfName = "Player";

		public static readonly Dictionary<int, PeerInfo> Peers = new Dictionary<int, PeerInfo>();
		public static string StatusText = "";
		public static string LastError = "";

		private static TcpListener _listener;
		private static Channel _serverChannel;
		private static readonly List<Channel> _awaiting = new List<Channel>();
		private static readonly List<Channel> _steamAwaiting = new List<Channel>();
		private static float _connectDeadline;
		private static bool _wantTcp;
		private static int _tcpPort = 27890;
		private static string _pendingTcpHost = "";
		private static int _pendingTcpPort;
		private static ulong _pendingSteamHost;
		private static bool _hasPendingSteamHost;

		public static bool IsHost   { get { return Role == NetRole.Host; } }
		public static bool IsClient { get { return Role == NetRole.Client; } }
		public static bool Active   { get { return Role != NetRole.None; } }

		public static int PlayerCount { get { return Peers.Count + (Active ? 1 : 0); } }
		public static int TcpPort    { get { return _tcpPort; } }

		public static List<PeerInfo> PeerList()
		{
			List<PeerInfo> list = new List<PeerInfo>(Peers.Values);
			list.Sort(delegate(PeerInfo a, PeerInfo b) { return a.id.CompareTo(b.id); });
			return list;
		}

		public static bool HostStart(int port, bool useSteam)
		{
			if (Active) return false;
			Reset();
			Role = NetRole.Host;
			SelfId = 0;
			_tcpPort = port;
			_wantTcp = !useSteam;
			
			if (useSteam && SteamNet.SteamReady)
			{
				
			}

			try
			{
				if (!useSteam)
				{
					_listener = new TcpListener(IPAddress.Any, port);
					_listener.Start();
					_status("Hosting on port " + port);
				}
			}
			catch (Exception e)
			{
				LastError = "Listen failed: " + e.Message;
				
				Reset();
				return false;
			}

			if (useSteam && SteamNet.SteamReady)
			{
				if (SteamNet.StartListening())
					_status("Hosting via Steam P2P");
				else
				{
					LastError = "Steam P2P listen failed";
					StopHostSockets();
					Reset();
					return false;
				}
			}
			return true;
		}

		private static void StopHostSockets()
		{
			if (_listener != null)
			{
				try { _listener.Stop(); } catch { }
				_listener = null;
			}
			SteamNet.StopListening();
		}

		public static bool ClientConnectTcp(string host, int port)
		{
			if (Active) return false;
			Reset();
			_pendingTcpHost = host;
			_pendingTcpPort = port;
			_connectDeadline = Time.realtimeSinceStartup + 10f;
			StatusText = "Connecting to " + host + ":" + port + " ...";
			

			System.Threading.ThreadPool.QueueUserWorkItem(delegate(object o)
			{
				try
				{
					TcpClient client = new TcpClient();
					client.NoDelay = true;
					client.Connect(host, port);
					Post(delegate
					{
						if (Role != NetRole.None) { client.Close(); return; }
						Role = NetRole.Client;
						SelfId = -1;
						TcpChannel ch = new TcpChannel(client);
						ch.AwaitingHandshake = false;
						_serverChannel = ch;
						ch.Send(MsgBuild.Hello(SelfName, 0));
						_status("Handshaking ...");
					});
				}
				catch (Exception e)
				{
					Post(delegate
					{
						LastError = "Connect failed: " + e.Message;
						
						StatusText = LastError;
						if (Role == NetRole.Client && SelfId < 0) Disconnect();
					});
				}
			});
			return true;
		}

		public static bool ClientConnectSteam(ulong steamId)
		{
			if (Active || !SteamNet.SteamReady) return false;

			if (_hasPendingSteamHost) return false;
			Reset();
			Role = NetRole.Client;
			SelfId = -1;
			_pendingSteamHost = steamId;
			_hasPendingSteamHost = true;
			StatusText = "Connecting via Steam P2P ...";
			
			
			if (!SteamNet.Connect(steamId))
			{
				StatusText = "Steam connect failed";
				_hasPendingSteamHost = false;
				Role = NetRole.None;
				
				Game.AddToast("Steam connect failed - ask the host to start a Steam lobby");
				return false;
			}
			_connectDeadline = Time.realtimeSinceStartup + 15f;
			return true;
		}

		private static void Post(Action a)
		{
			lock (_pendingActions) { _pendingActions.Enqueue(a); }
		}

		private static readonly Queue<Action> _pendingActions = new Queue<Action>();

		private static void DrainActions()
		{
			while (true)
			{
				Action a;
				lock (_pendingActions)
				{
					if (_pendingActions.Count == 0) break;
					a = _pendingActions.Dequeue();
				}
				try { a(); }
				catch (Exception e) {  }
			}
		}

		public static void Update()
		{
			DrainActions();
			SteamNet.DrainPending();

			Channel ch;
			while (SteamNet.DequeueConnected(out ch))
			{
				if (Role == NetRole.Client && _serverChannel == null && _hasPendingSteamHost)
				{
					_roleToClient(ch);
				}
				else
				{
					
					ch.Close();
				}
			}
			while (SteamNet.DequeueAccepted(out ch))
			{
				if (Role == NetRole.Host)
					_awaiting.Add(ch);
				else
					ch.Close();
			}

			if (Role == NetRole.Host)
				AcceptTcp();
			else if (Role == NetRole.Client && _serverChannel == null
				&& ((_pendingTcpHost.Length > 0 && Time.realtimeSinceStartup > _connectDeadline)
					|| (_hasPendingSteamHost && Time.realtimeSinceStartup > _connectDeadline)))
			{
				LastError = _hasPendingSteamHost ? "Steam P2P connection timed out" : "Connection timed out";
				StatusText = LastError;
				
				Game.AddToast(LastError + " - try the invite again");
				Disconnect();
				return;
			}

			for (int i = _awaiting.Count - 1; i >= 0; i--)
			{
				Channel c = _awaiting[i];
				PumpOne(c);
				if (!c.Connected) { c.Close(); _awaiting.RemoveAt(i); continue; }
				if (Time.realtimeSinceStartup - c.LastSeen > HelloTimeout)
				{
					c.Close();
					_awaiting.RemoveAt(i);
				}
			}

			if (_serverChannel != null)
			{
				PumpOne(_serverChannel);
				if (!_serverChannel.Connected) OnChannelLost(_serverChannel);
			}

			if (Role == NetRole.Host)
			{
				List<int> dead = null;
				List<string> why = null;
				foreach (KeyValuePair<int, PeerInfo> kv in Peers)
				{
					PeerInfo p = kv.Value;
					PumpOne(p.channel);

					float seen = p.channel != null ? p.channel.LastSeen : p.lastSeen;
					if (!p.channel.Connected || Time.realtimeSinceStartup - seen > IdleTimeout)
					{
						if (dead == null) { dead = new List<int>(); why = new List<string>(); }
						dead.Add(kv.Key);
						string detail = p.channel != null ? p.channel.EndDetail() : "";
						why.Add(!p.channel.Connected
							? ("link lost (" + p.channel.Label + (detail.Length > 0 ? ", " + detail : "") + ")")
							: ("no data for " + (int)IdleTimeout + "s"));
					}
				}
				if (dead != null)
					for (int i = 0; i < dead.Count; i++) DropPeer(dead[i], why[i]);
			}
			else if (Role == NetRole.Client && _serverChannel != null
				&& Time.realtimeSinceStartup - _serverChannel.LastSeen > IdleTimeout)
			{
				LastError = "Host is not responding (" + (int)IdleTimeout + "s)";
				StatusText = LastError;
				
				Disconnect();
			}
		}

		private static void _roleToClient(Channel ch)
		{
			Role = NetRole.Client;
			SelfId = -1;
			_serverChannel = ch;
			ch.AwaitingHandshake = false;
			ch.Send(MsgBuild.Hello(SelfName, 1));
			_status("Handshaking (Steam) ...");
			
		}

		private static void AcceptTcp()
		{
			if (_listener == null) return;
			try
			{
				while (_listener.Pending())
				{
					TcpClient client = _listener.AcceptTcpClient();
					client.NoDelay = true;
					TcpChannel ch = new TcpChannel(client);
					ch.IsServerSide = true;
					_awaiting.Add(ch);
					
				}
			}
			catch (Exception e) {  }
		}

		private static void PumpOne(Channel ch)
		{
			if (ch == null)
			{
				return;
			}
			try
			{
				ch.Pump();
			}
			catch (Exception e)
			{

				
				try
				{
					ch.Close();
				}
				catch
				{
				}
				return;
			}
			byte[] msg;
			while (ch.TryDequeue(out msg))
			{
				try { Dispatch(ch, msg); }
				catch (Exception e)
				{
					
				}
			}
		}

		private static void Dispatch(Channel ch, byte[] msg)
		{
			if (msg == null || msg.Length == 0) return;
			ch.LastSeen = Time.realtimeSinceStartup;
			using (PacketReader r = new PacketReader(msg))
			{
				Msg type = (Msg)r.Type;
				if (ch.AwaitingHandshake)
				{
					if (type != Msg.Hello) return;
					HandleHello(ch, r);
					return;
				}

				if (Role == NetRole.Client && ch == _serverChannel)
				{
					if (type == Msg.Welcome) { HandleWelcome(r); return; }
					if (type == Msg.Reject)
					{
						LastError = "Host rejected: " + r.Str();
						StatusText = LastError;
						
						Disconnect();
						return;
					}
				}
				Game.HandleMessage(ch.PeerId, type, r);
			}
		}

		private static void HandleHello(Channel ch, PacketReader r)
		{
			byte ver = r.U8();
			string name = r.Str();
			string codes = "";
			for (int ci = 0; ci < name.Length && ci < 24; ci++)
			{
				codes += (ci > 0 ? "," : "") + ((int)name[ci]).ToString();
			}
			
			byte transport = r.U8();
			if (ver != Proto.Version)
			{
				ch.Send(MsgBuild.Reject("protocol version mismatch (host=" + Proto.Version + ", peer=" + ver + ")"));
				return;
			}
			if (Role != NetRole.Host)
			{
				ch.Send(MsgBuild.Reject("not hosting"));
				return;
			}
			if (Peers.Count + 1 >= Proto.MaxPlayers)
			{
				ch.Send(MsgBuild.Reject("lobby is full (max " + Proto.MaxPlayers + ")"));
				return;
			}
			int id = 1;
			while (Peers.ContainsKey(id)) id++;
			ch.AwaitingHandshake = false;
			ch.PeerId = id;
			PeerInfo info = new PeerInfo();
			info.id = id;
			info.name = string.IsNullOrEmpty(name) ? ("Player" + id) : FixPeerName(name);
			info.channel = ch;
			info.lastSeen = Time.realtimeSinceStartup;
			Peers[id] = info;
			
			

			List<PeerInfo> roster = PeerList();
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(id);
				w.Str(SelfName);
				Game.WriteLobbyState(w);
				w.I32(roster.Count);
				for (int i = 0; i < roster.Count; i++)
				{
					if (roster[i].id == id) continue;
					w.I32(roster[i].id);
					w.Str(roster[i].name);
				}
				w.I32(0);
				w.Str(SelfName);
				ch.Send(w.ToMessage(Msg.Welcome));
			}
			Broadcast(MsgBuild.PeerJoined(id, info.name), id);
			ch.Send(Game.BuildSnapshot());
		}

		private static void HandleWelcome(PacketReader r)
		{

			if (SelfId >= 0) {  return; }

			SelfId = r.I32();
			string hostName = r.Str();
			Game.ReadLobbyState(r);
			Peers.Clear();
			int n = r.I32();
			for (int i = 0; i < n; i++)
			{
				int pid = r.I32();
				string pname = r.Str();
				PeerInfo info = new PeerInfo();
				info.id = pid;
				info.name = FixPeerName(pname);
				Peers[pid] = info;
			}
			_status("Connected as '" + SelfName + "' (id " + SelfId + "), host: " + hostName);
			
			
		}

		public static void OnHandshakeComplete()
		{
			Game.EnsureSelfChar();
			Game.SendMyCustomize();
		}

		internal static string FixPeerName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return name;
			}
			bool suspect = false;
			for (int i = 0; i < name.Length; i++)
			{
				int c = (int)name[i];
				if (c == 0xD0 || c == 0xD1 || (c >= 0x80 && c <= 0xBF))
				{
					suspect = true;
					break;
				}
			}
			if (!suspect)
			{
				return name;
			}
			try
			{
				byte[] bytes = new byte[name.Length];
				for (int i = 0; i < name.Length; i++)
				{
					int c = (int)name[i];
					bytes[i] = c <= 0xFF ? (byte)c : (byte)'?';
				}
				string repaired = System.Text.Encoding.UTF8.GetString(bytes);
				if (!repaired.Contains("\uFFFD"))
				{
					
					return repaired;
				}
			}
			catch
			{
			}
			return name;
		}

		private static void SendOn(Channel ch, byte[] msg)
		{
			if (ch != null && ch.Connected) ch.Send(msg);
		}

		public static void SendToHost(byte[] msg)
		{
			if (Role == NetRole.Client) SendOn(_serverChannel, msg);
		}

		public static void SendTo(int peerId, byte[] msg)
		{
			if (Role != NetRole.Host) return;
			PeerInfo p;
			if (Peers.TryGetValue(peerId, out p)) SendOn(p.channel, msg);
		}

		public static void Broadcast(byte[] msg, int exceptPeerId = -1)
		{
			if (Role != NetRole.Host) return;
			foreach (KeyValuePair<int, PeerInfo> kv in Peers)
			{
				if (kv.Key == exceptPeerId) continue;
				SendOn(kv.Value.channel, msg);
			}
		}

		public static void RelayFrom(int fromPeerId, byte[] msg)
		{
			if (Role == NetRole.Host) Broadcast(msg, fromPeerId);
		}

		public static void KickPeer(int peerId, string reason)
		{
			if (Role != NetRole.Host) return;
			PeerInfo p;
			if (!Peers.TryGetValue(peerId, out p)) return;
			string why = string.IsNullOrEmpty(reason) ? "kicked by host" : reason;
			try { p.channel.Send(MsgBuild.Kick(peerId, why)); } catch { }
			try { p.channel.Flush(); } catch { }
			DropPeer(peerId, why);
			
		}

		public static void DropPeer(int peerId, string reason)
		{
			PeerInfo p;
			if (!Peers.TryGetValue(peerId, out p)) return;
			Peers.Remove(peerId);
			try { p.channel.Close(); } catch { }
			
			Broadcast(MsgBuild.PeerLeft(peerId));
			Game.OnPeerLeft(peerId, reason);
		}

		public static void OnChannelLost(Channel ch)
		{
			if (Role == NetRole.Client && ch == _serverChannel)
			{
				string detail = ch.EndDetail();
				LastError = "Host link lost (" + ch.Label + (detail.Length > 0 ? ", " + detail : "") + ")";
				StatusText = LastError;
				
				Disconnect();
			}
		}

		public static void Disconnect()
		{
			bool wasActive = Active;
			int oldSelf = SelfId;
			NetRole oldRole = Role;
			StopHostSockets();
			if (_serverChannel != null)
			{
				try { _serverChannel.Close(); } catch { }
			}
			for (int i = 0; i < _awaiting.Count; i++)
			{
				try { _awaiting[i].Close(); } catch { }
			}
			foreach (KeyValuePair<int, PeerInfo> kv in Peers)
			{
				try { kv.Value.channel.Close(); } catch { }
			}
			bool wasLobby = SteamNet.InLobby;
			SteamNet.LeaveLobby();
			Reset();
			if (wasActive) Game.OnSessionEnded(oldRole, oldSelf, wasLobby);
			StatusText = "Disconnected";

			if (wasActive)
				Game.AddToast(string.IsNullOrEmpty(LastError) ? "Disconnected" : LastError);
		}

		public static void Reset()
		{
			Role = NetRole.None;
			SelfId = 0;
			Peers.Clear();
			_awaiting.Clear();
			_serverChannel = null;
			_listener = null;
			_pendingTcpHost = "";
			_hasPendingSteamHost = false;
			_wantTcp = false;
			Game.ResetLocalGameState();
		}

		private static void _status(string s)
		{
			StatusText = s;
			
		}

		public static void OnSteamLobbyCreated()
		{
			bool ok = SteamNet.StartListening();
			
			if (Role == NetRole.Host && ok)
				_status("Steam lobby open, waiting for friends ...");
		}

		public static void OnSteamLobbyJoined(ulong owner)
		{

			if (Role != NetRole.None || _hasPendingSteamHost) return;

			ulong self = 0;
			try { self = Steamworks.SteamUser.GetSteamID().m_SteamID; } catch { }
			if (owner == self || owner == 0) return;

			
			ClientConnectSteam(owner);
		}

		public static void OnSteamLobbyMembersChanged() { }

		public static string DescribePeers()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(SelfName).Append(" (you)");
			foreach (PeerInfo p in PeerList())
				sb.Append(", ").Append(p.name);
			return sb.ToString();
		}
	}
}
