// Local consent lease. No external request can extend the lease.
export class ConsentLease {
  #expiresAt;
  #revoked = false;
  constructor(durationMs, now = Date.now) {
    if (!Number.isSafeInteger(durationMs) || durationMs < 1 || durationMs > 30 * 60 * 1000) throw new Error('Invalid lease duration');
    this.now = now;
    this.#expiresAt = now() + durationMs;
  }
  get active() { return !this.#revoked && this.now() < this.#expiresAt; }
  revoke() { this.#revoked = true; }
  assertActive() { if (!this.active) throw new Error('Local consent expired or revoked'); }
  status() { return { authorized: this.active, expiresAt: new Date(this.#expiresAt).toISOString(), revoked: this.#revoked }; }
}
