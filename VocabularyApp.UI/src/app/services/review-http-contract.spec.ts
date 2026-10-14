import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AuthService } from './auth.service';
import { ReviewService, REVIEW_STORAGE, REVIEW_UUID } from './review.service';
import { requireReviewDue, requireReviewStart, requireReviewSummary } from './review-contract';
import { normalizeApiError } from './api-error';
import { ApiResponse } from '../models/user.model';
import { ReviewDueResponse, ReviewStartResponse, ReviewSubmitRequest, ReviewSummaryResponse } from '../models/review.model';
import { ReviewTestStorage } from '../testing/review-storage';
import { phase5HttpEvidence } from '../testing/phase5-http.evidence';
import { environment } from '../../environments/environment';

// These bytes originate from real WAF HTTP and its real Swagger document.
// Transport here is mocked: this verifies serialized compatibility, not live browser E2E.
interface Evidence {
  responses: Record<string, { status: number; contentType: string | null; body: object | null }>;
  submission: ReviewSubmitRequest;
  schemas: Record<string, { properties?: Record<string, unknown>; required?: string[] }>;
  paths: Record<string, Record<string, { parameters?: { name: string; schema: { default?: unknown } }[] }>>;
}
const evidence = phase5HttpEvidence as unknown as Evidence;

describe('Review contracts captured from real ASP.NET HTTP', () => {
  let service: ReviewService; let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: { currentUser$: of({ id: 7 }), getToken: () => 'isolated-contract-token' } },
      { provide: REVIEW_STORAGE, useValue: new ReviewTestStorage() },
      { provide: REVIEW_UUID, useValue: () => evidence.submission.submissionId }] });
    service = TestBed.inject(ReviewService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('accepts actual summary and due response bodies including protected cursor and nullable fields', () => {
    service.summary().subscribe(summary => expect(summary.totalEnrolledCount).toBe(4));
    http.expectOne(environment.apiUrl + '/reviews/summary').flush(evidence.responses['summary'].body);
    service.due({ take: 2 }).subscribe(page => {
      expect(page.items.length).toBe(2); expect(page.nextCursor).not.toBeNull();
      service.due({ take: 20, cursor: page.nextCursor }).subscribe();
      http.expectOne(environment.apiUrl + '/reviews/due?take=20&cursor=' + encodeURIComponent(page.nextCursor!)).flush(evidence.responses['due'].body);
    });
    http.expectOne(environment.apiUrl + '/reviews/due?take=2').flush(evidence.responses['due'].body);
    expect(requireReviewDue(evidence.responses['blockedDue'].body as ApiResponse<ReviewDueResponse>).items[0].isUsable).toBeFalse();
    expect(requireReviewSummary(evidence.responses['summary'].body as ApiResponse<ReviewSummaryResponse>).newCount).toBe(4);
  });
  it('sends the actual accepted option-zero payload, validates its receipt and resumes immutable snapshots', () => {
    service.start(2).subscribe(); const start = http.expectOne(environment.apiUrl + '/reviews/start');
    expect(start.request.body).toEqual({ questionCount: 2 }); start.flush(evidence.responses['ready'].body);
    const payload = evidence.submission;
    service.answer(payload.sessionId, payload.itemId, 0).subscribe(receipt => {
      expect(receipt.selectedOptionId).toBe(0); expect(receipt.submissionId).toBe(payload.submissionId);
    });
    const submit = http.expectOne(environment.apiUrl + '/reviews/submit'); expect(submit.request.body).toEqual(payload); submit.flush(evidence.responses['receipt'].body);
    service.resume().subscribe(session => {
      const original = requireReviewStart(evidence.responses['ready'].body as ApiResponse<ReviewStartResponse>);
      expect(session.items.map(i => i.options)).toEqual(original.items.map(i => i.options)); expect(session.items[0].acceptedReceipt).not.toBeNull();
    });
    const resume = http.expectOne(environment.apiUrl + '/reviews/start'); expect(resume.request.body).toEqual({ resumeSessionId: payload.sessionId }); resume.flush(evidence.responses['resume'].body);
    expect(evidence.responses['replay'].body).toEqual(evidence.responses['receipt'].body);
  });
  for (const name of ['empty', 'insufficient', 'blocked', 'ambiguous', 'completed']) {
    it(`accepts actual ${name} availability/session fields`, () => {
      expect(() => requireReviewStart(evidence.responses[name].body as ApiResponse<ReviewStartResponse>)).not.toThrow();
      const result = requireReviewStart(evidence.responses[name].body as ApiResponse<ReviewStartResponse>);
      if (name === 'completed') expect(result.sessionSummary!.isComplete).toBeTrue();
      else { expect(result.sessionId).toBeNull(); expect(result.items).toEqual([]); }
    });
  }
  for (const [name, code] of [['validation', 'validation_failed'], ['missing', 'review_session_unavailable'], ['stateChanged', 'review_state_changed'],
    ['conflict', 'review_submission_conflict'], ['expired', 'review_session_expired'], ['disabled', 'reviews_disabled'], ['internal', undefined]] as const) {
    it(`normalizes the actual HTTP ${name} error without diagnostics`, () => {
      const captured = evidence.responses[name];
      const error = normalizeApiError(new HttpErrorResponse({ status: captured.status, error: captured.body }), 'review-submit', 'Please retry.');
      expect(error.code).toBe(code); expect(error.message).not.toContain('Exception'); expect(error.message).not.toContain('SQL');
      expect(captured.contentType).toBe(name === 'validation' ? 'application/problem+json' : 'application/json');
    });
  }
  it('handles the real bodyless authentication challenge', () => {
    const response = evidence.responses['unauthorized']; expect(response.status).toBe(401); expect(response.body).toBeNull();
    expect(normalizeApiError(new HttpErrorResponse({ status: response.status, error: response.body }), 'review-summary', 'Retry').message).toBe('Please sign in to continue.');
  });
  it('matches submission keys, optional start members and due defaults to the real OpenAPI schemas', () => {
    const submit = evidence.schemas['ReviewSubmitRequestDto'];
    expect(Object.keys(evidence.submission).sort()).toEqual(Object.keys(submit.properties!).sort());
    expect(submit.required!.slice().sort()).toEqual(Object.keys(evidence.submission).sort());
    const start = evidence.schemas['ReviewStartRequestDto']; expect(start.required ?? []).toEqual([]);
    expect(Object.keys(start.properties!).sort()).toEqual(['questionCount', 'resumeSessionId']);
    const take = evidence.paths['/api/reviews/due']['get'].parameters!.find(p => p.name.toLowerCase() === 'take')!;
    expect(take.schema.default).toBe(20);
    const ready = requireReviewStart(evidence.responses['ready'].body as ApiResponse<ReviewStartResponse>);
    expect(Object.keys(ready.items[0]).sort()).toEqual(Object.keys(evidence.schemas['ReviewItemDto'].properties!).sort());
    expect(Object.keys(ready.items[0].options[0]).sort()).toEqual(['optionId', 'text']);
    expect(Object.keys(requireReviewDue(evidence.responses['due'].body as ApiResponse<ReviewDueResponse>).items[0]).sort()).toEqual(Object.keys(evidence.schemas['ReviewDueItemDto'].properties!).sort());
  });
});
