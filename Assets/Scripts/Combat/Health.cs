using System;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>Hit points for anything that can be damaged. The team decides who can hurt whom.</summary>
    public class Health : MonoBehaviour
    {
        public Team team;
        public bool Invulnerable;
        public float MaxHp { get; private set; } = 100f;
        public float Current { get; private set; } = 100f;
        public bool IsDead => Current <= 0f;
        public float Fraction => MaxHp > 0f ? Current / MaxHp : 0f;

        public event Action<float> Damaged;
        public event Action Died;

        public void Init(Team owner, float maxHp)
        {
            team = owner;
            MaxHp = Current = maxHp;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || Invulnerable || amount <= 0f) return;
            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount);
            if (Current <= 0f) Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (!IsDead) Current = Mathf.Min(MaxHp, Current + amount);
        }

        public void AddMax(float amount)
        {
            MaxHp += amount;
            Heal(amount);
        }
    }
}
