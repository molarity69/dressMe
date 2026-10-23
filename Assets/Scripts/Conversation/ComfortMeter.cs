using UnityEngine;

/// <summary>
/// Hidden 0–100 comfort value for a conversation character. Plain C# so it is easy to reason about and test.
/// </summary>
public class ComfortMeter
{
    public const int Minimum = 0;
    public const int Maximum = 100;

    public int Value { get; private set; }

    public ComfortMeter(int startValue)
    {
        Value = Mathf.Clamp(startValue, Minimum, Maximum);
    }

    /// <summary>Applies a change and returns how much the value actually moved after clamping.</summary>
    public int Change(int delta)
    {
        int previousValue = Value;
        Value = Mathf.Clamp(Value + delta, Minimum, Maximum);
        return Value - previousValue;
    }
}
