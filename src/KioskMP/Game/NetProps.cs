using UnityEngine;

namespace KioskMP
{
	public static partial class Game
	{
		public static void HandlePropSpawn(int fromPeerId, PacketReader r)
		{
			int peer = r.I32();
			int propId = r.I32();
			NetVec3 p = r.V3();
			float yaw = r.F32();
			if (propId != 0 || peer != 0)
			{
				return;
			}
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.PropSpawn(peer, propId, p, yaw), fromPeerId);
			}
			PropFX.SpawnLocal(propId, peer, new Vector3(p.x, p.y, p.z), yaw);
			AddChat("[KP] " + PeerName(peer) + " summoned a wild prop");
		}
	}
}
