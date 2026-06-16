using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRStickScope.Models;

public class LutEntry
{
    [JsonPropertyName("rs")]
    public float RadiusScale { get; set; } = 1f;
    [JsonPropertyName("ao")]
    public float AngleOffset { get; set; } = 0f;
    [JsonPropertyName("xc")]
    public float XCross      { get; set; } = 0f;
    [JsonPropertyName("yc")]
    public float YCross      { get; set; } = 0f;
}

public class CorrectionLUT
{
    public const int Bins = 360;
    [JsonPropertyName("entries")]
    public LutEntry[] Entries { get; set; } = CreateDefaultEntries();
    [JsonPropertyName("strength")]
    public float Strength { get; set; } = 1f;

    private static LutEntry[] CreateDefaultEntries()
    {
        var entries = new LutEntry[Bins];
        for (int i = 0; i < Bins; i++) entries[i] = new LutEntry();
        return entries;
    }

    public void EnsureComplete()
    {
        if (Entries.Length != Bins)
        {
            var resized = CreateDefaultEntries();
            Array.Copy(Entries, resized, Math.Min(Entries.Length, Bins));
            Entries = resized;
        }

        for (int i = 0; i < Entries.Length; i++)
        {
            Entries[i] ??= new LutEntry();
        }
    }

    public static int AngleIndex(float x, float y)
    {
        if (x == 0 && y == 0) return 0;
        float deg = MathF.Atan2(y, x) * (180f / MathF.PI);
        int idx = (int)MathF.Round(deg) % 360;
        if (idx < 0) idx += 360;
        return idx;
    }

    public (float cx, float cy) Apply(float rx, float ry)
    {
        EnsureComplete();
        float r = MathF.Sqrt(rx * rx + ry * ry);
        if (r < 1e-6f) return (rx, ry);

        int idx = AngleIndex(rx, ry);
        var e = Entries[idx];

        float theta = MathF.Atan2(ry, rx);
        float corrTheta = theta + e.AngleOffset * (MathF.PI / 180f);
        float corrR = r * e.RadiusScale;

        float bx = corrR * MathF.Cos(corrTheta) - e.XCross * ry;
        float by = corrR * MathF.Sin(corrTheta) - e.YCross * rx;

        float cx = Math.Clamp(rx + (bx - rx) * Strength, -1f, 1f);
        float cy = Math.Clamp(ry + (by - ry) * Strength, -1f, 1f);
        return (cx, cy);
    }

    // 円回し診断: 収集したサンプルからLUTを構築
    public void BuildFromCircleSweep(System.Collections.Generic.List<(float rx, float ry)> samples)
    {
        // 各角度ビンへのサンプル集計
        var binSamples = new System.Collections.Generic.List<(float rx, float ry)>[Bins];
        for (int i = 0; i < Bins; i++) binSamples[i] = new();

        foreach (var (rx, ry) in samples)
        {
            float r = MathF.Sqrt(rx * rx + ry * ry);
            if (r < 0.3f) continue; // スティック中央付近は無視
            int idx = AngleIndex(rx, ry);
            binSamples[idx].Add((rx, ry));
        }

        // 各ビンの平均半径と角度誤差を計算
        float globalAvgR = 0;
        int countR = 0;
        for (int i = 0; i < Bins; i++)
        {
            if (binSamples[i].Count == 0) continue;
            foreach (var (rx, ry) in binSamples[i])
            {
                globalAvgR += MathF.Sqrt(rx * rx + ry * ry);
                countR++;
            }
        }
        if (countR > 0) globalAvgR /= countR;

        for (int i = 0; i < Bins; i++)
        {
            if (binSamples[i].Count == 0)
            {
                Entries[i] = new LutEntry();
                continue;
            }

            float avgR = 0;
            float sumSinErr = 0, sumCosErr = 0;
            float expectedAngle = i * MathF.PI / 180f;

            foreach (var (rx, ry) in binSamples[i])
            {
                float r = MathF.Sqrt(rx * rx + ry * ry);
                float actualAngle = MathF.Atan2(ry, rx);
                float angErr = expectedAngle - actualAngle;
                avgR += r;
                sumSinErr += MathF.Sin(angErr);
                sumCosErr += MathF.Cos(angErr);
            }
            avgR /= binSamples[i].Count;
            float angleErr = MathF.Atan2(sumSinErr / binSamples[i].Count, sumCosErr / binSamples[i].Count);

            Entries[i] = new LutEntry
            {
                RadiusScale = globalAvgR > 0 ? globalAvgR / avgR : 1f,
                AngleOffset = angleErr * 180f / MathF.PI,
                XCross = 0f,
                YCross = 0f
            };
        }

        // スムージング
        Smooth(5);
    }

