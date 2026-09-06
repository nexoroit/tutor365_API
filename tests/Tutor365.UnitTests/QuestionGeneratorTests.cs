using FluentAssertions;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Infrastructure.AI;
using Xunit;

namespace Tutor365.UnitTests;

public class QuestionGeneratorTests
{
    private static Question Numeric() => new() { QuestionType = QuestionType.NumericalAnswer, MaxMarks = 2, Difficulty = 3, QuestionText = "Work out 15% of 240." };
    private static Question Choice() => new() { QuestionType = QuestionType.MultipleChoice, MaxMarks = 1, Difficulty = 2, QuestionText = "Which is a prime number?" };

    [Fact]
    public void Parses_json_with_or_without_fences()
    {
        var raw = "Here you go:\n```json\n{\"type\":\"NumericalAnswer\",\"marks\":2,\"text\":\"Find 35% of 180 cm.\",\"hint\":\"h\",\"explanation\":\"e\",\"answers\":[{\"numeric\":63,\"tolerance\":0,\"unit\":\"cm\",\"marks\":2}]}\n```";
        var cq = AiQuestionGeneratorService.Parse(raw);
        cq.Should().NotBeNull(); cq!.Answers![0].Numeric.Should().Be(63);
        AiQuestionGeneratorService.Parse("{\"type\":\"MultipleChoice\"}").Should().NotBeNull();
        AiQuestionGeneratorService.Parse("no json here").Should().BeNull();
    }

    [Fact]
    public void Accepts_a_well_formed_variant()
    {
        var cq = AiQuestionGeneratorService.Parse("{\"type\":\"NumericalAnswer\",\"difficulty\":3,\"marks\":2,\"text\":\"A jacket costs £60. It is reduced by 35%. What is the new price in pounds?\",\"hint\":\"Find 35% first.\",\"explanation\":\"35% of 60 is 21, so 60 - 21 = 39.\",\"answers\":[{\"numeric\":39,\"tolerance\":0,\"unit\":\"£\",\"marks\":2}]}")!;
        AiQuestionGeneratorService.Validate(Numeric(), cq, new[] { "Work out 15% of 240." }).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_wrong_type_marks_duplicates_and_bad_answer_sections()
    {
        var wrongType = AiQuestionGeneratorService.Parse("{\"type\":\"MultipleChoice\",\"marks\":2,\"text\":\"Something long enough here.\",\"hint\":\"h\",\"explanation\":\"e\",\"options\":[{\"text\":\"a\",\"correct\":true},{\"text\":\"b\",\"correct\":false}]}")!;
        AiQuestionGeneratorService.Validate(Numeric(), wrongType, Array.Empty<string>()).Should().Contain(e => e.Contains("type must be NumericalAnswer"));

        var wrongMarks = AiQuestionGeneratorService.Parse("{\"type\":\"NumericalAnswer\",\"marks\":1,\"text\":\"Something long enough here.\",\"hint\":\"h\",\"explanation\":\"e\",\"answers\":[{\"numeric\":5,\"marks\":1}]}")!;
        AiQuestionGeneratorService.Validate(Numeric(), wrongMarks, Array.Empty<string>()).Should().Contain(e => e.Contains("marks must be 2"));

        var duplicate = AiQuestionGeneratorService.Parse("{\"type\":\"NumericalAnswer\",\"marks\":2,\"text\":\"Work out 15% of 240.\",\"hint\":\"h\",\"explanation\":\"e\",\"answers\":[{\"numeric\":36,\"marks\":2}]}")!;
        AiQuestionGeneratorService.Validate(Numeric(), duplicate, new[] { "Work out 15% of 240." }).Should().Contain(e => e.Contains("duplicates"));

        var twoCorrect = AiQuestionGeneratorService.Parse("{\"type\":\"MultipleChoice\",\"marks\":1,\"text\":\"Which of these is prime?\",\"hint\":\"h\",\"explanation\":\"e\",\"options\":[{\"text\":\"7\",\"correct\":true},{\"text\":\"11\",\"correct\":true},{\"text\":\"9\",\"correct\":false}]}")!;
        AiQuestionGeneratorService.Validate(Choice(), twoCorrect, Array.Empty<string>()).Should().Contain(e => e.Contains("exactly one option"));

        var badScheme = AiQuestionGeneratorService.Parse("{\"type\":\"ShortAnswer\",\"marks\":2,\"text\":\"Explain why metals conduct electricity.\",\"hint\":\"h\",\"explanation\":\"e\",\"markScheme\":[{\"criterion\":\"delocalised electrons\",\"marks\":2,\"keywords\":[[\"delocalised\"]]}]}")!;
        AiQuestionGeneratorService.Validate(new Question { QuestionType = QuestionType.ShortAnswer, MaxMarks = 2, QuestionText = "x" }, badScheme, Array.Empty<string>()).Should().Contain(e => e.Contains("2-5 alternatives"));
    }

    [Fact]
    public void Entity_round_trips_to_content_shape_for_the_prompt()
    {
        var q = new Question { QuestionType = QuestionType.Matching, MaxMarks = 3, Difficulty = 2, QuestionText = "Match.", Options = new List<QuestionOption> { new() { Text = "a", MatchKey = "1", SortOrder = 0 }, new() { Text = "b", MatchKey = "2", SortOrder = 1 } } };
        var cq = AiQuestionGeneratorService.ToContent(q);
        cq.Type.Should().Be("Matching"); cq.Pairs.Should().HaveCount(2); cq.Pairs![1].Right.Should().Be("2");
    }
}
