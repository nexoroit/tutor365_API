using System.Text.Json;
using FluentAssertions;
using Tutor365.Application.Services;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Xunit;

namespace Tutor365.UnitTests;

public class MarkingServiceTests
{
    private readonly MarkingService _sut = new();
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Question Choice(QuestionType type, params (string text, bool correct)[] opts)
    {
        var q = new Question { QuestionType = type, MaxMarks = type == QuestionType.MultipleAnswer ? opts.Count(o => o.correct) : 1, QuestionText = "q" };
        var i = 0;
        foreach (var (text, correct) in opts) q.Options.Add(new QuestionOption { Id = Guid.NewGuid(), Text = text, IsCorrect = correct, SortOrder = i++, QuestionId = q.Id });
        return q;
    }

    [Fact]
    public void MultipleChoice_by_option_id_marks_correct()
    {
        var q = Choice(QuestionType.MultipleChoice, ("A", false), ("B", true), ("C", false));
        var right = q.Options.First(o => o.IsCorrect).Id;
        var r = _sut.Mark(q, null, J($"{{\"selectedOptionIds\":[\"{right}\"]}}"));
        r.Correct.Should().BeTrue(); r.Score.Should().Be(1);
        _sut.Mark(q, "A", null).Correct.Should().BeFalse();
        _sut.Mark(q, "B", null).Correct.Should().BeTrue("option text is accepted");
    }

    [Fact]
    public void MultipleAnswer_gives_partial_credit_and_penalises_wrong_picks()
    {
        var q = Choice(QuestionType.MultipleAnswer, ("A", true), ("B", true), ("C", false), ("D", false));
        var a = q.Options.ElementAt(0).Id; var c = q.Options.ElementAt(2).Id;
        var r = _sut.Mark(q, null, J($"{{\"selectedOptionIds\":[\"{a}\",\"{c}\"]}}"));
        r.Correct.Should().BeFalse(); r.Score.Should().Be(0); // 1 hit - 1 wrong = 0
        var r2 = _sut.Mark(q, null, J($"{{\"selectedOptionIds\":[\"{a}\"]}}"));
        r2.Score.Should().Be(1); r2.MaxScore.Should().Be(2);
    }

    [Fact]
    public void Numerical_respects_tolerance_units_and_standard_form()
    {
        var q = new Question { QuestionType = QuestionType.NumericalAnswer, MaxMarks = 2, QuestionText = "q" };
        q.AcceptedAnswers.Add(new QuestionAnswer { AnswerText = "0.2", NumericValue = 0.2m, NumericTolerance = 0.005m, Unit = "mol", Marks = 2 });
        _sut.Mark(q, "0.20 mol", null).Correct.Should().BeTrue();
        _sut.Mark(q, "0.204", null).Correct.Should().BeTrue();
        _sut.Mark(q, "0.3", null).Correct.Should().BeFalse();
        var big = new Question { QuestionType = QuestionType.FormulaCalculation, MaxMarks = 1, QuestionText = "q" };
        big.AcceptedAnswers.Add(new QuestionAnswer { AnswerText = "3000000", NumericValue = 3000000m, NumericTolerance = 0 });
        _sut.Mark(big, "3 x 10^6 J", null).Correct.Should().BeTrue();
        _sut.Mark(big, "3,000,000", null).Correct.Should().BeTrue();
    }

    [Fact]
    public void FillInTheBlank_marks_each_blank()
    {
        var q = new Question { QuestionType = QuestionType.FillInTheBlank, MaxMarks = 2, QuestionText = "q" };
        q.AcceptedAnswers.Add(new QuestionAnswer { AnswerText = "6.02", BlankIndex = 0, Marks = 1 });
        q.AcceptedAnswers.Add(new QuestionAnswer { AnswerText = "Avogadro", BlankIndex = 1, Marks = 1 });
        var r = _sut.Mark(q, null, J("{\"blanks\":[\"6.02\",\"avogadro\"]}"));
        r.Correct.Should().BeTrue(); r.Score.Should().Be(2);
        var half = _sut.Mark(q, null, J("{\"blanks\":[\"6.02\",\"wrong\"]}"));
        half.Correct.Should().BeFalse(); half.Score.Should().Be(1); half.MissingCriteria.Should().HaveCount(1);
    }

