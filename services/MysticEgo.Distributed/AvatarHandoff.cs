using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Distributed;

[ApiController, Authorize]
[Route("api/v1/managed/avatar-handoff")]
public sealed class AvatarHandoffController(NodeOptions node, UiAutomationBridge bridge,
    IAssistantAccessPolicy assistant, AvatarHandoffQueue queue) : ControllerBase
{
    private string? Principal(bool desktopOnly = false)
    {
        if (node.Kind is not (NodeKind.Agent or NodeKind.AuditOrchestra) ||
            User.Identity?.IsAuthenticated != true || User.FindFirst("token_use")?.Value == "device" ||
            HttpContext.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
            Request.Headers.ContainsKey("Forwarded") || Request.Headers.ContainsKey("X-Forwarded-For")) return null;
        var resolved = assistant.ResolveOwner(User);
        if (string.IsNullOrWhiteSpace(bridge.Owner) || !string.Equals(resolved, bridge.Owner, StringComparison.Ordinal)) return null;
        if (Request.Headers.TryGetValue("Origin", out var origin))
        {
            if (desktopOnly || !Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var u) ||
                !u.IsLoopback || u.Scheme != Request.Scheme || !string.Equals(u.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return resolved;
    }

    [HttpPost("request"), RequestSizeLimit(4096)]
    public IActionResult RequestHandoff(AvatarHandoffRequest request)
    {
        var owner = Principal(); if (owner is null) return Forbid();
        var item = queue.Request(owner, request);
        return item is null ? Conflict() : Accepted(item);
    }
    [HttpGet("{id:guid}")]
    public IActionResult Status(Guid id)
    {
        var owner = Principal(); if (owner is null) return Forbid();
        var item = queue.Status(owner, id); return item is null ? NotFound() : Ok(item);
    }
    [HttpPost("claim")]
    public IActionResult Claim()
    {
        var owner = Principal(true); if (owner is null) return Forbid();
        var item = queue.Claim(owner); return item is null ? NoContent() : Ok(item);
    }
    [HttpPost("{id:guid}/complete"), RequestSizeLimit(4096)]
    public IActionResult Complete(Guid id, AvatarHandoffCompletion result)
    {
        var owner = Principal(true); if (owner is null) return Forbid();
        return queue.Complete(owner, id, result) ? NoContent() : Conflict();
    }
}
