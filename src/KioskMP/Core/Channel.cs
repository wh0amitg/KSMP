using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Steamworks;

namespace KioskMP
{

	public abstract class Channel : IDisposable
	{
		private readonly Queue<byte[]> _inbox = new Queue<byte[]>();
		private readonly object _inboxLock = new object();

		public abstract bool Connected { get; }

		public abstract string Label { get; }

		public bool IsServerSide { get; set; }

		public int PeerId { get; set; }

		public bool AwaitingHandshake { get; set; }

		public float LastSeen { get; set; }

		public abstract void Send(byte[] message);

		public abstract void Pump();

		public abstract void Close();

		public virtual void Flush()
		{
		}

		public virtual string EndDetail()
		{
			return "";
		}

		protected void Enqueue(byte[] message)
		{
			lock (_inboxLock)
			{
				_inbox.Enqueue(message);
			}
		}

		public bool TryDequeue(out byte[] message)
		{
			lock (_inboxLock)
			{
				if (_inbox.Count == 0)
				{
					message = null;
					return false;
				}
				message = _inbox.Dequeue();
				return true;
			}
		}

		public int Pending
		{
			get
			{
				lock (_inboxLock)
				{
					return _inbox.Count;
				}
			}
		}

		public virtual void Dispose()
		{
			Close();
		}
	}

	public sealed class TcpChannel : Channel
	{
		private readonly TcpClient _client;
		private readonly Socket _sock;
		private readonly byte[] _rbuf = new byte[64 * 1024];
		private readonly byte[] _frameLen = new byte[4];
		private int _rlen;
		private int _need = -1;
		private byte[] _accum;
		private int _accumLen;
		private volatile bool _dead;

		public TcpChannel(TcpClient client)
		{
			_client = client;
			AwaitingHandshake = true;
			LastSeen = UnityEngine.Time.realtimeSinceStartup;
			_sock = client.Client;
			_sock.NoDelay = true;
			_sock.SendTimeout = 2000;
			_sock.ReceiveTimeout = 0;
			_client.ReceiveBufferSize = 256 * 1024;
			_client.SendBufferSize = 256 * 1024;
		}

		public override bool Connected
		{
			get
			{
				if (_dead)
				{
					return false;
				}
				try
				{
					if (!_sock.Connected)
					{
						return false;
					}

					if (_sock.Poll(0, SelectMode.SelectRead) && _sock.Available == 0)
					{
						_dead = true;
						return false;
					}
					return true;
				}
				catch
				{
					_dead = true;
					return false;
				}
			}
		}

		public override string Label
		{
			get
			{
				try
				{
					return ((IPEndPoint)_sock.RemoteEndPoint).ToString();
				}
				catch
				{
					return "?";
				}
			}
		}

		public override void Send(byte[] message)
		{
			if (_dead || message == null)
			{
				return;
			}
			byte[] frame = new byte[message.Length + 4];
			frame[0] = (byte)(message.Length & 0xFF);
			frame[1] = (byte)((message.Length >> 8) & 0xFF);
			frame[2] = (byte)((message.Length >> 16) & 0xFF);
			frame[3] = (byte)((message.Length >> 24) & 0xFF);
			Buffer.BlockCopy(message, 0, frame, 4, message.Length);
			try
			{
				int sent = 0;
				while (sent < frame.Length)
				{
					int n = _sock.Send(frame, sent, frame.Length - sent, SocketFlags.None);
					if (n <= 0)
					{
						_dead = true;
						return;
					}
					sent += n;
				}
			}
			catch
			{
				_dead = true;
			}
		}

		public override void Pump()
		{
			if (_dead)
			{
				return;
			}
			try
			{
				int avail = _sock.Available;
				while (avail > 0)
				{
					int want = Math.Min(avail, _rbuf.Length);
					int n = _sock.Receive(_rbuf, 0, want, SocketFlags.None);
					if (n <= 0)
					{
						_dead = true;
						return;
					}
					Consume(_rbuf, n);
					avail = _sock.Available;
				}
			}
			catch (SocketException)
			{
				_dead = true;
			}
			catch (ObjectDisposedException)
			{
				_dead = true;
			}
		}

