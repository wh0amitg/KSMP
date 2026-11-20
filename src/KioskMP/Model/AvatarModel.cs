using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KioskMP
{

	public static class AvatarModel
	{
		public static Mesh Mesh;
		public static Material Material;
		public static bool Ready;
		public static string Status = "not loaded";

		private static bool _tried;

		public static void EnsureLoaded()
		{
			if (_tried) return;
			_tried = true;
			try
			{
				Load();
				Ready = Mesh != null && Material != null;
			}
			catch (Exception e)
			{
				Status = "load failed: " + e.Message;
				
			}
		}

		private static void Load()
		{
			string fbxPath;
			byte[] fbxBytes = ReadAsset("avatar.fbx", "Bob.fbx", "KioskMP.Bob.fbx", out fbxPath);
			if (fbxBytes == null)
				throw new FileNotFoundException("no avatar.fbx/Bob.fbx found (external or embedded)");

			FbxMesh fm = Fbx.Load(fbxBytes);
			

			float h = fm.maxY - fm.minY;
			if (h < 1e-6f) h = 1f;
			float s = 2f / h;
			float cx = (fm.minX + fm.maxX) * 0.5f;
			float cz = (fm.minZ + fm.maxZ) * 0.5f;

			int vc = fm.triangleCount * 3;
			Vector3[] verts = new Vector3[vc];
			Vector3[] norms = new Vector3[vc];
			Vector2[] uvs = new Vector2[vc];
			for (int i = 0; i < vc; i++)
			{
				int o = i * 3;
				verts[i] = new Vector3(
					(fm.positions[o] - cx) * s,
					(fm.positions[o + 1] - fm.minY) * s - 1f,
					(fm.positions[o + 2] - cz) * s);
				norms[i] = new Vector3(fm.normals[o], fm.normals[o + 1], fm.normals[o + 2]);
				uvs[i] = new Vector2(fm.uvs[i * 2], fm.uvs[i * 2 + 1]);
			}

			Mesh mesh = new Mesh();
			mesh.name = "KioskMP_Avatar";
			mesh.vertices = verts;
			mesh.normals = norms;
			mesh.uv = uvs;
			mesh.subMeshCount = Math.Max(1, fm.submeshes.Count);
			if (fm.submeshes.Count == 0)
			{
				int[] all = new int[vc];
				for (int i = 0; i < vc; i++) all[i] = i;
				mesh.SetTriangles(all, 0);
			}
			else
			{
				for (int i = 0; i < fm.submeshes.Count; i++)
				{
					FbxSubMesh sm = fm.submeshes[i];
					int[] tris = new int[sm.triCount * 3];
					for (int k = 0; k < tris.Length; k++)
						tris[k] = sm.triStart * 3 + k;
					mesh.SetTriangles(tris, i);
				}
			}
			mesh.RecalculateBounds();
			Mesh = mesh;

			string texPath;
			byte[] texBytes = ReadAsset("avatar.png", "Bob.png", "KioskMP.Bob.png", out texPath);
			Texture2D tex = null;
			if (texBytes != null)
			{
				tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
				if (!tex.LoadImage(texBytes))
				{
					UnityEngine.Object.Destroy(tex);
					tex = null;
					
				}
				else
				{
					tex.wrapMode = TextureWrapMode.Clamp;
					tex.anisoLevel = 4;
					
				}
			}

			Material = MakeMaterial(fm, tex);
			Status = "ok | " + fm.summary + " | " + Material.shader.name
				+ (tex != null ? " | tex" : "");
		}

		private static Material MakeMaterial(FbxMesh fm, Texture2D tex)
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
			string picked = "";
			for (int i = 0; i < chain.Length; i++)
			{
				sh = Shader.Find(chain[i]);
				if (sh != null) { picked = chain[i]; break; }
			}
			if (sh == null)
				throw new InvalidOperationException("no usable shader found");

			Material mat = new Material(sh);
			float[] d = fm.materialColors.Count > 0 ? fm.materialColors[0] : null;
			Color col = d != null ? new Color(d[0], d[1], d[2], 1f) : Color.white;

			if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
			if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
			if (tex != null)
			{
				if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
				if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);

				if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
			}
			if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
			if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);

			
			return mat;
		}

		private static byte[] ReadAsset(string extA, string extB, string resourceName, out string used)
		{
			string dir = PluginDir();
			if (dir != null)
			{
				foreach (string name in new[] { extA, extB })
				{
					try
					{
						string p = Path.Combine(dir, name);
						if (File.Exists(p))
						{
							used = p;
							return File.ReadAllBytes(p);
						}
					}
					catch { }
				}
			}
			try
			{
				Assembly asm = typeof(AvatarModel).Assembly;
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
			used = null;
			return null;
		}

		private static string PluginDir()
		{
			try
			{
				string loc = typeof(AvatarModel).Assembly.Location;
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
