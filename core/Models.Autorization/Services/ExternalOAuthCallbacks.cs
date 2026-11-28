namespace Models.Autorization.Services;

/// <summary>
/// Server-owned provider configuration, not request data. Do not log or serialize.
/// </summary>
public sealed class ExternalCallbackConfiguration(string provider, string clientId, Uri redirectUri, string? clientSecret = null)
{
    public string Provider { get; } = provider;
    public string ClientId { get; } = clientId;
    public Uri RedirectUri { get; } = redirectUri;
    internal string? ClientSecret { get; } = clientSecret;
}

/// <summary>
/// Orchestrates an OAuth linking callback after browser correlation. This is an application
/// service, not a public HTTP action. It stages a verified candidate only; no automatic linking,
/// account creation, token issuance or provider credentials in the result.
/// </summary>
public sealed class ExternalOAuthCallbacks(ExternalOAuthFlows flows, TelegramOAuthExchange telegram, VkIdentityProof vk)
{
    public async Task<bool> Complete(ExternalCallbackConfiguration configured, string state, string browserBinding,
        string authorizationCode, string? deviceId = null, CancellationToken ct = default)
    {
        if (configured.Provider is not ("telegram" or "vk") ||
            !TelegramIdentityProof.PositiveId(configured.ClientId) ||
            configured.RedirectUri is not { IsAbsoluteUri: true } ||
            configured.RedirectUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(authorizationCode) || authorizationCode.Length > 4096 ||
            authorizationCode.Any(char.IsControl)) return false;
        // Consume server-owned state first, even if provider proof is subsequently rejected.
        var claim = await flows.Claim(state, browserBinding, configured.Provider, ct);
        if (claim is null || claim.ClientId != configured.ClientId ||
            !string.Equals(claim.RedirectUri.AbsoluteUri, configured.RedirectUri.AbsoluteUri, StringComparison.Ordinal))
            return false;
        ExternalIdentityKey? verified = configured.Provider == "telegram"
            ? await telegram.Exchange(claim, configured.ClientSecret ?? "", authorizationCode, ct)
            : await vk.Exchange(claim.ClientId, claim.RedirectUri, state, state,
                claim.Verifier, authorizationCode, deviceId ?? "", ct);
        return verified is not null && await flows.StageVerifiedProof(claim, verified, ct);
    }
}
