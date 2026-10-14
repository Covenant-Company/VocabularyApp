// Exact F6 public DTOs. Scheduling and correctness are supplied by the server.
export type ReviewStatus = 'new' | 'learning' | 'reviewing';
export interface ReviewDueQuery { take?: number; cursor?: string | null; }
export interface ReviewDueItem {
  userWordId: number; word: string; nextReviewAt: string; reviewStatus: ReviewStatus; isUsable: boolean;
}
export interface ReviewDueResponse { items: ReviewDueItem[]; nextCursor: string | null; asOfUtc: string; }
export interface ReviewSummaryResponse {
  dueCount: number; newCount: number; overdueCount: number; blockedCount: number;
  usableDueCount: number; totalEnrolledCount: number; nextReviewAt: string | null; asOfUtc: string;
}
export interface ReviewStartRequest { questionCount?: number; resumeSessionId?: string | null; }
export interface ReviewOption { optionId: number; text: string; }
export interface ReviewItem {
  itemId: string; userWordId: number; questionType: 'word-to-definition'; prompt: string;
  options: ReviewOption[]; acceptedReceipt: ReviewSubmitResponse | null;
}
export interface ReviewSessionSummary {
  totalItems: number; acceptedCount: number; correctCount: number; incorrectCount: number;
  pendingCount: number; isComplete: boolean;
}
export interface ReviewStartResponse {
  availability: 'ready' | 'empty' | 'unavailable';
  reason: 'insufficient_usable_vocabulary' | 'ambiguous_options' | 'no_usable_due_words' | null;
  sessionId: string | null; expiresAtUtc: string | null; items: ReviewItem[]; sessionSummary: ReviewSessionSummary | null;
}
export interface ReviewSubmitRequest {
  readonly sessionId: string; readonly itemId: string; readonly submissionId: string; readonly selectedOptionId: number;
}
export interface ReviewSubmitResponse {
  sessionId: string; itemId: string; submissionId: string; reviewedAtUtc: string; isCorrect: boolean;
  selectedOptionId: number; correctOptionId: number; selectedAnswer: string; correctAnswer: string;
  nextReviewAt: string; reviewIntervalDays: number; reviewStatus: 'learning' | 'reviewing'; sessionSummary: ReviewSessionSummary;
}
