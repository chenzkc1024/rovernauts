using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Rovernauts
{
    public enum GameState { Menu, Playing, Upgrade, Paused, GameOver }

    /// <summary>
    /// Entry point. Builds the whole game at runtime (arena, pathfinding grid, rover, camera, directors, HUD)
    /// and owns the top-level game state: Menu -> Playing <-> Upgrade/Paused -> GameOver.
    /// Local co-op crew: P1 drives (keyboard), P2 guns (mouse), P3-P4 join as side gunners with gamepads.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public static readonly Color[] PlayerColors =
        {
            new Color(0.2f, 0.9f, 1f), new Color(1f, 0.85f, 0.2f), new Color(1f, 0.35f, 0.8f), new Color(0.5f, 1f, 0.35f),
        };
        static readonly Vector3[] GunnerMounts =
        {
            new Vector3(0f, 1.15f, 0.45f), new Vector3(-0.85f, 1.5f, -0.9f), new Vector3(0.85f, 1.5f, -0.9f),
        };

        public GameState State { get; private set; } = GameState.Menu;
        public float RunTime { get; private set; }
        public int Kills { get; private set; }
        public int Score { get; private set; }
        public bool AIDebug { get; private set; }
        public bool GodMode { get; private set; }
        public Rover Rover { get; private set; }
        public WaveDirector Waves { get; private set; }
        public EventDirector Events { get; private set; }
        public UpgradeSystem Upgrades { get; private set; }

        GameState stateBeforePause;

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
            EnemyBase.All.Clear();

            gameObject.AddComponent<AudioManager>();
            WorldBuilder.Build(486);
            GridPathfinder.Instance = new GridPathfinder(WorldBuilder.HalfSize, 2f, 0.45f);
            Rover = Rover.Create(Vector3.zero);
            Turret.Create(Rover, GunnerMounts[0], 2, PlayerColors[1], GunnerInput.Mouse);
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
            if (I != this) return;
            EnemyBase.AnyKilled -= OnEnemyKilled;
            Time.timeScale = 1f;
            I = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            bool confirm = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            foreach (var pad in Gamepad.all) confirm |= pad.startButton.wasPressedThisFrame;

            switch (State)
            {
                case GameState.Menu:
                    if (confirm || (kb != null && kb.spaceKey.wasPressedThisFrame)) StartRun();
                    break;
                case GameState.Playing:
                    RunTime += Time.deltaTime;
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) SetPaused(true);
                    break;
                case GameState.Paused:
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) SetPaused(false);
                    if (kb != null && kb.rKey.wasPressedThisFrame) Restart();
                    break;
                case GameState.Upgrade:
                    HandleUpgradeInput(kb);
                    break;
                case GameState.GameOver:
                    if (confirm || (kb != null && kb.rKey.wasPressedThisFrame)) Restart();
                    break;
            }

            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) SetAIDebug(!AIDebug);
                if (kb.f2Key.wasPressedThisFrame) SetAutopilot(!Rover.Autopilot);
                if (kb.f3Key.wasPressedThisFrame && State == GameState.Playing) Waves.SkipToNextBoss();
                if (kb.f4Key.wasPressedThisFrame) SetGodMode(!GodMode);
            }
            JoinGamepads();
        }

        public void StartRun(int startWave = 1)
        {
            if (State != GameState.Menu) return;
            State = GameState.Playing;
            Waves.Begin(startWave);
        }

        public void OfferUpgrades()
        {
            Upgrades.Roll();
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
            State = GameState.GameOver;
            Time.timeScale = 1f;
        }

        public void Restart()
        {
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

        public void SetAutopilot(bool on)
        {
            Rover.Autopilot = on;
            foreach (var t in Rover.Turrets)
                if (t.Pad == null) t.Source = on ? GunnerInput.Auto : GunnerInput.Mouse;
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

        void SetPaused(bool paused)
        {
            if (paused)
            {
                stateBeforePause = State;
                State = GameState.Paused;
                Time.timeScale = 0f;
            }
            else
            {
                State = stateBeforePause;
                Time.timeScale = 1f;
            }
        }

        void HandleUpgradeInput(Keyboard kb)
        {
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) ChooseUpgrade(0);
                else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) ChooseUpgrade(1);
                else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) ChooseUpgrade(2);
            }
            foreach (var pad in Gamepad.all)
            {
                if (pad.buttonWest.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame) ChooseUpgrade(0);
                else if (pad.buttonNorth.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame) ChooseUpgrade(1);
                else if (pad.buttonEast.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame) ChooseUpgrade(2);
            }
        }

        /// <summary>Any unclaimed gamepad pressing A / Start takes the next free gun seat (up to 4 crew total).</summary>
        void JoinGamepads()
        {
            if (Rover == null || Rover.Health.IsDead) return;
            foreach (var pad in Gamepad.all)
            {
                if (Rover.Turrets.Count >= GunnerMounts.Length) return;
                if (Rover.Turrets.Exists(t => t.Pad == pad)) continue;
                if (!pad.buttonSouth.wasPressedThisFrame && !pad.startButton.wasPressedThisFrame) continue;
                int seat = Rover.Turrets.Count;
                int player = seat + 2;
                Turret.Create(Rover, GunnerMounts[seat], player, PlayerColors[player - 1], GunnerInput.Gamepad, pad);
                HUD.Banner($"PLAYER {player} JOINED", "Gamepad gunner: right stick aims, RT fires", PlayerColors[player - 1]);
                AudioManager.Play("upgrade", 0.6f, 0f);
            }
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
