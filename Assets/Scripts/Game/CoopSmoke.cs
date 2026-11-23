using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Rovernauts
{
    /// <summary>Exercises the actual keyboard bindings and runs LAN through two player processes.</summary>
    internal static class CoopSmoke
    {
        internal static string ExpectedReturn { get; private set; }

        internal static void Finish()
        {
            if (ExpectedReturn == null) return;
            if (!Check(GameManager.I.State == GameState.Menu && GameManager.I.Mode == CrewMode.Solo,
                "LAN session closed and returned to the solo title")) return;
            Debug.Log("[Co-op] LAN " + ExpectedReturn + " PASS");
            Application.Quit();
        }

        static bool Check(bool result, string description)
        {
            if (result) Debug.Log("[Co-op] OK: " + description);
            else
            {
                Debug.LogError("[Co-op] FAILED: " + description);
                Application.Quit(1);
            }
            return result;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds = 6f)
        {
            float timeout = Time.unscaledTime + seconds;
            while (!condition() && Time.unscaledTime < timeout) yield return null;
        }

        static void Press(Keyboard keyboard, params Key[] keys) =>
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

        internal static IEnumerator Local()
        {
            var gm = GameManager.I;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Press(keyboard, Key.C);
            yield return null;
            if (!Check(gm.Mode == CrewMode.Local, "C selects local co-op")) yield break;
            Press(keyboard, Key.Enter);
            yield return new WaitForSecondsRealtime(0.2f);
            if (!Check(gm.State == GameState.Playing, "local crew can start a mission")) yield break;
            Press(keyboard, Key.Space);
            yield return new WaitForSecondsRealtime(0.2f);
            if (!Check(gm.Rover.MainTurret.BurstsFired == 0, "local gunnery uses the right-hand fire key")) yield break;
            Press(keyboard, Key.W, Key.LeftShift, Key.UpArrow, Key.RightArrow, Key.RightCtrl);
            yield return new WaitForSecondsRealtime(0.3f);
            if (!Check(gm.Rover.transform.position.z > 1f && gm.Rover.BoostReady < 0.5f && gm.Rover.MainTurret.BurstsFired > 0
                && Vector3.Dot(gm.Rover.MainTurret.AimDirection, new Vector3(1f, 0f, 1f).normalized) > 0.99f,
                "both players can drive, boost, aim and fire at once")) yield break;
            Debug.Log("[Co-op] LOCAL PASS");
            Application.Quit();
        }

        internal static IEnumerator Snapshot()
        {
            var gm = GameManager.I;
            gm.StartExpedition();
            var enemy = EnemyBase.Spawn(EnemyType.MegaBoss, new Vector3(12f, 0f, 12f), new EnemyStats(180f, 4f, 9f));
            enemy.Health.TakeDamage(40f);
            Projectile.Fire(Vector3.forward, Vector3.right, 20f, 10f, Team.Player, Turret.Accent);
            Meteor.Launch(new Vector3(5f, 0f, 5f), 3.5f, 12f);
            gm.OfferUpgrades();

            var captured = new LanSnapshot();
            captured.Capture(gm);
            var received = JsonUtility.FromJson<LanSnapshot>(JsonUtility.ToJson(captured));
            if (!Check(received.version == 1 && received.enemies.Count == 1 && received.shots.Count == 1
                && received.meteors.Count == 1 && received.offers.Length == 3, "LAN snapshot round-trips through JSON")) yield break;

            UnityEngine.Object.Destroy(enemy.gameObject);
            EnemyBase.All.Clear();
            Projectile.ClearAll();
            Meteor.ClearAll();
            yield return null;

            gm.Mode = CrewMode.LanGunner;
            var world = new LanWorld();
            world.Receive(gm, received);
            world.Tick(gm.Rover);
            if (!Check(gm.State == GameState.Upgrade && gm.Waves.Wave == 3 && gm.Waves.ActiveBoss?.DisplayName == "Void Titan"
                && gm.Waves.ActiveBoss.Agent == null && gm.Events.Active == MoonEvent.AsteroidStorm && gm.Upgrades.Offer.Count == 3,
                "guest reconstructs boss visuals, wave, asteroid event and upgrade choices")) yield break;

            received.state = GameState.GameOver;
            received.hull = 0f;
            received.enemies.Clear();
            received.shots.Clear();
            received.meteors.Clear();
            received.offers = Array.Empty<string>();
            world.Receive(gm, received);
            if (!Check(gm.State == GameState.GameOver && gm.Rover.Health.IsDead && EnemyBase.All.Count == 0
                && gm.Waves.ActiveBoss == null && gm.Upgrades.Offer.Count == 0, "guest applies defeat and removes stale replicas")) yield break;
            Debug.Log("[Co-op] SNAPSHOT PASS");
            Application.Quit();
        }

        internal static IEnumerator Host(bool loopback)
        {
            var gm = GameManager.I;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            if (loopback) gm.Lan.Host(gm, true);
            else
            {
                Press(keyboard, Key.L);
                yield return null;
                Press(keyboard, Key.H);
                yield return null;
            }
            if (!Check(gm.Mode == CrewMode.LanDriver, "host is listening")) yield break;
            Debug.Log("[Co-op] Host addresses: " + gm.Lan.HostAddresses);
            yield return WaitUntil(() => gm.Lan.Ready, 20f);
            if (!Check(gm.Lan.Ready, "gunner joined")) yield break;
            Press(keyboard, Key.E);
            yield return new WaitForSecondsRealtime(0.3f);
            if (!Check(gm.State == GameState.Playing && gm.Expedition, "host launched Crater Expedition")) yield break;
            Press(keyboard, Key.W, Key.LeftShift);
            yield return new WaitForSecondsRealtime(0.4f);
            Press(keyboard);
            yield return WaitUntil(() => gm.Rover.MainTurret.BurstsFired > 0);
            if (!Check(gm.Rover.MainTurret.BurstsFired > 0
                && Vector3.Dot(gm.Rover.MainTurret.AimDirection, Vector3.right) > 0.99f, "guest input fires and aims the host turret")) yield break;
            yield return WaitUntil(() => gm.RunTime > 5.8f);
            Press(keyboard, Key.Escape);
            yield return new WaitForSecondsRealtime(0.6f);
            if (!Check(gm.State == GameState.Paused, "driver paused")) yield break;
            Press(keyboard);
            yield return null;
            Press(keyboard, Key.Escape);
            yield return new WaitForSecondsRealtime(0.2f);
            gm.OfferUpgrades();
            yield return new WaitForSecondsRealtime(0.8f);
            Press(keyboard, Key.Digit2);
            yield return new WaitForSecondsRealtime(0.8f);
            if (!Check(gm.Upgrades.Taken.Count == 1, "host installed an upgrade")) yield break;
            ExpectedReturn = "HOST";
            gm.Rover.Health.TakeDamage(gm.Rover.Health.Current);
            yield return new WaitForSecondsRealtime(6f);
            Check(false, "guest departure returned the host to title");
        }

        internal static IEnumerator Guest(string address)
        {
            var gm = GameManager.I;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            if (address == "loopback") gm.Lan.Join(gm, address, true);
            else
            {
                Press(keyboard, Key.L);
                yield return null;
                Press(keyboard, Key.J);
                yield return null;
                foreach (char digit in address)
                {
                    Press(keyboard, digit switch
                    {
                        '.' => Key.Period,
                        '0' => Key.Digit0,
                        _ => (Key)((int)Key.Digit1 + digit - '1'),
                    });
                    yield return null;
                    Press(keyboard);
                    yield return null;
                }
                if (!Check(gm.Lan.Address == address, "guest typed the host IPv4 address")) yield break;
                Press(keyboard, Key.Enter);
            }
            yield return WaitUntil(() => gm.State == GameState.Playing);
            if (!Check(gm.State == GameState.Playing && gm.Waves.Wave == 3 && gm.Events.Active == MoonEvent.AsteroidStorm,
                "guest received the mission, wave and asteroid event")) yield break;
            Press(keyboard, Key.RightArrow, Key.Space);
            yield return WaitUntil(() => gm.Lan.Latest?.shots.Count > 0 && gm.Rover.transform.position.z > 1f);
            if (!Check(gm.Lan.Latest?.shots.Count > 0 && gm.Rover.transform.position.z > 1f,
                "guest sees shared rover movement and shots")) yield break;
            Press(keyboard);
            yield return WaitUntil(() => gm.Lan.Latest?.meteors.Count > 0 && EnemyBase.All.Count > 0);
            if (!Check(gm.Lan.Latest?.meteors.Count > 0 && EnemyBase.All.Count > 0 && EnemyBase.All[0].Agent == null,
                "guest renders host meteors and enemy replicas without running enemy AI")) yield break;
            yield return WaitUntil(() => gm.State == GameState.Paused);
            if (!Check(gm.State == GameState.Paused, "guest sees the shared pause")) yield break;
            yield return WaitUntil(() => gm.State == GameState.Upgrade);
            if (!Check(gm.State == GameState.Upgrade && gm.Upgrades.Offer.Count == 3, "guest sees the host's upgrade options")) yield break;
            yield return WaitUntil(() => gm.State == GameState.Playing && gm.Upgrades.Taken.Count == 1);
            if (!Check(gm.State == GameState.Playing && gm.Upgrades.Taken.Count == 1, "selected upgrade reached the guest")) yield break;
            yield return WaitUntil(() => gm.State == GameState.GameOver);
            if (!Check(gm.State == GameState.GameOver && gm.Rover.Health.IsDead, "guest sees the shared defeat")) yield break;
            ExpectedReturn = "GUEST";
            Press(keyboard, Key.F8);
            yield return new WaitForSecondsRealtime(6f);
            Check(false, "guest can leave with F8");
        }
    }
}
