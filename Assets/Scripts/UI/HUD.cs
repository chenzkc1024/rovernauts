using UnityEngine;
using UnityEngine.InputSystem;

namespace Rovernauts
{
    /// <summary>
    /// Immediate-mode UI for the alpha: title/tutorial screen, in-run HUD, boss bar, upgrade cards,
    /// pause and game-over screens, plus the AI debug overlay (F1). Laid out on a 1080p-tall virtual canvas.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        static HUD instance;
        const float RefHeight = 1080f;

        string bannerTitle, bannerSub;
        Color bannerColor;
        float bannerUntil;
        GUIStyle title, huge, big, mid, small;
        readonly System.Collections.Generic.Dictionary<GUIStyle, GUIStyle> centeredVariants = new System.Collections.Generic.Dictionary<GUIStyle, GUIStyle>();
        float scale, width;

        public static void Banner(string text, string subtitle, Color color, float seconds = 3f)
        {
            if (instance == null) return;
            instance.bannerTitle = text;
            instance.bannerSub = subtitle;
            instance.bannerColor = color;
            instance.bannerUntil = Time.unscaledTime + seconds;
        }

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnGUI()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            EnsureStyles();
            scale = Screen.height / RefHeight;
            width = Screen.width / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            switch (gm.State)
            {
                case GameState.Menu:
                    DrawMenu();
                    break;
                case GameState.Playing:
                    DrawWorldOverlays(gm);
                    DrawRunHud(gm);
                    break;
                case GameState.Upgrade:
                    DrawRunHud(gm);
                    DrawUpgrade(gm);
                    break;
                case GameState.Paused:
                    DrawRunHud(gm);
                    DrawPaused();
                    break;
                case GameState.GameOver:
                    DrawGameOver(gm);
                    break;
            }
            if (gm.State != GameState.Menu && Time.unscaledTime < bannerUntil) DrawBanner();
        }

        // ---------- screens ----------

        void DrawMenu()
        {
            Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0.03f, 0.55f));
            float cx = width * 0.5f;
            Shadowed(new Rect(cx - 600f, 120f, 1200f, 140f), "ROVERNAUTS", title, new Color(1f, 0.6f, 0.15f));
            Shadowed(new Rect(cx - 600f, 255f, 1200f, 40f), "Hold the line on a hostile moon. One rover, 2-4 crew, endless alien waves.", mid, Color.white);

            var panel = new Rect(cx - 520f, 330f, 1040f, 400f);
            Fill(panel, new Color(0.05f, 0.07f, 0.12f, 0.85f));
            float y = panel.y + 25f;
            Row(ref y, panel.x + 40f, "CREW", "", GameManager.PlayerColors[0], big);
            Row(ref y, panel.x + 40f, "P1  DRIVER", "WASD / arrow keys to drive,  Space / Shift to boost", GameManager.PlayerColors[0], mid);
            Row(ref y, panel.x + 40f, "P2  GUNNER", "Mouse to aim,  hold left click to fire", GameManager.PlayerColors[1], mid);
            Row(ref y, panel.x + 40f, "P3-P4  GUNNERS", "Gamepad: press A / Start to join,  right stick aims,  RT fires", GameManager.PlayerColors[2], mid);
            y += 15f;
            Row(ref y, panel.x + 40f, "THREATS", "", new Color(1f, 0.4f, 0.3f), big);
            Row(ref y, panel.x + 40f, "Aliens", "Crawlers swarm, Spitters kite and shoot, Dashers telegraph a charge", Color.white, mid);
            Row(ref y, panel.x + 40f, "Bosses", "Every 5th wave (a Void Titan every 10th). Beat bosses and moon events to earn upgrades", Color.white, mid);
            Row(ref y, panel.x + 40f, "Events", "Asteroid storms and blood moons. Aliens grow stronger the longer you survive", Color.white, mid);

            bool blink = Mathf.Repeat(Time.unscaledTime, 1.2f) < 0.8f;
            if (blink) Shadowed(new Rect(cx - 400f, 760f, 800f, 60f), "PRESS ENTER TO LAUNCH", big, new Color(0.4f, 1f, 0.6f));
            if (GUI.Button(new Rect(cx - 120f, 830f, 240f, 50f), "LAUNCH")) GameManager.I.StartRun();
            Shadowed(new Rect(cx - 600f, 960f, 1200f, 30f), "Esc pause   |   F1 AI debug view   |   F2 autopilot demo   |   F3 skip to boss wave   |   F4 god mode", small, new Color(0.7f, 0.7f, 0.8f));
        }

        void DrawRunHud(GameManager gm)
        {
            var waves = gm.Waves;
            var rover = gm.Rover;

            // Top-left: run info.
            Fill(new Rect(20f, 20f, 360f, 175f), new Color(0f, 0f, 0f, 0.45f));
            Shadowed(new Rect(36f, 26f, 340f, 60f), $"WAVE {waves.Wave}", huge, Color.white);
            int alive = EnemyBase.All.Count;
            string incoming = waves.QueuedCount > 0 ? $"  (+{waves.QueuedCount} incoming)" : "";
            Shadowed(new Rect(36f, 92f, 340f, 30f), $"Aliens: {alive}{incoming}", mid, Color.white);
            Shadowed(new Rect(36f, 122f, 340f, 30f), $"Time {Mathf.FloorToInt(gm.RunTime / 60f):00}:{Mathf.FloorToInt(gm.RunTime % 60f):00}    Kills {gm.Kills}    Score {gm.Score}", small, Color.white);
            Shadowed(new Rect(36f, 152f, 340f, 30f), $"Alien strength x{waves.HpMultiplier:0.00}", small, new Color(1f, 0.6f, 0.5f));
            if (waves.CountdownLeft > 0f)
                Shadowed(new Rect(width * 0.5f - 200f, 330f, 400f, 50f), $"Incoming in {waves.CountdownLeft:0.0}", big, Color.white, true);

            // Top-right: active moon event.
            var ev = gm.Events.Active;
            if (ev != MoonEvent.None)
            {
                string label = ev == MoonEvent.BloodMoon ? "BLOOD MOON" : "ASTEROID STORM";
                Color c = ev == MoonEvent.BloodMoon ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.65f, 0.3f);
                Fill(new Rect(width - 400f, 20f, 380f, 50f), new Color(0f, 0f, 0f, 0.45f));
                Shadowed(new Rect(width - 390f, 26f, 360f, 40f), label, big, c);
            }

            // Top-centre: boss bar.
            var boss = waves.ActiveBoss;
            if (boss != null && !boss.Health.IsDead)
            {
                var bar = new Rect(width * 0.5f - 400f, 40f, 800f, 26f);
                string name = boss.DisplayName.ToUpper() + (boss.Enraged ? "  -  ENRAGED" : "");
                Shadowed(new Rect(bar.x, 8f, bar.width, 32f), name, mid, new Color(1f, 0.5f, 0.4f), true);
                Bar(bar, boss.Health.Fraction, boss.Enraged ? new Color(1f, 0.4f, 0.05f) : new Color(0.85f, 0.15f, 0.15f));
            }

            // Bottom-left: hull and boost.
            var hullRect = new Rect(30f, RefHeight - 110f, 460f, 30f);
            Fill(new Rect(20f, RefHeight - 150f, 480f, 130f), new Color(0f, 0f, 0f, 0.45f));
            Shadowed(new Rect(30f, RefHeight - 145f, 460f, 32f), $"HULL  {rover.Health.Current:0} / {rover.Health.MaxHp:0}" + (gm.GodMode ? "   [GOD MODE]" : ""), mid, Color.white);
            Bar(hullRect, rover.Health.Fraction, Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.95f, 0.45f), rover.Health.Fraction));
            Shadowed(new Rect(30f, RefHeight - 72f, 120f, 28f), "BOOST", small, Color.white);
            Bar(new Rect(110f, RefHeight - 66f, 380f, 14f), rover.BoostReady, rover.BoostReady >= 1f ? new Color(1f, 0.7f, 0.2f) : new Color(0.5f, 0.4f, 0.3f));

            // Bottom-right: crew.
            int crew = rover.Turrets.Count + 1;
            float h = 50f + crew * 30f + 30f;
            var crewRect = new Rect(width - 420f, RefHeight - h - 20f, 400f, h);
            Fill(crewRect, new Color(0f, 0f, 0f, 0.45f));
            float y = crewRect.y + 8f;
            Shadowed(new Rect(crewRect.x + 14f, y, 380f, 32f), $"CREW  {crew}/4", mid, Color.white);
            y += 36f;
            Shadowed(new Rect(crewRect.x + 14f, y, 380f, 28f), $"P1  Driver  -  {(rover.Autopilot ? "Autopilot" : "Keyboard")}", small, GameManager.PlayerColors[0]);
            foreach (var t in rover.Turrets)
            {
                y += 30f;
                Shadowed(new Rect(crewRect.x + 14f, y, 380f, 28f), $"P{t.PlayerNumber}  Gunner  -  {t.ControlLabel}", small, t.Color);
            }
            y += 30f;
            if (crew < 4) Shadowed(new Rect(crewRect.x + 14f, y, 380f, 28f), "Gamepad: press A to join", small, new Color(0.6f, 0.6f, 0.7f));

            if (gm.Upgrades.Taken.Count > 0)
                Shadowed(new Rect(520f, RefHeight - 50f, width - 1000f, 30f), "Upgrades: " + string.Join(", ", gm.Upgrades.Taken), small, new Color(0.6f, 1f, 0.7f));
            if (rover.Autopilot)
                Shadowed(new Rect(width * 0.5f - 300f, RefHeight - 90f, 600f, 30f), "AUTOPILOT DEMO  (F2 to take control)", mid, new Color(0.4f, 0.9f, 1f), true);

            DrawCrosshair(rover);
        }

        void DrawWorldOverlays(GameManager gm)
        {
            var cam = CameraRig.I != null ? CameraRig.I.Cam : null;
            if (cam == null) return;
            foreach (var e in EnemyBase.All)
            {
                if (e.IsBoss && !gm.AIDebug) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (e.IsBoss ? 6f : 2.6f));
                if (sp.z < 0f) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
                if (e.Health.Fraction < 1f && !e.IsBoss) Bar(new Rect(x - 25f, y, 50f, 6f), e.Health.Fraction, new Color(1f, 0.3f, 0.3f));
                if (gm.AIDebug)
                    Shadowed(new Rect(x - 90f, y - 44f, 180f, 44f), $"{e.DisplayName}\n[{e.StateName}]", small, StateColor(e.StateName), true);
            }
            if (gm.AIDebug)
            {
                Fill(new Rect(20f, 205f, 360f, 118f), new Color(0f, 0f, 0f, 0.45f));
                Shadowed(new Rect(32f, 210f, 340f, 110f),
                    "AI DEBUG (F1)\nLabels: FSM state per enemy\nGreen lines: A* paths\nRed tiles: blocked grid cells", small, new Color(0.5f, 1f, 0.6f));
            }
        }

        void DrawUpgrade(GameManager gm)
        {
            Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.6f));
            float cx = width * 0.5f;
            Shadowed(new Rect(cx - 600f, 230f, 1200f, 70f), $"WAVE {gm.Waves.Wave} SURVIVED", huge, new Color(0.5f, 1f, 0.6f), true);
            Shadowed(new Rect(cx - 600f, 300f, 1200f, 40f), "Choose an upgrade for the rover  (1 / 2 / 3, gamepad X / Y / B, or click)", mid, Color.white, true);
            var offer = gm.Upgrades.Offer;
            float cardW = 340f, gap = 30f;
            float x0 = cx - (offer.Count * cardW + (offer.Count - 1) * gap) * 0.5f;
            for (int i = 0; i < offer.Count; i++)
            {
                var card = new Rect(x0 + i * (cardW + gap), 400f, cardW, 220f);
                Fill(card, new Color(0.08f, 0.12f, 0.2f, 0.95f));
                Fill(new Rect(card.x, card.y, card.width, 6f), new Color(0.4f, 1f, 0.6f));
                Shadowed(new Rect(card.x, card.y + 20f, card.width, 40f), $"[{i + 1}]", big, new Color(0.4f, 1f, 0.6f), true);
                Shadowed(new Rect(card.x + 10f, card.y + 70f, card.width - 20f, 40f), offer[i].Name, big, Color.white, true);
                Shadowed(new Rect(card.x + 10f, card.y + 120f, card.width - 20f, 80f), offer[i].Description, mid, new Color(0.8f, 0.85f, 0.9f), true);
                if (GUI.Button(card, GUIContent.none, GUIStyle.none)) gm.ChooseUpgrade(i);
            }
        }

        void DrawPaused()
        {
            Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.6f));
            Shadowed(new Rect(0f, 400f, width, 90f), "PAUSED", title, Color.white, true);
            Shadowed(new Rect(0f, 520f, width, 40f), "Esc to resume   |   R to restart", mid, Color.white, true);
        }

        void DrawGameOver(GameManager gm)
        {
            Fill(new Rect(0f, 0f, width, RefHeight), new Color(0.15f, 0f, 0f, 0.55f));
            Shadowed(new Rect(0f, 300f, width, 120f), "ROVER DESTROYED", title, new Color(1f, 0.35f, 0.25f), true);
            Shadowed(new Rect(0f, 440f, width, 50f), $"Survived to wave {gm.Waves.Wave}", big, Color.white, true);
            Shadowed(new Rect(0f, 500f, width, 40f),
                $"Time {Mathf.FloorToInt(gm.RunTime / 60f):00}:{Mathf.FloorToInt(gm.RunTime % 60f):00}    Kills {gm.Kills}    Score {gm.Score}", mid, Color.white, true);
            if (Mathf.Repeat(Time.unscaledTime, 1.2f) < 0.8f)
                Shadowed(new Rect(0f, 600f, width, 50f), "Press R or Enter to try again", big, new Color(0.4f, 1f, 0.6f), true);
        }

        void DrawBanner()
        {
            float alpha = Mathf.Clamp01((bannerUntil - Time.unscaledTime) / 0.5f);
            var c = bannerColor;
            c.a = alpha;
            Shadowed(new Rect(0f, 200f, width, 70f), bannerTitle, huge, c, true, alpha);
            if (!string.IsNullOrEmpty(bannerSub))
                Shadowed(new Rect(0f, 270f, width, 40f), bannerSub, mid, new Color(1f, 1f, 1f, alpha), true, alpha);
        }

        void DrawCrosshair(Rover rover)
        {
            var mouse = Mouse.current;
            if (mouse == null || rover.Autopilot) return;
            Vector2 m = mouse.position.ReadValue();
            float x = m.x / scale, y = (Screen.height - m.y) / scale;
            var c = GameManager.PlayerColors[1];
            Fill(new Rect(x - 12f, y - 1.5f, 8f, 3f), c);
            Fill(new Rect(x + 4f, y - 1.5f, 8f, 3f), c);
            Fill(new Rect(x - 1.5f, y - 12f, 3f, 8f), c);
            Fill(new Rect(x - 1.5f, y + 4f, 3f, 8f), c);
        }

        // ---------- helpers ----------

        static Color StateColor(string state) => state switch
        {
            "Attack" or "Dash" or "Charge" or "Barrage" => new Color(1f, 0.35f, 0.3f),
            "WindUp" or "ChargeWindUp" or "Summon" or "Enrage" => new Color(1f, 0.8f, 0.2f),
            "Retreat" or "Recover" => new Color(0.6f, 0.8f, 1f),
            _ => new Color(0.6f, 1f, 0.6f),
        };

        void Row(ref float y, float x, string label, string text, Color labelColor, GUIStyle style)
        {
            Shadowed(new Rect(x, y, 260f, 36f), label, style, labelColor);
            if (!string.IsNullOrEmpty(text)) Shadowed(new Rect(x + 250f, y, 760f, 36f), text, mid, Color.white);
            y += style == big ? 44f : 34f;
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Bar(Rect r, float fraction, Color fg)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * Mathf.Clamp01(fraction), r.height - 4f), fg);
        }

        void Shadowed(Rect r, string text, GUIStyle style, Color color, bool center = false, float alpha = 1f)
        {
            var s = style;
            if (center)
            {
                if (!centeredVariants.TryGetValue(style, out s))
                    centeredVariants[style] = s = new GUIStyle(style) { alignment = TextAnchor.UpperCenter };
            }
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f * alpha);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, s);
            GUI.color = color;
            GUI.Label(r, text, s);
            GUI.color = old;
        }

        void EnsureStyles()
        {
            if (title != null) return;
            GUIStyle Make(int size, FontStyle fs) => new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fs,
                normal = { textColor = Color.white },
                wordWrap = true,
            };
            title = Make(110, FontStyle.Bold);
            title.alignment = TextAnchor.UpperCenter;
            huge = Make(48, FontStyle.Bold);
            big = Make(32, FontStyle.Bold);
            mid = Make(24, FontStyle.Normal);
            small = Make(20, FontStyle.Normal);
        }
    }
}
