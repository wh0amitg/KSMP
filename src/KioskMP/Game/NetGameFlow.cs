using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KioskMP
{

	public static partial class Game
	{
		private static CustomerSpawnInfo _pendingHostSpawn;
		private static int _pendingHostSpawnFrame;
		private static CustomerSpawnInfo _queuedCust;
		private static int _queuedCustFrame;
		private static bool _boardDirty;

		private static readonly AccessTools.FieldRef<CustomersController, Customer> _currentCustomerRef =
			AccessTools.FieldRefAccess<CustomersController, Customer>("currentCustomer");

		private static List<OrderLine> ToLines(List<Customer.OrdedItemNew> src)
		{
			List<OrderLine> list = new List<OrderLine>();
			if (src == null)
			{
				return list;
			}
			for (int i = 0; i < src.Count; i++)
			{
				OrderLine l = new OrderLine();
				l.objectId = src[i].objectID;
				l.done = src[i].orderDone;
				l.additions.AddRange(src[i].additions);
				list.Add(l);
			}
			return list;
		}

		private static List<Customer.OrdedItemNew> FromLines(List<OrderLine> src)
		{
			List<Customer.OrdedItemNew> list = new List<Customer.OrdedItemNew>();
			if (src == null)
			{
				return list;
			}
			for (int i = 0; i < src.Count; i++)
			{
				Customer.OrdedItemNew o = new Customer.OrdedItemNew();
				o.objectID = src[i].objectId;
				o.orderDone = src[i].done;
				o.additions.AddRange(src[i].additions);
				list.Add(o);
			}
			return list;
		}

		private static bool SameLines(List<OrderLine> a, List<OrderLine> b)
		{
			if (a == null || b == null)
			{
				return a == b;
			}
			if (a.Count != b.Count)
			{
				return false;
			}
			for (int i = 0; i < a.Count; i++)
			{
				if (a[i].objectId != b[i].objectId || a[i].additions.Count != b[i].additions.Count)
				{
					return false;
				}
				for (int j = 0; j < a[i].additions.Count; j++)
				{
					if (a[i].additions[j] != b[i].additions[j])
					{
						return false;
					}
				}
			}
			return true;
		}

		private static CustomerSpawnInfo CaptureCustomer()
		{
			CustomersController cc = CustomersController.instance;
			if (cc == null)
			{
				return null;
			}
			Customer c = cc.GetCurrentCustomer();
			if (c == null)
			{
				return null;
			}
			GameManager g = GameManager.instance;
			CustomerSpawnInfo info = new CustomerSpawnInfo();
			info.gameMode = g != null ? (byte)g.gameMode : (byte)0;
			info.day = g != null ? g.currentDay : 0;
			info.customerIdx = g != null ? g.currentCustomer : 0;
			bool endless = g != null && g.isEndlessOrRelaxMode();
			info.prefabKind = endless ? (byte)1 : (byte)0;
			info.prefabIndex = 0;
			if (endless)
			{
				for (int i = 0; i < cc.allCustomers.Length; i++)
				{
					if (cc.allCustomers[i] == c || cc.allCustomers[i].gameObject.name == c.gameObject.name)
					{
						info.prefabIndex = i;
						break;
					}
				}
			}
			else if (g != null && g.currentDay < cc.days.Length
				&& g.currentCustomer < cc.days[g.currentDay].customerPrefabs.Length)
			{
				info.prefabIndex = g.currentCustomer;
			}
			info.applyOrder = endless;
			info.ordered = (bool)AccessTools.Field(typeof(Customer), "ordered").GetValue(c);
			info.order = ToLines(c.endlessOrder);
			return info;
		}

		public static void NotifyCustomerSpawned()
		{
			if (!Session.Active || !Session.IsHost)
			{
				return;
			}
			_pendingHostSpawn = CaptureCustomer();
			_pendingHostSpawnFrame = Time.frameCount + 2;
		}

		private static void ApplyCustomerSpawn(CustomerSpawnInfo info)
		{
			CustomersController cc = CustomersController.instance;
			GameManager g = GameManager.instance;
			if (cc == null || g == null || info == null)
			{
				return;
			}
			try
			{
				cc.DestroyCurrentCustomer();

				Customer[] existing = Resources.FindObjectsOfTypeAll<Customer>();
				for (int i = 0; i < existing.Length; i++)
				{
					Customer old = existing[i];
					if (old != null && old.gameObject.scene.IsValid())
					{
						UnityEngine.Object.Destroy(old.gameObject);
					}
				}
				Customer c;
				if (info.prefabKind == 1)
				{
					if (info.prefabIndex < 0 || info.prefabIndex >= cc.allCustomers.Length)
					{
						return;
					}
					c = UnityEngine.Object.Instantiate(cc.allCustomers[info.prefabIndex], cc.startPos.position, Quaternion.identity);
				}
				else
				{
					if (info.day < 0 || info.day >= cc.days.Length)
					{
						return;
					}
					Customer[] arr = cc.days[info.day].customerPrefabs;
					if (info.prefabIndex < 0 || info.prefabIndex >= arr.Length)
					{
						return;
					}
					c = UnityEngine.Object.Instantiate(arr[info.prefabIndex], cc.startPos.position, Quaternion.identity);
				}
				c.UpdateDestination(cc.kioskPos);
				_currentCustomerRef(cc) = c;

				_queuedCust = info;
				_queuedCustFrame = Time.frameCount + 2;
			}
			catch (Exception e)
			{
				
			}
		}

		private static void FinalizeCustomerSpawn(CustomerSpawnInfo info)
		{
			CustomersController cc = CustomersController.instance;
			if (cc == null)
			{
				return;
			}
			Customer c = cc.GetCurrentCustomer();
			if (c == null)
			{
				return;
			}
			try
			{
				if (info.order.Count > 0)
				{
					c.endlessOrder = FromLines(info.order);
				}
				if (info.ordered)
				{
					AccessTools.Field(typeof(Customer), "ordered").SetValue(c, true);
					if (WhiteBoard.instance != null && c.endlessOrder.Count > 0)
					{
						WhiteBoard.instance.UpdateWhiteBoard(c.endlessOrder);
					}
				}
				if (_hasPendingCustState)
				{
					_hasPendingCustState = false;
					ApplyCustState(c, _pendingCustFlags, _pendingCustLines);
				}
			}
			catch (Exception e)
			{
				
	}
		}

		public static void HandleCustSpawn(PacketReader r)
		{
			if (Session.IsHost)
			{
				return;
			}
			ApplyCustomerSpawn(CustomerSpawnInfo.Read(r));
		}

		public static void HandleCustGoAway(PacketReader r)
		{
			bool next = r.Bool();
			ApplyGoAway(next, false);
		}

		public static void HandleCustGoAwayReq(int fromPeerId, PacketReader r)
		{
			if (!Session.IsHost)
			{
				return;
			}
			bool next = r.Bool();
			Session.Broadcast(MsgBuild.CustGoAway(next));
			ApplyGoAway(next, true);
		}

		private static void ApplyGoAway(bool nextCustomer, bool hostLocal)
		{
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			if (c == null)
			{
				return;
			}
			NetCtx.ApplyingGoAway = true;
			NetCtx.ApplyingRemote = true;
			try
			{
				if (Session.IsClient)
				{

					c.GoAway(false);
				}
				else
				{
					c.GoAway(nextCustomer);
				}
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.ApplyingGoAway = false;
				NetCtx.ApplyingRemote = false;
			}
		}

		private static byte _pendingCustFlags;
		private static List<OrderLine> _pendingCustLines;
		private static bool _hasPendingCustState;

		public static void SendCustState()
		{
			if (!Session.Active || !Session.IsHost)
			{
				return;
			}
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			if (c == null)
			{
				return;
			}
			byte flags = 0;
			try
			{
				if (AccessTools.Field(typeof(Customer), "ordered").GetValue(c) is bool ordered && ordered)
				{
					flags |= 1;
				}
			}
			catch
			{
			}
			if (c.canInteract)
			{
				flags |= 2;
			}
			Session.Broadcast(MsgBuild.CustState(flags, ToLines(c.endlessOrder)));
		}

		public static void HandleCustState(PacketReader r)
		{
			if (Session.IsHost)
			{
				return;
			}
			byte flags = r.U8();
			int n = r.I32();
			List<OrderLine> lines = new List<OrderLine>();
			for (int i = 0; i < n; i++)
			{
				lines.Add(OrderLine.Read(r));
			}
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			if (c == null)
			{

				_pendingCustFlags = flags;
				_pendingCustLines = lines;
				_hasPendingCustState = true;
				return;
			}
			ApplyCustState(c, flags, lines);
		}

		private static void ApplyCustState(Customer c, byte flags, List<OrderLine> lines)
		{
			NetCtx.ApplyingRemote = true;
			try
			{
				bool ordered = (flags & 1) != 0;
				bool wasOrdered = AccessTools.Field(typeof(Customer), "ordered").GetValue(c) is bool b && b;
				AccessTools.Field(typeof(Customer), "ordered").SetValue(c, ordered);
				c.canInteract = (flags & 2) != 0;
				if (ordered && !wasOrdered)
				{

					c.interactableName = "";
				}
				if (lines != null && lines.Count > 0)
				{
					bool changed = !SameLines(ToLines(c.endlessOrder), lines);
					c.endlessOrder = FromLines(lines);
					if (changed && WhiteBoard.instance != null)
					{

						WhiteBoard.instance.UpdateWhiteBoard(c.endlessOrder);
					}
					if (WhiteBoard.instance != null)
					{
						for (int i = 0; i < lines.Count; i++)
						{
							if (lines[i].done && i < WhiteBoard.instance.xObjects.Length)
							{
								WhiteBoard.instance.xObjects[i].SetActive(true);
							}
						}
					}
				}
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		public static int DialogOwner = -1;

		public static bool PendingStoryDelivery;

		public static bool RemoteDialogRunning()
		{
			if (!Session.Active)
			{
				return false;
			}
			return DialogOwner >= 0 && DialogOwner != Session.SelfId
				&& DialogActiveOf(DialogController.instance);
		}

		public static bool DialogOnScreen()
		{
			DialogController d = DialogController.instance;
			return d != null && DialogActiveOf(d);
		}

		public static int DialogStartBlockedFrame = -1;

		private static readonly System.Reflection.MethodInfo _stopDialogMethod =
			AccessTools.Method(typeof(DialogController), "StopDialog");

		private static readonly AccessTools.FieldRef<DialogController, bool> _dialogActiveRef =
			AccessTools.FieldRefAccess<DialogController, bool>("dialogActive");

		private static readonly AccessTools.FieldRef<DialogController, int> _currentDialogRef =
			AccessTools.FieldRefAccess<DialogController, int>("currentDialog");

		public static bool DialogActiveOf(DialogController d)
		{
			try
			{
				return d != null && _dialogActiveRef(d);
			}
			catch
			{
				return false;
			}
		}

		public static void SendDialogStart(int index, bool onlyLastText, bool setSkipFirst)
		{
			if (!Session.Active || NetCtx.ApplyingDialog)
			{
				return;
			}
			int owner = NetCtx.DialogOwnerOverride >= 0 ? NetCtx.DialogOwnerOverride : Session.SelfId;
			DialogOwner = owner;
			byte[] m = MsgBuild.DialogSync(0, owner, index, onlyLastText, setSkipFirst);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void NotifyDialogAdvance()
		{
			if (!Session.Active || NetCtx.ApplyingDialog)
			{
				return;
			}
			if (DialogOwner >= 0 && DialogOwner != Session.SelfId)
			{
				return;
			}
			byte[] m = MsgBuild.DialogSync(1, DialogOwner, -1, false, false);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleDialogSync(int fromPeerId, PacketReader r)
		{
			byte kind = r.U8();
			int owner = r.I32();
			int id = r.I32();
			bool onlyLast = r.Bool();
			bool setSkip = r.Bool();
			if (Session.IsHost)
			{

				Session.Broadcast(MsgBuild.DialogSync(kind, owner, id, onlyLast, setSkip), fromPeerId);
			}
			ApplyDialogSync(kind, owner, id, onlyLast, setSkip);
		}

		public static void ApplyDialogSync(byte kind, int owner, int id, bool onlyLast, bool setSkip)
		{
			DialogController d = DialogController.instance;
			if (d == null)
			{
				return;
			}
			NetCtx.ApplyingDialog = true;
			try
			{
				if (kind == 0)
				{
					ApplyDialogStart(d, owner, id, onlyLast, setSkip);
				}
				else if (_stopDialogMethod != null)
				{

					if (owner == Session.SelfId
						|| (DialogOwner >= 0 && DialogOwner != owner))
					{
						return;
					}
					_stopDialogMethod.Invoke(d, null);
					if (!DialogActiveOf(d))
					{
						ExitQuietDialog(d);
					}
				}
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.ApplyingDialog = false;
			}
		}

		private static void ApplyDialogStart(DialogController d, int owner, int id, bool onlyLast, bool setSkip)
		{
			bool active = DialogActiveOf(d);
			if (owner == Session.SelfId)
			{

				if (active && _currentDialogRef(d) == id)
				{
					return;
				}
			}
			else if (active)
			{
				if (DialogOwner == owner && _currentDialogRef(d) == id)
				{

					return;
				}

				int rank = DialogOwner >= 0 ? DialogOwner : int.MaxValue;
				if (owner > rank)
				{

					return;
				}

				DialogOwner = owner;
				EnterQuietDialog(d);

				d.StartDialog(id, onlyLast, setSkip);
				ReleaseQuietInput();
				return;
			}
			bool quiet = owner != Session.SelfId;
			DialogOwner = owner;
			if (quiet)
			{

				EnterQuietDialog(d);
			}
			else
			{
				ExitQuietDialog(d);
			}
			d.StartDialog(id, onlyLast, setSkip);
			if (quiet)
			{
				ReleaseQuietInput();
			}
			else
			{
				CustomersController cc = CustomersController.instance;
				Customer c = cc != null ? cc.GetCurrentCustomer() : null;
				if (c != null && c.headRotate != null && PlayerController.instance != null)
				{
					PlayerController.instance.FocusViewOn(c.headRotate);
				}
			}
		}

		public static bool QuietDialogActive;

		private static Text _realDialogText;
		private static Text _dummyDialogText;

		private static void EnsureDummyDialogText()
		{
			if (_dummyDialogText != null)
			{
				return;
			}
			GameObject go = new GameObject("MP_SilentDialogText",
				typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.transform.position = new Vector3(0f, -10000f, 0f);
			_dummyDialogText = go.GetComponent<Text>();
		}

		private static void EnterQuietDialog(DialogController d)
		{
			if (d == null || d.dialogTextNormal == null || d.dialogTextNormal == _dummyDialogText)
			{
				return;
			}
			EnsureDummyDialogText();
			_realDialogText = d.dialogTextNormal;

			_realDialogText.text = "";
			_realDialogText.gameObject.SetActive(false);
			d.dialogTextNormal = _dummyDialogText;
			QuietDialogActive = true;
		}

		private static void ExitQuietDialog(DialogController d)
		{
			QuietDialogActive = false;
			if (d == null || _realDialogText == null)
			{
				return;
			}
			if (d.dialogTextNormal == _dummyDialogText)
			{
				d.dialogTextNormal = _realDialogText;
			}
			_realDialogText = null;
		}

		public static void EnsureLoudDialog()
		{

			if (NetCtx.ApplyingDialog || NetCtx.FromRemoteInteract)
			{
				return;
			}
			ExitQuietDialog(DialogController.instance);
		}

		public static void QuietRemoteDialog(DialogController d)
		{
			EnterQuietDialog(d);
			NetCtx.RemoteQuietFrame = Time.frameCount;
		}

		private static void ReleaseQuietInput()
		{

			PlayerController.blockGlobal = false;
			if (UIController.instance != null)
			{
				UIController.instance.SetCrosshair(b: true);
			}
			if (PlayerController.instance != null)
			{
				PlayerController.instance.ForceStopFocus();
			}
		}

		public static void HandleOrderDone(PacketReader r)
		{
			int index = r.I32();
			float delta = r.F32();
			bool line = r.Bool();
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.OrderDone(index, delta, line));
			}
			ApplyOrderDone(index, delta, line);
		}

		public static void ApplyOrderDone(int index, float delta, bool line)
		{
			NetCtx.ApplyingRemote = true;
			try
			{
				if (line && index >= 0 && WhiteBoard.instance != null)
				{
					WhiteBoard.instance.SetOrderDone(index);
				}
				if (delta != 0f && TimerController.instance != null)
				{
					TimerController.instance.SetCurrentSeconds(delta);
				}
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		public static byte[] BuildTimerSync()
		{
			TimerController t = TimerController.instance;
			GameManager g = GameManager.instance;
			return MsgBuild.TimerSync(
				t != null ? t.currentTimerInSeconds : 0f,
				t != null ? t.timeElapsedValue : 0f,
				g != null ? g.ordersCompleted : 0,
				g != null ? (byte)g.gameMode : (byte)0,
				g != null ? g.currentDay : 0,
				g != null ? g.currentCustomer : 0,
				GameRunning);
		}

		public static void HandleTimerSync(PacketReader r)
		{
			if (Session.IsHost)
			{
				return;
			}
			float rem = r.F32();
			float elapsed = r.F32();
			int orders = r.I32();
			byte mode = r.U8();
			int day = r.I32();
			int cust = r.I32();
			bool inGame = r.Bool();

			GameManager g = GameManager.instance;
			if (g != null)
			{
				g.ordersCompleted = orders;
				g.gameMode = (GameManager.GameModes)mode;
				g.currentDay = day;
				g.currentCustomer = cust;
			}
			GameMode = mode;
			CurrentDay = day;
			CurrentCustomer = cust;
			GameRunning = inGame;

			TimerController t = TimerController.instance;
			if (t != null)
			{
				if (Mathf.Abs(t.currentTimerInSeconds - rem) > 0.6f)
				{
					t.currentTimerInSeconds = rem;
				}
				if (Mathf.Abs(t.timeElapsedValue - elapsed) > 0.4f)
				{
					t.timeElapsedValue = elapsed;
				}
				if (orders >= 0 && t.ordersText != null)
				{
					try
					{
						t.UpdateOrdersText();
					}
					catch
					{
					}
				}
			}
			if (inGame && Session.IsClient)
			{
				RunMidJoinFlow();
			}
		}

		public static void SendBoard(bool clear, List<OrderLine> lines)
		{
			if (!Session.Active)
			{
				return;
			}
			byte[] m = MsgBuild.BoardSync(clear, lines);
			if (Session.IsHost)
			{
				Session.Broadcast(m);
			}
			else
			{
				Session.SendToHost(m);
			}
		}

		public static void HandleBoardSync(PacketReader r)
		{
			bool clear = r.Bool();
			int n = r.I32();
			List<OrderLine> lines = new List<OrderLine>();
			for (int i = 0; i < n; i++)
			{
				lines.Add(OrderLine.Read(r));
			}
			if (Session.IsHost)
			{

				Session.Broadcast(MsgBuild.BoardSync(clear, lines));
			}
			ApplyBoard(clear, lines);
		}

		private static void ApplyBoard(bool clear, List<OrderLine> lines)
		{
			WhiteBoard wb = WhiteBoard.instance;
			if (wb == null)
			{
				return;
			}
			NetCtx.ApplyingRemote = true;
			try
			{
				if (clear)
				{
					wb.ClearOldOrder();
					wb.ClearWhiteBoard();
					return;
				}
				List<Customer.OrdedItemNew> order = FromLines(lines);
				wb.UpdateWhiteBoard(order);
				for (int i = 0; i < order.Count && i < wb.xObjects.Length; i++)
				{
					wb.xObjects[i].SetActive(order[i].orderDone);
				}
			}
			catch (Exception e)
			{
				
			}
			finally
			{
				NetCtx.ApplyingRemote = false;
			}
		}

		public static void NotifyBoardChanged(bool clear, List<OrderLine> lines)
		{
			if (!Session.Active || NetCtx.ApplyingRemote)
			{
				return;
			}
			SendBoard(clear, lines);
		}

		public static void NotifyVanillaBoard(List<Customer.OrdedItemNew> order)
		{
			if (!Session.Active || NetCtx.ApplyingRemote)
			{
				return;
			}
			if (GameManager.instance == null || GameManager.instance.isEndlessOrRelaxMode())
			{
				return;
			}
			SendBoard(false, ToLines(order));
		}

		public static void NotifyVanillaBoardClear()
		{
			if (!Session.Active || NetCtx.ApplyingRemote)
			{
				return;
			}
			if (GameManager.instance == null || GameManager.instance.isEndlessOrRelaxMode())
			{
				return;
			}
			SendBoard(true, new List<OrderLine>());
		}

		public static void HandleUiCmd(int fromPeerId, PacketReader r)
		{
			byte b = r.U8();
			UiCommand cmd = (UiCommand)b;
			if (Session.IsHost && IsGuestBlockedStart(cmd))
			{
				
				return;
			}
			if (Session.IsHost)
			{
				Session.Broadcast(MsgBuild.UiCmd(cmd));
			}
			ApplyUiCmd(cmd);
		}

		private static int _deferredUi = -1;
		private static float _deferredUiUntil;
		private static bool _deferredUiBroadcast;

		private static bool IsGuestBlockedStart(UiCommand cmd)
		{
			return cmd == UiCommand.StartStory
				|| cmd == UiCommand.StartEndless
				|| cmd == UiCommand.StartRelax;
		}

		private static bool IsStartCmd(UiCommand cmd)
		{
			return cmd == UiCommand.StartStory
				|| cmd == UiCommand.StartEndless
				|| cmd == UiCommand.StartRelax
				|| cmd == UiCommand.Continue;
		}

		public static UIController ActiveUiController()
		{
			UIController u = UIController.instance;
			if (u != null && u.gameObject != null && u.gameObject.activeInHierarchy)
			{
				return u;
			}
			UIController best = null;
			UIController[] all = Resources.FindObjectsOfTypeAll<UIController>();
			for (int i = 0; i < all.Length; i++)
			{
				UIController c = all[i];
				if (c == null || c.gameObject == null || !c.gameObject.activeInHierarchy)
				{
					continue;
				}
				if (!c.gameObject.scene.IsValid())
				{
					continue;
				}
				bool menuUp = c.mainMenuScreen != null && c.mainMenuScreen.activeInHierarchy;
				if (best == null || menuUp)
				{
					best = c;
					if (menuUp)
					{
						break;
					}
				}
			}
			if (best != null)
			{
				UIController.instance = best;
			}
			return best;
		}

		public static void DeferredUiTick()
		{
			if (_deferredUi < 0)
			{
				return;
			}
			if (Time.realtimeSinceStartup > _deferredUiUntil)
			{
				
				
				_deferredUi = -1;
				_deferredUiBroadcast = false;
				return;
			}
			if (ActiveUiController() == null)
			{
				return;
			}
			int c = _deferredUi;
			_deferredUi = -1;
			
			UiCommand cmd = (UiCommand)c;
			bool applied = ApplyUiCmd(cmd);
			if (applied && IsStartCmd(cmd) && !GameRunning)
			{
				applied = false;
				
			}
			if (applied && _deferredUiBroadcast && Session.Active && Session.IsHost)
			{
				_deferredUiBroadcast = false;
				Session.Broadcast(MsgBuild.UiCmd(cmd));
				
			}
		}

		public static void NoteUiState(UiCommand cmd)
		{
			switch (cmd)
			{
				case UiCommand.StartStory:
				case UiCommand.StartEndless:
				case UiCommand.StartRelax:
				case UiCommand.Continue:
				case UiCommand.NextDay:
				case UiCommand.RestartEndless:
				case UiCommand.YouDiedRestart:
					if (!GameRunning)
					{
						
					}
					GameRunning = true;
					MidJoinHandled = true;
					break;
				case UiCommand.BackToMenu:
				case UiCommand.EndCredit:
					if (GameRunning)
					{
						
					}
					GameRunning = false;
					MidJoinHandled = false;
					_deferredUi = -1;
					_deferredUiBroadcast = false;
					break;
			}
		}

		public static void ResetLocalGameState()
		{
			GameRunning = false;
			MidJoinHandled = false;
			_deferredUi = -1;
			_deferredUiBroadcast = false;
		}

		public static bool ApplyUiCmd(UiCommand cmd)
		{
			UIController u = ActiveUiController();
			if (u == null)
			{
				if (IsStartCmd(cmd))
				{
					_deferredUi = (int)cmd;
					_deferredUiUntil = Time.realtimeSinceStartup + 30f;
					
					
				}
				else
				{
					
				}
				return false;
			}
			if ((cmd == UiCommand.BackToMenu || cmd == UiCommand.EndCredit) && _deferredUi >= 0)
			{
				_deferredUi = -1;
				_deferredUiBroadcast = false;
			}
			if (IsStartCmd(cmd) && CanvasMenu.IsOpen)
			{
				CanvasMenu.Close();
			}
			int c = (int)cmd;
			if (c >= 0 && c < _lastUiRun.Length)
			{
				_lastUiRun[c] = Time.realtimeSinceStartup;
			}
			SetFollowup(cmd);
			NetCtx.SuppressUiSend = true;
			bool ok = true;
			try
			{
				switch (cmd)
				{
					case UiCommand.StartStory:
						u.StartTheGame();
						break;
					case UiCommand.StartEndless:
						u.StartEndlessMode();
						break;
					case UiCommand.StartRelax:
						u.StartRelaxMode();
						break;
					case UiCommand.Continue:
						u.ContinueGame();
						break;
					case UiCommand.BackToMenu:
						u.BackToMainMenu();
						break;
					case UiCommand.RestartEndless:
						u.RestartEndlessMode();
						break;
					case UiCommand.YouDiedRestart:
						u.YouDiedRestart();
						break;
					case UiCommand.EndCredit:
						u.EndCredit();
						break;
					case UiCommand.NextDay:
						u.NextDayScreen(false);
						break;
					case UiCommand.CallOverEndless:
						u.CallOverEndlessMode();
						break;
				}
			}
			catch (Exception e)
			{
				ok = false;
				
				
			}
			finally
			{
				NetCtx.SuppressUiSend = false;
			}
			if (ok)
			{
				
			}
			return ok;
		}

		public static void WriteFlowSnapshot(PacketWriter w)
		{
			CustomersController cc = CustomersController.instance;
			Customer c = cc != null ? cc.GetCurrentCustomer() : null;
			CustomerSpawnInfo info = c != null ? CaptureCustomer() : null;
			bool hasCust = info != null;
			w.Bool(hasCust);
			if (hasCust)
			{
				info.Write(w);
			}
			WhiteBoard wb = WhiteBoard.instance;
			List<OrderLine> lines = wb != null ? ToLines(wb.oldOrder) : new List<OrderLine>();
			w.Bool(lines.Count > 0);
			w.I32(lines.Count);
			for (int i = 0; i < lines.Count; i++)
			{
				lines[i].Write(w);
			}
		}

		public static void ReadFlowSnapshot(PacketReader r)
		{
			bool hasCust = r.Bool();
			if (hasCust)
			{
				ApplyCustomerSpawn(CustomerSpawnInfo.Read(r));
			}
			bool hasBoard = r.Bool();
			int n = r.I32();
			List<OrderLine> lines = new List<OrderLine>();
			for (int i = 0; i < n; i++)
			{
				lines.Add(OrderLine.Read(r));
			}
			if (hasBoard && n > 0)
			{
				ApplyBoard(false, lines);
			}
		}

		public static void UpdateFlow()
		{
			if (_pendingHostSpawn != null && Time.frameCount >= _pendingHostSpawnFrame)
			{
				CustomerSpawnInfo info = _pendingHostSpawn;
				_pendingHostSpawn = null;
				if (info != null && Session.Active && Session.IsHost)
				{
					Session.Broadcast(MsgBuild.CustSpawn(info));
				}
			}
			if (_queuedCust != null && Time.frameCount >= _queuedCustFrame)
			{
				CustomerSpawnInfo info = _queuedCust;
				_queuedCust = null;
				FinalizeCustomerSpawn(info);
			}
		}
	}
}
