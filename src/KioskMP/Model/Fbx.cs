using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KioskMP
{

	public sealed class FbxSubMesh
	{
		public int materialIndex;
		public int triStart;
		public int triCount;
	}

	public sealed class FbxMesh
	{
		public float[] positions = new float[0];
		public float[] normals = new float[0];
		public float[] uvs = new float[0];
		public int triangleCount;

		public readonly List<FbxSubMesh> submeshes = new List<FbxSubMesh>();
		public List<float[]> materialColors = new List<float[]>();
		public List<int> cornerPoint = new List<int>();
		public List<long> geomIds = new List<long>();
		public List<int> geomPointCounts = new List<int>();

		public float signedVolume;

		public float minX, minY, minZ, maxX, maxY, maxZ;
		public string summary = "";

		public int VertexCount { get { return triangleCount * 3; } }
	}

	public static class Fbx
	{
		public sealed class Node
		{
			public string name = "";
			public object[] props = new object[0];
			public List<Node> kids = new List<Node>();
		}

		public static FbxMesh Load(byte[] data)
		{
			return LoadMode(data, false);
		}

		public static FbxMesh LoadRaw(byte[] data)
		{
			return LoadMode(data, true);
		}

		private static FbxMesh LoadMode(byte[] data, bool raw)
		{
			if (data == null || data.Length < 27)
			{
				throw new InvalidDataException("FBX: file too small");
			}
			if (data[0] != 'K' || data[1] != 'a' || data[2] != 'y' || data[3] != 'd')
			{
				throw new InvalidDataException("FBX: not a binary FBX file (ASCII FBX is not supported)");
			}
			uint version = BitConverter.ToUInt32(data, 23);
			int off = 27;
			List<Node> roots = ParseNodes(data, ref off, data.Length, version);
			return Build(roots, version, raw);
		}

		public static void ParseTree(byte[] data, out List<Node> roots, out uint version)
		{
			if (data == null || data.Length < 27)
			{
				throw new InvalidDataException("FBX: file too small");
			}
			if (data[0] != 'K' || data[1] != 'a' || data[2] != 'y' || data[3] != 'd')
			{
				throw new InvalidDataException("FBX: not a binary FBX file (ASCII FBX is not supported)");
			}
			version = BitConverter.ToUInt32(data, 23);
			int off = 27;
			roots = ParseNodes(data, ref off, data.Length, version);
		}

		private static List<Node> ParseNodes(byte[] d, ref int off, int end, uint version)
		{
			bool big = version >= 7500;
			List<Node> list = new List<Node>();
			while (off + (big ? 25 : 13) <= end)
			{
				long endOff;
				long nprops;
				if (big)
				{
					endOff = (long)BitConverter.ToUInt64(d, off);
					nprops = (long)BitConverter.ToUInt64(d, off + 8);
					off += 24;
				}
				else
				{
					endOff = BitConverter.ToUInt32(d, off);
					nprops = BitConverter.ToUInt32(d, off + 4);
					off += 12;
				}

				if (endOff == 0)
				{
					off += 1;
					break;
				}
				if (endOff > end || endOff < off || nprops < 0 || nprops > 1000000)
				{
					throw new InvalidDataException("FBX: corrupt node header at " + (off - (big ? 24 : 12)));
				}

				int nlen = d[off++];
				string name = Encoding.UTF8.GetString(d, off, nlen);
				off += nlen;

				Node node = new Node();
				node.name = name;
				node.props = new object[nprops];
				for (int i = 0; i < nprops; i++)
				{
					node.props[i] = ReadProp(d, ref off);
				}

				if (endOff > off)
				{
					int childEnd = (int)endOff;
					node.kids = ParseNodes(d, ref off, childEnd, version);
				}
				off = (int)endOff;
				list.Add(node);
			}
			return list;
		}

		private static object ReadProp(byte[] d, ref int off)
		{
			char t = (char)d[off++];
			switch (t)
			{
				case 'Y':
				{
					short v = BitConverter.ToInt16(d, off);
					off += 2;
					return v;
				}
				case 'C':
				{
					bool v = d[off] != 0;
					off += 1;
					return v;
				}
				case 'I':
				{
					int v = BitConverter.ToInt32(d, off);
					off += 4;
					return v;
				}
				case 'F':
				{
					float v = BitConverter.ToSingle(d, off);
					off += 4;
					return v;
				}
				case 'D':
				{
					double v = BitConverter.ToDouble(d, off);
					off += 8;
					return v;
				}
				case 'L':
				{
					long v = BitConverter.ToInt64(d, off);
					off += 8;
					return v;
				}
				case 'f':
				case 'd':
				case 'i':
				case 'l':
				case 'b':
				{
					uint n = BitConverter.ToUInt32(d, off);
					uint enc = BitConverter.ToUInt32(d, off + 4);
					uint clen = BitConverter.ToUInt32(d, off + 8);
					off += 12;
					if (clen > d.Length - off || n > 100000000)
					{
						throw new InvalidDataException("FBX: bad array property at " + off);
					}
					byte[] raw = new byte[clen];
					Buffer.BlockCopy(d, off, raw, 0, (int)clen);
					off += (int)clen;
					if (enc == 1)
					{
						raw = Inflate(raw);
					}
					return DecodeArray(t, raw, n);
				}
				case 'S':
				{
					uint n = BitConverter.ToUInt32(d, off);
					off += 4;
					if (n > d.Length - off)
					{
						throw new InvalidDataException("FBX: bad string property");
					}
					string s = Encoding.UTF8.GetString(d, off, (int)n);
					off += (int)n;
					return s;
				}
				case 'R':
				{
					uint n = BitConverter.ToUInt32(d, off);
					off += 4;
					if (n > d.Length - off)
					{
						throw new InvalidDataException("FBX: bad raw property");
					}
					byte[] r = new byte[n];
					Buffer.BlockCopy(d, off, r, 0, (int)n);
					off += (int)n;
					return r;
				}
				default:
					throw new InvalidDataException("FBX: unknown property type '" + t + "' at " + (off - 1));
			}
		}

		private static object DecodeArray(char t, byte[] raw, uint n)
		{
			switch (t)
			{
				case 'f':
				{
					if (raw.Length != n * 4) throw new InvalidDataException("FBX: float array size mismatch");
					float[] a = new float[n];
					Buffer.BlockCopy(raw, 0, a, 0, (int)(n * 4));
					return a;
				}
				case 'd':
				{
					if (raw.Length != n * 8) throw new InvalidDataException("FBX: double array size mismatch");
					double[] a = new double[n];
					Buffer.BlockCopy(raw, 0, a, 0, (int)(n * 8));
					return a;
				}
				case 'i':
				{
					if (raw.Length != n * 4) throw new InvalidDataException("FBX: int array size mismatch");
					int[] a = new int[n];
					Buffer.BlockCopy(raw, 0, a, 0, (int)(n * 4));
					return a;
				}
				case 'l':
				{
					if (raw.Length != n * 8) throw new InvalidDataException("FBX: long array size mismatch");
					long[] a = new long[n];
					Buffer.BlockCopy(raw, 0, a, 0, (int)(n * 8));
					return a;
				}
				case 'b':
				{
					if (raw.Length != n) throw new InvalidDataException("FBX: bool array size mismatch");
					bool[] a = new bool[n];
					for (int i = 0; i < n; i++) a[i] = raw[i] != 0;
					return a;
				}
				default:
					throw new InvalidDataException("FBX: bad array type");
			}
		}

		private static byte[] Inflate(byte[] src)
		{
			int skip = 0;
			if (src.Length > 2 && src[0] == 0x78 && ((src[0] * 256 + src[1]) % 31) == 0)
			{
				skip = 2;
			}
			Exception last = null;
			for (int attempt = 0; attempt < 2; attempt++)
			{
				int s = attempt == 0 ? skip : 0;
				try
				{
					using (MemoryStream ms = new MemoryStream(src, s, src.Length - s, false))
					using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
					using (MemoryStream o = new MemoryStream(src.Length * 4))
					{
						ds.CopyTo(o);
						return o.ToArray();
					}
				}
				catch (Exception e)
				{
					last = e;
				}
			}
			throw new InvalidDataException("FBX: could not inflate array (" + (last != null ? last.Message : "?") + ")");
		}

		internal static Node Child(Node n, string name)
		{
			if (n == null) return null;
			for (int i = 0; i < n.kids.Count; i++)
			{
				if (n.kids[i].name == name) return n.kids[i];
			}
			return null;
		}

		internal static string PS(Node n, int i)
		{
			return n != null && i < n.props.Length && n.props[i] is string ? (string)n.props[i] : "";
		}

		internal static double PD(Node n, int i)
		{
			if (n == null || i >= n.props.Length) return 0.0;
			object o = n.props[i];
			if (o is double) return (double)o;
			if (o is float) return (float)o;
			if (o is int) return (int)o;
			if (o is long) return (long)o;
			if (o is short) return (short)o;
			if (o is bool) return (bool)o ? 1.0 : 0.0;
			return 0.0;
		}

		internal static long PL(Node n, int i)
		{
			if (n == null || i >= n.props.Length) return 0;
			object o = n.props[i];
			if (o is long) return (long)o;
			if (o is int) return (int)o;
			if (o is double) return (long)(double)o;
			return 0;
		}

		internal static double[] AsDoubles(object o)
		{
			double[] d = o as double[];
			if (d != null) return d;
			float[] f = o as float[];
			if (f != null)
			{
				double[] r = new double[f.Length];
				for (int i = 0; i < f.Length; i++) r[i] = f[i];
				return r;
			}
			long[] l = o as long[];
			if (l != null)
			{
				double[] r = new double[l.Length];
				for (int i = 0; i < l.Length; i++) r[i] = l[i];
				return r;
			}
			int[] ii = o as int[];
			if (ii != null)
			{
				double[] r = new double[ii.Length];
				for (int i = 0; i < ii.Length; i++) r[i] = ii[i];
				return r;
			}
			short[] sh = o as short[];
			if (sh != null)
			{
				double[] r = new double[sh.Length];
				for (int i = 0; i < sh.Length; i++) r[i] = sh[i];
				return r;
			}
			byte[] by = o as byte[];
			if (by != null)
			{
				double[] r = new double[by.Length];
				for (int i = 0; i < by.Length; i++) r[i] = by[i];
				return r;
			}
			return null;
		}

		internal static int[] AsInts(object o)
		{
			int[] a = o as int[];
			if (a != null) return a;
			long[] l = o as long[];
			if (l != null)
			{
				int[] r = new int[l.Length];
				for (int i = 0; i < l.Length; i++) r[i] = (int)l[i];
				return r;
			}
			double[] d = o as double[];
			if (d != null)
			{
				int[] r = new int[d.Length];
				for (int i = 0; i < d.Length; i++) r[i] = (int)d[i];
				return r;
			}
			return null;
		}

		internal static double[] GetD(Node n, string name)
		{
			Node c = Child(n, name);
			return c == null || c.props.Length == 0 ? null : AsDoubles(c.props[0]);
		}

		internal static int[] GetI(Node n, string name)
		{
			Node c = Child(n, name);
			return c == null || c.props.Length == 0 ? null : AsInts(c.props[0]);
		}

		internal static string GetS(Node n, string name)
		{
			Node c = Child(n, name);
			return c == null || c.props.Length == 0 ? "" : PS(c, 0);
		}

		internal static void ReadVec3(Node model, string key, ref double x, ref double y, ref double z)
		{
			Node p70 = Child(model, "Properties70");
			if (p70 == null) return;
			for (int i = 0; i < p70.kids.Count; i++)
			{
				Node p = p70.kids[i];
				if (p.name != "P" || p.props.Length < 7) continue;
				if (PS(p, 0) != key) continue;
				x = PD(p, 4);
				y = PD(p, 5);
				z = PD(p, 6);
				return;
			}
		}

		private static float[] ReadColor(Node material, string key)
		{
			Node p70 = Child(material, "Properties70");
			if (p70 == null) return null;
			for (int i = 0; i < p70.kids.Count; i++)
			{
				Node p = p70.kids[i];
				if (p.name != "P" || p.props.Length < 7) continue;
				if (PS(p, 0) != key) continue;
				return new float[] { (float)PD(p, 4), (float)PD(p, 5), (float)PD(p, 6) };
			}
			return null;
		}

		private static FbxMesh Build(List<Node> roots, uint version, bool raw)
		{
			Node objects = null;
			Node connections = null;
			for (int i = 0; i < roots.Count; i++)
			{
				if (roots[i].name == "Objects") objects = roots[i];
				else if (roots[i].name == "Connections") connections = roots[i];
			}
			if (objects == null)
			{
				throw new InvalidDataException("FBX: no Objects node");
			}

			Dictionary<long, long> geomToModel = new Dictionary<long, long>();
			Dictionary<long, List<long>> modelToMats = new Dictionary<long, List<long>>();
			Dictionary<long, Node> byId = new Dictionary<long, Node>();

			for (int i = 0; i < objects.kids.Count; i++)
			{
				Node n = objects.kids[i];
				if (n.props.Length > 0)
				{
					byId[PL(n, 0)] = n;
				}
			}
			if (connections != null)
			{
				for (int i = 0; i < connections.kids.Count; i++)
				{
					Node c = connections.kids[i];
					if (c.name != "C" || c.props.Length < 3 || PS(c, 0) != "OO") continue;
					long child = PL(c, 1);
					long parent = PL(c, 2);
					Node childNode;
					if (!byId.TryGetValue(child, out childNode)) continue;
					if (childNode.name == "Geometry")
					{
						geomToModel[child] = parent;
					}
					else if (childNode.name == "Material")
					{
						List<long> list;
						if (!modelToMats.TryGetValue(parent, out list))
						{
							list = new List<long>();
							modelToMats[parent] = list;
						}
						list.Add(child);
					}
				}
			}

			List<Node> geoms = new List<Node>();
			for (int i = 0; i < objects.kids.Count; i++)
			{
				if (objects.kids[i].name == "Geometry") geoms.Add(objects.kids[i]);
			}
			if (geoms.Count == 0)
			{
				throw new InvalidDataException("FBX: no Geometry nodes");
			}

			List<float[]> colors = new List<float[]>();
			Dictionary<long, int> colorIndex = new Dictionary<long, int>();
			foreach (KeyValuePair<long, List<long>> kv in modelToMats)
			{
				for (int i = 0; i < kv.Value.Count; i++)
				{
					long matId = kv.Value[i];
					if (colorIndex.ContainsKey(matId)) continue;
					Node mat;
					float[] col = null;
					if (byId.TryGetValue(matId, out mat))
					{
						col = ReadColor(mat, "DiffuseColor");
					}
					if (col == null) col = new float[] { 0.8f, 0.8f, 0.8f };
					colorIndex[matId] = colors.Count;
					colors.Add(col);
				}
			}
			if (colors.Count == 0)
			{
				colors.Add(new float[] { 0.85f, 0.85f, 0.85f });
			}

			List<float> pos = new List<float>();
			List<float> nrm = new List<float>();
			List<float> uv = new List<float>();
			List<int> cp = new List<int>();
			List<int> triMat = new List<int>();
			List<long> geomIdsL = new List<long>();
			List<int> geomPointCountsL = new List<int>();
			StringBuilder summary = new StringBuilder();
			int pointOffset = 0;

			for (int gi = 0; gi < geoms.Count; gi++)
			{
				Node g = geoms[gi];
				double[] verts = GetD(g, "Vertices");
				int[] pvi = GetI(g, "PolygonVertexIndex");
				if (verts == null || pvi == null || verts.Length < 9 || pvi.Length < 3)
				{
					summary.Append("[skip geom]");
					continue;
				}

				Node len = Child(g, "LayerElementNormal");
				string nMap = len == null ? "" : GetS(len, "MappingInformationType");
				string nRef = len == null ? "" : GetS(len, "ReferenceInformationType");
				double[] normals = len == null ? null : GetD(len, "Normals");
				int[] nIdx = len == null ? null : GetI(len, "NormalsIndex");

				Node luv = Child(g, "LayerElementUV");
				string uMap = luv == null ? "" : GetS(luv, "MappingInformationType");
				string uRef = luv == null ? "" : GetS(luv, "ReferenceInformationType");
				double[] uvs = luv == null ? null : GetD(luv, "UV");
				int[] uIdx = luv == null ? null : GetI(luv, "UVIndex");

				Node lmat = Child(g, "LayerElementMaterial");
				string mMap = lmat == null ? "AllSame" : GetS(lmat, "MappingInformationType");
				int[] matArr = lmat == null ? null : GetI(lmat, "Materials");

				double tx = 0, ty = 0, tz = 0, rx = 0, ry = 0, rz = 0, sx = 1, sy = 1, sz = 1;
				long geomId = PL(g, 0);
				long modelId;
				Node model = null;
				if (geomToModel.TryGetValue(geomId, out modelId))
				{
					byId.TryGetValue(modelId, out model);
				}
				if (model != null && !raw)
				{
					ReadVec3(model, "Lcl Translation", ref tx, ref ty, ref tz);
					ReadVec3(model, "Lcl Rotation", ref rx, ref ry, ref rz);
					ReadVec3(model, "Lcl Scaling", ref sx, ref sy, ref sz);
				}
				double[,] R = raw
					? new double[,] { { 1.0, 0.0, 0.0 }, { 0.0, 1.0, 0.0 }, { 0.0, 0.0, 1.0 } }
					: EulerXYZ(rx, ry, rz);

				List<int[]> polys = new List<int[]>();
				List<int> polyStart = new List<int>();
				{
					List<int> cur = new List<int>();
					for (int i = 0; i < pvi.Length; i++)
					{
						int v = pvi[i];
						if (v < 0)
						{
							cur.Add(-v - 1);
							polys.Add(cur.ToArray());
							polyStart.Add(i - cur.Count + 1);
							cur = new List<int>();
						}
						else
						{
							cur.Add(v);
						}
					}
					if (cur.Count >= 3)
					{
						polys.Add(cur.ToArray());
						polyStart.Add(pvi.Length - cur.Count);
					}
				}

				int baseTri = triMat.Count;
				int geomTris = 0;
				for (int p = 0; p < polys.Count; p++)
				{
					int[] poly = polys[p];
					if (poly.Length < 3) continue;

					int matLocal = 0;
					if (matArr != null && matArr.Length > 0)
					{
						if (mMap == "ByPolygon")
						{
							matLocal = p < matArr.Length ? matArr[p] : 0;
						}
						else if (mMap == "ByVertice" || mMap == "ByVertex")
						{
							matLocal = poly[0] < matArr.Length ? matArr[poly[0]] : 0;
						}
						else
						{
							matLocal = matArr[0];
						}
					}
					if (matLocal < 0) matLocal = 0;

					for (int t = 1; t + 1 < poly.Length; t++)
					{
						EmitCorner(pos, nrm, uv, cp, pointOffset, verts, normals, nMap, nRef, nIdx,
							uvs, uMap, uRef, uIdx, polyStart[p], p, 0, t, t + 1, poly[0], poly[t], poly[t + 1],
							R, tx, ty, tz, sx, sy, sz);
						triMat.Add(matLocal);
						geomTris++;
					}
				}
				geomIdsL.Add(geomId);
				geomPointCountsL.Add(verts.Length / 3);
				pointOffset += verts.Length / 3;
				summary.Append(PS(g, 1)).Append(":").Append(geomTris).Append(" ");
			}

			int triCount = triMat.Count;
			if (triCount == 0)
			{
				throw new InvalidDataException("FBX: mesh has no triangles");
			}

			FbxMesh m = new FbxMesh();
			m.positions = pos.ToArray();
			m.normals = nrm.ToArray();
			m.uvs = uv.ToArray();
			m.cornerPoint = cp;
			m.triangleCount = triCount;
			m.materialColors = colors;
			m.geomIds = geomIdsL;
			m.geomPointCounts = geomPointCountsL;

			{
				int runStart = 0;
				int runMat = triMat[0];
				for (int i = 1; i <= triCount; i++)
				{
					int cm = i < triCount ? triMat[i] : -1;
					if (cm != runMat)
					{
						FbxSubMesh sm = new FbxSubMesh();
						sm.materialIndex = runMat < colors.Count ? runMat : 0;
						sm.triStart = runStart;
						sm.triCount = i - runStart;
						m.submeshes.Add(sm);
						runStart = i;
						runMat = cm;
					}
				}
			}

			m.minX = m.minY = m.minZ = float.MaxValue;
			m.maxX = m.maxY = m.maxZ = float.MinValue;
			for (int i = 0; i < m.positions.Length; i += 3)
			{
				float x = m.positions[i], y = m.positions[i + 1], z = m.positions[i + 2];
				if (x < m.minX) m.minX = x;
				if (y < m.minY) m.minY = y;
				if (z < m.minZ) m.minZ = z;
				if (x > m.maxX) m.maxX = x;
				if (y > m.maxY) m.maxY = y;
				if (z > m.maxZ) m.maxZ = z;
			}

			double cx = 0, cy = 0, cz = 0;
			int nv = m.VertexCount;
			for (int i = 0; i < m.positions.Length; i += 3)
			{
				cx += m.positions[i];
				cy += m.positions[i + 1];
				cz += m.positions[i + 2];
			}
			if (nv > 0)
			{
				cx /= nv;
				cy /= nv;
				cz /= nv;
			}
			double vol = 0;
			for (int t = 0; t < triCount; t++)
			{
				int o = t * 9;
				double ax = m.positions[o] - cx, ay = m.positions[o + 1] - cy, az = m.positions[o + 2] - cz;
				double bx = m.positions[o + 3] - cx, by = m.positions[o + 4] - cy, bz = m.positions[o + 5] - cz;
				double dx = m.positions[o + 6] - cx, dy = m.positions[o + 7] - cy, dz = m.positions[o + 8] - cz;
				vol += (ax * (by * dz - bz * dy) - ay * (bx * dz - bz * dx) + az * (bx * dy - by * dx)) / 6.0;
			}
			m.signedVolume = (float)vol;

			if (m.signedVolume < 0f)
			{
				FlipWinding(m);
				m.signedVolume = -m.signedVolume;
			}

			m.summary = "fbx " + version + " | " + summary.ToString().Trim()
				+ "| tris=" + triCount + " sub=" + m.submeshes.Count
				+ " vol=" + m.signedVolume.ToString("F3")
				+ " box=(" + m.minX.ToString("F2") + "," + m.minY.ToString("F2") + "," + m.minZ.ToString("F2")
				+ ")-(" + m.maxX.ToString("F2") + "," + m.maxY.ToString("F2") + "," + m.maxZ.ToString("F2") + ")";
			return m;
		}

		private static void FlipWinding(FbxMesh m)
		{
			for (int t = 0; t < m.triangleCount; t++)
			{
				int o = t * 9;
				for (int k = 0; k < 3; k++)
				{
					float tmp;
					tmp = m.positions[o + 3 + k]; m.positions[o + 3 + k] = m.positions[o + 6 + k]; m.positions[o + 6 + k] = tmp;
					tmp = m.normals[o + 3 + k]; m.normals[o + 3 + k] = m.normals[o + 6 + k]; m.normals[o + 6 + k] = tmp;
				}
				int u = t * 6;
				float tu = m.uvs[u + 2]; m.uvs[u + 2] = m.uvs[u + 4]; m.uvs[u + 4] = tu;
				tu = m.uvs[u + 3]; m.uvs[u + 3] = m.uvs[u + 5]; m.uvs[u + 5] = tu;

				int ctmp = m.cornerPoint[t * 3 + 1];
				m.cornerPoint[t * 3 + 1] = m.cornerPoint[t * 3 + 2];
				m.cornerPoint[t * 3 + 2] = ctmp;

				for (int k = 0; k < 3; k++)
				{
					m.normals[o + 3 + k] = -m.normals[o + 3 + k];
					m.normals[o + 6 + k] = -m.normals[o + 6 + k];
				}
			}
		}

		private static void EmitCorner(
			List<float> pos, List<float> nrm, List<float> uv, List<int> cp, int pointBase,
			double[] verts, double[] normals, string nMap, string nRef, int[] nIdx,
			double[] uvs, string uMap, string uRef, int[] uIdx,
			int polyStart, int polyOrdinal, int o0, int o1, int o2, int c0, int c1, int c2,
			double[,] R, double tx, double ty, double tz, double sx, double sy, double sz)
		{
			int[] corners = { c0, c1, c2 };

			int running = polyStart;
			int[] runOf = { o0, o1, o2 };
			for (int i = 0; i < 3; i++)
			{
				int vertIdx = corners[i];
				cp.Add(vertIdx + pointBase);

				double px = verts[vertIdx * 3] * sx;
				double py = verts[vertIdx * 3 + 1] * sy;
				double pz = verts[vertIdx * 3 + 2] * sz;
				double wx = R[0, 0] * px + R[0, 1] * py + R[0, 2] * pz + tx;
				double wy = R[1, 0] * px + R[1, 1] * py + R[1, 2] * pz + ty;
				double wz = R[2, 0] * px + R[2, 1] * py + R[2, 2] * pz + tz;
				pos.Add((float)wx);
				pos.Add((float)wy);
				pos.Add((float)wz);

				double nx = 0, ny = 0, nz = 1;
				if (normals != null && normals.Length >= 3)
				{
					int ni = ResolveIndex(nMap, nRef, nIdx, vertIdx, running + runOf[i], polyOrdinal);
					if (ni >= 0 && (ni + 1) * 3 <= normals.Length)
					{
						double ox = normals[ni * 3] * sx;
						double oy = normals[ni * 3 + 1] * sy;
						double oz = normals[ni * 3 + 2] * sz;
						nx = R[0, 0] * ox + R[0, 1] * oy + R[0, 2] * oz;
						ny = R[1, 0] * ox + R[1, 1] * oy + R[1, 2] * oz;
						nz = R[2, 0] * ox + R[2, 1] * oy + R[2, 2] * oz;
						double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
						if (len > 1e-9) { nx /= len; ny /= len; nz /= len; }
						else { nx = 0; ny = 0; nz = 1; }
					}
				}
				nrm.Add((float)nx);
				nrm.Add((float)ny);
				nrm.Add((float)nz);

				float uu = 0, vv = 0;
				if (uvs != null && uvs.Length >= 2)
				{
					int ui = ResolveIndex(uMap, uRef, uIdx, vertIdx, running + runOf[i], polyOrdinal);
					if (ui >= 0 && (ui + 1) * 2 <= uvs.Length)
					{
						uu = (float)uvs[ui * 2];
						vv = (float)uvs[ui * 2 + 1];
					}
				}
				uv.Add(uu);
				uv.Add(vv);
			}
		}

		private static int ResolveIndex(string mapping, string reference, int[] idx, int vertex, int corner, int polygon)
		{
			int direct;
			if (mapping == "ByPolygonVertex") direct = corner;
			else if (mapping == "ByVertice" || mapping == "ByVertex") direct = vertex;
			else if (mapping == "ByPolygon") direct = polygon;
			else direct = 0;

			if (reference == "IndexToDirect" && idx != null)
			{
				return direct >= 0 && direct < idx.Length ? idx[direct] : -1;
			}
			return direct;
		}

		private static double[,] EulerXYZ(double dx, double dy, double dz)
		{
			double deg = Math.PI / 180.0;
			double x = dx * deg, y = dy * deg, z = dz * deg;
			double cx = Math.Cos(x), sx = Math.Sin(x);
			double cy = Math.Cos(y), sy = Math.Sin(y);
			double cz = Math.Cos(z), sz = Math.Sin(z);

			double[,] rx = { { 1, 0, 0 }, { 0, cx, -sx }, { 0, sx, cx } };
			double[,] ry = { { cy, 0, sy }, { 0, 1, 0 }, { -sy, 0, cy } };
			double[,] rz = { { cz, -sz, 0 }, { sz, cz, 0 }, { 0, 0, 1 } };
			return Mul(rz, Mul(ry, rx));
		}

		private static double[,] Mul(double[,] a, double[,] b)
		{
			double[,] r = new double[3, 3];
			for (int i = 0; i < 3; i++)
			{
				for (int j = 0; j < 3; j++)
				{
					double s = 0;
					for (int k = 0; k < 3; k++) s += a[i, k] * b[k, j];
					r[i, j] = s;
				}
			}
			return r;
		}
	}
}
