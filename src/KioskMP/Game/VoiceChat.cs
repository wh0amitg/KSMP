using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KioskMP
{
	public static partial class Game
	{
		public static void HandleVoice(int fromPeerId, PacketReader r)
		{
			int peer = r.I32();
			if (peer < 0)
			{
				peer = fromPeerId;
			}
			byte[] data = r.Bytes();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.Voice(peer, data), fromPeerId);
			}
			VoiceChat.Receive(peer, data);
		}
	}

	public static class VoiceChat
	{
		private const int MaxSamples = 48000;
		private const int MaxCompressed = 8192;

		private sealed class Player
		{
			public AudioSource source;
			public AudioClip clip;
			public readonly Queue<float> samples = new Queue<float>();
		}

		private static readonly Dictionary<int, Player> _players = new Dictionary<int, Player>();
		private static GameObject _root;
		private static byte[] _packet = new byte[MaxCompressed];
		private static byte[] _pcm = new byte[16384];
		private static uint _rate;
		private static bool _recording;
		private static bool _pttDown;

		public static bool Speaking;

		public static void Reset()
		{
			if (_recording)
			{
				try { SteamUser.StopVoiceRecording(); } catch { }
				_recording = false;
			}
			foreach (KeyValuePair<int, Player> kv in _players)
			{
				try
				{
					if (kv.Value.source != null)
					{
						kv.Value.source.Stop();
						UnityEngine.Object.Destroy(kv.Value.source);
					}
					if (kv.Value.clip != null)
					{
						UnityEngine.Object.Destroy(kv.Value.clip);
					}
				}
				catch { }
			}
			_players.Clear();
			Speaking = false;
			_pttDown = false;
		}

		public static void Tick()
		{
			if (!Session.Active || !SteamNet.SteamReady || !SteamManager.Initialized)
			{
				if (_recording)
				{
					try { SteamUser.StopVoiceRecording(); } catch { }
					_recording = false;
					Speaking = false;
				}
				return;
			}
			if (_rate == 0)
			{
				try { _rate = SteamUser.GetVoiceOptimalSampleRate(); } catch { }
				if (_rate < 8000) _rate = 11025;
			}

			bool down = false;
			if (!MenuUI.IsOpen && !ChatOverlay.IsOpen)
			{
				try { down = Input.GetKey(KeyCode.V); } catch { }
				if (!down)
				{
					try
					{
						Keyboard kb = Keyboard.current;
						down = kb != null && kb.vKey.isPressed;
					}
					catch { }
				}
			}
			if (down && !_recording)
			{
				try { SteamUser.StartVoiceRecording(); _recording = true; } catch { }
			}
			else if (!down && _recording)
			{
				try { SteamUser.StopVoiceRecording(); } catch { }
				_recording = false;
				Speaking = false;
			}

			if (_recording)
			{
				Pump();
			}
			UpdatePositions();
		}

		private static void UpdatePositions()
		{
			foreach (KeyValuePair<int, Player> kv in _players)
			{
				Player p = kv.Value;
				if (p.source == null)
				{
					continue;
				}
				Transform avatar = Game.AvatarTransformOf(kv.Key);
				if (avatar != null && Game.GameRunning)
				{
					p.source.transform.position = avatar.position;
					p.source.spatialBlend = 1f;
				}
				else
				{
					p.source.spatialBlend = 0f;
				}
			}
		}

		private static void Pump()
		{
			uint avail;
			EVoiceResult check;
			try { check = SteamUser.GetAvailableVoice(out avail); }
			catch { return; }
			if (check != EVoiceResult.k_EVoiceResultOK || avail == 0)
			{
				Speaking = false;
				return;
			}
			if (avail > (uint)_packet.Length)
			{
				_packet = new byte[avail];
			}
			uint written;
			EVoiceResult res;
			try { res = SteamUser.GetVoice(true, _packet, (uint)_packet.Length, out written); }
			catch { return; }
			if (res != EVoiceResult.k_EVoiceResultOK || written < 1)
			{
				Speaking = false;
				return;
			}
			byte[] data = new byte[written];
			Buffer.BlockCopy(_packet, 0, data, 0, (int)written);
			byte[] m = MsgBuild.Voice(Session.SelfId, data);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
			Speaking = true;
		}

		public static void Receive(int peer, byte[] data)
		{
			if (peer == Session.SelfId || data == null || data.Length == 0)
			{
				return;
			}
			if (_rate == 0)
			{
				try { _rate = SteamUser.GetVoiceOptimalSampleRate(); } catch { }
				if (_rate < 8000) _rate = 11025;
			}
			uint written = 0;
			EVoiceResult res = EVoiceResult.k_EVoiceResultBufferTooSmall;
			int tries = 0;
			while (res == EVoiceResult.k_EVoiceResultBufferTooSmall && tries < 4)
			{
				try
				{
					res = SteamUser.DecompressVoice(data, (uint)data.Length, _pcm, (uint)_pcm.Length, out written, _rate);
				}
				catch { return; }
				if (res == EVoiceResult.k_EVoiceResultBufferTooSmall)
				{
					_pcm = new byte[_pcm.Length * 2];
				}
				tries++;
			}
			if (res != EVoiceResult.k_EVoiceResultOK || written < 2)
			{
				return;
			}
			Player p = GetPlayer(peer);
			if (p == null)
			{
				return;
			}
			int count = (int)written / 2;
			lock (p.samples)
			{
				for (int i = 0; i < count; i++)
				{
					short v = (short)(_pcm[i * 2] | (_pcm[i * 2 + 1] << 8));
					p.samples.Enqueue(v / 32768f);
				}
				while (p.samples.Count > MaxSamples)
				{
					p.samples.Dequeue();
				}
			}
		}

		private static Player GetPlayer(int peer)
		{
			Player p;
			if (_players.TryGetValue(peer, out p) && p.source != null)
			{
				return p;
			}
			if (_root == null)
			{
				_root = new GameObject("KSMP_Voice");
				UnityEngine.Object.DontDestroyOnLoad(_root);
			}
			p = new Player();
			p.clip = AudioClip.Create("KSMP_Voice_" + peer, 4096, 1, (int)_rate, true,
				delegate(float[] buffer) { Read(peer, buffer); });
			p.source = _root.AddComponent<AudioSource>();
			p.source.clip = p.clip;
			p.source.loop = true;
			p.source.spatialBlend = 1f;
			p.source.rolloffMode = AudioRolloffMode.Linear;
			p.source.minDistance = 1.2f;
			p.source.maxDistance = 22f;
			p.source.dopplerLevel = 0f;
			p.source.spread = 0f;
			p.source.playOnAwake = false;
			p.source.volume = 1f;
			p.source.Play();
			_players[peer] = p;
			return p;
		}

		private static void Read(int peer, float[] buffer)
		{
			Player p;
			if (!_players.TryGetValue(peer, out p))
			{
				Array.Clear(buffer, 0, buffer.Length);
				return;
			}
			lock (p.samples)
			{
				int n = Mathf.Min(buffer.Length, p.samples.Count);
				for (int i = 0; i < n; i++)
				{
					buffer[i] = p.samples.Dequeue();
				}
				if (n < buffer.Length)
				{
					Array.Clear(buffer, n, buffer.Length - n);
				}
			}
		}
	}
}
