using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Command-line driven smoke test / demo recorder for builds. Does nothing unless launched with arguments:
    ///   -autoplay            start immediately with the autopilot driving and gunning, auto-pick upgrades
    ///   -expedition          choose Crater Expedition when using -autoplay
    ///   -checkControls       exercise the keyboard controls
    ///   -startWave N         begin at wave N (e.g. 5 for the first boss)
    ///   -godmode             rover can't die (to reach later waves)
    ///   -aidebug             show FSM labels, A* paths and the blocked grid
    ///   -shots DIR           save a screenshot to DIR every 6 seconds
    ///   -quitAfter SECONDS   log the FSM transition counts and quit
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        static bool coopCheckStarted;
        bool autoplay, expedition, checkControls;
        int startWave = 1, shotIndex;
        float quitAfter = -1f, nextLog, nextShot = 3f, upgradeAt = -1f;
        string shotDir;

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-checkCoopLocal":
                    case "-checkLanSnapshot":
                    case "-checkLanHost":
                    case "-checkLanGuest":
                        if (coopCheckStarted)
                        {
                            CoopSmoke.Finish();
                            return;
                        }
                        coopCheckStarted = true;
                        StartCoroutine(args[i] switch
                        {
                            "-checkCoopLocal" => CoopSmoke.Local(),
                            "-checkLanSnapshot" => CoopSmoke.Snapshot(),
                            "-checkLanHost" => CoopSmoke.Host(next == "loopback"),
                            _ => CoopSmoke.Guest(next ?? ""),
                        });
                        return;
                    case "-autoplay": autoplay = true; break;
                    case "-expedition": expedition = true; break;
                    case "-checkControls": checkControls = true; break;
                    case "-startWave": int.TryParse(next, out startWave); break;
                    case "-quitAfter": float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out quitAfter); break;
                    case "-shots": shotDir = next; break;
                    case "-godmode": GameManager.I.SetGodMode(true); break;
                    case "-aidebug": GameManager.I.SetAIDebug(true); break;
                }
            }
            if (!string.IsNullOrEmpty(shotDir))
            {
                shotDir = Path.GetFullPath(shotDir);
                Directory.CreateDirectory(shotDir);
            }
            if (checkControls)
            {
                StartCoroutine(ControlSmoke.AwaitingRestart ? ControlSmoke.Finish(shotDir) : ControlSmoke.Run(shotDir));
                return;
            }
            if (!autoplay) return;
            Debug.Log($"[AutoTest] autoplay from wave {(expedition ? 3 : startWave)}, quitAfter={quitAfter}, grid blocked cells={GridPathfinder.Instance.BlockedCount}");
            GameManager.I.SetAutopilot(true);
            if (expedition) GameManager.I.StartExpedition();
            else GameManager.I.StartRun(Mathf.Max(1, startWave));
        }

        void Update()
        {
            if (!autoplay) return;
            var gm = GameManager.I;
            float now = Time.realtimeSinceStartup;
            if (!gm.Rover.Autopilot) gm.SetAutopilot(true); // stray keypresses on the test window shouldn't end the test

            if (gm.State == GameState.Upgrade)
            {
                if (upgradeAt < 0f) upgradeAt = now + 1f;
                else if (now >= upgradeAt)
                {
                    upgradeAt = -1f;
                    Debug.Log($"[AutoTest] picking upgrade: {gm.Upgrades.Offer[0].Name}");
                    gm.ChooseUpgrade(0);
                }
            }

            if (now >= nextLog)
            {
                nextLog = now + 5f;
                var waves = gm.Waves;
                var states = string.Join(", ", EnemyBase.All.GroupBy(e => $"{e.DisplayName}/{e.StateName}").Select(g => $"{g.Key}x{g.Count()}"));
                Debug.Log($"[AutoTest] t={gm.RunTime:0.0}s state={gm.State} wave={waves.Wave} phase={waves.PhaseName} queued={waves.QueuedCount} " +
                          $"alive={EnemyBase.All.Count} hull={gm.Rover.Health.Current:0}/{gm.Rover.Health.MaxHp:0} kills={gm.Kills} " +
                          $"event={gm.Events.Active} fps={1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):0} | {states}");
            }

            if (shotDir != null && now >= nextShot)
            {
                nextShot = now + 6f;
                ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, $"shot_{shotIndex++:00}.png"));
            }

            if (quitAfter > 0f && now >= quitAfter)
            {
                quitAfter = -1f;
                Debug.Log("[AutoTest] FSM transitions:\n" + string.Join("\n",
                    EnemyBase.TransitionLog.OrderBy(kv => kv.Key).Select(kv => $"  {kv.Key}: {kv.Value}")));
                Debug.Log($"[AutoTest] DONE wave={gm.Waves.Wave} kills={gm.Kills} score={gm.Score} state={gm.State} upgrades=[{string.Join(", ", gm.Upgrades.Taken)}]");
                Application.Quit();
            }
        }
    }
}
