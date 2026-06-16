using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.UI;

namespace VRStickScope.Controls;

public sealed partial class StickPlotControl : UserControl
{
    private readonly CanvasControl Canvas = new();
    // 最新の値
    private float _rx, _ry, _cx, _cy;
    private float _guideDeg = float.NaN;
    private float _guideRadius = 1f;
    private List<(float rx, float ry)> _trail = new();

    // カラー定義
    private static readonly Color ColBg       = Color.FromArgb(255, 20,  26,  40);
    private static readonly Color ColGrid     = Color.FromArgb(80,  100, 130, 180);
    private static readonly Color ColRaw      = Color.FromArgb(255, 79,  158, 255);  // 青
    private static readonly Color ColCorrected= Color.FromArgb(255, 61,  220, 132);  // 緑
    private static readonly Color ColTrail    = Color.FromArgb(60,  79,  158, 255);
    private static readonly Color ColRing     = Color.FromArgb(60,  150, 170, 220);
    private static readonly Color ColCenter   = Color.FromArgb(120, 200, 220, 255);
    private static readonly Color ColText     = Color.FromArgb(180, 140, 160, 200);
    private static readonly Color ColGuide    = Color.FromArgb(255, 255, 180, 55);

    public StickPlotControl()
    {
        InitializeComponent();
        Canvas.Draw += Canvas_Draw;
        Canvas.CreateResources += Canvas_CreateResources;
        Content = Canvas;
    }

    public void UpdateStick(float rx, float ry, float cx, float cy,
        List<(float, float)> trail, float guideDeg = float.NaN, float guideRadius = 1f)
    {
        _rx = rx; _ry = ry; _cx = cx; _cy = cy;
        _guideDeg = guideDeg;
        _guideRadius = Math.Clamp(guideRadius, 0f, 1f);
        _trail = new List<(float, float)>(trail);
        Canvas.Invalidate();
    }

    private void Canvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) { }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        float cx = w / 2f;
        float cy = h / 2f;
        float radius = MathF.Min(cx, cy) - 12f;

        // 背景
        ds.FillRectangle(0, 0, w, h, ColBg);

        // アウターリング
        ds.DrawCircle(cx, cy, radius, ColGrid, 1.5f);

        // ミッドリング (0.5)
        ds.DrawCircle(cx, cy, radius * 0.5f, ColRing, 1f);

        // 十字線
        ds.DrawLine(cx - radius, cy, cx + radius, cy, ColGrid, 1f);
        ds.DrawLine(cx, cy - radius, cx, cy + radius, ColGrid, 1f);

        // ラベル
        var textFormat = new CanvasTextFormat
        {
            FontSize = 10,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center
        };
        ds.DrawText("+X", cx + radius + 8, cy, ColText, textFormat);
        ds.DrawText("-X", cx - radius - 8, cy, ColText, textFormat);
        ds.DrawText("+Y", cx, cy - radius - 8, ColText, textFormat);
        ds.DrawText("-Y", cx, cy + radius + 8, ColText, textFormat);

        // トレイル
        if (_trail.Count > 1)
        {
            for (int i = 1; i < _trail.Count; i++)
            {
                float alpha = (float)i / _trail.Count;
                var (x1, y1) = StickToScreen(_trail[i - 1].rx, _trail[i - 1].ry, cx, cy, radius);
                var (x2, y2) = StickToScreen(_trail[i].rx,     _trail[i].ry,     cx, cy, radius);
                var trailColor = Color.FromArgb((byte)(alpha * 80), 79, 158, 255);
                ds.DrawLine(x1, y1, x2, y2, trailColor, 1.5f);
            }
        }

        if (!float.IsNaN(_guideDeg))
        {
            float rad = _guideDeg * MathF.PI / 180f;
            float gx = MathF.Cos(rad) * _guideRadius;
            float gy = MathF.Sin(rad) * _guideRadius;
            var (tx, ty) = StickToScreen(gx, gy, cx, cy, radius);
            if (_guideRadius > 0.05f)
            {
                ds.DrawLine(cx, cy, tx, ty, Color.FromArgb(180, 255, 180, 55), 2.5f);
            }
            ds.FillCircle(tx, ty, 10f, Color.FromArgb(80, 255, 180, 55));
            ds.DrawCircle(tx, ty, 10f, ColGuide, 2.5f);
            ds.FillCircle(tx, ty, 3.5f, ColGuide);
            ds.DrawText(_guideRadius <= 0.05f ? "CENTER" : "GUIDE", tx, ty - 22, ColGuide, textFormat);
        }

        // Raw位置 (青丸)
        var (rrx, rry) = StickToScreen(_rx, _ry, cx, cy, radius);
        ds.FillCircle(rrx, rry, 6f, ColRaw);
        ds.DrawCircle(rrx, rry, 6f, Color.FromArgb(180, 255, 255, 255), 1f);

        // Corrected位置 (緑丸・中抜き)
        var (rcx, rcy) = StickToScreen(_cx, _cy, cx, cy, radius);
        ds.DrawCircle(rcx, rcy, 5f, ColCorrected, 2f);

        // Raw -> Corrected の差分線
        if (MathF.Abs(_rx - _cx) > 0.005f || MathF.Abs(_ry - _cy) > 0.005f)
        {
            ds.DrawLine(rrx, rry, rcx, rcy,
                Color.FromArgb(160, 255, 200, 60), 1.5f);
        }

        // 中心点
        ds.FillCircle(cx, cy, 2.5f, ColCenter);
    }

    private static (float x, float y) StickToScreen(float sx, float sy, float cx, float cy, float radius)
    {
        return (cx + sx * radius, cy - sy * radius); // Y軸反転
    }
}
