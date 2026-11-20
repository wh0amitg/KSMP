using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace KioskMP
{

	public static partial class Game
	{
		public const float AvatarHeight = 1.6f;

		private sealed class Avatar
		{
			public GameObject root;
			public Transform body;
			public bool capsuleFallback;
			public CapsuleCollider capsule;
			public Transform tag;
			public TextMesh[] layers;
			public Vector3[] layerOffset;
			public float tagNorm = -1f;
			public Vector3 pos;
			public float yaw;
			public float pitch;
			public bool crouch;
			public float lastRecv;
			public Vector3 vel;
			public int charId = -1;
			public int variant = -1;
			public GameObject charRoot;
			public AvatarAnimator anim;
			public bool dancing;
			public float activeSince = -1f;
			public bool diagDone;
		}

		private static readonly Dictionary<int, Avatar> _avatars = new Dictionary<int, Avatar>();
		private static float _lastPlayerSend;
		private static Font _tagFont;
		private static bool _tagFontTried;
		private static bool _tagFontLogged;

		public static void OnPeerJoined(int peerId, string name)
		{
			EnsureAvatar(peerId, name);
		}

		public static void RebuildPlayers()
		{
			foreach (KeyValuePair<int, Avatar> kv in _avatars)
				DestroyAvatar(kv.Value);
			_avatars.Clear();
			foreach (KeyValuePair<int, PeerInfo> kv in Session.Peers)
				EnsureAvatar(kv.Key, kv.Value.name);
		}

		public static void ClearAvatars()
		{
			foreach (KeyValuePair<int, Avatar> kv in _avatars)
				DestroyAvatar(kv.Value);
			_avatars.Clear();
		}

		public static void DropAvatar(int peerId)
		{
			Avatar a;
			if (_avatars.TryGetValue(peerId, out a))
			{
				DestroyAvatar(a);
				_avatars.Remove(peerId);
			}
		}

		public static void ResetPlayers()
		{
			ClearAvatars();
			ResetCustomize();
		}

		public static void RebuildAvatar(int peerId)
		{
			if (peerId == Session.SelfId)
			{
				return;
			}
			string name = PeerName(peerId);
			DropAvatar(peerId);
			EnsureAvatar(peerId, name);
		}

		public static void ToggleDance(int peerId)
		{
			Avatar a;
			if (!_avatars.TryGetValue(peerId, out a) || a == null)
			{
				return;
			}
			a.dancing = !a.dancing;
		}

		private static void TryDanceKey()
		{
			if (MenuUI.IsOpen || ChatOverlay.IsOpen)
			{
				return;
			}
			bool pressed = false;
			try { pressed = Input.GetKeyDown(KeyCode.P); } catch { }
			if (!pressed)
			{
				try
				{
					Keyboard kb = Keyboard.current;
					if (kb != null && kb.pKey.wasPressedThisFrame)
					{
						pressed = true;
					}
				}
				catch { }
			}
			if (!pressed)
			{
				return;
			}
			SendMyDance();
		}

		private static void DestroyAvatar(Avatar a)
		{
			if (a != null && a.root != null)
			{
				try { UnityEngine.Object.Destroy(a.root); } catch { }
			}
		}

		private static Font TagFont()
		{
			if (_tagFontTried) return _tagFont;
			_tagFontTried = true;
			try
			{
				LanguageController lc = LanguageController.instance;
				if (lc != null)
				{
					_tagFont = lc.GetCurrentFontUnityMarker();
					if (_tagFont == null) _tagFont = lc.GetCurrentFontUnity(true);
				}
			}
			catch (Exception e)
			{
				
			}
			if (_tagFont == null)
			{
				try { _tagFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
			}
			if (_tagFont == null && !_tagFontLogged)
			{
				_tagFontLogged = true;
				
			}
			return _tagFont;
		}

		private static string CleanName(string n, int id)
		{
			if (string.IsNullOrEmpty(n)) return "P" + id;
			n = n.Replace('\r', ' ').Replace('\n', ' ').Trim();
			if (n.Length > 24) n = n.Substring(0, 24);
			if (n.Length == 0) n = "P" + id;
			return n;
		}

		private static Avatar EnsureAvatar(int peerId, string name)
		{

			if (peerId < 0 || peerId == Session.SelfId) return null;

			Avatar a;
			if (_avatars.TryGetValue(peerId, out a) && a != null && a.root != null)
				return a;

			a = new Avatar();
			a.root = new GameObject("MP_Avatar_" + peerId);

			a.root.SetActive(false);

			Rigidbody arb = a.root.AddComponent<Rigidbody>();
			arb.isKinematic = true;
			arb.useGravity = false;
			arb.interpolation = RigidbodyInterpolation.Interpolate;
			a.capsule = a.root.AddComponent<CapsuleCollider>();
			a.capsule.radius = 0.3f;
			a.capsule.height = AvatarHeight;
			a.capsule.center = Vector3.zero;

			int charId = CharIdOf(peerId);
			int variant = VariantOf(peerId);
			a.charId = charId;
			a.variant = variant;

			AvatarModel.EnsureLoaded();
			CharacterDef def = CharacterAssets.Get(charId);
			if (def != null)
			{
				try
				{
					AvatarAnimator anim;
					GameObject charGo = CharacterAssets.Build(def, variant, out anim);
					if (charGo != null)
					{
						charGo.transform.SetParent(a.root.transform, false);
						charGo.transform.localPosition = new Vector3(0f, -AvatarHeight * 0.5f, 0f);
						charGo.transform.localRotation = Quaternion.identity;
						a.charRoot = charGo;
						a.anim = anim;
					}
				}
				catch (Exception e)
				{
					
					a.charRoot = null;
					a.anim = null;
				}
			}

			string bodyKind;
			if (a.charRoot != null)
			{
				bodyKind = "char" + (charId + 1) + (variant == 1 ? "/v2" : "");
			}
			else if (AvatarModel.Ready)
			{
				GameObject modelGo = new GameObject("model");
				modelGo.transform.SetParent(a.root.transform, false);
				MeshFilter mf = modelGo.AddComponent<MeshFilter>();
				MeshRenderer mr = modelGo.AddComponent<MeshRenderer>();
				mf.sharedMesh = AvatarModel.Mesh;
				mr.sharedMaterial = AvatarModel.Material;
				mr.shadowCastingMode = ShadowCastingMode.On;
				mr.receiveShadows = true;
				a.body = modelGo.transform;
				bodyKind = "fbx";
			}
			else
			{
				GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
				Collider col = cap.GetComponent<Collider>();
				if (col != null) UnityEngine.Object.Destroy(col);
				cap.transform.SetParent(a.root.transform, false);
				cap.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
				Renderer rend = cap.GetComponent<Renderer>();
				if (rend != null)
				{
					Shader sh = Shader.Find("Universal Render Pipeline/Lit");
					if (sh == null) sh = Shader.Find("Standard");
					if (sh == null) sh = Shader.Find("Sprites/Default");
					if (sh != null)
					{
						Material mat = new Material(sh);
						Color[] palette =
						{
							new Color(0.2f, 0.6f, 1.0f),
							new Color(1.0f, 0.4f, 0.2f),
							new Color(0.3f, 0.9f, 0.3f),
							new Color(0.9f, 0.2f, 0.9f),
							new Color(1.0f, 0.9f, 0.1f)
						};
						Color c = palette[Mathf.Abs(peerId) % palette.Length];
						if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
						if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
						rend.material = mat;
					}
				}
				a.body = cap.transform;
				a.capsuleFallback = true;
				bodyKind = "capsule(" + AvatarModel.Status + ")";
			}

			if (a.body != null) a.body.localRotation = Quaternion.Euler(0f, 180f, 0f);

			_avatars[peerId] = a;

			Font font = TagFont();
			string txt = CleanName(name, peerId);
			if (font != null)
			{
				GameObject tagGo = null;
				try
				{
					tagGo = new GameObject("tag");
					tagGo.transform.SetParent(a.root.transform, false);

					tagGo.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

					TextMesh[] layers = new TextMesh[3];
					Vector3[] offsets = new Vector3[3];
					offsets[0] = new Vector3(0f, 0f, 0f);
					offsets[1] = new Vector3(0.005f, 0.005f, 0.002f);
					offsets[2] = new Vector3(0.014f, 0.014f, 0.006f);
					Color mainCol = new Color(1f, 0.95f, 0.3f, 1f);
					Color[] cols = { mainCol, mainCol, new Color(0f, 0f, 0f, 1f) };
					bool ok = true;
					for (int i = 0; i < 3 && ok; i++)
					{
						layers[i] = MakeTagLayer(tagGo.transform, txt, font, cols[i]);
						ok = layers[i] != null;
					}
					if (ok)
					{
						a.tag = tagGo.transform;
						a.layers = layers;
						a.layerOffset = offsets;
					}
					else
					{
						UnityEngine.Object.Destroy(tagGo);
						tagGo = null;
						
					}
				}
				catch (Exception e)
				{
					if (tagGo != null)
					{
						try { UnityEngine.Object.Destroy(tagGo); } catch { }
					}
					a.tag = null;
					a.layers = null;
					a.layerOffset = null;
					
				}
			}

			
			
			return a;
		}

		private static TextMesh MakeTagLayer(Transform parent, string text, Font font, Color col)
		{
			GameObject go = new GameObject("layer");
			go.transform.SetParent(parent, false);

			TextMesh tm = go.AddComponent<TextMesh>();
			if (tm == null)
			{
				UnityEngine.Object.Destroy(go);
				return null;
			}
			tm.text = text;
			tm.font = font;
			tm.fontSize = 48;
			tm.characterSize = 0.05f;
			tm.anchor = TextAnchor.LowerCenter;
			tm.alignment = TextAlignment.Center;
			tm.richText = false;
			tm.color = col;
			MeshRenderer mr = go.GetComponent<MeshRenderer>();
			if (mr == null) mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = font.material;
			mr.shadowCastingMode = ShadowCastingMode.Off;
			mr.receiveShadows = false;
			return tm;
		}

		private static void RunAvatarDiag(Avatar a, int peerId)
		{
			a.diagDone = true;
			try
			{
				if (a.root == null || a.charRoot == null)
				{
					return;
				}
				
				SkinnedMeshRenderer smr = a.charRoot.GetComponentInChildren<SkinnedMeshRenderer>();
				if (smr == null)
				{
					
					return;
				}
				Mesh m = smr.sharedMesh;
				Mesh baked = new Mesh();
				smr.BakeMesh(baked);
				Vector3 bc = baked.bounds.center;
				Vector3 bs = baked.bounds.size;
				Vector3 sc = smr.bounds.center;
				Vector3 ss = smr.bounds.size;
				
				if (smr.bones != null && smr.bones.Length > 0)
				{
					string[] want = { "mixamorig:Hips", "mixamorig:Head", "mixamorig:LeftHand", "mixamorig:RightHand", "mixamorig:LeftFoot", "mixamorig:RightFoot" };
					string bl = "";
					for (int wi = 0; wi < want.Length; wi++)
					{
						for (int bi = 0; bi < smr.bones.Length; bi++)
						{
							if (smr.bones[bi] != null && smr.bones[bi].name == want[wi])
							{
								Vector3 rel = smr.bones[bi].position - a.root.transform.position;
								bl += " " + want[wi].Substring(10) + "=(" + rel.x.ToString("F2") + "," + rel.y.ToString("F2") + "," + rel.z.ToString("F2") + ")";
								break;
							}
						}
					}
					
				}
				UnityEngine.Object.Destroy(baked);
			}
			catch (Exception e)
			{
				
			}
		}

		private static void SendPlayerState()
		{
			PlayerController pc = PlayerController.instance;
			if (pc == null || pc.mainCamera == null) return;

			if (Session.SelfId < 0) return;

			Vector3 p = pc.transform.position;
			Vector3 e = pc.mainCamera.transform.eulerAngles;
			byte flags = 0;

			CharacterController cc = pc.GetComponent<CharacterController>();
			if (cc != null && cc.height < 1.5f) flags |= 1;

			byte[] m = MsgBuild.PlayerState(Session.SelfId, flags,
				new NetVec3(p.x, p.y, p.z), e.y, e.x);

			if (Session.IsHost)
				Session.Broadcast(m);
			else
				Session.SendToHost(m);
		}

		public static void ApplyPlayerState(PacketReader r)
		{
			int id = r.I32();
			byte flags = r.U8();
			NetVec3 pos = r.V3();
			float yaw = r.F32();
			float pitch = r.F32();

			if (id < 0) return;

			if (Session.IsHost)
			{

				Session.Broadcast(MsgBuild.PlayerState(id, flags, pos, yaw, pitch), id);
			}

			if (id == Session.SelfId) return;

			string name = PeerName(id);
			Avatar a = EnsureAvatar(id, name);
			if (a == null) return;

			Vector3 np = new Vector3(pos.x, pos.y, pos.z);
			if (a.lastRecv > 0f)
			{
				float dt = Time.realtimeSinceStartup - a.lastRecv;
				if (dt > 0.0001f && dt < 1f)
					a.vel = (np - a.pos) / dt;
			}
			a.lastRecv = Time.realtimeSinceStartup;
			a.pos = np;
			a.yaw = yaw;
			a.pitch = pitch;
			a.crouch = (flags & 1) != 0;
			if (!a.root.activeSelf) a.root.SetActive(true);
		}

		private static void SetVerticalScale(Transform t, float factor)
		{
			if (t == null)
			{
				return;
			}
			Vector3 up = t.InverseTransformDirection(Vector3.up);
			float ax = Mathf.Abs(up.x);
			float ay = Mathf.Abs(up.y);
			float az = Mathf.Abs(up.z);
			Vector3 s = Vector3.one;
			if (ay >= ax && ay >= az)
			{
				s.y = factor;
			}
			else if (az >= ax)
			{
				s.z = factor;
			}
			else
			{
				s.x = factor;
			}
			t.localScale = s;
		}

		public static void UpdatePlayers()
		{
			if (!Session.Active || !SceneReady) return;

			TryDanceKey();

			float now = Time.realtimeSinceStartup;
			if (now - _lastPlayerSend > 0.05f)
			{
				_lastPlayerSend = now;
				SendPlayerState();
			}

			float crouchH = 0.8f;
			PlayerController lpc = PlayerController.instance;
			if (lpc != null)
			{
				try { crouchH = lpc.crouchValue; } catch { }
			}

			Camera cam = Camera.main;

			List<int> needRebuild = null;

			foreach (KeyValuePair<int, Avatar> kv in _avatars)
			{
				Avatar a = kv.Value;
				if (a == null || a.root == null) continue;

				int curChar = CharIdOf(kv.Key);
				int curVar = VariantOf(kv.Key);
				if (a.charId != curChar || a.variant != curVar)
				{
					if (needRebuild == null) needRebuild = new List<int>();
					needRebuild.Add(kv.Key);
					continue;
				}

				if (a.lastRecv <= 0f) continue;

				if (now - a.lastRecv > 5f)
				{
					if (a.root.activeSelf) a.root.SetActive(false);
					continue;
				}
				if (!a.root.activeSelf) a.root.SetActive(true);

				float h = a.crouch ? crouchH : AvatarHeight;
				float cy = a.crouch ? (crouchH - AvatarHeight) * 0.5f : 0f;
				float heightFactor = Mathf.Clamp(h / AvatarHeight, 0.35f, 1f);

				if (a.capsule != null)
				{
					if (Mathf.Abs(a.capsule.height - h) > 0.01f) a.capsule.height = h;
					a.capsule.center = new Vector3(0f, cy, 0f);
				}

				Transform rt = a.root.transform;
				Vector3 target = a.pos;
				if ((rt.position - target).sqrMagnitude > 25f)
					rt.position = target;
				else
					rt.position = Vector3.Lerp(rt.position, target,
						1f - Mathf.Exp(-20f * Time.deltaTime));
				rt.rotation = Quaternion.Slerp(rt.rotation,
					Quaternion.Euler(0f, a.yaw, 0f),
					1f - Mathf.Exp(-20f * Time.deltaTime));

				if (a.charRoot != null)
				{
					SetVerticalScale(a.charRoot.transform, heightFactor);
					if (a.anim != null)
					{
						string st = a.dancing ? "dance" : (a.vel.magnitude > 0.3f ? "walk" : "idle");
						if (a.anim.State != st) a.anim.Play(st);
						a.anim.Tick(Time.deltaTime);
					}
				}
				else if (a.body != null && !a.capsuleFallback)
				{
					a.body.localPosition = new Vector3(0f, -AvatarHeight * 0.5f, 0f);
					SetVerticalScale(a.body, heightFactor);
				}

				if (!a.diagDone && a.charRoot != null)
				{
					if (a.activeSince < 0f)
					{
						a.activeSince = now;
					}
					else if (now - a.activeSince > 1f)
					{
						RunAvatarDiag(a, kv.Key);
					}
				}

				if (a.tag != null && a.layers != null && cam != null)
				{
					a.tag.localPosition = a.charRoot != null
						? new Vector3(0f, AvatarHeight * 0.625f, 0f)
						: new Vector3(0f, cy + h * 0.5f + 0.1f, 0f);

					if (a.tagNorm <= 0f)
					{
						a.tag.rotation = Quaternion.identity;
						a.tag.localScale = new Vector3(0.1f, 0.1f, 0.1f);
						float textH = 0f;
						MeshFilter mf = a.layers[0].GetComponent<MeshFilter>();
						Mesh mesh = mf != null ? mf.sharedMesh : null;
						if (mesh == null && mf != null) mesh = mf.mesh;
						if (mesh != null) textH = mesh.bounds.size.y;
						if (textH <= 1e-4f)
						{
							MeshRenderer mr = a.layers[0].GetComponent<MeshRenderer>();
							if (mr != null) textH = mr.bounds.size.y * 10f;
						}
						if (textH > 1e-4f)
						{
							float tn = 0.30f / textH;
							if (tn < 0.02f) tn = 0.02f;
							if (tn > 2f) tn = 2f;
							a.tagNorm = tn;
							for (int i = 0; i < a.layers.Length; i++)
								a.layers[i].transform.localPosition = a.layerOffset[i] / a.tagNorm;
						}
					}

					if (a.tagNorm > 0f)
					{
						Vector3 tp = a.tag.position;
						a.tag.rotation = Quaternion.LookRotation(tp - cam.transform.position);

						float dist = Vector3.Distance(cam.transform.position, tp);
						float distScale = Mathf.Clamp(dist * 0.5f, 0.4f, 8f);
						float scale = a.tagNorm * distScale;
						a.tag.localScale = new Vector3(scale, scale, scale);
					}
				}
			}

			if (needRebuild != null)
			{
				for (int i = 0; i < needRebuild.Count; i++)
				{
					RebuildAvatar(needRebuild[i]);
				}
			}
		}
	}
}
