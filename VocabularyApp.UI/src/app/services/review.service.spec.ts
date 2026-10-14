import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject, Observable } from 'rxjs';
import { AuthService } from './auth.service';
import { ReviewRecoveryError, ReviewService, REVIEW_STORAGE, REVIEW_UUID } from './review.service';
import { ApiContractError, normalizeApiError } from './api-error';
import { User } from '../models/user.model';
import { currentReviewSuccess, reviewApplicationFailure } from '../testing/api-contract.fixtures';
import { ReviewTestStorage } from '../testing/review-storage';
import { environment } from '../../environments/environment';

describe('ReviewService', () => {
  let service: ReviewService; let http: HttpTestingController; let storage: ReviewTestStorage;
  let users: BehaviorSubject<User | null>; let uuid: jasmine.Spy;
  const user: User = { id: 7, username: 'test', email: 'test@example.test', createdAt: '2026-01-02T03:04:05Z', lastLoginAt: null };
  const key = 'vocab_review_v1:7';
  beforeEach(() => {
    storage = new ReviewTestStorage(); users = new BehaviorSubject<User | null>(user);
    uuid = jasmine.createSpy('uuid').and.returnValue(currentReviewSuccess().submit.data.submissionId);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: { currentUser$: users.asObservable(), getToken: () => 'review-bearer' } },
      { provide: REVIEW_STORAGE, useValue: storage }, { provide: REVIEW_UUID, useValue: uuid }] });
    service = TestBed.inject(ReviewService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function start(): void { service.start().subscribe(); http.expectOne(environment.apiUrl + '/reviews/start').flush(currentReviewSuccess().start); }

  it('uses the existing JWT transport for summary and encodes opaque due cursors', () => {
    let summary: unknown; let due: unknown;
    service.summary().subscribe(v => summary = v); service.due({ take: 10, cursor: 'opaque+/=&?' }).subscribe(v => due = v);
    const summaryRequest = http.expectOne(environment.apiUrl + '/reviews/summary');
    expect(summaryRequest.request.headers.get('Authorization')).toBe('Bearer review-bearer'); summaryRequest.flush(currentReviewSuccess().summary);
    const dueRequest = http.expectOne(environment.apiUrl + '/reviews/due?take=10&cursor=opaque%2B%2F%3D%26%3F'); dueRequest.flush(currentReviewSuccess().due);
    expect(summary).toEqual(currentReviewSuccess().summary.data); expect(due).toEqual(currentReviewSuccess().due.data);
  });
  it('starts with exact request and persists only minimal recovery metadata', () => {
    service.start(2).subscribe(); const request = http.expectOne(environment.apiUrl + '/reviews/start');
    expect(request.request.body).toEqual({ questionCount: 2 }); request.flush(currentReviewSuccess().start);
    expect(JSON.parse(storage.getItem(key)!)).toEqual({ sessionId: currentReviewSuccess().start.data.sessionId, pending: null });
    expect(storage.getItem(key)).not.toContain('A trial'); expect(storage.getItem(key)).not.toContain('bearer');
    service.resume().subscribe(); const resume = http.expectOne(environment.apiUrl + '/reviews/start');
    expect(resume.request.body).toEqual({ resumeSessionId: currentReviewSuccess().start.data.sessionId }); resume.flush(currentReviewSuccess().start);
  });
  it('persists option zero and a single UUID before HTTP and coalesces repeated sends', () => {
    start(); const f = currentReviewSuccess(); let receipt: unknown;
    service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe(v => receipt = v);
    const pending = JSON.parse(storage.getItem(key)!).pending; expect(pending.selectedOptionId).toBe(0); expect(uuid).toHaveBeenCalledTimes(1);
    service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe();
    const request = http.expectOne(environment.apiUrl + '/reviews/submit'); expect(request.request.body).toEqual(pending);
    request.flush(f.submit); expect(receipt).toEqual(f.submit.data); expect(service.recovery.pending).toBeNull();
    expect(JSON.parse(storage.getItem(key)!).pending).toBeNull();
  });
  for (const failure of ['network', 'server', 'malformed', 'wrong-receipt', 'disabled', 'unauthorized']) {
    it(`retains the exact request after ${failure} and reuses it on retry`, () => {
      start(); const f = currentReviewSuccess(); const error = jasmine.createSpy('error');
      service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe({ error });
      const request = http.expectOne(environment.apiUrl + '/reviews/submit'); const pending = request.request.body;
      if (failure === 'network') request.error(new ProgressEvent('error'));
      else if (failure === 'malformed') request.flush({ success: true, data: {} });
      else if (failure === 'wrong-receipt') request.flush({ ...f.submit, data: { ...f.submit.data, submissionId: '40000000-0000-0000-0000-000000000001' } });
      else request.flush(reviewApplicationFailure(failure === 'disabled' ? 'reviews_disabled' : failure === 'unauthorized' ? 'invalid_token' : 'internal_error'),
        { status: failure === 'disabled' ? 503 : failure === 'unauthorized' ? 401 : 500, statusText: 'Failure' });
      expect(error).toHaveBeenCalled(); expect(service.recovery.pending).toEqual(pending); expect(JSON.parse(storage.getItem(key)!).pending).toEqual(pending);
      service.retryPending().subscribe(); const replay = http.expectOne(environment.apiUrl + '/reviews/submit'); expect(replay.request.body).toEqual(pending);
      replay.flush(f.submit); expect(uuid).toHaveBeenCalledTimes(1);
    });
  }
  it('blocks a different answer, new session and resume while an outcome is uncertain', () => {
    start(); const f = currentReviewSuccess();
    service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe({ error: () => {} });
    http.expectOne(environment.apiUrl + '/reviews/submit').error(new ProgressEvent('error'));
    const operations: Observable<unknown>[] = [service.start(), service.resume(), service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 1)];
    for (const operation of operations) {
      operation.subscribe({ next: () => fail('Unsafe recovery'), error: e => expect(e instanceof ReviewRecoveryError).toBeTrue() });
    }
    expect(() => service.discardSession()).toThrowError(ReviewRecoveryError); http.expectNone(environment.apiUrl + '/reviews/start');
  });
  for (const status of [400, 404, 409]) {
    it(`clears the pending payload only after definitive HTTP ${status} rejection`, () => {
      start(); const f = currentReviewSuccess();
      service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe({ error: () => {} });
      http.expectOne(environment.apiUrl + '/reviews/submit').flush(reviewApplicationFailure('review_state_changed'), { status, statusText: 'Failure' });
      expect(service.recovery.pending).toBeNull(); expect(service.recovery.sessionId).toBe(f.start.data.sessionId);
    });
  }
  it('sends no answer if pending storage cannot be written', () => {
    start(); spyOn(storage, 'setItem').and.throwError('QuotaExceededError SECRET'); const f = currentReviewSuccess();
    service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe({ error: e => expect(e.message).not.toContain('SECRET') });
    http.expectNone(environment.apiUrl + '/reviews/submit');
  });
  it('retains accepted feedback even if clearing storage fails', () => {
    start(); const f = currentReviewSuccess(); let accepted = false;
    service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe(() => accepted = true);
    spyOn(storage, 'setItem').and.throwError('Storage denied'); http.expectOne(environment.apiUrl + '/reviews/submit').flush(f.submit);
    expect(accepted).toBeTrue(); expect(service.recovery.pending).toBeNull(); expect(JSON.parse(storage.getItem(key)!).pending).not.toBeNull();
  });
  it('continues the receipt subscription across route unsubscribe', () => {
    start(); const f = currentReviewSuccess(); const subscription = service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe();
    const request = http.expectOne(environment.apiUrl + '/reviews/submit'); subscription.unsubscribe(); expect(request.cancelled).toBeFalse();
    request.flush(f.submit); expect(service.recovery.pending).toBeNull();
  });
  it('clears review recovery on logout/account change without touching unrelated storage', () => {
    start(); storage.setItem('unrelated', 'keep'); users.next({ ...user, id: 8 });
    expect(storage.getItem(key)).toBeNull(); expect(service.recovery.sessionId).toBeNull(); expect(storage.getItem('unrelated')).toBe('keep');
    users.next(null); expect(service.recovery.pending).toBeNull();
  });
  it('rejects malformed and cross-account recovery metadata', () => {
    storage.setItem('vocab_review_v1:8', JSON.stringify({ sessionId: 'bad', pending: { selectedOptionId: 0 } }));
    users.next({ ...user, id: 8 }); expect(service.recovery.sessionId).toBeNull(); expect(storage.getItem('vocab_review_v1:8')).toBeNull();
  });
  it('removes recovery metadata when the accepted receipt completes a session', () => {
    start(); const f = currentReviewSuccess();
    service.answer(f.start.data.sessionId!, f.start.data.items[0].itemId, 0).subscribe();
    http.expectOne(environment.apiUrl + '/reviews/submit').flush({ ...f.submit, data: { ...f.submit.data,
      sessionSummary: { totalItems: 2, acceptedCount: 2, correctCount: 2, incorrectCount: 0, pendingCount: 0, isComplete: true } } });
    expect(storage.getItem(key)).toBeNull();
  });
  it('rejects missing options, duplicate options, invalid dates and counts in success bodies', () => {
    for (const edit of [(f: ReturnType<typeof currentReviewSuccess>) => f.start.data.items[0].options.pop(),
      (f: ReturnType<typeof currentReviewSuccess>) => f.start.data.items[0].options[0].optionId = 1,
      (f: ReturnType<typeof currentReviewSuccess>) => f.start.data.expiresAtUtc = '2026-10-13T12:30:00',
      (f: ReturnType<typeof currentReviewSuccess>) => f.start.data.sessionSummary!.acceptedCount = 1]) {
      const f = currentReviewSuccess(); edit(f);
      service.start().subscribe({ next: () => fail('Malformed session accepted'), error: e => expect(e instanceof ApiContractError).toBeTrue() });
      http.expectOne(environment.apiUrl + '/reviews/start').flush(f.start);
    }
    expect(service.recovery.sessionId).toBeNull();
  });
  it('uses safe review error normalization for all required status families', () => {
    for (const [status, code] of [[400, 'invalid_review_answer'], [401, 'invalid_token'], [404, 'review_session_unavailable'], [409, 'review_state_changed'], [503, 'reviews_disabled']] as const) {
      const error = new HttpErrorResponse({ status, error: reviewApplicationFailure(code) });
      const normalized = normalizeApiError(error, 'review-submit', 'Please retry.'); expect(normalized.code).toBe(code); expect(normalized.message).not.toContain('SECRET');
    }
  });
  it('cancels old-account summary and due requests before a different user can receive them', () => {
    const received = jasmine.createSpy('received'); service.summary().subscribe(received); service.due().subscribe(received);
    const summary = http.expectOne(environment.apiUrl + '/reviews/summary'); const due = http.expectOne(environment.apiUrl + '/reviews/due?take=20');
    users.next({ ...user, id: 8 }); expect(summary.cancelled).toBeTrue(); expect(due.cancelled).toBeTrue(); expect(received).not.toHaveBeenCalled();
  });
});
