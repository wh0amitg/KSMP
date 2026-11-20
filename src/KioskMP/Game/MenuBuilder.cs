using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KioskMP
{
	internal static class MenuBuilder
	{
		private static bool _done;
		private static bool _titleDone;
		private static GameObject _btn;
		private static GameObject _titleGo;
		private static float _nextTry;
		private static int _attempts;
		private static string _scene;
		private static string _menuScene;

		public static void Tick()
		{
			CheckScene();
			if (_done && _btn == null)
			{
				_done = false;
				_nextTry = 0f;
			}
			if (_titleDone && _titleGo == null)
			{
				_titleDone = false;
			}
			if (_done && _titleDone) return;
			if (_menuScene != null && _scene != _menuScene) return;
			if (Time.realtimeSinceStartup < _nextTry) return;
			_nextTry = Time.realtimeSinceStartup + 0.5f;
			_attempts++;

			

			try { if (!TryBuild()) _nextTry += 2.5f; }
			catch (Exception e) {  }
		}

		private static void CheckScene()
		{
			try
			{
				string s = SceneManager.GetActiveScene().name;
				if (s == _scene) return;
				bool first = _scene == null;
				_scene = s;
				if (first) return;
				_done = false;
				_titleDone = false;
				_attempts = 0;
				_nextTry = 0f;
				
			}
			catch (Exception e) {  }
		}

		private static bool TryBuild()
		{

			Transform tabs = null;
			Transform kiosk_text = null;

			foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
			{
				if (t == null || !t.gameObject.activeInHierarchy) continue;
				if (!t.gameObject.scene.IsValid()) continue;
				if (t.name == "MenuTabs" && tabs == null) tabs = t;
				if (t.name == "Kiosk_text" && kiosk_text == null) kiosk_text = t;
			}

			

			if (!_titleDone && kiosk_text != null)
			{
				SetText(kiosk_text.gameObject, "KSMP");
				_titleDone = true;
				_titleGo = kiosk_text.gameObject;
				_menuScene = _scene;
				
			}

			if (_done) return true;
			if (tabs == null) return kiosk_text != null;

			GameObject template = null;
			for (int i = 0; i < tabs.childCount; i++)
			{
				Transform child = tabs.GetChild(i);
				if (child == null || !child.gameObject.activeInHierarchy) continue;
				if (child.GetComponentInChildren<Button>(true) == null) continue;
				template = child.gameObject;
				break;
			}

			
			if (template == null) return true;

			GameObject btn = UnityEngine.Object.Instantiate(template, tabs);
			btn.name = "KioskMP_Multiplayer";

			foreach (TextMeshProUGUI t in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
				SetText(t.gameObject, "Multiplayer (WIP)");
			foreach (Text t in btn.GetComponentsInChildren<Text>(true))
				SetText(t.gameObject, "Multiplayer (WIP)");

			Button button = btn.GetComponentInChildren<Button>(true);
			if (button != null)
			{
				button.onClick = new Button.ButtonClickedEvent();
				button.onClick.AddListener(() => MenuUI.Toggle());
			}

			foreach (Component c in btn.GetComponentsInChildren<Component>(true))
			{
				if (c != null && c.GetType().Name == "ButtonHelper")
				{
					Behaviour b = c as Behaviour;
					if (b != null) b.enabled = false;
				}
			}

			RectTransform rt = btn.GetComponent<RectTransform>();
			if (rt != null)
			{
				float lowestY = float.MaxValue;
				float spacingY = 80f;
				float anchorX = 0f;
				float prevY = float.NaN;

				for (int i = 0; i < tabs.childCount; i++)
				{
					Transform sib = tabs.GetChild(i);
					if (sib == null || sib == btn.transform) continue;
					if (!sib.gameObject.activeInHierarchy) continue;
					RectTransform r = sib as RectTransform;
					if (r == null) continue;

					if (r.anchoredPosition.y < lowestY)
					{
						lowestY = r.anchoredPosition.y;
						anchorX = r.anchoredPosition.x;
					}
					if (!float.IsNaN(prevY))
					{
						float d = Mathf.Abs(prevY - r.anchoredPosition.y);
						if (d > 2f && d < 300f) spacingY = d;
					}
					prevY = r.anchoredPosition.y;
				}

				if (lowestY < float.MaxValue)
					rt.anchoredPosition = new Vector2(anchorX, lowestY - spacingY);
			}

			btn.transform.SetAsLastSibling();
			btn.SetActive(true);

			_btn = btn;
			_done = true;
			_menuScene = _scene;
			
			return true;
		}

		private static void SetText(GameObject go, string text)
		{

			foreach (string comp in new[] { "LocalizeString", "LocalizeStringEvent" })
			{
				Component c = go.GetComponent(comp);
				if (c != null) UnityEngine.Object.Destroy(c);
			}

			TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
			if (tmp != null) { tmp.text = text; return; }

			Text txt = go.GetComponent<Text>();
			if (txt != null) { txt.text = text; return; }

			TextMeshPro world = go.GetComponent<TextMeshPro>();
			if (world != null) world.text = text;
		}
	}
}
