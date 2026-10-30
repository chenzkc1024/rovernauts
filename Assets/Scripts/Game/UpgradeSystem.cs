using System;
using System.Collections.Generic;

namespace Rovernauts
{
    public class Upgrade
    {
        public readonly string Name, Description;
        public readonly Action<Rover> Apply;

        public Upgrade(string name, string description, Action<Rover> apply)
        {
            Name = name;
            Description = description;
            Apply = apply;
        }
    }

    /// <summary>Weapon/stat upgrades offered (three at a time) after boss waves and moon events.</summary>
    public class UpgradeSystem
    {
        public readonly List<Upgrade> Offer = new List<Upgrade>();
        public readonly List<string> Taken = new List<string>();

        readonly List<Upgrade> pool = new List<Upgrade>
        {
            new Upgrade("Plasma Rounds", "+25% turret damage", r => r.Stats.damage *= 1.25f),
            new Upgrade("Rapid Cyclers", "+20% fire rate", r => r.Stats.fireInterval /= 1.2f),
            new Upgrade("Hull Plating", "+30 max hull and repair 30", r => r.Health.AddMax(30f)),
            new Upgrade("Overdrive Motors", "+12% drive speed", r => r.Stats.moveSpeed *= 1.12f),
            new Upgrade("Split Barrel", "+1 projectile per shot", r => r.Stats.multishot++),
            new Upgrade("Railgun Coils", "Shots pierce +1 enemy, +15% shot speed", r =>
            {
                r.Stats.pierce++;
                r.Stats.projectileSpeed *= 1.15f;
            }),
            new Upgrade("Field Repairs", "Restore 60% hull", r => r.Health.Heal(r.Health.MaxHp * 0.6f)),
        };

        public void Roll(int count = 3)
        {
            Offer.Clear();
            var candidates = new List<Upgrade>(pool);
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int pick = UnityEngine.Random.Range(0, candidates.Count);
                Offer.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
        }

        public void Choose(int index, Rover rover)
        {
            if (index < 0 || index >= Offer.Count) return;
            Offer[index].Apply(rover);
            Taken.Add(Offer[index].Name);
            Offer.Clear();
        }
    }
}
