namespace CommandDeck.Models;

/// <summary>
/// Serializable snapshot of the canvas camera (pan offset + zoom level).
/// </summary>
public class CameraStateModel
{
    public double OffsetX { get; set; } = 0;
    public double OffsetY { get; set; } = 0;

    /// <summary>Zoom multiplier, clamped to [0.25, 2.0].</summary>
    public double Zoom { get; set; } = 1.0;
}
