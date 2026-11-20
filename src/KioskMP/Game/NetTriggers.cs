using System;
using System.Collections.Generic;
using UnityEngine;

namespace KioskMP
{

	public static partial class Game
	{

		private const byte TEvtHit = 0;
		private const byte TEvtExit = 1;

		private static readonly Dictionary<int, float> _custHitSeen = new Dictionary<int, float>();

		private static readonly Dictionary<int, float> _custHitSent = new Dictionary<int, float>();

		public static void RelayCustomerTrigger(TriggerObject inst, GrabObject g, byte kind)
		{
			if (!Session.Active || Session.IsHost || g == null || !(inst is Customer))
			{
				return;
			}
			if (NetCtx.ApplyingRemote || NetCtx.FromRemoteInteract)
			{
				return;
			}
			NetObj n = Lookup(g);
			if (n == null || n.id < 0)
			{
				return;
			}
			if (kind == TEvtHit)
			{
				float t;
				if (_custHitSent.TryGetValue(n.id, out t) && Time.realtimeSinceStartup - t < 0.25f)
				{
					return;
				}
				_custHitSent[n.id] = Time.realtimeSinceStartup;
			}
			Session.SendToHost(MsgBuild.TriggerEvt(n.id, kind));
		}

		public static void NoteCustomerLocalHit(TriggerObject inst, GrabObject g, byte kind)
		{
			if (!Session.Active || !Session.IsHost || g == null || kind == TEvtExit)
			{
				return;
			}
			NetObj n = Lookup(g);
			if (n == null || n.id < 0)
			{
				return;
			}
			_custHitSeen[n.id] = Time.realtimeSinceStartup;
		}

		public static void HandleTriggerEvt(int fromPeerId, PacketReader r)
		{
			if (!Session.IsHost)
			{
				return;
			}
			int objId = r.I32();
			byte kind = r.U8();
			NetObj n;
			if (!ById.TryGetValue(objId, out n) || n.grab == null)
			{
				return;
			}
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			if (c == null || !c.isActiveAndEnabled)
			{
				return;
			}
			if (kind == TEvtHit)
			{

				float t;
				if (_custHitSeen.TryGetValue(objId, out t) && Time.realtimeSinceStartup - t < 0.4f)
				{
					return;
				}
				_custHitSeen[objId] = Time.realtimeSinceStartup;
			}
			try
			{
				NetCtx.FromRemoteInteract = true;

				NetCtx.DialogOwnerOverride = fromPeerId;

				NetCtx.CustomerDelivery = true;
				if (c.canInteract)
				{
					if (kind == TEvtExit)
					{
						c.OnTriggerExitEvent(n.grab);
					}
					else
					{
						c.OnTriggerOrCollision(n.grab);
					}
					NoteBlockedStoryDelivery(c);
				}
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.FromRemoteInteract = false;
				NetCtx.CustomerDelivery = false;
				NetCtx.DialogOwnerOverride = -1;
				if (NetCtx.RemoteQuietFrame == Time.frameCount)
				{
					NetCtx.RemoteQuietFrame = -1;
					ReleaseQuietInput();
				}
			}
		}

		private static void NoteBlockedStoryDelivery(Customer c)
		{
			try
			{
				if (PendingStoryDelivery || GameManager.instance.isEndlessOrRelaxMode())
				{
					return;
				}
				if (DialogStartBlockedFrame != Time.frameCount)
				{
					return;
				}
				for (int i = 0; i < c.endlessOrder.Count; i++)
				{
					if (!c.endlessOrder[i].orderDone)
					{
						return;
					}
				}
				PendingStoryDelivery = true;
				PendingStoryOwner = NetCtx.DialogOwnerOverride;
				
			}
			catch (Exception e)
			{
				
			}
		}

		public static void PendingStoryDeliveryTick()
		{
			if (!Session.Active || !Session.IsHost || !PendingStoryDelivery)
			{
				return;
			}
			DialogController d = DialogController.instance;
			if (d != null && DialogActiveOf(d))
			{
				return;
			}
			PendingStoryDelivery = false;
			int owner = PendingStoryOwner;
			PendingStoryOwner = -1;
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			if (c == null || !c.isActiveAndEnabled)
			{
				return;
			}
			try
			{
				int idx = c.dayTextDialogID[GameManager.instance.currentDay] + 1;
				
				NetCtx.DialogOwnerOverride = owner;
				NetCtx.FromRemoteInteract = true;
				NetCtx.CustomerDelivery = true;
				try
				{
					d.StartDialog(idx, false, false);
				}
				finally
				{
					NetCtx.FromRemoteInteract = false;
					NetCtx.CustomerDelivery = false;
					NetCtx.DialogOwnerOverride = -1;
					NetCtx.RemoteQuietFrame = -1;
					ReleaseQuietInput();
				}
			}
			catch (Exception e)
			{
				
			}
		}

		public static int PendingStoryOwner = -1;

		public static void ResetTriggerRelay()
		{
			_custHitSeen.Clear();
			_custHitSent.Clear();
		}
	}
}
