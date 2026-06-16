using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VRStickScope.Services;

public enum DisplayLanguage
{
    Japanese,
    English
}

public enum DiagnosticStickSide
{
    Left,
    Right
}

public sealed class DiagnosticSummary
{
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public int TotalSamples { get; init; }
}

public sealed class DiagnosticUiStandalone
{
    private const int BinCount = 360;
    private const float DeadzoneThreshold = 0.08f;
    private const float DropThreshold = 0.65f;
    private const int MinimumSamplesForWarning = 3;
    private const float PassiveActiveRadius = 0.45f;
    private const float PassiveOuterRadius = 0.55f;
    private const float PassiveCenterRadius = 0.12f;
    private const double PassiveFlipWindowMs = 260;
    private const double PassiveCenterDropWindowMs = 180;
    private const float PassiveOppositeThresholdDeg = 150f;

    private readonly object _gate = new();
    private readonly StickAccumulator _left = new();
    private readonly StickAccumulator _right = new();

    public DisplayLanguage CurrentLanguage { get; private set; } = DisplayLanguage.Japanese;

    public bool IsJapanese => CurrentLanguage == DisplayLanguage.Japanese;

    public void ToggleLanguage()
    {
        CurrentLanguage = IsJapanese ? DisplayLanguage.English : DisplayLanguage.Japanese;
    }

