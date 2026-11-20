using System;
using System.Collections.Generic;
using UnityEngine;

namespace KioskMP
{

	public static class MenuUI
	{
		public static bool IsOpen;

		private static bool _stylesReady;
		private static GUIStyle _box, _label, _btn, _head;

		public static void Toggle()
		{

			CanvasMenu.Toggle();
		}

		public static void Tick()
		{

			try
			{
				string n;
				while ((n = SteamNet.DequeueNotice()) != null)
				{
					Game.AddToast(n);
				}
			}
			catch (Exception e) {  }
			if (IsOpen)
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
			try { CanvasMenu.Tick(); }
			catch (Exception e) {  }
			try { ChatOverlay.Tick(); }
			catch (Exception e) {  }
		}

		private static void Styles()
		{
			if (_stylesReady)
			{
				return;
			}
			_stylesReady = true;
			_box = new GUIStyle(GUI.skin.box);
			_label = new GUIStyle(GUI.skin.label)
			{
				richText = true,
				wordWrap = true,
				fontSize = 13
			};
			_btn = new GUIStyle(GUI.skin.button)
			{
				fontSize = 13
			};
			_head = new GUIStyle(GUI.skin.label)
			{
				fontSize = 16,
				fontStyle = FontStyle.Bold
			};
		}

		public static void Draw()
		{
			Styles();
			DrawInvitePopup();
		}

		private static void DrawInvitePopup()
		{
			if (SteamNet.PendingInvites.Count == 0)
			{
				return;
			}
			float w = Mathf.Min(380f, Screen.width - 20f);
			float h = 34f + SteamNet.PendingInvites.Count * 36f + 34f;
			GUILayout.BeginArea(new Rect(Screen.width - w - 10f, 10f, w, h), _box);
			GUILayout.Label("Steam invite" + (SteamNet.PendingInvites.Count > 1 ? "s" : ""), _head);
			bool doAccept = false;
			bool doDecline = false;
			ulong actId = 0;
			string actWho = "";
			for (int i = 0; i < SteamNet.PendingInvites.Count; i++)
			{
				KeyValuePair<ulong, string> inv = SteamNet.PendingInvites[i];
				GUILayout.BeginHorizontal();
				string who = string.IsNullOrEmpty(inv.Value) ? ("player " + inv.Key) : inv.Value;
				GUILayout.Label(who, _label);
				if (GUILayout.Button("Accept", _btn, GUILayout.Width(70f)))
				{
					doAccept = true;
					actId = inv.Key;
					actWho = who;
				}
				GUI.enabled = true;
				if (GUILayout.Button("X", _btn, GUILayout.Width(30f)))
				{
					doDecline = true;
					actId = inv.Key;
				}
				GUILayout.EndHorizontal();
				if (doAccept || doDecline)
				{
					break;
				}
			}
			if (doAccept)
			{
				if (Session.Active)
				{
					Session.Disconnect();
				}
				SteamNet.AcceptInvite(actId);
				Game.AddToast("Joining " + actWho + " ...");
			}
			else if (doDecline)
			{
				SteamNet.DeclineInvite(actId);
			}
			if (Session.Active)
			{
				GUILayout.Label("<i>accepting will leave the current session</i>", _label);
			}
			GUILayout.EndArea();
		}
	}
}
