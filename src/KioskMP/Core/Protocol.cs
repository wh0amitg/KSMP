using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KioskMP
{

	public enum Msg : byte
	{
		Hello = 1,
		Welcome = 2,
		Reject = 3,

		PlayerState = 10,
		Chat = 11,
		PeerJoined = 12,
		PeerLeft = 13,
		Kick = 14,

		ObjState = 20,
		ObjClaim = 21,
		ObjGrant = 22,
		ObjSpawn = 23,
		ObjSpawnLocal = 24,
		ObjDespawn = 25,

		Interact = 30,
		AddAddition = 31,

		CustSpawn = 40,
		OrderDone = 41,
		CustGoAway = 42,
		CustGoAwayReq = 43,
		TimerSync = 44,
		UiCmd = 45,
		Reaction = 46,
		BoardSync = 47,
		CustState = 48,
		DialogSync = 49,

		Snapshot = 50,
		WorldReset = 51,
		ObjCut = 52,
		TriggerEvt = 53,

		Ping = 60,
		Pong = 61,

		LobbyReady = 62,
		LobbySync = 63,

		Customize = 64,
		Dance = 65,

		PropSpawn = 66,
		DecalBatch = 67,
		SauceSync = 68,
		FryerStart = 69,
		FryerEmpty = 70,
		MarkerColor = 71,
		FriesScoop = 72
	}

	public enum InteractMethod : byte
	{
		Press = 0,
		Release = 1,
		Drag = 2,
		Use = 3,
		StartUse = 4,
		StopUse = 5,
		UseSecond = 6
	}

	public enum UiCommand : byte
	{
		StartStory = 0,
		StartEndless = 1,
		StartRelax = 2,
		Continue = 3,
		BackToMenu = 4,
		RestartEndless = 5,
		YouDiedRestart = 6,
		EndCredit = 7,
		NextDay = 8,
		CallOverEndless = 9
	}

	public struct NetVec3
	{
		public float x, y, z;

		public NetVec3(float x, float y, float z)
		{
			this.x = x;
			this.y = y;
			this.z = z;
		}

		public override string ToString()
		{
			return string.Format("({0:F2}, {1:F2}, {2:F2})", x, y, z);
		}
	}

	public struct NetQuat
	{
		public float x, y, z, w;

		public NetQuat(float x, float y, float z, float w)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.w = w;
		}
	}

	public struct DecalRec
	{
		public int toolId;
		public byte kind;
		public NetVec3 pos;
		public NetQuat rot;
		public NetVec3 scale;
		public string prefab;
	}

	public static class Proto
	{
		public const byte Version = 8;
		public const int MaxFrame = 1 << 20;
		public const int MaxPlayers = 4;

		public const int DynamicIdBase = 100000;
	}

	public sealed class PacketWriter : IDisposable
	{
		private readonly MemoryStream _ms;
		private readonly BinaryWriter _w;

		public PacketWriter()
		{
			_ms = new MemoryStream(64);
			_w = new BinaryWriter(_ms, Encoding.UTF8);
		}

		public int Length
		{
			get { return (int)_ms.Length; }
		}

		public void U8(byte v)
		{
			_w.Write(v);
		}

		public void I8(sbyte v)
		{
			_w.Write(v);
		}

		public void Bool(bool v)
		{
			_w.Write(v);
		}

		public void I16(short v)
		{
			_w.Write(v);
		}

		public void U16(ushort v)
		{
			_w.Write(v);
		}

		public void I32(int v)
		{
			_w.Write(v);
		}

		public void U32(uint v)
		{
			_w.Write(v);
		}

		public void I64(long v)
		{
			_w.Write(v);
		}

		public void F32(float v)
		{
			_w.Write(v);
		}

		public void Str(string v)
		{
			if (v == null)
			{
				v = "";
			}
			if (v.Length > 256)
			{
				v = v.Substring(0, 256);
			}
			_w.Write(v);
		}

		public void V3(NetVec3 v)
		{
			_w.Write(v.x);
			_w.Write(v.y);
			_w.Write(v.z);
		}

		public void Q4(NetQuat v)
		{
			_w.Write(v.x);
			_w.Write(v.y);
			_w.Write(v.z);
			_w.Write(v.w);
		}

		public void Bytes(byte[] v)
		{
			_w.Write(v.Length);
			_w.Write(v);
		}

		public byte[] ToMessage(Msg type)
		{
			byte[] payload = _ms.ToArray();
			byte[] outp = new byte[payload.Length + 1];
			outp[0] = (byte)type;
			Buffer.BlockCopy(payload, 0, outp, 1, payload.Length);
			return outp;
		}

		public void Dispose()
		{
			_w.Dispose();
			_ms.Dispose();
		}
	}

	public sealed class PacketReader : IDisposable
	{
		private readonly BinaryReader _r;
		private readonly byte[] _buf;

		public PacketReader(byte[] message)
		{
			_buf = message;
			_r = new BinaryReader(new MemoryStream(message, 1, message.Length - 1, false), Encoding.UTF8);
		}

		public byte Type
		{
			get { return _buf[0]; }
		}

		public byte U8()
		{
			return _r.ReadByte();
		}

		public bool Bool()
		{
			return _r.ReadBoolean();
		}

		public short I16()
		{
			return _r.ReadInt16();
		}

		public ushort U16()
		{
			return _r.ReadUInt16();
		}

		public int I32()
		{
			return _r.ReadInt32();
		}

		public uint U32()
		{
			return _r.ReadUInt32();
		}

		public long I64()
		{
			return _r.ReadInt64();
		}

		public float F32()
		{
			return _r.ReadSingle();
		}

		public string Str()
		{
			return _r.ReadString();
		}

		public NetVec3 V3()
		{
			NetVec3 v;
			v.x = _r.ReadSingle();
			v.y = _r.ReadSingle();
			v.z = _r.ReadSingle();
			return v;
		}

		public NetQuat Q4()
		{
			NetQuat v;
			v.x = _r.ReadSingle();
			v.y = _r.ReadSingle();
			v.z = _r.ReadSingle();
			v.w = _r.ReadSingle();
			return v;
		}

		public byte[] Bytes()
		{
			int n = _r.ReadInt32();
			if (n < 0 || n > Proto.MaxFrame)
			{
				throw new InvalidDataException("bad byte block length " + n);
			}
			return _r.ReadBytes(n);
		}

		public bool HasMore
		{
			get { return _r.BaseStream.Position < _r.BaseStream.Length; }
		}

		public void Dispose()
		{
			_r.Dispose();
		}
	}

	public sealed class OrderLine
	{
		public int objectId;
		public bool done;
		public List<int> additions = new List<int>();

		public void Write(PacketWriter w)
		{
			w.I32(objectId);
			w.Bool(done);
			w.I32(additions.Count);
			for (int i = 0; i < additions.Count; i++)
			{
				w.I32(additions[i]);
			}
		}

		public static OrderLine Read(PacketReader r)
		{
			OrderLine line = new OrderLine();
			line.objectId = r.I32();
			line.done = r.Bool();
			int n = r.I32();
			for (int i = 0; i < n; i++)
			{
				line.additions.Add(r.I32());
			}
			return line;
		}
	}

	public sealed class CustomerSpawnInfo
	{
		public byte gameMode;
		public int day;
		public int customerIdx;
		public byte prefabKind;
		public int prefabIndex;
		public bool applyOrder;
		public bool ordered;
		public List<OrderLine> order = new List<OrderLine>();

		public void Write(PacketWriter w)
		{
			w.U8(gameMode);
			w.I32(day);
			w.I32(customerIdx);
			w.U8(prefabKind);
			w.I32(prefabIndex);
			w.Bool(applyOrder);
			w.Bool(ordered);
			w.I32(order.Count);
			for (int i = 0; i < order.Count; i++)
			{
				order[i].Write(w);
			}
		}

		public static CustomerSpawnInfo Read(PacketReader r)
		{
			CustomerSpawnInfo info = new CustomerSpawnInfo();
			info.gameMode = r.U8();
			info.day = r.I32();
			info.customerIdx = r.I32();
			info.prefabKind = r.U8();
			info.prefabIndex = r.I32();
			info.applyOrder = r.Bool();
			info.ordered = r.Bool();
			int n = r.I32();
			for (int i = 0; i < n; i++)
			{
				info.order.Add(OrderLine.Read(r));
			}
			return info;
		}
	}

	public struct ObjState
	{
		public int objId;
		public int owner;
		public int parentId;
		public byte mode;
		public byte flags;
		public NetVec3 pos;
		public NetQuat rot;
		public NetVec3 vel;
		public List<int> additions;

		public int grabObjectId;

		public int meatType;

		public float cook;

		public int bunFlags;
		public float ketchup;
		public float mustard;
		public float coffee;

		public const byte FlagDisabled = 1;
		public const byte FlagHeld = 2;

		public const int FlagNone = -1;
		public const int BunCheddar = 1;
		public const int BunLettuce = 2;
		public const int BunKetchup = 4;
		public const int BunMustard = 8;
		public const int BunCut = 16;
		public const int BunStinky = 32;

		public void Write(PacketWriter w)
		{
			w.I32(objId);
			w.I32(owner);
			w.I32(parentId);
			w.U8(mode);
			w.U8(flags);
			w.V3(pos);
			w.Q4(rot);
			w.V3(vel);
			int n = additions == null ? 0 : additions.Count;
			w.I32(n);
			for (int i = 0; i < n; i++)
			{
				w.I32(additions[i]);
			}
			w.I32(grabObjectId);
			w.I32(meatType);
			w.F32(cook);
			w.I32(bunFlags);
			w.F32(ketchup);
			w.F32(mustard);
			w.F32(coffee);
		}

		public static ObjState Read(PacketReader r)
		{
			ObjState s = new ObjState();
			s.objId = r.I32();
			s.owner = r.I32();
			s.parentId = r.I32();
			s.mode = r.U8();
			s.flags = r.U8();
			s.pos = r.V3();
			s.rot = r.Q4();
			s.vel = r.V3();
			int n = r.I32();
			if (n < 0) n = 0;
			if (n > 4096) n = 4096;
			s.additions = new List<int>(n);
			for (int i = 0; i < n; i++)
			{
				s.additions.Add(r.I32());
			}
			s.grabObjectId = r.I32();
			s.meatType = r.I32();
			s.cook = r.F32();
			s.bunFlags = r.I32();
			s.ketchup = r.F32();
			s.mustard = r.F32();
			s.coffee = r.F32();
			return s;
		}
	}

	public static class MsgBuild
	{
		public static byte[] Hello(string name, byte transport)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.U8(Proto.Version);
				w.Str(name);
				w.U8(transport);
				return w.ToMessage(Msg.Hello);
			}
		}

		public static byte[] Reject(string reason)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.Str(reason);
				return w.ToMessage(Msg.Reject);
			}
		}

		public static byte[] PlayerState(int id, byte flags, NetVec3 pos, float yaw, float pitch)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(id);
				w.U8(flags);
				w.V3(pos);
				w.F32(yaw);
				w.F32(pitch);
				return w.ToMessage(Msg.PlayerState);
			}
		}

		public static byte[] Chat(int fromId, string text)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(fromId);
				w.Str(text);
				return w.ToMessage(Msg.Chat);
			}
		}

		public static byte[] PeerJoined(int id, string name)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(id);
				w.Str(name);
				return w.ToMessage(Msg.PeerJoined);
			}
		}

		public static byte[] PeerLeft(int id)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(id);
				return w.ToMessage(Msg.PeerLeft);
			}
		}

		public static byte[] Kick(int id, string reason)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(id);
				w.Str(reason ?? "");
				return w.ToMessage(Msg.Kick);
			}
		}

		public static byte[] ObjStateMsg(ObjState s)
		{
			using (PacketWriter w = new PacketWriter())
			{
				s.Write(w);
				return w.ToMessage(Msg.ObjState);
			}
		}

		public static byte[] ObjClaim(int objId)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				return w.ToMessage(Msg.ObjClaim);
			}
		}

		public static byte[] ObjGrant(int objId, int owner)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.I32(owner);
				return w.ToMessage(Msg.ObjGrant);
			}
		}

		public static byte[] ObjSpawn(int objId, int prefabIndex, int originPeer, int originSeq, int parentId, int ownerId, byte mode, byte flags, NetVec3 pos, NetQuat rot, string spawnName)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.I32(prefabIndex);
				w.I32(originPeer);
				w.I32(originSeq);
				w.I32(parentId);
				w.I32(ownerId);
				w.U8(mode);
				w.U8(flags);
				w.V3(pos);
				w.Q4(rot);
				w.Str(spawnName);
				return w.ToMessage(Msg.ObjSpawn);
			}
		}

		public static byte[] ObjSpawnLocal(int seq, int prefabIndex, NetVec3 pos, NetQuat rot, int ownerId, byte mode, byte flags, string spawnName)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(seq);
				w.I32(prefabIndex);
				w.V3(pos);
				w.Q4(rot);
				w.I32(ownerId);
				w.U8(mode);
				w.U8(flags);
				w.Str(spawnName);
				return w.ToMessage(Msg.ObjSpawnLocal);
			}
		}

		public static byte[] ObjDespawn(int objId)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				return w.ToMessage(Msg.ObjDespawn);
			}
		}

		public static byte[] Interact(int objId, InteractMethod method, bool argB, float argF)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.U8((byte)method);
				w.Bool(argB);
				w.F32(argF);
				return w.ToMessage(Msg.Interact);
			}
		}

		public static byte[] AddAddition(int objId, int additionId)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.I32(additionId);
				return w.ToMessage(Msg.AddAddition);
			}
		}

		public static byte[] BoardSync(bool clear, List<OrderLine> lines)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.Bool(clear);
				int n = lines == null ? 0 : lines.Count;
				w.I32(n);
				for (int i = 0; i < n; i++)
				{
					lines[i].Write(w);
				}
				return w.ToMessage(Msg.BoardSync);
			}
		}

		public static byte[] Snapshot(byte[] payload)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.Bytes(payload);
				return w.ToMessage(Msg.Snapshot);
			}
		}

		public static byte[] CustSpawn(CustomerSpawnInfo info)
		{
			using (PacketWriter w = new PacketWriter())
			{
				info.Write(w);
				return w.ToMessage(Msg.CustSpawn);
			}
		}

		public static byte[] OrderDone(int index, float timerDelta, bool lineCompleted)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(index);
				w.F32(timerDelta);
				w.Bool(lineCompleted);
				return w.ToMessage(Msg.OrderDone);
			}
		}

		public static byte[] CustGoAway(bool nextCustomer)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.Bool(nextCustomer);
				return w.ToMessage(Msg.CustGoAway);
			}
		}

		public static byte[] CustGoAwayReq(bool nextCustomer)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.Bool(nextCustomer);
				return w.ToMessage(Msg.CustGoAwayReq);
			}
		}

		public static byte[] TimerSync(float remaining, float elapsed, int orders, byte gameMode, int day, int customerIdx, bool inGame)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.F32(remaining);
				w.F32(elapsed);
				w.I32(orders);
				w.U8(gameMode);
				w.I32(day);
				w.I32(customerIdx);
				w.Bool(inGame);
				return w.ToMessage(Msg.TimerSync);
			}
		}

		public static byte[] UiCmd(UiCommand cmd)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.U8((byte)cmd);
				return w.ToMessage(Msg.UiCmd);
			}
		}

		public static byte[] Reaction(string id)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.Str(id);
				return w.ToMessage(Msg.Reaction);
			}
		}

		public static byte[] WorldReset(byte reason)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.U8(reason);
				return w.ToMessage(Msg.WorldReset);
			}
		}

		public static byte[] CustState(byte flags, List<OrderLine> lines)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.U8(flags);
				int n = lines == null ? 0 : lines.Count;
				w.I32(n);
				for (int i = 0; i < n; i++)
				{
					lines[i].Write(w);
				}
				return w.ToMessage(Msg.CustState);
			}
		}

		public static byte[] DialogSync(byte kind, int owner, int dialogId, bool onlyLastText, bool setSkipFirst)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.U8(kind);
				w.I32(owner);
				w.I32(dialogId);
				w.Bool(onlyLastText);
				w.Bool(setSkipFirst);
				return w.ToMessage(Msg.DialogSync);
			}
		}

		public static byte[] ObjCut(int objId)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				return w.ToMessage(Msg.ObjCut);
			}
		}

		public static byte[] TriggerEvt(int objId, byte kind)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.U8(kind);
				return w.ToMessage(Msg.TriggerEvt);
			}
		}

		public static byte[] Ping(long t)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I64(t);
				return w.ToMessage(Msg.Ping);
			}
		}

		public static byte[] Pong(long t)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I64(t);
				return w.ToMessage(Msg.Pong);
			}
		}

		public static byte[] LobbyReady(bool ready)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.Bool(ready);
				return w.ToMessage(Msg.LobbyReady);
			}
		}

		public static byte[] LobbySync(List<int> readyIds)
		{
			using (PacketWriter w = new PacketWriter())
			{
				int n = readyIds == null ? 0 : readyIds.Count;
				w.I32(n);
				for (int i = 0; i < n; i++)
				{
					w.I32(readyIds[i]);
				}
				return w.ToMessage(Msg.LobbySync);
			}
		}

		public static byte[] Customize(int peerId, int characterId, int variant)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(peerId);
				w.I32(characterId);
				w.I32(variant);
				return w.ToMessage(Msg.Customize);
			}
		}

		public static byte[] Dance(int peerId)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(peerId);
				return w.ToMessage(Msg.Dance);
			}
		}

		public static byte[] PropSpawn(int peerId, int propId, NetVec3 pos, float yaw)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(peerId);
				w.I32(propId);
				w.V3(pos);
				w.F32(yaw);
				return w.ToMessage(Msg.PropSpawn);
			}
		}

		public static byte[] ReactionStr(byte kind, string id)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.U8(kind);
				w.Str(id == null ? "" : id);
				return w.ToMessage(Msg.Reaction);
			}
		}

		public static byte[] DecalBatch(List<DecalRec> list)
		{
			using (PacketWriter w = new PacketWriter())
			{
				int n = list == null ? 0 : list.Count;
				w.I32(n);
				for (int i = 0; i < n; i++)
				{
					w.I32(list[i].toolId);
					w.U8(list[i].kind);
					w.V3(list[i].pos);
					w.Q4(list[i].rot);
					w.V3(list[i].scale);
					w.Str(list[i].prefab);
				}
				return w.ToMessage(Msg.DecalBatch);
			}
		}

		public static byte[] SauceSync(int objId, float ketchup, float mustard, int flags)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.F32(ketchup);
				w.F32(mustard);
				w.I32(flags);
				return w.ToMessage(Msg.SauceSync);
			}
		}

		public static byte[] FryerStart(int objId, int grabId)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.I32(grabId);
				return w.ToMessage(Msg.FryerStart);
			}
		}

		public static byte[] FryerEmpty(int objId)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				return w.ToMessage(Msg.FryerEmpty);
			}
		}

		public static byte[] MarkerColor(int objId, int color)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.I32(color);
				return w.ToMessage(Msg.MarkerColor);
			}
		}

		public static byte[] FriesScoop(int objId, int grabId, string name)
		{
			using (PacketWriter w = new PacketWriter())
			{
				w.I32(objId);
				w.I32(grabId);
				w.Str(name == null ? "" : name);
				return w.ToMessage(Msg.FriesScoop);
			}
		}
	}
}
