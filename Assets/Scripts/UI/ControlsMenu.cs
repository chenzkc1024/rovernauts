using UnityEngine.InputSystem;

namespace Rovernauts
{
    /// <summary>
    /// State and input for the rebinding screen (K on the title or pause screen). HUD draws it.
    /// Menu keys are fixed so a bad binding can never lock the player out:
    /// Up/Down pick a row, Left/Right pick primary or alternate, Enter rebinds (to a key or mouse button), Delete clears,
    /// Tab switches between the Solo and Local co-op sets, Backspace restores that set's defaults, Esc closes.
    /// </summary>
    internal sealed class ControlsMenu
    {
        internal bool Open { get; private set; }
        internal ControlProfile Profile { get; private set; }
        internal int Row { get; private set; }
        internal int Column { get; private set; }
        internal bool Capturing { get; private set; }
        internal string Status { get; private set; } = "";
        internal bool OnAimRow => Row == Controls.ActionCount;
        internal bool OnSteeringRow => Row == Controls.ActionCount + 1;
        bool OnToggleRow => Row >= Controls.ActionCount;
        int RowCount => Controls.ActionCount + 2;

        internal void Show(CrewMode mode)
        {
            Open = true;
            Profile = Controls.ProfileFor(mode);
            Row = 0;
            Column = 0;
            Capturing = false;
            Status = "";
        }

        /// <summary>Returns true while the menu is open, so no other screen reacts to the same key press.</summary>
        internal bool Tick(Keyboard kb)
        {
            if (!Open) return false;
            if (kb == null) return true;
            if (Capturing)
            {
                Capture(kb);
                return true;
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                Open = false;
            }
            else if (kb.upArrowKey.wasPressedThisFrame)
            {
                Row = (Row + RowCount - 1) % RowCount;
            }
            else if (kb.downArrowKey.wasPressedThisFrame)
            {
                Row = (Row + 1) % RowCount;
            }
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
            {
                if (OnToggleRow) Toggle();
                else Column = 1 - Column;
            }
            else if (kb.tabKey.wasPressedThisFrame)
            {
                Profile = Profile == ControlProfile.Solo ? ControlProfile.Coop : ControlProfile.Solo;
                Status = "";
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                if (OnToggleRow) Toggle();
                else
                {
                    Capturing = true;
                    Status = $"Press a key or mouse button for {Controls.ActionLabel(Profile, (Act)Row).Trim()}.  Esc cancels.";
                }
            }
            else if (kb.deleteKey.wasPressedThisFrame && !OnToggleRow)
            {
                Controls.Set(Profile, (Act)Row, Column, Controls.None);
                Status = "Key cleared.";
            }
            else if (kb.backspaceKey.wasPressedThisFrame)
            {
                Controls.ResetToDefaults(Profile);
                Status = (Profile == ControlProfile.Solo ? "Solo" : "Local co-op") + " controls restored to defaults.";
            }
            return true;
        }

        void Toggle()
        {
            if (OnAimRow) Controls.SetMouseAim(Profile, !Controls.MouseAim(Profile));
            else Controls.SetCarSteering(!Controls.CarSteering);
            Status = "";
        }

        void Capture(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Capturing = false;
                Status = "Cancelled.";
                return;
            }
            int input = Controls.PressedThisFrame(kb, Mouse.current);
            if (input == Controls.None) return;
            string name = Controls.InputName(input);
            if (Controls.IsReserved(input))
            {
                Status = $"{name} is reserved. Press another key, or Esc to cancel.";
                return;
            }
            var action = (Act)Row;
            var moved = Controls.Set(Profile, action, Column, input);
            Capturing = false;
            string label = Controls.ActionLabel(Profile, action).Trim();
            Status = moved.HasValue
                ? $"{name} set for {label}. It was removed from {Controls.ActionLabel(Profile, moved.Value).Trim()}."
                : $"{name} set for {label}.";
        }
    }
}
