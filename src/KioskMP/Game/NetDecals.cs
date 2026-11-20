using System;
using System.Collections.Generic;
using UnityEngine;

namespace KioskMP
{
	public static partial class Game
	{
		private const int MaxDecalQueue = 512;
		private const float DecalFlushInterval = 0.1f;

		private static readonly List<DecalRec> _decalQueue = new List<DecalRec>();
		private static readonly Dictionary<string, GameObject> _decalPrefabs = new Dictionary<string, GameObject>();
		private static float _lastDecalFlush;

		public static void NotifyDecal(GrabObject tool, Transform parent, int beforeCount)
		{
			if (!Session.Active || NetCtx.ApplyingRemote || NetCtx.FromRemoteInteract)
			{
				return;
			}
			if (tool == null || parent == null || beforeCount < 0)
			{
				return;
			}
			int after = parent.childCount;
			if (after <= beforeCount)
			{
				return;
			}
			Transform child = parent.GetChild(after - 1);
			if (child == null)
			{
				return;
			}
			GameObject prefab = null;
			byte kind = 0;
			PancakeMix mix = tool as PancakeMix;
			if (mix != null)
			{
				prefab = mix.decal;
				kind = 1;
			}
			else
			{
				SauceBottle bottle = tool as SauceBottle;
				if (bottle != null)
				{
					prefab = bottle.decal;
				}
			}
			if (prefab == null)
			{
				return;
			}
			NetObj n = Lookup(tool);
			int toolId = n != null ? n.id : StaticIdOf(tool);
			DecalRec d = new DecalRec();
			d.toolId = toolId;
			d.kind = kind;
			d.pos = new NetVec3(child.position.x, child.position.y, child.position.z);
			Quaternion q = child.rotation;
			d.rot = new NetQuat(q.x, q.y, q.z, q.w);
			Vector3 s = child.localScale;
			d.scale = new NetVec3(s.x, s.y, s.z);
			d.prefab = prefab.name;
			_decalQueue.Add(d);
			if (_decalQueue.Count > MaxDecalQueue)
			{
				_decalQueue.RemoveAt(0);
			}
		}

		public static void FlushDecals(float now)
		{
			if (!Session.Active)
			{
				_decalQueue.Clear();
				return;
			}
			if (_decalQueue.Count == 0 || now - _lastDecalFlush < DecalFlushInterval)
			{
				return;
			}
			_lastDecalFlush = now;
			byte[] m = MsgBuild.DecalBatch(_decalQueue);
			_decalQueue.Clear();
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleDecalBatch(int fromPeerId, PacketReader r)
		{
			int count = r.I32();
			if (count < 0)
			{
				count = 0;
			}
			if (count > MaxDecalQueue)
			{
				count = MaxDecalQueue;
			}
			List<DecalRec> list = new List<DecalRec>(count);
			for (int i = 0; i < count; i++)
			{
				DecalRec d = new DecalRec();
				d.toolId = r.I32();
				d.kind = r.U8();
				NetVec3 p = r.V3();
				NetQuat q = r.Q4();
				NetVec3 s = r.V3();
				d.pos = new NetVec3(p.x, p.y, p.z);
				d.rot = new NetQuat(q.x, q.y, q.z, q.w);
				d.scale = new NetVec3(s.x, s.y, s.z);
				d.prefab = r.Str();
				list.Add(d);
			}
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.DecalBatch(list), fromPeerId);
			}
			for (int i = 0; i < list.Count; i++)
			{
				ApplyDecal(list[i]);
			}
		}

