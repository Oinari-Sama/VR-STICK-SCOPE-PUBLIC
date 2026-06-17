namespace InariKontroller.Models;

public enum StickIssueType
{
    None,
    SectorCollapse, // ��`�̒������� / �Z�N�^����
    EdgeDrop,      // ��������̓��͕s��
    Unstable       // ���͂̕s����i�m�C�Y�E�`���^�����O�j
}

public sealed class DiagnosticAnalysisResult
{
    public StickIssueType PrimaryIssue { get; init; } = StickIssueType.None;
    public string Summary { get; init; } = "���͌�����܂���ł���";
    public string DetailedAnalysis { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public bool IsHardwareFailureLikely => PrimaryIssue != StickIssueType.None;
}
