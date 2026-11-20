using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KioskMP
{
	public static partial class Game
	{
		private const int SauceHasKetchup = 1;
		private const int SauceHasMustard = 2;

		private static readonly FieldInfo _fHotdogMesh = AccessTools.Field(typeof(Hotdog), "hotdogMesh");
		private static readonly FieldInfo _fBurgerMesh = AccessTools.Field(typeof(Burger), "burgerMesh");
		private static readonly FieldInfo _fPancakeMesh = AccessTools.Field(typeof(Pancake), "pancakeMesh");
		private static readonly FieldInfo _fEggMesh = AccessTools.Field(typeof(FriedEgg), "eggMesh");
		private static readonly FieldInfo _fFriesHasSomething = AccessTools.Field(typeof(Fries), "alreadyHasSomething");
		private static readonly MethodInfo _fryerStartMethod = AccessTools.Method(typeof(FryerBasket), "PlayBasketAnimNormal");

		public static void NotifySauce(Bun b)
		{
			if (!Session.Active || b == null || NetCtx.ApplyingRemote || NetCtx.FromRemoteInteract)
			{
				return;
			}
			NetObj n = Lookup(b);
			if (n == null)
			{
				return;
			}
			if (LocalAuthority(n))
			{
				n.dirty = true;
				return;
			}
			int flags = (b.hasKetchup ? SauceHasKetchup : 0) | (b.hasMustard ? SauceHasMustard : 0);
			float now = Time.realtimeSinceStartup;
			if (flags == n.lastSauceFlags && now - n.lastSauceSend < 0.05f)
			{
				return;
			}
			n.lastSauceSend = now;
			n.lastSauceFlags = flags;
			byte[] m = MsgBuild.SauceSync(n.id, b.ketchupValue, b.mustardValue, flags);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleSauceSync(int fromPeerId, PacketReader r)
		{
			int objId = r.I32();
			float ketchup = r.F32();
			float mustard = r.F32();
			int flags = r.I32();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.SauceSync(objId, ketchup, mustard, flags), fromPeerId);
			}
			NetObj n;
			if (!ById.TryGetValue(objId, out n) || n.grab == null)
			{
				return;
			}
			Bun b = n.grab as Bun;
			if (b == null)
			{
				return;
			}
			ApplySauce(b, ketchup, mustard, flags);
			n.dirty = true;
		}

		private static void ApplySauce(Bun b, float ketchup, float mustard, int flags)
		{
			if (ketchup > b.ketchupValue)
			{
				b.ketchupValue = ketchup;
			}
			if (mustard > b.mustardValue)
			{
				b.mustardValue = mustard;
			}
			bool hasKetchup = (flags & SauceHasKetchup) != 0 || b.hasKetchup;
			bool hasMustard = (flags & SauceHasMustard) != 0 || b.hasMustard;
			if (hasKetchup && !b.hasKetchup)
			{
				b.hasKetchup = true;
				if (b.ketchupValue < 8f)
				{
					b.ketchupValue = 8f;
				}
				if (b.ketchupObj != null)
				{
					b.ketchupObj.SetActive(true);
				}
			}
			if (hasMustard && !b.hasMustard)
			{
				b.hasMustard = true;
				if (b.mustardValue < 8f)
				{
					b.mustardValue = 8f;
				}
				if (b.mustardObj != null)
				{
					b.mustardObj.SetActive(true);
				}
			}
		}

		public static void NotifyFryerStart(FryerBasket fb, int grabId)
		{
			if (!Session.Active || fb == null || NetCtx.ApplyingRemote || NetCtx.FromRemoteInteract)
			{
				return;
			}
			int id = StaticIdOf(fb);
			if (id < 0)
			{
				return;
			}
			byte[] m = MsgBuild.FryerStart(id, grabId);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleFryerStart(int fromPeerId, PacketReader r)
		{
			int objId = r.I32();
			int grabId = r.I32();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.FryerStart(objId, grabId), fromPeerId);
			}
			FryerBasket fb = InteractableById(objId) as FryerBasket;
			if (fb == null || _fryerStartMethod == null)
			{
				return;
			}
			NetCtx.ApplyingRemote = true;
			try
			{
				if (fb.fryerBasketObjects != null && grabId >= 0 && grabId < fb.fryerBasketObjects.Length)
				{
					GameObject slot = fb.fryerBasketObjects[grabId];
					if (slot != null)
					{
						slot.SetActive(true);
					}
				}
				_fryerStartMethod.Invoke(fb, new object[] { grabId });
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		public static void NotifyFryerEmpty(FryerBasket fb)
		{
			if (!Session.Active || fb == null || NetCtx.ApplyingRemote || NetCtx.FromRemoteInteract)
			{
				return;
			}
			int id = StaticIdOf(fb);
			if (id < 0)
			{
				return;
			}
			byte[] m = MsgBuild.FryerEmpty(id);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleFryerEmpty(int fromPeerId, PacketReader r)
		{
			int objId = r.I32();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.FryerEmpty(objId), fromPeerId);
			}
			FryerBasket fb = InteractableById(objId) as FryerBasket;
			if (fb == null)
			{
				return;
			}
			NetCtx.ApplyingRemote = true;
			try
			{
				fb.EmptyFryerBasket();
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		public static bool FriesHasSomething(Fries fr)
		{
			if (fr == null || _fFriesHasSomething == null)
			{
				return false;
			}
			try
			{
				return _fFriesHasSomething.GetValue(fr) is bool b && b;
			}
			catch
			{
				return false;
			}
		}

		public static void NotifyFriesScoop(Fries fr, int grabId, string name)
		{
			if (!Session.Active || fr == null || NetCtx.ApplyingRemote || NetCtx.FromRemoteInteract)
			{
				return;
			}
			int id = StaticIdOf(fr);
			if (id < 0)
			{
				NetObj n = Lookup(fr);
				if (n == null)
				{
					return;
				}
				id = n.id;
			}
			if (id < 0)
			{
				return;
			}
			byte[] m = MsgBuild.FriesScoop(id, grabId, name);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleFriesScoop(int fromPeerId, PacketReader r)
		{
			int objId = r.I32();
			int grabId = r.I32();
			string name = r.Str();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.FriesScoop(objId, grabId, name), fromPeerId);
			}
			Fries fr = InteractableById(objId) as Fries;
			if (fr == null)
			{
				NetObj n;
				if (ById.TryGetValue(objId, out n))
				{
					fr = n.grab as Fries;
				}
			}
			if (fr == null)
			{
				return;
			}
			ApplyFriesScoop(fr, grabId, name);
		}

		private static void ApplyFriesScoop(Fries fr, int grabId, string name)
		{
			try
			{
				fr.grabObjectID = grabId;
				if (!string.IsNullOrEmpty(name))
				{
					fr.interactableName = name;
				}
				int idx = grabId - 32;
				if (fr.friesObjects != null && idx >= 0 && idx < fr.friesObjects.Length && fr.friesObjects[idx] != null)
				{
					fr.friesObjects[idx].SetActive(true);
				}
				if (_fFriesHasSomething != null)
				{
					_fFriesHasSomething.SetValue(fr, true);
				}
				if (!string.IsNullOrEmpty(name) && name.StartsWith("Burned"))
				{
					fr.SetStinky(false);
				}
			}
			catch (Exception e)
			{
				
			}
		}

		public static float GetCook(GrabObject g)
		{
			Hotdog h = g as Hotdog;
			if (h != null)
			{
				return h.cookValue;
			}
			Burger b = g as Burger;
			if (b != null)
			{
				return b.cookValue;
			}
			Pancake p = g as Pancake;
			if (p != null)
			{
				return p.cookValue;
			}
			FriedEgg e = g as FriedEgg;
			if (e != null)
			{
				return e.cookValue;
			}
			return -1f;
		}

		private static void SetCook(GrabObject g, float v)
		{
			Hotdog h = g as Hotdog;
			if (h != null)
			{
				h.cookValue = v;
				return;
			}
			Burger b = g as Burger;
			if (b != null)
			{
				b.cookValue = v;
				return;
			}
			Pancake p = g as Pancake;
			if (p != null)
			{
				p.cookValue = v;
				return;
			}
			FriedEgg e = g as FriedEgg;
			if (e != null)
			{
				e.cookValue = v;
			}
		}

		private static void FillFoodExtra(GrabObject g, ref ObjState s)
		{
			s.cook = GetCook(g);
			Cup cup = g as Cup;
			if (cup != null && cup.liquidMesh != null)
			{
				Mesh sm = cup.liquidMesh.sharedMesh;
				if (sm != null && sm.blendShapeCount > 0)
				{
					s.coffee = cup.liquidMesh.GetBlendShapeWeight(0);
				}
			}
		}

		private static void ApplyFoodExtra(GrabObject g, ObjState s)
		{
			Meat m = g as Meat;
			if (m != null && s.cook >= 0f)
			{
				float cook = GetCook(g);
				if (s.cook > cook)
				{
					SetCook(g, s.cook);
					cook = s.cook;
				}
				int want = (int)m.meatType;
				if (s.meatType > want)
				{
					want = s.meatType;
				}
				if (cook >= 100f && want < 2)
				{
					want = 2;
				}
				GameManager gm = GameManager.instance;
				if (cook >= m.GetBurnValue() && want == 2 && gm != null && gm.canBurnStuff)
				{
					want = 3;
				}
				if (want > (int)m.meatType)
				{
					m.meatType = (Bun.MeatType)want;
					ApplyMeatVisual(m, want);
				}
			}
			Cup cup = g as Cup;
			if (cup != null && cup.liquidMesh != null)
			{
				Mesh sm = cup.liquidMesh.sharedMesh;
				bool hasShape = sm != null && sm.blendShapeCount > 0;
				if (hasShape && s.coffee > 0f)
				{
					float cur = cup.liquidMesh.GetBlendShapeWeight(0);
					if (s.coffee > cur)
					{
						cup.SetCoffeeValue(s.coffee);
					}
				}
				if (s.grabObjectId == 7 && cup.interactableName != "Coffee")
				{
					cup.CoffeeCompleted();
				}
			}
		}

		private static void ApplyMeatVisual(Meat m, int level)
		{
			GameManager gm = GameManager.instance;
			if (gm == null)
			{
				return;
			}
			m.canInteract = true;
			bool burned = level >= 3;
			Hotdog h = m as Hotdog;
			if (h != null)
			{
				MeshRenderer r = _fHotdogMesh != null ? _fHotdogMesh.GetValue(h) as MeshRenderer : null;
				Material mat = burned ? gm.burnedHotdogMat : gm.cookedHotdogMat;
				if (r != null && mat != null)
				{
					r.material = mat;
				}
				h.interactableName = burned ? "Burned Sausage" : "Cooked Sausage";
			}
			else
			{
				Burger b = m as Burger;
				if (b != null)
				{
					MeshRenderer r2 = _fBurgerMesh != null ? _fBurgerMesh.GetValue(b) as MeshRenderer : null;
					Material mat2 = burned ? gm.burnedBurgerMat : gm.cookedBurgerMat;
					if (r2 != null && mat2 != null)
					{
						r2.material = mat2;
					}
					b.interactableName = burned ? "Burned Burger" : "Cooked Burger";
				}
				else
				{
					Pancake p = m as Pancake;
					if (p != null)
					{
						SkinnedMeshRenderer r3 = _fPancakeMesh != null ? _fPancakeMesh.GetValue(p) as SkinnedMeshRenderer : null;
						Material mat3 = burned ? gm.burnedPancake : gm.cookedPancake;
						if (r3 != null && mat3 != null)
						{
							r3.material = mat3;
						}
						p.interactableName = burned ? "Burned Pancake" : "Pancake";
					}
					else
					{
						FriedEgg e = m as FriedEgg;
						if (e != null)
						{
							MeshRenderer r4 = _fEggMesh != null ? _fEggMesh.GetValue(e) as MeshRenderer : null;
							if (r4 != null)
							{
								if (burned && gm.burnedEggMat != null)
								{
									r4.materials = new Material[] { gm.burnedEggMat, gm.burnedEggMat };
								}
								else if (gm.cookedEggMat != null)
								{
									Material[] mats = r4.materials;
									if (mats.Length < 2)
									{
										mats = new Material[2];
									}
									mats[1] = gm.cookedEggMat;
									r4.materials = mats;
								}
							}
							e.interactableName = burned ? "Burned Egg" : "Fried Egg";
						}
					}
				}
			}
			m.StartStinky();
			PlayerController pc = PlayerController.instance;
			if (pc != null)
			{
				pc.UpdateHoverName();
			}
		}

		private static void ResetCooking()
		{
			ResetDecals();
		}
	}
}
