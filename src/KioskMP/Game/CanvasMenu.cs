using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KioskMP
{
	public static class CanvasMenu
	{
		public static bool IsOpen;
		public static string Mode = "story";
		public static int SelectedCharacter;
		public static int SelectedSkin;

		private static GameObject _root;
		private static Text _statusLabel;
		private static Text _toastLabel;
		private static RectTransform _pageRoot;
		private static readonly List<Field> _fields = new List<Field>();
		private static Field _focus;
		private static bool _textInputHooked;
		private static string _page = "";
		private static string _lastListSig = "\0";
		private static bool _wasRunning;

		private static Text _infoLine;
		private static Text _codeLine;
		private static GameObject _readyBtn;
		private static Text _readyLabel;
		private static RectTransform _listRoot;
		private static GameObject _modeRow;
		private static readonly Dictionary<string, Image> _modeBtns = new Dictionary<string, Image>();
		private static readonly Dictionary<string, Text> _modeLabels = new Dictionary<string, Text>();
		private static readonly List<Card> _cards = new List<Card>();
		private static int _lastCharFrame = -1;
		private static char _lastChar;
		private static char _pendingChar;
		private static int _pendingFrame = -1;

		private static Font _font;
		private static bool _fontTried;
		private static int _buildFails;

		private static float _lastFriendsRefresh;
		private static float _lastLobbyRefresh;

		private static string _lobbyDesc = "";
		private static string _lobbyPwd = "";
		private static string _joinPwd = "";

		private static int _friendScroll;
		private static int _friendScrollMax;
		private static Slider _friendSlider;
		private static RectTransform _friendSliderRoot;
		private static bool _sliderSetting;
		private static float _friendListTop;

		private static int _lobbyScroll;
		private static int _lobbyScrollMax;
		private static Slider _lobbySlider;
		private static RectTransform _lobbySliderRoot;

		private static string _lobbyName = "Kiosk Co-op";
		private static string _maxPlayers = "4";
		private static string _port = "27890";
		private static string _ip = "127.0.0.1";
		private static string _codeSteam = "";

		private static readonly Color ScreenBg = new Color(0.043f, 0.043f, 0.051f, 1f);
		private static readonly Color Yellow = new Color(1f, 0.769f, 0f, 1f);
		private static readonly Color Black = new Color(0.07f, 0.07f, 0.07f, 1f);
		private static readonly Color Panel = new Color(0.11f, 0.11f, 0.12f, 1f);
		private static readonly Color FieldBg = new Color(0.15f, 0.15f, 0.16f, 1f);
		private static readonly Color FieldFocus = new Color(0.21f, 0.21f, 0.22f, 1f);
		private static readonly Color Grey = new Color(0.55f, 0.55f, 0.58f, 1f);
		private static readonly Color White = new Color(0.95f, 0.95f, 0.95f, 1f);

		private const float ContentW = 520f;

		private sealed class Field
		{
			public string name;
			public RectTransform rect;
			public Image bg;
			public Text label;
			public Func<string> get;
			public Action<string> set;
			public string placeholder = "";
			public int maxLen = 24;
			public bool digitsOnly;
		}

		private sealed class Card
		{
			public Image bg;
			public Text title;
			public Text sub;
			public Text num;
		}

		public static void Toggle()
		{
			if (IsOpen)
			{
				Close();
			}
			else
			{
				Open();
			}
		}

		public static void Open()
		{
			try
			{
				
				EnsureBuilt();
				if (_root == null)
				{
					
					return;
				}
				IsOpen = true;
				MenuUI.IsOpen = true;
				ChatOverlay.Close();
				_wasRunning = Game.GameRunning;
				_focus = null;
				_root.SetActive(true);
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
				ShowPage(Session.Active ? "lobby" : "main");
			}
			catch (Exception e)
			{
				
				IsOpen = false;
				MenuUI.IsOpen = false;
				if (_root != null)
				{
					_root.SetActive(false);
				}
			}
		}

		public static void Close()
		{
			
			IsOpen = false;
			MenuUI.IsOpen = false;
			_focus = null;
			if (_root != null)
			{
				try { _root.SetActive(false); }
				catch (Exception e) {  }
			}
		}

		public static void Tick()
		{
			if (!IsOpen)
			{
				return;
			}
			EnsureBuilt();
			if (_root == null)
			{
				
				return;
			}
			if (!_root.activeSelf)
			{
				return;
			}
			if (SteamNet.SteamReady && _page == "join"
				&& Time.realtimeSinceStartup - _lastLobbyRefresh > 15f)
			{
				_lastLobbyRefresh = Time.realtimeSinceStartup;
				SteamNet.RequestLobbyList();
			}
			if (SteamNet.SteamReady && _page == "lobby"
				&& Time.realtimeSinceStartup - _lastFriendsRefresh > 15f)
			{
				_lastFriendsRefresh = Time.realtimeSinceStartup;
				SteamNet.RefreshFriends();
			}
			KeyboardTick();
			FriendWheelTick();
			RefreshPageFlow();
			RefreshFields();
			RefreshDynamic();
			RefreshToasts();
			RefreshList();
		}

		private static void RefreshPageFlow()
		{
			if (Session.Active && _page != "lobby")
			{
				ShowPage("lobby");
			}
			else if (!Session.Active && _page == "lobby")
			{
				ShowPage("main");
			}
			bool running = Game.GameRunning;
			if (running && !_wasRunning && IsOpen && _page == "lobby")
			{
				Close();
			}
			_wasRunning = running;
		}

		public static UiCommand ModeCmd()
		{
			if (Mode == "endless")
			{
				return UiCommand.StartEndless;
			}
			if (Mode == "relax")
			{
				return UiCommand.StartRelax;
			}
			return UiCommand.StartStory;
		}

		private static int PortVal()
		{
			int p;
			if (!int.TryParse(_port.Trim(), out p) || p <= 0)
			{
				p = 27890;
			}
			return Mathf.Clamp(p, 1, 65535);
		}

		private static int MaxVal()
		{
			int m;
			if (!int.TryParse(_maxPlayers.Trim(), out m) || m < 2)
			{
				m = 4;
			}
			return Mathf.Clamp(m, 2, Proto.MaxPlayers);
		}

		private static void DoHostSteam()
		{
			if (!SteamNet.SteamReady)
			{
				Game.AddToast("Steam is not available");
				return;
			}
			if (Session.HostStart(PortVal(), true))
			{
				string name = _lobbyName.Trim();
				if (name.Length == 0)
				{
					name = "Kiosk Co-op";
				}
				SteamNet.CreateLobby(name, MaxVal(), _lobbyDesc, _lobbyPwd);
				Game.AddToast("Hosting via Steam - the lobby code is in the top right corner");
			}
		}

		private static void DoSteamInvite()
		{
			if (!SteamNet.InLobby || !SteamNet.IsLobbyOwner)
			{
				Game.AddToast("Host a Steam lobby first");
				return;
			}
			if (SteamNet.OpenInviteDialog())
			{
				Game.AddToast("Steam invite dialog opened");
			}
			else
			{
				Game.AddToast("Steam invite dialog failed");
			}
		}

		private static void DoHostLan()
		{
			if (Session.HostStart(PortVal(), false))
			{
				Game.AddToast("Hosting on port " + PortVal());
			}
		}

		private static void DoConnectTcp()
		{
			string host = _ip.Trim();
			if (host.Length == 0)
			{
				Game.AddToast("Enter the host IP");
				return;
			}
			Session.ClientConnectTcp(host, PortVal());
		}

		private static void DoJoinSteamCode()
		{
			ulong id;
			if (!ulong.TryParse(_codeSteam.Trim(), out id) || id == 0)
			{
				Game.AddToast("Invalid lobby code");
				return;
			}
			SteamNet.JoinPassword = _joinPwd;
			SteamNet.JoinLobby(id);
		}

		private static void DoFindLobbies()
		{
			if (!SteamNet.SteamReady)
			{
				Game.AddToast("Steam is not available");
				return;
			}
			if (SteamNet.RequestLobbyList())
			{
				_lobbyScroll = 0;
				_lastLobbyRefresh = Time.realtimeSinceStartup;
				Game.AddToast("Searching lobbies ...");
			}
		}

		private static void DoReadyToggle()
		{
			if (!Session.Active || Game.GameRunning || !Session.IsHost)
			{
				return;
			}
			Game.StartMpGame(ModeCmd());
		}

		private static void DoMode(string m)
		{
			Mode = m;
			RefreshDynamic();
		}

		private static void DoSelectCharacter(int index)
		{
			SelectedCharacter = index;
			SelectedSkin = 0;
			PlayerPrefs.SetInt("KSMP_Character", index);
			RefreshCards();
			try { Game.SendMyCustomize(); } catch (Exception e) {  }
		}

		private static void CopyCode()
		{
			if (SteamNet.InLobby)
			{
				GUIUtility.systemCopyBuffer = SteamNet.LobbyId.m_SteamID.ToString();
				Game.AddToast("Lobby code copied");
			}
		}

		private static void EnsureBuilt()
		{
			if (_root != null)
			{
				HookTextInput();
				EnsureEventSystem();
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

		public static void EnsureEventSystem()
		{
			if (EventSystem.current != null)
			{
				return;
			}
			GameObject es = new GameObject("KioskMP_EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
			
		}

		private static void BuildCanvas()
		{
			GameObject root = new GameObject("KioskMP_CanvasMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			Canvas canvas = root.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 900;
			CanvasScaler scaler = root.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1280f, 720f);
			scaler.matchWidthOrHeight = 0.5f;
			UnityEngine.Object.DontDestroyOnLoad(root);
			root.SetActive(false);
			_root = root;

			GameObject bg = new GameObject("screen", typeof(RectTransform), typeof(Image));
			RectTransform brt = bg.GetComponent<RectTransform>();
			brt.SetParent(root.transform, false);
			brt.anchorMin = Vector2.zero;
			brt.anchorMax = Vector2.one;
			brt.offsetMin = Vector2.zero;
			brt.offsetMax = Vector2.zero;
			bg.GetComponent<Image>().color = ScreenBg;

			Bar(brt, "topbar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), new Vector2(0f, 10f), Yellow);
			Bar(brt, "bottombar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 5f), new Vector2(0f, 10f), Yellow);

			GameObject logo = new GameObject("logo", typeof(RectTransform));
			RectTransform lrt = logo.GetComponent<RectTransform>();
			lrt.SetParent(brt, false);
			lrt.anchorMin = new Vector2(0f, 1f);
			lrt.anchorMax = new Vector2(0f, 1f);
			lrt.pivot = new Vector2(0f, 1f);
			lrt.anchoredPosition = new Vector2(40f, -16f);
			lrt.sizeDelta = new Vector2(500f, 74f);
			Text logoText = logo.AddComponent<Text>();
			logoText.font = UiFont();
			logoText.fontSize = 56;
			logoText.fontStyle = FontStyle.Bold;
			logoText.alignment = TextAnchor.UpperLeft;
			logoText.color = Yellow;
			logoText.text = "KSMP";
			logoText.supportRichText = false;

			GameObject status = new GameObject("status", typeof(RectTransform));
			RectTransform srt = status.GetComponent<RectTransform>();
			srt.SetParent(brt, false);
			srt.anchorMin = new Vector2(1f, 1f);
			srt.anchorMax = new Vector2(1f, 1f);
			srt.pivot = new Vector2(1f, 1f);
			srt.anchoredPosition = new Vector2(-40f, -22f);
			srt.sizeDelta = new Vector2(640f, 24f);
			_statusLabel = status.AddComponent<Text>();
			_statusLabel.font = UiFont();
			_statusLabel.fontSize = 15;
			_statusLabel.alignment = TextAnchor.UpperRight;
			_statusLabel.color = Grey;
			_statusLabel.supportRichText = false;
			Button statusBtn = status.AddComponent<Button>();
			statusBtn.targetGraphic = _statusLabel;
			statusBtn.onClick.AddListener(CopyCode);

			GameObject toasts = new GameObject("toasts", typeof(RectTransform));
			RectTransform trt = toasts.GetComponent<RectTransform>();
			trt.SetParent(brt, false);
			trt.anchorMin = new Vector2(0f, 0f);
			trt.anchorMax = new Vector2(0f, 0f);
			trt.pivot = new Vector2(0f, 0f);
			trt.anchoredPosition = new Vector2(40f, 34f);
			trt.sizeDelta = new Vector2(560f, 90f);
			_toastLabel = toasts.AddComponent<Text>();
			_toastLabel.font = UiFont();
			_toastLabel.fontSize = 15;
			_toastLabel.alignment = TextAnchor.LowerLeft;
			_toastLabel.color = Grey;
			_toastLabel.supportRichText = false;
			_toastLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
			_toastLabel.verticalOverflow = VerticalWrapMode.Overflow;

			_pageRoot = new GameObject("page", typeof(RectTransform)).GetComponent<RectTransform>();
			_pageRoot.SetParent(brt, false);
			_pageRoot.anchorMin = Vector2.zero;
			_pageRoot.anchorMax = Vector2.one;
			_pageRoot.offsetMin = new Vector2(40f, 70f);
			_pageRoot.offsetMax = new Vector2(-40f, -120f);

			SelectedCharacter = PlayerPrefs.GetInt("KSMP_Character", 0);
			SelectedSkin = 0;
			HookTextInput();
			EnsureEventSystem();
			_page = "";
			
		}

		private static Image Bar(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Color col)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			rt.anchorMin = aMin;
			rt.anchorMax = aMax;
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = pos;
			rt.sizeDelta = size;
			rt.GetComponent<Image>().color = col;
			return rt.GetComponent<Image>();
		}

		private static void ShowPage(string page)
		{
			if (_page == page && _pageRoot != null)
			{
				return;
			}
			_page = page;
			_fields.Clear();
			_focus = null;
			_lastListSig = "\0";
			_infoLine = null;
			_codeLine = null;
			_readyLabel = null;
			_readyBtn = null;
			_modeRow = null;
			_listRoot = null;
			_friendSlider = null;
			_friendSliderRoot = null;
			_lobbySlider = null;
			_lobbySliderRoot = null;
			_modeBtns.Clear();
			_modeLabels.Clear();
			_cards.Clear();
			if (_pageRoot != null)
			{
				UnityEngine.Object.Destroy(_pageRoot.gameObject);
			}
			_pageRoot = NewPageRoot();
			switch (page)
			{
				case "host":
					BuildHost(_pageRoot);
					break;
				case "steamhost":
					BuildSteamHost(_pageRoot);
					break;
				case "join":
					BuildJoin(_pageRoot);
					break;
				case "custom":
					BuildCustom(_pageRoot);
					break;
				case "lobby":
					BuildLobby(_pageRoot);
					break;
				default:
					BuildMain(_pageRoot);
					break;
			}
		}

		private static RectTransform NewPageRoot()
		{
			RectTransform rt = new GameObject("page_" + _page, typeof(RectTransform)).GetComponent<RectTransform>();
			rt.SetParent(_root.transform, false);
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = new Vector2(40f, 70f);
			rt.offsetMax = new Vector2(-40f, -120f);
			return rt;
		}

		private static void Title(Transform p, string text)
		{
			Label(p, text, 30, TextAnchor.UpperLeft, Yellow, 0f, 0f, 620f, 38f);
		}

		private static void BuildMain(Transform p)
		{
			Button(p, "HOST GAME", 0f, 24f, ContentW, 70f, delegate { ShowPage("host"); }, 30, Yellow, Black, "btnHost");
			Button(p, "JOIN GAME", 0f, 112f, ContentW, 70f, delegate { ShowPage("join"); }, 30, Yellow, Black, "btnJoin");
			Button(p, "CUSTOMIZATION", 0f, 200f, ContentW, 70f, delegate { ShowPage("custom"); }, 30, Panel, Yellow, "btnCustom");
		}

		private static void BuildHost(Transform p)
		{
			Title(p, "HOST GAME");
			Label(p, "LOBBY NAME", 15, TextAnchor.UpperLeft, Grey, 0f, 56f, 300f, 20f);
			MakeField(p, "lobbyName", delegate { return _lobbyName; }, delegate(string v) { _lobbyName = v; },
				"Kiosk Co-op", 0f, 82f, ContentW, 44f, 32, false);
			Label(p, "MAX PLAYERS", 15, TextAnchor.UpperLeft, Grey, 0f, 142f, 160f, 20f);
			MakeField(p, "max", delegate { return _maxPlayers; }, delegate(string v) { _maxPlayers = v; },
				"4", 0f, 168f, 110f, 44f, 2, true);
			Label(p, "TCP PORT", 15, TextAnchor.UpperLeft, Grey, 150f, 142f, 160f, 20f);
			MakeField(p, "port", delegate { return _port; }, delegate(string v) { _port = v; },
				"27890", 150f, 168f, 140f, 44f, 5, true);

			Button(p, "HOST VIA STEAM", 0f, 240f, ContentW, 56f,
				delegate { ShowPage("steamhost"); }, 22, Yellow, Black, "btnSteamPage");
			Button(p, "HOST ON LAN (TCP)", 0f, 314f, ContentW, 56f, DoHostLan, 22, Panel, White, "btnLan");
			Button(p, "BACK", 0f, 390f, 220f, 46f, delegate { ShowPage("main"); }, 18, Panel, Grey, "btnBack");
		}

		private static void BuildSteamHost(Transform p)
		{
			Title(p, "STEAM LOBBY");
			Label(p, "LOBBY NAME", 15, TextAnchor.UpperLeft, Grey, 0f, 56f, 300f, 20f);
			MakeField(p, "steamName", delegate { return _lobbyName; }, delegate(string v) { _lobbyName = v; },
				"Kiosk Co-op", 0f, 82f, ContentW, 40f, 32, false);
			Label(p, "DESCRIPTION", 15, TextAnchor.UpperLeft, Grey, 0f, 138f, 300f, 20f);
			MakeField(p, "desc", delegate { return _lobbyDesc; }, delegate(string v) { _lobbyDesc = v; },
				"co-op, all welcome", 0f, 164f, ContentW, 40f, 64, false);
			Label(p, "MAX PLAYERS", 15, TextAnchor.UpperLeft, Grey, 0f, 220f, 160f, 20f);
			MakeField(p, "steamMax", delegate { return _maxPlayers; }, delegate(string v) { _maxPlayers = v; },
				"4", 0f, 246f, 110f, 40f, 2, true);
			Label(p, "PASSWORD (PRIVATE)", 15, TextAnchor.UpperLeft, Grey, 150f, 220f, 260f, 20f);
			MakeField(p, "pwd", delegate { return _lobbyPwd; }, delegate(string v) { _lobbyPwd = v; },
				"empty = public", 150f, 246f, 370f, 40f, 32, false);

			bool steam = SteamNet.SteamReady;
			Button create = Button(p, "CREATE STEAM LOBBY", 0f, 312f, ContentW, 56f, DoHostSteam, 22, Yellow, Black, "btnSteamCreate");
			if (!steam)
			{
				create.interactable = false;
			}
			Label(p, steam ? "Steam ready as '" + Session.SelfName + "'." : "Steam is not running.",
				14, TextAnchor.UpperLeft, Grey, 0f, 378f, ContentW, 20f);
			Button(p, "BACK", 0f, 410f, 220f, 46f, delegate { ShowPage("host"); }, 18, Panel, Grey, "btnBack");
		}

		private static void BuildJoin(Transform p)
		{
			Title(p, "JOIN GAME");
			bool steam = SteamNet.SteamReady;
			Button find = Button(p, "SEARCH STEAM LOBBIES", 0f, 52f, 320f, 46f, DoFindLobbies, 18, Yellow, Black, "btnFind");
			if (!steam)
			{
				find.interactable = false;
			}
			Button refresh = Button(p, "REFRESH", 330f, 52f, 170f, 46f, DoFindLobbies, 18, Panel, Yellow, "btnRefresh");
			if (!steam)
			{
				refresh.interactable = false;
			}
			_listRoot = Rect(p, "list", 0f, 112f, new Vector2(760f, 300f), Color.clear);
			MakeLobbySlider(p, 0f, 10f);
			Label(p, "LOBBY CODE", 15, TextAnchor.UpperLeft, Grey, 0f, 420f, 300f, 20f);
			MakeField(p, "code", delegate { return _codeSteam; }, delegate(string v) { _codeSteam = v; },
				"friend's code", 0f, 446f, 220f, 44f, 30, false);
			MakeField(p, "joinPwd", delegate { return _joinPwd; }, delegate(string v) { _joinPwd = v; },
				"password", 230f, 446f, 150f, 44f, 24, false);
			Button(p, "JOIN BY CODE", 390f, 446f, 170f, 44f, DoJoinSteamCode, 18, Yellow, Black, "btnCode");
			Label(p, "LAN (DIRECT IP)", 15, TextAnchor.UpperLeft, Grey, 0f, 506f, 300f, 20f);
			MakeField(p, "ip", delegate { return _ip; }, delegate(string v) { _ip = v; },
				"127.0.0.1", 0f, 532f, 240f, 44f, 40, false);
			MakeField(p, "port2", delegate { return _port; }, delegate(string v) { _port = v; },
				"27890", 256f, 532f, 110f, 44f, 5, true);
			Button(p, "CONNECT", 382f, 532f, 180f, 44f, DoConnectTcp, 18, Panel, White, "btnConnect");
			Button(p, "BACK", 0f, 596f, 220f, 42f, delegate { ShowPage("main"); }, 18, Panel, Grey, "btnBack");
		}

		private static void BuildCustom(Transform p)
		{
			Title(p, "CUSTOMIZATION");
			for (int i = 0; i < 4; i++)
			{
				MakeCard(p, i, i * 230f, 60f, 210f, 250f);
			}
			Button(p, "BACK", 0f, 466f, 220f, 46f, delegate { ShowPage("main"); }, 18, Panel, Grey, "btnBack");
			RefreshCards();
		}

		private static void MakeCard(Transform parent, int index, float x, float yTop, float w, float h)
		{
			RectTransform rt = Rect(parent, "card_" + index, x, yTop, new Vector2(w, h), Panel);
			Button b = rt.gameObject.AddComponent<Button>();
			b.targetGraphic = rt.GetComponent<Image>();
			ColorBlock cb = b.colors;
			cb.highlightedColor = Shade(Panel, 1.6f);
			cb.pressedColor = Shade(Panel, 0.8f);
			cb.fadeDuration = 0.05f;
			b.colors = cb;
			int idx = index;
			b.onClick.AddListener(delegate
			{
				try { DoSelectCharacter(idx); }
				catch (Exception e) {  }
			});

			GameObject numGo = new GameObject("num", typeof(RectTransform));
			RectTransform nrt = numGo.GetComponent<RectTransform>();
			nrt.SetParent(rt, false);
			nrt.anchorMin = new Vector2(0f, 0f);
			nrt.anchorMax = new Vector2(1f, 1f);
			nrt.offsetMin = new Vector2(10f, 46f);
			nrt.offsetMax = new Vector2(-10f, -76f);
			Text num = numGo.AddComponent<Text>();
			num.font = UiFont();
			num.fontSize = 62;
			num.fontStyle = FontStyle.Bold;
			num.alignment = TextAnchor.MiddleCenter;
			num.text = (index + 1).ToString();
			num.supportRichText = false;

			GameObject titleGo = new GameObject("title", typeof(RectTransform));
			RectTransform trt = titleGo.GetComponent<RectTransform>();
			trt.SetParent(rt, false);
			trt.anchorMin = new Vector2(0f, 1f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.offsetMin = new Vector2(12f, -66f);
			trt.offsetMax = new Vector2(-12f, -18f);
			Text title = titleGo.AddComponent<Text>();
			title.font = UiFont();
			title.fontSize = 22;
			title.fontStyle = FontStyle.Bold;
			title.alignment = TextAnchor.UpperLeft;
			title.text = "PEOPLE " + (index + 1);
			title.supportRichText = false;

			GameObject subGo = new GameObject("sub", typeof(RectTransform));
			RectTransform srt = subGo.GetComponent<RectTransform>();
			srt.SetParent(rt, false);
			srt.anchorMin = new Vector2(0f, 0f);
			srt.anchorMax = new Vector2(1f, 0f);
			srt.offsetMin = new Vector2(12f, 14f);
			srt.offsetMax = new Vector2(-12f, 38f);
			Text sub = subGo.AddComponent<Text>();
			sub.font = UiFont();
			sub.fontSize = 14;
			sub.alignment = TextAnchor.LowerLeft;
			sub.text = "TAP TO SELECT";
			sub.supportRichText = false;

			Card c = new Card();
			c.bg = rt.GetComponent<Image>();
			c.title = title;
			c.sub = sub;
			c.num = num;
			_cards.Add(c);
		}

		private static void BuildLobby(Transform p)
		{
			Title(p, "LOBBY");
			bool host = Session.IsHost;
			bool running = Game.GameRunning;
			_codeLine = null;
			_listRoot = Rect(p, "list", 0f, 88f, new Vector2(760f, 190f), Color.clear);
			_friendListTop = 88f;
			MakeFriendSlider(p, 0f, 10f);

			if (host && !running)
			{
				Label(p, "GAME MODE", 15, TextAnchor.UpperLeft, Grey, 0f, 290f, 200f, 20f);
				_modeRow = Rect(p, "modeRow", 0f, 314f, new Vector2(560f, 46f), Color.clear).gameObject;
				_modeBtns["story"] = ModeBtn("story", "STORY", 0f);
				_modeBtns["endless"] = ModeBtn("endless", "ENDLESS", 190f);
				_modeBtns["relax"] = ModeBtn("relax", "RELAX", 380f);
				_readyBtn = Button(p, "START GAME", 0f, 376f, 760f, 60f, DoReadyToggle, 24, Yellow, Black, "btnReady").gameObject;
				_readyLabel = _readyBtn.GetComponentInChildren<Text>();
			}
			else if (!running)
			{
				Label(p, "WAITING FOR THE HOST", 20, TextAnchor.UpperLeft, Grey, 0f, 330f, 760f, 30f);
			}

			_infoLine = Label(p, "", 16, TextAnchor.UpperLeft, Grey, 0f, 446f, 760f, 22f);
			Button(p, "DISCONNECT", 0f, 478f, 360f, 46f, delegate { Session.Disconnect(); }, 18, Panel, White, "btnDisconnect");
			Button(p, "CLOSE", 380f, 478f, 380f, 46f, Close, 18, Panel, Grey, "btnClose");
			RefreshDynamic();
		}

		private static Image ModeBtn(string key, string label, float x)
		{
			Button b = Button(_modeRow.transform, label, x, 0f, 170f, 46f, delegate { DoMode(key); }, 18, Panel, Yellow, "mode_" + key);
			_modeLabels[key] = b.GetComponentInChildren<Text>();
			return b.GetComponent<Image>();
		}

		private static string ComputeSig()
		{
			StringBuilder sb = new StringBuilder(160);
			sb.Append(_page).Append('|');
			sb.Append(Session.Active ? 'A' : 'O').Append(Session.IsHost ? 'H' : 'C');
			sb.Append(Game.GameRunning ? 'R' : '-').Append('|');
			sb.Append(SteamNet.SteamReady ? 'S' : 's').Append('|');
			if (_page == "join")
			{
				foreach (LobbyInfo li in SteamNet.LobbyResults)
				{
					sb.Append(li.id).Append(',').Append(li.members).Append('/').Append(li.maxMembers)
						.Append(',').Append(li.name).Append(',').Append(li.desc).Append(';');
				}
				sb.Append('|').Append(_lobbyScroll);
			}
			else if (_page == "lobby")
			{
				foreach (PeerInfo pi in Session.PeerList())
				{
					sb.Append(pi.id).Append(':').Append(pi.name).Append(':')
						.Append(Mathf.RoundToInt(pi.rtt / 50f) * 50).Append(';');
				}
				sb.Append('|');
				List<int> ready = new List<int>(Game.LobbyReadyIds);
				ready.Sort();
				foreach (int id in ready)
				{
					sb.Append(id).Append(';');
				}
				sb.Append('|');
				sb.Append(SteamNet.InLobby ? SteamNet.LobbyId.m_SteamID.ToString() : "");
				sb.Append('|');
				foreach (KeyValuePair<ulong, string> f in SteamNet.FriendList)
				{
					sb.Append(f.Key).Append(';');
				}
				sb.Append('|').Append(_friendScroll);
			}
			return sb.ToString();
		}

		private static void RefreshList()
		{
			if (_listRoot == null)
			{
				return;
			}
			string sig = ComputeSig();
			if (sig == _lastListSig)
			{
				return;
			}
			_lastListSig = sig;
			for (int i = _listRoot.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(_listRoot.GetChild(i).gameObject);
			}
			if (_page == "join")
			{
				BuildJoinRows();
			}
			else if (_page == "lobby")
			{
				BuildPlayerRows();
			}
		}

		private static void PlaceFriendSlider(float y, float rows)
		{
			if (_friendSliderRoot == null)
			{
				return;
			}
			float h = rows * 30f;
			_friendSliderRoot.anchoredPosition = new Vector2(745f, -(_friendListTop + y + h * 0.5f));
			_friendSliderRoot.sizeDelta = new Vector2(20f, h);
		}

		private static Slider MakeScrollSlider(Transform parent, string name, float x, float y, float w, float h,
			out RectTransform root, UnityEngine.Events.UnityAction<float> onChange)
		{
			root = Rect(parent, name, x, y, new Vector2(w, h), Color.clear);
			GameObject track = new GameObject("track", typeof(RectTransform), typeof(Image));
			RectTransform tRt = track.GetComponent<RectTransform>();
			tRt.SetParent(root, false);
			tRt.anchorMin = Vector2.zero;
			tRt.anchorMax = Vector2.one;
			tRt.pivot = new Vector2(0.5f, 0.5f);
			tRt.offsetMin = new Vector2(8f, 8f);
			tRt.offsetMax = new Vector2(-8f, -8f);
			Image trackImg = track.GetComponent<Image>();
			trackImg.color = Panel;
			trackImg.raycastTarget = true;

			GameObject area = new GameObject("handleArea", typeof(RectTransform));
			RectTransform aRt = area.GetComponent<RectTransform>();
			aRt.SetParent(root, false);
			aRt.anchorMin = Vector2.zero;
			aRt.anchorMax = Vector2.one;
			aRt.offsetMin = new Vector2(2f, 4f);
			aRt.offsetMax = new Vector2(-2f, -4f);

			GameObject handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
			RectTransform hRt = handle.GetComponent<RectTransform>();
			hRt.SetParent(aRt, false);
			hRt.anchorMin = Vector2.zero;
			hRt.anchorMax = Vector2.one;
			hRt.pivot = new Vector2(0.5f, 0.5f);
			hRt.sizeDelta = new Vector2(20f, 30f);
			Image hImg = handle.GetComponent<Image>();
			hImg.color = Yellow;
			hImg.raycastTarget = true;

			Slider s = root.gameObject.AddComponent<Slider>();
			s.direction = Slider.Direction.TopToBottom;
			s.minValue = 0f;
			s.maxValue = 1f;
			s.wholeNumbers = false;
			s.handleRect = hRt;
			s.targetGraphic = hImg;
			ColorBlock cb = s.colors;
			cb.normalColor = Yellow;
			cb.highlightedColor = White;
			cb.pressedColor = Shade(Yellow, 0.75f);
			cb.selectedColor = Yellow;
			cb.disabledColor = Grey;
			s.colors = cb;
			s.onValueChanged.AddListener(onChange);
			root.gameObject.SetActive(false);
			return s;
		}

		private static void MakeFriendSlider(Transform parent, float y, float h)
		{
			_friendSlider = MakeScrollSlider(parent, "friendScroll", 730f, y, 24f, h,
				out _friendSliderRoot, delegate(float v)
				{
					if (_sliderSetting)
					{
						return;
					}
					_friendScroll = Mathf.RoundToInt(v * _friendScrollMax);
					ClampFriendScroll();
					_lastListSig = "\0";
				});
		}

		private static void MakeLobbySlider(Transform parent, float y, float h)
		{
			_lobbySlider = MakeScrollSlider(parent, "lobbyScroll", 730f, y, 24f, h,
				out _lobbySliderRoot, delegate(float v)
				{
					if (_sliderSetting)
					{
						return;
					}
					_lobbyScroll = Mathf.RoundToInt(v * _lobbyScrollMax);
					ClampLobbyScroll();
					_lastListSig = "\0";
				});
		}

		private static void ClampLobbyScroll()
		{
			if (_lobbyScroll < 0)
			{
				_lobbyScroll = 0;
			}
			if (_lobbyScroll > _lobbyScrollMax)
			{
				_lobbyScroll = _lobbyScrollMax;
			}
		}

		private static void ClampFriendScroll()
		{
			if (_friendScroll < 0)
			{
				_friendScroll = 0;
			}
			if (_friendScroll > _friendScrollMax)
			{
				_friendScroll = _friendScrollMax;
			}
		}

		private static void FriendWheelTick()
		{
			if (_page != "lobby" && _page != "join")
			{
				return;
			}
			float wheel;
			try { wheel = Input.mouseScrollDelta.y; }
			catch { return; }
			if (wheel > 0.01f)
			{
				if (_page == "lobby") _friendScroll--; else _lobbyScroll--;
			}
			else if (wheel < -0.01f)
			{
				if (_page == "lobby") _friendScroll++; else _lobbyScroll++;
			}
			else
			{
				return;
			}
			if (_page == "lobby")
			{
				ClampFriendScroll();
			}
			else
			{
				ClampLobbyScroll();
			}
		}

		private static float BuildFriendRows(float y, float bottom)
		{
			int total = SteamNet.FriendList.Count;
			if (_friendSliderRoot != null)
			{
				_friendSliderRoot.gameObject.SetActive(false);
			}
			if (total == 0)
			{
				return y;
			}
			Label(_listRoot, "STEAM FRIENDS  (" + total + ")", 14, TextAnchor.UpperLeft, Grey, 0f, y, 540f, 22f);
			y += 24f;
			int rows = Mathf.Max(0, Mathf.FloorToInt((bottom - y) / 30f));
			if (rows == 0)
			{
				return y;
			}
			_friendScrollMax = Mathf.Max(0, total - rows);
			ClampFriendScroll();
			int first = _friendScroll;
			int last = Mathf.Min(total, first + rows);
			for (int i = first; i < last; i++)
			{
				KeyValuePair<ulong, string> f = SteamNet.FriendList[i];
				float rowY = y + (i - first) * 30f;
				ulong fid = f.Key;
				string fname = string.IsNullOrEmpty(f.Value) ? ("player " + f.Key) : f.Value;
				Label(_listRoot, fname, 17, TextAnchor.UpperLeft, White, 0f, rowY, 540f, 28f);
				Button(_listRoot, "INVITE", 600f, rowY, 110f, 28f, delegate
				{
					if (SteamNet.Invite(fid))
					{
						Game.AddToast("Invite sent to " + fname);
					}
				}, 14, Panel, Yellow, "friendInviteRow");
			}
			if (_friendScrollMax > 0 && _friendSlider != null && _friendSliderRoot != null)
			{
				_friendSliderRoot.gameObject.SetActive(true);
				PlaceFriendSlider(y, rows);
				_sliderSetting = true;
				_friendSlider.value = (float)_friendScroll / _friendScrollMax;
				_sliderSetting = false;
			}
			return y + rows * 30f;
		}

		private static void BuildJoinRows()
		{
			float y = 0f;
			if (_lobbySliderRoot != null)
			{
				_lobbySliderRoot.gameObject.SetActive(false);
			}
			if (!SteamNet.SteamReady)
			{
				Label(_listRoot, "Steam is not available - use the lobby code or LAN.", 15,
					TextAnchor.UpperLeft, Grey, 0f, y, 760f, 30f);
				y += 30f;
			}
			else if (SteamNet.LobbyResults.Count == 0)
			{
				Label(_listRoot, "NO PUBLIC LOBBIES", 15,
					TextAnchor.UpperLeft, Grey, 0f, y, 760f, 30f);
				y += 30f;
			}
			else
			{
				int total = SteamNet.LobbyResults.Count;
				int rows = Mathf.Min(7, total);
				_lobbyScrollMax = Mathf.Max(0, total - rows);
				ClampLobbyScroll();
				for (int i = 0; i < rows; i++)
				{
					LobbyInfo li = SteamNet.LobbyResults[_lobbyScroll + i];
					float rowY = y + i * 36f;
					ulong id = li.id;
					bool full = li.maxMembers > 0 && li.members >= li.maxMembers;
					Label(_listRoot, li.name, 17, TextAnchor.UpperLeft, White, 0f, rowY, 560f, 20f);
					string sub = (string.IsNullOrEmpty(li.desc) ? "" : li.desc + "  |  ")
						+ (string.IsNullOrEmpty(li.host) ? "host ?" : li.host)
						+ "  |  " + li.members + "/" + li.maxMembers + (full ? "  FULL" : "");
					Label(_listRoot, sub, 13, TextAnchor.UpperLeft, Grey, 0f, rowY + 18f, 570f, 18f);
					Button joinBtn = Button(_listRoot, "JOIN", 600f, rowY + 1f, 110f, 28f, delegate
					{
						SteamNet.JoinPassword = "";
						SteamNet.JoinLobby(id);
					}, 14, Yellow, Black, "joinRow");
					joinBtn.interactable = !full;
				}
				if (_lobbyScrollMax > 0 && _lobbySlider != null && _lobbySliderRoot != null)
				{
					_lobbySliderRoot.gameObject.SetActive(true);
					_lobbySliderRoot.anchoredPosition = new Vector2(745f, -(112f + y + rows * 18f));
					_lobbySliderRoot.sizeDelta = new Vector2(20f, rows * 36f);
					_sliderSetting = true;
					_lobbySlider.value = (float)_lobbyScroll / _lobbyScrollMax;
					_sliderSetting = false;
				}
			}
		}

		private static void BuildPlayerRows()
		{
			float y = 0f;
			AddPlayerRow(y, Session.SelfName, Session.SelfId, Session.IsHost, Game.IsSelfReady(), true);
			y += 34f;
			foreach (PeerInfo pi in Session.PeerList())
			{
				AddPlayerRow(y, pi.name, pi.id, false, Game.IsPeerReady(pi.id), false);
				y += 34f;
			}
			if (Session.IsHost && SteamNet.InLobby)
			{
				BuildFriendRows(y + 6f, 190f);
			}
		}

		private static void AddPlayerRow(float y, string name, int id, bool isHost, bool ready, bool self)
		{
			string tag = isHost ? "HOST" : "";
			string rtt = "";
			PeerInfo info;
			if (!self && Session.Peers.TryGetValue(id, out info) && info.rtt > 0f)
			{
				rtt = string.Format("  {0:F0} ms", info.rtt);
			}
			Label(_listRoot, name + (self ? "  (YOU)" : ""), 18, TextAnchor.UpperLeft, White, 0f, y, 420f, 30f);
			bool canKick = Session.IsHost && !self;
			Label(_listRoot, tag + rtt, 15, TextAnchor.UpperRight, Grey, 420f, y + 2f,
				canKick ? 150f : 340f, 28f);
			if (canKick)
			{
				Button(_listRoot, "KICK", 580f, y, 130f, 30f, delegate
				{
					Session.KickPeer(id, "removed by host");
				}, 14, Panel, Yellow, "kickRow");
			}
		}

		private static string StatusString()
		{
			if (Session.Active && SteamNet.InLobby)
			{
				return "CODE  " + SteamNet.LobbyId.m_SteamID;
			}
			if (!Session.Active)
			{
				return string.IsNullOrEmpty(Session.StatusText) ? "offline" : Session.StatusText;
			}
			return "";
		}

		private static void RefreshDynamic()
		{
			if (_statusLabel != null)
			{
				_statusLabel.text = StatusString();
				_statusLabel.color = _statusLabel.text.StartsWith("CODE") ? Yellow : Grey;
			}
			if (_infoLine != null)
			{
				_infoLine.text = Game.GameRunning ? "" : (Session.Active ? Session.Peers.Count + 1 + " PLAYER(S)" : "");
			}
			if (_readyLabel != null && _readyBtn != null && _readyBtn.activeSelf)
			{
				_readyLabel.text = "START GAME";
			}
			if (_codeLine != null && SteamNet.InLobby)
			{
				_codeLine.text = "CODE  " + SteamNet.LobbyId.m_SteamID;
			}
			foreach (KeyValuePair<string, Image> kv in _modeBtns)
			{
				bool sel = Mode == kv.Key;
				if (kv.Value != null)
				{
					kv.Value.color = sel ? Yellow : Panel;
				}
				Text lt;
				if (_modeLabels.TryGetValue(kv.Key, out lt) && lt != null)
				{
					lt.color = sel ? Black : Yellow;
				}
			}
			RefreshCards();
		}

		private static void RefreshCards()
		{
			for (int i = 0; i < _cards.Count; i++)
			{
				Card c = _cards[i];
				if (c == null || c.bg == null)
				{
					continue;
				}
				bool sel = SelectedCharacter == i;
				c.bg.color = sel ? Yellow : Panel;
				if (c.title != null)
				{
					c.title.color = sel ? Black : Yellow;
				}
				if (c.sub != null)
				{
					c.sub.text = sel ? "SELECTED" : "TAP TO SELECT";
					c.sub.color = sel ? Shade(Black, 2.2f) : Grey;
				}
				if (c.num != null)
				{
					c.num.color = sel ? new Color(0f, 0f, 0f, 0.25f) : new Color(1f, 1f, 1f, 0.1f);
				}
			}
		}

		private static void RefreshToasts()
		{
			if (_toastLabel == null)
			{
				return;
			}
			List<string> tl = Game.ToastLog;
			StringBuilder sb = new StringBuilder();
			int start = Mathf.Max(0, tl.Count - 4);
			for (int i = start; i < tl.Count; i++)
			{
				if (sb.Length > 0)
				{
					sb.Append('\n');
				}
				sb.Append(tl[i]);
			}
			_toastLabel.text = sb.ToString();
		}

		private static RectTransform Rect(Transform parent, string name, float x, float yTop, Vector2 size, Color bg)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = new Vector2(x + size.x * 0.5f, -(yTop + size.y * 0.5f));
			rt.sizeDelta = size;
			if (bg.a > 0f)
			{
				Image img = go.AddComponent<Image>();
				img.color = bg;
			}
			return rt;
		}

		private static Text Label(Transform parent, string text, int size, TextAnchor anchor, Color col,
			float x, float yTop, float w, float h)
		{
			RectTransform rt = Rect(parent, "txt", x, yTop, new Vector2(w, h), Color.clear);
			Text t = rt.gameObject.AddComponent<Text>();
			t.font = UiFont();
			t.fontSize = size;
			t.alignment = anchor;
			t.color = col;
			t.text = text;
			t.supportRichText = false;
			t.horizontalOverflow = HorizontalWrapMode.Wrap;
			t.verticalOverflow = VerticalWrapMode.Overflow;
			return t;
		}

		private static Button Button(Transform parent, string text, float x, float yTop, float w, float h,
			Action onClick, int size = 20, Color? bgCol = null, Color? fgCol = null, string name = "btn")
		{
			Color bgc = bgCol ?? Yellow;
			RectTransform rt = Rect(parent, name, x, yTop, new Vector2(w, h), bgc);
			GameObject tgo = new GameObject("label", typeof(RectTransform));
			RectTransform trt = tgo.GetComponent<RectTransform>();
			trt.SetParent(rt, false);
			trt.anchorMin = Vector2.zero;
			trt.anchorMax = Vector2.one;
			trt.offsetMin = new Vector2(6f, 0f);
			trt.offsetMax = new Vector2(-6f, 0f);
			Text t = tgo.AddComponent<Text>();
			t.font = UiFont();
			t.fontSize = size;
			t.fontStyle = FontStyle.Bold;
			t.alignment = TextAnchor.MiddleCenter;
			t.color = fgCol ?? White;
			t.text = text;
			t.supportRichText = false;
			Button b = rt.gameObject.AddComponent<Button>();
			b.targetGraphic = rt.gameObject.GetComponent<Image>();
			ColorBlock cb = b.colors;
			cb.highlightedColor = Shade(bgc, 1.22f);
			cb.pressedColor = Shade(bgc, 0.8f);
			cb.disabledColor = new Color(0.22f, 0.22f, 0.23f, 1f);
			cb.fadeDuration = 0.05f;
			b.colors = cb;
			b.onClick.AddListener(delegate
			{
				try { onClick(); }
				catch (Exception e)
				{
					
				}
			});
			return b;
		}

		private static void MakeField(Transform parent, string name, Func<string> get, Action<string> set,
			string placeholder, float x, float yTop, float w, float h, int maxLen, bool digits)
		{
			RectTransform rt = Rect(parent, "field_" + name, x, yTop, new Vector2(w, h), FieldBg);
			GameObject tgo = new GameObject("text", typeof(RectTransform));
			RectTransform trt = tgo.GetComponent<RectTransform>();
			trt.SetParent(rt, false);
			trt.anchorMin = Vector2.zero;
			trt.anchorMax = Vector2.one;
			trt.offsetMin = new Vector2(10f, 0f);
			trt.offsetMax = new Vector2(-10f, 0f);
			Text t = tgo.AddComponent<Text>();
			t.font = UiFont();
			t.fontSize = 16;
			t.alignment = TextAnchor.MiddleLeft;
			t.color = White;
			t.text = "";
			t.supportRichText = false;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Overflow;

			Field f = new Field();
			f.name = name;
			f.rect = rt;
			f.bg = rt.GetComponent<Image>();
			f.label = t;
			f.get = get;
			f.set = set;
			f.placeholder = placeholder;
			f.maxLen = maxLen;
			f.digitsOnly = digits;
			Button b = rt.gameObject.AddComponent<Button>();
			b.targetGraphic = f.bg;
			b.transition = Selectable.Transition.ColorTint;
			ColorBlock cb = b.colors;
			cb.highlightedColor = FieldFocus;
			cb.pressedColor = FieldFocus;
			cb.fadeDuration = 0.05f;
			b.colors = cb;
			b.onClick.AddListener(delegate
			{
				_focus = f;
				if (_pendingFrame == Time.frameCount && _pendingChar >= ' ')
				{
					char pc = _pendingChar;
					_pendingChar = '\0';
					_pendingFrame = -1;
					string pv = f.get();
					if (pv.Length < f.maxLen && (!f.digitsOnly || (pc >= '0' && pc <= '9')))
					{
						f.set(pv + pc.ToString());
					}
				}
			});
			_fields.Add(f);
		}

		private static void HookTextInput()
		{
			if (_textInputHooked)
			{
				return;
			}
			Keyboard kb = Keyboard.current;
			if (kb == null)
			{
				return;
			}
			kb.onTextInput += OnTextInputChar;
			_textInputHooked = true;
		}

		private static void OnTextInputChar(char c)
		{
			Keyboard kbNow = Keyboard.current;
			bool ctrlNow = kbNow != null && (kbNow.leftCtrlKey.isPressed || kbNow.rightCtrlKey.isPressed);
			Field f = _focus;
			if (f == null)
			{
				if (!ctrlNow && c >= ' ')
				{
					_pendingChar = c;
					_pendingFrame = Time.frameCount;
				}
				return;
			}
			if (c < ' ')
			{
				return;
			}
			if (ctrlNow)
			{
				return;
			}
			int frame = Time.frameCount;
			if (frame == _lastCharFrame && c == _lastChar)
			{
				return;
			}
			_lastCharFrame = frame;
			_lastChar = c;
			string v = f.get();
			if (v.Length >= f.maxLen)
			{
				return;
			}
			if (f.digitsOnly && (c < '0' || c > '9'))
			{
				return;
			}
			f.set(v + c);
		}

		private static void AppendToFocus(string text)
		{
			Field f = _focus;
			if (f == null)
			{
				return;
			}
			StringBuilder ok = new StringBuilder();
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c < ' ' || c == '\r' || c == '\n')
				{
					continue;
				}
				if (f.digitsOnly && (c < '0' || c > '9'))
				{
					continue;
				}
				ok.Append(c);
			}
			string v = f.get();
			int room = f.maxLen - v.Length;
			if (room <= 0)
			{
				return;
			}
			if (ok.Length > room)
			{
				ok.Length = room;
			}
			f.set(v + ok.ToString());
		}

		private static void KeyboardTick()
		{
			if (_focus == null)
			{
				return;
			}
			Keyboard kb = Keyboard.current;
			if (kb == null)
			{
				return;
			}
			bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
			if (ctrl && kb.vKey.wasPressedThisFrame)
			{
				string clip = GUIUtility.systemCopyBuffer;
				if (!string.IsNullOrEmpty(clip))
				{
					AppendToFocus(clip);
				}
			}
			if (ctrl && kb.cKey.wasPressedThisFrame)
			{
				string v = _focus.get();
				if (v.Length > 0)
				{
					GUIUtility.systemCopyBuffer = v;
				}
			}
			if (ctrl && kb.xKey.wasPressedThisFrame)
			{
				string v = _focus.get();
				if (v.Length > 0)
				{
					GUIUtility.systemCopyBuffer = v;
					_focus.set("");
				}
			}
			if (kb.backspaceKey.wasPressedThisFrame)
			{
				string v = _focus.get();
				if (v.Length > 0)
				{
					_focus.set(v.Substring(0, v.Length - 1));
				}
			}
			if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
			{
				
				_focus = null;
			}
		}

		private static void RefreshFields()
		{
			for (int i = 0; i < _fields.Count; i++)
			{
				Field f = _fields[i];
				if (f == null || f.label == null)
				{
					continue;
				}
				string v = f.get();
				if (_focus == f)
				{
					f.label.text = v + "|";
					f.label.color = White;
					if (f.bg != null)
					{
						f.bg.color = FieldFocus;
					}
				}
				else if (v.Length == 0)
				{
					f.label.text = f.placeholder;
					f.label.color = Grey;
					if (f.bg != null)
					{
						f.bg.color = FieldBg;
					}
				}
				else
				{
					f.label.text = v;
					f.label.color = White;
					if (f.bg != null)
					{
						f.bg.color = FieldBg;
					}
				}
			}
		}

		private static Color Shade(Color c, float f)
		{
			return new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);
		}

		public static Font UiFont()
		{
			if (_fontTried)
			{
				return _font;
			}
			_fontTried = true;
			try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
			if (_font == null)
			{
				try
				{
					LanguageController lc = LanguageController.instance;
					if (lc != null)
					{
						_font = lc.GetCurrentFontUnityMarker();
						if (_font == null)
						{
							_font = lc.GetCurrentFontUnity(true);
						}
					}
				}
				catch (Exception e)
				{
					
				}
			}
			if (_font == null)
			{
				
			}
			return _font;
		}
	}
}
