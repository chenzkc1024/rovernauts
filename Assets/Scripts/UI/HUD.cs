using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Mission selection, combat HUD, boss bar, upgrade cards,
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
        float scale, width, top, bottom;
        static readonly Color Ink = new Color(0.028f, 0.048f, 0.075f, 0.94f);
        static readonly Color Border = new Color(0.32f, 0.5f, 0.62f, 0.5f);
        static readonly Color Pale = new Color(0.73f, 0.82f, 0.88f);
        static readonly Color Threat = new Color(1f, 0.36f, 0.27f);
        static readonly Color Signal = new Color(0.36f, 0.88f, 1f);

        public static void Banner(string text, string subtitle, Color color, float seconds = 3f)
        {
            if (instance == null) return;
            instance.bannerTitle = text;
            instance.bannerSub = subtitle;
            instance.bannerColor = color;
            instance.bannerUntil = Time.unscaledTime + seconds;
        }

        void Awake() => instance = this;

        // ---------- damage feedback ----------

        float lastHull = -1f, damageFlash;
        static Texture2D vignette;

        /// <summary>
        /// Watches the hull instead of hooking damage events so it works the same on the host and the LAN gunner.
        /// Any loss shakes the camera and flashes the screen edges red, harder for bigger hits.
        /// </summary>
        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Rover == null) return;
            float hull = gm.Rover.Health.Current;
            if (lastHull >= 0f && hull < lastHull - 0.01f && gm.State == GameState.Playing)
            {
                float lost = lastHull - hull;
                damageFlash = Mathf.Clamp01(Mathf.Max(damageFlash, 0.5f + lost / 30f));
                CameraRig.Shake(0.25f + lost * 0.03f);
            }
            lastHull = hull;
            damageFlash = Mathf.Max(0f, damageFlash - Time.unscaledDeltaTime * 1.5f);
        }

        void DrawDamageVignette(GameManager gm)
        {
            float alpha = damageFlash;
            float hull = gm.Rover.Health.Fraction;
            // Below 30% hull the edges keep a slow red pulse as a warning.
            if (hull < 0.3f && !gm.Rover.Health.IsDead)
                alpha = Mathf.Max(alpha, (0.3f - hull) / 0.3f * (0.22f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f)));
            if (alpha <= 0.01f) return;
            if (vignette == null) vignette = BuildVignette();
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(0f, top, width, bottom - top), vignette, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        /// <summary>Red at the screen edges fading to clear well before the centre.</summary>
        static Texture2D BuildVignette()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)); // 0 at the edge, 0.5 at the centre
                float a = 1f - Mathf.SmoothStep(0f, 1f, edge / 0.24f);
                pixels[x + y * size] = new Color32(230, 20, 12, (byte)(a * a * 235f));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnGUI()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            EnsureStyles();
            if (Event.current.type != EventType.Repaint) return;
            scale = Mathf.Min(Screen.height / RefHeight, Screen.width / 1200f);
            width = Screen.width / scale;
            float offset = (Screen.height - RefHeight * scale) * 0.5f;
            top = -offset / scale;
            bottom = RefHeight + offset / scale;
            GUI.matrix = Matrix4x4.TRS(new Vector3(0f, offset, 0f), Quaternion.identity, Vector3.one * scale);

            if (gm.ControlsUI.Open)
            {
                if (gm.State != GameState.Menu) DrawRunHud(gm);
                DrawControls(gm.ControlsUI);
                return;
            }

            switch (gm.State)
            {
                case GameState.Menu:
                    DrawMenu(gm);
                    break;
                case GameState.Playing:
                    DrawDamageVignette(gm);
                    DrawWorldOverlays(gm);
                    DrawRunHud(gm);
                    break;
                case GameState.Upgrade:
                    DrawRunHud(gm);
                    DrawUpgrade(gm);
                    break;
                case GameState.Paused:
                    DrawRunHud(gm);
                    DrawPaused(gm);
                    break;
                case GameState.Victory:
                case GameState.GameOver:
                    DrawResults(gm);
                    break;
            }
            if (gm.State == GameState.Playing && Time.unscaledTime < bannerUntil) DrawBanner();
        }

        // ---------- screens ----------

        void DrawMenu(GameManager gm)
        {
            if (gm.Lan.Screen != LanSession.Page.Closed)
            {
                DrawLanMenu(gm.Lan);
                return;
            }
            Fill(new Rect(0f, top, width, bottom - top), new Color(0.01f, 0.03f, 0.06f, 0.16f));
            float panelWidth = Mathf.Min(775f, width * 0.64f);
            Fill(new Rect(0f, top, panelWidth, bottom - top), Ink);
            Fill(new Rect(panelWidth, top, 2f, bottom - top), Border);
            Shadowed(new Rect(55f, 73f, panelWidth - 90f, 103f), "ROVERNAUTS", title, Color.white);
            Shadowed(new Rect(58f, 171f, panelWidth - 115f, 78f),
                "Drive a lunar rover and hold back the swarm.", big, Pale);

            var endless = new Rect(57f, 272f, panelWidth - 114f, 91f);
            DrawCard(endless, "ENDLESS SURVIVAL", "Enter to launch", LunarArt.Orange);
            DrawCard(new Rect(endless.x, 378f, endless.width, 91f), "CRATER EXPEDITION",
                "Storm, upgrades and a boss  /  E to launch", Signal);

            if (gm.Mode is CrewMode.Solo or CrewMode.Local)
                DrawCard(new Rect(endless.x, 484f, endless.width, 91f), "WATCH DEMO",
                    "Autopilot plays Crater Expedition  /  F2", Pale);

            float y = 617f;
            bool local = gm.Mode == CrewMode.Local;
            Shadowed(new Rect(57f, y, panelWidth - 110f, 45f), local ? "LOCAL CO-OP" : "SOLO", big, Color.white);
            y += 51f;
            var profile = Controls.ProfileFor(gm.Mode);
            ControlRow(ref y, panelWidth, local ? "PLAYER 1" : "DRIVE", DriveKeys(profile),
                $"{Controls.Describe(profile, Act.Boost)} to boost");
            ControlRow(ref y, panelWidth, local ? "PLAYER 2" : "GUN", Controls.DescribeAim(profile) + " to aim",
                $"Hold {Controls.Describe(profile, Act.Fire)} to fire");
            Shadowed(new Rect(57f, 849f, panelWidth - 110f, 35f),
                (local ? "C  Solo" : "C  Local co-op") + "   /   L  LAN co-op   /   K  Controls", mid, Color.white);
            if (gm.Lan.Status.Length > 0)
                Shadowed(new Rect(57f, 887f, panelWidth - 110f, 38f), gm.Lan.Status, small, Pale);

            DrawCard(new Rect(57f, 949f, 230f, 78f), gm.Music.Muted ? "MUSIC OFF" : "MUSIC ON", "M", Pale);
            DrawCard(new Rect(300f, 949f, 160f, 78f), "QUIT", "Q", Pale);
        }

        void DrawLanMenu(LanSession lan)
        {
            Dim();
            float x = width * 0.5f - 380f;
            Panel(new Rect(x, 245f, 760f, 580f), Signal);
            float textX = x + 32f;
            Shadowed(new Rect(textX, 274f, 695f, 70f), "LAN CO-OP", huge, Color.white);
            switch (lan.Screen)
            {
                case LanSession.Page.Menu:
                    DrawCard(new Rect(textX, 386f, 695f, 90f), "HOST AS DRIVER",
                        $"H  /  {DriveKeys(ControlProfile.Solo)} and {Controls.Describe(ControlProfile.Solo, Act.Boost)}", Signal);
                    DrawCard(new Rect(textX, 500f, 695f, 90f), "JOIN AS GUNNER", "J  /  " + GunnerKeys(), LunarArt.Orange);
                    break;
                case LanSession.Page.Host:
                    Shadowed(new Rect(textX, 372f, 695f, 50f), "Share your local IP with your gunner", big, Color.white);
                    Shadowed(new Rect(textX, 433f, 695f, 69f), lan.HostAddresses, small, Signal);
                    if (lan.Ready)
                    {
                        DrawCard(new Rect(textX, 505f, 332f, 95f), "ENDLESS SURVIVAL", "Enter", LunarArt.Orange, mid);
                        DrawCard(new Rect(textX + 349f, 505f, 346f, 95f), "CRATER EXPEDITION", "E", Signal, mid);
                    }
                    break;
                case LanSession.Page.Join:
                    Shadowed(new Rect(textX, 373f, 695f, 45f), "Enter the host's local IPv4 address", big, Color.white);
                    Panel(new Rect(textX, 445f, 695f, 76f), Pale);
                    Shadowed(new Rect(textX + 18f, 462f, 670f, 40f), lan.Address.Length == 0 ? "e.g. 192.168.1.25" : lan.Address, big, Pale);
                    Shadowed(new Rect(textX, 550f, 695f, 40f), "Enter to connect", mid, Signal);
                    break;
                case LanSession.Page.Connecting:
                case LanSession.Page.Guest:
                    Shadowed(new Rect(textX, 395f, 695f, 60f), "GUNNER  /  " + GunnerKeys(), big, Color.white);
                    break;
            }
            Shadowed(new Rect(textX, 662f, 695f, 40f), lan.Status, mid, Pale);
            Shadowed(new Rect(textX, 751f, 695f, 40f), "Esc  /  Back", small, Pale);
        }

        static string DriveKeys(ControlProfile p) => Controls.DescribeDirections(p, Act.Forward, Act.TurnLeft, Act.Reverse, Act.TurnRight);
        static string GunnerKeys() => $"{Controls.DescribeAim(ControlProfile.Solo)} and {Controls.Describe(ControlProfile.Solo, Act.Fire)}";

        void DrawControls(ControlsMenu menu)
        {
            Dim();
            float panelW = Mathf.Min(1000f, width - 40f);
            float x = width * 0.5f - panelW * 0.5f;
            Panel(new Rect(x, 40f, panelW, 1000f), Signal);
            float textX = x + 34f, innerW = panelW - 68f;
            Shadowed(new Rect(textX, 58f, innerW, 60f), "CONTROLS", huge, Color.white);

            bool solo = menu.Profile == ControlProfile.Solo;
            var tabSolo = new Rect(textX, 126f, 260f, 44f);
            var tabCoop = new Rect(textX + 272f, 126f, 300f, 44f);
            var on = new Color(0.36f, 0.88f, 1f, 0.22f);
            var off = new Color(1f, 1f, 1f, 0.04f);
            Fill(tabSolo, solo ? on : off);
            Fill(tabCoop, solo ? off : on);
            Shadowed(new Rect(tabSolo.x, tabSolo.y + 8f, tabSolo.width, 30f), "SOLO / LAN", mid, solo ? Color.white : Pale, true);
            Shadowed(new Rect(tabCoop.x, tabCoop.y + 8f, tabCoop.width, 30f), "LOCAL CO-OP", mid, solo ? Pale : Color.white, true);
            Shadowed(new Rect(tabCoop.xMax + 16f, 134f, 200f, 30f), "Tab to switch", small, Pale);

            float colAction = textX + 16f, colA = textX + innerW * 0.45f, colB = textX + innerW * 0.73f;
            float colW = innerW * 0.26f;
            Shadowed(new Rect(colA, 186f, colW, 28f), "PRIMARY", small, Signal);
            Shadowed(new Rect(colB, 186f, colW, 28f), "ALTERNATE", small, Signal);

            float y = 216f, rowH = 50f;
            var highlight = new Color(0.36f, 0.88f, 1f, 0.25f);
            for (int row = 0; row < Controls.ActionCount + 2; row++, y += rowH)
            {
                bool selected = row == menu.Row;
                Fill(new Rect(textX, y, innerW, 1f), Border);
                if (selected) Fill(new Rect(textX, y + 1f, innerW, rowH - 1f), new Color(0.36f, 0.88f, 1f, 0.1f));
                if (row >= Controls.ActionCount)
                {
                    bool aimRow = row == Controls.ActionCount;
                    string label = aimRow ? (solo ? "Aim with" : "P2  Aim with") : "Steering in reverse";
                    string value = aimRow
                        ? (Controls.MouseAim(menu.Profile) ? "Mouse  (aim keys still override)" : "Aim keys only")
                        : (Controls.CarSteering ? "Car  (back swings toward the key)" : "Tank  (turns toward the key)");
                    Shadowed(new Rect(colAction, y + 10f, colA - colAction, 32f), label, mid, Color.white);
                    var cell = new Rect(colA - 10f, y + 6f, innerW * 0.55f, rowH - 12f);
                    if (selected) Fill(cell, highlight);
                    Shadowed(new Rect(colA, y + 12f, cell.width - 20f, 30f), value, small, Color.white);
                    continue;
                }
                var action = (Act)row;
                Shadowed(new Rect(colAction, y + 10f, colA - colAction, 32f), Controls.ActionLabel(menu.Profile, action), mid, Color.white);
                for (int slot = 0; slot < Controls.Slots; slot++)
                {
                    var cell = new Rect((slot == 0 ? colA : colB) - 10f, y + 6f, colW, rowH - 12f);
                    bool active = selected && slot == menu.Column;
                    bool waiting = active && menu.Capturing;
                    if (active) Fill(cell, waiting ? new Color(1f, 0.7f, 0.2f, 0.35f) : highlight);
                    int input = Controls.Get(menu.Profile, action, slot);
                    string text = waiting ? "Press a key..." : Controls.InputName(input);
                    Shadowed(new Rect(cell.x + 10f, y + 10f, cell.width - 14f, 32f), text, mid,
                        input == Controls.None && !waiting ? Pale : Color.white);
                }
            }

            Shadowed(new Rect(textX, y + 12f, innerW, 34f), menu.Status, small, new Color(1f, 0.85f, 0.4f));
            Shadowed(new Rect(textX, 958f, innerW, 70f),
                "Up/Down  choose     Left/Right  primary or alternate     Enter  rebind\nDelete  clear     Backspace  restore defaults     Esc  save and close",
                small, Pale);
        }

        void ControlRow(ref float y, float span, string command, string input, string help)
        {
            Fill(new Rect(57f, y, span - 114f, 1f), Border);
            Shadowed(new Rect(57f, y + 12f, 140f, 30f), command, small, Signal);
            Shadowed(new Rect(203f, y + 9f, span - 270f, 32f), input, mid, Color.white);
            Shadowed(new Rect(203f, y + 41f, span - 265f, 32f), help, small, Pale);
            y += 85f;
        }

        void DrawRunHud(GameManager gm)
        {
            var waves = gm.Waves;
            var rover = gm.Rover;

            // Top-left: run info.
            Panel(new Rect(24f, 24f, 350f, 151f), Signal);
            Shadowed(new Rect(40f, 32f, 330f, 57f), gm.Expedition ? $"WAVE {waves.Wave} / 5" : $"WAVE {waves.Wave}", huge, Color.white);
            int alive = EnemyBase.All.Count;
            string incoming = waves.QueuedCount > 0 ? $"  (+{waves.QueuedCount} incoming)" : "";
            Shadowed(new Rect(40f, 91f, 322f, 30f), $"Hostiles  {alive}{incoming}", mid, Color.white);
            Shadowed(new Rect(40f, 128f, 330f, 30f), $"Time {Mathf.FloorToInt(gm.RunTime / 60f):00}:{Mathf.FloorToInt(gm.RunTime % 60f):00}    Kills {gm.Kills}    Score {gm.Score}", small, Color.white);
            if (waves.CountdownLeft > 0f)
                Shadowed(new Rect(width * 0.5f - 200f, 330f, 400f, 50f), $"Incoming in {waves.CountdownLeft:0.0}", big, Color.white, true);

            // Top-right: active moon event.
            var ev = gm.Events.Active;
            if (ev != MoonEvent.None)
            {
                string label = ev == MoonEvent.BloodMoon ? "BLOOD MOON" : "ASTEROID STORM";
                Color c = ev == MoonEvent.BloodMoon ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.65f, 0.3f);
                Panel(new Rect(width - 342f, 24f, 318f, 50f), c);
                Shadowed(new Rect(width - 326f, 29f, 300f, 40f), label, mid, c);
            }

            // Top-centre: boss bar.
            var boss = waves.ActiveBoss;
            if (boss != null && !boss.Health.IsDead)
            {
                float barWidth = Mathf.Min(width - 770f, 650f);
                var bar = new Rect(width * 0.5f - barWidth * 0.5f, 62f, barWidth, 16f);
                Panel(new Rect(bar.x - 13f, 24f, bar.width + 26f, 67f), Threat);
                string name = boss.DisplayName.ToUpper() + (boss.Enraged ? "  -  ENRAGED" : "");
                Shadowed(new Rect(bar.x, 27f, bar.width, 32f), name, mid, new Color(1f, 0.5f, 0.4f), true);
                Bar(bar, boss.Health.Fraction, boss.Enraged ? new Color(1f, 0.4f, 0.05f) : new Color(0.85f, 0.15f, 0.15f));
            }

            // Bottom-left: hull and boost.
            var hullRect = new Rect(30f, RefHeight - 110f, 460f, 30f);
            Panel(new Rect(20f, RefHeight - 150f, 480f, 130f), Signal);
            Shadowed(new Rect(30f, RefHeight - 145f, 460f, 32f), $"HULL  {rover.Health.Current:0} / {rover.Health.MaxHp:0}" + (gm.GodMode ? "   [GOD MODE]" : ""), mid, Color.white);
            Bar(hullRect, rover.Health.Fraction, Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.95f, 0.45f), rover.Health.Fraction));
            Shadowed(new Rect(30f, RefHeight - 72f, 120f, 28f), "BOOST", small, Color.white);
            Bar(new Rect(110f, RefHeight - 66f, 380f, 14f), rover.BoostReady, rover.BoostReady >= 1f ? new Color(1f, 0.7f, 0.2f) : new Color(0.5f, 0.4f, 0.3f));

            if (gm.Upgrades.Taken.Count > 0)
                Shadowed(new Rect(29f, RefHeight - 189f, 480f, 35f), $"Mods installed  {gm.Upgrades.Taken.Count}", small, new Color(0.6f, 1f, 0.7f));
            if (gm.Mode != CrewMode.Solo)
            {
                string role = gm.Mode switch
                {
                    CrewMode.Local => "P1 DRIVE  /  P2 GUN",
                    CrewMode.LanDriver => "LAN DRIVER",
                    _ => "LAN GUNNER  /  F8 to leave",
                };
                Shadowed(new Rect(width - 355f, RefHeight - 73f, 330f, 45f), role, small, Pale);
            }
            if (rover.Autopilot && gm.State == GameState.Playing)
            {
                var takeover = new Rect(width * 0.5f - 90f, RefHeight - 105f, 195f, 85f);
                DrawCard(takeover, "TAKE CONTROL", "F2", Signal, small);
            }

            if (gm.State == GameState.Playing)
            {
                DrawRadar(rover, ev == MoonEvent.None ? 24f : 91f);
                DrawCrosshair(rover);
            }
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
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale + top;
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
            Dim();
            float cx = width * 0.5f;
            Shadowed(new Rect(cx - 600f, 230f, 1200f, 70f), $"WAVE {gm.Waves.Wave} SURVIVED", huge, new Color(0.5f, 1f, 0.6f), true);
            float demoTime = gm.DemoUpgradeRemaining;
            string prompt = gm.Mode == CrewMode.LanGunner ? "The driver is choosing an upgrade" : demoTime >= 0f
                ? $"Autopilot installs the first upgrade in {Mathf.CeilToInt(demoTime)}s  /  Press 1-3 to choose"
                : "Press 1-3 to choose an upgrade  /  Esc to pause";
            Shadowed(new Rect(cx - 600f, 300f, 1200f, 40f), prompt, mid, Color.white, true);
            var offer = gm.Upgrades.Offer;
            float cardW = Mathf.Min(340f, (width - 100f) / 3f), gap = 24f;
            float x0 = cx - (offer.Count * cardW + (offer.Count - 1) * gap) * 0.5f;
            for (int i = 0; i < offer.Count; i++)
            {
                var card = new Rect(x0 + i * (cardW + gap), 400f, cardW, 220f);
                Panel(card, Signal);
                Fill(new Rect(card.x, card.y, card.width, 6f), new Color(0.4f, 1f, 0.6f));
                Shadowed(new Rect(card.x, card.y + 20f, card.width, 40f), $"[{i + 1}]", big, new Color(0.4f, 1f, 0.6f), true);
                Shadowed(new Rect(card.x + 10f, card.y + 70f, card.width - 20f, 40f), offer[i].Name, big, Color.white, true);
                Shadowed(new Rect(card.x + 10f, card.y + 120f, card.width - 20f, 80f), offer[i].Description, mid, new Color(0.8f, 0.85f, 0.9f), true);
            }
        }

        void DrawPaused(GameManager gm)
        {
            Dim();
            float cx = width * 0.5f;
            bool guest = gm.Mode == CrewMode.LanGunner;
            Panel(new Rect(cx - 280f, 320f, 560f, guest ? 460f : 540f), Signal);
            Shadowed(new Rect(cx - 245f, 361f, 490f, 80f), "PAUSED", huge, Color.white, true);
            DrawCard(new Rect(cx - 216f, 457f, 432f, 78f), guest ? "DRIVER PAUSED" : "RESUME",
                guest ? "Waiting for the driver" : "Esc", Signal);
            DrawCard(new Rect(cx - 216f, 539f, 432f, 78f), guest ? "LEAVE SESSION" : "RETURN TO TITLE", "R", Pale);
            if (!guest)
            {
                DrawCard(new Rect(cx - 216f, 621f, 432f, 78f), gm.Music.Muted ? "MUSIC OFF" : "MUSIC ON", "M", Pale);
                DrawCard(new Rect(cx - 216f, 703f, 432f, 78f), "CONTROLS", "K", Pale);
            }
        }

        void DrawResults(GameManager gm)
        {
            bool won = gm.State == GameState.Victory;
            Color accent = won ? Signal : Threat;
            Dim();
            float cx = width * 0.5f;
            Panel(new Rect(cx - 390f, 270f, 780f, 505f), accent);
            Shadowed(new Rect(cx - 370f, 321f, 740f, 80f),
                won ? "EXPEDITION COMPLETE" : "ROVER DESTROYED", huge, accent, true);
            Shadowed(new Rect(cx - 370f, 425f, 740f, 52f),
                $"Waves cleared  {gm.Waves.Cleared}       Hostiles down  {gm.Kills}", big, Color.white, true);
            Shadowed(new Rect(cx - 350f, 491f, 700f, 36f),
                $"Time  {Mathf.FloorToInt(gm.RunTime / 60f):00}:{Mathf.FloorToInt(gm.RunTime % 60f):00}       Score  {gm.Score}",
                mid, Pale, true);
            DrawCard(new Rect(cx - 240f, 614f, 480f, 82f), "RETURN TO TITLE", "Enter / R", accent);
        }

        void DrawRadar(Rover rover, float y)
        {
            var rect = new Rect(width - 204f, y, 180f, 180f);
            Panel(rect, Signal);
            var inner = new Rect(rect.x + 13f, rect.y + 13f, rect.width - 26f, rect.height - 26f);
            Fill(new Rect(inner.center.x, inner.y, 1f, inner.height), Border);
            Fill(new Rect(inner.x, inner.center.y, inner.width, 1f), Border);

            // Centred on the rover: the moon wraps, so every hostile is within half a tile in each direction.
            Vector3 centre = rover.transform.position;
            Vector2 Plot(Vector3 point) => inner.center +
                new Vector2(point.x - centre.x, centre.z - point.z) * (inner.width / (2f * WorldBuilder.HalfSize));

            foreach (var enemy in EnemyBase.All)
            {
                Vector2 point = Plot(enemy.transform.position);
                float dot = enemy.IsBoss ? 10f : 5f;
                Fill(new Rect(point.x - dot * 0.5f, point.y - dot * 0.5f, dot, dot), Threat);
            }

            Vector2 ours = Plot(rover.transform.position);
            Fill(new Rect(ours.x - 4f, ours.y - 4f, 8f, 8f), Signal);
        }

        void DrawBanner()
        {
            float alpha = Mathf.Clamp01((bannerUntil - Time.unscaledTime) / 0.5f);
            var c = bannerColor;
            c.a = alpha;
            float panelWidth = Mathf.Min(width - 120f, 940f);
            float x = (width - panelWidth) * 0.5f;
            Fill(new Rect(x, 197f, panelWidth, 115f), new Color(0.025f, 0.045f, 0.07f, alpha * 0.91f));
            Fill(new Rect(x, 197f, panelWidth, 2f), c);
            Shadowed(new Rect(x + 14f, 204f, panelWidth - 28f, 66f), bannerTitle, huge, c, true, alpha);
            if (!string.IsNullOrEmpty(bannerSub))
                Shadowed(new Rect(0f, 270f, width, 40f), bannerSub, mid, new Color(1f, 1f, 1f, alpha), true, alpha);
        }

        void DrawCrosshair(Rover rover)
        {
            var cam = CameraRig.I != null ? CameraRig.I.Cam : null;
            if (cam == null || rover.Autopilot) return;
            var turret = rover.MainTurret;
            Vector3 target = turret.transform.position + turret.AimDirection * 7f;
            target.y = Projectile.PlaneHeight;
            Vector3 screen = cam.WorldToScreenPoint(target);
            if (screen.z <= 0f) return;
            float x = screen.x / scale, y = (Screen.height - screen.y) / scale + top;
            var c = Turret.Accent;
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

        void Dim() => Fill(new Rect(0f, top, width, bottom - top), new Color(0.01f, 0.024f, 0.049f, 0.73f));

        static void Panel(Rect rect, Color accent)
        {
            Fill(rect, Ink);
            Fill(new Rect(rect.x, rect.y, rect.width, 1f), Border);
            Fill(new Rect(rect.x, rect.y, 3f, rect.height), accent);
        }

        void DrawCard(Rect rect, string label, string detail, Color accent, GUIStyle labelStyle = null)
        {
            Panel(rect, accent);
            Shadowed(new Rect(rect.x + 17f, rect.y + 7f, rect.width - 32f, 39f), label, labelStyle ?? big, accent);
            Shadowed(new Rect(rect.x + 18f, rect.y + 42f, rect.width - 32f, 32f), detail, small, Pale);
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
            title = Make(82, FontStyle.Bold);
            huge = Make(48, FontStyle.Bold);
            big = Make(32, FontStyle.Bold);
            mid = Make(24, FontStyle.Normal);
            small = Make(20, FontStyle.Normal);
        }
    }
}
