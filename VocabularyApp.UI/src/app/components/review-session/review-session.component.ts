import { AfterViewChecked, Component, DestroyRef, ElementRef, inject, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../services/auth.service';
import { normalizeApiError } from '../../services/api-error';
import { ReviewRecoveryError, ReviewService } from '../../services/review.service';
import { ReviewDueItem, ReviewSessionSummary, ReviewStartResponse, ReviewSubmitResponse, ReviewSummaryResponse } from '../../models/review.model';

@Component({
  selector: 'app-review-session', standalone: true, imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './review-session.component.html', styleUrl: './review-session.component.scss'
})
export class ReviewSessionComponent implements OnInit, AfterViewChecked {
  private readonly destroyRef = inject(DestroyRef);
  @ViewChild('questionHeading') questionHeading?: ElementRef<HTMLElement>;
  private focusQuestion = false;
  private ownerId: number | null = null;
  view: 'landing' | 'session' | 'complete' = 'landing';
  summary: ReviewSummaryResponse | null = null;
  summaryLoading = false;
  summaryError = '';
  dueItems: ReviewDueItem[] = [];
  nextCursor: string | null = null;
  dueAsOf: string | null = null;
  dueLoading = false;
  dueLoaded = false;
  dueError = '';
  cursorExpired = false;
  disabled = false;
  authenticationRequired = false;
  session: ReviewStartResponse | null = null;
  sessionLoading = false;
  submitting = false;
  terminal = false;
  sessionError = '';
  availabilityMessage = '';
  selection: number | null = null;
  questionCount = 10;
  currentIndex = 0;
  recoveredReceipt: ReviewSubmitResponse | null = null;
  completion: ReviewSessionSummary | null = null;

  constructor(public reviews: ReviewService, private auth: AuthService, private router: Router) {}
  ngOnInit(): void {
    this.auth.currentUser$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(user => {
      const changed = this.ownerId !== null && this.ownerId !== user?.id;
      this.ownerId = user?.id ?? null;
      if (!user || changed) {
        this.session = null; this.recoveredReceipt = null; this.completion = null; this.summary = null; this.dueItems = [];
        this.view = 'landing'; this.disabled = false; this.authenticationRequired = false; this.terminal = false;
        this.summaryLoading = false; this.dueLoading = false; this.sessionLoading = false; this.submitting = false;
        this.sessionError = ''; this.summaryError = ''; this.dueError = ''; this.selection = null;
      }
      if (!user) this.router.navigate(['/login']);
      else if (changed) {
        this.refreshOverview();
        if (this.reviews.recovery.sessionId) this.recoverSession();
      }
    });
    if (!this.auth.isAuthenticated()) { this.router.navigate(['/login']); return; }
    this.refreshOverview();
    if (this.reviews.recovery.sessionId) this.recoverSession();
  }
  ngAfterViewChecked(): void {
    if (this.focusQuestion && this.questionHeading) { this.focusQuestion = false; this.questionHeading.nativeElement.focus(); }
  }
  get currentItem() { return this.session?.items[this.currentIndex] ?? null; }
  get currentReceipt() { return this.currentItem?.acceptedReceipt ?? null; }
  get acceptedItems() { return this.session?.items.filter(i => i.acceptedReceipt !== null) ?? []; }
  get locked(): boolean { return this.submitting || this.sessionLoading || this.terminal || this.disabled || this.authenticationRequired || this.reviews.recovery.pending !== null; }
  get progress(): ReviewSessionSummary | null { return this.session?.sessionSummary ?? this.completion; }

  refreshOverview(): void { this.loadSummary(); this.refreshDue(); }
  loadSummary(): void {
    if (this.summaryLoading) return;
    this.summaryLoading = true; this.summaryError = '';
    this.reviews.summary().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: summary => { this.summary = summary; this.summaryLoading = false; this.disabled = false; },
      error: error => { this.summaryLoading = false; this.summaryError = this.failure(error, 'review-summary', 'Unable to load review counts.'); }
    });
  }
  refreshDue(): void {
    if (this.dueLoading) return;
    this.dueItems = []; this.nextCursor = null; this.dueAsOf = null; this.dueLoaded = false; this.cursorExpired = false;
    this.loadDue();
  }
  loadDue(): void {
    if (this.dueLoading || this.cursorExpired || this.dueLoaded && this.nextCursor === null) return;
    this.dueLoading = true; this.dueError = '';
    const cursor = this.nextCursor;
    this.reviews.due({ take: 20, cursor }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => {
        const seen = new Set(this.dueItems.map(i => i.userWordId));
        this.dueItems = [...this.dueItems, ...page.items.filter(i => { if (seen.has(i.userWordId)) return false; seen.add(i.userWordId); return true; })];
        this.nextCursor = page.nextCursor; this.dueAsOf = page.asOfUtc; this.dueLoaded = true; this.dueLoading = false;
      },
      error: error => {
        this.dueLoading = false;
        this.cursorExpired = cursor !== null && error instanceof HttpErrorResponse && error.status === 400;
        this.dueError = this.cursorExpired ? 'This backlog page has expired. Refresh the list to continue.' : this.failure(error, 'review-due', 'Unable to load due vocabulary.');
      }
    });
  }
  startSession(): void {
    if (this.sessionLoading || this.submitting || this.disabled || this.authenticationRequired || this.reviews.recovery.sessionId) return;
    if (!Number.isInteger(this.questionCount) || this.questionCount < 1 || this.questionCount > 20) {
      this.sessionError = 'Choose between 1 and 20 questions.'; return;
    }
    this.sessionLoading = true; this.sessionError = ''; this.availabilityMessage = ''; this.terminal = false;
    this.reviews.start(this.questionCount).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: session => {
        this.sessionLoading = false;
        if (session.availability === 'ready') this.install(session);
        else {
          this.availabilityMessage = session.availability === 'empty' ? 'No reviews are due right now.' : {
            insufficient_usable_vocabulary: 'Save at least four words with usable meanings to review with multiple choice.',
            ambiguous_options: 'Distinct answer choices are unavailable for your due words. Check your selected meanings in My Words.',
            no_usable_due_words: 'Your due words need usable selected meanings. Check them in My Words.'
          }[session.reason!];
          this.refreshOverview();
        }
      }, error: error => this.sessionFailure(error)
    });
  }
  recoverSession(): void {
    if (this.sessionLoading || this.submitting) return;
    this.sessionError = ''; this.sessionLoading = true;
    if (this.reviews.recovery.pending) {
      this.submitting = true;
      this.reviews.retryPending().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: receipt => {
          this.submitting = false; this.recoveredReceipt = receipt;
          const item = this.session?.items.find(i => i.itemId === receipt.itemId);
          if (item) item.acceptedReceipt = receipt;
          if (this.session && receipt.sessionSummary.acceptedCount >= this.session.sessionSummary!.acceptedCount)
            this.session.sessionSummary = receipt.sessionSummary;
          if (receipt.sessionSummary.isComplete) { this.sessionLoading = false; this.showCompletion(receipt.sessionSummary); }
          else this.resume(receipt.itemId);
        }, error: error => { this.submitting = false; this.sessionFailure(error); }
      });
    } else this.resume();
  }
  private resume(itemId?: string): void {
    this.reviews.resume().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: session => { this.sessionLoading = false; this.install(session, itemId); },
      error: error => this.sessionFailure(error)
    });
  }
  private install(session: ReviewStartResponse, itemId?: string): void {
    this.session = session; this.terminal = false; this.authenticationRequired = false; this.disabled = false;
    if (session.sessionSummary!.isComplete) { this.reviews.finish(session.sessionId!); this.showCompletion(session.sessionSummary!); return; }
    const preferred = itemId ? session.items.findIndex(i => i.itemId === itemId) : -1;
    this.currentIndex = preferred >= 0 ? preferred : session.items.findIndex(i => i.acceptedReceipt === null);
    this.selection = this.currentReceipt?.selectedOptionId ?? null; this.view = 'session'; this.focusQuestion = true;
  }
  selectOption(optionId: number): void {
    if (!this.locked && !this.currentReceipt && this.currentItem?.options.some(o => o.optionId === optionId)) { this.selection = optionId; this.sessionError = ''; }
  }
  submitAnswer(): void {
    if (this.locked || !this.session || !this.currentItem || this.currentReceipt) return;
    if (this.selection === null) { this.sessionError = 'Select a definition before submitting.'; return; }
    this.submitting = true; this.sessionError = '';
    this.reviews.answer(this.session.sessionId!, this.currentItem.itemId, this.selection).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: receipt => {
        this.submitting = false; this.currentItem!.acceptedReceipt = receipt;
        if (receipt.sessionSummary.acceptedCount >= this.session!.sessionSummary!.acceptedCount) this.session!.sessionSummary = receipt.sessionSummary;
        // Keep the final answer feedback visible until the user chooses the completion action.
        if (receipt.sessionSummary.isComplete) this.refreshOverview();
      }, error: error => { this.submitting = false; this.sessionFailure(error); }
    });
  }
  nextQuestion(): void {
    if (this.locked || !this.session || !this.currentReceipt) return;
    if (this.session.sessionSummary!.isComplete) { this.reviews.finish(this.session.sessionId!); this.showCompletion(this.session.sessionSummary!); return; }
    this.currentIndex = this.session.items.findIndex(i => i.acceptedReceipt === null); this.selection = null;
    this.recoveredReceipt = null; this.sessionError = ''; this.focusQuestion = true;
  }
  overview(): void { this.view = 'landing'; this.refreshOverview(); }
  startNewSession(): void {
    if (this.reviews.recovery.pending || this.submitting || this.sessionLoading || this.disabled || this.authenticationRequired) return;
    this.reviews.discardSession(); this.session = null; this.completion = null; this.recoveredReceipt = null;
    this.terminal = false; this.sessionError = ''; this.view = 'landing'; this.startSession();
  }
  private showCompletion(summary: ReviewSessionSummary): void { this.completion = summary; this.view = 'complete'; this.refreshOverview(); }
  private sessionFailure(error: unknown): void {
    this.sessionLoading = false;
    this.sessionError = this.failure(error, 'review-submit', 'Unable to continue your review. Please try again.');
    this.terminal = error instanceof HttpErrorResponse && [404, 409].includes(error.status);
    if (this.terminal) this.refreshOverview();
  }
  private failure(error: unknown, operation: 'review-due' | 'review-summary' | 'review-submit', fallback: string): string {
    if (error instanceof ReviewRecoveryError) return error.message;
    const failure = normalizeApiError(error, operation, fallback);
    if (failure.code === 'reviews_disabled') this.disabled = true;
    if (error instanceof HttpErrorResponse && error.status === 401) this.authenticationRequired = true;
    return failure.message;
  }
}
