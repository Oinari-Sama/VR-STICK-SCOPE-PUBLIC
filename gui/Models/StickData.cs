using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRStickScope.Models;

public class StickAxes
{
    public float Rx { get; set; }
    public float Ry { get; set; }
    public float Cx { get; set; }
    public float Cy { get; set; }
    public bool Touched { get; set; }

    // 生の角度 (度)
    public float RawAngleDeg => MathF.Atan2(Ry, Rx) * (180f / MathF.PI);
    // 生の強度
    public float RawMagnitude => MathF.Sqrt(Rx * Rx + Ry * Ry);
    // 補正後角度
    public float CorrectedAngleDeg => MathF.Atan2(Cy, Cx) * (180f / MathF.PI);
    // 補正後強度
    public float CorrectedMagnitude => MathF.Sqrt(Cx * Cx + Cy * Cy);
    // 角度誤差
    public float AngleError => CorrectedAngleDeg - RawAngleDeg;
    // 半径誤差
    public float RadiusError => CorrectedMagnitude - RawMagnitude;
}

public class EngineStateMessage
{
    [JsonPropertyName("type")]  public string Type   { get; set; } = "";
    [JsonPropertyName("ts")]    public long Timestamp { get; set; }
    [JsonPropertyName("left")]  public StickAxes Left  { get; set; } = new();
    [JsonPropertyName("right")] public StickAxes Right { get; set; } = new();
    [JsonPropertyName("correction_enabled")] public bool CorrectionEnabled { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}
