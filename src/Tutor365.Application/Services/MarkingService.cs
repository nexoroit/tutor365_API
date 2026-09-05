using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;

namespace Tutor365.Application.Services;

public record MarkingResult(bool Correct, decimal Score, decimal MaxScore, string Feedback, IReadOnlyList<string> MissingCriteria, string? CorrectAnswer, MarkingSource MarkedBy);

public interface IMarkingService
{
    MarkingResult Mark(Question question, string? answerText, JsonElement? answerJson);
    /// <summary>Human-readable model answer, shown after answering (never before).</summary>
    string? DescribeCorrectAnswer(Question question);
}

/// <summary>Rule-based marker covering every question type. Written answers use mark-scheme keyword groups; an AI marker can refine later.</summary>
public class MarkingService : IMarkingService
{
    private readonly Microsoft.Extensions.Logging.ILogger<MarkingService>? _logger;
    public MarkingService() { }
    public MarkingService(Microsoft.Extensions.Logging.ILogger<MarkingService> logger) => _logger = logger;

    public MarkingResult Mark(Question q, string? answerText, JsonElement? answerJson)
    {
        var max = (decimal)q.MaxMarks;
        try
        {
            return q.QuestionType switch
            {
                QuestionType.MultipleChoice or QuestionType.TrueFalse or QuestionType.SingleAnswer when q.Options.Count > 0 => MarkSingleChoice(q, answerText, answerJson, max),
                QuestionType.MultipleAnswer => MarkMultipleChoice(q, answerText, answerJson, max),
                QuestionType.SingleAnswer or QuestionType.FillInTheBlank => MarkTextAnswers(q, answerText, answerJson, max),
                QuestionType.NumericalAnswer or QuestionType.FormulaCalculation or QuestionType.Equation => MarkNumeric(q, answerText, answerJson, max),
                QuestionType.Matching or QuestionType.DragAndDrop or QuestionType.DiagramLabelling => MarkPairs(q, answerText, answerJson, max),
                QuestionType.Ordering => MarkOrdering(q, answerText, answerJson, max),
                QuestionType.ShortAnswer or QuestionType.LongAnswer or QuestionType.ExamQuestion => MarkWritten(q, answerText, answerJson, max),
                _ => MarkWritten(q, answerText, answerJson, max)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Marking failed for question {QuestionId} ({Type})", q.Id, q.QuestionType);
            return new MarkingResult(false, 0, max, "We couldn't read that answer. Please check the format and try again.", Array.Empty<string>(), null, MarkingSource.Rule);
        }
    }

    // ---------- choice ----------

    private MarkingResult MarkSingleChoice(Question q, string? text, JsonElement? json, decimal max)
    {
        var selected = SelectedOptionIds(q, text, json);
        var correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        var isCorrect = selected.Count == 1 && correct.Contains(selected[0]);
        var chosen = selected.Count == 1 ? q.Options.FirstOrDefault(o => o.Id == selected[0]) : null;
        var feedback = isCorrect ? "Correct! Well done."
            : chosen?.Feedback ?? (selected.Count == 0 ? "No option was selected." : "Not quite. Read the explanation and try to see why.");
        return new MarkingResult(isCorrect, isCorrect ? max : 0, max, feedback, Array.Empty<string>(), DescribeCorrectAnswer(q), MarkingSource.Rule);
    }

    private MarkingResult MarkMultipleChoice(Question q, string? text, JsonElement? json, decimal max)
    {
        var selected = SelectedOptionIds(q, text, json).ToHashSet();
        var correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        if (correct.Count == 0) return new MarkingResult(false, 0, max, "This question has no correct options configured.", Array.Empty<string>(), null, MarkingSource.Rule);
        var hits = selected.Count(correct.Contains);
        var wrong = selected.Count(id => !correct.Contains(id));
        var raw = Math.Max(0, hits - wrong) / (decimal)correct.Count;
        var score = Round(raw * max);
        var isCorrect = hits == correct.Count && wrong == 0;
        var feedback = isCorrect ? "Correct! You picked all the right answers."
            : hits > 0 ? $"Partly right: {hits} of {correct.Count} correct answers found" + (wrong > 0 ? $", but {wrong} incorrect choice{(wrong > 1 ? "s" : "")} selected." : ".")
            : "None of the selected answers were correct.";
        return new MarkingResult(isCorrect, score, max, feedback, Array.Empty<string>(), DescribeCorrectAnswer(q), MarkingSource.Rule);
    }

    private static List<Guid> SelectedOptionIds(Question q, string? text, JsonElement? json)
    {
        var ids = new List<Guid>();
        if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object)
        {
            if (json.Value.TryGetProperty("selectedOptionIds", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray()) if (Guid.TryParse(e.GetString(), out var g)) ids.Add(g);
            if (json.Value.TryGetProperty("selectedOptionId", out var one) && Guid.TryParse(one.GetString(), out var g1)) ids.Add(g1);
            if (json.Value.TryGetProperty("selectedOptions", out var texts) && texts.ValueKind == JsonValueKind.Array)
                foreach (var e in texts.EnumerateArray())
                {
                    var t = e.GetString();
                    var opt = q.Options.FirstOrDefault(o => Normalise(o.Text) == Normalise(t));
                    if (opt != null) ids.Add(opt.Id);
                }
        }
        else if (json.HasValue && json.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in json.Value.EnumerateArray()) if (e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var g)) ids.Add(g);
        }
        if (ids.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            foreach (var part in text.Split(new[] { '|', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                if (Guid.TryParse(p, out var g)) { ids.Add(g); continue; }
                var opt = q.Options.FirstOrDefault(o => Normalise(o.Text) == Normalise(p));
                if (opt == null && p.Length == 1 && char.IsLetter(p[0]))
                {
                    var idx = char.ToUpperInvariant(p[0]) - 'A';
                    opt = q.Options.OrderBy(o => o.SortOrder).ElementAtOrDefault(idx);
                }
                if (opt != null) ids.Add(opt.Id);
            }
        }
        return ids.Distinct().ToList();
    }

    // ---------- text / blanks ----------

    private MarkingResult MarkTextAnswers(Question q, string? text, JsonElement? json, decimal max)
    {
        var blanks = ExtractBlanks(text, json);
        var groups = q.AcceptedAnswers.GroupBy(a => a.BlankIndex ?? 0).OrderBy(g => g.Key).ToList();
        if (groups.Count == 0) return new MarkingResult(false, 0, max, "No accepted answers are configured for this question.", Array.Empty<string>(), null, MarkingSource.Rule);

        decimal score = 0; decimal possible = 0; var missing = new List<string>();
        foreach (var g in groups)
        {
            var marks = g.Max(a => a.Marks);
            possible += marks;
            var given = blanks.ElementAtOrDefault(g.Key) ?? (groups.Count == 1 ? text : null);
            var hit = g.Any(a => TextMatches(a, given));
            if (hit) score += marks; else missing.Add(groups.Count == 1 ? "Expected: " + g.First().AnswerText : $"Blank {g.Key + 1}: expected '{g.First().AnswerText}'");
        }
        var scaled = possible == 0 ? 0 : Round(score / possible * max);
        var correct = missing.Count == 0;
        var fb = correct ? "Correct!" : missing.Count == groups.Count ? "Not quite. Check the explanation." : "Partly correct.";
        return new MarkingResult(correct, scaled, max, fb, missing, DescribeCorrectAnswer(q), MarkingSource.Rule);
    }

    private static List<string?> ExtractBlanks(string? text, JsonElement? json)
    {
        var list = new List<string?>();
        if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object && json.Value.TryGetProperty("blanks", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray()) list.Add(e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString());
        else if (json.HasValue && json.Value.ValueKind == JsonValueKind.Array)
            foreach (var e in json.Value.EnumerateArray()) list.Add(e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString());
        else if (!string.IsNullOrWhiteSpace(text) && text.Contains('|'))
            list.AddRange(text.Split('|').Select(s => (string?)s.Trim()));
        else if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
        return list;
    }

    private static bool TextMatches(QuestionAnswer accepted, string? given)
    {
        if (string.IsNullOrWhiteSpace(given)) return false;
        if (accepted.NumericValue.HasValue && TryParseNumber(given, out var n))
            return Math.Abs(n - accepted.NumericValue.Value) <= (accepted.NumericTolerance ?? 0);
        var a = accepted.IsCaseSensitive ? accepted.AnswerText.Trim() : Normalise(accepted.AnswerText);
        var g = accepted.IsCaseSensitive ? given.Trim() : Normalise(given);
        if (a == g) return true;
        // tolerate articles and trailing punctuation
        static string Strip(string s) => Regex.Replace(s, @"^(the|a|an)\s+", "").TrimEnd('.', '!');
        return Strip(a) == Strip(g);
    }

    // ---------- numeric ----------

    private MarkingResult MarkNumeric(Question q, string? text, JsonElement? json, decimal max)
    {
        var given = text;
        if (string.IsNullOrWhiteSpace(given) && json.HasValue)
        {
            if (json.Value.ValueKind == JsonValueKind.Object && json.Value.TryGetProperty("value", out var v)) given = v.ToString();
            else if (json.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String) given = json.Value.ToString();
        }
        if (string.IsNullOrWhiteSpace(given)) return new MarkingResult(false, 0, max, "No answer given.", Array.Empty<string>(), DescribeCorrectAnswer(q), MarkingSource.Rule);

        var numericAnswers = q.AcceptedAnswers.Where(a => a.NumericValue.HasValue).ToList();
        if (numericAnswers.Count > 0 && TryParseNumber(given, out var n))
        {
            var best = numericAnswers.OrderByDescending(a => a.Marks).FirstOrDefault(a => Math.Abs(n - a.NumericValue!.Value) <= (a.NumericTolerance ?? 0));
            if (best != null)
                return new MarkingResult(true, max, max, "Correct!", Array.Empty<string>(), DescribeCorrectAnswer(q), MarkingSource.Rule);
            // close but not within tolerance: give a nudge
            var nearest = numericAnswers.OrderBy(a => Math.Abs(n - a.NumericValue!.Value)).First();
            var rel = nearest.NumericValue!.Value == 0 ? 1 : Math.Abs((n - nearest.NumericValue.Value) / nearest.NumericValue.Value);
            var fb = rel < 0.05m ? "Very close, but check your rounding or significant figures." : "Not correct. Check each step of the calculation.";
            return new MarkingResult(false, 0, max, fb, new[] { "Expected " + DescribeCorrectAnswer(q) }, DescribeCorrectAnswer(q), MarkingSource.Rule);
        }

        // symbolic / equation: compare normalised strings
        var symbolic = q.AcceptedAnswers.Any(a => SymbolicEquals(a.AnswerText, given));
        return new MarkingResult(symbolic, symbolic ? max : 0, max, symbolic ? "Correct!" : "Not quite. Check the explanation for the expected form.",
            symbolic ? Array.Empty<string>() : new[] { "Expected " + DescribeCorrectAnswer(q) }, DescribeCorrectAnswer(q), MarkingSource.Rule);
    }

    private static bool SymbolicEquals(string a, string b)
    {
        static string N(string s) => Regex.Replace(s.ToLowerInvariant(), @"\s+|\*|\\times|×", "").Replace("^", "").Replace("**", "").Replace("÷", "/").Replace("−", "-").Replace("–", "-");
        return N(a) == N(b);
    }

    private static bool TryParseNumber(string s, out decimal value)
    {
        value = 0;
        var cleaned = s.Trim().Replace(",", "").Replace("£", "").Replace("−", "-").Replace("–", "-");
        // strip trailing units / words
        var m = Regex.Match(cleaned, @"^[^\d\-\.]*(-?\d+(?:\.\d+)?)(?:\s*[x×]\s*10\^?\s*(-?\d+))?");
        if (!m.Success) return false;
        if (!decimal.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var exp))
            value *= (decimal)Math.Pow(10, exp);
        // fractions like 3/4
        var frac = Regex.Match(cleaned, @"^(-?\d+)\s*/\s*(\d+)$");
        if (frac.Success && decimal.TryParse(frac.Groups[2].Value, out var den) && den != 0)
            value = decimal.Parse(frac.Groups[1].Value, CultureInfo.InvariantCulture) / den;
        return true;
    }

