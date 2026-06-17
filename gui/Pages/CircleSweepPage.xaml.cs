using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using InariKontroller.Models;
using InariKontroller.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InariKontroller.Pages;

public sealed partial class CircleSweepPage : Page
{
    private readonly IpcClientService _ipc = App.IpcClient;
    private readonly List<(float rx, float ry)> _samples = new();
    private readonly List<(float, float)> _emptySamples = new();
    private bool _collecting = false;
    private bool _isLeft = true;
    private readonly List<(float, float)> _trail = new();
    private readonly List<GuidedSweepSample> _guidedSamples = new();
    private readonly List<RefineRange> _refineRanges = new();
    private readonly List<RefineProbe> _refineProbes = new();
    private DateTime _startedAtUtc;
    private DateTime _refineStartedAtUtc;
    private DateTime _lastStatsAtUtc = DateTime.MinValue;
    private DateTime _lastActualAtUtc = DateTime.MinValue;
    private float _lastActualDeg = float.NaN;
    private float _guideDeg = float.NaN;
    private float _guideRadius = 1f;
    private int _refinementPass = 0;
    private bool _collectionCompleted = false;
    private bool _engineStartedForCollection = false;
    private RefineGuideState _currentRefineGuide = new(float.NaN, 1f, RefineProbePhase.Measure, -1, 0, false);
    private SweepPhase _phase = SweepPhase.Idle;
    private const int TrailLen = 200;
    private const float SecondsPerTurn = 11.5f;
    private const float TargetTurns = 3.0f;
    private const float RefineSpeedMultiplier = 1.3f;
    private const float RefineDegreesPerSecond = (360f / SecondsPerTurn) * RefineSpeedMultiplier;
    private const int RefineRepeatsPerRange = 4;
    private const int MaxRefinementPasses = 5;
    private const float RefineRecenterSeconds = 0.55f;
    private const float RefineSettleSeconds = 0.22f;
    private const float RefineMinMeasureSeconds = 0.55f;
    private const float SuddenJumpThresholdDeg = 55f;
    private const float OppositeJumpThresholdDeg = 120f;
    private const float OppositeAlignmentToleranceDeg = 45f;
    private const float StableRadiusThreshold = 0.35f;