    [Fact]
    public void Matching_scores_fraction_of_pairs()
    {
        var q = new Question { QuestionType = QuestionType.Matching, MaxMarks = 3, QuestionText = "q" };
        q.Options.Add(new QuestionOption { Id = Guid.NewGuid(), Text = "NaCl", MatchKey = "58.5", SortOrder = 0 });
        q.Options.Add(new QuestionOption { Id = Guid.NewGuid(), Text = "NH3", MatchKey = "17", SortOrder = 1 });
        q.Options.Add(new QuestionOption { Id = Guid.NewGuid(), Text = "CH4", MatchKey = "16", SortOrder = 2 });
        var pairs = $"{{\"pairs\":[{{\"optionId\":\"{q.Options.ElementAt(0).Id}\",\"matchKey\":\"58.5\"}},{{\"optionId\":\"{q.Options.ElementAt(1).Id}\",\"matchKey\":\"16\"}},{{\"optionId\":\"{q.Options.ElementAt(2).Id}\",\"matchKey\":\"16\"}}]}}";
        var r = _sut.Mark(q, null, J(pairs));
        r.Correct.Should().BeFalse(); r.Score.Should().Be(2); r.MissingCriteria.Should().ContainSingle();
        var byName = _sut.Mark(q, null, J("{\"pairs\":{\"NaCl\":\"58.5\",\"NH3\":\"17\",\"CH4\":\"16\"}}"));
        byName.Correct.Should().BeTrue();
    }

    [Fact]
    public void Ordering_requires_exact_sequence_for_full_marks()
    {
        var q = new Question { QuestionType = QuestionType.Ordering, MaxMarks = 2, QuestionText = "q" };
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) { var id = Guid.NewGuid(); ids.Add(id); q.Options.Add(new QuestionOption { Id = id, Text = $"step{i}", MatchKey = i.ToString(), SortOrder = 2 - i }); }
        _sut.Mark(q, null, J($"{{\"order\":[\"{ids[0]}\",\"{ids[1]}\",\"{ids[2]}\"]}}")).Correct.Should().BeTrue();
        var partial = _sut.Mark(q, null, J($"{{\"order\":[\"{ids[0]}\",\"{ids[2]}\",\"{ids[1]}\"]}}"));
        partial.Correct.Should().BeFalse(); partial.Score.Should().BeLessThan(2);
    }

    [Fact]
    public void Written_answers_award_criteria_by_keyword_groups()
    {
        var q = new Question { QuestionType = QuestionType.ShortAnswer, MaxMarks = 2, QuestionText = "q" };
        q.MarkScheme.Add(new MarkScheme { CriterionText = "Same number of particles", Marks = 1, SortOrder = 0, KeywordsJson = "[[\"same number\",\"6.02\",\"avogadro\"]]" });
        q.MarkScheme.Add(new MarkScheme { CriterionText = "Mass depends on Mr", Marks = 1, SortOrder = 1, KeywordsJson = "[[\"mr\",\"relative formula mass\",\"molar mass\"],[\"depend\",\"different\"]]" });
        var full = _sut.Mark(q, "A mole always has the same number of particles but the mass depends on the Mr.", null);
        full.Correct.Should().BeTrue(); full.Score.Should().Be(2);
        var half = _sut.Mark(q, "It contains 6.02 x 10^23 particles.", null);
        half.Score.Should().Be(1); half.MissingCriteria.Should().ContainSingle().Which.Should().Contain("Mr");
        _sut.Mark(q, "", null).Score.Should().Be(0);
    }

    [Fact]
    public void TrueFalse_accepts_text()
    {
        var q = Choice(QuestionType.TrueFalse, ("True", false), ("False", true));
        _sut.Mark(q, "false", null).Correct.Should().BeTrue();
        _sut.Mark(q, "true", null).Correct.Should().BeFalse();
    }
}
