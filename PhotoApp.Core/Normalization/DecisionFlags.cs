namespace PhotoApp.Core.Normalization;

[Flags]
public enum DecisionFlags
{
    None = 0,
    Ambiguous = 1,
    Disagreement = 2,
    RealFormat = 4,
    AssumedZone = 8,
    Derivative = 16,
    LivePhoto = 32
}