    // 自動セクター修復: 収集したサンプルから局所的な縮退を修正するLUTを構築
    public void BuildAutomaticSectorRepair(System.Collections.Generic.List<(float rx, float ry)> samples)
    {
        var binSamples = new System.Collections.Generic.List<(float rx, float ry)>[Bins];
        for (int i = 0; i < Bins; i++) binSamples[i] = new();

        foreach (var (rx, ry) in samples)
        {
            float r = MathF.Sqrt(rx * rx + ry * ry);
            if (r < 0.2f) continue;
            int idx = AngleIndex(rx, ry);
            binSamples[idx].Add((rx, ry));
        }

        float[] binAvgR = new float[Bins];
        float[] binMaxR = new float[Bins];
        float globalMaxR = 0f;

        for (int i = 0; i < Bins; i++)
        {
            if (binSamples[i].Count == 0) continue;
            float sum = 0, max = 0;
            foreach (var (rx, ry) in binSamples[i])
            {
                float r = MathF.Sqrt(rx * rx + ry * ry);
                sum += r;
                if (r > max) max = r;
            }
            binAvgR[i] = sum / binSamples[i].Count;
            binMaxR[i] = max;
            if (max > globalMaxR) globalMaxR = max;
        }

        float[] targetR = new float[Bins];
        for (int i = 0; i < Bins; i++)
        {
            float localMax = 0;
            for (int d = -15; d <= 15; d++)
            {
                int j = (i + d + Bins) % Bins;
                if (binMaxR[j] > localMax) localMax = binMaxR[j];
            }
            targetR[i] = localMax > 0 ? localMax : (globalMaxR > 0 ? globalMaxR : 1f);
        }

        for (int i = 0; i < Bins; i++)
        {
            Entries[i] = new LutEntry();
            if (binSamples[i].Count > 0 && targetR[i] > 0 && binMaxR[i] > 0)
            {
                float scale = targetR[i] / binMaxR[i];
                scale = Math.Clamp(scale, 1f, 1.5f);
                Entries[i].RadiusScale = scale;
            }
            else
            {
                Entries[i].RadiusScale = 1f;
            }
        }

        Smooth(15);
    }

    // 補正後入力も使う局所補正: 既存補正を入れても弱い方向を追加で強める
    public void BuildAutomaticSectorRepairFromCorrected(System.Collections.Generic.List<(float rx, float ry, float cx, float cy)> samples)
    {
        EnsureComplete();
        var rawSamples = new System.Collections.Generic.List<(float rx, float ry)>();
        var binCorrectedMaxR = new float[Bins];
        var binRawMaxR = new float[Bins];
        float globalCorrectedMaxR = 0f;

        foreach (var (rx, ry, cx, cy) in samples)
        {
            float rawR = MathF.Sqrt(rx * rx + ry * ry);
            float correctedR = MathF.Sqrt(cx * cx + cy * cy);
            if (rawR < 0.2f) continue;

            rawSamples.Add((rx, ry));
            int idx = AngleIndex(rx, ry);
            if (rawR > binRawMaxR[idx]) binRawMaxR[idx] = rawR;
            if (correctedR > binCorrectedMaxR[idx]) binCorrectedMaxR[idx] = correctedR;
            if (correctedR > globalCorrectedMaxR) globalCorrectedMaxR = correctedR;
        }

        if (rawSamples.Count == 0)
        {
            return;
        }

        float[] targetR = new float[Bins];
        for (int i = 0; i < Bins; i++)
        {
            float localMax = 0;
            for (int d = -15; d <= 15; d++)
            {
                int j = (i + d + Bins) % Bins;
                if (binCorrectedMaxR[j] > localMax) localMax = binCorrectedMaxR[j];
            }
            targetR[i] = localMax > 0 ? localMax : (globalCorrectedMaxR > 0 ? globalCorrectedMaxR : 1f);
        }

        for (int i = 0; i < Bins; i++)
        {
            if (targetR[i] > 0 && binCorrectedMaxR[i] > 0 && binRawMaxR[i] > 0)
            {
                float correctedScale = targetR[i] / binCorrectedMaxR[i];
                float rawScale = targetR[i] / binRawMaxR[i];
                float scale = MathF.Max(correctedScale, rawScale);
                Entries[i].RadiusScale = Math.Clamp(MathF.Max(Entries[i].RadiusScale, scale), 1f, 1.8f);
            }
        }

        SmoothRadiusScale(15);
    }

    private void Smooth(int window)
    {
        var copy = (LutEntry[])Entries.Clone();
        int half = window / 2;
        for (int i = 0; i < Bins; i++)
        {
            float rs = 0, ao = 0, xc = 0, yc = 0, w = 0;
            for (int d = -half; d <= half; d++)
            {
                int j = ((i + d) % Bins + Bins) % Bins;
                float weight = 1f - MathF.Abs(d) / (half + 1f);
                rs += copy[j].RadiusScale * weight;
                ao += copy[j].AngleOffset * weight;
                xc += copy[j].XCross * weight;
                yc += copy[j].YCross * weight;
                w  += weight;
            }
            Entries[i] = new LutEntry { RadiusScale = rs/w, AngleOffset = ao/w, XCross = xc/w, YCross = yc/w };
        }
    }

    private void SmoothRadiusScale(int window)
    {
        var copy = (LutEntry[])Entries.Clone();
        int half = window / 2;
        for (int i = 0; i < Bins; i++)
        {
            float rs = 0, w = 0;
            for (int d = -half; d <= half; d++)
            {
                int j = ((i + d) % Bins + Bins) % Bins;
                float weight = 1f - MathF.Abs(d) / (half + 1f);
                rs += copy[j].RadiusScale * weight;
                w += weight;
            }
            Entries[i].RadiusScale = Math.Clamp(rs / w, 1f, 1.8f);
        }
    }
}
