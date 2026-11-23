using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Rovernauts
{
    /// <summary>Gameplay actions that can be rebound.</summary>
    public enum Act { Forward, Reverse, TurnLeft, TurnRight, Boost, AimUp, AimDown, AimLeft, AimRight, Fire }

    /// <summary>
    /// Two binding sets: Solo (also used by both LAN players) and Local co-op (two players sharing one PC).
    /// </summary>
    public enum ControlProfile { Solo, Coop }

    /// <summary>
    /// Rebindable controls. Each action has a primary and an alternate input per profile.
    /// An input is a keyboard <see cref="Key"/> or a mouse button (codes from <see cref="MouseLeft"/> up).
    /// The turret aims at the mouse cursor by default; aim keys override it while held.
    /// Bindings are saved in PlayerPrefs. Smoke tests (any -check argument) always use the defaults and never save.
    /// </summary>
    public static class Controls
    {
        public const int Slots = 2;
        public const int None = 0, MouseLeft = 1000, MouseRight = 1001, MouseMiddle = 1002, MouseForward = 1003, MouseBack = 1004;
        public static readonly int ActionCount = Enum.GetValues(typeof(Act)).Length;
        const string Prefix = "rovernauts.controls.v1.";

        static readonly int[,,] bindings = new int[2, Enum.GetValues(typeof(Act)).Length, Slots];
        static readonly bool[] mouseAim = new bool[2];
        static readonly bool persist;

        /// <summary>When true, turning keys swap while reversing so the rover steers like a car.</summary>
        public static bool CarSteering { get; private set; }

        static Controls()
        {
            persist = !Array.Exists(Environment.GetCommandLineArgs(), a => a.StartsWith("-check", StringComparison.Ordinal));
            ResetToDefaults(ControlProfile.Solo, false);
            ResetToDefaults(ControlProfile.Coop, false);
            if (persist) Load();
        }

        public static ControlProfile ProfileFor(CrewMode mode) => mode == CrewMode.Local ? ControlProfile.Coop : ControlProfile.Solo;

        public static int Get(ControlProfile profile, Act action, int slot) => bindings[(int)profile, (int)action, slot];

        /// <summary>True when the turret follows the mouse cursor for this profile.</summary>
        public static bool MouseAim(ControlProfile profile) => mouseAim[(int)profile];

        /// <summary>
        /// Binds an input. If another action in the same profile already uses it, that slot is cleared
        /// and its action is returned so the menu can say what moved.
        /// </summary>
        public static Act? Set(ControlProfile profile, Act action, int slot, int input)
        {
            Act? displaced = null;
            if (input != None)
            {
                for (int a = 0; a < ActionCount; a++)
                    for (int s = 0; s < Slots; s++)
                    {
                        if ((a == (int)action && s == slot) || bindings[(int)profile, a, s] != input) continue;
                        bindings[(int)profile, a, s] = None;
                        if (a != (int)action) displaced = (Act)a;
                    }
            }
            bindings[(int)profile, (int)action, slot] = input;
            Save();
            return displaced;
        }

        public static void SetMouseAim(ControlProfile profile, bool on)
        {
            mouseAim[(int)profile] = on;
            Save();
        }

        public static void SetCarSteering(bool on)
        {
            CarSteering = on;
            Save();
        }

        public static void ResetToDefaults(ControlProfile profile, bool save = true)
        {
            int p = (int)profile;
            void Bind(Act action, int primary, int alternate = None)
            {
                bindings[p, (int)action, 0] = primary;
                bindings[p, (int)action, 1] = alternate;
            }

            Bind(Act.Forward, (int)Key.W);
            Bind(Act.Reverse, (int)Key.S);
            Bind(Act.TurnLeft, (int)Key.A);
            Bind(Act.TurnRight, (int)Key.D);
            Bind(Act.AimUp, (int)Key.UpArrow);
            Bind(Act.AimDown, (int)Key.DownArrow);
            Bind(Act.AimLeft, (int)Key.LeftArrow);
            Bind(Act.AimRight, (int)Key.RightArrow);
            if (profile == ControlProfile.Solo)
            {
                Bind(Act.Boost, (int)Key.LeftShift, (int)Key.RightShift);
                Bind(Act.Fire, MouseLeft, (int)Key.Space);
            }
            else
            {
                // Player 2 takes the mouse (or the arrow keys and Right Ctrl), Player 1 keeps the left of the keyboard.
                Bind(Act.Boost, (int)Key.LeftShift);
                Bind(Act.Fire, MouseLeft, (int)Key.RightCtrl);
            }
            mouseAim[p] = true;
            if (save) Save();
        }

        /// <summary>Inputs the rebinding menu refuses: Escape opens menus, function keys run demo and debug commands.</summary>
        public static bool IsReserved(int input) => input == None || input == (int)Key.Escape
            || (input >= (int)Key.F1 && input <= (int)Key.F12);

        static bool IsMouse(int input) => input >= MouseLeft && input <= MouseBack;

        static ButtonControl Control(int input, Keyboard kb, Mouse mouse)
        {
            if (IsMouse(input))
            {
                if (mouse == null) return null;
                return input switch
                {
                    MouseLeft => mouse.leftButton,
                    MouseRight => mouse.rightButton,
                    MouseMiddle => mouse.middleButton,
                    MouseForward => mouse.forwardButton,
                    _ => mouse.backButton,
                };
            }
            if (kb == null || input <= None) return null;
            try { return kb[(Key)input]; }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        public static bool Held(Keyboard kb, ControlProfile profile, Act action)
        {
            var mouse = Mouse.current;
            for (int s = 0; s < Slots; s++)
            {
                var input = bindings[(int)profile, (int)action, s];
                if (input != None && Control(input, kb, mouse) is { isPressed: true }) return true;
            }
            return false;
        }

        /// <summary>The first keyboard key or mouse button pressed this frame, or <see cref="None"/>.</summary>
        public static int PressedThisFrame(Keyboard kb, Mouse mouse)
        {
            if (kb != null)
                foreach (var key in kb.allKeys)
                    if (key != null && key.wasPressedThisFrame) return (int)key.keyCode;
            if (mouse != null)
                for (int code = MouseLeft; code <= MouseBack; code++)
                    if (Control(code, null, mouse) is { wasPressedThisFrame: true }) return code;
            return None;
        }

        public static string InputName(int input)
        {
            switch (input)
            {
                case None: return "-";
                case MouseLeft: return "Left Click";
                case MouseRight: return "Right Click";
                case MouseMiddle: return "Middle Click";
                case MouseForward: return "Mouse Forward";
                case MouseBack: return "Mouse Back";
            }
            var key = (Key)input;
            switch (key)
            {
                case Key.LeftShift: return "Left Shift";
                case Key.RightShift: return "Right Shift";
                case Key.LeftCtrl: return "Left Ctrl";
                case Key.RightCtrl: return "Right Ctrl";
                case Key.LeftAlt: return "Left Alt";
                case Key.RightAlt: return "Right Alt";
                case Key.UpArrow: return "Up";
                case Key.DownArrow: return "Down";
                case Key.LeftArrow: return "Left";
                case Key.RightArrow: return "Right";
            }
            var control = Keyboard.current != null ? Control(input, Keyboard.current, null) : null;
            string name = control != null && !string.IsNullOrEmpty(control.displayName) ? control.displayName : key.ToString();
            return name.Length == 1 ? name.ToUpperInvariant() : name;
        }

        /// <summary>"Left Click / Space", or "unbound".</summary>
        public static string Describe(ControlProfile profile, Act action)
        {
            int a = Get(profile, action, 0), b = Get(profile, action, 1);
            if (a == None && b == None) return "unbound";
            if (a == None || b == None) return InputName(a == None ? b : a);
            return InputName(a) + " / " + InputName(b);
        }

        /// <summary>Four directions as one compact label, e.g. "WASD" or "Arrow keys".</summary>
        public static string DescribeDirections(ControlProfile profile, Act up, Act left, Act down, Act right)
        {
            int u = Get(profile, up, 0), l = Get(profile, left, 0), d = Get(profile, down, 0), r = Get(profile, right, 0);
            if (u == (int)Key.UpArrow && l == (int)Key.LeftArrow && d == (int)Key.DownArrow && r == (int)Key.RightArrow) return "Arrow keys";
            string joined = InputName(u) + InputName(l) + InputName(d) + InputName(r);
            return joined.Length == 4 ? joined : $"{InputName(u)} {InputName(l)} {InputName(d)} {InputName(r)}";
        }

        /// <summary>How the gunner aims, e.g. "Mouse" or "Arrow keys".</summary>
        public static string DescribeAim(ControlProfile profile) => MouseAim(profile)
            ? "Mouse"
            : DescribeDirections(profile, Act.AimUp, Act.AimLeft, Act.AimDown, Act.AimRight);

        public static string ActionLabel(ControlProfile profile, Act action)
        {
            string label = action switch
            {
                Act.Forward => "Drive forward",
                Act.Reverse => "Reverse",
                Act.TurnLeft => "Turn left",
                Act.TurnRight => "Turn right",
                Act.Boost => "Boost",
                Act.AimUp => "Aim up",
                Act.AimDown => "Aim down",
                Act.AimLeft => "Aim left",
                Act.AimRight => "Aim right",
                _ => "Fire",
            };
            if (profile == ControlProfile.Solo) return label;
            return (action <= Act.Boost ? "P1  " : "P2  ") + label;
        }

        static bool IsValid(int input) => IsMouse(input) || (Enum.IsDefined(typeof(Key), input) && !IsReserved(input));

        static void Load()
        {
            for (int p = 0; p < 2; p++)
            {
                for (int a = 0; a < ActionCount; a++)
                    for (int s = 0; s < Slots; s++)
                    {
                        string id = $"{Prefix}{(ControlProfile)p}.{(Act)a}.{s}";
                        if (!PlayerPrefs.HasKey(id)) continue;
                        int value = PlayerPrefs.GetInt(id);
                        bindings[p, a, s] = IsValid(value) ? value : None;
                    }
                string aimId = $"{Prefix}{(ControlProfile)p}.mouseAim";
                if (PlayerPrefs.HasKey(aimId)) mouseAim[p] = PlayerPrefs.GetInt(aimId) == 1;
            }
            CarSteering = PlayerPrefs.GetInt(Prefix + "carSteering", 0) == 1;
        }

        static void Save()
        {
            if (!persist) return;
            for (int p = 0; p < 2; p++)
            {
                for (int a = 0; a < ActionCount; a++)
                    for (int s = 0; s < Slots; s++)
                        PlayerPrefs.SetInt($"{Prefix}{(ControlProfile)p}.{(Act)a}.{s}", bindings[p, a, s]);
                PlayerPrefs.SetInt($"{Prefix}{(ControlProfile)p}.mouseAim", mouseAim[p] ? 1 : 0);
            }
            PlayerPrefs.SetInt(Prefix + "carSteering", CarSteering ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