    public CircleSweepPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ApplyLanguage();
        Loaded += (_, _) =>
        {
            _ipc.StateUpdated -= OnStateUpdated;
            _ipc.StateUpdated += OnStateUpdated;
        };
        Unloaded += (_, _) => _ipc.StateUpdated -= OnStateUpdated;
    }

    public void ApplyLanguage()
    {
        PageTitle.Text = App.DiagnosticUi.GetText("CircleSweep");
        PageHelpText.Text = App.DiagnosticUi.IsJapanese
            ? "SteamVRとVRChatを起動し、VRChatが入力を受け付ける状態で操作してください。左スティックは時計回り、右スティックは反時計回りに、オレンジの目標点を追って外周を3周します。"
            : "Start SteamVR and VRChat, make sure VRChat accepts input, and follow the orange target dot for three turns: clockwise for the left stick, counter-clockwise for the right stick.";
        GuideTitleText.Text = App.DiagnosticUi.IsJapanese ? "オレンジの点を追って回す" : "Follow the orange dot";
        if (!_collecting && _samples.Count == 0)
        {
            GuideDirectionText.Text = App.DiagnosticUi.IsJapanese ? "計測開始を押してください" : "Press Start";
            GuideProgressText.Text = $"0.0 / {TargetTurns:0.0}";
        }
        SampleCountText.Text = $"{App.DiagnosticUi.GetText("SampleCount")}: {_samples.Count}";
        BtnStart.Content = App.DiagnosticUi.GetText("Start");
        BtnStop.Content = App.DiagnosticUi.GetText("Stop");
        BtnClear.Content = App.DiagnosticUi.GetText("Clear");
        BtnBuild.Content = App.DiagnosticUi.GetText("BuildLUT");
        BtnLeft.Content = App.DiagnosticUi.GetText("LeftStick");
        BtnRight.Content = App.DiagnosticUi.GetText("RightStick");
    }

    private void OnStateUpdated(object? sender, StateUpdatedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var now = DateTime.UtcNow;
            var ax = _isLeft ? e.State.Left : e.State.Right;
            _trail.Add((ax.Rx, ax.Ry));
            if (_trail.Count > TrailLen) _trail.RemoveAt(0);
            float plotGuideDeg = float.NaN;
            if (_collecting)
            {
                float r = MathF.Sqrt(ax.Rx * ax.Rx + ax.Ry * ax.Ry);
                var elapsed = (float)(now - _startedAtUtc).TotalSeconds;
                if (_guidedSamples.Count == 0 && r < 0.5f)
                {
                    _startedAtUtc = now;
                    UpdateGuide(0f, 0f, r, 0f);
                    StickPlot.UpdateStick(ax.Rx, ax.Ry, ax.Cx, ax.Cy, _trail, _guideDeg, _guideRadius);
                    return;
                }

                bool contributesToDiagnosis = _phase != SweepPhase.Refining;
                float expected;
                if (_phase == SweepPhase.Refining)
                {
                    _currentRefineGuide = GetRefineGuide((float)(now - _refineStartedAtUtc).TotalSeconds);
                    expected = _currentRefineGuide.AngleDeg;
                    contributesToDiagnosis = _currentRefineGuide.IsTarget;
                }
                else
                {
                    expected = GetGuideAngle(elapsed);
                    _currentRefineGuide = new RefineGuideState(float.NaN, 1f, RefineProbePhase.Measure, -1, 0, false);
                }
                plotGuideDeg = expected;
                float actual = r > 0.12f ? NormalizeDeg(RadToDeg(MathF.Atan2(ax.Ry, ax.Rx))) : float.NaN;
                float err = float.IsNaN(actual) ? 180f : AbsAngleDiff(expected, actual);
                float priorActual = _lastActualDeg;
                bool hasRecentPrior = !float.IsNaN(priorActual) && _lastActualAtUtc != DateTime.MinValue &&
                    (now - _lastActualAtUtc).TotalMilliseconds <= 350;
                float rawJump = (!float.IsNaN(actual) && hasRecentPrior) ? AbsAngleDiff(actual, priorActual) : 0f;
                bool oppositeInput = contributesToDiagnosis && IsOppositeInput(expected, actual, r, err);
                bool guideDeparture = contributesToDiagnosis && !float.IsNaN(actual) &&
                    r >= StableRadiusThreshold && err >= SuddenJumpThresholdDeg;
                float diagnosticJump = guideDeparture ? MathF.Max(rawJump, err) : rawJump;
                if (oppositeInput) diagnosticJump = MathF.Max(diagnosticJump, OppositeJumpThresholdDeg);
                if (!contributesToDiagnosis)
                {
                    ResetTransientAngleState();
                }
                else if (!float.IsNaN(actual))
                {
                    _lastActualDeg = actual;
                    _lastActualAtUtc = now;
                }
                _guidedSamples.Add(new GuidedSweepSample(expected, actual, r, err, diagnosticJump, NormalizeDeg(expected), _phase, _refinementPass, contributesToDiagnosis));

                if (r > 0.05f && _phase == SweepPhase.Primary)
                {
                    _samples.Add((ax.Rx, ax.Ry));
                    SampleCountText.Text = $"{App.DiagnosticUi.GetText("SampleCount")}: {_samples.Count}";
                }

                UpdateGuide(elapsed, expected, r, diagnosticJump);
                if (elapsed >= SecondsPerTurn * TargetTurns)
                {
                    if (_phase == SweepPhase.Primary && StartRefinementIfNeeded(now))
                    {
                        UpdateGuide(SecondsPerTurn * TargetTurns, _guideDeg, r, diagnosticJump);
                    }
                    else if (_phase != SweepPhase.Refining)
                    {
                        FinishCollection(completed: true);
                    }
                }
                if (_phase == SweepPhase.Refining &&
                    (now - _refineStartedAtUtc).TotalSeconds >= GetRefineDurationSeconds())
                {
                    if (_refinementPass < MaxRefinementPasses && StartRefinementIfNeeded(now, SweepPhase.Refining))
                    {
                        UpdateGuide(SecondsPerTurn * TargetTurns, _guideDeg, r, diagnosticJump);
                    }
                    else
                    {
                        FinishCollection(completed: true);
                    }
                }
            }
            StickPlot.UpdateStick(ax.Rx, ax.Ry, ax.Cx, ax.Cy, _trail, plotGuideDeg, _guideRadius);
            PolarChart.UpdateData(
                _isLeft ? _samples : _emptySamples,
                _isLeft ? _emptySamples : _samples);
            if ((now - _lastStatsAtUtc).TotalMilliseconds >= 250)
            {
                _lastStatsAtUtc = now;
                UpdateStats();
            }
        });
    }

    private void UpdateStats()
    {
        if (_samples.Count == 0) return;
        float avgR = _samples.Average(s => MathF.Sqrt(s.rx * s.rx + s.ry * s.ry));
        StatAvgR.Text = (App.DiagnosticUi.IsJapanese ? "平均の強さ: " : "Average strength: ") + avgR.ToString("F3");
        float sumR2 = _samples.Sum(s => { float r = MathF.Sqrt(s.rx*s.rx+s.ry*s.ry); return (r-avgR)*(r-avgR); });
        StatRadiusVar.Text = (App.DiagnosticUi.IsJapanese ? "強さのばらつき: " : "Strength variation: ") + (_samples.Count > 0 ? MathF.Sqrt(sumR2 / _samples.Count) : 0f).ToString("F3");
        if (_guidedSamples.Count > 0)
        {
            var maxErr = _guidedSamples
                .Where(s => s.ContributesToDiagnosis && !float.IsNaN(s.ActualDeg))
                .Select(s => s.ConsecutiveJumpDeg)
                .DefaultIfEmpty(0f)
                .Max();
            StatMaxAngleErr.Text = (App.DiagnosticUi.IsJapanese ? "最大の角度飛び: " : "Max angle jump: ") + maxErr.ToString("F0") + "°";
            GuidedDiagnosisText.Text = BuildGuidedDiagnosis();
        }
        BtnBuild.IsEnabled = !_collecting && _collectionCompleted && _samples.Count >= 100;
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = false;
        BtnClear.IsEnabled = false;
        GuideDirectionText.Text = App.DiagnosticUi.IsJapanese ? "診断エンジン起動中" : "Starting diagnostic engine";
        GuideWarningText.Text = "";

        if (!await StartDiagnosticEngineForCollectionAsync())
        {
            BtnStart.IsEnabled = true;
            BtnClear.IsEnabled = true;
            GuideDirectionText.Text = App.DiagnosticUi.IsJapanese ? "診断を開始できません" : "Could not start diagnostics";
            GuideWarningText.Text = App.DiagnosticUi.IsJapanese
                ? "診断エンジンに接続できませんでした。SteamVRを起動してからもう一度試してください。"
                : "Could not connect to the diagnostic engine. Start SteamVR and try again.";
            return;
        }

        BeginCollection();
    }

    private void BeginCollection()
    {
        _samples.Clear();
        _guidedSamples.Clear();
        _refineRanges.Clear();
        _refineProbes.Clear();
        _trail.Clear();
        _startedAtUtc = DateTime.UtcNow;
        _refineStartedAtUtc = DateTime.MinValue;
        _lastStatsAtUtc = DateTime.MinValue;
        _lastActualAtUtc = DateTime.MinValue;
        _lastActualDeg = float.NaN;
        _guideDeg = 0f;
        _guideRadius = 1f;
        _refinementPass = 0;
        _collectionCompleted = false;
        _currentRefineGuide = new RefineGuideState(float.NaN, 1f, RefineProbePhase.Measure, -1, 0, false);
        _phase = SweepPhase.Primary;
        _collecting = true;
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled  = true;
        BtnLeft.IsEnabled = false;
        BtnRight.IsEnabled = false;
        BtnClear.IsEnabled = false;
        BtnBuild.IsEnabled = false;
        GuideWarningText.Text = "";
        GuidedDiagnosisText.Text = App.DiagnosticUi.IsJapanese
            ? $"オレンジの点を追って、外周を{GuideDirectionTextValue()}にゆっくり3周してください。"
            : $"Follow the orange dot and slowly rotate {GuideDirectionTextValue()} three turns.";
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        FinishCollection(completed: false);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _samples.Clear(); _trail.Clear();
        _guidedSamples.Clear();
        _refineRanges.Clear();
        _refineProbes.Clear();
        _lastStatsAtUtc = DateTime.MinValue;
        _lastActualAtUtc = DateTime.MinValue;
        _lastActualDeg = float.NaN;
        _guideDeg = float.NaN;
        _guideRadius = 1f;
        _refinementPass = 0;
        _collectionCompleted = false;
        _currentRefineGuide = new RefineGuideState(float.NaN, 1f, RefineProbePhase.Measure, -1, 0, false);
        _phase = SweepPhase.Idle;
        SampleCountText.Text = $"{App.DiagnosticUi.GetText("SampleCount")}: 0";
        BtnBuild.IsEnabled = false;
        StatAvgR.Text = "---"; StatRadiusVar.Text = "---"; StatMaxAngleErr.Text = "---";
        GuidedDiagnosisText.Text = "---";
        GuideWarningText.Text = "";
        GuideDirectionText.Text = App.DiagnosticUi.IsJapanese ? "計測開始を押してください" : "Press Start";
        GuideProgressText.Text = $"0.0 / {TargetTurns:0.0}";
    }

    private void UpdateGuide(float elapsedSeconds, float expectedDeg, float radius, float consecutiveJump)
    {
        _guideDeg = expectedDeg;
        _guideRadius = _phase == SweepPhase.Refining ? _currentRefineGuide.Radius : 1f;
        float turns = MathF.Min(elapsedSeconds / SecondsPerTurn, TargetTurns);
        bool isRefineNonMeasure = _phase == SweepPhase.Refining && !_currentRefineGuide.IsTarget;
        string prefix = _phase == SweepPhase.Refining
            ? _currentRefineGuide.Phase switch
            {
                RefineProbePhase.Recenter => App.DiagnosticUi.IsJapanese ? "中心へ戻す" : "Recenter",
                RefineProbePhase.Settle => App.DiagnosticUi.IsJapanese ? "開始位置で安定" : "Settle",
                _ => App.DiagnosticUi.IsJapanese ? "絞り込み" : "Refine"
            }
            : (App.DiagnosticUi.IsJapanese ? "目標" : "Target");
        GuideDirectionText.Text = App.DiagnosticUi.IsJapanese
            ? $"{prefix}: {DirectionName(expectedDeg)} / {GuideDirectionTextValue()}"
            : $"{prefix}: {DirectionName(expectedDeg)} / {GuideDirectionTextValue()}";
        GuideProgressText.Text = _phase == SweepPhase.Refining
            ? BuildRefineProgressText()
            : $"{turns:0.0} / {TargetTurns:0.0} " + (App.DiagnosticUi.IsJapanese ? "周" : "turns");

        if (isRefineNonMeasure)
        {
            GuideWarningText.Text = "";
        }
        else if (radius < 0.35f)
        {
            GuideWarningText.Text = App.DiagnosticUi.IsJapanese
                ? "今の方向で入力が中心へ落ちています"
                : "Input is dropping toward center in this direction";
        }
        else if (consecutiveJump >= OppositeJumpThresholdDeg)
        {
            GuideWarningText.Text = App.DiagnosticUi.IsJapanese
                ? "角度が大きく飛びました。真逆入力の可能性があります"
                : "Large angle jump detected. Opposite input is possible";
        }
        else if (consecutiveJump >= SuddenJumpThresholdDeg)
        {
            GuideWarningText.Text = App.DiagnosticUi.IsJapanese
                ? "角度が急に飛びました"
                : "Sudden angle jump detected";
        }
        else
        {
            GuideWarningText.Text = "";
        }
    }

    private bool StartRefinementIfNeeded(DateTime now, SweepPhase sourcePhase = SweepPhase.Primary)
    {
        _refineRanges.Clear();

        var sourcePass = sourcePhase == SweepPhase.Refining ? _refinementPass : 0;
        var sectors = BuildSectorSummaries(sourcePhase, sourcePass);
        var weak = sectors.Where(IsWeakSector);
        var reverse = sectors.Where(s => s.ReverseCount >= 1);
        var jump = sectors.Where(s => s.JumpCount >= 1);

        foreach (var sector in weak.Concat(reverse).Concat(jump)
                     .DistinctBy(s => s.Start))
        {
            _refineRanges.Add(new RefineRange(
                NormalizeDeg(sector.Start - 15f),
                NormalizeDeg(sector.End + 15f)));
        }

        var mergedRanges = MergeRanges(_refineRanges);
        _refineRanges.Clear();
        _refineRanges.AddRange(mergedRanges);
        BuildRefineProbes(_guideDeg);

        if (_refineRanges.Count == 0 || _refineProbes.Count == 0) return false;

        _phase = SweepPhase.Refining;
        _refinementPass++;
        _refineStartedAtUtc = now;
        _currentRefineGuide = GetRefineGuide(0f);
        _guideDeg = _currentRefineGuide.AngleDeg;
        _guideRadius = _currentRefineGuide.Radius;
        GuideWarningText.Text = App.DiagnosticUi.IsJapanese
            ? $"異常候補を検出しました。各範囲を中心戻しから{RefineRepeatsPerRange}回ずつ連続で絞り込みます。"
            : $"Possible errors found. Each range will be re-centered and measured {RefineRepeatsPerRange} times in a row.";
        return true;
    }

    private void FinishCollection(bool completed)
    {
        if (!_collecting && _phase == SweepPhase.Complete) return;

        _collecting = false;
        _collectionCompleted = completed;
        _phase = completed ? SweepPhase.Complete : SweepPhase.Idle;
        _guideDeg = float.NaN;
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        BtnLeft.IsEnabled = true;
        BtnRight.IsEnabled = true;
        BtnClear.IsEnabled = true;
        BtnBuild.IsEnabled = completed && _samples.Count >= 100;
        GuideDirectionText.Text = completed
            ? (App.DiagnosticUi.IsJapanese ? "診断完了" : "Diagnosis complete")
            : (App.DiagnosticUi.IsJapanese ? "診断を中止しました" : "Diagnosis stopped");
        GuideProgressText.Text = completed
            ? (_refineRanges.Count > 0
                ? (App.DiagnosticUi.IsJapanese ? "絞り込み検査まで完了" : "Refinement complete")
                : (App.DiagnosticUi.IsJapanese ? "3周の検査完了" : "Three-turn test complete"))
            : (App.DiagnosticUi.IsJapanese ? "途中で停止しました" : "Stopped before completion");
        GuideWarningText.Text = "";
        _guideRadius = 1f;
        _currentRefineGuide = new RefineGuideState(float.NaN, 1f, RefineProbePhase.Measure, -1, 0, false);
        UpdateStats();
        _ = StopDiagnosticEngineAfterCollectionAsync();
    }

    private RefineGuideState GetRefineGuide(float refineElapsedSeconds)
    {
        if (_refineProbes.Count == 0)
        {
            return new RefineGuideState(_guideDeg, 1f, RefineProbePhase.Measure, -1, 0, false);
        }

        float elapsed = MathF.Max(0f, refineElapsedSeconds);
        float cursor = 0f;
        foreach (var probe in _refineProbes)
        {
            float end = cursor + probe.TotalSeconds;
            if (elapsed <= end || ReferenceEquals(probe, _refineProbes[^1]))
            {
                float local = Math.Clamp(elapsed - cursor, 0f, probe.TotalSeconds);
                if (local < RefineRecenterSeconds)
                {
                    return new RefineGuideState(probe.EntryDeg, 0f, RefineProbePhase.Recenter, probe.RangeIndex, probe.Repeat, false);
                }

                local -= RefineRecenterSeconds;
                if (local < RefineSettleSeconds)
                {
                    return new RefineGuideState(probe.EntryDeg, 1f, RefineProbePhase.Settle, probe.RangeIndex, probe.Repeat, false);
                }

                float measureLocal = Math.Clamp(local - RefineSettleSeconds, 0f, probe.MeasureSeconds);
                float t = probe.MeasureSeconds <= 1e-4f ? 1f : measureLocal / probe.MeasureSeconds;
                float angle = MoveAlongGuide(probe.EntryDeg, probe.LengthDeg * t);
                return new RefineGuideState(angle, 1f, RefineProbePhase.Measure, probe.RangeIndex, probe.Repeat, true);
            }
            cursor = end;
        }

        var last = _refineProbes[^1];
        return new RefineGuideState(last.ExitDeg, 1f, RefineProbePhase.Measure, last.RangeIndex, last.Repeat, true);
    }

    private string BuildRefineProgressText()
    {
        if (_refineRanges.Count == 0)
        {
            return App.DiagnosticUi.IsJapanese ? "絞り込み準備中" : "Preparing refinement";
        }

        var elapsed = (float)(DateTime.UtcNow - _refineStartedAtUtc).TotalSeconds;
        var guide = GetRefineGuide(elapsed);
        int index = Math.Clamp(guide.RangeIndex, 0, _refineRanges.Count - 1);
        int repeat = guide.Repeat + 1;
        var range = _refineRanges[index];
        string phaseText = guide.Phase switch
        {
            RefineProbePhase.Recenter => App.DiagnosticUi.IsJapanese ? "中心へ戻す" : "recenter",
            RefineProbePhase.Settle => App.DiagnosticUi.IsJapanese ? "開始位置で安定" : "settle",
            _ => App.DiagnosticUi.IsJapanese ? "測定" : "measure"
        };
        return App.DiagnosticUi.IsJapanese
            ? $"絞り込み{_refinementPass}回目 {index + 1}/{_refineRanges.Count} 反復{repeat}/{RefineRepeatsPerRange} {phaseText}: {FormatAngleRange(range.Start, range.End)}"
            : $"Refine pass {_refinementPass} {index + 1}/{_refineRanges.Count} repeat {repeat}/{RefineRepeatsPerRange} {phaseText}: {FormatAngleRange(range.Start, range.End)}";
    }

    private float GetRefineDurationSeconds()
        => _refineProbes.Sum(p => p.TotalSeconds);

    private void BuildRefineProbes(float currentGuideDeg)
    {
        _refineProbes.Clear();
        if (_refineRanges.Count == 0) return;

        float cursor = float.IsNaN(currentGuideDeg)
            ? GetRangeEntryAngle(_refineRanges[0])
            : NormalizeDeg(currentGuideDeg);
        var orderedRanges = _refineRanges
            .Select((range, index) => new { Range = range, Index = index, Entry = GetRangeEntryAngle(range) })
            .OrderBy(r => GuideTravelDistance(cursor, r.Entry))
            .ToList();

        foreach (var item in orderedRanges)
        {
            for (int repeat = 0; repeat < RefineRepeatsPerRange; repeat++)
            {
                float entry = GetRangeEntryAngle(item.Range);
                float exit = GetRangeExitAngle(item.Range);
                float length = GuideTravelDistance(entry, exit);
                if (length < 1f) length = NormalizeDeg(item.Range.End - item.Range.Start);
                float measureSeconds = MathF.Max(RefineMinMeasureSeconds, length / RefineDegreesPerSecond);
                _refineProbes.Add(new RefineProbe(
                    item.Index,
                    repeat,
                    entry,
                    exit,
                    length,
                    measureSeconds));
                cursor = exit;
            }
        }
    }

    private float GetRangeEntryAngle(RefineRange range)
        => _isLeft ? NormalizeDeg(range.End) : NormalizeDeg(range.Start);

    private float GetRangeExitAngle(RefineRange range)
        => _isLeft ? NormalizeDeg(range.Start) : NormalizeDeg(range.End);

    private float GuideTravelDistance(float fromDeg, float toDeg)
        => _isLeft
            ? NormalizeDeg(fromDeg - toDeg)
            : NormalizeDeg(toDeg - fromDeg);

    private float MoveAlongGuide(float fromDeg, float distanceDeg)
        => _isLeft
            ? NormalizeDeg(fromDeg - distanceDeg)
            : NormalizeDeg(fromDeg + distanceDeg);

    private string BuildGuidedDiagnosis()
    {
        if (_guidedSamples.Count < 60)
        {
            return App.DiagnosticUi.IsJapanese
                ? "診断中: まだデータが足りません。最低3周回してください。"
                : "Measuring: not enough data yet. Rotate at least three turns.";
        }

        int latestRefinePass = _guidedSamples
            .Where(s => s.Phase == SweepPhase.Refining && s.ContributesToDiagnosis)
            .Select(s => s.RefinePass)
            .DefaultIfEmpty(0)
            .Max();
        var sectors = latestRefinePass > 0
            ? BuildSectorSummaries(SweepPhase.Refining, latestRefinePass)
            : BuildSectorSummaries(SweepPhase.Primary, 0);

        var weak = sectors.Where(IsWeakSector).ToList();
        var reverse = sectors.Where(s => s.ReverseCount >= 1).ToList();
        var jump = sectors.Where(s => s.JumpCount >= 1).Except(reverse).ToList();

        var sb = new StringBuilder();
        if (weak.Count == 0 && reverse.Count == 0 && jump.Count == 0)
        {
            if (latestRefinePass > 0 && HasAnyPrimaryIssue())
            {
                return App.DiagnosticUi.IsJapanese
                    ? "診断: 一次検査では異常候補がありましたが、絞り込み検査では再現しませんでした。操作ブレの可能性があります。気になる場合はもう一度診断してください。"
                    : "Diagnosis: the primary sweep found possible issues, but the latest refinement pass did not reproduce them. This may have been input wobble; run the test again if concerned.";
            }

            return App.DiagnosticUi.IsJapanese
                ? "診断: この計測では、特定方向の中心落ち・真逆入力の疑い・大きな角度飛びは検出されていません。"
                : "Diagnosis: no direction-specific center drop, possible opposite input, or large angle jump was detected in this run.";
        }

        sb.Append(App.DiagnosticUi.IsJapanese ? "診断: " : "Diagnosis: ");
        if (weak.Count > 0)
        {
            sb.Append(App.DiagnosticUi.IsJapanese ? "入力が弱くなる方向 " : "weak directions ");
            sb.Append(FormatRanges(RefineErrorRanges(weak, s => s.Radius < 0.55f)));
            sb.Append("。");
        }
        if (reverse.Count > 0)
        {
            sb.Append(App.DiagnosticUi.IsJapanese ? " 真逆入力の疑い " : " possible opposite-direction input ");
            sb.Append(FormatRanges(RefineErrorRanges(reverse, IsOppositeInputSample)));
            sb.Append("。");
        }
        if (jump.Count > 0)
        {
            sb.Append(App.DiagnosticUi.IsJapanese ? " 角度が大きく飛ぶ方向 " : " large angle jump directions ");
            sb.Append(FormatRanges(RefineErrorRanges(jump, IsJumpSample)));
            sb.Append("。");
        }

        return sb.ToString();
    }

    private bool HasAnyPrimaryIssue()
    {
        var sectors = BuildSectorSummaries(SweepPhase.Primary, 0);
        return sectors.Any(s => IsWeakSector(s) || s.ReverseCount >= 1 || s.JumpCount >= 1);
    }

    private List<SectorSummary> BuildSectorSummaries(SweepPhase phase, int refinePass)
    {
        var samples = _guidedSamples
            .Where(s => s.Phase == phase && (phase != SweepPhase.Refining || s.RefinePass == refinePass))
            .Where(s => s.ContributesToDiagnosis)
            .ToList();
        return Enumerable.Range(0, 24)
            .Select(i =>
            {
                float start = i * 15f;
                float end = start + 15f;
                var list = samples.Where(s => s.SectorDeg >= start && s.SectorDeg < end).ToList();
                return new SectorSummary(
                    start,
                    end,
                    list.Count,
                    list.Count == 0 ? 0f : list.Average(s => s.Radius),
                    list.Count(s => s.Radius < 0.35f),
                    list.Count(IsOppositeInputSample),
                    list.Count(IsJumpSample));
            })
            .Where(s => s.Count >= 4)
            .ToList();
    }

    private static bool IsWeakSector(SectorSummary sector)
        => sector.AvgRadius < 0.55f || sector.LowCount >= Math.Max(3, sector.Count / 4);

    private static bool IsOppositeInputSample(GuidedSweepSample sample)
        => sample.ContributesToDiagnosis &&
           IsOppositeInput(sample.ExpectedDeg, sample.ActualDeg, sample.Radius, sample.AngleErrorDeg);

    private static bool IsJumpSample(GuidedSweepSample sample)
        => sample.ContributesToDiagnosis &&
           !float.IsNaN(sample.ActualDeg) &&
           sample.Radius >= StableRadiusThreshold &&
           sample.AngleErrorDeg >= SuddenJumpThresholdDeg;

    private static bool IsOppositeInput(float expectedDeg, float actualDeg, float radius, float angleErrorDeg)
        => !float.IsNaN(actualDeg) &&
           radius >= StableRadiusThreshold &&
           (angleErrorDeg >= OppositeJumpThresholdDeg ||
            AbsAngleDiff(actualDeg, NormalizeDeg(expectedDeg + 180f)) <= OppositeAlignmentToleranceDeg);

    private List<RefineRange> RefineErrorRanges(IEnumerable<SectorSummary> sectors, Func<GuidedSweepSample, bool> isError)
    {
        var ranges = new List<RefineRange>();
        int latestRefinePass = _guidedSamples
            .Where(s => s.Phase == SweepPhase.Refining && s.ContributesToDiagnosis)
            .Select(s => s.RefinePass)
            .DefaultIfEmpty(0)
            .Max();
        var preferredSamples = latestRefinePass > 0
            ? _guidedSamples.Where(s => s.Phase == SweepPhase.Refining && s.RefinePass == latestRefinePass && s.ContributesToDiagnosis).ToList()
            : _guidedSamples.Where(s => s.ContributesToDiagnosis).ToList();

        foreach (var sector in sectors)
        {
            var expanded = new RefineRange(NormalizeDeg(sector.Start - 15f), NormalizeDeg(sector.End + 15f));
            var failed = preferredSamples
                .Where(s => IsAngleInRange(s.SectorDeg, expanded.Start, expanded.End) && isError(s))
                .Select(s => s.SectorDeg)
                .ToList();

            if (failed.Count == 0 && !preferredSamples.Any(s => s.Phase == SweepPhase.Refining))
            {
                ranges.Add(new RefineRange(sector.Start, sector.End));
                continue;
            }
            if (failed.Count == 0)
            {
                continue;
            }

            ranges.Add(CreateTightRange(expanded.Start, failed));
        }

        return MergeRanges(ranges);
    }

    private static RefineRange CreateTightRange(float origin, List<float> angles)
    {
        var offsets = angles.Select(a => NormalizeDeg(a - origin)).OrderBy(a => a).ToList();
        return new RefineRange(
            NormalizeDeg(origin + offsets.First()),
            NormalizeDeg(origin + offsets.Last()));
    }

    private static List<RefineRange> MergeRanges(List<RefineRange> ranges)
    {
        if (ranges.Count <= 1) return ranges;

        var segments = new List<(float Start, float End)>();
        foreach (var range in ranges)
        {
            float start = NormalizeDeg(range.Start);
            float end = NormalizeDeg(range.End);
            if (start <= end)
            {
                segments.Add((start, end));
            }
            else
            {
                segments.Add((start, 360f));
                segments.Add((0f, end));
            }
        }

        var merged = new List<(float Start, float End)>();
        foreach (var segment in segments.OrderBy(s => s.Start))
        {
            if (merged.Count == 0 || segment.Start > merged[^1].End + 1f)
            {
                merged.Add(segment);
            }
            else
            {
                var last = merged[^1];
                merged[^1] = (last.Start, MathF.Max(last.End, segment.End));
            }
        }

        if (merged.Count > 1 && merged[0].Start <= 1f && merged[^1].End >= 359f)
        {
            var first = merged[0];
            var last = merged[^1];
            merged.RemoveAt(merged.Count - 1);
            merged[0] = (last.Start, first.End);
        }

        return merged.Select(r => new RefineRange(NormalizeDeg(r.Start), NormalizeDeg(r.End))).ToList();
    }

    private string FormatRanges(IEnumerable<RefineRange> ranges)
        => string.Join(", ", ranges.Select(r =>
        {
            float mid = NormalizeDeg(r.Start + NormalizeDeg(r.End - r.Start) / 2f);
            return $"{DirectionName(mid)} {FormatAngleRange(r.Start, r.End)}";
        }));

    private static string FormatAngleRange(float start, float end)
        => $"{(int)MathF.Round(NormalizeDeg(start))}-{(int)MathF.Round(NormalizeDeg(end))}°";

    private static bool IsAngleInRange(float angle, float start, float end)
    {
        angle = NormalizeDeg(angle);
        start = NormalizeDeg(start);
        end = NormalizeDeg(end);
        return start <= end
            ? angle >= start && angle <= end
            : angle >= start || angle <= end;
    }

    private string DirectionName(float degree)
    {
        degree = NormalizeDeg(degree);
        if (degree >= 338 || degree < 23) return App.DiagnosticUi.IsJapanese ? "右" : "Right";
        if (degree < 68) return App.DiagnosticUi.IsJapanese ? "右上" : "Upper right";
        if (degree < 113) return App.DiagnosticUi.IsJapanese ? "上" : "Up";
        if (degree < 158) return App.DiagnosticUi.IsJapanese ? "左上" : "Upper left";
        if (degree < 203) return App.DiagnosticUi.IsJapanese ? "左" : "Left";
        if (degree < 248) return App.DiagnosticUi.IsJapanese ? "左下" : "Lower left";
        if (degree < 293) return App.DiagnosticUi.IsJapanese ? "下" : "Down";
        return App.DiagnosticUi.IsJapanese ? "右下" : "Lower right";
    }

    private float GetGuideAngle(float elapsedSeconds)
    {
        float turns = elapsedSeconds / SecondsPerTurn;
        float direction = _isLeft ? -1f : 1f;
        return NormalizeDeg(turns * 360f * direction);
    }

    private string GuideDirectionTextValue()
        => App.DiagnosticUi.IsJapanese
            ? (_isLeft ? "時計回り" : "反時計回り")
            : (_isLeft ? "clockwise" : "counter-clockwise");

    private static float RadToDeg(float rad) => rad * 180f / MathF.PI;
    private static float NormalizeDeg(float deg)
    {
        deg %= 360f;
        return deg < 0 ? deg + 360f : deg;
    }

    private static float AbsAngleDiff(float a, float b)
    {
        float d = MathF.Abs(NormalizeDeg(a) - NormalizeDeg(b));
        return d > 180f ? 360f - d : d;
    }

    private void ResetTransientAngleState()
    {
        _lastActualDeg = float.NaN;
        _lastActualAtUtc = DateTime.MinValue;
    }

    private async Task<bool> StartDiagnosticEngineForCollectionAsync()
    {
        App.IpcClient.SendCommand(new { type = "shutdown" });
        await Task.Delay(300);
        App.EngineRuntime.StopEngine();
        await _ipc.WaitForDisconnectionAsync(TimeSpan.FromSeconds(2));

        _engineStartedForCollection = App.EngineRuntime.StartDiagnostics();
        if (!_engineStartedForCollection) return false;

        return await _ipc.WaitForConnectionAsync(TimeSpan.FromSeconds(8));
    }

    private async Task StopDiagnosticEngineAfterCollectionAsync()
    {
        if (!_engineStartedForCollection) return;

        _engineStartedForCollection = false;
        App.IpcClient.SendCommand(new { type = "shutdown" });
        await Task.Delay(300);
        App.EngineRuntime.StopEngine();
    }

    private async void BtnBuild_Click(object sender, RoutedEventArgs e)
    {
        var lut = new CorrectionLUT();
        lut.BuildFromCircleSweep(_samples);
        var correctedSamples = _samples
            .Select(s =>
            {
                var corrected = lut.Apply(s.rx, s.ry);
                return (s.rx, s.ry, corrected.cx, corrected.cy);
            })
            .ToList();
        lut.BuildAutomaticSectorRepairFromCorrected(correctedSamples);

        var profiles = App.Profiles.LoadAll();
        var profile = profiles.FirstOrDefault() ?? new CorrectionProfile
            { Name = $"Profile {DateTime.Now:yyyy-MM-dd HH:mm}" };
        if (_isLeft) profile.LeftLut  = lut;
        else         profile.RightLut = lut;
        App.Profiles.Save(profile);

        App.IpcClient.SendLut(_isLeft ? "left" : "right", lut);

        var dialog = new ContentDialog
        {
            Title = App.DiagnosticUi.IsJapanese ? "補正データ生成完了" : "Correction data created",
            Content = App.DiagnosticUi.IsJapanese
                ? $"{_samples.Count}サンプルから補正データを生成して保存しました。次にVRChatへ補正入力を送ると、この補正データが使われます。"
                : $"Created correction data from {_samples.Count} samples. It will be used the next time corrected input is sent to VRChat.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void BtnLeft_Click(object sender, RoutedEventArgs e)
    { _isLeft = true;  BtnLeft.IsChecked = true;  BtnRight.IsChecked = false; BtnClear_Click(sender, e); }

    private void BtnRight_Click(object sender, RoutedEventArgs e)
    { _isLeft = false; BtnLeft.IsChecked = false; BtnRight.IsChecked = true;  BtnClear_Click(sender, e); }

    private enum SweepPhase
    {
        Idle,
        Primary,
        Refining,
        Complete
    }

    private sealed record SectorSummary(
        float Start,
        float End,
        int Count,
        float AvgRadius,
        int LowCount,
        int ReverseCount,
        int JumpCount);

    private sealed record RefineRange(float Start, float End);

    private enum RefineProbePhase
    {
        Recenter,
        Settle,
        Measure
    }

    private sealed record RefineProbe(
        int RangeIndex,
        int Repeat,
        float EntryDeg,
        float ExitDeg,
        float LengthDeg,
        float MeasureSeconds)
    {
        public float TotalSeconds => RefineRecenterSeconds + RefineSettleSeconds + MeasureSeconds;
    }

    private readonly record struct RefineGuideState(
        float AngleDeg,
        float Radius,
        RefineProbePhase Phase,
        int RangeIndex,
        int Repeat,
        bool IsTarget);

    private sealed record GuidedSweepSample(float ExpectedDeg, float ActualDeg, float Radius, float AngleErrorDeg, float ConsecutiveJumpDeg, float SectorDeg, SweepPhase Phase, int RefinePass, bool ContributesToDiagnosis);
}

