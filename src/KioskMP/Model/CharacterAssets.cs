using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KioskMP
{
	public sealed class CharacterDef
	{
		public int index;
		public bool loaded;
		public string status = "not loaded";
		public FbxRig rig;
		public FbxMesh raw;
		public float scale = 1f;
		public float minY;
		public FbxAnim idle;
		public FbxAnim walk;
		public FbxAnim dance;

		public FbxAnim GetAnim(string state)
		{
			if (state == "dance")
			{
				return dance;
			}
			if (state == "walk")
			{
				return walk;
			}
			return idle;
		}

		public Quaternion Pose(int boneIndex, Vector3 eulerDeg)
		{
			FbxBone b = rig != null && boneIndex >= 0 && boneIndex < rig.bones.Count ? rig.bones[boneIndex] : null;
			Quaternion q = OrderQuat(b != null ? b.rotOrder : 0, eulerDeg);
			if (b != null && b.hasPre)
			{
				q = AxisQuat(b.preX, b.preY, b.preZ) * q;
			}
			if (b != null && b.hasPost)
			{
				q = q * AxisQuat(b.postX, b.postY, b.postZ);
			}
			return q;
		}

		private static Quaternion AxisQuat(double x, double y, double z)
		{
			return Quaternion.Euler(0f, 0f, (float)z)
				* Quaternion.Euler(0f, (float)y, 0f)
				* Quaternion.Euler((float)x, 0f, 0f);
		}

		private static Quaternion OrderQuat(int order, Vector3 e)
		{
			Quaternion qx = Quaternion.Euler(e.x, 0f, 0f);
			Quaternion qy = Quaternion.Euler(0f, e.y, 0f);
			Quaternion qz = Quaternion.Euler(0f, 0f, e.z);
			switch (order)
			{
				case 1: return qy * qz * qx;
				case 2: return qx * qz * qy;
				case 3: return qz * qx * qy;
				case 4: return qy * qx * qz;
				case 5: return qx * qy * qz;
				default: return qz * qy * qx;
			}
		}
	}

	public static class CharacterAssets
	{
		private static CharacterDef[] _defs;
		private static bool _tried;
		private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
		public static string Status = "not loaded";

		public static void EnsureLoaded()
		{
			if (_tried)
			{
				return;
			}
			_tried = true;
			_defs = new CharacterDef[4];
			int ok = 0;
			for (int i = 0; i < 4; i++)
			{
				try
				{
					CharacterDef d = LoadChar(i);
					_defs[i] = d;
					if (d != null && d.loaded)
					{
						ok++;
					}
				}
				catch (Exception e)
				{
					
				}
			}
			Status = ok + "/4 characters";
			
		}

		public static CharacterDef Get(int charId)
		{
			EnsureLoaded();
			if (charId < 0 || charId >= 4)
			{
				return null;
			}
			CharacterDef d = _defs[charId];
			return d != null && d.loaded ? d : null;
		}

		private static CharacterDef LoadChar(int i)
		{
			CharacterDef d = new CharacterDef();
			d.index = i;
			string used;
			byte[] meshFbx = ReadAsset("Peoples" + (i + 1) + ".fbx", "KioskMP.Peoples" + (i + 1) + ".fbx", out used);
			if (meshFbx == null)
			{
				d.status = "no fbx";
				return d;
			}
			string set = (i == 0 || i == 1) ? "AnimP12" : "AnimP34";
			string animUsed;
			byte[] idleB = ReadAsset(set + "_Idle.fbx", "KioskMP." + set + "_Idle.fbx", out animUsed);
			byte[] walkB = ReadAsset(set + "_Walk.fbx", "KioskMP." + set + "_Walk.fbx", out animUsed);
			byte[] danceB = ReadAsset(set + "_Dance.fbx", "KioskMP." + set + "_Dance.fbx", out animUsed);
			FbxRig rig = null;
			byte[][] sources = { idleB, walkB, danceB };
			for (int s = 0; s < sources.Length && rig == null; s++)
			{
				if (sources[s] == null)
				{
					continue;
				}
				FbxRig r = FbxRigReader.Read(sources[s]);
				if (r.bones.Count > 0)
				{
					rig = r;
				}
			}
			if (rig == null)
			{
				d.status = "no skeleton in assets";
				return d;
			}
			d.rig = rig;
			int start = rig.anims.Count;
			ReadAnimsInto(rig, idleB);
			d.idle = BestAnim(rig, start);
			DeltaHips(d.idle);
			start = rig.anims.Count;
			ReadAnimsInto(rig, walkB);
			d.walk = BestAnim(rig, start);
			DeltaHips(d.walk);
			start = rig.anims.Count;
			ReadAnimsInto(rig, danceB);
			d.dance = BestAnim(rig, start);
			DeltaHips(d.dance);
			d.raw = Fbx.LoadRaw(meshFbx);
			float h = d.raw.maxY - d.raw.minY;
			if (h < 1e-6f)
			{
				h = 1f;
			}
			d.scale = Game.AvatarHeight / h;
			d.minY = d.raw.minY;
			d.loaded = true;
			int n = (d.idle != null ? 1 : 0) + (d.walk != null ? 1 : 0) + (d.dance != null ? 1 : 0);
			d.status = "bones=" + rig.bones.Count + " anims=" + n + "/3";
			
			return d;
		}

		private static void ReadAnimsInto(FbxRig rig, byte[] data)
		{
			if (data == null)
			{
				return;
			}
			try
			{
				FbxRigReader.ReadAnims(data, rig);
			}
			catch (Exception e)
			{
				
			}
		}

		private static FbxAnim BestAnim(FbxRig rig, int startIdx)
		{
			FbxAnim best = null;
			for (int i = startIdx; i < rig.anims.Count; i++)
			{
				FbxAnim a = rig.anims[i];
				if (a != null && a.bones.Count > 0 && (best == null || a.duration > best.duration))
				{
					best = a;
				}
			}
			return best;
		}

		private static void DeltaHips(FbxAnim a)
		{
			if (a == null)
			{
				return;
			}
			for (int i = 0; i < a.bones.Count; i++)
			{
				FbxAnimBone ab = a.bones[i];
				if (ab == null || ab.trs != "Lcl Translation" || ab.boneName.IndexOf("Hips", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				float[] baseV = new float[3];
				for (int axis = 0; axis < 3 && axis < ab.channels.Count; axis++)
				{
					FbxChannel ch = ab.channels[axis];
					if (ch == null)
					{
						continue;
					}
					baseV[axis] = ch.curve != null ? ch.curve.Eval(0f) : ch.def;
				}
				for (int axis = 0; axis < 3 && axis < ab.channels.Count; axis++)
				{
					FbxChannel ch = ab.channels[axis];
					if (ch == null)
					{
						continue;
					}
					if (ch.curve != null)
					{
						for (int k = 0; k < ch.curve.v.Length; k++)
						{
							ch.curve.v[k] -= baseV[axis];
						}
					}
					else
					{
						ch.def -= baseV[axis];
					}
				}
			}
		}

		private static void AlignArmsToMesh(CharacterDef def, Transform[] bones, Transform space)
		{
			AlignArm(def, bones, space, "mixamorig:LeftArm", "mixamorig:LeftHand", 1f);
			AlignArm(def, bones, space, "mixamorig:RightArm", "mixamorig:RightHand", -1f);
		}

		private static void AlignArm(CharacterDef def, Transform[] bones, Transform space, string armName, string handName, float side)
		{
			int arm = -1;
			int hand = -1;
			for (int i = 0; i < bones.Length; i++)
			{
				string n = def.rig.bones[i].name;
				if (n == armName)
				{
					arm = i;
				}
				else if (n == handName)
				{
					hand = i;
				}
			}
			if (arm < 0 || hand < 0 || bones[arm] == null || bones[hand] == null)
			{
				return;
			}
			FbxMesh fm = def.raw;
			float extreme = side > 0f ? fm.maxX : -fm.minX;
			float handDepth = (fm.maxY - fm.minY) * 0.033f;
			float lim = extreme - handDepth;
			Vector3 sum = Vector3.zero;
			int c = 0;
			for (int i = 0; i < fm.triangleCount * 3; i++)
			{
				float x = fm.positions[i * 3];
				if (side > 0f ? x < lim : x > -lim)
				{
					continue;
				}
				sum += new Vector3(x, fm.positions[i * 3 + 1], fm.positions[i * 3 + 2]);
				c++;
			}
			if (c == 0)
			{
				return;
			}
			Vector3 meshHand = space.TransformPoint(sum / c);
			Vector3 armPos = bones[arm].position;
			Vector3 dirRig = bones[hand].position - armPos;
			Vector3 dirMesh = meshHand - armPos;
			if (dirRig.sqrMagnitude < 1e-6f || dirMesh.sqrMagnitude < 1e-6f)
			{
				return;
			}
			Quaternion delta = Quaternion.FromToRotation(dirRig.normalized, dirMesh.normalized);
			bones[arm].rotation = delta * bones[arm].rotation;
			
		}

		public static GameObject Build(CharacterDef def, int variant, out AvatarAnimator anim)
		{
			anim = null;
			if (def == null || !def.loaded || def.rig == null || def.raw == null)
			{
				return null;
			}
			FbxRig rig = def.rig;
			int n = rig.bones.Count;
			if (n == 0)
			{
				return null;
			}

			GameObject container = new GameObject("KSMP_Char" + (def.index + 1));
			GameObject offset = new GameObject("offset");
			offset.transform.SetParent(container.transform, false);
			offset.transform.localPosition = new Vector3(0f, -def.minY, 0f);
			GameObject fit = new GameObject("fit");
			fit.transform.SetParent(offset.transform, false);

			Transform[] bones = new Transform[n];
			for (int i = 0; i < n; i++)
			{
				FbxBone b = rig.bones[i];
				GameObject bg = new GameObject(b.name);
				bones[i] = bg.transform;
			}
			for (int i = 0; i < n; i++)
			{
				FbxBone b = rig.bones[i];
				Transform parent = b.parent >= 0 && b.parent < n ? bones[b.parent] : fit.transform;
				bones[i].SetParent(parent, false);
			}
			for (int i = 0; i < n; i++)
			{
				FbxBone b = rig.bones[i];
				bones[i].localPosition = new Vector3((float)b.tx, (float)b.ty, (float)b.tz);
				bones[i].localRotation = def.Pose(i, new Vector3((float)b.rx, (float)b.ry, (float)b.rz));
				bones[i].localScale = new Vector3((float)b.sx, (float)b.sy, (float)b.sz);
			}
			anim = new AvatarAnimator(def, bones);

			Vector3 sMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 sMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			for (int i = 0; i < n; i++)
			{
				if (bones[i] == null)
				{
					continue;
				}
				Vector3 p = offset.transform.InverseTransformPoint(bones[i].position);
				sMin = Vector3.Min(sMin, p);
				sMax = Vector3.Max(sMax, p);
			}
			float h = def.raw.maxY - def.raw.minY;
			float skelH = sMax.y - sMin.y;
			float k = skelH > 1e-3f ? h / skelH : 1f;
			Vector3 meshC = new Vector3((def.raw.minX + def.raw.maxX) * 0.5f, def.raw.minY, (def.raw.minZ + def.raw.maxZ) * 0.5f);
			Vector3 skelC = new Vector3((sMin.x + sMax.x) * 0.5f, sMin.y, (sMin.z + sMax.z) * 0.5f);
			fit.transform.localScale = new Vector3(k, k, k);
			fit.transform.localPosition = new Vector3(meshC.x - skelC.x * k, meshC.y - skelC.y * k, meshC.z - skelC.z * k);
			

			AlignArmsToMesh(def, bones, offset.transform);
			anim.CaptureRest();

			Mesh mesh = BuildMesh(def, bones, offset.transform);
			GameObject bodyGo = new GameObject("body");
			bodyGo.transform.SetParent(offset.transform, false);
			SkinnedMeshRenderer smr = bodyGo.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = bones;
			smr.rootBone = bones[BindBoneIndex(def)];
			smr.updateWhenOffscreen = true;
			if (mesh.subMeshCount > 1)
			{
				Material baseMat = GetMaterial(def.index, variant);
				Material[] mats = new Material[mesh.subMeshCount];
				for (int i = 0; i < mats.Length; i++)
				{
					mats[i] = baseMat;
				}
				smr.sharedMaterials = mats;
			}
			else
			{
				smr.sharedMaterial = GetMaterial(def.index, variant);
			}
			smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
			smr.receiveShadows = true;

			container.transform.localScale = new Vector3(def.scale, def.scale, def.scale);
			Matrix4x4 meshW = bodyGo.transform.localToWorldMatrix;
			Matrix4x4[] bps = new Matrix4x4[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				Matrix4x4 w = bones[i] != null ? bones[i].localToWorldMatrix : Matrix4x4.identity;
				bps[i] = w.inverse * meshW;
			}
			mesh.bindposes = bps;
			int nanBp = 0;
			for (int i = 0; i < bps.Length; i++)
			{
				if (float.IsNaN(bps[i].m00) || float.IsNaN(bps[i].m11) || float.IsNaN(bps[i].m22)
					|| float.IsNaN(bps[i].m03) || float.IsNaN(bps[i].m13) || float.IsNaN(bps[i].m23))
				{
					nanBp++;
				}
			}
			if (nanBp > 0)
			{
				
			}
			anim.Play("idle");
			return container;
		}

		private static Mesh BuildMesh(CharacterDef def, Transform[] bones, Transform spaceTf)
		{
			FbxMesh fm = def.raw;
			int vc = fm.triangleCount * 3;
			Vector3[] verts = new Vector3[vc];
			Vector3[] norms = new Vector3[vc];
			Vector2[] uvs = new Vector2[vc];
			for (int i = 0; i < vc; i++)
			{
				int o = i * 3;
				verts[i] = new Vector3(fm.positions[o], fm.positions[o + 1], fm.positions[o + 2]);
				norms[i] = new Vector3(fm.normals[o], fm.normals[o + 1], fm.normals[o + 2]);
				uvs[i] = new Vector2(fm.uvs[i * 2], fm.uvs[i * 2 + 1]);
			}
			BoneWeight[] weights = ComputeWeights(def, bones, verts, spaceTf);
			Mesh mesh = new Mesh();
			mesh.name = "KSMP_CharMesh" + (def.index + 1);
			mesh.vertices = verts;
			mesh.normals = norms;
			mesh.uv = uvs;
			mesh.boneWeights = weights;
			int subCount = Math.Max(1, fm.submeshes.Count);
			mesh.subMeshCount = subCount;
			if (fm.submeshes.Count == 0)
			{
				int[] all = new int[vc];
				for (int i = 0; i < vc; i++)
				{
					all[i] = i;
				}
				mesh.SetTriangles(all, 0);
			}
			else
			{
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
			return mesh;
		}

		private static int BindBoneIndex(CharacterDef def)
		{
			FbxRig rig = def.rig;
			int root = 0;
			for (int i = 0; i < rig.bones.Count; i++)
			{
				if (rig.bones[i].parent < 0)
				{
					root = i;
					break;
				}
			}
			for (int i = 0; i < rig.bones.Count; i++)
			{
				if (rig.bones[i] != null && rig.bones[i].name.IndexOf("Hips", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return i;
				}
			}
			return root;
		}

		private static BoneWeight[] ComputeWeights(CharacterDef def, Transform[] bones, Vector3[] verts, Transform spaceTf)
		{
			int vc = verts.Length;
			BoneWeight[] bw = new BoneWeight[vc];
			int bind = BindBoneIndex(def);
			FbxRig rig = def.rig;
			if (rig == null || rig.skins.Count == 0)
			{
				ProximityWeights(def, bones, verts, spaceTf, bind, bw);
				return bw;
			}
			Dictionary<long, List<int>> pwBone = new Dictionary<long, List<int>>();
			Dictionary<long, List<float>> pwW = new Dictionary<long, List<float>>();
			for (int si = 0; si < rig.skins.Count; si++)
			{
				FbxGeomSkin skin = rig.skins[si];
				int offset = OffsetOf(def.raw, skin.geomId);
				for (int ci = 0; ci < skin.clusters.Count; ci++)
				{
					FbxCluster cl = skin.clusters[ci];
					if (cl.bone < 0 || cl.bone >= bones.Length || cl.indices == null)
					{
						continue;
					}
					for (int k = 0; k < cl.indices.Length && k < cl.weights.Length; k++)
					{
						long pt = offset + cl.indices[k];
						float w = cl.weights[k];
						if (w <= 0f)
						{
							continue;
						}
						List<int> bl;
						List<float> wl;
						if (!pwBone.TryGetValue(pt, out bl) || !pwW.TryGetValue(pt, out wl))
						{
							bl = new List<int>(4);
							wl = new List<float>(4);
							pwBone[pt] = bl;
							pwW[pt] = wl;
						}
						bl.Add(cl.bone);
						wl.Add(w);
					}
				}
			}
			for (int v = 0; v < vc; v++)
			{
				long pt = v < def.raw.cornerPoint.Count ? def.raw.cornerPoint[v] : -1;
				List<int> bl;
				List<float> wl;
				BoneWeight b;
				if (pt >= 0 && pwBone.TryGetValue(pt, out bl) && pwW.TryGetValue(pt, out wl) && bl.Count > 0)
				{
					int c = Math.Min(4, bl.Count);
					float sum = 0f;
					for (int k = 0; k < c; k++)
					{
						sum += wl[k];
					}
					if (sum <= 1e-6f)
					{
						b = new BoneWeight();
						b.boneIndex0 = bind;
						b.weight0 = 1f;
						bw[v] = b;
						continue;
					}
					b = new BoneWeight();
					for (int k = 0; k < c; k++)
					{
						float w = wl[k] / sum;
						switch (k)
						{
							case 0: b.boneIndex0 = bl[k]; b.weight0 = w; break;
							case 1: b.boneIndex1 = bl[k]; b.weight1 = w; break;
							case 2: b.boneIndex2 = bl[k]; b.weight2 = w; break;
							case 3: b.boneIndex3 = bl[k]; b.weight3 = w; break;
						}
					}
					bw[v] = b;
				}
				else
				{
					b = new BoneWeight();
					b.boneIndex0 = bind;
					b.weight0 = 1f;
					bw[v] = b;
				}
			}
			return bw;
		}

		private static void ProximityWeights(CharacterDef def, Transform[] bones, Vector3[] verts, Transform spaceTf, int bind, BoneWeight[] bw)
		{
			FbxRig rig = def.rig;
			int n = bones.Length;
			Vector3[] pos = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				pos[i] = bones[i] != null ? bones[i].position : Vector3.zero;
			}
			Vector3[] segEnd = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				Vector3 acc = Vector3.zero;
				int c = 0;
				for (int j = 0; j < n; j++)
				{
					if (rig.bones[j].parent == i)
					{
						acc += pos[j];
						c++;
					}
				}
				segEnd[i] = c > 0 ? acc / c : pos[i];
			}
			bool[] isEnd = new bool[n];
			for (int i = 0; i < n; i++)
			{
				isEnd[i] = rig.bones[i] != null && rig.bones[i].name.EndsWith("_End", StringComparison.Ordinal);
			}
			float eps = 0.04f;
			for (int v = 0; v < verts.Length; v++)
			{
				Vector3 p = spaceTf != null ? spaceTf.TransformPoint(verts[v]) : verts[v];
				int[] bi = { -1, -1, -1, -1 };
				float[] ws = { 0f, 0f, 0f, 0f };
				for (int i = 0; i < n; i++)
				{
					if (isEnd[i])
					{
						continue;
					}
					float d = DistPointSegment(p, pos[i], segEnd[i]);
					float w = 1f / (d * d + eps * eps);
					for (int s = 0; s < 4; s++)
					{
						if (bi[s] < 0 || w > ws[s])
						{
							for (int t2 = 3; t2 > s; t2--)
							{
								bi[t2] = bi[t2 - 1];
								ws[t2] = ws[t2 - 1];
							}
							bi[s] = i;
							ws[s] = w;
							break;
						}
					}
				}
				float sum = ws[0] + ws[1] + ws[2] + ws[3];
				BoneWeight b = new BoneWeight();
				if (sum <= 1e-9f)
				{
					b.boneIndex0 = bind;
					b.weight0 = 1f;
				}
				else
				{
					b.boneIndex0 = bi[0];
					b.weight0 = ws[0] / sum;
					if (bi[1] >= 0)
					{
						b.boneIndex1 = bi[1];
						b.weight1 = ws[1] / sum;
					}
					if (bi[2] >= 0)
					{
						b.boneIndex2 = bi[2];
						b.weight2 = ws[2] / sum;
					}
					if (bi[3] >= 0)
					{
						b.boneIndex3 = bi[3];
						b.weight3 = ws[3] / sum;
					}
				}
				bw[v] = b;
			}
		}

		private static float DistPointSegment(Vector3 p, Vector3 a, Vector3 b)
		{
			Vector3 ab = b - a;
			float len2 = ab.sqrMagnitude;
			if (len2 <= 1e-10f)
			{
				return Vector3.Distance(p, a);
			}
			float t = Vector3.Dot(p - a, ab) / len2;
			if (t < 0f)
			{
				t = 0f;
			}
			if (t > 1f)
			{
				t = 1f;
			}
			return Vector3.Distance(p, a + ab * t);
		}

		private static int OffsetOf(FbxMesh fm, long geomId)
		{
			for (int i = 0; i < fm.geomIds.Count; i++)
			{
				if (fm.geomIds[i] == geomId)
				{
					int off = 0;
					for (int k = 0; k < i; k++)
					{
						off += fm.geomPointCounts[k];
					}
					return off;
				}
			}
			return 0;
		}

		public static Material GetMaterial(int charId, int variant)
		{
			EnsureLoaded();
			string key = charId + ":" + variant;
			Material m;
			if (_mats.TryGetValue(key, out m) && m != null)
			{
				return m;
			}
			Texture2D tex = LoadTexture(charId, variant);
			m = MakeMaterial(tex);
			_mats[key] = m;
			return m;
		}

		private static Texture2D LoadTexture(int charId, int variant)
		{
			int texChar = charId;
			string used;
			byte[] bytes = ReadAsset("Peoples" + (texChar + 1) + ".jpg", "KioskMP.Peoples" + (texChar + 1) + ".jpg", out used);
			if (bytes == null)
			{
				
				return null;
			}
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
			if (!tex.LoadImage(bytes))
			{
				UnityEngine.Object.Destroy(tex);
				
				return null;
			}
			
			tex.wrapMode = TextureWrapMode.Repeat;
			tex.anisoLevel = 4;
			return tex;
		}

		private static Material MakeMaterial(Texture2D tex)
		{
			string[] chain =
			{
				"Universal Render Pipeline/Lit",
				"Universal Render Pipeline/Simple Lit",
				"Standard",
				"Sprites/Default",
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
				if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
				if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
				if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
				if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
			}
			if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
			if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
			
			return mat;
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
				Assembly asm = typeof(CharacterAssets).Assembly;
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
				string loc = typeof(CharacterAssets).Assembly.Location;
				if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
			}
			catch { }
			try
			{
				string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
					Path.Combine("BepInEx", Path.Combine("plugins", "KioskMP")));
				if (Directory.Exists(p)) return p;
			}
			catch { }
			return null;
		}
	}
}
