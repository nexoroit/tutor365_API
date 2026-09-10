using FluentAssertions;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Xunit;

namespace Tutor365.UnitTests;

public class StudyScheduleTests
{
    [Fact]
    public void Defaults_apply_to_every_active_day_when_no_overrides_exist()
    {
        var s = new StudySchedule { SessionsPerDay = 2, SessionMinutes = 45, ActiveDays = DaysOfWeek.Weekdays };
        s.PlanFor(DayOfWeek.Monday).Should().Be((2, 45));
        s.PlanFor(DayOfWeek.Saturday).Should().Be((0, 0));
        s.WeeklySessions.Should().Be(10);
        s.WeeklyMinutes.Should().Be(450);
    }

    [Fact]
    public void Per_day_overrides_round_trip_and_change_the_weekly_totals()
    {
        var s = new StudySchedule { SessionsPerDay = 2, SessionMinutes = 45, ActiveDays = DaysOfWeek.All };
        s.WriteDayPlans(new Dictionary<DayOfWeek, (int, int)> { [DayOfWeek.Monday] = (1, 20), [DayOfWeek.Saturday] = (4, 60) });
        s.DayPlansJson.Should().Contain("\"Monday\"").And.Contain("\"minutes\":20");
        s.PlanFor(DayOfWeek.Monday).Should().Be((1, 20));
        s.PlanFor(DayOfWeek.Saturday).Should().Be((4, 60));
        s.PlanFor(DayOfWeek.Tuesday).Should().Be((2, 45), "days without an override use the defaults");
        s.WeeklySessions.Should().Be(1 + 4 + 2 * 5);
        s.WeeklyMinutes.Should().Be(20 + 240 + 90 * 5);
    }

    [Fact]
    public void Inactive_day_wins_over_an_override_and_bad_json_is_ignored()
    {
        var s = new StudySchedule { ActiveDays = DaysOfWeek.Weekdays, DayPlansJson = "{\"Sunday\":{\"sessions\":6,\"minutes\":60}}" };
        s.PlanFor(DayOfWeek.Sunday).Should().Be((0, 0));
        s.DayPlansJson = "{not json";
        s.PlanFor(DayOfWeek.Monday).Should().Be((2, 45));
    }
}
