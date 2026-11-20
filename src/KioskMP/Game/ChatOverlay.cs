using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KioskMP
{
	public static class ChatOverlay
	{
		public static bool IsOpen;

		private static string _input = "";
		private static int _openFrame = -1;
		private static bool _hooked;
		private static GameObject _root;
		private static RectTransform _inputRow;
		private static Text _inputText;
		private static GameObject _logObj;
		private static Text _logText;
		private static GameObject _feedObj;
		private static Text _feedText;
		private static bool _logDirty;
		private static int _lastChatCount = -1;		private static float _feedUntil;
		private static int _buildFails;
		private static int _lastDupFrame = -1;
		private static char _lastDupChar;

		private static readonly Color Yellow = new Color(1f, 0.769f, 0f, 1f);
		private static readonly Color InputBg = new Color(0.09f, 0.09f, 0.1f, 0.94f);
		private static readonly Color LogBg = new Color(0f, 0f, 0f, 0.55f);
		private static readonly Color White = new Color(0.95f, 0.95f, 0.95f, 1f);
		private static readonly Color Grey = new Color(0.6f, 0.6f, 0.62f, 1f);

		private const int MaxLen = 120;

		public static void Open()
		{
			EnsureBuilt();
			if (_root == null)
			{
				return;
			}
			IsOpen = true;
			_input = "";
			_openFrame = Time.frameCount;
			_logDirty = true;
		}

		public static void Close()
		{
			IsOpen = false;
			_openFrame = -1;
		}

		public static void Tick()
		{
			EnsureBuilt();
			if (_root == null)
			{
				return;
			}
			if (!Session.Active)
			{
				if (IsOpen)
				{
					Close();
				}
				_root.SetActive(false);
				return;
			}
			if (MenuUI.IsOpen && IsOpen)
			{
				Close();
			}
			if (!IsOpen && ChatHotkeyPressed() && !Game.DialogOnScreen())
			{
				Open();
			}
			if (IsOpen)
			{
				HandleKeys();
			}
			UpdateVisuals();
			_root.SetActive(IsOpen || Time.realtimeSinceStartup < _feedUntil);
		}

		private static bool ChatHotkeyPressed()
		{
			try
			{
				if (Input.GetKeyDown(KeyCode.T))
				{
					return true;
				}
			}
			catch
			{
			}
			try
			{
				Keyboard kb = Keyboard.current;
				if (kb != null && kb.tKey.wasPressedThisFrame)
				{
					return true;
				}
			}
			catch
			{
			}
			return false;
		}

		private static void HandleKeys()
		{
			Keyboard kb = Keyboard.current;
			if (kb == null)
			{
				return;
			}
			bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
			if (kb.escapeKey.wasPressedThisFrame)
			{
				Close();
				return;
			}
			if (kb.backspaceKey.wasPressedThisFrame && _input.Length > 0)
			{
				_input = _input.Substring(0, _input.Length - 1);
			}
			if (ctrl && kb.vKey.wasPressedThisFrame)
			{
				string clip = GUIUtility.systemCopyBuffer;
				if (!string.IsNullOrEmpty(clip))
				{
					AppendText(clip);
				}
			}
			if (ctrl && kb.cKey.wasPressedThisFrame && _input.Length > 0)
			{
				GUIUtility.systemCopyBuffer = _input;
			}
			if (ctrl && kb.xKey.wasPressedThisFrame && _input.Length > 0)
			{
				GUIUtility.systemCopyBuffer = _input;
				_input = "";
			}
			if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
			{
				string t = _input.Trim();
				if (t.Length > 0)
				{
					Game.SendChat(t);
				}
				_input = "";
				Close();
			}
		}

		private static void AppendText(string text)
		{
			StringBuilder ok = new StringBuilder();
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c >= ' ' && c != '\r' && c != '\n')
				{
					ok.Append(c);
				}
			}
			int room = MaxLen - _input.Length;
			if (room <= 0)
			{
				return;
			}
			if (ok.Length > room)
			{
				ok.Length = room;
			}
			_input += ok.ToString();
		}

		private static void OnTextInputChar(char c)
		{
			if (!IsOpen || Time.frameCount == _openFrame)
			{
				return;
			}
			if (Time.frameCount == _lastDupFrame && c == _lastDupChar)
			{
				return;
			}
			_lastDupFrame = Time.frameCount;
			_lastDupChar = c;
			Keyboard kb = Keyboard.current;
			if (kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
			{
				return;
			}
			if (c < ' ')
			{
				return;
			}
			if (_input.Length >= MaxLen)
			{
				return;
			}
			_input += c;
		}

		private static void HookTextInput()
		{
			if (_hooked)
			{
				return;
			}
			Keyboard kb = Keyboard.current;
			if (kb == null)
			{
				return;
			}
			kb.onTextInput += OnTextInputChar;
			_hooked = true;
		}

		private static void EnsureBuilt()
		{
			HookTextInput();
			if (_root != null)
			{
				return;
			}
			if (_buildFails >= 5)
			{
				return;
			}
			try
			{
				BuildCanvas();
			}
			catch (Exception e)
			{
				_buildFails++;
				
			}
		}

		private static void BuildCanvas()
		{
			GameObject root = new GameObject("KioskMP_ChatOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			Canvas canvas = root.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 800;
			CanvasScaler scaler = root.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1280f, 720f);
			scaler.matchWidthOrHeight = 0.5f;
			UnityEngine.Object.DontDestroyOnLoad(root);
			root.SetActive(false);
			_root = root;

			_logObj = Panel(root.transform, "chatlog", new Vector2(16f, 90f), new Vector2(640f, 320f), LogBg).gameObject;
			_logText = TextIn(_logObj.transform, "log", 12f, 10f, TextAnchor.LowerLeft, White, 16);

			_inputRow = Panel(root.transform, "chatinput", new Vector2(16f, 40f), new Vector2(640f, 44f), Yellow);
			GameObject inner = new GameObject("inner", typeof(RectTransform), typeof(Image));
			RectTransform irt = inner.GetComponent<RectTransform>();
			irt.SetParent(_inputRow, false);
			irt.anchorMin = Vector2.zero;
			irt.anchorMax = Vector2.one;
			irt.offsetMin = new Vector2(2f, 2f);
			irt.offsetMax = new Vector2(-2f, -2f);
			inner.GetComponent<Image>().color = InputBg;
			_inputText = TextIn(irt, "text", 12f, 0f, TextAnchor.MiddleLeft, White, 17);

			GameObject feed = new GameObject("chatfeed", typeof(RectTransform));
			RectTransform frt = feed.GetComponent<RectTransform>();
			frt.SetParent(root.transform, false);
			frt.anchorMin = new Vector2(0f, 0f);
			frt.anchorMax = new Vector2(0f, 0f);
			frt.pivot = new Vector2(0f, 0f);
			frt.anchoredPosition = new Vector2(14f, 40f);
			frt.sizeDelta = new Vector2(560f, 110f);
			_feedText = feed.AddComponent<Text>();
			_feedText.font = CanvasMenu.UiFont();
			_feedText.fontSize = 15;
			_feedText.alignment = TextAnchor.LowerLeft;
			_feedText.color = White;
			_feedText.supportRichText = false;
			_feedText.horizontalOverflow = HorizontalWrapMode.Wrap;
			_feedText.verticalOverflow = VerticalWrapMode.Overflow;
			_feedObj = feed;
			feed.SetActive(false);

			CanvasMenu.EnsureEventSystem();
		}

		private static RectTransform Panel(Transform parent, string name, Vector2 pos, Vector2 size, Color col)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			rt.anchorMin = new Vector2(0f, 0f);
			rt.anchorMax = new Vector2(0f, 0f);
			rt.pivot = new Vector2(0f, 0f);
			rt.anchoredPosition = pos;
			rt.sizeDelta = size;
			rt.GetComponent<Image>().color = col;
			return rt;
		}

		private static Text TextIn(Transform parent, string name, float padX, float padY, TextAnchor anchor, Color col, int size)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = new Vector2(padX, padY);
			rt.offsetMax = new Vector2(-padX, -padY);
			Text t = go.AddComponent<Text>();
			t.font = CanvasMenu.UiFont();
			t.fontSize = size;
			t.alignment = anchor;
			t.color = col;
			t.supportRichText = false;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Overflow;
			return t;
		}

		private static void UpdateVisuals()
		{
			int count = Game.ChatLog.Count;
			if (count != _lastChatCount)
			{
				_lastChatCount = count;
				if (count > 0)
				{
					_feedUntil = Time.realtimeSinceStartup + 8f;
				}
				_logDirty = true;
			}
			if (IsOpen)
			{
				if (_logDirty)
				{
					_logDirty = false;
					StringBuilder sb = new StringBuilder();
					int start = Mathf.Max(0, count - 12);
					for (int i = start; i < count; i++)
					{
						if (sb.Length > 0)
						{
							sb.Append('\n');
						}
						sb.Append(Game.ChatLog[i]);
					}
					bool empty = sb.Length == 0;
					_logText.text = empty ? "No messages yet. Type and press Enter." : sb.ToString();
					_logText.color = empty ? Grey : White;
				}
				if (_inputText != null)
				{
					if (_input.Length == 0)
					{
						_inputText.text = "> say something ...";
						_inputText.color = Grey;
					}
					else
					{
						_inputText.text = "> " + _input + "_";
						_inputText.color = White;
					}
				}
			}
			if (_feedObj != null)
			{
				bool showFeed = !IsOpen && count > 0 && Time.realtimeSinceStartup < _feedUntil;
				_feedObj.SetActive(showFeed);
				if (showFeed)
				{
					StringBuilder sb = new StringBuilder();
					int start = Mathf.Max(0, count - 4);
					for (int i = start; i < count; i++)
					{
						if (sb.Length > 0)
						{
							sb.Append('\n');
						}
						sb.Append(Game.ChatLog[i]);
					}
					_feedText.text = sb.ToString();
					float a = Mathf.Clamp01((_feedUntil - Time.realtimeSinceStartup) / 2f);
					_feedText.color = new Color(1f, 1f, 1f, 0.85f * a);
				}
			}
			if (_inputRow != null)
			{
				_inputRow.gameObject.SetActive(IsOpen);
			}
			if (_logObj != null)
			{
				_logObj.SetActive(IsOpen);
			}
		}
	}
}
