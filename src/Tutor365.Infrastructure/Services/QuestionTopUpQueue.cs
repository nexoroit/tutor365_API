using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tutor365.Application.Interfaces;

namespace Tutor365.Infrastructure.Services;

/// <summary>In-process queue of (lesson, student) pairs that need fresh question variants. Duplicates within a short window are dropped.</summary>
public class QuestionTopUpQueue : IQuestionTopUpQueue
{
    private readonly Channel<(Guid LessonId, Guid StudentId)> _channel = Channel.CreateBounded<(Guid, Guid)>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Dictionary<(Guid, Guid), DateTime> _recent = new();
    private readonly object _gate = new();

    public void Enqueue(Guid lessonId, Guid studentId)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            foreach (var k in _recent.Where(kv => kv.Value < now.AddMinutes(-30)).Select(kv => kv.Key).ToList()) _recent.Remove(k);
            if (_recent.ContainsKey((lessonId, studentId))) return;
            _recent[(lessonId, studentId)] = now;
        }
        _channel.Writer.TryWrite((lessonId, studentId));
    }

    public ChannelReader<(Guid LessonId, Guid StudentId)> Reader => _channel.Reader;
}

/// <summary>Drains the top-up queue and runs the generator for each pair. One job at a time keeps AI usage predictable.</summary>
public class QuestionTopUpHostedService : BackgroundService
{
    private readonly QuestionTopUpQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<QuestionTopUpHostedService> _logger;
    public QuestionTopUpHostedService(QuestionTopUpQueue queue, IServiceScopeFactory scopes, ILogger<QuestionTopUpHostedService> logger) { _queue = queue; _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (lessonId, studentId) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var generator = scope.ServiceProvider.GetRequiredService<IQuestionGenerator>();
                var made = await generator.GenerateVariantsAsync(lessonId, studentId, 1, stoppingToken);
                if (made > 0) _logger.LogInformation("Question top-up: {Count} new variants for lesson {LessonId} (student {StudentId})", made, lessonId, studentId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Question top-up failed for lesson {LessonId}", lessonId); }
        }
    }
}
