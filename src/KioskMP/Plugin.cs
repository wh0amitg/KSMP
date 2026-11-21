using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KioskMP
{
	[BepInPlugin(GUID, NAME, VERSION)]
	public class Plugin : BaseUnityPlugin
	{
		public const string GUID = "com.slihu.ksmp";
		public const string NAME = "KioskMP";
		public const string VERSION = "0.2.0";

		private Harmony _harmony;

		public static Plugin Instance { get; private set; }
		public static ManualLogSource LogSource { get; private set; }

		public static bool Verbose;

		private void Awake()
		{
			Instance = this;
			LogSource = Logger;
			DontDestroyOnLoad(gameObject);
			Application.runInBackground = true;

			gameObject.AddComponent<MpManager>();

			_harmony = new Harmony(GUID);
			_harmony.PatchAll(typeof(Plugin).Assembly);

			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			try { Game.OnSceneLoaded(scene); }
			catch (System.Exception e) { }
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			if (_harmony != null) _harmony.UnpatchSelf();
		}
	}
}
