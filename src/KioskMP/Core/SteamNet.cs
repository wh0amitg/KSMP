using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;

namespace KioskMP
{
	public struct LobbyInfo
	{
		public ulong id;
		public string name;
		public string desc;
		public string host;
		public int members;
		public int maxMembers;
		public ulong owner;
		public string mapMode;
		public bool hasPassword;
	}

	public static class SteamNet
	{
		public const string LobbyKey = "kioskmp";
		public const string LobbyNameKey = "name";
		public const string LobbyModeKey = "mode";

		private static readonly List<IDisposable> _callbacks = new List<IDisposable>();
		private static CallResult<LobbyCreated_t> _createResult;
		private static CallResult<LobbyEnter_t> _enterResult;
		private static CallResult<LobbyMatchList_t> _listResult;

		private static readonly HashSet<HSteamNetConnection> _outgoing = new HashSet<HSteamNetConnection>();
		private static readonly HashSet<HSteamNetConnection> _pendingIncoming = new HashSet<HSteamNetConnection>();

		private static readonly Queue<Channel> _accepted = new Queue<Channel>();
		private static readonly Queue<Channel> _connected = new Queue<Channel>();
		private static readonly Queue<string> _notices = new Queue<string>();
		private static readonly Queue<Action> _mainThread = new Queue<Action>();

		private static HSteamListenSocket _listenSocket;
		private static bool _initialized;

		private static readonly Queue<string> _warnings = new Queue<string>();
		private static SteamAPIWarningMessageHook_t _warningHook;

		private static void OnWarning(int severity, StringBuilder text)
		{
			lock (_warnings)
			{
				if (_warnings.Count < 16)
				{
					_warnings.Enqueue(severity + ": " + (text == null ? "" : text.ToString()));
				}
			}
		}

		public static bool Ready { get; private set; }

		public static CSteamID LobbyId;
		public static bool InLobby;
		public static bool IsLobbyOwner;
		public static string LastLobbyName = "";
		public static string LastLobbyDesc = "";
		public static string LastLobbyPassword = "";
		public static string JoinPassword = "";

		public static readonly List<LobbyInfo> LobbyResults = new List<LobbyInfo>();
		public static readonly List<KeyValuePair<ulong, string>> FriendList = new List<KeyValuePair<ulong, string>>();
		public static readonly List<KeyValuePair<ulong, string>> PendingInvites = new List<KeyValuePair<ulong, string>>();

		public static bool SteamReady
		{
			get
			{
				try
				{
					return Ready && SteamManager.Initialized;
				}
				catch
				{
					return false;
				}
			}
		}

		public static void Init()
		{
			if (_initialized)
			{
				return;
			}
			_initialized = true;
			try
			{
				if (!SteamManager.Initialized)
				{
					Notice("SteamAPI is not initialised - Steam lobbies disabled");
					return;
				}

				_callbacks.Add(new Callback<LobbyCreated_t>(OnLobbyCreated));
				_callbacks.Add(new Callback<LobbyEnter_t>(OnLobbyEnter));
				_callbacks.Add(new Callback<LobbyMatchList_t>(OnLobbyList));
				_callbacks.Add(new Callback<LobbyInvite_t>(OnLobbyInvite));
				_callbacks.Add(new Callback<LobbyDataUpdate_t>(OnLobbyData));
				_callbacks.Add(new Callback<LobbyChatUpdate_t>(OnLobbyChatUpdate));
				_callbacks.Add(new Callback<SteamNetConnectionStatusChangedCallback_t>(OnConnectionStatus));

				_callbacks.Add(new Callback<GameLobbyJoinRequested_t>(OnGameJoinRequested));

				_createResult = new CallResult<LobbyCreated_t>(OnLobbyCreatedResult);
				_enterResult = new CallResult<LobbyEnter_t>(OnLobbyEnterResult);
				_listResult = new CallResult<LobbyMatchList_t>(OnLobbyListResult);

				try
				{
					string persona = SteamFriends.GetPersonaName();
					if (!string.IsNullOrEmpty(persona))
					{
						Session.SelfName = persona;
					}
				}
				catch
				{
				}

				Ready = true;
				try
				{
					_warningHook = OnWarning;
					SteamClient.SetWarningMessageHook(_warningHook);
				}
				catch (Exception e)
				{
					
				}
			}
			catch (Exception e)
			{
				Ready = false;
				Notice("Steam init failed: " + e.Message);
			}
		}