    // ---------- pairs / ordering ----------

    private MarkingResult MarkPairs(Question q, string? text, JsonElement? json, decimal max)
    {
        var options = q.Options.OrderBy(o => o.SortOrder).ToList();
        if (options.Count == 0) return new MarkingResult(false, 0, max, "No pairs configured.", Array.Empty<string>(), null, MarkingSource.Rule);
        var given = new Dictionary<Guid, string>();
        if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object && json.Value.TryGetProperty("pairs", out var pairs))
        {
            if (pairs.ValueKind == JsonValueKind.Array)
                foreach (var p in pairs.EnumerateArray())
                {
                    Guid? id = null;
                    if (p.TryGetProperty("optionId", out var oid) && Guid.TryParse(oid.GetString(), out var g)) id = g;
                    else if (p.TryGetProperty("left", out var left)) id = options.FirstOrDefault(o => Normalise(o.Text) == Normalise(left.GetString()))?.Id;
                    var right = p.TryGetProperty("matchKey", out var mk) ? mk.GetString() : p.TryGetProperty("right", out var r) ? r.GetString() : null;
                    if (id.HasValue && right != null) given[id.Value] = right;
                }
            else if (pairs.ValueKind == JsonValueKind.Object)
                foreach (var prop in pairs.EnumerateObject())
                {
                    var id = Guid.TryParse(prop.Name, out var g) ? g : options.FirstOrDefault(o => Normalise(o.Text) == Normalise(prop.Name))?.Id;
                    if (id.HasValue) given[id.Value] = prop.Value.GetString() ?? "";
                }
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var line in text.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(new[] { "->", "=", ":" }, 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                var opt = options.FirstOrDefault(o => Normalise(o.Text) == Normalise(parts[0]));
                if (opt != null) given[opt.Id] = parts[1];
            }
        }
        var hits = options.Count(o => given.TryGetValue(o.Id, out var r) && Normalise(r) == Normalise(o.MatchKey));
        var score = Round(hits / (decimal)options.Count * max);
        var correct = hits == options.Count;
        var missing = options.Where(o => !(given.TryGetValue(o.Id, out var r) && Normalise(r) == Normalise(o.MatchKey))).Select(o => $"{o.Text} → {o.MatchKey}").ToList();
        return new MarkingResult(correct, score, max, correct ? "All matched correctly!" : $"{hits} of {options.Count} matched correctly.", missing, DescribeCorrectAnswer(q), MarkingSource.Rule);
    }

    private MarkingResult MarkOrdering(Question q, string? text, JsonElement? json, decimal max)
    {
        var options = q.Options.OrderBy(o => int.TryParse(o.MatchKey, out var k) ? k : o.SortOrder).ToList();
        if (options.Count == 0) return new MarkingResult(false, 0, max, "No ordering configured.", Array.Empty<string>(), null, MarkingSource.Rule);
        var givenIds = new List<Guid>();
        if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object && json.Value.TryGetProperty("order", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
            {
                var s = e.GetString();
                if (Guid.TryParse(s, out var g)) givenIds.Add(g);
                else { var o = options.FirstOrDefault(x => Normalise(x.Text) == Normalise(s)); if (o != null) givenIds.Add(o.Id); }
            }
        else if (json.HasValue && json.Value.ValueKind == JsonValueKind.Array)
            foreach (var e in json.Value.EnumerateArray()) if (Guid.TryParse(e.GetString(), out var g)) givenIds.Add(g);
        else if (!string.IsNullOrWhiteSpace(text))
            foreach (var part in text.Split(new[] { '|', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            { var o = options.FirstOrDefault(x => Normalise(x.Text) == Normalise(part)); if (o != null) givenIds.Add(o.Id); }

        var hits = 0;
        for (var i = 0; i < options.Count; i++) if (i < givenIds.Count && givenIds[i] == options[i].Id) hits++;
        var correct = hits == options.Count;
        var score = correct ? max : Round(hits / (decimal)options.Count * max * 0.5m); // partial credit is capped at half
        return new MarkingResult(correct, score, max, correct ? "Correct order!" : $"{hits} of {options.Count} items in the right position.",
            correct ? Array.Empty<string>() : new[] { "Correct order: " + string.Join(" → ", options.Select(o => o.Text)) }, DescribeCorrectAnswer(q), MarkingSource.Rule);
    }

    // ---------- written ----------

    private MarkingResult MarkWritten(Question q, string? text, JsonElement? json, decimal max)
    {
        var answer = text;
        if (string.IsNullOrWhiteSpace(answer) && json.HasValue && json.Value.ValueKind == JsonValueKind.Object && json.Value.TryGetProperty("text", out var t)) answer = t.GetString();
        var norm = Normalise(answer ?? "");
        if (norm.Length == 0) return new MarkingResult(false, 0, max, "No answer given.", q.MarkScheme.Select(m => m.CriterionText).ToList(), null, MarkingSource.Rule);

        var scheme = q.MarkScheme.OrderBy(m => m.SortOrder).ToList();
        if (scheme.Count == 0)
        {
            // No scheme: fall back to accepted answers keyword containment.
            var hit = q.AcceptedAnswers.Any(a => norm.Contains(Normalise(a.AnswerText)));
            return new MarkingResult(hit, hit ? max : 0, max, hit ? "Correct!" : "Not quite. Compare your answer with the explanation.", Array.Empty<string>(), DescribeCorrectAnswer(q), MarkingSource.Rule);
        }

        decimal earned = 0; var possible = scheme.Sum(m => m.Marks); var missing = new List<string>(); var met = new List<string>();
        foreach (var m in scheme)
        {
            var groups = ParseKeywords(m.KeywordsJson);
            bool ok;
            if (groups.Count == 0) ok = norm.Contains(Normalise(m.CriterionText)); // no keywords: require the criterion phrase itself
            else ok = groups.All(g => g.Any(k => norm.Contains(Normalise(k))));
            if (ok) { earned += m.Marks; met.Add(m.CriterionText); } else missing.Add(m.CriterionText);
        }
        var score = possible == 0 ? 0 : Round(earned / possible * max);
        var correct = missing.Count == 0;
        string fb;
        if (correct) fb = "Excellent, you covered every point in the mark scheme.";
        else if (score == 0) fb = "Your answer did not cover the key points. Look at what was missing, then try again.";
        else fb = $"Good start: you covered {met.Count} of {scheme.Count} points. See what was missing to gain full marks.";
        return new MarkingResult(correct, score, max, fb, missing, null, MarkingSource.Rule);
    }

    private static List<List<string>> ParseKeywords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new List<List<string>>();
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                if (g.ValueKind == JsonValueKind.Array) result.Add(g.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList());
                else if (g.ValueKind == JsonValueKind.String) result.Add(new List<string> { g.GetString()! });
            }
            return result.Where(g => g.Count > 0).ToList();
        }
        catch { return new(); }
    }

    // ---------- helpers ----------

    public string? DescribeCorrectAnswer(Question q) => q.QuestionType switch
    {
        QuestionType.MultipleChoice or QuestionType.TrueFalse or QuestionType.MultipleAnswer =>
            string.Join("; ", q.Options.Where(o => o.IsCorrect).OrderBy(o => o.SortOrder).Select(o => o.Text)),
        QuestionType.Matching or QuestionType.DragAndDrop or QuestionType.DiagramLabelling =>
            string.Join("; ", q.Options.OrderBy(o => o.SortOrder).Select(o => $"{o.Text} → {o.MatchKey}")),
        QuestionType.Ordering => string.Join(" → ", q.Options.OrderBy(o => int.TryParse(o.MatchKey, out var k) ? k : o.SortOrder).Select(o => o.Text)),
        QuestionType.ShortAnswer or QuestionType.LongAnswer or QuestionType.ExamQuestion => null,
        _ => q.AcceptedAnswers.Count == 0 ? null
            : string.Join(" | ", q.AcceptedAnswers.GroupBy(a => a.BlankIndex ?? 0).OrderBy(g => g.Key)
                .Select(g => { var a = g.First(); return a.NumericValue.HasValue ? $"{a.NumericValue.Value.ToString("0.####", CultureInfo.InvariantCulture)}{(string.IsNullOrEmpty(a.Unit) ? "" : " " + a.Unit)}" : a.AnswerText; }))
    };

    private static string Normalise(string? s) => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s.ToLowerInvariant().Trim(), @"\s+", " ");
    private static decimal Round(decimal d) => Math.Round(d, 2, MidpointRounding.AwayFromZero);
}
