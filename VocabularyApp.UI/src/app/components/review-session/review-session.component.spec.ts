import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { ReviewSessionComponent } from './review-session.component';
import { AuthService } from '../../services/auth.service';
import { REVIEW_STORAGE, REVIEW_UUID, ReviewService } from '../../services/review.service';
import { currentReviewSuccess, reviewApplicationFailure } from '../../testing/api-contract.fixtures';
import { ReviewTestStorage } from '../../testing/review-storage';
import { environment } from '../../../environments/environment';
import { User } from '../../models/user.model';
import { ReviewSubmitResponse } from '../../models/review.model';
import { routes } from '../../app.routes';
import { authGuard } from '../../guards/auth.guard';

describe('ReviewSessionComponent', () => {
  let fixture: ComponentFixture<ReviewSessionComponent>; let component: ReviewSessionComponent;
  let http: HttpTestingController; let storage: ReviewTestStorage; let users: BehaviorSubject<User | null>;
  const base = environment.apiUrl; const key = 'vocab_review_v1:7';
  beforeEach(async () => {
    storage = new ReviewTestStorage(); users = new BehaviorSubject<User | null>({ id: 7, username: 'reviewer', email: 'review@example.test', createdAt: '2026-01-02T03:04:05Z', lastLoginAt: null });
    await TestBed.configureTestingModule({ imports: [ReviewSessionComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: AuthService, useValue: { currentUser$: users.asObservable(), getToken: () => 'review-token', isAuthenticated: () => true } },
      { provide: REVIEW_STORAGE, useValue: storage }, { provide: REVIEW_UUID, useValue: () => currentReviewSuccess().submit.data.submissionId }] }).compileComponents();
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(ReviewSessionComponent); component = fixture.componentInstance; http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); fixture.destroy(); });
  function flushOverview(): void {
    for (const request of http.match(base + '/reviews/summary')) request.flush(currentReviewSuccess().summary);
    for (const request of http.match(base + '/reviews/due?take=20')) request.flush(currentReviewSuccess().due);
  }
  function initialize(): void { fixture.detectChanges(); flushOverview(); fixture.detectChanges(); }
  function start(): void {
    initialize(); component.startSession(); http.expectOne(base + '/reviews/start').flush(currentReviewSuccess().start); fixture.detectChanges();
  }
  function flushAccepted(data = currentReviewSuccess().submit.data): void {
    http.expectOne(base + '/reviews/submit').flush({ success: true, data }); fixture.detectChanges();
  }
  it('registers the review route with the existing auth guard', async () => {
    const route = routes.find(r => r.path === 'reviews')!; expect(route.canActivate).toEqual([authGuard]);
    expect(await route.loadComponent!()).toBe(ReviewSessionComponent);
    spyOn(TestBed.inject(AuthService), 'isAuthenticated').and.returnValue(false);
    expect(TestBed.runInInjectionContext(authGuard)).toBeFalse();
  });
  it('renders only server counts and marks blocked due words', () => {
    initialize(); const text = fixture.nativeElement.textContent;
    expect(text).toContain('Due reviews'); expect(text).toContain('Blocked: selected meaning unavailable');
    expect(text).toContain('All currently due words have been loaded.'); expect(text).toContain('your local time');
    expect(component.summary).toEqual(currentReviewSuccess().summary.data);
  });
  it('renders an empty enrolled backlog without inventing whether legacy vocabulary exists', () => {
    fixture.detectChanges(); const summary = currentReviewSuccess().summary;
    http.expectOne(base + '/reviews/summary').flush({ ...summary, data: { dueCount: 0, newCount: 0, overdueCount: 0, blockedCount: 0, usableDueCount: 0, totalEnrolledCount: 0, nextReviewAt: null, asOfUtc: summary.data.asOfUtc } });
    http.expectOne(base + '/reviews/due?take=20').flush({ success: true, data: { items: [], nextCursor: null, asOfUtc: summary.data.asOfUtc } }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No words are enrolled for reviews yet.');
    expect(fixture.nativeElement.textContent).toContain('No due vocabulary in this list.');
  });
  it('paginates in server order and suppresses duplicate IDs', () => {
    fixture.detectChanges(); http.expectOne(base + '/reviews/summary').flush(currentReviewSuccess().summary);
    const page = currentReviewSuccess().due;
    http.expectOne(base + '/reviews/due?take=20').flush({ ...page, data: { ...page.data, nextCursor: 'opaque' } });
    component.loadDue(); component.loadDue();
    http.expectOne(base + '/reviews/due?take=20&cursor=opaque').flush({ ...page, data: { ...page.data, items: [page.data.items[1], { ...page.data.items[0], userWordId: 9, word: 'next' }] } });
    expect(component.dueItems.map(i => i.userWordId)).toEqual([7, 8, 9]); expect(component.nextCursor).toBeNull();
  });
  it('retains a failed page for retry and safely restarts an expired cursor', () => {
    initialize(); component.nextCursor = 'expired'; component.loadDue();
    http.expectOne(base + '/reviews/due?take=20&cursor=expired').error(new ProgressEvent('error'));
    expect(component.dueItems.length).toBe(2); component.loadDue();
    http.expectOne(base + '/reviews/due?take=20&cursor=expired').flush({ code: 'validation_failed' }, { status: 400, statusText: 'Bad cursor' });
    expect(component.cursorExpired).toBeTrue(); component.refreshDue();
    http.expectOne(base + '/reviews/due?take=20').flush(currentReviewSuccess().due); expect(component.cursorExpired).toBeFalse();
  });
  for (const reason of ['insufficient_usable_vocabulary', 'ambiguous_options', 'no_usable_due_words'] as const) {
    it(`uses backend availability ${reason} even when due is positive`, () => {
      initialize(); component.startSession();
      http.expectOne(base + '/reviews/start').flush({ success: true, data: { availability: 'unavailable', reason, sessionId: null, expiresAtUtc: null, items: [], sessionSummary: null } });
      flushOverview(); expect(component.availabilityMessage.length).toBeGreaterThan(0); expect(component.session).toBeNull(); expect(storage.getItem(key)).toBeNull();
    });
  }
  it('handles a server empty session without creating a client session', () => {
    initialize(); component.startSession(); http.expectOne(base + '/reviews/start').flush({ success: true, data: { availability: 'empty', reason: null, sessionId: null, expiresAtUtc: null, items: [], sessionSummary: null } });
    flushOverview(); expect(component.availabilityMessage).toBe('No reviews are due right now.');
  });
  it('renders four native labelled radio choices, focuses the question and submits option zero once', () => {
    start(); const radios = fixture.nativeElement.querySelectorAll('input[type=radio]') as NodeListOf<HTMLInputElement>;
    expect(radios.length).toBe(4); expect(new Set(Array.from(radios).map(r => r.name)).size).toBe(1);
    expect(fixture.nativeElement.querySelector('fieldset legend').textContent).toContain('Select the definition');
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('h2.question'));
    radios[0].checked = true; radios[0].dispatchEvent(new Event('change')); fixture.detectChanges();
    expect(component.selection).toBe(0); expect(fixture.nativeElement.querySelector('button[type=submit]').disabled).toBeFalse();
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true })); component.submitAnswer(); component.selectOption(1);
    expect(component.submitting).toBeTrue(); expect(component.selection).toBe(0); fixture.detectChanges(); expect(radios[0].matches(':disabled')).toBeTrue();
    flushAccepted(); expect(component.currentReceipt?.isCorrect).toBeTrue(); expect(fixture.nativeElement.textContent).toContain('Correct answer: A trial');
    expect(fixture.nativeElement.querySelector('.feedback').getAttribute('aria-live')).toBe('polite');
  });
  it('does not infer correctness before acceptance and displays an incorrect server receipt', () => {
    start(); component.selectOption(1); component.submitAnswer(); expect(component.currentReceipt).toBeNull();
    flushAccepted({ ...currentReviewSuccess().submit.data, selectedOptionId: 1, selectedAnswer: 'A fruit', isCorrect: false,
      sessionSummary: { totalItems: 2, acceptedCount: 1, correctCount: 0, incorrectCount: 1, pendingCount: 1, isComplete: false } });
    expect(fixture.nativeElement.textContent).toContain('Incorrect'); expect(fixture.nativeElement.textContent).toContain('Correct answer: A trial');
  });
  it('keeps accepted questions immutable and completes with server counts', () => {
    start(); component.selectOption(0); component.submitAnswer(); flushAccepted(); component.selectOption(1); expect(component.selection).toBe(0);
    component.nextQuestion(); fixture.detectChanges(); expect(component.currentItem?.userWordId).toBe(8);
    component.selectOption(1); component.submitAnswer();
    flushAccepted({ ...currentReviewSuccess().submit.data, itemId: component.currentItem!.itemId, selectedOptionId: 1, correctOptionId: 1, selectedAnswer: 'A fruit', correctAnswer: 'A fruit',
      sessionSummary: { totalItems: 2, acceptedCount: 2, correctCount: 2, incorrectCount: 0, pendingCount: 0, isComplete: true } });
    flushOverview(); component.nextQuestion(); flushOverview(); fixture.detectChanges();
    expect(component.view).toBe('complete'); expect(component.completion?.correctCount).toBe(2); expect(storage.getItem(key)).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Review complete');
  });
  for (const failure of ['network', 'server', 'malformed'] as const) {
    it(`locks selection and new sessions after ${failure}, then replays the same request before resume`, () => {
      start(); component.selectOption(0); component.submitAnswer(); const request = http.expectOne(base + '/reviews/submit'); const payload = request.request.body;
      if (failure === 'network') request.error(new ProgressEvent('error'));
      else if (failure === 'server') request.flush(reviewApplicationFailure('internal_error'), { status: 500, statusText: 'Failure' });
      else request.flush({ success: true, data: {} });
      component.selectOption(2); component.startNewSession(); expect(component.selection).toBe(0); http.expectNone(base + '/reviews/start');
      component.recoverSession(); const replay = http.expectOne(base + '/reviews/submit'); expect(replay.request.body).toEqual(payload); replay.flush(currentReviewSuccess().submit);
      const resume = http.expectOne(base + '/reviews/start'); expect(resume.request.body).toEqual({ resumeSessionId: currentReviewSuccess().start.data.sessionId });
      const session = currentReviewSuccess().start; session.data.items[0].acceptedReceipt = currentReviewSuccess().submit.data;
      session.data.sessionSummary = currentReviewSuccess().submit.data.sessionSummary; resume.flush(session);
      expect(component.currentReceipt?.submissionId).toBe(payload.submissionId); expect(component.sessionError).toBe('');
    });
  }
  it('replays persisted pending payload before resuming after refresh', () => {
    // Component already exists, but a different root owner event simulates loading scoped recovery.
    const service = TestBed.inject(ReviewService); users.next(null);
    const f = currentReviewSuccess(); const pending = { sessionId: f.submit.data.sessionId, itemId: f.submit.data.itemId, submissionId: f.submit.data.submissionId, selectedOptionId: 0 };
    storage.setItem(key, JSON.stringify({ sessionId: pending.sessionId, pending }));
    users.next({ id: 7, username: 'reviewer', email: 'review@example.test', createdAt: '2026-01-02T03:04:05Z', lastLoginAt: null });
    expect(service.recovery.pending).toEqual(pending); fixture.detectChanges(); flushOverview();
    const replay = http.expectOne(base + '/reviews/submit'); expect(replay.request.body).toEqual(pending); http.expectNone(base + '/reviews/start'); replay.flush(f.submit);
    const resume = http.expectOne(base + '/reviews/start'); f.start.data.items[0].acceptedReceipt = f.submit.data; f.start.data.sessionSummary = f.submit.data.sessionSummary; resume.flush(f.start);
    expect(component.currentReceipt).toEqual(f.submit.data);
  });
  it('shows recovered feedback even if resume is expired after accepted replay', () => {
    start(); component.selectOption(0); component.submitAnswer(); http.expectOne(base + '/reviews/submit').error(new ProgressEvent('error'));
    component.recoverSession(); flushAccepted(); http.expectOne(base + '/reviews/start').flush(reviewApplicationFailure('review_session_expired'), { status: 409, statusText: 'Expired' });
    flushOverview(); fixture.detectChanges(); expect(component.recoveredReceipt?.isCorrect).toBeTrue(); expect(component.terminal).toBeTrue();
    expect(component.sessionError).toContain('expired'); expect(component.reviews.recovery.pending).toBeNull();
  });
  for (const code of ['review_session_expired', 'review_state_changed', 'review_submission_conflict', 'review_session_unavailable']) {
    it(`stops submission on definitive ${code} and preserves accepted feedback`, () => {
      start(); component.selectOption(0); component.submitAnswer(); flushAccepted(); component.nextQuestion(); component.selectOption(1); component.submitAnswer();
      http.expectOne(base + '/reviews/submit').flush(reviewApplicationFailure(code), { status: code === 'review_session_unavailable' ? 404 : 409, statusText: 'Conflict' });
      flushOverview(); expect(component.terminal).toBeTrue(); expect(component.acceptedItems.length).toBe(1); component.submitAnswer(); http.expectNone(base + '/reviews/submit');
      expect(component.reviews.recovery.pending).toBeNull();
    });
  }
  it('distinguishes disabled reviews from zero due and preserves pending recovery', () => {
    start(); component.selectOption(0); component.submitAnswer();
    http.expectOne(base + '/reviews/submit').flush(reviewApplicationFailure('reviews_disabled'), { status: 503, statusText: 'Disabled' });
    fixture.detectChanges(); expect(component.disabled).toBeTrue(); expect(component.reviews.recovery.pending).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Vocabulary reviews are not available right now.'); expect(fixture.nativeElement.textContent).not.toContain('SECRET');
  });
  it('keeps pending recovery on 401 and clears private view data on logout', () => {
    start(); component.selectOption(0); component.submitAnswer();
    http.expectOne(base + '/reviews/submit').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(component.authenticationRequired).toBeTrue(); expect(component.reviews.recovery.pending).not.toBeNull();
    users.next(null); expect(component.session).toBeNull(); expect(component.reviews.recovery.pending).toBeNull(); expect(storage.getItem(key)).toBeNull();
  });
  it('wraps long option text within a 320px container', () => {
    start(); fixture.nativeElement.style.width = '320px'; component.currentItem!.options[0].text = 'definition'.repeat(150); fixture.detectChanges();
    const page = fixture.nativeElement.querySelector('.review-page') as HTMLElement;
    expect(page.scrollWidth).toBeLessThanOrEqual(page.clientWidth + 1);
    expect(getComputedStyle(fixture.nativeElement.querySelector('.option')).overflowWrap).toBe('anywhere');
  });
  it('resumes persisted accepted receipts and presents the first pending question', () => {
    const service = TestBed.inject(ReviewService); users.next(null); const f = currentReviewSuccess();
    storage.setItem(key, JSON.stringify({ sessionId: f.start.data.sessionId, pending: null }));
    users.next({ id: 7, username: 'reviewer', email: 'review@example.test', createdAt: '2026-01-02T03:04:05Z', lastLoginAt: null });
    fixture.detectChanges(); flushOverview(); http.expectNone(base + '/reviews/submit');
    f.start.data.items[0].acceptedReceipt = f.submit.data; f.start.data.sessionSummary = f.submit.data.sessionSummary;
    http.expectOne(base + '/reviews/start').flush(f.start); fixture.detectChanges();
    expect(component.currentIndex).toBe(1); expect(component.acceptedItems.length).toBe(1); expect(service.recovery.sessionId).toBe(f.start.data.sessionId);
    expect(fixture.nativeElement.textContent).toContain('Accepted answers in this session (1)');
  });
  it('cancels old-account requests and clears the view before refreshing the new account', () => {
    start(); users.next({ id: 8, username: 'other', email: 'other@example.test', createdAt: '2026-01-02T03:04:05Z', lastLoginAt: null });
    expect(component.session).toBeNull(); expect(component.summary).toBeNull(); expect(component.dueItems.length).toBe(0);
    flushOverview(); expect(component.view).toBe('landing');
  });
  it('does not use the browser clock to expire a server-authorized session', () => {
    start(); expect(component.session!.expiresAtUtc).toBe('2026-10-13T12:30:00Z');
    component.selectOption(0); component.submitAnswer(); flushAccepted(); expect(component.currentReceipt).not.toBeNull();
  });
  it('keeps long words and definitions contained and interactive targets at least 44px high', () => {
    start(); fixture.nativeElement.style.width = '320px';
    component.currentItem!.prompt = "What is the meaning of '" + 'w'.repeat(100) + "'?";
    component.currentItem!.options[0].text = 'd'.repeat(1000); fixture.detectChanges();
    const page = fixture.nativeElement.querySelector('.review-page') as HTMLElement;
    expect(page.scrollWidth).toBeLessThanOrEqual(page.clientWidth + 1);
    const controls = fixture.nativeElement.querySelectorAll('button, .nav-link, .option') as NodeListOf<HTMLElement>;
    Array.from(controls).forEach(control => expect(control.getBoundingClientRect().height).toBeGreaterThanOrEqual(44));
    const radio = fixture.nativeElement.querySelector('input[type=radio]') as HTMLInputElement;
    radio.focus(); expect(document.activeElement).toBe(radio); expect(getComputedStyle(radio.closest('.option')!).outlineStyle).not.toBe('none');
  });
});
