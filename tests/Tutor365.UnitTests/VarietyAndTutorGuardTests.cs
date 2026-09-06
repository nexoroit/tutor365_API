using FluentAssertions;
using Tutor365.Application.Services;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Xunit;

namespace Tutor365.UnitTests;

public class VarietyAndTutorGuardTests
{
    private static Question NumericQuestion(decimal answer) => new()
    {
        QuestionType = QuestionType.NumericalAnswer, QuestionText = "Work out 3/4 of 96.",
        AcceptedAnswers = new List<QuestionAnswer> { new() { NumericValue = answer, AnswerText = answer.ToString() } }
    };

    private static Question ChoiceQuestion() => new()
    {
        QuestionType = QuestionType.MultipleChoice, QuestionText = "Which rule?",
        Options = new List<QuestionOption> { new() { Text = "Cosine rule", IsCorrect = true }, new() { Text = "Sine rule", IsCorrect = false } }
    };

    [Fact]
    public void Example_that_solves_the_students_own_question_is_detected()
    {
        AiTutorService.LeaksAnswer("Three quarters of 96: 96 ÷ 4 = 24, then 24 × 3 = 72. So the answer is 72.", NumericQuestion(72)).Should().BeTrue();
        AiTutorService.LeaksAnswer("Two sides and the included angle with no matching pair means the cosine rule.", ChoiceQuestion()).Should().BeTrue();
    }

    [Fact]
    public void Example_on_a_different_problem_is_allowed()
    {
        AiTutorService.LeaksAnswer("Let's try a different one: 2/5 of 60. 60 ÷ 5 = 12, and 12 × 2 = 24. Now apply the same steps to yours.", NumericQuestion(72)).Should().BeFalse();
        AiTutorService.LeaksAnswer("Ask yourself: do you know a side and the angle opposite it? That decides which rule fits.", ChoiceQuestion()).Should().BeFalse();
        AiTutorService.LeaksAnswer("The year 1972 saw a similar problem: 720 ÷ 10.", NumericQuestion(72)).Should().BeFalse("72 inside 1972 or 720 is not the answer");
    }

    [Fact]
    public void Variant_choice_is_stable_per_student_and_differs_between_students()
    {
        var candidates = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        var activity = Guid.NewGuid();
        var students = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToList();
        var picks = students.Select(s => StudySessionService.PickVariant(candidates, s, activity, 1)).ToList();
        picks.Distinct().Count().Should().BeGreaterThan(1, "different students should not all get the same question");
        foreach (var s in students) StudySessionService.PickVariant(candidates, s, activity, 1).Should().Be(StudySessionService.PickVariant(candidates, s, activity, 1));
        picks.Should().OnlyContain(p => candidates.Contains(p));
        StudySessionService.PickVariant(new[] { candidates[0] }, students[0], activity, 1).Should().Be(candidates[0]);
    }

    [Fact]
    public void Choice_options_are_shuffled_per_session_but_keep_their_ids()
    {
        var q = new Question { Id = Guid.NewGuid(), QuestionType = QuestionType.MultipleChoice, QuestionText = "?", Options = Enumerable.Range(0, 5).Select(i => new QuestionOption { Id = Guid.NewGuid(), Text = $"opt {i}", SortOrder = i }).ToList() };
        var renders = Enumerable.Range(0, 20).Select(_ => StudySessionService.RenderQuestion(q, Guid.NewGuid()).Options.Select(o => o.Id).ToList()).ToList();
        renders.Select(r => string.Join(",", r)).Distinct().Count().Should().BeGreaterThan(1);
        renders.Should().OnlyContain(r => r.OrderBy(x => x).SequenceEqual(q.Options.Select(o => o.Id).OrderBy(x => x)));
    }
}
