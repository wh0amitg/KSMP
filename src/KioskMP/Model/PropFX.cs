using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KioskMP
{
	public static class PropFX
	{
		private const float LifeTime = 120f;
		private const float SpawnDist = 1.6f;
		private const int MaxProps = 8;

		private sealed class PropDef
		{
			public Mesh mesh;
			public Material[] mats;
		}

		private sealed class Prop
		{
			public GameObject go;
			public float until;
		}

		private static PropDef[] _defs;
		private static readonly List<Prop> _props = new List<Prop>();

		public static void Tick()
		{
			for (int i = _props.Count - 1; i >= 0; i--)
			{
				Prop p = _props[i];
				if (p.go == null)
				{
					_props.RemoveAt(i);
					continue;
				}
				if (Time.realtimeSinceStartup > p.until)
				{
					UnityEngine.Object.Destroy(p.go);
					_props.RemoveAt(i);
				}
			}
		}

		public static void ClearAll()
		{
			for (int i = 0; i < _props.Count; i++)
			{
				if (_props[i].go != null)
				{
					UnityEngine.Object.Destroy(_props[i].go);
				}
			}
			_props.Clear();
		}

		public static void Command(string text) //not used now
		{
			string cmd = text.Trim().Split(' ')[0].ToLowerInvariant();
			if (cmd == "/help")
			{
				Game.AddToast("Commands: /wolf, /clear, /help");
				return;
			}
			if (cmd == "/clear")
			{
				ClearAll();
				Game.AddToast("Props cleared");
				return;
			}
			int propId;
			if (cmd == "/wolf")
			{
				if (Session.Active && !Session.IsHost)
				{
					Game.AddToast("Only the host can spawn the wolf");
					return;
				}
				propId = 0;
			}
			else
			{
				Game.AddToast("Unknown command: " + cmd + " (try /help)");
				return;
			}
			Vector3 pos;
			float yaw;
			string err = SpawnPointInFront(out pos, out yaw);
			if (err != null)
			{
				Game.AddToast(err);
				return;
			}
			SpawnLocal(propId, Session.SelfId, pos, yaw);
			Game.AddToast("Wolf spawned");
			Game.AddChat("[KP] " + Session.SelfName + " summoned a wolf");
			if (Session.Active && Session.SelfId >= 0)
			{
				byte[] m = MsgBuild.PropSpawn(Session.SelfId, propId, new NetVec3(pos.x, pos.y, pos.z), yaw);
				if (Session.IsHost)
				{
					Session.Broadcast(m);
				}
				else
				{
					Session.SendToHost(m);
				}
			}
		}

		public static void SpawnLocal(int propId, int ownerId, Vector3 pos, float yaw)
		{
			if (propId != 0)
			{
				return;
			}
			EnsureLoaded(propId);
			PropDef def = propId >= 0 && propId < _defs.Length ? _defs[propId] : null;
			if (def == null || def.mesh == null)
			{
				Game.AddToast("prop model not loaded");
				return;
			}
			while (_props.Count >= MaxProps)
			{
				if (_props[0].go != null)
				{
					UnityEngine.Object.Destroy(_props[0].go);
				}
				_props.RemoveAt(0);
			}
			GameObject go = new GameObject("KioskMP_Wolf");
			go.transform.position = pos + Vector3.up * 0.12f;
			go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
			go.AddComponent<MeshFilter>().sharedMesh = def.mesh;
			go.AddComponent<MeshRenderer>().sharedMaterials = def.mats;

			Rigidbody rb = go.AddComponent<Rigidbody>();
			rb.mass = 2.5f;
			rb.drag = 0.4f;
			rb.angularDrag = 0.9f;
			rb.interpolation = RigidbodyInterpolation.Interpolate;
			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			rb.useGravity = true;

			BoxCollider col = go.AddComponent<BoxCollider>();
			col.center = def.mesh.bounds.center;
			col.size = def.mesh.bounds.size;

			GrabObject grab = go.AddComponent<GrabObject>();
			grab.interactableName = "Wolf";
			grab.grabObjectID = -1;
			grab.throwForce = 55f;
			grab.breakable = false;
			grab.EnableGrabObject();

			Prop p = new Prop();
			p.go = go;
			p.until = Time.realtimeSinceStartup + LifeTime;
			_props.Add(p);
			
			
		}

		private static void EnsureLoaded(int propId)
		{
			if (_defs == null)
			{
				_defs = new PropDef[1];
			}
			if (propId < 0 || propId >= _defs.Length)
			{
				return;
			}
			if (_defs[propId] != null)
			{
				return;
			}
			try
			{
				_defs[propId] = LoadProp(propId);
			}
			catch (Exception e)
			{
				
			}
		}

		private static PropDef LoadProp(int id)
		{
			string fbxName = "PropWolf.fbx"; //easter egg to family time
			string bodyTex = "PropWolf_Body.png"; //easter egg to family time
			string eyeTex = "PropWolf_Eye.png"; //easter egg to family time
			float targetH = 1.77f;
			string used;
			byte[] fbx = ReadAsset(fbxName, "KioskMP." + fbxName, out used);
			if (fbx == null)
			{
				
				return null;
			}
			FbxMesh fm = Fbx.Load(fbx);
			
			

			float h = fm.maxY - fm.minY;
			float s = h > 1e-5f ? targetH / h : 1f;
			float cx = (fm.minX + fm.maxX) * 0.5f;
			float cz = (fm.minZ + fm.maxZ) * 0.5f;

			int vc = fm.triangleCount * 3;
			Vector3[] verts = new Vector3[vc];
			Vector3[] norms = new Vector3[vc];
			Vector2[] uvs = new Vector2[vc];
			for (int i = 0; i < vc; i++)
			{
				int o = i * 3;
				verts[i] = new Vector3((fm.positions[o] - cx) * s, (fm.positions[o + 1] - fm.minY) * s, (fm.positions[o + 2] - cz) * s);
				norms[i] = new Vector3(fm.normals[o], fm.normals[o + 1], fm.normals[o + 2]);
				uvs[i] = new Vector2(fm.uvs[i * 2], fm.uvs[i * 2 + 1]);
			}

			Mesh mesh = new Mesh();
			mesh.name = "KioskMP_Prop" + id;
			mesh.vertices = verts;
			mesh.normals = norms;
			mesh.uv = uvs;
			if (fm.submeshes.Count == 0)
			{
				mesh.subMeshCount = 1;
				int[] all = new int[vc];
				for (int i = 0; i < vc; i++)
				{
					all[i] = i;
				}
				mesh.SetTriangles(all, 0);
			}
			else
			{
				mesh.subMeshCount = fm.submeshes.Count;
				for (int i = 0; i < fm.submeshes.Count; i++)
				{
					FbxSubMesh sm = fm.submeshes[i];
					int[] tris = new int[sm.triCount * 3];
					for (int k = 0; k < tris.Length; k++)
					{
						tris[k] = sm.triStart * 3 + k;
					}
					mesh.SetTriangles(tris, i);
				}
			}
			mesh.RecalculateBounds();

			Texture2D body = LoadTex(bodyTex, "KioskMP." + bodyTex);
			Texture2D eye = eyeTex != null ? LoadTex(eyeTex, "KioskMP." + eyeTex) : null;
			int matCount = Math.Max(1, Math.Max(fm.materialColors.Count, fm.submeshes.Count));
			Material[] mats = new Material[matCount];
			for (int i = 0; i < matCount; i++)
			{
				Texture2D t = (eyeTex != null && i > 0) ? (eye != null ? eye : body) : body;
				mats[i] = MakeMat(t);
			}
			PropDef def = new PropDef();
			def.mesh = mesh;
			def.mats = mats;
			return def;
		}

		private static Texture2D LoadTex(string fileName, string resource)
		{
			string used;
			byte[] bytes = ReadAsset(fileName, resource, out used);
			if (bytes == null)
			{
				
				return null;
			}
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			if (!tex.LoadImage(bytes))
			{
				UnityEngine.Object.Destroy(tex);
				
				return null;
			}
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.anisoLevel = 4;
			
			return tex;
		}

		private static Material MakeMat(Texture2D tex)
		{
			string[] chain =
			{
				"Universal Render Pipeline/Lit",
				"Universal Render Pipeline/Simple Lit",
				"Standard",
				"Legacy Shaders/Diffuse",
				"Diffuse"
			};
			Shader sh = null;
			for (int i = 0; i < chain.Length; i++)
			{
				sh = Shader.Find(chain[i]);
				if (sh != null)
				{
					break;
				}
			}
			if (sh == null)
			{
				throw new InvalidOperationException("no usable shader found");
			}
			Material mat = new Material(sh);
			if (tex != null)
			{
				if (mat.HasProperty("_BaseMap"))
				{
					mat.SetTexture("_BaseMap", tex);
				}
				if (mat.HasProperty("_MainTex"))
				{
					mat.SetTexture("_MainTex", tex);
				}
				if (mat.HasProperty("_BaseColor"))
				{
					mat.SetColor("_BaseColor", Color.white);
				}
			}
			if (mat.HasProperty("_Smoothness"))
			{
				mat.SetFloat("_Smoothness", 0.05f);
			}
			if (mat.HasProperty("_Glossiness"))
			{
				mat.SetFloat("_Glossiness", 0.05f);
			}
			if (mat.HasProperty("_Cull"))
			{
				mat.SetFloat("_Cull", 0f);
			}
			return mat;
		}

		private static string SpawnPointInFront(out Vector3 pos, out float yaw)
		{
			pos = Vector3.zero;
			yaw = 0f;
			Camera cam = PickCamera();
			if (cam == null)
			{
				return "no camera found";
			}
			Transform t = cam.transform;
			Vector3 fwd = t.forward;
			fwd.y = 0f;
			if (fwd.sqrMagnitude < 1e-6f)
			{
				fwd = Vector3.forward;
			}
			fwd.Normalize();
			Vector3 p = t.position + fwd * SpawnDist;
			p.y = GroundY(p, t.position.y);
			pos = p;
			yaw = t.eulerAngles.y + 180f;
			return null;
		}

		private static Camera PickCamera()
		{
			PlayerController pc = PlayerController.instance;
			if (pc != null && pc.mainCamera != null && pc.mainCamera.isActiveAndEnabled)
			{
				return pc.mainCamera;
			}
			Camera cam = Camera.main;
			if (cam != null && cam.isActiveAndEnabled)
			{
				return cam;
			}
			Camera best = null;
			Camera[] cams = UnityEngine.Object.FindObjectsOfType<Camera>();
			for (int i = 0; i < cams.Length; i++)
			{
				Camera c = cams[i];
				if (c == null || !c.isActiveAndEnabled)
				{
					continue;
				}
				if (best == null || c.depth > best.depth)
				{
					best = c;
				}
			}
			return best;
		}

		private static float GroundY(Vector3 pos, float fallback)
		{
			RaycastHit hit;
			Vector3 from = pos + Vector3.up * 1.5f;
			if (Physics.Raycast(from, Vector3.down, out hit, 8f))
			{
				return hit.point.y;
			}
			return fallback - 1.4f;
		}

		private static byte[] ReadAsset(string fileName, string resourceName, out string used)
		{
			used = null;
			string dir = PluginDir();
			if (dir != null)
			{
				try
				{
					string p = Path.Combine(dir, fileName);
					if (File.Exists(p))
					{
						used = p;
						return File.ReadAllBytes(p);
					}
				}
				catch { }
			}
			try
			{
				Assembly asm = typeof(PropFX).Assembly;
				Stream st = asm.GetManifestResourceStream(resourceName);
				if (st != null)
				{
					try
					{
						using (MemoryStream ms = new MemoryStream())
						{
							st.CopyTo(ms);
							used = "embedded:" + resourceName;
							return ms.ToArray();
						}
					}
					finally { st.Close(); }
				}
			}
			catch { }
			return null;
		}

		private static string PluginDir()
		{
			try
			{
				string loc = typeof(PropFX).Assembly.Location;
				if (!string.IsNullOrEmpty(loc))
				{
					return Path.GetDirectoryName(loc);
				}
			}
			catch { }
			try
			{
				string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
					Path.Combine("BepInEx", Path.Combine("plugins", "KioskMP")));
				if (Directory.Exists(p))
				{
					return p;
				}
			}
			catch { }
			return null;
		}
	}
}
