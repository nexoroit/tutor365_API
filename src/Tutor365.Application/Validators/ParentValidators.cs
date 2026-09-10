using FluentValidation;
using Tutor365.Application.DTOs;

namespace Tutor365.Application.Validators;

public class CreateChildRequestValidator : AbstractValidator<CreateChildRequest>
{
    public CreateChildRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.YearGroupId).NotEmpty();
        RuleFor(x => x.TargetGrade).InclusiveBetween(1, 9);
        RuleFor(x => x.SessionsPerDay).InclusiveBetween(1, 6).When(x => x.SessionsPerDay.HasValue);
        RuleFor(x => x.SessionMinutes).InclusiveBetween(15, 120).When(x => x.SessionMinutes.HasValue);
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow)).When(x => x.DateOfBirth.HasValue);
    }
}

public class UpdateChildRequestValidator : AbstractValidator<UpdateChildRequest>
{
    public UpdateChildRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.TargetGrade).InclusiveBetween(1, 9).When(x => x.TargetGrade.HasValue);
    }
}

public class SetChildPasswordRequestValidator : AbstractValidator<SetChildPasswordRequest>
{
    public SetChildPasswordRequestValidator() => RuleFor(x => x.NewPassword).StrongPassword();
}

public class UpdateStudyScheduleRequestValidator : AbstractValidator<UpdateStudyScheduleRequest>
{
    private static readonly string[] Days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
    public UpdateStudyScheduleRequestValidator()
    {
        RuleFor(x => x.SessionsPerDay).InclusiveBetween(1, 6).When(x => x.SessionsPerDay.HasValue);
        RuleFor(x => x.SessionMinutes).InclusiveBetween(20, 120).When(x => x.SessionMinutes.HasValue);
        RuleForEach(x => x.ActiveDays).Must(d => Days.Contains(d, StringComparer.OrdinalIgnoreCase))
            .WithMessage("ActiveDays must contain day names (Monday..Sunday).");
        RuleForEach(x => x.Days).ChildRules(d =>
        {
            d.RuleFor(x => x.Day).Must(v => Days.Contains(v, StringComparer.OrdinalIgnoreCase)).WithMessage("Day must be Monday..Sunday.");
            d.RuleFor(x => x.Sessions).InclusiveBetween(1, 6).When(x => x.Active).WithMessage("Sessions per day must be between 1 and 6.");
            d.RuleFor(x => x.Minutes).InclusiveBetween(20, 120).When(x => x.Active).WithMessage("Minutes per session must be between 20 and 120.");
        }).When(x => x.Days != null);
        RuleFor(x => x.Days).Must(days => days!.Any(d => d.Active)).When(x => x.Days != null && x.Days.Count > 0).WithMessage("At least one study day must be active.");
    }
}

public class UpdateSubjectSettingRequestValidator : AbstractValidator<UpdateSubjectSettingRequest>
{
    public UpdateSubjectSettingRequestValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.TargetGrade).InclusiveBetween(1, 9).When(x => x.TargetGrade.HasValue);
        RuleFor(x => x.PassThresholdPercent).InclusiveBetween(30, 100).When(x => x.PassThresholdPercent.HasValue);
        RuleFor(x => x.MaxAttemptsBeforeMoveOn).InclusiveBetween(0, 10).When(x => x.MaxAttemptsBeforeMoveOn.HasValue);
        RuleFor(x => x.Priority).InclusiveBetween(1, 5).When(x => x.Priority.HasValue);
        RuleFor(x => x.Tier).Must(t => t == null || Enum.TryParse<Domain.Enums.Tier>(t, true, out _)).WithMessage("Tier must be Foundation, Higher or NotApplicable.");
    }
}