		private void Consume(byte[] data, int count)
		{
			int off = 0;
			while (off < count)
			{
				if (_need < 0)
				{
					while (_rlen < 4 && off < count)
					{
						_frameLen[_rlen++] = data[off++];
					}
					if (_rlen < 4)
					{
						return;
					}
					_need = _frameLen[0] | (_frameLen[1] << 8) | (_frameLen[2] << 16) | (_frameLen[3] << 24);
					_rlen = 0;
					if (_need <= 0 || _need > Proto.MaxFrame)
					{
						_dead = true;
						return;
					}
					_accum = new byte[_need];
					_accumLen = 0;
				}

				int take = Math.Min(count - off, _need - _accumLen);
				Buffer.BlockCopy(data, off, _accum, _accumLen, take);
				_accumLen += take;
				off += take;
				if (_accumLen == _need)
				{
					Enqueue(_accum);
					_accum = null;
					_accumLen = 0;
					_need = -1;
				}
			}
		}

		public override void Close()
		{
			_dead = true;
			try
			{
				_sock.Close();
			}
			catch
			{
			}
			try
			{
				_client.Close();
			}
			catch
			{
			}
		}

		public override string EndDetail()
		{
			try
			{
				if (!_sock.Connected)
				{
					return "tcp socket closed";
				}
				return "";
			}
			catch (Exception e)
			{
				return "tcp error: " + e.Message;
			}
		}
	}

	public sealed class SteamP2PChannel : Channel
	{
		public const int VirtualPort = 7241;

		private readonly HSteamNetConnection _conn;
		private readonly IntPtr[] _ptrs = new IntPtr[32];
		private volatile bool _dead;

		public SteamP2PChannel(HSteamNetConnection conn)
		{
			_conn = conn;
			AwaitingHandshake = true;
			LastSeen = UnityEngine.Time.realtimeSinceStartup;
			try
			{
				SteamNetworkingSockets.SetConnectionName(conn, "KioskMP");
			}
			catch
			{
			}
		}

		public HSteamNetConnection Connection
		{
			get { return _conn; }
		}

		public override bool Connected
		{
			get
			{
				if (_dead)
				{
					return false;
				}
				try
				{
					SteamNetConnectionInfo_t info;
					if (!SteamNetworkingSockets.GetConnectionInfo(_conn, out info))
					{
						_dead = true;
						return false;
					}
					if (info.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
					{
						_dead = true;
						return false;
					}
					return true;
				}
				catch
				{
					_dead = true;
					return false;
				}
			}
		}

		public override string Label
		{
			get
			{
				try
				{
					SteamNetConnectionInfo_t info;
					if (SteamNetworkingSockets.GetConnectionInfo(_conn, out info))
					{
						return "steam:" + info.m_identityRemote.GetSteamID64();
					}
				}
				catch
				{
				}
				return "steam:?";
			}
		}

		public override void Send(byte[] message)
		{
			if (_dead || message == null || message.Length == 0)
			{
				return;
			}
			IntPtr pData = Marshal.AllocHGlobal(message.Length);
			try
			{
				Marshal.Copy(message, 0, pData, message.Length);
				long ignored;
				EResult res = SteamNetworkingSockets.SendMessageToConnection(
					_conn, pData, (uint)message.Length,
					8, out ignored);
				if (res == EResult.k_EResultLimitExceeded)
				{

					
				}
				else if (res != EResult.k_EResultOK)
				{
					
					_dead = true;
				}
			}
			finally
			{
				Marshal.FreeHGlobal(pData);
			}
		}

		public override void Pump()
		{
			if (_dead)
			{
				return;
			}
			try
			{
				int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(_conn, _ptrs, _ptrs.Length);
				for (int i = 0; i < n; i++)
				{
					IntPtr p = _ptrs[i];
					if (p == IntPtr.Zero)
					{
						continue;
					}
					try
					{
						SteamNetworkingMessage_t m = SteamNetworkingMessage_t.FromIntPtr(p);
						if (m.m_cbSize > 0 && m.m_cbSize <= Proto.MaxFrame)
						{
							byte[] data = new byte[m.m_cbSize];
							Marshal.Copy(m.m_pData, data, 0, m.m_cbSize);
							Enqueue(data);
						}
					}
					finally
					{
						SteamNetworkingMessage_t.Release(p);
					}
				}
			}
			catch
			{
				_dead = true;
			}
		}

		public override void Close()
		{
			if (_dead)
			{
				return;
			}
			_dead = true;
			try
			{
				SteamNetworkingSockets.CloseConnection(_conn, 0, "bye", false);
			}
			catch
			{
			}
		}

		public override void Flush()
		{
			try { SteamNetworkingSockets.FlushMessagesOnConnection(_conn); } catch { }
		}

		public override string EndDetail()
		{
			try
			{
				SteamNetConnectionInfo_t info;
				if (SteamNetworkingSockets.GetConnectionInfo(_conn, out info))
				{
					return "steam state=" + info.m_eState + " end=" + info.m_eEndReason;
				}
			}
			catch
			{
			}
			return "steam ?";
		}
	}
}