		public static void Shutdown()
		{
			try
			{
				LeaveLobby();
				StopListening();
			}
			catch
			{
			}
			for (int i = 0; i < _callbacks.Count; i++)
			{
				try
				{
					_callbacks[i].Dispose();
				}
				catch
				{
				}
			}
			_callbacks.Clear();
			Ready = false;
			_initialized = false;
		}

		private static void Notice(string text)
		{
			lock (_notices)
			{
				_notices.Enqueue(text);
			}
			
		}

		public static void Post(Action a)
		{
			lock (_mainThread)
			{
				_mainThread.Enqueue(a);
			}
		}

		public static void DrainPending()
		{
			lock (_warnings)
			{
				while (_warnings.Count > 0)
				{
					string w = _warnings.Dequeue();
					
				}
			}
			while (true)
			{
				Action a;
				lock (_mainThread)
				{
					if (_mainThread.Count == 0)
					{
						break;
					}
					a = _mainThread.Dequeue();
				}
				try
				{
					a();
				}
				catch (Exception e)
				{
					
				}
			}
		}

		public static string DequeueNotice()
		{
			lock (_notices)
			{
				return _notices.Count > 0 ? _notices.Dequeue() : null;
			}
		}

		public static bool DequeueAccepted(out Channel ch)
		{
			lock (_accepted)
			{
				if (_accepted.Count == 0)
				{
					ch = null;
					return false;
				}
				ch = _accepted.Dequeue();
				return true;
			}
		}

		public static bool DequeueConnected(out Channel ch)
		{
			lock (_connected)
			{
				if (_connected.Count == 0)
				{
					ch = null;
					return false;
				}
				ch = _connected.Dequeue();
				return true;
			}
		}

		public static bool CreateLobby(string name, int maxPlayers, string desc, string password)
		{
			if (!SteamReady)
			{
				return false;
			}
			try
			{
				ELobbyType type = string.IsNullOrEmpty(password)
					? ELobbyType.k_ELobbyTypePublic
					: ELobbyType.k_ELobbyTypePrivate;
				SteamAPICall_t call = SteamMatchmaking.CreateLobby(type, maxPlayers);
				_createResult.Set(call);
				LastLobbyName = name;
				LastLobbyDesc = desc ?? "";
				LastLobbyPassword = password ?? "";
				JoinPassword = "";
				
				return true;
			}
			catch (Exception e)
			{
				Notice("CreateLobby failed: " + e.Message);
				return false;
			}
		}

		public static bool RequestLobbyList()
		{
			if (!SteamReady)
			{
				return false;
			}
			try
			{
				LobbyResults.Clear();
				SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyKey, "1", ELobbyComparison.k_ELobbyComparisonEqual);
				SteamMatchmaking.AddRequestLobbyListStringFilter("public", "1", ELobbyComparison.k_ELobbyComparisonEqual);
				SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
				SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
				SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
				_listResult.Set(call);
				return true;
			}
			catch (Exception e)
			{
				Notice("RequestLobbyList failed: " + e.Message);
				return false;
			}
		}

		public static bool JoinLobby(ulong lobbyId)
		{
			if (!SteamReady)
			{
				return false;
			}
			try
			{
				
				SteamAPICall_t call = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
				_enterResult.Set(call);
				return true;
			}
			catch (Exception e)
			{
				Notice("JoinLobby failed: " + e.Message);
				return false;
			}
		}

		public static void LeaveLobby()
		{
			if (!SteamReady || !InLobby)
			{
				return;
			}
			try
			{
				SteamMatchmaking.LeaveLobby(LobbyId);
			}
			catch
			{
			}
			InLobby = false;
			IsLobbyOwner = false;
			LobbyId = CSteamID.Nil;
			PendingInvites.Clear();
		}

