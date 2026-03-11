using System;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Tracks welfare indicators such as condition score, mortality risk modifiers.
    /// </summary>
    public class FishWelfare
    {
        public double ConditionScore { get; private set; } = 1.0; // 0-2 where 1 is neutral

        public event Action<double>? OnConditionChanged;

        public void ModifyCondition(double delta)
        {
            ConditionScore = Math.Clamp(ConditionScore + delta, 0.0, 2.0);
            OnConditionChanged?.Invoke(ConditionScore);
        }
    }
}