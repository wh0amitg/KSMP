using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KioskMP
{
	public static class ReactionNet
	{
		public static bool ApplyingRemote;

		private const float Window = 2.5f;
		private static readonly Dictionary<string, float> _lastLocal = new Dictionary<string, float>();
		private static readonly Dictionary<string, float> _lastApply = new Dictionary<string, float>();

		private static readonly AccessTools.FieldRef<JumpScareObject, bool> _eventInvokedRef =
			AccessTools.FieldRefAccess<JumpScareObject, bool>("eventInvoked");

		private static readonly HashSet<int> _armedNotified = new HashSet<int>();

		public static void Reset()
		{
			_lastLocal.Clear();
			_lastApply.Clear();
			_armedNotified.Clear();
			ApplyingRemote = false;
		}

		public static bool Gate(string reactionId)
		{
			float now = Time.realtimeSinceStartup;
			if (ApplyingRemote)
			{
				_lastApply[reactionId] = now;
				if (Session.IsHost && Session.Active)
				{
					Session.Broadcast(MsgBuild.ReactionStr(0, reactionId));
				}
				return true;
			}
			if (!Session.Active)
			{
				return true;
			}
			_lastLocal[reactionId] = now;
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.ReactionStr(0, reactionId));
			}
			return true;
		}

		public static void SendToAll(byte kind, string id)
		{
			if (!Session.Active)
			{
				return;
			}
			byte[] m = MsgBuild.ReactionStr(kind, id);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleReaction(int fromPeerId, PacketReader r)
		{
			byte kind = r.U8();
			string id = r.Str();
			switch (kind)
			{
				case 0:
					ApplyId(id);
					break;
				case 1:
					if (Session.IsHost)
					{
						Session.Broadcast(MsgBuild.ReactionStr(1, id), fromPeerId);
					}
					ArmScare(id);
					break;
				case 2:
					if (Session.IsHost && !RecentlyApplied(id))
					{
						try
						{
							ReactionController.instance.InvokeReaction(id);
						}
						catch (Exception e)
						{
							
						}
						DeactivateBoxes(id);
					}
					break;
				case 4:
					FireScare(id);
					break;
			}
		}

		private static bool RecentlyApplied(string id)
		{
			float t;
			float now = Time.realtimeSinceStartup;
			if (_lastApply.TryGetValue(id, out t) && now - t < Window)
			{
				return true;
			}
			if (_lastLocal.TryGetValue(id, out t) && now - t < Window)
			{
				return true;
			}
			return false;
		}

		private static void ApplyId(string id)
		{
			if (string.IsNullOrEmpty(id) || ReactionController.instance == null)
			{
				return;
			}
			if (RecentlyApplied(id))
			{
				return;
			}
			ApplyingRemote = true;
			try
			{
				ReactionController.instance.InvokeReaction(id);
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				ApplyingRemote = false;
			}
			DeactivateBoxes(id);
		}

		private static void DeactivateBoxes(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return;
			}
			TriggerBox[] boxes = UnityEngine.Object.FindObjectsOfType<TriggerBox>();
			for (int i = 0; i < boxes.Length; i++)
			{
				if (boxes[i] != null && boxes[i].reactionName == id && boxes[i].gameObject.activeSelf)
				{
					boxes[i].gameObject.SetActive(false);
				}
			}
		}

		private static void ArmScare(string id)
		{
			int idx;
			if (!int.TryParse(id, out idx))
			{
				return;
			}
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			if (c == null)
			{
				return;
			}
			JumpScareObject[] all = c.GetComponentsInChildren<JumpScareObject>(true);
			if (idx < 0 || idx >= all.Length || all[idx] == null || all[idx].enabled)
			{
				return;
			}
			all[idx].enabled = true;
			
		}

		private static int ScareIndexOf(Customer c, JumpScareObject js)
		{
			if (c == null || js == null)
			{
				return -1;
			}
			JumpScareObject[] all = c.GetComponentsInChildren<JumpScareObject>(true);
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i] == js)
				{
					return i;
				}
			}
			return -1;
		}

		public static void NotifyArmed(JumpScareObject js)
		{
			if (!Session.Active || js == null)
			{
				return;
			}
			int iid = js.GetInstanceID();
			if (!_armedNotified.Add(iid))
			{
				return;
			}
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			int idx = ScareIndexOf(c, js);
			if (idx < 0)
			{
				return;
			}
			SendToAll(1, idx.ToString());
			
		}

		private static void FireScare(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return;
			}
			JumpScareObject[] all = UnityEngine.Object.FindObjectsOfType<JumpScareObject>();
			for (int i = 0; i < all.Length; i++)
			{
				JumpScareObject js = all[i];
				if (js == null || js.reactionName != id || !js.enabled)
				{
					continue;
				}
				try
				{
					_eventInvokedRef(js) = true;
				}
				catch { }
				js.enabled = false;
				ShadowGuy sg = js as ShadowGuy;
				if (sg != null)
				{
					try { sg.Explode(); } catch { }
				}
			}
		}
	}

	[HarmonyPatch(typeof(ReactionController), "InvokeReaction")]
	internal static class ReactionInvokePatch
	{
		private static bool Prefix(string reactionId)
		{
			return ReactionNet.Gate(reactionId);
		}
	}

	[HarmonyPatch(typeof(JumpScareObject), "Update")]
	internal static class JumpScareUpdatePatch
	{
		private static bool Prefix()
		{
			return !(Session.Active && Session.IsClient);
		}
	}

	[HarmonyPatch(typeof(JumpScareObject), "InvokeReaction")]
	internal static class JumpScareFirePatch
	{
		private static void Postfix(JumpScareObject __instance)
		{
			if (!Session.Active || ReactionNet.ApplyingRemote)
			{
				return;
			}
			if (__instance.enabled)
			{
				return;
			}
			ReactionNet.SendToAll(4, __instance.reactionName);
		}
	}

	[HarmonyPatch(typeof(TriggerBox), "OnTriggerEnter")]
	internal static class TriggerBoxEnterPatch
	{
		private static bool Prefix(TriggerBox __instance, Collider other)
		{
			if (!Session.Active || Session.IsHost)
			{
				return true;
			}
			if (PlayerController.instance == null || other == null
				|| other.gameObject != PlayerController.instance.gameObject)
			{
				return true;
			}
			ReactionNet.SendToAll(2, __instance.reactionName);
			return true;
		}
	}
}