		public static bool Invite(ulong steamId)
		{
			if (!SteamReady || !InLobby || !IsLobbyOwner)
			{
				Notice("Invite needs a Steam lobby you own");
				return false;
			}
			try
			{
				
				bool sent = SteamMatchmaking.InviteUserToLobby(LobbyId, new CSteamID(steamId));
				if (!sent)
				{
					SteamFriends.ActivateGameOverlayInviteDialog(LobbyId);
					Notice("Opened Steam invite dialog");
				}
				return true;
			}
			catch (Exception e)
			{
				Notice("Invite failed: " + e.Message);
				return false;
			}
		}

		public static bool OpenInviteDialog()
		{
			if (!SteamReady || !InLobby)
			{
				Notice("Host a Steam lobby first");
				return false;
			}
			try
			{
				SteamFriends.ActivateGameOverlayInviteDialog(LobbyId);
				
				return true;
			}
			catch (Exception e)
			{
				Notice("Invite dialog failed: " + e.Message);
				return false;
			}
		}

		public static void RefreshFriends()
		{
			FriendList.Clear();
			if (!SteamReady)
			{
				return;
			}
			try
			{
				int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
				for (int i = 0; i < count && FriendList.Count < 24; i++)
				{
					CSteamID id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
					FriendList.Add(new KeyValuePair<ulong, string>(id.m_SteamID, SteamFriends.GetFriendPersonaName(id)));
				}
			}
			catch (Exception e)
			{
				Notice("GetFriendCount failed: " + e.Message);
			}
		}

		public static bool StartListening()
		{
			if (!SteamReady)
			{
				return false;
			}
			try
			{
				if (_listenSocket == HSteamListenSocket.Invalid)
				{
					_listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(SteamP2PChannel.VirtualPort, 0, null);
				}
				return _listenSocket != HSteamListenSocket.Invalid;
			}
			catch (Exception e)
			{
				Notice("CreateListenSocketP2P failed: " + e.Message);
				return false;
			}
		}

		public static void StopListening()
		{
			if (_listenSocket != HSteamListenSocket.Invalid)
			{
				try
				{
					SteamNetworkingSockets.CloseListenSocket(_listenSocket);
				}
				catch
				{
				}
				_listenSocket = HSteamListenSocket.Invalid;
			}
		}

		public static bool Connect(ulong steamId)
		{
			if (!SteamReady)
			{
				return false;
			}
			try
			{
				SteamNetworkingIdentity identity = default(SteamNetworkingIdentity);
				identity.Clear();
				identity.SetSteamID(new CSteamID(steamId));
				HSteamNetConnection conn = SteamNetworkingSockets.ConnectP2P(ref identity, SteamP2PChannel.VirtualPort, 0, null);
				if (conn == HSteamNetConnection.Invalid)
				{
					Notice("ConnectP2P returned invalid connection");
					return false;
				}
				lock (_outgoing)
				{
					_outgoing.Add(conn);
				}
				
				return true;
			}
			catch (Exception e)
			{
				Notice("ConnectP2P failed: " + e.Message);
				return false;
			}
		}

		private static void OnConnectionStatus(SteamNetConnectionStatusChangedCallback_t p)
		{
			HSteamNetConnection conn = p.m_hConn;
			ESteamNetworkingConnectionState state = p.m_info.m_eState;
			ESteamNetworkingConnectionState old = p.m_eOldState;

			bool ours;
			lock (_outgoing)
			{
				ours = _outgoing.Contains(conn);
			}

			string snap = "steam p2p state=" + state + " old=" + old + " ours=" + ours
				+ " listen=" + (_listenSocket != HSteamListenSocket.Invalid);
			Post(delegate {  });

			if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting && !ours)
			{
				bool pending;
				lock (_pendingIncoming)
				{
					pending = _pendingIncoming.Contains(conn);
				}
				if (pending)
				{
					return;
				}
				if (_listenSocket != HSteamListenSocket.Invalid)
				{
					EResult res = SteamNetworkingSockets.AcceptConnection(conn);
					EResult acceptResult = res;
					Post(delegate {  });
					if (res == EResult.k_EResultOK)
					{
						lock (_pendingIncoming)
						{
							_pendingIncoming.Add(conn);
						}
					}
				}
				else
				{
					Post(delegate {  });
					SteamNetworkingSockets.CloseConnection(conn, 0, "not hosting", false);
				}
				return;
			}

