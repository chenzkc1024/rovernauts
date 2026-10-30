using System;

namespace Rovernauts
{
    /// <summary>One state of a finite state machine. Behaviour is supplied as delegates so each
    /// enemy's states and transitions read top-to-bottom in a single method.</summary>
    public sealed class State
    {
        public readonly string Name;
        public readonly Action Enter;
        public readonly Action<float> Tick;
        public readonly Action Exit;

        public State(string name, Action enter = null, Action<float> tick = null, Action exit = null)
        {
            Name = name;
            Enter = enter;
            Tick = tick;
            Exit = exit;
        }
    }

    /// <summary>Minimal finite state machine: exactly one active state, explicit transitions.</summary>
    public sealed class StateMachine
    {
        public State Current { get; private set; }
        public float TimeInState { get; private set; }

        /// <summary>Raised after every transition with (from, to). Used by the AI debug log.</summary>
        public event Action<State, State> Changed;

        public void Change(State next)
        {
            if (next == null || next == Current) return;
            var previous = Current;
            previous?.Exit?.Invoke();
            Current = next;
            TimeInState = 0f;
            next.Enter?.Invoke();
            Changed?.Invoke(previous, next);
        }

        public void Tick(float dt)
        {
            TimeInState += dt;
            Current?.Tick?.Invoke(dt);
        }
    }
}
