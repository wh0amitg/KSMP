using System;
using UnityEngine;

namespace KioskMP
{
	public sealed class MpManager : MonoBehaviour
	{
		public static MpManager Instance;

		private bool _steamTried;
		private float _nextSteamTry;
		private bool _menuErrLogged;

		private void Awake()
		{
			Instance = this;
			DontDestroyOnLoad(gameObject);
			
		}

		private void Update()
		{
			try { Session.Update(); }
			catch (Exception e) {  }

			try { Game.Update(); }
			catch (Exception e) {  }

			if (Session.Active && Game.GameRunning && Time.timeScale < 1f)
			{
				Time.timeScale = 1f;
			}

			if (!_steamTried && Game.SceneReady && Time.realtimeSinceStartup > _nextSteamTry)
			{
				_nextSteamTry = Time.realtimeSinceStartup + 5f;
				try
				{
					SteamNet.Init();
					_steamTried = SteamNet.SteamReady;
				}
				catch (Exception e)
				{
					
					_steamTried = true;
				}
			}

			try { MenuBuilder.Tick(); }
			catch (Exception e) {  }

			if (MenuHotkeyPressed())
			{
				
				try { MenuUI.Toggle(); }
				catch (Exception e) {  }
			}

			try { MenuUI.Tick(); }
			catch (Exception e) {  }

			try { PropFX.Tick(); }
			catch (Exception e) {  }
		}

		private static bool MenuHotkeyPressed()
		{
			try
			{
				if (Input.GetKeyDown(KeyCode.F6) || Input.GetKeyDown(KeyCode.F7))
					return true;
			}
			catch { }
			try
			{
				var kb = UnityEngine.InputSystem.Keyboard.current;
				if (kb != null && (kb.f6Key.wasPressedThisFrame || kb.f7Key.wasPressedThisFrame))
					return true;
			}
			catch { }
			return false;
		}

		private void OnGUI()
		{
			try { MenuUI.Draw(); }
			catch (Exception e)
			{
				if (!_menuErrLogged)
				{
					_menuErrLogged = true;
					
				}
			}
		}
	}
}
