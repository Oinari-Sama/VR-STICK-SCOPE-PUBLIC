using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace VRStickScope.Controls;

/// <summary>
/// 左右スティックの軌跡を極座標で表示するチャート。
/// 各角度ビン(1度単位)の平均半径を折れ線プロットする。
/// </summary>
public sealed partial class PolarChartControl : UserControl
{
    private readonly CanvasControl Canvas = new();
    private readonly float[] _leftBinR  = new float[360];
    private readonly float[] _rightBinR = new float[360];
    private readonly int[]   _leftCount  = new int[360];
    private readonly int[]   _rightCount = new int[360];

    private static readonly Color ColBg    = Color.FromArgb(255, 18, 23, 36);
    private static readonly Color ColGrid  = Color.FromArgb(60,  100, 130, 180);
    private static readonly Color ColLeft  = Color.FromArgb(200, 79,  158, 255);
    private static readonly Color ColRight = Color.FromArgb(200, 255, 149, 0);
    private static readonly Color ColIdeal = Color.FromArgb(80,  61,  220, 132);

    public PolarChartControl()
    {
        InitializeComponent();
        Canvas.Draw += Canvas_Draw;
        Content = Canvas;
    }

    public void UpdateData(
        List<(float rx, float ry)> leftSamples,
        List<(float rx, float ry)> rightSamples)
    {
        Array.Clear(_leftBinR,   0, 360);
        Array.Clear(_rightBinR,  0, 360);
        Array.Clear(_leftCount,  0, 360);
        Array.Clear(_rightCount, 0, 360);

        Accumulate(leftSamples,  _leftBinR,  _leftCount);
        Accumulate(rightSamples, _rightBinR, _rightCount);

        Canvas.Invalidate();
    }

    private static void Accumulate(List<(float rx, float ry)> samples,
        float[] binR, int[] binCount)
    {
        foreach (var (x, y) in samples)
        {
            float r = MathF.Sqrt(x * x + y * y);
            if (r < 0.05f) continue;
            float deg = MathF.Atan2(y, x) * (180f / MathF.PI);
            int idx = ((int)MathF.Round(deg) % 360 + 360) % 360;
            binR[idx]    += r;
            binCount[idx]++;
        }
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        float cx = w / 2f;
        float cy = h / 2f;
        float maxR = MathF.Min(cx, cy) - 20f;

        ds.FillRectangle(0, 0, w, h, ColBg);

        // グリッドリング
        foreach (float frac in new[] { 0.25f, 0.5f, 0.75f, 1.0f })
        {
            ds.DrawCircle(cx, cy, maxR * frac, ColGrid, frac == 1f ? 1.5f : 0.8f);
        }

        // 8方向線
        for (int a = 0; a < 360; a += 45)
        {
            float rad = a * MathF.PI / 180f;
            ds.DrawLine(cx, cy,
                cx + MathF.Cos(rad) * maxR,
                cy - MathF.Sin(rad) * maxR,
                ColGrid, 0.8f);
        }

        // 方向ラベル
        var tf = new CanvasTextFormat { FontSize = 10, HorizontalAlignment = CanvasHorizontalAlignment.Center };
        string[] labels = { "+X", "↗", "+Y", "↖", "-X", "↙", "-Y", "↘" };
        for (int i = 0; i < 8; i++)
        {
            float rad = i * 45f * MathF.PI / 180f;
            float lx = cx + MathF.Cos(rad) * (maxR + 14);
            float ly = cy - MathF.Sin(rad) * (maxR + 14);
            ds.DrawText(labels[i], lx, ly - 6, Color.FromArgb(150, 180, 200, 230), tf);
        }

        // 理想円 (半径 = 1.0)
        ds.DrawCircle(cx, cy, maxR, ColIdeal, 2f);

        // 左スティックプロット
        DrawPolarLine(ds, _leftBinR, _leftCount, cx, cy, maxR, ColLeft);

        // 右スティックプロット
        DrawPolarLine(ds, _rightBinR, _rightCount, cx, cy, maxR, ColRight);

        // 凡例
        ds.FillCircle(w - 80, 20, 5, ColLeft);
        ds.DrawText("L Stick", w - 70, 12, ColLeft, new CanvasTextFormat { FontSize = 11 });
        ds.FillCircle(w - 80, 38, 5, ColRight);
        ds.DrawText("R Stick", w - 70, 30, ColRight, new CanvasTextFormat { FontSize = 11 });
    }

    private static void DrawPolarLine(CanvasDrawingSession ds,
        float[] binR, int[] binCount,
        float cx, float cy, float maxR, Color color)
    {
        var pts = new System.Numerics.Vector2[360];
        bool hasAny = false;

        for (int i = 0; i < 360; i++)
        {
            float r = binCount[i] > 0 ? binR[i] / binCount[i] : 0f;
            float rad = i * MathF.PI / 180f;
            pts[i] = new System.Numerics.Vector2(
                cx + MathF.Cos(rad) * r * maxR,
                cy - MathF.Sin(rad) * r * maxR);
            if (binCount[i] > 0) hasAny = true;
        }

        if (!hasAny) return;

        for (int i = 0; i < 360; i++)
        {
            int next = (i + 1) % 360;
            if (binCount[i] > 0 && binCount[next] > 0)
            {
                ds.DrawLine(pts[i], pts[next], color, 2f);
            }
        }
    }
}
