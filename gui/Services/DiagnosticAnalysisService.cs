using System;
using System.Collections.Generic;
using System.Linq;
using InariKontroller.Models;

namespace InariKontroller.Services;

public sealed class DiagnosticAnalysisService
{
    private const int SectorSize = 10;
    private const int TotalDegrees = 360;

    public DiagnosticAnalysisResult Analyze(IReadOnlyList<AngleBinSnapshot> bins)
    {
        if (bins == null || bins.Count < TotalDegrees)
        {
            return new DiagnosticAnalysisResult { Summary = "自動分析：データが不足しています" };
        }

        var activeBins = bins.Where(b => b.SampleCount > 0).ToList();
        if (activeBins.Count < 30)
        {
            return new DiagnosticAnalysisResult
            {
                Summary = "自動分析：計測データが不足しています",
                DetailedAnalysis = "スティックを外周に沿ってゆっくり一周させてください。特定方向の抜けを判断するには、複数方向の記録が必要です。"
            };
        }

        var sectorStats = new List<SectorStat>();
        for (int i = 0; i < TotalDegrees; i += SectorSize)
        {
            var sectorBins = bins.Skip(i).Take(SectorSize).Where(b => b.SampleCount > 0).ToList();
            if (sectorBins.Count == 0) continue;

            sectorStats.Add(new SectorStat
            {
                StartDegree = i,
                SampleCount = sectorBins.Sum(b => b.SampleCount),
                MaxRadius = sectorBins.Max(b => b.MaxRadius),
                MinRadius = sectorBins.Min(b => b.MinRadius),
                AvgRadius = sectorBins.Average(b => b.AverageRadius),
                Coverage = sectorBins.Count / (float)SectorSize
            });
        }

        var collapseSectors = sectorStats.Where(s =>
            s.MaxRadius > 0.78f &&
            s.MinRadius < 0.35f &&
            (s.MaxRadius - s.MinRadius) > 0.45f &&
            s.SampleCount > 8
        ).ToList();

        var unstableSectors = sectorStats.Where(s =>
            s.MaxRadius > 0.70f &&
            (s.MaxRadius - s.MinRadius) > 0.35f &&
            s.SampleCount > 8
        ).Except(collapseSectors).ToList();

        var dropSectors = sectorStats.Where(s =>
            s.MaxRadius < 0.72f &&
            s.SampleCount > 8 &&
            s.Coverage >= 0.3f
        ).ToList();

        if (collapseSectors.Count >= 1)
        {
            string range = FormatSectorRanges(collapseSectors);
            float minRadius = collapseSectors.Min(s => s.MinRadius);
            float maxSpread = collapseSectors.Max(s => s.MaxRadius - s.MinRadius);
            return new DiagnosticAnalysisResult
            {
                PrimaryIssue = StickIssueType.SectorCollapse,
                Summary = "自動分析：扇形の中央落ちを検出",
                DetailedAnalysis = $"{range} 付近で、外周へ倒している途中に入力が中心へ抜けています。最小半径は {minRadius:0.000}、同じ方向内の半径差は最大 {maxSpread:0.000} です。これはセンサー接点の摩耗、汚れ、またはスティックモジュールの物理故障でよく出るパターンです。補正で軽減できる可能性はありますが、強く出る場合は清掃またはスティックモジュール交換も検討してください。",
                Confidence = 0.95f
            };
        }

        if (unstableSectors.Count >= 1)
        {
            string range = FormatSectorRanges(unstableSectors);
            float spread = unstableSectors.Max(s => s.MaxRadius - s.MinRadius);
            return new DiagnosticAnalysisResult
            {
                PrimaryIssue = StickIssueType.Unstable,
                Summary = "自動分析：特定方向の入力が不安定",
                DetailedAnalysis = $"{range} 付近で、同じ方向に倒しているのに入力半径が大きく揺れています。最大の揺れ幅は {spread:0.000} です。接点の汚れ、摩耗、または一時的な断線に近い挙動が疑われます。",
                Confidence = 0.88f
            };
        }

        if (dropSectors.Count >= 1)
        {
            string range = FormatSectorRanges(dropSectors);
            float maxRadius = dropSectors.Max(s => s.MaxRadius);
            return new DiagnosticAnalysisResult
            {
                PrimaryIssue = StickIssueType.EdgeDrop,
                Summary = "自動分析：特定方向の入力不足",
                DetailedAnalysis = $"{range} 付近で入力が外周まで届いていません。最大半径は {maxRadius:0.000} です。スティック軸の摩耗、内部へのゴミの侵入、または可変抵抗器の劣化が疑われます。",
                Confidence = 0.85f
            };
        }

        return new DiagnosticAnalysisResult
        {
            PrimaryIssue = StickIssueType.None,
            Summary = "自動分析：大きな異常は未検出",
            DetailedAnalysis = "現在の計測データからは、扇形の中央落ちや特定方向の大きな入力不足は見つかりませんでした。気になる方向がある場合は、その方向を重点的にゆっくり倒して記録してください。",
            Confidence = 0.8f
        };
    }

    private static string FormatSectorRanges(IReadOnlyList<SectorStat> sectors)
    {
        if (sectors.Count == 0) return "不明な方向";

        var ordered = sectors.OrderBy(s => s.StartDegree).ToList();
        var ranges = new List<string>();
        int start = ordered[0].StartDegree;
        int end = start + SectorSize - 1;

        for (int i = 1; i < ordered.Count; i++)
        {
            int sectorStart = ordered[i].StartDegree;
            if (sectorStart <= end + 1)
            {
                end = sectorStart + SectorSize - 1;
                continue;
            }

            ranges.Add(FormatRange(start, end));
            start = sectorStart;
            end = sectorStart + SectorSize - 1;
        }

        ranges.Add(FormatRange(start, end));
        return string.Join(", ", ranges);
    }

    private static string FormatRange(int start, int end)
    {
        int mid = ((start + end) / 2 + TotalDegrees) % TotalDegrees;
        return $"{DirectionName(mid)}（{start}-{end}度）";
    }

    private static string DirectionName(int degree)
    {
        return degree switch
        {
            >= 338 or < 23 => "右方向",
            < 68 => "右上方向",
            < 113 => "上方向",
            < 158 => "左上方向",
            < 203 => "左方向",
            < 248 => "左下方向",
            < 293 => "下方向",
            _ => "右下方向"
        };
    }

    private sealed class SectorStat
    {
        public int StartDegree { get; set; }
        public int SampleCount { get; set; }
        public float MaxRadius { get; set; }
        public float MinRadius { get; set; }
        public float AvgRadius { get; set; }
        public float Coverage { get; set; }
    }
}
