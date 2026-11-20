using System;
using System.Collections.Generic;
using UnityEngine;

namespace KioskMP
{
	public sealed class AvatarAnimator
	{
		private readonly CharacterDef _def;
		private readonly Transform[] _bones;
		private readonly Dictionary<string, int> _idx;
		private readonly Vector3[] _restPos;
		private readonly Quaternion[] _restRot;
		private readonly Vector3[] _restScl;
		private FbxAnim _cur;
		private string _state = "";
		private float _t;

		public AvatarAnimator(CharacterDef def, Transform[] bones)
		{
			_def = def;
			_bones = bones;
			_idx = new Dictionary<string, int>(StringComparer.Ordinal);
			_restPos = new Vector3[bones.Length];
			_restRot = new Quaternion[bones.Length];
			_restScl = new Vector3[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				if (bones[i] != null && !_idx.ContainsKey(bones[i].name))
				{
					_idx[bones[i].name] = i;
				}
				_restPos[i] = bones[i] != null ? bones[i].localPosition : Vector3.zero;
				_restRot[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
				_restScl[i] = bones[i] != null ? bones[i].localScale : Vector3.one;
			}
		}

		public string State
		{
			get { return _state; }
		}

		public void CaptureRest()
		{
			for (int i = 0; i < _bones.Length; i++)
			{
				if (_bones[i] == null)
				{
					continue;
				}
				_restPos[i] = _bones[i].localPosition;
				_restRot[i] = _bones[i].localRotation;
				_restScl[i] = _bones[i].localScale;
			}
		}

		public void Play(string state)
		{
			if (_state == state)
			{
				return;
			}
			_state = state;
			_cur = _def != null ? _def.GetAnim(state) : null;
			_t = 0f;
			ResetPose();
			if (_cur != null && _cur.bones.Count > 0)
			{
				Apply();
			}
		}

		public void Tick(float dt)
		{
			if (_cur == null || _bones == null)
			{
				return;
			}
			_t += dt;
			if (_cur.duration > 0.05f)
			{
				_t %= (float)_cur.duration;
			}
			if (_t < 0f)
			{
				_t = 0f;
			}
			Apply();
		}

		private void ResetPose()
		{
			for (int i = 0; i < _bones.Length; i++)
			{
				if (_bones[i] == null)
				{
					continue;
				}
				_bones[i].localPosition = _restPos[i];
				_bones[i].localRotation = _restRot[i];
				_bones[i].localScale = _restScl[i];
			}
		}

		private void Apply()
		{
			for (int i = 0; i < _cur.bones.Count; i++)
			{
				FbxAnimBone ab = _cur.bones[i];
				if (ab == null || ab.channels.Count < 3)
				{
					continue;
				}
				int bi;
				if (!_idx.TryGetValue(ab.boneName, out bi))
				{
					continue;
				}
				Transform tr = _bones[bi];
				if (tr == null)
				{
					continue;
				}
				if (ab.trs == "Lcl Translation")
				{
					tr.localPosition = _restPos[bi] + Sample3(ab);
				}
				else if (ab.trs == "Lcl Rotation")
				{
					tr.localRotation = _def.Pose(bi, Sample3(ab));
				}
			}
		}

		private Vector3 Sample3(FbxAnimBone ab)
		{
			float x = ChannelValue(ab, 0);
			float y = ChannelValue(ab, 1);
			float z = ChannelValue(ab, 2);
			return new Vector3(x, y, z);
		}

		private float ChannelValue(FbxAnimBone ab, int axis)
		{
			FbxChannel ch = axis < ab.channels.Count ? ab.channels[axis] : null;
			if (ch == null)
			{
				return 0f;
			}
			if (ch.curve != null)
			{
				return ch.curve.Eval(_t);
			}
			return ch.def;
		}
	}
}
