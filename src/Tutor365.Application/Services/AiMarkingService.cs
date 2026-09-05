using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;

namespace Tutor365.Application.Services;

/// <summary>Marks written answers with the AI provider against the question's mark scheme, returning a strict JSON verdict.</summary>
public class AiMarkingService : IAiMarker
{
    private readonly IAiProvider _ai;
    private readonly IAiSettingsService _settings;
    private readonly ILogger<AiMarkingService> _logger;

    public AiMarkingService(IAiProvider ai, IAiSettingsService settings, ILogger<AiMarkingService> logger) { _ai = ai; _settings = settings; _logger = logger; }

    public async Task<AiMarkResult?> MarkAsync(Question q, string answerText, CancellationToken ct = default)
    {
        var cfg = await _settings.GetConfigAsync(ct);
        if (!cfg.IsUsable || !cfg.AiMarkingEnabled || q.MarkScheme.Count == 0 || string.IsNullOrWhiteSpace(answerText)) return null;

        var scheme = string.Join("\n", q.MarkScheme.OrderBy(m => m.SortOrder).Select((m, i) => $"{i + 1}. [{m.Marks} mark{(m.Marks == 1 ? "" : "s")}] {m.CriterionText}"));
        var system = "You are a GCSE examiner marking a student's written answer strictly against the mark scheme. Award marks only for points that are clearly made; accept equivalent wording and correct alternative reasoning. Do not award marks for vague or contradictory statements.\n" +
            "Respond with JSON only, no prose, in exactly this shape:\n" +
            "{\"score\": <number 0.." + q.MaxMarks + ">, \"feedback\": \"<two or three encouraging sentences for a 14-16 year old, British English>\", \"missing\": [\"<criterion text not achieved>\", ...]}";
        var user = $"""
QUESTION ({q.MaxMarks} marks):
{q.QuestionText}

MARK SCHEME:
{scheme}

MODEL EXPLANATION (for your reference only):
{q.Explanation}

STUDENT ANSWER:
{answerText}
""";
        var completion = await _ai.CompleteAsync(new AiRequest(system, new[] { new AiChatMessage("user", user) }, 600, "marking"), ct);
        if (completion.IsStub) return null;

        try
        {
            var json = completion.Content.Trim();
            var start = json.IndexOf('{'); var end = json.LastIndexOf('}');
            if (start < 0 || end <= start) throw new FormatException("no JSON object");
            using var doc = JsonDocument.Parse(json[start..(end + 1)]);
            var root = doc.RootElement;
            var score = Math.Clamp(Math.Round(root.GetProperty("score").GetDecimal() * 2, MidpointRounding.AwayFromZero) / 2, 0, q.MaxMarks);
            var feedback = root.TryGetProperty("feedback", out var f) ? f.GetString() ?? "" : "";
            var missing = root.TryGetProperty("missing", out var m) && m.ValueKind == JsonValueKind.Array ? m.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList() : new List<string>();
            return new AiMarkResult(score, q.MaxMarks, score >= q.MaxMarks, feedback, missing, completion.Model ?? "ai");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI marking returned unparseable output; falling back to rule marking");
            return null;
        }
    }
}
