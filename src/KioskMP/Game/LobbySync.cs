using System.Collections.Generic;
using UnityEngine;

namespace KioskMP
{

	public static partial class Game
	{

		public static readonly HashSet<int> LobbyReadyIds = new HashSet<int>();

		private static float _allReadySince;

		public static bool IsPeerReady(int peerId)
		{
			return LobbyReadyIds.Contains(peerId);
		}

		public static bool IsSelfReady()
		{
			return Session.Active && LobbyReadyIds.Contains(Session.SelfId);
		}

		public static void ResetLobbyReady()
		{
			LobbyReadyIds.Clear();
			_allReadySince = 0f;
		}

		public static void HandleLobbyReady(int fromPeerId, PacketReader r)
		{
			if (!Session.IsHost)
			{
				return;
			}
			bool ready = r.Bool();
			if (ready)
			{
				LobbyReadyIds.Add(fromPeerId);
			}
			else
			{
				LobbyReadyIds.Remove(fromPeerId);
			}
			
			AddToast(PeerName(fromPeerId) + (ready ? " is ready" : " cancelled ready"));
			BroadcastLobbySync();
		}

		public static void HandleLobbySync(PacketReader r)
		{
			int n = r.I32();
			LobbyReadyIds.Clear();
			for (int i = 0; i < n; i++)
			{
				LobbyReadyIds.Add(r.I32());
			}
		}

		public static void BroadcastLobbySync()
		{
			if (!Session.Active || !Session.IsHost)
			{
				return;
			}
			List<int> ids = new List<int>(LobbyReadyIds);
			Session.Broadcast(MsgBuild.LobbySync(ids));
		}

		public static void ToggleSelfReady()
		{
			if (!Session.Active)
			{
				return;
			}
			if (Session.IsHost)
			{
				if (LobbyReadyIds.Contains(Session.SelfId))
				{
					LobbyReadyIds.Remove(Session.SelfId);
				}
				else
				{
					LobbyReadyIds.Add(Session.SelfId);
				}
				BroadcastLobbySync();
			}
			else
			{
				bool want = !LobbyReadyIds.Contains(Session.SelfId);
				Session.SendToHost(MsgBuild.LobbyReady(want));
			}
		}

		public static bool AllPlayersReady()
		{
			if (!Session.Active || !Session.IsHost)
			{
				return false;
			}
			if (Session.Peers.Count == 0)
			{
				return false;
			}
			foreach (KeyValuePair<int, PeerInfo> kv in Session.Peers)
			{
				if (!LobbyReadyIds.Contains(kv.Key))
				{
					return false;
				}
			}
			return true;
		}

		public static void StartMpGame(UiCommand cmd)
		{
			if (!Session.Active)
			{
				return;
			}
			
			
			if (Session.IsHost)
			{
				_deferredUiBroadcast = true;
				bool applied = ApplyUiCmd(cmd);
				if (applied && IsStartCmd(cmd) && !GameRunning)
				{
					applied = false;
					
				}
				if (applied)
				{
					_deferredUiBroadcast = false;
					Session.Broadcast(MsgBuild.UiCmd(cmd));
					
				}
				else if (_deferredUi < 0)
				{
					_deferredUiBroadcast = false;
					
				}
				else
				{
					
				}
			}
			else
			{
				Session.SendToHost(MsgBuild.UiCmd(cmd));
			}
			ResetLobbyReady();
		}

		public static void LobbyStartTick(UiCommand cmd)
		{
			if (!Session.Active || !Session.IsHost || GameRunning)
			{
				_allReadySince = 0f;
				return;
			}
			if (!AllPlayersReady())
			{
				_allReadySince = 0f;
				return;
			}
			if (_allReadySince <= 0f)
			{
				_allReadySince = Time.realtimeSinceStartup;
				return;
			}
			if (Time.realtimeSinceStartup - _allReadySince > 0.8f)
			{
				_allReadySince = 0f;
				StartMpGame(cmd);
			}
		}
	}
}
