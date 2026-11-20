using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KioskMP
{

	[HarmonyPatch]
	internal static class UiControllerPatches
	{
		private static readonly Dictionary<MethodBase, UiCommand> _map = new Dictionary<MethodBase, UiCommand>();

		private static IEnumerable<MethodBase> TargetMethods()
		{
			_map.Clear();
			Type t = typeof(UIController);
			Add(t, "StartTheGame", UiCommand.StartStory);
			Add(t, "StartEndlessMode", UiCommand.StartEndless);
			Add(t, "StartRelaxMode", UiCommand.StartRelax);
			Add(t, "ContinueGame", UiCommand.Continue);
			Add(t, "BackToMainMenu", UiCommand.BackToMenu);
			Add(t, "RestartEndlessMode", UiCommand.RestartEndless);
			Add(t, "YouDiedRestart", UiCommand.YouDiedRestart);
			Add(t, "EndCredit", UiCommand.EndCredit);
			Add(t, "NextDayScreen", UiCommand.NextDay);
			Add(t, "CallOverEndlessMode", UiCommand.CallOverEndless);
			return new List<MethodBase>(_map.Keys);
		}

		private static void Add(Type t, string name, UiCommand cmd)
		{
			MethodBase m = AccessTools.Method(t, name);
			if (m == null)
			{
				
				return;
			}
			_map[m] = cmd;
		}

		private static bool CmdOf(MethodBase m, out UiCommand cmd)
		{
			return _map.TryGetValue(m, out cmd);
		}

		private static bool Prefix(MethodBase __originalMethod, ref UiAction __state)
		{
			UiCommand cmd;
			if (!CmdOf(__originalMethod, out cmd))
			{
				__state = UiAction.Run;
				return true;
			}
			__state = Game.UiGate(cmd);
			return __state != UiAction.Skip;
		}

		private static void Postfix(MethodBase __originalMethod, UiAction __state)
		{
			UiCommand cmd;
			if (!CmdOf(__originalMethod, out cmd))
			{
				return;
			}
			if (__state == UiAction.Run || __state == UiAction.RunBroadcast)
			{
				Game.NoteUiState(cmd);
			}
			if (__state != UiAction.RunBroadcast)
			{
				return;
			}
			Session.Broadcast(MsgBuild.UiCmd(cmd));
		}
	}

	[HarmonyPatch(typeof(PlayerController), "SetHeight")]
	internal static class PlayerHeightPatch
	{
		private static readonly AccessTools.FieldRef<PlayerController, float> _viewOffset =
			AccessTools.FieldRefAccess<PlayerController, float>("playerViewYOffset");

		private static void Postfix()
		{
			PlayerController pc = PlayerController.instance;
			if (pc == null)
			{
				return;
			}
			CharacterController cc = pc.GetComponent<CharacterController>();
			if (cc != null)
			{
				cc.height = cc.height >= 1.5f ? Game.AvatarHeight : Game.AvatarHeight * 0.5f;
				cc.center = cc.height >= 1.5f ? Vector3.zero : new Vector3(0f, (Game.AvatarHeight * 0.5f - Game.AvatarHeight) * 0.5f, 0f);
			}
			if (_viewOffset != null && cc != null)
			{
				_viewOffset(pc) = cc.height >= 1.5f ? 0.48f : 0f;
			}
		}
	}

	[HarmonyPatch(typeof(PlayerController), "MoveAndRotateTo")]
	internal static class PlayerSpawnPatch
	{
		private static Vector3 _savedPos;
		private static Quaternion _savedRot;
		private static bool _patched;

		private static void Prefix(ref Transform newPos)
		{
			_patched = false;
			if (!Session.Active || Session.SelfId < 0 || Session.SelfId > 3)
			{
				return;
			}
			GameManager gm = GameManager.instance;
			if (gm == null || gm.startingPos == null || newPos != gm.startingPos)
			{
				return;
			}
			Transform basePos = gm.startingPos;
			_savedPos = basePos.position;
			_savedRot = basePos.rotation;
			Vector3 fwd = basePos.forward;
			fwd.y = 0f;
			if (fwd.sqrMagnitude < 1e-6f)
			{
				fwd = Vector3.forward;
			}
			Vector3 left = Vector3.Cross(fwd.normalized, Vector3.up).normalized;
			basePos.position = _savedPos + Game.SpawnOffset(Session.SelfId, left);
			_patched = true;
		}

		private static void Postfix()
		{
			if (!_patched)
			{
				return;
			}
			_patched = false;
			GameManager gm = GameManager.instance;
			if (gm != null && gm.startingPos != null)
			{
				gm.startingPos.position = _savedPos;
				gm.startingPos.rotation = _savedRot;
			}
		}
	}

	[HarmonyPatch(typeof(TimerController), "GameOver")]
	internal static class TimerGameOverPatch
	{
		private static bool Prefix()
		{
			return !(Session.Active && Session.IsClient);
		}
	}

	[HarmonyPatch(typeof(TimerController), "SetCurrentSeconds")]
	internal static class TimerSetSecondsPatch
	{
		private static bool Prefix(float seconds)
		{
			if (!Session.Active || NetCtx.ApplyingRemote || seconds == 0f)
			{
				return true;
			}
			byte[] m = MsgBuild.OrderDone(-1, seconds, false);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
				return true;
			}
			Session.SendToHost(m);
			return false;
		}
	}

	[HarmonyPatch(typeof(WhiteBoard), "SetOrderDone")]
	internal static class BoardSetOrderDonePatch
	{
		private static bool Prefix(int orderID)
		{
			if (!Session.Active || NetCtx.ApplyingRemote)
			{
				return true;
			}
			byte[] m = MsgBuild.OrderDone(orderID, 0f, true);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
				return true;
			}
			Session.SendToHost(m);
			return false;
		}
	}

	[HarmonyPatch(typeof(WhiteBoard), "UpdateWhiteBoard")]
	internal static class BoardUpdatePatch
	{
		private static void Postfix(List<Customer.OrdedItemNew> newOrder, bool refresh)
		{
			Game.NotifyVanillaBoard(newOrder);
		}
	}

	[HarmonyPatch(typeof(WhiteBoard), "ClearOldOrder")]
	internal static class BoardClearPatch
	{
		private static void Postfix()
		{
			Game.NotifyVanillaBoardClear();
		}
	}

	[HarmonyPatch(typeof(CustomersController), "SpawnCustomer")]
	internal static class SpawnCustomerPatch
	{
		private static bool Prefix()
		{
			return !Session.Active || Session.IsHost;
		}

		private static void Postfix()
		{
			Game.NotifyCustomerSpawned();
		}
	}

	[HarmonyPatch(typeof(PlayerController), "EquipGrabObj")]
	internal static class EquipGrabPatch
	{
		private static void Postfix(GrabObject tempGrabObj)
		{
			PlayerController pc = PlayerController.instance;
			if (pc != null && tempGrabObj != null && pc.currentGrabObj == tempGrabObj)
			{
				Game.NotifyGrabbed(tempGrabObj);
			}
		}
	}

	[HarmonyPatch(typeof(GrabObject), "Break")]
	internal static class GrabBreakPatch
	{
		private static void Postfix(GrabObject __instance)
		{
			Game.NotifyDestroyed(__instance);
		}
	}

	[HarmonyPatch(typeof(GrabObject), "AddAddition")]
	internal static class GrabAddAdditionPatch
	{
		private static void Postfix(GrabObject __instance, int additionID)
		{
			Game.NotifyAddition(__instance, additionID);
		}
	}

	internal static class TriggerGate
	{
		public static bool Allow(TriggerObject inst, GrabObject g)
		{
			if (!Session.Active)
			{
				return true;
			}
			if (inst is Customer)
			{

				return Session.IsHost;
			}
			if (g == null)
			{
				return true;
			}
			NetObj n = Game.Lookup(g);
			if (n == null)
			{
				return true;
			}
			return !Game.IsHeldByRemote(n);
		}
	}

	[HarmonyPatch(typeof(TriggerObject), "OnTriggerStay")]
	internal static class TriggerStayPatch
	{
		private static bool Prefix(TriggerObject __instance, Collider other)
		{
			GrabObject g = other == null ? null : other.GetComponent<GrabObject>();
			bool allow = TriggerGate.Allow(__instance, g);
			if (allow)
			{
				Game.NoteCustomerLocalHit(__instance, g, 0);
			}
			else
			{
				Game.RelayCustomerTrigger(__instance, g, 0);
			}
			return allow;
		}
	}

	[HarmonyPatch(typeof(TriggerObject), "OnTriggerEnter")]
	internal static class TriggerEnterPatch
	{
		private static bool Prefix(TriggerObject __instance, Collider other)
		{
			GrabObject g = other == null ? null : other.GetComponent<GrabObject>();
			bool allow = TriggerGate.Allow(__instance, g);
			if (allow)
			{
				Game.NoteCustomerLocalHit(__instance, g, 0);
			}
			else
			{
				Game.RelayCustomerTrigger(__instance, g, 0);
			}
			return allow;
		}
	}

	[HarmonyPatch(typeof(TriggerObject), "OnTriggerExit")]
	internal static class TriggerExitPatch
	{
		private static bool Prefix(TriggerObject __instance, Collider other)
		{
			GrabObject g = other == null ? null : other.GetComponent<GrabObject>();
			bool allow = TriggerGate.Allow(__instance, g);
			if (allow)
			{
				Game.NoteCustomerLocalHit(__instance, g, 1);
			}
			else
			{
				Game.RelayCustomerTrigger(__instance, g, 1);
			}
			return allow;
		}
	}

	[HarmonyPatch(typeof(TriggerObject), "OnCollisionEnter")]
	internal static class TriggerCollisionPatch
	{
		private static bool Prefix(TriggerObject __instance, Collision other)
		{
			GrabObject g = other == null || other.collider == null
				? null
				: other.collider.GetComponent<GrabObject>();
			bool allow = TriggerGate.Allow(__instance, g);
			if (allow)
			{
				Game.NoteCustomerLocalHit(__instance, g, 0);
			}
			else
			{
				Game.RelayCustomerTrigger(__instance, g, 0);
			}
			return allow;
		}
	}

	[HarmonyPatch(typeof(DialogController), "StartDialog")]
	internal static class DialogStartPatch
	{
		private static bool Prefix(DialogController __instance, ref bool __state)
		{
			__state = false;
			if (NetCtx.ApplyingDialog)
			{

				return true;
			}

			if (NetCtx.FromRemoteInteract && !NetCtx.CustomerDelivery && !NetCtx.RemoteInteractPhone)
			{
				Game.DialogStartBlockedFrame = Time.frameCount;
				return false;
			}

			if (Game.RemoteDialogRunning() && !NetCtx.RemoteInteractPhone)
			{
				Game.DialogStartBlockedFrame = Time.frameCount;
				return false;
			}

			if (NetCtx.FromRemoteInteract)
			{
				Game.QuietRemoteDialog(__instance);
			}
			else
			{
				Game.EnsureLoudDialog();
			}
			__state = true;
			return true;
		}

		private static void Postfix(bool __state, int index, bool onlyLastText, bool setSkipFirstTimeTrue)
		{
			if (__state)
			{
				Game.SendDialogStart(index, onlyLastText, setSkipFirstTimeTrue);
			}
		}
	}

	[HarmonyPatch(typeof(PlayerController), "FocusViewOn")]
	internal static class FocusViewPatch
	{
		private static bool Prefix()
		{
			if (NetCtx.FromRemoteInteract)
			{
				return false;
			}
			return Game.DialogStartBlockedFrame != Time.frameCount;
		}
	}

	[HarmonyPatch(typeof(AudioController), "SpawnDialogLetterTick")]
	internal static class DialogLetterTickPatch
	{
		private static bool Prefix()
		{

			return !Game.QuietDialogActive;
		}
	}

	[HarmonyPatch(typeof(DialogController), "StopDialog")]
	internal static class DialogStopPatch
	{

		private static int _depth;

		private static bool Prefix(DialogController __instance, ref bool __state)
		{
			__state = false;
			if (!Session.Active || NetCtx.ApplyingDialog)
			{
				return true;
			}
			if (!Game.DialogActiveOf(__instance))
			{
				return true;
			}
			if (Game.DialogOwner >= 0 && Game.DialogOwner != Session.SelfId)
			{
				return false;
			}
			bool outer = _depth == 0;
			_depth++;
			__state = true;
			if (outer)
			{
				Game.NotifyDialogAdvance();
			}
			return true;
		}

		private static void Postfix(ref bool __state)
		{
			if (__state && _depth > 0)
			{
				_depth--;
			}
		}
	}

	[HarmonyPatch(typeof(DialogController), "Update")]
	internal static class DialogUpdatePatch
	{
		private static bool Prefix(DialogController __instance)
		{
			if (!Session.Active)
			{
				return true;
			}

			if (Game.DialogActiveOf(__instance) && Game.DialogOwner >= 0 && Game.DialogOwner != Session.SelfId)
			{
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(Customer), "DialogEventDone")]
	internal static class CustDialogDonePatch
	{
		private static bool Prefix(bool lastText)
		{

			if (Session.Active && Session.IsClient && lastText && Game.DialogOwner != Session.SelfId)
			{
				return false;
			}
			return true;
		}

		private static void Postfix(Customer __instance)
		{
			if (Session.Active && Session.IsHost)
			{
				Game.SendCustState();
				Game.PendingStoryDeliveryTick();
			}
			if (Session.Active && __instance != null)
			{
				try
				{
					JumpScareObject[] scares = __instance.GetComponentsInChildren<JumpScareObject>(true);
					for (int i = 0; i < scares.Length; i++)
					{
						if (scares[i] != null && scares[i].enabled)
						{
							ReactionNet.NotifyArmed(scares[i]);
						}
					}
				}
				catch (Exception e)
				{
					
				}
			}
		}
	}

	[HarmonyPatch(typeof(Customer), "GoAway")]
	internal static class CustGoAwayPatch
	{
		private static bool Prefix(bool nextCustomer)
		{
			if (!Session.Active)
			{
				return true;
			}
			if (Session.IsHost && !NetCtx.ApplyingGoAway)
			{
				Session.Broadcast(MsgBuild.CustGoAway(nextCustomer));
				return true;
			}
			if (Session.IsClient && !NetCtx.ApplyingGoAway)
			{
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(GrabObject), "Cut")]
	internal static class GrabCutPatch
	{
		private static void Prefix(GrabObject __instance)
		{
			Game.SendCut(__instance);
		}
	}

	[HarmonyPatch(typeof(PlayerController), "Update")]
	internal static class PlayerUpdatePatch
	{
		private static bool Prefix()
		{
			return !MenuUI.IsOpen && !ChatOverlay.IsOpen;
		}
	}

	internal static class InteractGate
	{
		public static bool Excluded(Interactable it, InteractMethod m)
		{
			if (it == null)
			{
				return true;
			}
			if (it is SauceBottle || it is PancakeMix || it is Fries)
			{
				return true;
			}
			if (it is GrabObject)
			{

				return m != InteractMethod.StartUse && m != InteractMethod.StopUse;
			}
			if (it is Customer || it is PackageBox)
			{

				return true;
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(Interactable), "Press")]
	internal static class InteractPressPatch
	{
		private static void Postfix(Interactable __instance)
		{
			InteractRelay.Relay(__instance, InteractMethod.Press, false, 0f);
		}
	}

	[HarmonyPatch(typeof(Interactable), "Use")]
	internal static class InteractUsePatch
	{
		private static void Postfix(Interactable __instance)
		{
			InteractRelay.Relay(__instance, InteractMethod.Use, false, 0f);
		}
	}

	[HarmonyPatch(typeof(Interactable), "StartUse")]
	internal static class InteractStartUsePatch
	{
		private static void Postfix(Interactable __instance)
		{
			InteractRelay.Relay(__instance, InteractMethod.StartUse, false, 0f);
		}
	}

	[HarmonyPatch(typeof(Interactable), "StopUse")]
	internal static class InteractStopUsePatch
	{
		private static void Postfix(Interactable __instance)
		{
			InteractRelay.Relay(__instance, InteractMethod.StopUse, false, 0f);
		}
	}

	[HarmonyPatch(typeof(Interactable), "UseSecond")]
	internal static class InteractUseSecondPatch
	{
		private static void Postfix(Interactable __instance, bool b)
		{
			InteractRelay.Relay(__instance, InteractMethod.UseSecond, b, 0f);
		}
	}

	internal static class InteractRelay
	{
		public static void Relay(Interactable it, InteractMethod m, bool argB, float argF)
		{
			if (!Session.Active || NetCtx.FromRemoteInteract || NetCtx.ApplyingRemote)
			{
				return;
			}
			if (InteractGate.Excluded(it, m))
			{
				return;
			}
			int id = Game.StaticIdOf(it);
			if (id < 0)
			{
				return;
			}
			Game.SendInteract(id, m, argB, argF);
		}
	}

	[HarmonyPatch(typeof(SauceBottle), "UseVirtual")]
	internal static class SauceUsePatch
	{
		private static void Prefix(out int __state)
		{
			__state = MarkerController.instance != null ? MarkerController.instance.transform.childCount : -1;
		}

		private static void Postfix(SauceBottle __instance, int __state)
		{
			if (__state < 0 || MarkerController.instance == null)
			{
				return;
			}
			Game.NotifyDecal(__instance, MarkerController.instance.transform, __state);
		}
	}

	[HarmonyPatch(typeof(PancakeMix), "UseVirtual")]
	internal static class MixUsePatch
	{
		private static void Prefix(out int __state)
		{
			__state = GameManager.instance != null ? GameManager.instance.transform.childCount : -1;
		}

		private static void Postfix(PancakeMix __instance, int __state)
		{
			if (__state < 0 || GameManager.instance == null)
			{
				return;
			}
			Game.NotifyDecal(__instance, GameManager.instance.transform, __state);
		}
	}

	[HarmonyPatch(typeof(Marker), "UseSecondVirtual")]
	internal static class MarkerColorPatch
	{
		private static void Postfix(Marker __instance)
		{
			Game.NotifyMarkerColor(__instance);
		}
	}

	[HarmonyPatch(typeof(Bun), "AddKetchupValue")]
	internal static class BunKetchupPatch
	{
		private static void Postfix(Bun __instance)
		{
			Game.NotifySauce(__instance);
		}
	}

	[HarmonyPatch(typeof(Bun), "AddMustardValue")]
	internal static class BunMustardPatch
	{
		private static void Postfix(Bun __instance)
		{
			Game.NotifySauce(__instance);
		}
	}

	[HarmonyPatch(typeof(Grill), "OnTriggerOrCollision")]
	internal static class GrillCookPatch
	{
		private static bool Prefix(GrabObject tempGrabObj)
		{
			if (!Session.Active || tempGrabObj == null)
			{
				return true;
			}
			NetObj n = Game.Lookup(tempGrabObj);
			if (n == null)
			{
				return true;
			}
			return Game.LocalAuthority(n);
		}
	}

	[HarmonyPatch(typeof(FryerBasket), "OnTriggerOrCollision")]
	internal static class FryerInsertPatch
	{
		private static bool Prefix(GrabObject tempGrabObj)
		{
			if (!Session.Active || tempGrabObj == null)
			{
				return true;
			}
			int id = tempGrabObj.grabObjectID;
			if (id != 29 && id != 30 && id != 31)
			{
				return true;
			}
			NetObj n = Game.Lookup(tempGrabObj);
			if (n == null)
			{
				return true;
			}
			return Game.LocalAuthority(n);
		}
	}

	[HarmonyPatch(typeof(Fries), "StartUseVirtual")]
	internal static class FriesScoopPatch
	{
		private static void Prefix(Fries __instance, out bool __state)
		{
			__state = Game.FriesHasSomething(__instance);
		}

		private static void Postfix(Fries __instance, bool __state)
		{
			if (__state || !Game.FriesHasSomething(__instance))
			{
				return;
			}
			Game.NotifyFriesScoop(__instance, __instance.grabObjectID, __instance.interactableName);
		}
	}

	[HarmonyPatch(typeof(FryerBasket), "PlayBasketAnimNormal")]
	internal static class FryerStartPatch
	{
		private static void Prefix(FryerBasket __instance, int grabID)
		{
			Game.NotifyFryerStart(__instance, grabID);
		}
	}

	[HarmonyPatch(typeof(FryerBasket), "EmptyFryerBasket")]
	internal static class FryerEmptyPatch
	{
		private static void Postfix(FryerBasket __instance)
		{
			Game.NotifyFryerEmpty(__instance);
		}
	}
}
