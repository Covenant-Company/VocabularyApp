import { inject, Injectable, InjectionToken } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { defer, finalize, map, Observable, shareReplay, Subject, takeUntil, tap, throwError } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { ApiContractError } from './api-error';
import { requireReviewDue, requireReviewReceipt, requireReviewStart, requireReviewSummary, reviewGuid, reviewOptionId } from './review-contract';
import { ReviewDueQuery, ReviewDueResponse, ReviewStartRequest, ReviewStartResponse, ReviewSubmitRequest, ReviewSubmitResponse, ReviewSummaryResponse } from '../models/review.model';

export const REVIEW_STORAGE = new InjectionToken<Storage | null>('Review session storage', {
  providedIn: 'root', factory: () => { try { return sessionStorage; } catch { return null; } }
});
export const REVIEW_UUID = new InjectionToken<() => string>('Review submission UUID', {
  providedIn: 'root', factory: () => () => crypto.randomUUID()
});
export class ReviewRecoveryError extends Error {}
export interface ReviewRecovery { sessionId: string | null; pending: ReviewSubmitRequest | null; }
const PREFIX = 'vocab_review_v1:';

@Injectable({ providedIn: 'root' })
export class ReviewService {
  private readonly storage = inject(REVIEW_STORAGE);
  private readonly uuid = inject(REVIEW_UUID);
  private readonly identityChanged = new Subject<void>();
  private owner: number | null = null;
  private state: ReviewRecovery = { sessionId: null, pending: null };
  private activeRequest: Observable<ReviewSubmitResponse> | null = null;
  private creating = false;

  constructor(private api: ApiService, private auth: AuthService) {
    // Survives route destruction: explicit logout/account changes revoke client recovery.
    this.auth.currentUser$.subscribe(user => {
      const nextOwner = user?.id ?? null;
      if (nextOwner === this.owner) {
        if (nextOwner === null) this.clearOtherOwners(null);
        return;
      }
      this.identityChanged.next(); this.activeRequest = null; this.creating = false;
      this.clearOtherOwners(nextOwner);
      this.owner = nextOwner;
      this.state = this.read();
    });
  }
  get recovery(): ReviewRecovery { return { ...this.state }; }
  get isSubmitting(): boolean { return this.activeRequest !== null; }

