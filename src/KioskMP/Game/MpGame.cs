using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KioskMP
{

	public static class NetCtx
	{

		public static bool SuppressUiSend;

		public static bool ApplyingRemote;

		public static bool FromRemoteInteract;

		public static bool CustomerDelivery;

		public static bool RemoteInteractPhone;

		public static bool ApplyingDialog;

		public static bool ApplyingGoAway;

		public static int DialogOwnerOverride = -1;

		public static int RemoteQuietFrame = -1;

		public static int PendingFollowup = Game.FU_NONE;

		public static UiAction LastUiAction;

		public static void Reset()
		{
			SuppressUiSend = false;
			ApplyingRemote = false;
			FromRemoteInteract = false;
			ApplyingDialog = false;
			ApplyingGoAway = false;
			RemoteInteractPhone = false;
			DialogOwnerOverride = -1;
			RemoteQuietFrame = -1;
			PendingFollowup = Game.FU_NONE;
			LastUiAction = UiAction.Run;
		}
	}

	public enum UiAction : byte
	{
		Skip = 0,
		Run = 1,
		RunBroadcast = 2
	}

	public static partial class Game
	{
		public const int FU_NONE = -1;
		public const int FU_STARTMODE = -2;

		public static bool SceneReady;

		public static bool NeedRebuild = true;

		public static bool GameRunning;

		public static bool MidJoinHandled;

		public static byte GameMode;
		public static int CurrentDay;
		public static int CurrentCustomer;

		public static readonly List<string> ChatLog = new List<string>();
		public static readonly List<string> ToastLog = new List<string>();
		private static readonly List<float> _toastTime = new List<float>();
		private static readonly float[] _lastUiRun = new float[16];
		private static float _lastPing;
		private static float _lastTimerSend;
		private static float _lastCustStateSend;
		private static long _pingStamp;

		public static void AddToast(string s)
		{
			if (ToastLog.Count >= 6)
			{
				ToastLog.RemoveAt(0);
				_toastTime.RemoveAt(0);
			}
			ToastLog.Add(s);
			_toastTime.Add(Time.realtimeSinceStartup + 5f);
			
		}

		public static void AddChat(string s)
		{
			if (ChatLog.Count > 60)
			{
				ChatLog.RemoveAt(0);
			}
			ChatLog.Add(s);
		}

		public static void SendChat(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			if (text[0] == '/')
			{
				PropFX.Command(text);
				return;
			}
			if (!Session.Active)
			{
				return;
			}
			AddChat(Session.SelfName + ": " + text);
			byte[] m = MsgBuild.Chat(Session.SelfId, text);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void PruneToasts()
		{
			for (int i = ToastLog.Count - 1; i >= 0; i--)
			{
				if (Time.realtimeSinceStartup > _toastTime[i])
				{
					ToastLog.RemoveAt(i);
					_toastTime.RemoveAt(i);
				}
			}
		}

		public static int FollowupOf(UiCommand cmd)
		{
			switch (cmd)
			{
				case UiCommand.StartStory:
				case UiCommand.StartEndless:
				case UiCommand.StartRelax:
				case UiCommand.Continue:
					return (int)UiCommand.NextDay;
				case UiCommand.RestartEndless:
					return FU_STARTMODE;
				case UiCommand.CallOverEndless:
					return (int)UiCommand.RestartEndless;
				default:
					return FU_NONE;
			}
		}

		public static void SetFollowup(UiCommand cmd)
		{
			NetCtx.PendingFollowup = FollowupOf(cmd);
		}

		public static bool MatchesFollowup(UiCommand cmd)
		{
			int p = NetCtx.PendingFollowup;
			if (p == FU_NONE)
			{
				return false;
			}
			if (p == FU_STARTMODE)
			{
				return cmd == UiCommand.StartEndless || cmd == UiCommand.StartRelax;
			}
			return p == (int)cmd;
		}

		public static UiAction UiGate(UiCommand cmd)
		{
			if (!Session.Active)
			{
				return UiAction.Run;
			}
			if (NetCtx.SuppressUiSend)
			{
				return UiAction.Run;
			}
			if (!Session.IsHost && IsGuestBlockedStart(cmd))
			{
				return UiAction.Skip;
			}
			int c = (int)cmd;
			if (MatchesFollowup(cmd))
			{
				SetFollowup(cmd);
				_lastUiRun[c] = Time.realtimeSinceStartup;
				return UiAction.Run;
			}
			float now = Time.realtimeSinceStartup;
			if (c >= 0 && c < _lastUiRun.Length && now - _lastUiRun[c] < 1.5f)
			{
				return UiAction.Skip;
			}
			if (Session.IsHost)
			{
				SetFollowup(cmd);
				if (c >= 0 && c < _lastUiRun.Length)
				{
					_lastUiRun[c] = now;
				}
				return UiAction.RunBroadcast;
			}
			Session.SendToHost(MsgBuild.UiCmd(cmd));
			return UiAction.Skip;
		}

		public static void HandleMessage(int fromPeerId, Msg type, PacketReader r)
		{
			switch (type)
			{
				case Msg.Chat:
				{
					int from = r.I32();
					string text = r.Str();
					if (Session.IsHost)
					{
						Session.Broadcast(MsgBuild.Chat(from, text), fromPeerId);
					}
					string name = PeerName(from);
					AddChat(name + ": " + text);
					break;
				}
				case Msg.PeerJoined:
				{
					int id = r.I32();
					string name = r.Str();
					AddToast(name + " joined");
					OnPeerJoined(id, name);
					break;
				}
				case Msg.PeerLeft:
				{
					int id = r.I32();
					OnPeerLeft(id);
					break;
				}
				case Msg.Kick:
				{
					if (Session.IsHost)
					{
						break;
					}
					int target = r.I32();
					string why = r.Str();
					if (target == Session.SelfId)
					{
						HandleKicked(why);
					}
					break;
				}
				case Msg.PlayerState:
					ApplyPlayerState(r);
					break;

				case Msg.ObjState:
					ApplyState(r, fromPeerId);
					break;
				case Msg.ObjClaim:
					HandleClaim(fromPeerId, r);
					break;
				case Msg.ObjGrant:
					HandleGrant(r);
					break;
				case Msg.ObjSpawn:
					HandleSpawn(r, fromPeerId);
					break;
				case Msg.ObjSpawnLocal:
					HandleSpawnLocal(fromPeerId, r);
					break;
				case Msg.ObjDespawn:
					HandleDespawn(r, fromPeerId);
					break;
				case Msg.Interact:
					HandleInteract(fromPeerId, r);
					break;
				case Msg.AddAddition:
					HandleAddAddition(fromPeerId, r);
					break;

				case Msg.CustSpawn:
					HandleCustSpawn(r);
					break;
				case Msg.CustState:
					HandleCustState(r);
					break;
				case Msg.CustGoAway:
					HandleCustGoAway(r);
					break;
				case Msg.CustGoAwayReq:
					HandleCustGoAwayReq(fromPeerId, r);
					break;
				case Msg.DialogSync:
					HandleDialogSync(fromPeerId, r);
					break;
				case Msg.ObjCut:
					HandleCut(r, fromPeerId);
					break;
				case Msg.TriggerEvt:
					HandleTriggerEvt(fromPeerId, r);
					break;
				case Msg.OrderDone:
					HandleOrderDone(r);
					break;
				case Msg.TimerSync:
					HandleTimerSync(r);
					break;
				case Msg.BoardSync:
					HandleBoardSync(r);
					break;
				case Msg.UiCmd:
					HandleUiCmd(fromPeerId, r);
					break;
				case Msg.Reaction:
					ReactionNet.HandleReaction(fromPeerId, r);
					break;
				case Msg.Customize:
					HandleCustomize(fromPeerId, r);
					break;
				case Msg.Dance:
					HandleDance(fromPeerId, r);
					break;
				case Msg.PropSpawn:
					HandlePropSpawn(fromPeerId, r);
					break;
				case Msg.DecalBatch:
					HandleDecalBatch(fromPeerId, r);
					break;
				case Msg.SauceSync:
					HandleSauceSync(fromPeerId, r);
					break;
				case Msg.FryerStart:
					HandleFryerStart(fromPeerId, r);
					break;
				case Msg.FryerEmpty:
					HandleFryerEmpty(fromPeerId, r);
					break;
				case Msg.MarkerColor:
					HandleMarkerColor(fromPeerId, r);
					break;
				case Msg.FriesScoop:
					HandleFriesScoop(fromPeerId, r);
					break;

				case Msg.Snapshot:
					ApplySnapshot(r);
					break;
				case Msg.WorldReset:
					r.U8();
					HandleWorldReset();
					break;

				case Msg.Ping:
				{
					long t = r.I64();
					if (Session.IsHost)
					{
						Session.Broadcast(MsgBuild.Pong(t), fromPeerId);
					}
					else
					{
						Session.SendToHost(MsgBuild.Pong(t));
					}
					break;
				}
				case Msg.Pong:
				{
					long t = r.I64();
					long ms = (DateTime.UtcNow.Ticks - t) / TimeSpan.TicksPerMillisecond;
					PeerInfo p;
					if (Session.Peers.TryGetValue(fromPeerId, out p))
					{
						p.rtt = p.rtt <= 0f ? ms : p.rtt * 0.7f + ms * 0.3f;
					}
					break;
				}
				case Msg.LobbyReady:
					HandleLobbyReady(fromPeerId, r);
					break;
				case Msg.LobbySync:
					HandleLobbySync(r);
					break;
				default:
					
					break;
			}
		}

		public static string PeerName(int id)
		{
			if (id == Session.SelfId)
			{
				return Session.SelfName;
			}
			PeerInfo p;
			if (Session.Peers.TryGetValue(id, out p))
			{
				return p.name;
			}
			return id == 0 ? "Host" : ("P" + id);
		}

		public static void WriteLobbyState(PacketWriter w)
		{
			EnsureSelfChar();
			w.Bool(GameRunning);
			w.U8(GameMode);
			w.I32(CurrentDay);
			w.I32(CurrentCustomer);
			w.I32(GameManager.instance != null ? GameManager.instance.ordersCompleted : 0);
			w.Bool(MidJoinHandled);
			WriteCharsState(w);
		}

		public static void ReadLobbyState(PacketReader r)
		{
			GameRunning = r.Bool();
			GameMode = r.U8();
			CurrentDay = r.I32();
			CurrentCustomer = r.I32();
			int orders = r.I32();
			r.Bool();
			if (GameManager.instance != null)
			{
				GameManager.instance.ordersCompleted = orders;
				GameManager.instance.gameMode = (GameManager.GameModes)GameMode;
				GameManager.instance.currentDay = CurrentDay;
				GameManager.instance.currentCustomer = CurrentCustomer;
			}
			ReadCharsState(r);
			_lateJoin = GameRunning;
		}

		private static bool _lateJoin;

		public static byte[] BuildSnapshot()
		{
			using (PacketWriter w = new PacketWriter())
			{
				WriteLobbyState(w);
				WriteObjectsSnapshot(w);
				WriteFlowSnapshot(w);
				return MsgBuild.Snapshot(w.ToMessage(Msg.Snapshot));
			}
		}

		private static void ApplySnapshot(PacketReader outer)
		{
			byte[] payload = outer.Bytes();
			if (payload.Length < 1 || payload[0] != (byte)Msg.Snapshot)
			{
				
				return;
			}
			using (PacketReader r = new PacketReader(payload))
			{
				ReadLobbyState(r);
				ReadObjectsSnapshot(r);
				ReadFlowSnapshot(r);
			}
			Session.OnHandshakeComplete();
			AddToast("Joined session (" + Session.PlayerCount + " players)");
			
			if (_lateJoin)
			{
				RunMidJoinFlow();
			}
			_lateJoin = false;
		}

		public static void RunMidJoinFlow()
		{
			if (MidJoinHandled || !SceneReady)
			{
				return;
			}
			UIController u = ActiveUiController();
			if (u == null)
			{
				return;
			}
			MidJoinHandled = true;
			_deferredUi = -1;
			_deferredUiBroadcast = false;
			
			
			try
			{
				NetCtx.SuppressUiSend = true;
				try
				{
					u.NextDayScreen(true);
				}
				finally
				{
					NetCtx.SuppressUiSend = false;
				}
			}
			catch (Exception e)
			{
				
				MidJoinHandled = false;
			}
		}

		public static void HandleKicked(string reason)
		{
			string why = string.IsNullOrEmpty(reason) ? "kicked by host" : reason;
			Session.LastError = why;
			Session.StatusText = "Kicked: " + why;
			AddToast("Kicked from session: " + why);
			
			Session.Disconnect();
		}

		public static void OnPeerLeft(int peerId, string reason = null)
		{
			AddToast(PeerName(peerId) + " left" + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"));
			DropAvatar(peerId);
			ReleasePeerObjects(peerId);
		}

		public static void OnSessionEnded(NetRole oldRole, int oldSelf, bool wasLobby)
		{
			NetCtx.Reset();
			ReactionNet.Reset();
			ResetObjects();
			ResetPlayers();
			PropFX.ClearAll();
			ResetTriggerRelay();
			Game.DialogOwner = -1;
			Game.EnsureLoudDialog();
			Game._hasPendingCustState = false;
			GameRunning = false;
			ResetLobbyReady();
			PendingStoryDelivery = false;
			PendingStoryOwner = -1;
			MidJoinHandled = false;
			_lateJoin = false;
			_deferredUi = -1;
			_deferredUiBroadcast = false;
			ChatLog.Clear();
			ToastLog.Clear();
			for (int i = 0; i < _lastUiRun.Length; i++)
			{
				_lastUiRun[i] = 0f;
			}
			
			
		}

		public static void HandleWorldReset()
		{
			if (!SceneReady)
			{
				NeedRebuild = true;
				return;
			}
			RebuildTable();
			RebuildPlayers();
		}

		public static void OnSceneLoaded(Scene scene)
		{
			
			
			NeedRebuild = true;
			SceneReady = false;
			MidJoinHandled = false;
			ClearAvatars();
			if (Session.Active && Session.IsHost)
			{
				Session.Broadcast(MsgBuild.WorldReset(0));
			}
			if (!scene.IsValid())
			{
				return;
			}

		}

		public static void Update()
		{
			if (NeedRebuild)
			{
				NeedRebuild = false;
				try
				{
					RebuildTable();
					RebuildPlayers();
				}
				catch (Exception e)
				{
					
				}
				SceneReady = true;
			}
			if (!Session.Active)
			{
				return;
			}
			DeferredUiTick();
			CustomizeTick();
			float now = Time.realtimeSinceStartup;
			if (now - _lastPing > 2f)
			{
				_lastPing = now;
				_pingStamp = DateTime.UtcNow.Ticks;
				byte[] m = MsgBuild.Ping(_pingStamp);
				if (Session.IsHost)
				{
					Session.Broadcast(m);
				}
				else
				{
					Session.SendToHost(m);
				}
			}

			UpdatePlayers();
			UpdateObjects();
			UpdateFlow();

			if (Session.IsHost && now - _lastTimerSend > 1f)
			{
				_lastTimerSend = now;
				Session.Broadcast(BuildTimerSync());
			}

			if (Session.IsHost && now - _lastCustStateSend > 1f)
			{
				_lastCustStateSend = now;
				SendCustState();
			}

			PendingStoryDeliveryTick();

			PruneToasts();
		}
	}
}
