using System;
using System.Collections.Generic;
using UnityEngine;

namespace KioskMP
{
	public static partial class Game
	{
		private static readonly Dictionary<int, int[]> _chars = new Dictionary<int, int[]>();

		public static int[] CharOf(int peerId)
		{
			int[] v;
			return _chars.TryGetValue(peerId, out v) ? v : null;
		}

		public static int CharIdOf(int peerId)
		{
			int[] v = CharOf(peerId);
			return v != null ? ClampChar(v[0]) : Mathf.Abs(peerId) % 4;
		}

		public static int VariantOf(int peerId)
		{
			int[] v = CharOf(peerId);
			return v != null && v[1] == 1 ? 1 : 0;
		}

		private static int ClampChar(int c)
		{
			if (c < 0 || c > 3)
			{
				c = Mathf.Abs(c) % 4;
			}
			return c;
		}

		public static void SetChar(int peerId, int charId, int variant)
		{
			_chars[peerId] = new int[] { ClampChar(charId), variant == 1 ? 1 : 0 };
		}

		public static void EnsureSelfChar()
		{
			if (!Session.Active || Session.SelfId < 0)
			{
				return;
			}
			int ch = ClampChar(CanvasMenu.SelectedCharacter);
			int va = CanvasMenu.SelectedSkin == 1 ? 1 : 0;
			int[] cur;
			if (_chars.TryGetValue(Session.SelfId, out cur) && cur[0] == ch && cur[1] == va)
			{
				return;
			}
			_chars[Session.SelfId] = new int[] { ch, va };
		}

		public static void SendMyCustomize()
		{
			if (!Session.Active || Session.SelfId < 0)
			{
				return;
			}
			int ch = CanvasMenu.SelectedCharacter;
			int va = CanvasMenu.SelectedSkin;
			SetChar(Session.SelfId, ch, va);
			byte[] m = MsgBuild.Customize(Session.SelfId, ch, va);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
			
		}

		public static void HandleCustomize(int fromPeerId, PacketReader r)
		{
			int peer = r.I32();
			int ch = r.I32();
			int va = r.I32();
			if (peer < 0)
			{
				peer = fromPeerId;
			}
			if (Session.IsHost && peer >= 0)
			{
				Session.Broadcast(MsgBuild.Customize(peer, ch, va), fromPeerId);
			}
			SetChar(peer, ch, va);
			RebuildAvatar(peer);
			
			
		}

		public static void HandleDance(int fromPeerId, PacketReader r)
		{
			int peer = r.I32();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.Dance(peer), fromPeerId);
			}
			ToggleDance(peer);
		}

		public static void SendMyDance()
		{
			if (!Session.Active || Session.SelfId < 0)
			{
				return;
			}
			byte[] m = MsgBuild.Dance(Session.SelfId);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void WriteCharsState(PacketWriter w)
		{
			w.I32(_chars.Count);
			foreach (KeyValuePair<int, int[]> kv in _chars)
			{
				w.I32(kv.Key);
				w.I32(ClampChar(kv.Value[0]));
				w.I32(kv.Value[1] == 1 ? 1 : 0);
			}
		}

		public static void ReadCharsState(PacketReader r)
		{
			_chars.Clear();
			int n = r.I32();
			if (n < 0 || n > 64)
			{
				n = 0;
			}
			for (int i = 0; i < n; i++)
			{
				int peer = r.I32();
				int ch = r.I32();
				int va = r.I32();
				SetChar(peer, ch, va);
			}
		}

		public static void ResetCustomize()
		{
			_chars.Clear();
		}

		public static void CustomizeTick()
		{
			if (!Session.Active || !SceneReady || Session.SelfId < 0)
			{
				return;
			}
			int ch = ClampChar(CanvasMenu.SelectedCharacter);
			int va = CanvasMenu.SelectedSkin == 1 ? 1 : 0;
			int[] cur;
			if (_chars.TryGetValue(Session.SelfId, out cur) && cur[0] == ch && cur[1] == va)
			{
				return;
			}
			SendMyCustomize();
		}
	}
}
