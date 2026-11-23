using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Rovernauts
{
    public enum GameState { Menu, Playing, Upgrade, Paused, GameOver, Victory }

    /// <summary>
    /// Entry point. Builds the whole game at runtime (arena, pathfinding grid, rover, camera, directors, HUD)
    /// and owns the top-level game state: Menu -> Playing <-> Upgrade/Paused -> GameOver.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public GameState State { get; private set; } = GameState.Menu;
        public bool Expedition { get; private set; }
        public float RunTime { get; private set; }
        public int Kills { get; private set; }
        public int Score { get; private set; }
        public bool AIDebug { get; private set; }
        public bool GodMode { get; private set; }
        public MissionScore Music { get; private set; }
        public Rover Rover { get; private set; }
        public WaveDirector Waves { get; private set; }
        public EventDirector Events { get; private set; }
        public UpgradeSystem Upgrades { get; private set; }
        public CrewMode Mode { get; internal set; }
        internal LanSession Lan { get; private set; }

        const float DemoUpgradeDelay = 4f;
        static string nextTitleStatus;
        GameState stateBeforePause;
        bool demoRun;
        float upgradeOfferedAt;

        public float DemoUpgradeRemaining => demoRun && Rover.Autopilot && State == GameState.Upgrade
            ? Mathf.Max(0f, DemoUpgradeDelay - (Time.unscaledTime - upgradeOfferedAt))
            : -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureGameExists()
        {
            // Lets the game run from any scene (even an empty one) and survive scene reloads.
            if (FindAnyObjectByType<GameManager>() == null) new GameObject("Rovernauts Game").AddComponent<GameManager>();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<GameManager>() == null) new GameObject("Rovernauts Game").AddComponent<GameManager>();
        }

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
            Application.targetFrameRate = 120;
            EnemyBase.All.Clear();
            Lan = new LanSession(nextTitleStatus);
            nextTitleStatus = null;

            gameObject.AddComponent<AudioManager>();
            Music = gameObject.AddComponent<MissionScore>();
            WorldBuilder.Build(486);
            GridPathfinder.Instance = new GridPathfinder(WorldBuilder.HalfSize, 2f, 0.45f);
            Rover = Rover.Create(Vector3.zero);
            CameraRig.Create(Rover.transform);

            Upgrades = new UpgradeSystem();
            Events = gameObject.AddComponent<EventDirector>();
            Waves = gameObject.AddComponent<WaveDirector>();
            gameObject.AddComponent<HUD>();
            gameObject.AddComponent<AutoTest>();
            EnemyBase.AnyKilled += OnEnemyKilled;
        }

        void OnDestroy()
        {
            Lan?.Dispose();
            if (I != this) return;
            EnemyBase.AnyKilled -= OnEnemyKilled;
            Time.timeScale = 1f;
            I = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (Lan.Tick(this, kb)) return;
            if (Mode == CrewMode.Solo && kb != null && kb.f2Key.wasPressedThisFrame)
            {
                if (State == GameState.Menu)
                {
                    StartDemo();
                    return;
                }
                if (State is GameState.Playing or GameState.Paused or GameState.Upgrade)
                    SetAutopilot(!Rover.Autopilot);
            }

            bool confirm = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            bool escape = kb != null && kb.escapeKey.wasPressedThisFrame;
            if (kb != null && kb.mKey.wasPressedThisFrame && State is GameState.Menu or GameState.Paused)
                Music.Muted = !Music.Muted;

            switch (State)
            {
                case GameState.Menu:
                    if (kb != null && kb.cKey.wasPressedThisFrame)
                        Mode = Mode == CrewMode.Local ? CrewMode.Solo : CrewMode.Local;
                    else if (kb != null && kb.qKey.wasPressedThisFrame) Application.Quit();
                    else if (kb != null && kb.eKey.wasPressedThisFrame) StartExpedition();
                    else if (confirm) StartRun();
                    break;
                case GameState.Playing:
                    RunTime += Time.deltaTime;
                    if (escape) SetPaused(true);
                    break;
                case GameState.Paused:
                    if (kb != null && kb.rKey.wasPressedThisFrame) Restart();
                    else if (escape) SetPaused(false);
                    break;
                case GameState.Upgrade:
                    HandleUpgradeInput(kb);
                    if (DemoUpgradeRemaining == 0f) ChooseUpgrade(0);
                    break;
                case GameState.Victory:
                case GameState.GameOver:
                    if (confirm || (kb != null && kb.rKey.wasPressedThisFrame)) Restart();
                    break;
            }

            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) SetAIDebug(!AIDebug);
                if (kb.f3Key.wasPressedThisFrame && State == GameState.Playing) Waves.SkipToNextBoss();
                if (kb.f4Key.wasPressedThisFrame) SetGodMode(!GodMode);
            }
        }

        public void StartRun(int startWave = 1)
        {
            if (State != GameState.Menu || Mode == CrewMode.LanGunner || (Mode == CrewMode.LanDriver && !Lan.Ready)) return;
            State = GameState.Playing;
            Waves.Begin(startWave);
        }

        public void StartExpedition()
        {
            if (State != GameState.Menu || Mode == CrewMode.LanGunner || (Mode == CrewMode.LanDriver && !Lan.Ready)) return;
            Expedition = true;
            StartRun(3);
        }

        public void StartDemo()
        {
            if (State != GameState.Menu || Mode != CrewMode.Solo) return;
            demoRun = true;
            SetAutopilot(true);
            StartExpedition();
        }

        public void CompleteExpedition()
        {
            State = GameState.Victory;
            AudioManager.Play("victory", 0.9f, 0f);
        }

        public void OfferUpgrades()
        {
            Upgrades.Roll();
            upgradeOfferedAt = Time.unscaledTime;
            State = GameState.Upgrade;
            Time.timeScale = 0f;
            AudioManager.Play("upgrade", 0.8f, 0f);
        }

        public void ChooseUpgrade(int index)
        {
            if (State != GameState.Upgrade || index < 0 || index >= Upgrades.Offer.Count) return;
            string name = Upgrades.Offer[index].Name;
            Upgrades.Choose(index, Rover);
            State = GameState.Playing;
            Time.timeScale = 1f;
            HUD.Banner(name.ToUpper(), "Upgrade installed", new Color(0.5f, 1f, 0.6f));
            Waves.ResumeAfterUpgrade();
        }

        public void OnRoverDestroyed()
        {
            Events.OnWaveEnd();
            Projectile.ClearAll();
            State = GameState.GameOver;
            Time.timeScale = 1f;
        }

        public void Restart(string titleStatus = null)
        {
            nextTitleStatus = titleStatus;
            Lan.Dispose();
            Time.timeScale = 1f;
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
            }
            else
            {
                // Scene not in build settings (e.g. playing an untitled scene in the editor): rebuild in place.
                foreach (var root in scene.GetRootGameObjects()) Destroy(root);
                new GameObject("Rovernauts Game").AddComponent<GameManager>();
            }
        }

        public void SetAutopilot(bool on) => Rover.Autopilot = on;

        internal void ApplyLanState(LanSnapshot snapshot)
        {
            State = snapshot.state;
            Expedition = snapshot.expedition;
            GodMode = snapshot.godMode;
            RunTime = snapshot.time;
            Kills = snapshot.kills;
            Score = snapshot.score;
            Rover.ApplyLanState(snapshot);
            Waves.ApplyLanState(snapshot);
            Events.ApplyLanEvent(snapshot.moon);
            Upgrades.ApplyLanState(snapshot.upgrades, snapshot.offers);
        }

        public void SetAIDebug(bool on)
        {
            AIDebug = on;
            GridPathfinder.Instance?.SetDebugVisible(on);
        }

        public void SetGodMode(bool on)
        {
            GodMode = on;
            Rover.Health.Invulnerable = on;
        }

        public void SetPaused(bool paused)
        {
            if (paused && State == GameState.Playing)
            {
                stateBeforePause = State;
                State = GameState.Paused;
                Time.timeScale = 0f;
            }
            else if (!paused && State == GameState.Paused)
            {
                State = stateBeforePause;
                Time.timeScale = 1f;
            }
        }

        void HandleUpgradeInput(Keyboard kb)
        {
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) ChooseUpgrade(0);
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) ChooseUpgrade(1);
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) ChooseUpgrade(2);
        }

        void OnEnemyKilled(EnemyBase enemy)
        {
            Kills++;
            Score += enemy.Score;
            if (enemy.IsBoss)
                HUD.Banner($"{enemy.DisplayName.ToUpper()} DEFEATED", "Clear the remaining aliens", new Color(1f, 0.85f, 0.3f));
        }
    }
}