  due(query: ReviewDueQuery = {}): Observable<ReviewDueResponse> {
    const take = query.take ?? 20;
    const cursor = query.cursor == null ? '' : `&cursor=${encodeURIComponent(query.cursor)}`;
    return this.api.get<ReviewDueResponse>(`/reviews/due?take=${take}${cursor}`).pipe(map(requireReviewDue), takeUntil(this.identityChanged));
  }
  summary(): Observable<ReviewSummaryResponse> {
    return this.api.get<ReviewSummaryResponse>('/reviews/summary').pipe(map(requireReviewSummary), takeUntil(this.identityChanged));
  }
  start(questionCount = 10): Observable<ReviewStartResponse> {
    return defer(() => {
      if (this.state.pending || this.activeRequest || this.state.sessionId || this.creating)
        return throwError(() => new ReviewRecoveryError('Resume your saved review before starting another session.'));
      this.creating = true;
      const owner = this.owner;
      return this.api.post<ReviewStartResponse, ReviewStartRequest>('/reviews/start', { questionCount }).pipe(
        map(requireReviewStart), takeUntil(this.identityChanged),
        tap(session => {
          if (owner !== this.owner) throw new ReviewRecoveryError('Please sign in to continue.');
          if (session.availability === 'ready') this.persist({ sessionId: session.sessionId, pending: null });
        }), finalize(() => this.creating = false)
      );
    });
  }
  resume(): Observable<ReviewStartResponse> {
    if (this.state.pending || this.activeRequest) return throwError(() => new ReviewRecoveryError('Retry your pending answer before resuming.'));
    const sessionId = this.state.sessionId;
    if (!sessionId) return throwError(() => new ReviewRecoveryError('There is no saved review to resume.'));
    return this.api.post<ReviewStartResponse, ReviewStartRequest>('/reviews/start', { resumeSessionId: sessionId }).pipe(
      map(requireReviewStart), takeUntil(this.identityChanged),
      map(session => {
        if (session.availability !== 'ready' || session.sessionId !== sessionId) throw new ApiContractError();
        return session;
      })
    );
  }
  answer(sessionId: string, itemId: string, selectedOptionId: number): Observable<ReviewSubmitResponse> {
    if (!reviewGuid(sessionId) || !reviewGuid(itemId) || !reviewOptionId(selectedOptionId) || sessionId !== this.state.sessionId)
      return throwError(() => new ReviewRecoveryError('Please select an answer in the current review.'));
    const pending = this.state.pending;
    if (pending) {
      if (pending.sessionId !== sessionId || pending.itemId !== itemId || pending.selectedOptionId !== selectedOptionId)
        return throwError(() => new ReviewRecoveryError('Resolve your pending answer before selecting a different answer.'));
      return this.retryPending();
    }
    try {
      const submissionId = this.uuid();
      if (!reviewGuid(submissionId)) throw new Error();
      this.persist({ sessionId, pending: Object.freeze({ sessionId, itemId, submissionId, selectedOptionId }) });
    } catch {
      return throwError(() => new ReviewRecoveryError('Your answer could not be saved in this browser. Enable session storage and try again. No answer was sent.'));
    }
    return this.retryPending();
  }
  retryPending(): Observable<ReviewSubmitResponse> {
    if (this.activeRequest) return this.activeRequest;
    const request = this.state.pending;
    if (!request) return throwError(() => new ReviewRecoveryError('There is no pending answer to retry.'));
    const owner = this.owner;
    const response: Observable<ReviewSubmitResponse> = this.api.post<ReviewSubmitResponse, ReviewSubmitRequest>('/reviews/submit', request).pipe(
      map(requireReviewReceipt),
      map(receipt => {
        if (receipt.sessionId !== request.sessionId || receipt.itemId !== request.itemId || receipt.submissionId !== request.submissionId ||
            receipt.selectedOptionId !== request.selectedOptionId) throw new ApiContractError();
        return receipt;
      }),
      takeUntil(this.identityChanged),
      tap({
        next: receipt => {
          if (owner === this.owner) {
            this.clearPending();
            this.activeRequest = null;
            if (receipt.sessionSummary.isComplete) {
              this.state = { sessionId: null, pending: null };
              try { this.storage?.removeItem(PREFIX + this.owner); } catch { /* A retained accepted payload is safe to replay. */ }
            }
          }
        },
        // A definitive rejection cannot have accepted this exact immutable payload.
        // 401/503/network/500/malformed-200 remain unresolved and preserve it.
        error: error => { if (owner === this.owner && error instanceof HttpErrorResponse && [400, 404, 409].includes(error.status)) this.clearPending(); }
      }),
      finalize(() => { if (owner === this.owner && this.activeRequest === response) this.activeRequest = null; }),
      // Route changes may unsubscribe the view, but must not discard an in-flight receipt.
      shareReplay({ bufferSize: 1, refCount: false })
    );
    this.activeRequest = response;
    return response;
  }
  finish(sessionId: string): void {
    if (this.state.sessionId === sessionId && !this.state.pending && !this.activeRequest) this.discardSession();
  }
  discardSession(): void {
    if (this.state.pending || this.activeRequest) throw new ReviewRecoveryError('Retry your pending answer before starting another session.');
    this.state = { sessionId: null, pending: null };
    try { if (this.owner !== null) this.storage?.removeItem(PREFIX + this.owner); } catch { /* Stale recovery can safely resume/replay. */ }
  }
  private clearPending(): void {
    const state = { sessionId: this.state.sessionId, pending: null };
    // Receipt is authoritative even if storage becomes unavailable. An old stored payload
    // is safe to replay on refresh, and no new answer is sent without a successful write.
    this.state = state;
    try { this.persist(state); } catch { /* Preserve accepted result in memory. */ }
  }
  private persist(state: ReviewRecovery): void {
    if (!this.storage || this.owner === null) throw new ReviewRecoveryError('Browser session storage is unavailable.');
    this.storage.setItem(PREFIX + this.owner, JSON.stringify(state));
    this.state = state;
  }
  private read(): ReviewRecovery {
    const empty: ReviewRecovery = { sessionId: null, pending: null };
    if (this.owner === null || !this.storage) return empty;
    try {
      const raw = this.storage.getItem(PREFIX + this.owner);
      if (!raw) return empty;
      const value = JSON.parse(raw) as ReviewRecovery;
      if (!value || !reviewGuid(value.sessionId) || !Object.hasOwn(value, 'pending') || Object.keys(value).some(k => !['sessionId', 'pending'].includes(k))) throw new Error();
      const p = value.pending;
      if (p !== null && (!p || Object.keys(p).length !== 4 || !reviewGuid(p.sessionId) || !reviewGuid(p.itemId) || !reviewGuid(p.submissionId) ||
          !reviewOptionId(p.selectedOptionId) || p.sessionId !== value.sessionId)) throw new Error();
      return { sessionId: value.sessionId, pending: p === null ? null : Object.freeze({ ...p }) };
    } catch { try { this.storage.removeItem(PREFIX + this.owner); } catch { /* Storage is optional until an answer is sent. */ } return empty; }
  }
  private clearOtherOwners(keep: number | null): void {
    try {
      if (!this.storage) return;
      for (let i = this.storage.length - 1; i >= 0; i--) {
        const key = this.storage.key(i);
        if (key?.startsWith(PREFIX) && key !== PREFIX + keep) this.storage.removeItem(key);
      }
    } catch { /* Scoped keys are never read for another owner. */ }
  }
}
