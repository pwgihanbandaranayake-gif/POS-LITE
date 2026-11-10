namespace PosSystem.Helpers;

public static class Money
{
    public static decimal R(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;

    /// <summary>Rounds a bill total to the nearest step (e.g. 0.50, 1.00). Step 0 = no rounding.</summary>
    public static decimal RoundTo(decimal value, decimal step)
    {
        if (step <= 0) return R(value);
        return R(Math.Round(value / step, 0, MidpointRounding.AwayFromZero) * step);
    }
}

public class AppException : Exception
{
    public AppException(string message) : base(message) { }
}
