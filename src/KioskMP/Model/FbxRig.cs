using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KioskMP
{
	public sealed class FbxBone
	{
		public string name;
		public long id;
		public int parent = -1;
		public int index;
		public int rotOrder;
		public double tx, ty, tz, rx, ry, rz, sx = 1, sy = 1, sz = 1;
		public double preX, preY, preZ;
		public double postX, postY, postZ;
		public bool hasPre;
		public bool hasPost;
	}

	public sealed class FbxCluster
	{
		public long id;
		public int bone;
		public int[] indices = new int[0];
		public float[] weights = new float[0];
		public double[] transform;
		public double[] transformLink;
	}

	public sealed class FbxGeomSkin
	{
		public long geomId;
		public List<FbxCluster> clusters = new List<FbxCluster>();
	}

	public sealed class FbxCurve
	{
		public double[] t = new double[0];
		public float[] v = new float[0];

		public float Eval(double time)
		{
			if (t.Length == 0)
			{
				return 0f;
			}
			if (time <= t[0])
			{
				return v[0];
			}
			if (time >= t[t.Length - 1])
			{
				return v[v.Length - 1];
			}
			int lo = 0;
			int hi = t.Length - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) >> 1;
				if (t[mid] <= time) lo = mid; else hi = mid;
			}
			double span = t[hi] - t[lo];
			if (span <= 1e-9)
			{
				return v[hi];
			}
			float a = v[lo];
			float b = v[hi];
			return a + (b - a) * (float)((time - t[lo]) / span);
		}
	}

	public sealed class FbxChannel
	{
		public string axis;
		public FbxCurve curve;
		public float def;
	}

	public sealed class FbxAnimBone
	{
		public string boneName;
		public string trs;
		public List<FbxChannel> channels = new List<FbxChannel>();
	}

	public sealed class FbxAnim
	{
		public string name;
		public double start;
		public double duration;
		public List<FbxAnimBone> bones = new List<FbxAnimBone>();
	}

	public sealed class FbxRig
	{
		public List<FbxBone> bones = new List<FbxBone>();
		public List<FbxGeomSkin> skins = new List<FbxGeomSkin>();
		public List<FbxAnim> anims = new List<FbxAnim>();
		public string summary = "";
	}

	public static class FbxRigReader
	{
		private const double KTimePerSecond = 46186158000.0;

		private static string CleanFbxName(string s)
		{
			if (s == null)
			{
				return "";
			}
			int z = s.IndexOf('\0');
			if (z >= 0)
			{
				s = s.Substring(0, z);
			}
			int c = s.IndexOf("::", StringComparison.Ordinal);
			if (c >= 0)
			{
				s = s.Substring(c + 2);
			}
			return s;
		}

		public static FbxRig Read(byte[] data)
		{
			List<Fbx.Node> roots;
			uint ver;
			Fbx.ParseTree(data, out roots, out ver);

			Fbx.Node objects = null;
			Fbx.Node conns = null;
			for (int i = 0; i < roots.Count; i++)
			{
				if (roots[i].name == "Objects") objects = roots[i];
				else if (roots[i].name == "Connections") conns = roots[i];
			}
			if (objects == null)
			{
				throw new InvalidDataException("FBX rig: no Objects node");
			}

			FbxRig rig = new FbxRig();
			Dictionary<long, Fbx.Node> byId = new Dictionary<long, Fbx.Node>();
			for (int i = 0; i < objects.kids.Count; i++)
			{
				Fbx.Node n = objects.kids[i];
				if (n.props.Length > 0)
				{
					byId[Fbx.PL(n, 0)] = n;
				}
			}

			Dictionary<long, int> modelIdx = new Dictionary<long, int>();
			for (int i = 0; i < objects.kids.Count; i++)
			{
				Fbx.Node n = objects.kids[i];
				if (n.name != "Model")
				{
					continue;
				}
				string subtype = Fbx.PS(n, 2);
				if (subtype == "Mesh")
				{
					continue;
				}
				FbxBone b = new FbxBone();
				b.id = Fbx.PL(n, 0);
				b.name = CleanFbxName(Fbx.PS(n, 1));
				b.rotOrder = ReadRotOrder(n);
				Fbx.ReadVec3(n, "Lcl Translation", ref b.tx, ref b.ty, ref b.tz);
				Fbx.ReadVec3(n, "Lcl Rotation", ref b.rx, ref b.ry, ref b.rz);
				Fbx.ReadVec3(n, "Lcl Scaling", ref b.sx, ref b.sy, ref b.sz);
				double px = 0, py = 0, pz = 0;
				Fbx.ReadVec3(n, "PreRotation", ref px, ref py, ref pz);
				if (px != 0 || py != 0 || pz != 0)
				{
					b.hasPre = true;
					b.preX = px;
					b.preY = py;
					b.preZ = pz;
				}
				double qx = 0, qy = 0, qz = 0;
				Fbx.ReadVec3(n, "PostRotation", ref qx, ref qy, ref qz);
				if (qx != 0 || qy != 0 || qz != 0)
				{
					b.hasPost = true;
					b.postX = qx;
					b.postY = qy;
					b.postZ = qz;
				}
				b.index = rig.bones.Count;
				modelIdx[b.id] = b.index;
				rig.bones.Add(b);
			}

			if (conns != null)
			{
				for (int i = 0; i < conns.kids.Count; i++)
				{
					Fbx.Node c = conns.kids[i];
					if (c.name != "C" || c.props.Length < 3 || Fbx.PS(c, 0) != "OO")
					{
						continue;
					}
					long child = Fbx.PL(c, 1);
					long parent = Fbx.PL(c, 2);
					int ci, pi;
					if (modelIdx.TryGetValue(child, out ci) && modelIdx.TryGetValue(parent, out pi))
					{
						rig.bones[ci].parent = pi;
					}
				}
			}

			if (conns != null)
			{
				for (int i = 0; i < conns.kids.Count; i++)
				{
					Fbx.Node c = conns.kids[i];
					if (c.name != "C" || c.props.Length < 3 || Fbx.PS(c, 0) != "OO")
					{
						continue;
					}
					long child = Fbx.PL(c, 1);
					long parent = Fbx.PL(c, 2);
					Fbx.Node childNode;
					Fbx.Node parentNode;
					if (!byId.TryGetValue(child, out childNode) || !byId.TryGetValue(parent, out parentNode))
					{
						continue;
					}
					string childSub = Fbx.PS(childNode, 2);
					string parentSub = Fbx.PS(parentNode, 2);
					if (childSub == "Skin" && parentNode.name == "Geometry")
					{
						AddSkinToGeom(rig, byId, parent, child, conns);
					}
					else if (parentSub == "Skin" && childSub == "Cluster")
					{
						LinkCluster(rig, byId, modelIdx, parent, child, conns);
					}
					else if (childSub == "Cluster")
					{
						int bi;
						if (modelIdx.TryGetValue(parent, out bi))
						{
							LinkCluster(rig, byId, modelIdx, -1, child, conns, bi);
						}
					}
				}
			}

			StringBuilder sb = new StringBuilder();
			sb.Append("bones=").Append(rig.bones.Count);
			int clusterCount = 0;
			for (int i = 0; i < rig.skins.Count; i++)
			{
				clusterCount += rig.skins[i].clusters.Count;
			}
			sb.Append(" clusters=").Append(clusterCount);
			sb.Append(" anims=").Append(rig.anims.Count);
			rig.summary = sb.ToString();
			return rig;
		}

		private static void AddSkinToGeom(FbxRig rig, Dictionary<long, Fbx.Node> byId,
			long geomId, long skinId, Fbx.Node conns)
		{
			FbxGeomSkin skin = null;
			for (int i = 0; i < rig.skins.Count; i++)
			{
				if (rig.skins[i].geomId == geomId)
				{
					skin = rig.skins[i];
					break;
				}
			}
			if (skin == null)
			{
				skin = new FbxGeomSkin();
				skin.geomId = geomId;
				rig.skins.Add(skin);
			}
			if (conns != null)
			{
				for (int i = 0; i < conns.kids.Count; i++)
				{
					Fbx.Node c = conns.kids[i];
					if (c.name != "C" || c.props.Length < 3 || Fbx.PS(c, 0) != "OO")
					{
						continue;
					}
					if (Fbx.PL(c, 2) != skinId)
					{
						continue;
					}
					long clusterId = Fbx.PL(c, 1);
					Fbx.Node cn;
					if (!byId.TryGetValue(clusterId, out cn) || Fbx.PS(cn, 2) != "Cluster")
					{
						continue;
					}
					bool known = false;
					for (int k = 0; k < skin.clusters.Count; k++)
					{
						if (skin.clusters[k].id == clusterId)
						{
							known = true;
							break;
						}
					}
					if (!known)
					{
						FbxCluster cl = new FbxCluster();
						cl.id = clusterId;
						cl.bone = -1;
						int[] idx = Fbx.GetI(cn, "Indexes");
						double[] w = Fbx.GetD(cn, "Weights");
						if (idx != null)
						{
							cl.indices = idx;
						}
						if (w != null)
						{
							cl.weights = new float[w.Length];
							for (int k = 0; k < w.Length; k++)
							{
								cl.weights[k] = (float)w[k];
							}
						}
						cl.transform = Fbx.GetD(cn, "Transform");
						cl.transformLink = Fbx.GetD(cn, "TransformLink");
						skin.clusters.Add(cl);
					}
				}
			}
		}

		private static void LinkCluster(FbxRig rig, Dictionary<long, Fbx.Node> byId,
			Dictionary<long, int> modelIdx, long skinId, long clusterId, Fbx.Node conns, int forcedBone = -1)
		{
			FbxCluster target = null;
			for (int i = 0; i < rig.skins.Count; i++)
			{
				for (int k = 0; k < rig.skins[i].clusters.Count; k++)
				{
					if (rig.skins[i].clusters[k].id == clusterId)
					{
						target = rig.skins[i].clusters[k];
						break;
					}
				}
			}
			if (target == null || target.bone >= 0)
			{
				return;
			}
			if (forcedBone >= 0)
			{
				target.bone = forcedBone;
				return;
			}
			if (conns != null)
			{
				for (int i = 0; i < conns.kids.Count; i++)
				{
					Fbx.Node c = conns.kids[i];
					if (c.name != "C" || c.props.Length < 3 || Fbx.PS(c, 0) != "OO")
					{
						continue;
					}
					if (Fbx.PL(c, 1) != clusterId)
					{
						continue;
					}
					int bi;
					if (modelIdx.TryGetValue(Fbx.PL(c, 2), out bi))
					{
						target.bone = bi;
						return;
					}
				}
			}
		}

		public static void ReadAnims(byte[] data, FbxRig rig)
		{
			List<Fbx.Node> roots;
			uint ver;
			Fbx.ParseTree(data, out roots, out ver);
			Fbx.Node objects = null;
			Fbx.Node conns = null;
			for (int i = 0; i < roots.Count; i++)
			{
				if (roots[i].name == "Objects") objects = roots[i];
				else if (roots[i].name == "Connections") conns = roots[i];
			}
			if (objects == null)
			{
				return;
			}

			Dictionary<long, Fbx.Node> byId = new Dictionary<long, Fbx.Node>();
			for (int i = 0; i < objects.kids.Count; i++)
			{
				Fbx.Node n = objects.kids[i];
				if (n.props.Length > 0)
				{
					byId[Fbx.PL(n, 0)] = n;
				}
			}

			Dictionary<long, long> stackOfLayer = new Dictionary<long, long>();
			Dictionary<long, long> layerOfCurveNode = new Dictionary<long, long>();
			Dictionary<long, FbxAnim> stackByLayer = new Dictionary<long, FbxAnim>();
			Dictionary<long, string> trsOfCurveNode = new Dictionary<long, string>();
			Dictionary<long, long> modelOfCurveNode = new Dictionary<long, long>();
			List<Fbx.Node> curveNodes = new List<Fbx.Node>();

			for (int i = 0; i < objects.kids.Count; i++)
			{
				Fbx.Node n = objects.kids[i];
				if (n.name != "AnimationCurveNode")
				{
					continue;
				}
				curveNodes.Add(n);
			}

			Dictionary<long, FbxAnim> stacks = new Dictionary<long, FbxAnim>();
			CurveByNode.Clear();
			PropByCurve.Clear();
			for (int i = 0; i < objects.kids.Count; i++)
			{
				Fbx.Node n = objects.kids[i];
				if (n.name != "AnimationStack")
				{
					continue;
				}
				FbxAnim a = new FbxAnim();
				a.name = CleanFbxName(Fbx.PS(n, 1));
				a.start = 0;
				a.duration = 0;
				stacks[Fbx.PL(n, 0)] = a;
				rig.anims.Add(a);
			}

			if (conns != null)
			{
				for (int i = 0; i < conns.kids.Count; i++)
				{
					Fbx.Node c = conns.kids[i];
					if (c.props.Length < 3)
					{
						continue;
					}
					string type = Fbx.PS(c, 0);
					long child = Fbx.PL(c, 1);
					long parent = Fbx.PL(c, 2);
					string prop = c.props.Length > 3 ? Fbx.PS(c, 3) : "";
					if (type == "OO")
					{
						Fbx.Node cn, pn;
						if (byId.TryGetValue(child, out cn) && byId.TryGetValue(parent, out pn))
						{
							if (pn.name == "AnimationStack" && cn.name == "AnimationLayer")
							{
								FbxAnim a;
								if (stacks.TryGetValue(parent, out a))
								{
									stackByLayer[child] = a;
								}
							}
							else if (pn.name == "AnimationLayer" && cn.name == "AnimationCurveNode")
							{
								FbxAnim a;
								if (stackByLayer.TryGetValue(parent, out a))
								{
									layerOfCurveNode[child] = parent;
								}
							}
						}
					}
					else if (type == "OP")
					{
						Fbx.Node cn;
						if (!byId.TryGetValue(child, out cn))
						{
							continue;
						}
						if (cn.name == "AnimationCurveNode")
						{
							if (prop.StartsWith("Lcl", StringComparison.Ordinal) || prop.StartsWith("GeometricTranslation", StringComparison.Ordinal))
							{
								trsOfCurveNode[child] = prop;
								modelOfCurveNode[child] = parent;
							}
						}
						else if (cn.name == "AnimationCurve")
						{
							CurveByNode[child] = parent;
							PropByCurve[child] = prop;
						}
					}
				}
			}

			foreach (long nodeId in new List<long>(trsOfCurveNode.Keys))
			{
				long lay;
				if (!layerOfCurveNode.TryGetValue(nodeId, out lay))
				{
					continue;
				}
				FbxAnim a;
				if (!stackByLayer.TryGetValue(lay, out a))
				{
					continue;
				}
				long modelId;
				if (!modelOfCurveNode.TryGetValue(nodeId, out modelId))
				{
					continue;
				}
				Fbx.Node cn;
				if (!byId.TryGetValue(nodeId, out cn))
				{
					continue;
				}
				long m2 = modelId;
				Fbx.Node mn;
				string boneName = "";
				if (byId.TryGetValue(m2, out mn))
				{
					boneName = CleanFbxName(Fbx.PS(mn, 1));
				}
				FbxAnimBone ab = new FbxAnimBone();
				ab.boneName = boneName;
				string trs;
				trsOfCurveNode.TryGetValue(nodeId, out trs);
				ab.trs = trs == null ? "" : trs;
				for (int axis = 0; axis < 3; axis++)
				{
					string axisName = axis == 0 ? "d|X" : axis == 1 ? "d|Y" : "d|Z";
					FbxChannel ch = new FbxChannel();
					ch.axis = axisName;
					ch.def = ChannelDefault(cn, axisName);
					ch.curve = null;
					foreach (KeyValuePair<long, long> ck in CurveByNode)
					{
						if (ck.Value != nodeId)
						{
							continue;
						}
						string p;
						PropByCurve.TryGetValue(ck.Key, out p);
						if (p != axisName)
						{
							continue;
						}
						Fbx.Node curveNode;
						if (byId.TryGetValue(ck.Key, out curveNode))
						{
							ch.curve = ReadCurve(curveNode);
						}
						break;
					}
					ab.channels.Add(ch);
				}
				a.bones.Add(ab);
				if (ab.trs == "Lcl Rotation" || ab.trs == "Lcl Translation")
				{
					double tMax = 0;
					for (int k = 0; k < 3; k++)
					{
						FbxCurve cv = ab.channels[k].curve;
						if (cv != null && cv.t.Length > 0 && cv.t[cv.t.Length - 1] > tMax)
						{
							tMax = cv.t[cv.t.Length - 1];
						}
					}
					if (tMax > a.duration)
					{
						a.duration = tMax;
					}
				}
			}
		}

		private static int ReadRotOrder(Fbx.Node model)
		{
			Fbx.Node p70 = Fbx.Child(model, "Properties70");
			if (p70 == null)
			{
				return 0;
			}
			for (int i = 0; i < p70.kids.Count; i++)
			{
				Fbx.Node p = p70.kids[i];
				if (p.name != "P" || p.props.Length < 5)
				{
					continue;
				}
				if (Fbx.PS(p, 0) != "RotationOrder")
				{
					continue;
				}
				return (int)Fbx.PD(p, 4);
			}
			return 0;
		}

		private static readonly Dictionary<long, long> CurveByNode = new Dictionary<long, long>();
		private static readonly Dictionary<long, string> PropByCurve = new Dictionary<long, string>();

		private static float ChannelDefault(Fbx.Node curveNode, string axis)
		{
			Fbx.Node p70 = Fbx.Child(curveNode, "Properties70");
			if (p70 == null)
			{
				return 0f;
			}
			for (int i = 0; i < p70.kids.Count; i++)
			{
				Fbx.Node p = p70.kids[i];
				if (p.name != "P" || p.props.Length < 5)
				{
					continue;
				}
				if (Fbx.PS(p, 0) != axis)
				{
					continue;
				}
				return (float)Fbx.PD(p, 4);
			}
			return 0f;
		}

		private static FbxCurve ReadCurve(Fbx.Node n)
		{
			double[] t = Fbx.GetD(n, "KeyTime");
			double[] v = Fbx.GetD(n, "KeyValueFloat");
			if (t == null || v == null || t.Length == 0 || t.Length != v.Length)
			{
				return null;
			}
			FbxCurve c = new FbxCurve();
			c.t = t;
			c.v = new float[v.Length];
			for (int i = 0; i < v.Length; i++)
			{
				c.v[i] = (float)v[i];
			}
			double max = t[t.Length - 1];
			if (max > 1e7)
			{
				for (int i = 0; i < c.t.Length; i++)
				{
					c.t[i] /= KTimePerSecond;
				}
			}
			return c;
		}
	}
}