			if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
			{
				bool outgoing = ours;
				Post(delegate {  });
				if (ours)
				{
					lock (_outgoing)
					{
						_outgoing.Remove(conn);
					}
					SteamP2PChannel ch = new SteamP2PChannel(conn);
					lock (_connected)
					{
						_connected.Enqueue(ch);
					}
				}
				else
				{
					bool pending;
					lock (_pendingIncoming)
					{
						pending = _pendingIncoming.Remove(conn);
					}
					if (pending)
					{
						SteamP2PChannel ch = new SteamP2PChannel(conn);
						lock (_accepted)
						{
							_accepted.Enqueue(ch);
						}
					}
				}
				return;
			}

			if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
				|| state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
			{
				lock (_pendingIncoming)
				{
					_pendingIncoming.Remove(conn);
				}
				lock (_outgoing)
				{
					_outgoing.Remove(conn);
				}
			}
		}

		private static void OnLobbyCreated(LobbyCreated_t p)
		{
		}

		private static void OnLobbyCreatedResult(LobbyCreated_t p, bool ioFailure)
		{
			if (ioFailure || p.m_eResult != EResult.k_EResultOK)
			{
				Notice("Lobby creation failed: " + (ioFailure ? "IO failure" : p.m_eResult.ToString()));
				return;
			}
			LobbyId = new CSteamID(p.m_ulSteamIDLobby);
			try
			{
				SteamMatchmaking.SetLobbyData(LobbyId, LobbyKey, "1");
				SteamMatchmaking.SetLobbyData(LobbyId, LobbyNameKey, LastLobbyName);
				SteamMatchmaking.SetLobbyData(LobbyId, LobbyModeKey, "coop");
				SteamMatchmaking.SetLobbyData(LobbyId, "desc", LastLobbyDesc);
				SteamMatchmaking.SetLobbyData(LobbyId, "pwd", LastLobbyPassword);
				SteamMatchmaking.SetLobbyData(LobbyId, "public", string.IsNullOrEmpty(LastLobbyPassword) ? "1" : "0");
			}
			catch (Exception e)
			{
				Notice("SetLobbyData failed: " + e.Message);
			}
			InLobby = true;
			IsLobbyOwner = true;
			Notice("Lobby created: " + p.m_ulSteamIDLobby);
			Post(delegate {  });
			Post(delegate
			{
				Session.OnSteamLobbyCreated();
			});
		}

		private static void OnLobbyEnter(LobbyEnter_t p)
		{
		}

		private static void OnLobbyEnterResult(LobbyEnter_t p, bool ioFailure)
		{
			if (ioFailure || p.m_EChatRoomEnterResponse != 1)
			{
				Notice("Lobby enter failed: " + (ioFailure ? "IO failure" : ("response " + p.m_EChatRoomEnterResponse)));
				return;
			}
			LobbyId = new CSteamID(p.m_ulSteamIDLobby);
			string needPwd = SteamMatchmaking.GetLobbyData(LobbyId, "pwd");
			if (!string.IsNullOrEmpty(JoinPassword) && needPwd != JoinPassword)
			{
				Notice("Wrong lobby password");
				SteamMatchmaking.LeaveLobby(LobbyId);
				LobbyId = CSteamID.Nil;
				return;
			}
			InLobby = true;
			PendingInvites.Clear();
			ulong owner = 0;
			try
			{
				owner = SteamMatchmaking.GetLobbyOwner(LobbyId).m_SteamID;
				IsLobbyOwner = owner == SteamUser.GetSteamID().m_SteamID;
			}
			catch
			{
			}
			Notice("Entered lobby " + p.m_ulSteamIDLobby + (IsLobbyOwner ? " (owner)" : " (owner " + owner + ")"));
			ulong joined = p.m_ulSteamIDLobby;
			Post(delegate {  });
			Post(delegate
			{
				Session.OnSteamLobbyJoined(owner);
			});
		}

		private static void OnLobbyList(LobbyMatchList_t p)
		{
		}

		private static void OnLobbyListResult(LobbyMatchList_t p, bool ioFailure)
		{
			LobbyResults.Clear();
			if (ioFailure)
			{
				Notice("Lobby list failed (IO)");
				return;
			}
			try
			{
				for (uint i = 0; i < p.m_nLobbiesMatching; i++)
				{
					CSteamID id = SteamMatchmaking.GetLobbyByIndex((int)i);
					LobbyInfo info = new LobbyInfo();
					info.id = id.m_SteamID;
					info.name = SteamMatchmaking.GetLobbyData(id, LobbyNameKey);
					info.desc = SteamMatchmaking.GetLobbyData(id, "desc");
					info.mapMode = SteamMatchmaking.GetLobbyData(id, LobbyModeKey);
					info.members = SteamMatchmaking.GetNumLobbyMembers(id);
					info.maxMembers = SteamMatchmaking.GetLobbyMemberLimit(id);
					info.owner = SteamMatchmaking.GetLobbyOwner(id).m_SteamID;
					info.hasPassword = SteamMatchmaking.GetLobbyData(id, "pwd").Length > 0;
					try
					{
						info.host = SteamFriends.GetFriendPersonaName(new CSteamID(info.owner));
					}
					catch
					{
						info.host = "";
					}
					if (string.IsNullOrEmpty(info.name))
					{
						info.name = "Lobby " + id.m_SteamID;
					}
					LobbyResults.Add(info);
				}
			}
			catch (Exception e)
			{
				Notice("Read lobby list failed: " + e.Message);
			}
			LobbyResults.Sort(delegate(LobbyInfo a, LobbyInfo b)
			{
				int c = b.members.CompareTo(a.members);
				if (c != 0)
				{
					return c;
				}
				return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
			});
			Notice("Found " + LobbyResults.Count + " lobby(s)");
		}

		private static void OnLobbyInvite(LobbyInvite_t p)
		{
			Post(delegate
			{
				for (int i = 0; i < PendingInvites.Count; i++)
				{
					if (PendingInvites[i].Key == p.m_ulSteamIDLobby)
					{
						return;
					}
				}
				string from = "";
				try
				{
					from = SteamFriends.GetFriendPersonaName(new CSteamID(p.m_ulSteamIDUser));
				}
				catch
				{
				}
				PendingInvites.Add(new KeyValuePair<ulong, string>(p.m_ulSteamIDLobby, from));
				Notice("Lobby invite from " + from);
				
			});
		}

		private static void OnGameJoinRequested(GameLobbyJoinRequested_t p)
		{
			Post(delegate
			{
				ulong lobby = p.m_steamIDLobby.m_SteamID;
				
				Notice("Steam overlay wants us to join lobby " + lobby);
				if (Session.Active)
				{
					Notice("Leaving current session to join ...");
					Session.Disconnect();
				}
				PendingInvites.Clear();
				JoinLobby(lobby);
			});
		}

		public static bool AcceptInvite(ulong lobbyId)
		{
			PendingInvites.Clear();
			return JoinLobby(lobbyId);
		}

		public static void DeclineInvite(ulong lobbyId)
		{
			PendingInvites.RemoveAll(delegate(KeyValuePair<ulong, string> kv) { return kv.Key == lobbyId; });
		}

		private static void OnLobbyData(LobbyDataUpdate_t p)
		{
		}

		private static void OnLobbyChatUpdate(LobbyChatUpdate_t p)
		{
			Post(delegate
			{
				Session.OnSteamLobbyMembersChanged();
			});
		}
	}
}
