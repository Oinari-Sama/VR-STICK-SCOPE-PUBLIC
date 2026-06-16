using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRStickScope.Models;

namespace VRStickScope.Services;

public class ProfileService
{
    private readonly string _profileDir;
    private readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProfileService()
    {
        _profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRStickScope", "Profiles");
        Directory.CreateDirectory(_profileDir);
    }

    public List<CorrectionProfile> LoadAll()
    {
        var list = new List<CorrectionProfile>();
        foreach (var f in Directory.GetFiles(_profileDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(f);
                var p = JsonSerializer.Deserialize<CorrectionProfile>(json, _opts);
                if (p != null)
                {
                    p.Normalize();
                    list.Add(p);
                }
            }
            catch { }
        }
        return list;
    }

    public void Save(CorrectionProfile profile)
    {
        profile.Normalize();
        var path = Path.Combine(_profileDir, $"{profile.Id}.json");
        var json = JsonSerializer.Serialize(profile, _opts);
        File.WriteAllText(path, json);
    }

    public void Delete(string id)
    {
        var path = Path.Combine(_profileDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public string ProfileDirectory => _profileDir;
}
