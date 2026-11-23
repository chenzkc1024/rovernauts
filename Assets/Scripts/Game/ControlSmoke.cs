using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Rovernauts
{
    /// <summary>Runs only with -checkControls. Injects full device states through Unity's Input System.</summary>
    public static class ControlSmoke
    {
        public static bool AwaitingRestart { get; private set; }

        public static IEnumerator Finish(string screenshots)
        {
            var gm = GameManager.I;
            if (!Check(gm.State == GameState.Menu, "R returns from defeat to the title")) yield break;

            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F2));
            yield return new WaitForSecondsRealtime(0.25f);
            if (!Check(gm.State == GameState.Playing && gm.Expedition && gm.Waves.Wave == 3 && gm.Rover.Autopilot,
                "F2 launches the expedition demo from the title")) yield break;
            Capture(screenshots, "05-demo");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.12f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F2));
            yield return new WaitForSecondsRealtime(0.15f);
            if (!Check(gm.State == GameState.Playing && !gm.Rover.Autopilot,
                "F2 hands driving and gunnery back to the player")) yield break;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.12f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F2));
            yield return new WaitForSecondsRealtime(0.15f);
            gm.OfferUpgrades();
            yield return new WaitForSecondsRealtime(4.25f);
            if (!Check(gm.State == GameState.Playing && gm.Upgrades.Taken.Count == 1,
                "the demo installs an upgrade and keeps playing")) yield break;

            Debug.Log("[Controls] PASS");
            Application.Quit();
        }

        public static IEnumerator Run(string screenshots)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            var gm = GameManager.I;

            yield return new WaitForSecondsRealtime(0.6f);
            Capture(screenshots, "01-title");
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South).WithButton(GamepadButton.Start));
            yield return new WaitForSecondsRealtime(0.15f);
            if (!Check(gm.State == GameState.Menu && gm.Rover.GetComponentsInChildren<Turret>().Length == 1,
                "gamepad A and Start do not add a turret or launch the game")) yield break;

            bool wasMuted = gm.Music.Muted;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.M));
            yield return new WaitForSecondsRealtime(0.15f);
            if (!Check(gm.Music.Muted != wasMuted, "M toggles music from the title")) yield break;
            gm.Music.Muted = wasMuted;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
            yield return new WaitForSecondsRealtime(0.25f);
            if (!Check(gm.State == GameState.Playing && !gm.Expedition && gm.Waves.Wave == 1,
                "Enter launches Endless Survival")) yield break;

            var rover = gm.Rover;
            Vector3 before = rover.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSecondsRealtime(0.42f);
            if (!Check(Vector3.Dot(rover.transform.position - before, rover.transform.forward) > 1f,
                "W drives forward")) yield break;
            float angle = rover.transform.eulerAngles.y;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.LeftShift));
            yield return new WaitForSecondsRealtime(0.28f);
            if (!Check(Mathf.Abs(Mathf.DeltaAngle(angle, rover.transform.eulerAngles.y)) > 18f && rover.BoostReady < 0.5f,
                "D turns and Shift boosts")) yield break;
            angle = rover.transform.eulerAngles.y;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow, Key.RightArrow, Key.Space));
            yield return new WaitForSecondsRealtime(0.35f);
            if (!Check(rover.MainTurret.BurstsFired > 0
                && Vector3.Dot(rover.MainTurret.AimDirection, new Vector3(1f, 0f, 1f).normalized) > 0.99f
                && Mathf.Abs(Mathf.DeltaAngle(angle, rover.transform.eulerAngles.y)) < 1f,
                "arrow keys aim diagonally without steering and Space fires"))
                yield break;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return new WaitForSecondsRealtime(0.15f);
            if (!Check(gm.State == GameState.Paused && Time.timeScale == 0f, "Escape pauses the simulation"))
                yield break;
            Capture(screenshots, "02-pause");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.12f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return new WaitForSecondsRealtime(0.16f);
            if (!Check(gm.State == GameState.Playing && Time.timeScale == 1f, "Escape resumes the simulation"))
                yield break;

            gm.OfferUpgrades();
            yield return new WaitForSecondsRealtime(0.2f);
            Capture(screenshots, "03-upgrades");
            string chosen = gm.Upgrades.Offer[1].Name;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit2));
            yield return new WaitForSecondsRealtime(0.18f);
            if (!Check(gm.State == GameState.Playing && gm.Upgrades.Taken.Count == 1 && gm.Upgrades.Taken[0] == chosen,
                "2 selects and installs the second upgrade"))
                yield break;

            gm.Rover.Health.TakeDamage(gm.Rover.Health.Current);
            if (!Check(gm.State == GameState.GameOver, "destroyed hull ends the run"))
                yield break;
            Capture(screenshots, "04-defeat");
            AwaitingRestart = true;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
            yield return new WaitForSecondsRealtime(1f);
            Check(false, "R returns from defeat to the title");
        }

        static bool Check(bool passed, string description)
        {
            if (passed) Debug.Log("[Controls] OK: " + description);
            else
            {
                Debug.LogError("[Controls] FAILED: " + description);
                Application.Quit(1);
            }
            return passed;
        }

        static void Capture(string dir, string name)
        {
            if (dir != null) ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        }
    }
}
