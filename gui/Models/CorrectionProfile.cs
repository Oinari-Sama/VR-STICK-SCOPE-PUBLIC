using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InariKontroller.Models;

public class CorrectionProfile
{
    [JsonPropertyName("id")]
    public string Id          { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")]
    public string Name        { get; set; } = "New Profile";
    [JsonPropertyName("created_at")]
    public string CreatedAt   { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("notes")]
    public string Notes       { get; set; } = "";
    [JsonPropertyName("steamvr_version")]
    public string SteamVrVer  { get; set; } = "";
    [JsonPropertyName("vd_version")]
    public string VdVersion   { get; set; } = "";
    [JsonPropertyName("needs_rediagnosis")]
    public bool   NeedsRediag { get; set; } = false;

    [JsonPropertyName("left_lut")]
    public CorrectionLUT LeftLut  { get; set; } = new();
    [JsonPropertyName("right_lut")]
    public CorrectionLUT RightLut { get; set; } = new();

    [JsonIgnore]
    public string ApplyText => App.DiagnosticUi.GetText("Apply");
    [JsonIgnore]
    public string DeleteText => App.DiagnosticUi.GetText("Delete");

    public void Normalize()
    {
        LeftLut ??= new CorrectionLUT();
        RightLut ??= new CorrectionLUT();
        LeftLut.EnsureComplete();
        RightLut.EnsureComplete();
    }
}