		private static void ApplyDecal(DecalRec d)
		{
			GameObject prefab = null;
			NetObj tn;
			if (d.toolId >= 0 && ById.TryGetValue(d.toolId, out tn) && tn.grab != null)
			{
				PancakeMix mix = tn.grab as PancakeMix;
				if (mix != null)
				{
					prefab = mix.decal;
				}
				else
				{
					SauceBottle bottle = tn.grab as SauceBottle;
					if (bottle != null)
					{
						prefab = bottle.decal;
					}
				}
			}
			if (prefab == null)
			{
				prefab = FindDecalPrefab(d.kind, d.prefab);
			}
			if (prefab == null)
			{
				return;
			}
			Transform parent;
			if (d.kind == 1)
			{
				parent = GameManager.instance != null ? GameManager.instance.transform : null;
			}
			else
			{
				parent = MarkerController.instance != null ? MarkerController.instance.transform : null;
			}
			if (parent == null)
			{
				return;
			}
			Vector3 pos = new Vector3(d.pos.x, d.pos.y, d.pos.z);
			Quaternion rot = new Quaternion(d.rot.x, d.rot.y, d.rot.z, d.rot.w);
			try
			{
				GameObject go = UnityEngine.Object.Instantiate(prefab, pos, rot, parent);
				go.name = prefab.name;
				go.transform.localScale = new Vector3(d.scale.x, d.scale.y, d.scale.z);
				go.SetActive(true);
			}
			catch (Exception e)
			{
				
			}
		}

		private static GameObject FindDecalPrefab(byte kind, string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			string key = kind + "|" + name;
			GameObject cached;
			if (_decalPrefabs.TryGetValue(key, out cached))
			{
				if (cached != null)
				{
					return cached;
				}
				_decalPrefabs.Remove(key);
			}
			GameObject found = null;
			if (kind == 1)
			{
				PancakeMix[] mixes = Resources.FindObjectsOfTypeAll<PancakeMix>();
				for (int i = 0; i < mixes.Length; i++)
				{
					PancakeMix m = mixes[i];
					if (m != null && m.decal != null && m.decal.name == name)
					{
						found = m.decal;
						break;
					}
				}
			}
			else
			{
				SauceBottle[] bottles = Resources.FindObjectsOfTypeAll<SauceBottle>();
				for (int i = 0; i < bottles.Length; i++)
				{
					SauceBottle b = bottles[i];
					if (b != null && b.decal != null && b.decal.name == name)
					{
						found = b.decal;
						break;
					}
				}
			}
			if (found != null)
			{
				_decalPrefabs[key] = found;
			}
			return found;
		}

		public static void NotifyMarkerColor(Marker mk)
		{
			if (!Session.Active || mk == null || NetCtx.ApplyingRemote || NetCtx.FromRemoteInteract)
			{
				return;
			}
			NetObj n = Lookup(mk);
			if (n == null)
			{
				return;
			}
			byte[] m = MsgBuild.MarkerColor(n.id, mk.currentColor);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleMarkerColor(int fromPeerId, PacketReader r)
		{
			int objId = r.I32();
			int color = r.I32();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.MarkerColor(objId, color), fromPeerId);
			}
			NetObj n;
			if (!ById.TryGetValue(objId, out n) || n.grab == null)
			{
				return;
			}
			Marker mk = n.grab as Marker;
			if (mk == null)
			{
				return;
			}
			ApplyMarkerColor(mk, color);
		}

		private static void ApplyMarkerColor(Marker mk, int color)
		{
			try
			{
				Color[] cols = mk.markerColors;
				if (cols == null || cols.Length == 0)
				{
					return;
				}
				int idx = color % cols.Length;
				if (idx < 0)
				{
					idx += cols.Length;
				}
				mk.currentColor = idx;
				if (mk.decalMat != null && mk.trailDecal != null)
				{
					Material mat = new Material(mk.decalMat);
					mat.color = cols[idx];
					mk.trailDecal.material = mat;
				}
				MeshRenderer mr = mk.GetComponent<MeshRenderer>();
				if (mr != null)
				{
					Material[] mats = mr.materials;
					if (mats != null && mats.Length > 0 && mats[0] != null)
					{
						mats[0].color = cols[idx];
					}
				}
			}
			catch (Exception e)
			{
				
			}
		}

		private static void ResetDecals()
		{
			_decalQueue.Clear();
			_decalPrefabs.Clear();
			_lastDecalFlush = 0f;
		}
	}
}
