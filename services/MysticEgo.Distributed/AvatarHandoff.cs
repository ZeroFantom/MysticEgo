using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Distributed;

public sealed record AvatarHandoffRequest(string Motion, double Horizontal);
public sealed record AvatarHandoff(Guid Id, string Motion, double Horizontal, string Status, DateTimeOffset ExpiresAt);
public sealed record AvatarHandoffClaim(AvatarHandoff Request, string Proof);
public sealed record AvatarHandoffCompletion(string Proof, bool Started);

// One short-lived, owner-bound request per local node. No arbitrary native commands.
public sealed class AvatarHandoffQueue
{
    private readonly object gate = new();
    private AvatarHandoff? current;
    private string? owner, proof;
    private void Expire()
    {
        if (current is { } c && c.ExpiresAt <= DateTimeOffset.UtcNow)
        { current = null; owner = null; proof = null; }
    }
    public AvatarHandoff? Request(string principal, AvatarHandoffRequest request)
    {
        if (request.Motion is not ("walk" or "perch") || !double.IsFinite(request.Horizontal) ||
            request.Horizontal < 0 || request.Horizontal > 1) return null;
        lock (gate)
        {
            Expire();
            if (current is { Status: "pending" or "claimed" }) return null;
            owner = principal;
            proof = null;
            return current = new(Guid.NewGuid(), request.Motion, request.Horizontal, "pending", DateTimeOffset.UtcNow.AddSeconds(60));
        }
    }
    public AvatarHandoff? Status(string principal, Guid id)
    {
        lock (gate) { Expire(); return owner == principal && current?.Id == id ? current : null; }
    }
    public AvatarHandoffClaim? Claim(string principal)
    {
        lock (gate)
        {
            Expire();
            if (owner != principal || current is not { Status: "pending" } c) return null;
            current = c with { Status = "claimed" };
            proof = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            return new(current, proof);
        }
    }
    public bool Complete(string principal, Guid id, AvatarHandoffCompletion result)
    {
        lock (gate)
        {
            Expire();
            if (owner != principal || current is not { Status: "claimed" } c || c.Id != id ||
                string.IsNullOrEmpty(proof) || !string.Equals(proof, result.Proof, StringComparison.Ordinal)) return false;
            current = c with { Status = result.Started ? "started" : "declined", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2) };
            proof = null;
            return true;
        }
    }
}

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
