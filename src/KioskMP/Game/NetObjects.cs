using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace KioskMP
{
	public sealed class NetObj
	{
		public int id = -1;
		public GrabObject grab;
		public GameObject go;
		public int owner = -1;
		public byte mode;
		public byte flags;
		public int prefabIndex = -1;
		public string spawnName = "";
		public int originPeer;
		public int originSeq = -1;

		public bool dirty = true;
		public float lastSent = -999f;
		public Vector3 lastSentPos;
		public Quaternion lastSentRot = Quaternion.identity;

		public float lastCook = -999f;
		public float lastKetchup = -999f;
		public float lastMustard = -999f;
		public float lastCoffee = -999f;
		public int lastBunFlags = -999;
		public int lastMeatType = -999;
		public float lastSauceSend = -999f;
		public int lastSauceFlags = -1;

		public Vector3 targetPos;
		public Quaternion targetRot = Quaternion.identity;
		public Vector3 targetVel;
		public bool hasTarget;

		public readonly HashSet<int> pendingAdds = new HashSet<int>();

		public bool Held
		{
			get { return (flags & ObjState.FlagHeld) != 0; }
		}
	}

	public static partial class Game
	{
		public static readonly Dictionary<int, NetObj> ById = new Dictionary<int, NetObj>();
		public static readonly Dictionary<GrabObject, NetObj> ByRef = new Dictionary<GrabObject, NetObj>();
		public static readonly Dictionary<int, Interactable> StaticById = new Dictionary<int, Interactable>();
		public static readonly Dictionary<Interactable, int> StaticIds = new Dictionary<Interactable, int>();

		private static int _nextDynamic = Proto.DynamicIdBase;
		private static float _lastSweep;
		private static float _lastFullResync;
		private static int _localSeq;
		private static int _resyncCursor;

		private static readonly Dictionary<int, GrabObject> _pendingBySeq = new Dictionary<int, GrabObject>();
		private static readonly Dictionary<GrabObject, int> _pendingByRef = new Dictionary<GrabObject, int>();
		private static readonly Dictionary<int, float> _pendingTime = new Dictionary<int, float>();

		public static NetObj Lookup(GrabObject g)
		{
			NetObj n;
			return g != null && ByRef.TryGetValue(g, out n) ? n : null;
		}

		public static int StaticIdOf(Interactable i)
		{
			int id;
			return i != null && StaticIds.TryGetValue(i, out id) ? id : -1;
		}

		public static Interactable InteractableById(int id)
		{
			Interactable i;
			return StaticById.TryGetValue(id, out i) ? i : null;
		}

		public static bool LocalAuthority(NetObj n)
		{
			if (n == null)
			{
				return false;
			}
			if (n.owner == -1)
			{
				return Session.IsHost;
			}
			return n.owner == Session.SelfId;
		}

		public static bool IsHeldByRemote(NetObj n)
		{
			return n != null && n.owner != -1 && n.owner != Session.SelfId && n.Held;
		}

		private static string ScenePath(Transform t)
		{
			StringBuilder sb = new StringBuilder(64);
			Transform cur = t;
			int guard = 0;
			while (cur != null && guard++ < 64)
			{
				sb.Insert(0, "[" + cur.GetSiblingIndex() + "]" + cur.name + "/");
				cur = cur.parent;
			}
			return sb.ToString();
		}

		private static int CompareInteractables(Interactable a, Interactable b)
		{
			int c = string.CompareOrdinal(ScenePath(a.transform), ScenePath(b.transform));
			if (c != 0)
			{
				return c;
			}
			int ai = Array.IndexOf(a.gameObject.GetComponents<Interactable>(), a);
			int bi = Array.IndexOf(b.gameObject.GetComponents<Interactable>(), b);
			return ai.CompareTo(bi);
		}

		public static void RebuildTable()
		{
			ById.Clear();
			ByRef.Clear();
			StaticById.Clear();
			StaticIds.Clear();
			ClearAllPending();

			Interactable[] found = Resources.FindObjectsOfTypeAll<Interactable>();
			List<Interactable> list = new List<Interactable>(found.Length);
			for (int i = 0; i < found.Length; i++)
			{
				Interactable it = found[i];
				if (it == null || !it.gameObject.scene.IsValid())
				{
					continue;
				}
				list.Add(it);
			}
			list.Sort(CompareInteractables);
			for (int i = 0; i < list.Count; i++)
			{
				Interactable it = list[i];
				StaticById[i] = it;
				StaticIds[it] = i;
				GrabObject g = it as GrabObject;
				if (g != null)
				{
					BindNew(i, g, -1, 0, 0, g.gameObject.name, PrefabIndexOf(g));
				}
			}
			_nextDynamic = Proto.DynamicIdBase;
			
		}

		private static NetObj BindNew(int id, GrabObject g, int owner, byte mode, byte flags, string spawnName, int prefabIndex)
		{
			NetObj n = new NetObj();
			n.id = id;
			n.grab = g;
			n.go = g.gameObject;
			n.owner = owner;
			n.mode = mode;
			n.flags = flags;
			n.spawnName = spawnName ?? "";
			n.prefabIndex = prefabIndex;
			n.targetPos = g.transform.position;
			n.targetRot = g.transform.rotation;
			n.lastSentPos = n.targetPos;
			n.lastSentRot = n.targetRot;
			ById[id] = n;
			ByRef[g] = n;
			ClearPending(g);
			return n;
		}

		private static void ClearPending(GrabObject g)
		{
			if (g == null)
			{
				return;
			}
			int seq;
			if (_pendingByRef.TryGetValue(g, out seq))
			{
				_pendingByRef.Remove(g);
				_pendingBySeq.Remove(seq);
				_pendingTime.Remove(seq);
			}
		}

		private static void ClearAllPending()
		{
			_pendingBySeq.Clear();
			_pendingByRef.Clear();
			_pendingTime.Clear();
		}

		private static void ClaimIfHeld(NetObj n)
		{
			if (n == null || n.grab == null || !Session.Active)
			{
				return;
			}
			PlayerController pc = PlayerController.instance;
			if (pc == null || pc.currentGrabObj != n.grab)
			{
				return;
			}
			if (n.owner == Session.SelfId)
			{
				return;
			}
			if (Session.IsHost)
			{
				GrantTo(n, Session.SelfId);
			}
			else
			{
				Session.SendToHost(MsgBuild.ObjClaim(n.id));
			}
			
		}

		private static string StripClone(string s)
		{
			return s.EndsWith("(Clone)") ? s.Substring(0, s.Length - 7) : s;
		}

		private static int PrefabIndexOf(GrabObject g)
		{
			GameManager gm = GameManager.instance;
			if (gm == null || gm.allObjects == null)
			{
				return -1;
			}
			string n = StripClone(g.gameObject.name);
			for (int i = 0; i < gm.allObjects.Length; i++)
			{
				if (gm.allObjects[i] != null && StripClone(gm.allObjects[i].name) == n)
				{
					return i;
				}
			}
			return -1;
		}

		private static byte ComputeMode(GrabObject g)
		{
			Rigidbody rb = g.getMyRb();
			if (rb == null)
			{
				return 1;
			}
			return rb.isKinematic ? (byte)1 : (byte)0;
		}

		private static byte ComputeFlags(GrabObject g, bool held)
		{
			byte f = 0;
			if (!g.canInteract)
			{
				f |= ObjState.FlagDisabled;
			}
			if (held)
			{
				f |= ObjState.FlagHeld;
			}
			return f;
		}

		private static ObjState BuildState(NetObj n)
		{
			GrabObject g = n.grab;
			ObjState s = new ObjState();
			s.objId = n.id;
			s.owner = n.owner;
			s.parentId = -1;

			bool held = (PlayerController.instance != null && PlayerController.instance.currentGrabObj == g)
				|| (n.flags & ObjState.FlagHeld) != 0;
			s.mode = ComputeMode(g);
			s.flags = ComputeFlags(g, held);
			s.pos = new NetVec3(g.transform.position.x, g.transform.position.y, g.transform.position.z);
			Quaternion q = g.transform.rotation;
			s.rot = new NetQuat(q.x, q.y, q.z, q.w);
			Rigidbody rb = g.getMyRb();
			Vector3 v = rb != null && !held ? rb.velocity : Vector3.zero;
			s.vel = new NetVec3(v.x, v.y, v.z);
			s.additions = new List<int>(g.additions);
			FillFoodState(g, ref s);
			return s;
		}

		private static readonly FieldInfo _fStinky = AccessTools.Field(typeof(GrabObject), "stinky");
		private static readonly FieldInfo _fAlreadyCut = AccessTools.Field(typeof(GrabObject), "alreadyCut");

		private static void FillFoodState(GrabObject g, ref ObjState s)
		{
			s.grabObjectId = g.grabObjectID;
			s.meatType = -1;
			s.cook = -1f;
			s.ketchup = -1f;
			s.mustard = -1f;
			s.coffee = -1f;
			int flags = 0;
			if (g.isStinky())
			{
				flags |= ObjState.BunStinky;
			}
			Bun b = g as Bun;
			if (b != null)
			{
				s.meatType = (int)b.meatType;
				if (b.hasCheddar) flags |= ObjState.BunCheddar;
				if (b.hasLettuce) flags |= ObjState.BunLettuce;
				if (b.hasKetchup) flags |= ObjState.BunKetchup;
				if (b.hasMustard) flags |= ObjState.BunMustard;
				s.ketchup = b.ketchupValue;
				s.mustard = b.mustardValue;
			}
			else
			{
				Meat m = g as Meat;
				if (m != null)
				{
					s.meatType = (int)m.meatType;
				}
			}
			try
			{
				if (_fAlreadyCut.GetValue(g) is bool cut && cut)
				{
					flags |= ObjState.BunCut;
				}
			}
			catch
			{
			}
			s.bunFlags = flags;
			FillFoodExtra(g, ref s);
		}

		private static void PushState(NetObj n, ObjState s)
		{
			n.owner = s.owner;
			n.mode = s.mode;
			n.flags = s.flags;
			n.lastSentPos = new Vector3(s.pos.x, s.pos.y, s.pos.z);
			n.lastSentRot = new Quaternion(s.rot.x, s.rot.y, s.rot.z, s.rot.w);
			n.lastCook = s.cook;
			n.lastKetchup = s.ketchup;
			n.lastMustard = s.mustard;
			n.lastCoffee = s.coffee;
			n.lastBunFlags = s.bunFlags;
			n.lastMeatType = s.meatType;
			n.lastSent = Time.realtimeSinceStartup;
			n.dirty = false;
		}

		public static void ApplyState(PacketReader r, int fromPeerId)
		{
			ObjState s = ObjState.Read(r);
			NetObj n;
			if (!ById.TryGetValue(s.objId, out n) || n.grab == null || n.go == null)
			{
				return;
			}
			if (Session.IsHost && s.owner != fromPeerId)
			{
				return;
			}
			if (!Session.IsHost && fromPeerId != 0)
			{
				return;
			}
			ApplyStateTo(n, s);
			if (Session.IsHost && fromPeerId != 0)
			{

				Session.Broadcast(MsgBuild.ObjStateMsg(s), fromPeerId);
			}
		}

		private static void ApplyStateTo(NetObj n, ObjState s)
		{
			GrabObject g = n.grab;
			NetCtx.ApplyingRemote = true;
			try
			{
				n.owner = s.owner;
				n.mode = s.mode;
				n.flags = s.flags;
				if (!g.canInteract && (s.flags & ObjState.FlagDisabled) == 0)
				{
					g.EnableGrabObject();
				}
				else if (g.canInteract && (s.flags & ObjState.FlagDisabled) != 0)
				{
					g.DisableGrabObject();
				}
				g.additions.Clear();
				for (int i = 0; i < s.additions.Count; i++)
				{
					if (!g.additions.Contains(s.additions[i]))
					{
						g.additions.Add(s.additions[i]);
					}
				}
				foreach (int a in n.pendingAdds)
				{
					if (!g.additions.Contains(a))
					{
						g.additions.Add(a);
					}
				}
				n.pendingAdds.RemoveWhere(delegate(int a)
				{
					return g.additions.Contains(a);
				});
				ApplyFoodState(g, s);

				bool local = PlayerController.instance != null && PlayerController.instance.currentGrabObj == g;
				if (!local)
				{
					n.targetPos = new Vector3(s.pos.x, s.pos.y, s.pos.z);
					n.targetRot = new Quaternion(s.rot.x, s.rot.y, s.rot.z, s.rot.w);
					n.targetVel = new Vector3(s.vel.x, s.vel.y, s.vel.z);
					n.hasTarget = true;
					float d = Vector3.Distance(g.transform.position, n.targetPos);
					if (d > 5f)
					{
						SnapFollower(n);
					}
					EnsureFollower(n);
				}
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		private static void ApplyFoodState(GrabObject g, ObjState s)
		{
			try
			{
				Fries fr = g as Fries;
				Cup cupObj = g as Cup;
				bool friesScooped = fr != null && g.grabObjectID >= 32;
				bool coffeeMade = cupObj != null && g.grabObjectID == 7;
				if (s.grabObjectId >= 0 && g.grabObjectID != s.grabObjectId)
				{
					bool regressFries = friesScooped && s.grabObjectId < 32;
					bool regressCoffee = coffeeMade && s.grabObjectId < 7;
					if (!regressFries && !regressCoffee)
					{
						g.grabObjectID = s.grabObjectId;
					}
				}
				if (fr != null)
				{
					int fidx = g.grabObjectID - 32;
					if (fidx >= 0 && fr.friesObjects != null && fidx < fr.friesObjects.Length)
					{
						if (fr.friesObjects[fidx] != null)
						{
							fr.friesObjects[fidx].SetActive(true);
						}
						fr.interactableName = g.grabObjectID == 32 ? "Fries" : (g.grabObjectID == 33 ? "Onion Rings" : "Nuggets");
						try
						{
							_fFriesHasSomething.SetValue(fr, true);
						}
						catch
						{
						}
					}
				}
				Bun b = g as Bun;
				if (b != null)
				{
					if (s.meatType >= 0)
					{
						b.meatType = (Bun.MeatType)s.meatType;
					}
					if (s.ketchup > b.ketchupValue)
					{
						b.ketchupValue = s.ketchup;
					}
					if (s.mustard > b.mustardValue)
					{
						b.mustardValue = s.mustard;
					}
				}
				if (s.bunFlags >= 0)
				{
					if (b != null)
					{
						bool cheddar = (s.bunFlags & ObjState.BunCheddar) != 0;
						bool lettuce = (s.bunFlags & ObjState.BunLettuce) != 0;
						bool ketchup = (s.bunFlags & ObjState.BunKetchup) != 0;
						bool mustard = (s.bunFlags & ObjState.BunMustard) != 0;
						if (b.hasCheddar != cheddar) b.hasCheddar = cheddar;
						if (b.hasLettuce != lettuce)
						{
							b.hasLettuce = lettuce;
							if (b.lettuceObj != null) b.lettuceObj.SetActive(lettuce);
						}
						if (b.hasKetchup != ketchup)
						{
							if (ketchup)
							{
								b.hasKetchup = true;
								if (b.ketchupObj != null) b.ketchupObj.SetActive(true);
							}
						}
						if (b.hasMustard != mustard)
						{
							if (mustard)
							{
								b.hasMustard = true;
								if (b.mustardObj != null) b.mustardObj.SetActive(true);
							}
						}
					}
					bool stinky = (s.bunFlags & ObjState.BunStinky) != 0;
					if (stinky && !g.isStinky())
					{
						g.SetStinky(true);
					}
					else if (!stinky && g.isStinky())
					{
						_fStinky.SetValue(g, false);
					}
					bool cut = (s.bunFlags & ObjState.BunCut) != 0;
					bool alreadyCut = _fAlreadyCut.GetValue(g) is bool ac && ac;
					if (cut != alreadyCut)
					{
						_fAlreadyCut.SetValue(g, cut);
					}
				}
				ApplyFoodExtra(g, s);
			}
			catch (Exception e)
			{
				
			}
		}

		private static void EnsureFollower(NetObj n)
		{
			GrabObject g = n.grab;
			if (g == null)
			{
				return;
			}
			Rigidbody rb = g.getMyRb();
			if (rb != null && !rb.isKinematic)
			{
				rb.isKinematic = true;
				rb.useGravity = false;
				rb.velocity = Vector3.zero;
				rb.angularVelocity = Vector3.zero;
			}
		}

		private static void RestoreAuthorityBody(NetObj n)
		{
			GrabObject g = n.grab;
			if (g == null)
			{
				return;
			}
			Rigidbody rb = g.getMyRb();
			if (rb == null)
			{
				return;
			}

			PlayerController pc = PlayerController.instance;
			if (pc != null && pc.currentGrabObj == g)
			{
				return;
			}
			if (rb.isKinematic && n.mode == 0 && (n.flags & ObjState.FlagHeld) == 0)
			{
				rb.isKinematic = false;
				rb.useGravity = true;
			}
		}

		private static void SnapFollower(NetObj n)
		{
			GrabObject g = n.grab;
			g.transform.position = n.targetPos;
			g.transform.rotation = n.targetRot;
			n.hasTarget = false;
		}

		public static void NotifyGrabbed(GrabObject g)
		{
			if (!Session.Active || g == null)
			{
				return;
			}
			NetObj n = Lookup(g);
			if (n == null)
			{

				n = EagerBind(g);
				if (n == null)
				{
					return;
				}
			}
			n.dirty = true;
			if (Session.IsHost)
			{
				GrantTo(n, Session.SelfId);
			}
			else
			{
				Session.SendToHost(MsgBuild.ObjClaim(n.id));
			}
		}

		private static NetObj EagerBind(GrabObject g)
		{
			if (!g.gameObject.scene.IsValid() || !g.gameObject.activeInHierarchy)
			{
				return null;
			}
			if (Session.IsHost)
			{
				int id = _nextDynamic++;
				NetObj n = BindNew(id, g, -1, ComputeMode(g), ComputeFlags(g, true), g.gameObject.name, PrefabIndexOf(g));
				Session.Broadcast(MsgBuild.ObjSpawn(n.id, n.prefabIndex, 0, -1, -1, n.owner, n.mode, n.flags,
					new NetVec3(g.transform.position.x, g.transform.position.y, g.transform.position.z),
					ToNetQuat(g.transform.rotation), n.spawnName));
				
				return n;
			}
			if (_pendingByRef.ContainsKey(g))
			{
				return null;
			}
			int seq = ++_localSeq;
			_pendingBySeq[seq] = g;
			_pendingByRef[g] = seq;
			_pendingTime[seq] = Time.realtimeSinceStartup;
			Session.SendToHost(MsgBuild.ObjSpawnLocal(seq, PrefabIndexOf(g),
				new NetVec3(g.transform.position.x, g.transform.position.y, g.transform.position.z),
				ToNetQuat(g.transform.rotation), Session.SelfId, ComputeMode(g),
				ComputeFlags(g, true), g.gameObject.name));
			
			return null;
		}

		public static void HandleClaim(int fromPeerId, PacketReader r)
		{
			if (!Session.IsHost)
			{
				return;
			}
			int objId = r.I32();
			NetObj n;
			if (!ById.TryGetValue(objId, out n) || n.grab == null)
			{
				return;
			}
			GrantTo(n, fromPeerId);
		}

		private static void GrantTo(NetObj n, int newOwner)
		{
			int old = n.owner;
			n.owner = newOwner;
			n.dirty = true;
			Session.Broadcast(MsgBuild.ObjGrant(n.id, newOwner));
			ApplyGrant(n, newOwner, old);
		}

		public static void HandleGrant(PacketReader r)
		{
			int objId = r.I32();
			int owner = r.I32();
			NetObj n;
			if (!ById.TryGetValue(objId, out n) || n.grab == null)
			{
				return;
			}
			ApplyGrant(n, owner, n.owner);
		}

		private static void ApplyGrant(NetObj n, int owner, int oldOwner)
		{
			GrabObject g = n.grab;
			bool iHoldIt = PlayerController.instance != null && PlayerController.instance.currentGrabObj == g;
			n.owner = owner;
			n.dirty = true;

			if (owner != Session.SelfId && iHoldIt && owner != -1)
			{

				try
				{
					g.Throw(false, false);
					PlayerController.instance.SetGrabObj(null);
				}
				catch (Exception e)
				{
					
				}
			}
			if (owner != -1 && owner != oldOwner)
			{

				if (!(owner == Session.SelfId && iHoldIt))
				{
					ReplicateEquip(g);
				}
			}
			if (LocalAuthority(n))
			{
				RestoreAuthorityBody(n);
			}
		}

		private static void ReplicateEquip(GrabObject g)
		{
			TriggerObject[] all = Resources.FindObjectsOfTypeAll<TriggerObject>();
			for (int i = 0; i < all.Length; i++)
			{
				TriggerObject t = all[i];
				if (t == null || !t.gameObject.scene.IsValid())
				{
					continue;
				}
				try
				{
					t.OnGrabObjEquiped(g);
				}
				catch
				{
				}
			}
		}

		public static void NotifyDestroyed(GrabObject g)
		{
			NetObj n = Lookup(g);
			if (n == null)
			{
				return;
			}
			AnnounceDespawn(n.id);
		}

		private static void AnnounceDespawn(int objId)
		{
			if (!Session.Active)
			{
				return;
			}
			byte[] m = MsgBuild.ObjDespawn(objId);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleDespawn(PacketReader r, int fromPeerId)
		{
			int objId = r.I32();
			NetObj n;
			if (!ById.TryGetValue(objId, out n))
			{
				return;
			}
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.ObjDespawn(objId), fromPeerId);
			}
			DestroyNetObj(n);
		}

		private static void DestroyNetObj(NetObj n)
		{
			ById.Remove(n.id);
			if (n.grab != null)
			{
				ByRef.Remove(n.grab);
			}
			if (n.go != null)
			{
				try
				{
					UnityEngine.Object.Destroy(n.go);
				}
				catch
				{
				}
			}
		}

		public static void HandleSpawn(PacketReader r, int fromPeerId)
		{
			int objId = r.I32();
			int prefabIndex = r.I32();
			int originPeer = r.I32();
			int originSeq = r.I32();
			r.I32();
			int ownerId = r.I32();
			byte mode = r.U8();
			byte flags = r.U8();
			NetVec3 pos = r.V3();
			NetQuat rot = r.Q4();
			string spawnName = r.Str();

			Vector3 p = new Vector3(pos.x, pos.y, pos.z);
			Quaternion q = new Quaternion(rot.x, rot.y, rot.z, rot.w);

			GrabObject g = null;
			if (originPeer == Session.SelfId && originSeq >= 0)
			{
				GrabObject pg;
				if (_pendingBySeq.TryGetValue(originSeq, out pg))
				{
					_pendingBySeq.Remove(originSeq);
					_pendingByRef.Remove(pg);
					_pendingTime.Remove(originSeq);
					if (pg != null && pg.gameObject.scene.IsValid() && pg.gameObject.activeInHierarchy
						&& pg.gameObject.name == spawnName)
					{
						g = pg;
					}
				}
			}

			if (g == null)
			{
				g = FindBindable(spawnName, p, objId);
			}
			if (g == null)
			{
				g = InstantiateFromPrefab(prefabIndex, spawnName, p, q);
			}
			if (g == null)
			{
				
				return;
			}

			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.ObjSpawn(objId, prefabIndex, originPeer, originSeq, -1, ownerId, mode, flags, pos, rot, spawnName), fromPeerId);
			}

			NetObj n;
			if (ByRef.TryGetValue(g, out n))
			{

				if (n.id != objId)
				{
					ById.Remove(n.id);
					n.id = objId;
					ById[objId] = n;
				}
				n.owner = ownerId;
				n.mode = mode;
				n.flags = flags;
				if (!string.IsNullOrEmpty(spawnName))
				{
					n.spawnName = spawnName;
				}
			}
			else
			{
				n = BindNew(objId, g, ownerId, mode, flags, spawnName, prefabIndex);
				n.originPeer = originPeer;
				n.originSeq = originSeq;
			}
			ClearPending(g);
			n.dirty = true;
			n.hasTarget = true;
			n.targetPos = p;
			n.targetRot = q;
			if (ownerId != Session.SelfId)
			{
				EnsureFollower(n);
			}
			if ((flags & ObjState.FlagHeld) != 0 && ownerId != Session.SelfId)
			{

				ReplicateEquip(g);
			}
			ClaimIfHeld(n);
		}

		public static void HandleSpawnLocal(int fromPeerId, PacketReader r)
		{
			if (!Session.IsHost)
			{
				return;
			}
			int seq = r.I32();
			int prefabIndex = r.I32();
			NetVec3 pos = r.V3();
			NetQuat rot = r.Q4();
			int ownerId = r.I32();
			byte mode = r.U8();
			byte flags = r.U8();
			string spawnName = r.Str();

			Vector3 p = new Vector3(pos.x, pos.y, pos.z);
			GrabObject g = FindBindable(spawnName, p, -1);
			if (g == null)
			{
				g = InstantiateFromPrefab(prefabIndex, spawnName, p, new Quaternion(rot.x, rot.y, rot.z, rot.w));
			}
			if (g == null)
			{

				
				return;
			}

			int objId;
			NetObj bound;
			if (ByRef.TryGetValue(g, out bound))
			{
				objId = bound.id;
				bound.owner = ownerId;
				bound.mode = mode;
				bound.flags = flags;
				bound.dirty = true;
			}
			else
			{
				objId = _nextDynamic++;
				bound = BindNew(objId, g, ownerId, mode, flags, spawnName, prefabIndex);
				bound.dirty = true;
			}
			Session.Broadcast(MsgBuild.ObjSpawn(objId, prefabIndex, fromPeerId, seq, -1, ownerId, mode, flags, pos, rot, spawnName));
			ClaimIfHeld(bound);
		}

		private static GrabObject FindBindable(string spawnName, Vector3 pos, int objId)
		{
			GrabObject nearestFree = null;
			float bestFree = float.MaxValue;
			GrabObject nearestAny = null;
			float bestAny = float.MaxValue;
			GrabObject nearestInactive = null;
			float bestInactive = float.MaxValue;
			GrabObject[] all = Resources.FindObjectsOfTypeAll<GrabObject>();
			for (int i = 0; i < all.Length; i++)
			{
				GrabObject g = all[i];
				if (g == null || !g.gameObject.scene.IsValid())
				{
					continue;
				}
				if (g.gameObject.name != spawnName)
				{
					continue;
				}
				NetObj n;
				bool tracked = ByRef.TryGetValue(g, out n);
				if (tracked && n.id == objId)
				{
					return g;
				}
				float d = Vector3.Distance(g.transform.position, pos);
				if (!g.gameObject.activeInHierarchy)
				{

					if (!tracked && d < bestInactive)
					{
						bestInactive = d;
						nearestInactive = g;
					}
					continue;
				}
				if (!tracked && d < bestFree)
				{
					bestFree = d;
					nearestFree = g;
				}
				if (d < bestAny)
				{
					bestAny = d;
					nearestAny = g;
				}
			}

			if (nearestFree != null)
			{
				return nearestFree;
			}
			if (nearestInactive != null)
			{
				return nearestInactive;
			}
			return nearestAny;
		}

		private static GrabObject InstantiateFromPrefab(int prefabIndex, string spawnName, Vector3 pos, Quaternion rot)
		{
			GameManager gm = GameManager.instance;
			if (gm != null && gm.allObjects != null && prefabIndex >= 0 && prefabIndex < gm.allObjects.Length
				&& gm.allObjects[prefabIndex] != null)
			{
				GameObject go = UnityEngine.Object.Instantiate(gm.allObjects[prefabIndex], pos, rot);
				if (!string.IsNullOrEmpty(spawnName))
				{
					go.name = spawnName;
				}
				return go.GetComponent<GrabObject>();
			}
			if (!string.IsNullOrEmpty(spawnName) && spawnName.StartsWith("CompleteHotdog"))
			{
				return BuildHotdog(pos, rot);
			}
			return null;
		}

		private static GrabObject BuildHotdog(Vector3 pos, Quaternion rot)
		{
			GameObject obj = new GameObject("CompleteHotdog");
			obj.transform.position = pos;
			obj.transform.rotation = rot;
			GrabObject g = obj.AddComponent<GrabObject>();
			g.interactableName = "Hotdog";
			g.throwForce = 60f;
			g.grabObjectID = 3;
			Rigidbody rb = obj.AddComponent<Rigidbody>();
			rb.mass = 0.13f;
			rb.useGravity = false;
			rb.isKinematic = false;
			rb.constraints = RigidbodyConstraints.FreezeAll;
			BoxCollider box = obj.AddComponent<BoxCollider>();
			box.size = new Vector3(0.37f, 0.17f, 0.218f);
			box.center = new Vector3(0f, -0.015f, 0f);
			return g;
		}

		public static void NotifyAddition(GrabObject g, int additionId)
		{
			if (!Session.Active || g == null || NetCtx.ApplyingRemote)
			{
				return;
			}
			NetObj n = Lookup(g);
			if (n == null)
			{
				return;
			}
			if (LocalAuthority(n))
			{
				n.dirty = true;
				return;
			}
			n.pendingAdds.Add(additionId);
			byte[] m = MsgBuild.AddAddition(n.id, additionId);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleAddAddition(int fromPeerId, PacketReader r)
		{
			int objId = r.I32();
			int additionId = r.I32();
			NetObj n;
			if (!ById.TryGetValue(objId, out n) || n.grab == null)
			{
				return;
			}
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.AddAddition(objId, additionId), fromPeerId);
			}
			NetCtx.ApplyingRemote = true;
			try
			{
				n.grab.AddAddition(additionId);
				n.dirty = true;
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		public static void SendCut(GrabObject g)
		{
			if (!Session.Active || NetCtx.ApplyingRemote || g == null)
			{
				return;
			}
			NetObj n = Lookup(g);
			if (n == null)
			{
				return;
			}
			byte[] m = MsgBuild.ObjCut(n.id);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleCut(PacketReader r, int fromPeerId)
		{
			int objId = r.I32();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.ObjCut(objId), fromPeerId);
			}
			NetObj n;
			if (!ById.TryGetValue(objId, out n) || n.grab == null)
			{
				return;
			}
			NetCtx.ApplyingRemote = true;
			try
			{
				n.grab.Cut();
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		public static void SendInteract(int objId, InteractMethod m, bool argB, float argF)
		{
			if (!Session.Active)
			{
				return;
			}
			byte[] msg = MsgBuild.Interact(objId, m, argB, argF);
			if (Session.IsHost)
			{
				Session.Broadcast(msg);
			}
			else
			{
				Session.SendToHost(msg);
			}
		}

		public static void HandleInteract(int fromPeerId, PacketReader r)
		{
			int objId = r.I32();
			InteractMethod m = (InteractMethod)r.U8();
			bool argB = r.Bool();
			float argF = r.F32();
			if (Session.IsHost)
			{

				Session.Broadcast(MsgBuild.Interact(objId, m, argB, argF), fromPeerId);
			}
			ApplyInteract(fromPeerId, objId, m, argB, argF);
		}

		private static void ApplyInteract(int fromPeerId, int objId, InteractMethod m, bool argB, float argF)
		{
			Interactable it = InteractableById(objId);
			if (it == null)
			{
				return;
			}
			NetCtx.FromRemoteInteract = true;
			NetCtx.RemoteInteractPhone = it is Phone;
			NetCtx.DialogOwnerOverride = fromPeerId;
			try
			{
				switch (m)
				{
					case InteractMethod.Press:
						it.Press();
						break;
					case InteractMethod.Release:
						it.Release();
						break;
					case InteractMethod.Drag:
						it.Drag();
						break;
					case InteractMethod.Use:
						it.Use();
						break;
					case InteractMethod.StartUse:
						it.StartUse();
						break;
					case InteractMethod.StopUse:
						it.StopUse();
						break;
					case InteractMethod.UseSecond:
						it.UseSecond(argB);
						break;
				}
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.FromRemoteInteract = false;
				NetCtx.RemoteInteractPhone = false;
				NetCtx.DialogOwnerOverride = -1;
			}
		}

		public static void ReleasePeerObjects(int peerId)
		{
			if (!Session.Active)
			{
				return;
			}
			List<int> taken = null;
			foreach (KeyValuePair<int, NetObj> kv in ById)
			{
				if (kv.Value.owner == peerId)
				{
					if (taken == null)
					{
						taken = new List<int>();
					}
					taken.Add(kv.Key);
				}
			}
			if (taken == null)
			{
				return;
			}
			for (int i = 0; i < taken.Count; i++)
			{
				NetObj n;
				if (!ById.TryGetValue(taken[i], out n))
				{
					continue;
				}
				if (Session.IsHost)
				{
					GrantTo(n, -1);
				}
				else
				{
					n.owner = -1;
					n.dirty = true;
				}
			}
		}

		public static void WriteObjectsSnapshot(PacketWriter w)
		{
			List<NetObj> list = new List<NetObj>();
			foreach (KeyValuePair<int, NetObj> kv in ById)
			{
				NetObj n = kv.Value;
				if (n.id >= 0 && n.grab != null && n.go != null)
				{
					list.Add(n);
				}
			}
			list.Sort(delegate(NetObj a, NetObj b)
			{
				return a.id.CompareTo(b.id);
			});
			w.I32(list.Count);
			for (int i = 0; i < list.Count; i++)
			{
				NetObj n = list[i];
				BuildState(n).Write(w);
				w.I32(n.prefabIndex);
				w.Str(n.spawnName);
			}
		}

		public static void ReadObjectsSnapshot(PacketReader r)
		{
			int count = r.I32();
			for (int i = 0; i < count; i++)
			{
				ObjState s = ObjState.Read(r);
				int prefabIndex = r.I32();
				string spawnName = r.Str();
				Vector3 pos = new Vector3(s.pos.x, s.pos.y, s.pos.z);
				Quaternion rot = new Quaternion(s.rot.x, s.rot.y, s.rot.z, s.rot.w);
				try
				{
					NetObj n;
					if (!ById.TryGetValue(s.objId, out n) || n.grab == null || n.go == null)
					{
						GrabObject g = FindBindable(spawnName, pos, s.objId);
						if (g == null)
						{
							g = InstantiateFromPrefab(prefabIndex, spawnName, pos, rot);
						}
						if (g == null)
						{
							continue;
						}
						ClearPending(g);
						NetObj existing;
						if (ByRef.TryGetValue(g, out existing))
						{
							ById.Remove(existing.id);
							existing.id = s.objId;
							ById[s.objId] = existing;
							n = existing;
						}
						else
						{
							n = BindNew(s.objId, g, s.owner, s.mode, s.flags, spawnName, prefabIndex);
						}
					}
					n.prefabIndex = prefabIndex;
					if (string.IsNullOrEmpty(n.spawnName))
					{
						n.spawnName = spawnName;
					}
					ApplyStateTo(n, s);
				}
				catch (Exception e)
				{
					
				}
			}
		}

		public static void ResetObjects()
		{
			ById.Clear();
			ByRef.Clear();
			StaticById.Clear();
			StaticIds.Clear();
			ClearAllPending();
			ResetCooking();
			_nextDynamic = Proto.DynamicIdBase;
			_lastSweep = 0f;
			_lastFullResync = 0f;
			_resyncCursor = 0;
		}

		public static void UpdateObjects()
		{
			if (!Session.Active || !SceneReady)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;

			InterpolateAll(now);
			SendStates(now);
			Sweep(now);
			FlushDecals(now);
		}

		private static void InterpolateAll(float now)
		{
			foreach (KeyValuePair<int, NetObj> kv in ById)
			{
				NetObj n = kv.Value;
				if (n.grab == null || n.go == null || !n.hasTarget)
				{
					continue;
				}
				if (PlayerController.instance != null && PlayerController.instance.currentGrabObj == n.grab)
				{
					continue;
				}
				if (LocalAuthority(n))
				{
					continue;
				}
				Transform t = n.grab.transform;
				float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
				t.position = Vector3.Lerp(t.position, n.targetPos, k);
				t.rotation = Quaternion.Slerp(t.rotation, n.targetRot, k);
				if (Vector3.Distance(t.position, n.targetPos) < 0.01f)
				{
					t.position = n.targetPos;
					t.rotation = n.targetRot;
					n.hasTarget = false;
				}
			}
		}

		private static void SendStates(float now)
		{
			foreach (KeyValuePair<int, NetObj> kv in ById)
			{
				NetObj n = kv.Value;
				if (n.grab == null || n.go == null || n.id < 0)
				{
					continue;
				}
				if (!LocalAuthority(n))
				{
					continue;
				}
				PlayerController pc = PlayerController.instance;
				bool held = pc != null && pc.currentGrabObj == n.grab;
				float interval = held ? 0.05f : 0.1f;
				if (!n.dirty && now - n.lastSent < interval)
				{
					continue;
				}
				ObjState s = BuildState(n);
				Vector3 p = new Vector3(s.pos.x, s.pos.y, s.pos.z);
				Quaternion q = new Quaternion(s.rot.x, s.rot.y, s.rot.z, s.rot.w);
				bool foodChanged = s.cook != n.lastCook || s.ketchup != n.lastKetchup
					|| s.mustard != n.lastMustard || s.coffee != n.lastCoffee
					|| s.bunFlags != n.lastBunFlags || s.meatType != n.lastMeatType;
				if (n.mode == 1 && !held && !foodChanged && Vector3.Distance(p, n.lastSentPos) < 0.5f
					&& Quaternion.Angle(q, n.lastSentRot) < 15f && !n.dirty)
				{
					n.lastSent = now;
					continue;
				}
				if (!n.dirty && !foodChanged && p == n.lastSentPos && q == n.lastSentRot && s.flags == n.flags && s.owner == n.owner)
				{
					n.lastSent = now;
					continue;
				}
				PushState(n, s);
				byte[] msg = MsgBuild.ObjStateMsg(s);
				if (Session.IsHost)
				{
					Session.Broadcast(msg);
				}
				else
				{
					Session.SendToHost(msg);
				}
			}

			if (Session.IsHost && now - _lastFullResync > 0.125f)
			{
				_lastFullResync = now;
				List<NetObj> world = null;
				foreach (KeyValuePair<int, NetObj> kv in ById)
				{
					NetObj o = kv.Value;
					if (o.owner == -1 && o.grab != null && o.go != null && o.id >= 0)
					{
						if (world == null)
						{
							world = new List<NetObj>();
						}
						world.Add(o);
					}
				}
				if (world != null && world.Count > 0)
				{
					world.Sort(delegate(NetObj a, NetObj b) { return a.id.CompareTo(b.id); });
					int take = Mathf.Min(8, world.Count);
					if (_resyncCursor >= world.Count)
					{
						_resyncCursor = 0;
					}
					for (int i = 0; i < take; i++)
					{
						NetObj o = world[(_resyncCursor + i) % world.Count];
						ObjState s = BuildState(o);
						PushState(o, s);
						Session.Broadcast(MsgBuild.ObjStateMsg(s));
					}
					_resyncCursor = (_resyncCursor + take) % world.Count;
				}
			}
		}

		private static void Sweep(float now)
		{
			if (now - _lastSweep < 0.5f)
			{
				return;
			}
			_lastSweep = now;

			List<NetObj> dead = null;
			foreach (KeyValuePair<int, NetObj> kv in ById)
			{
				NetObj n = kv.Value;
				if (n.go == null)
				{
					if (dead == null)
					{
						dead = new List<NetObj>();
					}
					dead.Add(n);
				}
				else if (n.grab == null)
				{
					if (dead == null)
					{
						dead = new List<NetObj>();
					}
					dead.Add(n);
				}
			}
			if (dead != null)
			{
				for (int i = 0; i < dead.Count; i++)
				{
					NetObj n = dead[i];
					bool goDead = n.go == null;
					ById.Remove(n.id);
					if (n.grab != null)
					{
						ByRef.Remove(n.grab);
					}
					if (goDead && Session.Active && n.id >= 0)
					{
						AnnounceDespawn(n.id);
					}
				}
			}

			GrabObject[] all = Resources.FindObjectsOfTypeAll<GrabObject>();
			for (int i = 0; i < all.Length; i++)
			{
				GrabObject g = all[i];
				if (g == null || !g.gameObject.scene.IsValid() || !g.gameObject.activeInHierarchy)
				{
					continue;
				}
				if (ByRef.ContainsKey(g))
				{
					continue;
				}
				if (!Session.Active)
				{
					continue;
				}
				if (Session.IsHost)
				{
					int id = _nextDynamic++;
					NetObj n = BindNew(id, g, -1, ComputeMode(g), ComputeFlags(g, false), g.gameObject.name, PrefabIndexOf(g));
					Session.Broadcast(MsgBuild.ObjSpawn(n.id, n.prefabIndex, 0, -1, -1, n.owner, n.mode, n.flags,
						new NetVec3(g.transform.position.x, g.transform.position.y, g.transform.position.z),
						ToNetQuat(g.transform.rotation), n.spawnName));
					ClaimIfHeld(n);
				}
				else
				{

					int pseq;
					if (_pendingByRef.TryGetValue(g, out pseq))
					{
						float t;
						if (_pendingTime.TryGetValue(pseq, out t) && now - t < 10f)
						{
							continue;
						}

						_pendingByRef.Remove(g);
						_pendingBySeq.Remove(pseq);
						_pendingTime.Remove(pseq);
					}
					int seq = ++_localSeq;
					_pendingBySeq[seq] = g;
					_pendingByRef[g] = seq;
					_pendingTime[seq] = now;
					Session.SendToHost(MsgBuild.ObjSpawnLocal(seq, PrefabIndexOf(g),
						new NetVec3(g.transform.position.x, g.transform.position.y, g.transform.position.z),
						ToNetQuat(g.transform.rotation), Session.SelfId, ComputeMode(g),
						ComputeFlags(g, false), g.gameObject.name));
				}
			}
		}

		private static NetQuat ToNetQuat(Quaternion q)
		{
			return new NetQuat(q.x, q.y, q.z, q.w);
		}
	}
}
