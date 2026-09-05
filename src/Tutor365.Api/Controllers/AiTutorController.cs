using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Services;

namespace Tutor365.Api.Controllers;

/// <summary>Server-side AI tutor. All provider calls happen here; no keys reach the client. Currently backed by a stub provider.</summary>
[Route("api/v{version:apiVersion}/ai-tutor")]
public class AiTutorController : ApiControllerBase
{
    private readonly IAiTutorService _ai;
    public AiTutorController(IAiTutorService ai) => _ai = ai;

    /// <summary>Free-form message to the tutor within a session/question context.</summary>
    [HttpPost("message"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<AiTutorResponse>), 200)]
    public async Task<IActionResult> Message([FromBody] AiTutorRequest request, CancellationToken ct) => Ok(await _ai.AskAsync(request with { Intent = request.Intent ?? "message" }, ct));

    [HttpPost("hint"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<AiTutorResponse>), 200)]
    public async Task<IActionResult> Hint([FromBody] AiTutorRequest request, CancellationToken ct) => Ok(await _ai.AskAsync(request with { Intent = "hint" }, ct));

    [HttpPost("explain"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<AiTutorResponse>), 200)]
    public async Task<IActionResult> Explain([FromBody] AiTutorRequest request, CancellationToken ct) => Ok(await _ai.AskAsync(request with { Intent = "explain" }, ct));

    [HttpPost("example"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<AiTutorResponse>), 200)]
    public async Task<IActionResult> Example([FromBody] AiTutorRequest request, CancellationToken ct) => Ok(await _ai.AskAsync(request with { Intent = "example" }, ct));

    /// <summary>"Why is my answer wrong?" for the last answer to the given question.</summary>
    [HttpPost("why-wrong"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<AiTutorResponse>), 200)]
    public async Task<IActionResult> WhyWrong([FromBody] AiTutorRequest request, CancellationToken ct) => Ok(await _ai.AskAsync(request with { Intent = "why-wrong" }, ct));

    /// <summary>Alias kept for the documented contract (POST /ai-tutor/question): ask about the current question.</summary>
    [HttpPost("question"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<AiTutorResponse>), 200)]
    public async Task<IActionResult> Question([FromBody] AiTutorRequest request, CancellationToken ct) => Ok(await _ai.AskAsync(request with { Intent = request.Intent ?? "message" }, ct));

    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AiConversationDto>), 200)]
    public async Task<IActionResult> Conversation(Guid id, CancellationToken ct) => Ok(await _ai.GetConversationAsync(id, ct));

    /// <summary>Conversation history for a student (student self, linked parent, or admin).</summary>
    [HttpGet("students/{studentId:guid}/conversations")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AiConversationDto>>), 200)]
    public async Task<IActionResult> Conversations(Guid studentId, [FromQuery] PagingQuery paging, CancellationToken ct) => Ok(await _ai.GetConversationsAsync(studentId, paging, ct));
}
