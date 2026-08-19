using System.Text.Json.Serialization;

namespace Clipora.Core.Models;

public sealed record WindowLayout(int WidthDip, int HeightDip, bool IsMaximized)
{
    public const int MinimumWidthDip = 1000;

    public const int MinimumHeightDip = 680;

    public const int MaximumSideDip = 20000;

    [JsonIgnore]
    public bool IsValid =>
        WidthDip >= MinimumWidthDip &&
        HeightDip >= MinimumHeightDip &&
        WidthDip <= MaximumSideDip &&
        HeightDip <= MaximumSideDip;
}
