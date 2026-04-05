using UnityEngine;

namespace IGTAPMod
{
    public class DebugUI : MonoBehaviour
    {
        private bool menuOpen;
        private Rect windowRect = new Rect(20, 200, 290, 450);
        private Vector2 scrollPos;

        private void Update()
        {
            if (Plugin.UIToggleKey.Value.IsDown())
                menuOpen = !menuOpen;
        }

        private void OnGUI()
        {
            DrawHUD();

            if (menuOpen)
                windowRect = GUI.Window(9381, windowRect, DrawMenu, "IGTAP Mod");
        }

        private void DrawHUD()
        {
            string hud = "";
            foreach (var item in DebugMenuAPI.HudItems)
            {
                string text = item.GetText?.Invoke();
                if (!string.IsNullOrEmpty(text))
                {
                    if (hud.Length > 0) hud += "  ";
                    hud += text;
                }
            }

            if (string.IsNullOrEmpty(hud)) return;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 18;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(12, 12, 800, 30), hud, style);
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(10, 10, 800, 30), hud, style);
        }

        private void DrawMenu(int id)
        {
            scrollPos = GUILayout.BeginScrollView(scrollPos);

            for (int i = 0; i < DebugMenuAPI.Sections.Count; i++)
            {
                var section = DebugMenuAPI.Sections[i];
                if (i > 0) GUILayout.Space(10);
                GUILayout.Label($"<b>--- {section.Title} ---</b>",
                    new GUIStyle(GUI.skin.label) { richText = true });
                section.Draw?.Invoke();
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }
    }
}
