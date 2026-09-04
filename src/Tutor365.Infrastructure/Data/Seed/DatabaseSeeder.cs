using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tutor365.Application.Common;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Common;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;

namespace Tutor365.Infrastructure.Data.Seed;

/// <summary>Idempotent reference-data seeder. Safe to run on every start-up.</summary>
public class DatabaseSeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly AppOptions _app;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(AppDbContext db, IPasswordHasher hasher, IOptions<AppOptions> app, ILogger<DatabaseSeeder> logger)
    {
        _db = db; _hasher = hasher; _app = app.Value; _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedExamBoardsAsync(ct);
        await SeedYearGroupsAsync(ct);
        await SeedSubjectsAsync(ct);
        await _db.SaveChangesAsync(ct);
        await SeedCurriculumAsync(ct);
        await SeedSystemSettingsAsync(ct);
        await SeedAdminAsync(ct);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Reference data seeding complete.");
    }

    // ---------- reference tables ----------

    private static readonly (string Code, string Name, string Site)[] Boards =
    {
        ("AQA", "AQA", "https://www.aqa.org.uk"),
        ("EDEXCEL", "Pearson Edexcel", "https://qualifications.pearson.com"),
        ("OCR", "OCR", "https://www.ocr.org.uk")
    };

    private static readonly (string Code, string Name, string Colour, string Icon, int Order, string Desc)[] Subjects =
    {
        ("MAT", "Mathematics", "#3B5BDB", "calculator", 1, "Number, algebra, ratio, geometry, probability and statistics."),
        ("ENL", "English Language", "#E8590C", "book-open", 2, "Reading fiction and non-fiction; creative and viewpoint writing."),
        ("ELT", "English Literature", "#C2255C", "feather", 3, "Shakespeare, the 19th-century novel, modern texts and poetry."),
        ("BIO", "Biology", "#2F9E44", "leaf", 4, "Cells, organisation, infection, bioenergetics, homeostasis, inheritance and ecology."),
        ("CHE", "Chemistry", "#7048E8", "flask", 5, "Atomic structure, bonding, quantitative chemistry, reactions, organic chemistry and analysis."),
        ("PHY", "Physics", "#1098AD", "atom", 6, "Energy, electricity, particles, atomic structure, forces, waves, magnetism and space.")
    };

    private async Task SeedExamBoardsAsync(CancellationToken ct)
    {
        foreach (var (code, name, site) in Boards)
        {
            var id = DeterministicGuid.Create($"examboard:{code}");
            var entity = await _db.ExamBoards.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity == null) _db.ExamBoards.Add(new ExamBoard { Id = id, Code = code, Name = name, Website = site });
            else { entity.Name = name; entity.Website = site; }
        }
    }

    private async Task SeedYearGroupsAsync(CancellationToken ct)
    {
        foreach (var n in new[] { 9, 10, 11 })
        {
            var id = DeterministicGuid.Create($"year:{n}");
            if (!await _db.YearGroups.AnyAsync(x => x.Id == id, ct))
                _db.YearGroups.Add(new YearGroup { Id = id, Number = n, Name = $"Year {n}", KeyStage = n == 9 ? KeyStage.KS3 : KeyStage.KS4 });
        }
    }

    private async Task SeedSubjectsAsync(CancellationToken ct)
    {
        foreach (var (code, name, colour, icon, order, desc) in Subjects)
        {
            var id = DeterministicGuid.Create($"subject:{code}");
            var entity = await _db.Subjects.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity == null) _db.Subjects.Add(new Subject { Id = id, Code = code, Name = name, ColourHex = colour, Icon = icon, SortOrder = order, Description = desc });
            else { entity.Name = name; entity.ColourHex = colour; entity.Icon = icon; entity.SortOrder = order; entity.Description = desc; }
        }
    }

    // ---------- curriculum from embedded JSON ----------

    private record SeedSubTopic(string Code, string Name, string? Spec, string? Ref, string? Tier, string? Description);
    private record SeedTopic(string Code, string Name, string? Spec, int? Year, int? ExamWeight, string? Description, List<SeedSubTopic> SubTopics);
    private record SeedCurriculum(string Subject, string Board, string QualificationCode, string QualificationName, bool HasTiers,
        string? SpecUrl, string? ReferenceBook, string? ReferenceIsbn, List<SeedTopic> Topics);

    private async Task SeedCurriculumAsync(CancellationToken ct)
    {
        var asm = Assembly.GetExecutingAssembly();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.EndsWith(".json") && n.Contains("Seed.Curriculum")).OrderBy(n => n))
        {
            await using var stream = asm.GetManifestResourceStream(res)!;
            var data = await JsonSerializer.DeserializeAsync<SeedCurriculum>(stream, options, ct);
            if (data == null) continue;
            await UpsertCurriculumAsync(data, ct);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded curriculum {Board} {Qualification} ({Topics} topics)", data.Board, data.QualificationName, data.Topics.Count);
        }
    }

    private async Task UpsertCurriculumAsync(SeedCurriculum data, CancellationToken ct)
    {
        var boardId = DeterministicGuid.Create($"examboard:{data.Board}");
        var subjectId = DeterministicGuid.Create($"subject:{data.Subject}");
        var qualId = DeterministicGuid.Create($"qual:{data.Board}:{data.QualificationCode}");

        var qual = await _db.Qualifications.FirstOrDefaultAsync(q => q.Id == qualId, ct);
        if (qual == null)
        {
            qual = new Qualification { Id = qualId, ExamBoardId = boardId, SubjectId = subjectId, Code = data.QualificationCode };
            _db.Qualifications.Add(qual);
        }
        qual.Name = data.QualificationName; qual.HasTiers = data.HasTiers; qual.SpecificationUrl = data.SpecUrl;

        var topicOrder = 0;
        foreach (var t in data.Topics)
        {
            topicOrder++;
            var topicId = DeterministicGuid.Create($"topic:{data.Board}:{t.Code}");
            var topic = await _db.Topics.FirstOrDefaultAsync(x => x.Id == topicId, ct);
            if (topic == null)
            {
                topic = new Topic { Id = topicId, QualificationId = qualId, SubjectId = subjectId, Code = t.Code };
                _db.Topics.Add(topic);
            }
            topic.Name = t.Name; topic.SpecificationReference = t.Spec; topic.SortOrder = topicOrder;
            topic.ExamWeight = t.ExamWeight ?? 5; topic.Description = t.Description;
            topic.YearGroupId = t.Year.HasValue ? DeterministicGuid.Create($"year:{t.Year}") : null;

            var subOrder = 0;
            foreach (var s in t.SubTopics)
            {
                subOrder++;
                var subId = DeterministicGuid.Create($"subtopic:{data.Board}:{s.Code}");
                var sub = await _db.SubTopics.FirstOrDefaultAsync(x => x.Id == subId, ct);
                if (sub == null)
                {
                    sub = new SubTopic { Id = subId, TopicId = topicId, Code = s.Code };
                    _db.SubTopics.Add(sub);
                }
                sub.Name = s.Name; sub.SpecificationReference = s.Spec; sub.SortOrder = subOrder; sub.Description = s.Description;
                sub.Tier = Enum.TryParse<Tier>(s.Tier, true, out var tier) ? tier : Tier.NotApplicable;

                var mapId = DeterministicGuid.Create($"map:{data.Board}:{s.Code}");
                var map = await _db.CurriculumMappings.FirstOrDefaultAsync(x => x.Id == mapId, ct);
                if (map == null)
                {
                    map = new CurriculumMapping { Id = mapId, SubTopicId = subId, ExamBoardId = boardId };
                    _db.CurriculumMappings.Add(map);
                }
                map.SpecificationReference = s.Spec; map.ReferenceBookTitle = data.ReferenceBook;
                map.ReferenceBookIsbn = string.IsNullOrWhiteSpace(data.ReferenceIsbn) ? null : data.ReferenceIsbn;
                map.ReferenceBookSection = s.Ref;
            }
        }
    }

    // ---------- settings & admin ----------

    private async Task SeedSystemSettingsAsync(CancellationToken ct)
    {
        var defaults = new (string Key, string Value, string Desc, bool Public)[]
        {
            ("SpacedRepetition.IntervalsDays", "1,2,5,10,21,45,90", "Review intervals in days after a topic is learned.", false),
            ("Mastery.SecureThreshold", "0.70", "Mastery score at or above which a topic is 'Secure'.", true),
            ("Mastery.MasteredThreshold", "0.90", "Mastery score at or above which a topic is 'Mastered'.", true),
            ("Mastery.NeedsPracticeThreshold", "0.50", "Mastery score below which a topic 'Needs Practice'.", true),
            ("Session.AutoSaveSeconds", "45", "Recommended client auto-save interval.", true),
            ("Session.AbandonAfterHours", "48", "Paused sessions older than this are marked Abandoned.", false),
            ("Schedule.DefaultSessionsPerDay", "2", "Default study sessions per day for new students.", true),
            ("Schedule.DefaultSessionMinutes", "45", "Default session length for new students.", true),
            ("Lesson.DefaultPassThresholdPercent", "70", "Default pass mark to unlock the next lesson.", true),
            ("Marking.ShortAnswerKeywordMatchRatio", "0.6", "Fraction of mark-scheme keywords required for full marks on a criterion.", false),
            ("Reports.WeeklyReportDay", "Sunday", "Day on which weekly parent reports are generated.", false)
        };
        foreach (var (key, value, desc, pub) in defaults)
        {
            if (!await _db.SystemSettings.AnyAsync(s => s.Key == key, ct))
                _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = desc, IsPublic = pub });
        }
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Role == UserRole.Admin, ct)) return;
        _db.Users.Add(new User
        {
            Email = _app.AdminEmail,
            NormalizedEmail = _app.AdminEmail.ToUpperInvariant(),
            FirstName = "System",
            LastName = "Administrator",
            PasswordHash = _hasher.Hash(_app.AdminPassword),
            Role = UserRole.Admin,
            EmailConfirmed = true,
            MustChangePassword = true
        });
        _logger.LogWarning("Seeded default admin user {Email}. Change the password after first login.", _app.AdminEmail);
    }
}