    public void SetLanguage(DisplayLanguage language)
    {
        CurrentLanguage = language;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _left.Reset();
            _right.Reset();
        }
    }

    public void AddSample(DiagnosticStickSide side, float x, float y)
    {
        lock (_gate)
        {
            GetAccumulator(side).AddSample(x, y);
        }
    }

    public DiagnosticSummary GetSummary(DiagnosticStickSide side)
    {
        lock (_gate)
        {
            return BuildSummary(side, GetAccumulator(side));
        }
    }

    public string GetText(string key)
    {
        Dictionary<string, string> strings = IsJapanese ? JapaneseStrings : EnglishStrings;
        return strings.TryGetValue(key, out string? value) ? value : key;
    }

    public IReadOnlyList<AngleBinSnapshot> GetBins(DiagnosticStickSide side)
    {
        lock (_gate)
        {
            return GetAccumulator(side).GetBins();
        }
    }

    private DiagnosticSummary BuildSummary(DiagnosticStickSide side, StickAccumulator accumulator)
    {
        string stickName = GetStickName(side);
        var warnings = new List<string>();

        if (accumulator.TotalSamples == 0)
        {
            return new DiagnosticSummary
            {
                Title = IsJapanese ? $"{stickName}診断" : $"{stickName} diagnostics",
                Details = GetText("NoData"),
                Warnings = warnings,
                TotalSamples = 0
            };
        }

        IReadOnlyList<AngleBinSnapshot> bins = accumulator.GetBins();
        List<AngleBinSnapshot> activeBins = bins.Where(b => b.SampleCount > 0).ToList();

        // 1. スティックの倒し込み不足（一貫したドロップ）
        List<AngleBinSnapshot> dropBins = activeBins
            .Where(b => b.SampleCount >= MinimumSamplesForWarning && b.MaxRadius < DropThreshold)
            .OrderBy(b => b.Degree)
            .ToList();

        // 2. スティックの軌跡が中心に吸い込まれる挙動（不安定な最小半径）
        // 近隣のビンが外側まで出ているのに、特定のビンで最小値が極端に低い場合を検出
        var collapseBins = new List<AngleBinSnapshot>();
        for (int i = 0; i < BinCount; i++)
        {
            var b = bins[i];
            if (b.SampleCount < MinimumSamplesForWarning) continue;

            bool isCollapse = b.MinRadius < 0.45f && b.MaxRadius > 0.75f;
            if (!isCollapse && b.MinRadius < 0.45f)
            {
                bool neighborOut = false;
                for (int d = -10; d <= 10; d++) // 範囲を少し広げて検出
                {
                    int idx = (i + d + BinCount) % BinCount;
                    if (bins[idx].SampleCount >= MinimumSamplesForWarning && bins[idx].MaxRadius > 0.8f)
                    {
                        neighborOut = true;
                        break;
                    }
                }
                if (neighborOut) isCollapse = true;
            }
            if (isCollapse) collapseBins.Add(b);
        }

        // 3. 高い半径の変動（不安定な入力）
        var unstableBins = activeBins
            .Where(b => b.SampleCount >= MinimumSamplesForWarning && (b.MaxRadius - b.MinRadius) > 0.4f)
            .Except(collapseBins)
            .OrderBy(b => b.Degree)
            .ToList();

        if (collapseBins.Count > 0)
        {
            float minR = collapseBins.Min(b => b.MinRadius);
            warnings.Add(IsJapanese
                ? $"スティックの軌跡が中心に吸い込まれるような挙動が、{FormatRanges(collapseBins)} 度付近で検出されました（最小半径 {minR:0.000}）。ハードウェア故障の可能性が高いです。"
                : $"Stick trajectory collapse toward center detected near {FormatRanges(collapseBins)} degrees (min radius {minR:0.000}). Likely hardware failure.");
        }

        if (unstableBins.Count > 0)
        {
            warnings.Add(IsJapanese
                ? $"特定角度（{FormatRanges(unstableBins)}度）で入力が非常に不安定です。ハードウェア接点の汚れや摩耗の可能性があります。"
                : $"Input is very unstable at specific angles ({FormatRanges(unstableBins)} deg). Possible contact dirt or wear.");
        }

        if (dropBins.Count > 0 && collapseBins.Count == 0) // collapseが優先
        {
            warnings.Add(IsJapanese
                ? $"{FormatRanges(dropBins)} 度付近で最大倒し込み量が低く出ています。スティック摩耗、ゲート形状、または軸の読み違いが疑われます。"
                : $"Low maximum radius near {FormatRanges(dropBins)} degrees. Possible stick wear, gate shape, or axis read issue.");
        }

        if (accumulator.OppositeFlipCount > 0)
        {
            warnings.Add(IsJapanese
                ? $"短時間で約180度反対へ飛ぶ入力を {accumulator.OppositeFlipCount} 回検出しました（直前方向: {FormatDegreeRanges(accumulator.OppositeFlipDegrees)} 度）。ガイドなしでは確定できませんが、真逆入力故障の可能性があります。回して故障診断で確認してください。"
                : $"Possible 180-degree input flips detected {accumulator.OppositeFlipCount} times (prior directions: {FormatDegreeRanges(accumulator.OppositeFlipDegrees)} deg). Live input cannot prove intent; confirm with Guided stick test.");
        }

        if (accumulator.CenterDropCount > 0)
        {
            warnings.Add(IsJapanese
                ? $"外周入力から一瞬0付近へ落ちる入力を {accumulator.CenterDropCount} 回検出しました（直前方向: {FormatDegreeRanges(accumulator.CenterDropDegrees)} 度）。接点抜けや瞬断の可能性があります。"
                : $"Momentary drops from outer input toward center detected {accumulator.CenterDropCount} times (prior directions: {FormatDegreeRanges(accumulator.CenterDropDegrees)} deg). Possible contact dropout or intermittent disconnect.");
        }

        var sb = new StringBuilder();
        sb.AppendLine(IsJapanese ? $"{stickName} 診断サマリー" : $"{stickName} diagnostic summary");
        sb.AppendLine(IsJapanese
            ? $"総サンプル数: {accumulator.TotalSamples}"
            : $"Total samples: {accumulator.TotalSamples}");
        sb.AppendLine(IsJapanese
            ? $"記録済み角度: {activeBins.Count}/360 度"
            : $"Covered angles: {activeBins.Count}/360 degrees");
        sb.AppendLine(IsJapanese
            ? $"半径の最小/最大: {accumulator.MinObservedRadius:0.000} / {accumulator.MaxObservedRadius:0.000}"
            : $"Min/max radius: {accumulator.MinObservedRadius:0.000} / {accumulator.MaxObservedRadius:0.000}");

        if (warnings.Count == 0)
        {
            sb.AppendLine(IsJapanese
                ? "現時点では、特定角度だけ大きく落ちるパターンは検出されていません。"
                : "No angle-specific drop pattern has been detected yet.");
        }
        else
        {
            if (collapseBins.Count > 0)
                sb.AppendLine(IsJapanese ? $"吸い込み検出角度: {FormatRanges(collapseBins)}" : $"Collapse angles: {FormatRanges(collapseBins)}");
            if (unstableBins.Count > 0)
                sb.AppendLine(IsJapanese ? $"不安定角度: {FormatRanges(unstableBins)}" : $"Unstable angles: {FormatRanges(unstableBins)}");
            if (dropBins.Count > 0 && collapseBins.Count == 0)
                sb.AppendLine(IsJapanese ? $"低下角度: {FormatRanges(dropBins)}" : $"Drop angles: {FormatRanges(dropBins)}");
            if (accumulator.OppositeFlipCount > 0)
                sb.AppendLine(IsJapanese ? $"真逆入力の可能性: {accumulator.OppositeFlipCount} 回 ({FormatDegreeRanges(accumulator.OppositeFlipDegrees)} 度)" : $"Possible opposite flips: {accumulator.OppositeFlipCount} ({FormatDegreeRanges(accumulator.OppositeFlipDegrees)} deg)");
            if (accumulator.CenterDropCount > 0)
                sb.AppendLine(IsJapanese ? $"瞬間0落ちの可能性: {accumulator.CenterDropCount} 回 ({FormatDegreeRanges(accumulator.CenterDropDegrees)} 度)" : $"Possible center dropouts: {accumulator.CenterDropCount} ({FormatDegreeRanges(accumulator.CenterDropDegrees)} deg)");
        }

        sb.AppendLine(IsJapanese
            ? "この診断は時間で消えません。リセットするまで角度別の記録を保持します。"
            : "This diagnostic does not fade with time. Angle bins persist until reset.");

        return new DiagnosticSummary
        {
            Title = IsJapanese ? $"{stickName}診断" : $"{stickName} diagnostics",
            Details = sb.ToString(),
            Warnings = warnings,
            TotalSamples = accumulator.TotalSamples
        };
    }

    private StickAccumulator GetAccumulator(DiagnosticStickSide side)
    {
        return side == DiagnosticStickSide.Left ? _left : _right;
    }

    private string GetStickName(DiagnosticStickSide side)
    {
        return side switch
        {
            DiagnosticStickSide.Left => GetText("LeftStick"),
            DiagnosticStickSide.Right => GetText("RightStick"),
            _ => side.ToString()
        };
    }

    private static string FormatRanges(IReadOnlyList<AngleBinSnapshot> bins)
    {
        if (bins.Count == 0) return string.Empty;

        int[] degrees = bins.Select(b => b.Degree).Distinct().OrderBy(d => d).ToArray();
        return FormatDegreeRanges(degrees);
    }

    private static string FormatDegreeRanges(IEnumerable<int> sourceDegrees)
    {
        int[] degrees = sourceDegrees
            .Select(d => ((d % BinCount) + BinCount) % BinCount)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();
        if (degrees.Length == 0) return string.Empty;

        var ranges = new List<(int Start, int End)>();
        int start = degrees[0];
        int previous = start;

        for (int i = 1; i < degrees.Length; i++)
        {
            int degree = degrees[i];
            if (degree == previous + 1)
            {
                previous = degree;
                continue;
            }

            ranges.Add((start, previous));
            start = previous = degree;
        }

        ranges.Add((start, previous));

        if (ranges.Count > 1 && ranges[0].Start == 0 && ranges[^1].End == BinCount - 1)
        {
            (int firstStart, int firstEnd) = ranges[0];
            (int lastStart, int lastEnd) = ranges[^1];
            ranges.RemoveAt(ranges.Count - 1);
            ranges[0] = (lastStart, firstEnd);
        }

        return string.Join(", ", ranges.Select(r => FormatRange(r.Start, r.End)));
    }

    private static string FormatRange(int start, int end)
    {
        if (start > end)
        {
            return $"{start.ToString(CultureInfo.InvariantCulture)}-359, 0-{end.ToString(CultureInfo.InvariantCulture)}";
        }

        return start == end
            ? start.ToString(CultureInfo.InvariantCulture)
            : $"{start.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}";
    }

    private static readonly Dictionary<string, string> JapaneseStrings = new()
    {
        ["Dashboard"] = "入力を見る",
        ["CircleSweep"] = "回して故障診断",
        ["Profiles"] = "補正データ",
        ["Runtime"] = "起動と出力",
        ["LeftStick"] = "左スティック",
        ["RightStick"] = "右スティック",
        ["EngineDisconnected"] = "エンジン未接続",
        ["EngineConnected"] = "エンジン接続中",
        ["LanguageToggle"] = "English",
        ["ResetDiagnostics"] = "表示リセット",
        ["StickyDiagnostics"] = "履歴を保持",
        ["DiagnosticHelp"] = "ここは入力のライブ表示です。故障判定や補正作成は行いません。正確な故障診断は「回して故障診断」を使ってください。",
        ["NoData"] = "診断データがまだありません。SteamVRとVRChatを起動し、HMDを装着してAFKではない状態でスティックを動かしてください。",
        ["SampleCount"] = "サンプル数",
        ["AvgRadius"] = "平均半径",
        ["RadiusVar"] = "半径のばらつき",
        ["MaxAngleErr"] = "最大角度誤差",
        ["Start"] = "計測開始",
        ["Stop"] = "計測停止",
        ["Clear"] = "クリア",
        ["BuildLUT"] = "補正を作成",
        ["NoDirection"] = "方向を選んでください",
        ["Measure"] = "測定開始",
        ["ApplyLUT"] = "補正データを反映",
        ["NewProfile"] = "新規プロファイル",
        ["ProfileName"] = "プロファイル名",
        ["Create"] = "作成",
        ["Cancel"] = "キャンセル",
        ["Delete"] = "削除",
        ["DeleteConfirm"] = "このプロファイルを削除しますか？",
        ["Apply"] = "適用",
        ["Refresh"] = "状態を更新",
        ["OpenFolder"] = "フォルダを開く",
        ["Warning"] = "警告",
        ["RuntimeTitle"] = "エンジン起動とVRChat出力",
        ["EngineSection"] = "診断エンジン",
        ["EnginePathChecking"] = "エンジンパス: 確認中...",
        ["EngineUnknown"] = "エンジン状態: 確認中",
        ["StartDiagnostics"] = "入力表示用エンジンを開始",
        ["StartVrChatOsc"] = "VRChatへ補正入力を送る",
        ["StopEngine"] = "エンジン停止",
        ["SteamVrAutoStart"] = "SteamVR自動起動の解除",
        ["AutoStartChecking"] = "SteamVR自動起動: 確認中...",
        ["DisableAutoStart"] = "SteamVR自動起動を解除",
        ["VrChatOscSection"] = "VRChat OSC出力",
        ["VrChatOscHelp"] = "この画面で開始したときだけ、補正後の移動入力と右スティックの左右旋回をVRChatのOSCポート 127.0.0.1:9000 へ送ります。停止時とアプリ終了時はOSC入力を0へ戻します。"
    };

    private static readonly Dictionary<string, string> EnglishStrings = new()
    {
        ["Dashboard"] = "Live input",
        ["CircleSweep"] = "Guided stick test",
        ["Profiles"] = "Correction data",
        ["LeftStick"] = "Left stick",
        ["RightStick"] = "Right stick",
        ["Runtime"] = "Runtime",
        ["EngineDisconnected"] = "Engine disconnected",
        ["EngineConnected"] = "Engine connected",
        ["LanguageToggle"] = "日本語",
        ["ResetDiagnostics"] = "Reset view",
        ["StickyDiagnostics"] = "Keep history",
        ["DiagnosticHelp"] = "This page only shows live input. It does not diagnose faults or create correction data. For diagnosis, use Guided stick test.",
        ["NoData"] = "No diagnostic data yet. Start SteamVR and VRChat, wear the HMD, stay out of AFK, and move the stick.",
        ["SampleCount"] = "Samples",
        ["AvgRadius"] = "Average radius",
        ["RadiusVar"] = "Radius variation",
        ["MaxAngleErr"] = "Max angle error",
        ["Start"] = "Start measuring",
        ["Stop"] = "Stop measuring",
        ["Clear"] = "Clear",
        ["BuildLUT"] = "Create correction",
        ["NoDirection"] = "Choose a direction",
        ["Measure"] = "Start measurement",
        ["ApplyLUT"] = "Apply correction data",
        ["NewProfile"] = "New profile",
        ["ProfileName"] = "Profile name",
        ["Create"] = "Create",
        ["Cancel"] = "Cancel",
        ["Delete"] = "Delete",
        ["DeleteConfirm"] = "Delete this profile?",
        ["Apply"] = "Apply",
        ["Refresh"] = "Refresh status",
        ["OpenFolder"] = "Open folder",
        ["Warning"] = "Warning",
        ["RuntimeTitle"] = "Engine and VRChat output",
        ["EngineSection"] = "Diagnostic engine",
        ["EnginePathChecking"] = "Engine path: checking...",
        ["EngineUnknown"] = "Engine status: checking",
        ["StartDiagnostics"] = "Start live-input engine",
        ["StartVrChatOsc"] = "Send corrected input to VRChat",
        ["StopEngine"] = "Stop engine",
        ["SteamVrAutoStart"] = "Remove SteamVR auto start",
        ["AutoStartChecking"] = "SteamVR auto start: checking...",
        ["DisableAutoStart"] = "Remove SteamVR auto start",
        ["VrChatOscSection"] = "VRChat OSC output",
        ["VrChatOscHelp"] = "Only while started from this screen, the tool sends corrected movement input and right-stick horizontal turning to VRChat OSC at 127.0.0.1:9000. Stop and app exit reset OSC input to zero."
    };

    private sealed class StickAccumulator
    {
        private readonly Bin[] _bins = Enumerable.Range(0, BinCount).Select(_ => new Bin()).ToArray();
        private readonly List<int> _oppositeFlipDegrees = new();
        private readonly List<int> _centerDropDegrees = new();
        private int _lastActiveDegree = -1;
        private DateTime _lastActiveAtUtc = DateTime.MinValue;
        private float _lastActiveRadius = 0f;
        private bool _centerDropLatched = false;

        public int TotalSamples { get; private set; }
        public float MinObservedRadius { get; private set; }
        public float MaxObservedRadius { get; private set; }
        public int OppositeFlipCount { get; private set; }
        public int CenterDropCount { get; private set; }
        public IReadOnlyList<int> OppositeFlipDegrees => _oppositeFlipDegrees;
        public IReadOnlyList<int> CenterDropDegrees => _centerDropDegrees;

        public void Reset()
        {
            foreach (Bin bin in _bins) bin.Reset();
            _oppositeFlipDegrees.Clear();
            _centerDropDegrees.Clear();
            _lastActiveDegree = -1;
            _lastActiveAtUtc = DateTime.MinValue;
            _lastActiveRadius = 0f;
            _centerDropLatched = false;
            TotalSamples = 0;
            MinObservedRadius = 0f;
            MaxObservedRadius = 0f;
            OppositeFlipCount = 0;
            CenterDropCount = 0;
        }

        public void AddSample(float x, float y)
        {
            DateTime now = DateTime.UtcNow;
            float radius = MathF.Sqrt(x * x + y * y);
            if (radius < DeadzoneThreshold)
            {
                TryRecordCenterDrop(now, radius);
                return;
            }

            int degree;
            degree = ToDegree(x, y);
            TryRecordOppositeFlip(now, degree, radius);
            if (radius > 0.30f)
            {
                _lastActiveDegree = degree;
                _lastActiveAtUtc = now;
                _lastActiveRadius = radius;
                _centerDropLatched = false;
            }

            _bins[degree].Add(x, y, radius);

            TotalSamples++;
            if (TotalSamples == 1)
            {
                MinObservedRadius = radius;
                MaxObservedRadius = radius;
            }
            else
            {
                MinObservedRadius = MathF.Min(MinObservedRadius, radius);
                MaxObservedRadius = MathF.Max(MaxObservedRadius, radius);
            }
        }

        private void TryRecordOppositeFlip(DateTime now, int degree, float radius)
        {
            if (_lastActiveDegree < 0 ||
                _lastActiveAtUtc == DateTime.MinValue ||
                _lastActiveRadius < PassiveActiveRadius ||
                radius < PassiveActiveRadius)
            {
                return;
            }

            double dtMs = (now - _lastActiveAtUtc).TotalMilliseconds;
            if (dtMs <= 0 || dtMs > PassiveFlipWindowMs)
            {
                return;
            }

            float diff = AbsAngleDiff(degree, _lastActiveDegree);
            if (diff < PassiveOppositeThresholdDeg)
            {
                return;
            }

            OppositeFlipCount++;
            _oppositeFlipDegrees.Add(_lastActiveDegree);
        }

        private void TryRecordCenterDrop(DateTime now, float radius)
        {
            if (_centerDropLatched ||
                _lastActiveDegree < 0 ||
                _lastActiveAtUtc == DateTime.MinValue ||
                _lastActiveRadius < PassiveOuterRadius ||
                radius > PassiveCenterRadius)
            {
                return;
            }

            double dtMs = (now - _lastActiveAtUtc).TotalMilliseconds;
            if (dtMs <= 0 || dtMs > PassiveCenterDropWindowMs)
            {
                return;
            }

            CenterDropCount++;
            _centerDropDegrees.Add(_lastActiveDegree);
            _centerDropLatched = true;
        }

        public IReadOnlyList<AngleBinSnapshot> GetBins()
        {
            var result = new AngleBinSnapshot[BinCount];
            for (int degree = 0; degree < BinCount; degree++)
            {
                result[degree] = _bins[degree].ToSnapshot(degree);
            }

            return result;
        }

        private static int ToDegree(float x, float y)
        {
            float degrees = MathF.Atan2(y, x) * 180f / MathF.PI;
            return ((int)MathF.Round(degrees) % BinCount + BinCount) % BinCount;
        }

        private static float AbsAngleDiff(int a, int b)
        {
            float d = MathF.Abs(a - b);
            return d > 180f ? 360f - d : d;
        }
    }

    private sealed class Bin
    {
        private float _sumX;
        private float _sumY;
        private float _sumRadius;

        public int Count { get; private set; }
        public float MinRadius { get; private set; }
        public float MaxRadius { get; private set; }

        public void Reset()
        {
            _sumX = 0f;
            _sumY = 0f;
            _sumRadius = 0f;
            Count = 0;
            MinRadius = 0f;
            MaxRadius = 0f;
        }

        public void Add(float x, float y, float radius)
        {
            _sumX += x;
            _sumY += y;
            _sumRadius += radius;

            if (Count == 0)
            {
                MinRadius = radius;
                MaxRadius = radius;
            }
            else
            {
                MinRadius = MathF.Min(MinRadius, radius);
                MaxRadius = MathF.Max(MaxRadius, radius);
            }

            Count++;
        }

        public AngleBinSnapshot ToSnapshot(int degree)
        {
            if (Count == 0)
            {
                return new AngleBinSnapshot(degree, 0, 0f, 0f, 0f, 0f, 0f);
            }

            return new AngleBinSnapshot(
                degree,
                Count,
                _sumX / Count,
                _sumY / Count,
                _sumRadius / Count,
                MinRadius,
                MaxRadius);
        }
    }
}

public readonly record struct AngleBinSnapshot(
    int Degree,
    int SampleCount,
    float AverageX,
    float AverageY,
    float AverageRadius,
    float MinRadius,
    float MaxRadius);
