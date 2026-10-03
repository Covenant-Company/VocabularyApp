import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { normalizeApiError } from '../../services/api-error';
import {
  QuizHistoryItem,
  QuizHistoryResponse,
  QuizAnswerSubmission,
  QuizMode,
  QuizQuestion,
  QuizStartResponse,
  QuizSubmitResponse,
  StartQuizRequest,
  QuizSubmitRequest
} from '../../models/quiz.model';

@Component({
  selector: 'app-quiz',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './quiz.component.html',
  styleUrl: './quiz.component.scss'
})
export class QuizComponent {
  questionCount = 10;
  mode: QuizMode = 'mixed';

  isLoading = false;
  isSubmitting = false;
  quizRecoveryRequiresRestart = false;
  errorMessage = '';
  quizHistory: QuizHistoryItem[] = [];
  quizHistoryLoading = false;
  quizHistoryError = '';
  showQuizHistory = false;

  quizSession: QuizStartResponse | null = null;
  quizResult: QuizSubmitResponse | null = null;

  currentQuestionIndex = 0;
  selectedOptionId: number | null = null;
  private selectedAnswers = new Map<string, number>();

  constructor(
    private apiService: ApiService,
    private router: Router
  ) { }

  get currentQuestion(): QuizQuestion | null {
    if (!this.quizSession) {
      return null;
    }

    return this.quizSession.questions[this.currentQuestionIndex] ?? null;
  }

  get progressText(): string {
    if (!this.quizSession) {
      return '';
    }

    return `${this.currentQuestionIndex + 1} / ${this.quizSession.questions.length}`;
  }

  backToVocabulary(): void {
    this.router.navigate(['/vocabulary']);
  }

  startQuiz(): void {
    if (this.isLoading || this.isSubmitting) return;
    this.errorMessage = '';
    this.quizRecoveryRequiresRestart = false;
    this.quizResult = null;
    this.quizSession = null;
    this.currentQuestionIndex = 0;
    this.selectedOptionId = null;
    this.selectedAnswers.clear();

    const payload: StartQuizRequest = {
      questionCount: this.questionCount,
      mode: this.mode
    };

    this.isLoading = true;
    this.apiService.post<QuizStartResponse, StartQuizRequest>('/quiz/start', payload).subscribe({
      next: response => {
        if (response?.success && response.data && typeof response.data.sessionId === 'string' && Array.isArray(response.data.questions) && response.data.questions.length > 0) {
          this.quizSession = response.data;
          this.syncSelectedAnswer();
        } else {
          this.errorMessage = 'Unable to start quiz.';
        }

        this.isLoading = false;
      },
      error: error => {
        this.errorMessage = normalizeApiError(error, 'quiz-start', 'Unable to start quiz.').message;
        this.isLoading = false;
      }
    });
  }

  selectOption(optionId: number): void {
    if (this.isSubmitting || this.quizResult) return;
    this.selectedOptionId = optionId;
  }

  previousQuestion(): void {
    if (!this.quizSession || this.currentQuestionIndex === 0 || this.isSubmitting || this.quizResult) {
      return;
    }

    this.persistCurrentAnswer();
    this.currentQuestionIndex--;
    this.syncSelectedAnswer();
  }

  nextQuestion(): void {
    if (!this.quizSession || !this.currentQuestion || this.isSubmitting || this.quizResult || this.quizRecoveryRequiresRestart) {
      return;
    }

    if (this.selectedOptionId === null) {
      this.errorMessage = 'Please select an answer before continuing.';
      return;
    }

    this.errorMessage = '';
    this.persistCurrentAnswer();

    if (this.currentQuestionIndex >= this.quizSession.questions.length - 1) {
      this.submitQuiz();
      return;
    }

    this.currentQuestionIndex++;
    this.syncSelectedAnswer();
  }

  restartQuiz(): void {
    this.startQuiz();
  }

  toggleQuizHistory(): void {
    this.showQuizHistory = !this.showQuizHistory;

    if (this.showQuizHistory && this.quizHistory.length === 0 && !this.quizHistoryLoading) {
      this.loadRecentQuizHistory();
    }
  }

  private persistCurrentAnswer(): void {
    if (!this.currentQuestion || this.selectedOptionId === null) {
      return;
    }

    this.selectedAnswers.set(this.currentQuestion.questionId, this.selectedOptionId);
  }

  private syncSelectedAnswer(): void {
    if (!this.currentQuestion) {
      this.selectedOptionId = null;
      return;
    }

    this.selectedOptionId = this.selectedAnswers.get(this.currentQuestion.questionId) ?? null;
  }

  private submitQuiz(): void {
    if (!this.quizSession || this.isSubmitting || this.quizResult || this.quizRecoveryRequiresRestart) {
      return;
    }

    this.errorMessage = '';
    this.persistCurrentAnswer();

    const answers: QuizAnswerSubmission[] = this.quizSession.questions
      .flatMap(question => {
        const selectedOptionId = this.selectedAnswers.get(question.questionId);
        return selectedOptionId === undefined ? [] : [{ questionId: question.questionId, selectedOptionId }];
      });

    this.isSubmitting = true;
    this.apiService.post<QuizSubmitResponse, QuizSubmitRequest>('/quiz/submit', {
      sessionId: this.quizSession.sessionId,
      answers
    }).subscribe({
      next: response => {
        if (response?.success && response.data && Array.isArray(response.data.questionResults)
          && Number.isFinite(response.data.totalQuestions) && Number.isFinite(response.data.correctAnswers) && Number.isFinite(response.data.scorePercentage)) {
          this.quizResult = response.data;
        } else {
          this.errorMessage = 'Unable to submit quiz.';
        }

        this.isSubmitting = false;
      },
      error: error => {
        const failure = normalizeApiError(error, 'quiz-submit', 'Unable to submit quiz.');
        this.errorMessage = failure.message;
        this.quizRecoveryRequiresRestart = error?.status === 404
          || failure.code === 'quiz_session_unavailable' || failure.code === 'quiz_vocabulary_changed';
        this.isSubmitting = false;
      }
    });
  }

  private loadRecentQuizHistory(): void {
    this.quizHistoryLoading = true;
    this.quizHistoryError = '';

    this.apiService.get<QuizHistoryResponse>('/quiz/history?take=5').subscribe({
      next: response => {
        if (response?.success && response.data && Array.isArray(response.data.items)) {
          this.quizHistory = response.data.items;
        } else {
          this.quizHistoryError = 'Unable to load quiz history.';
        }

        this.quizHistoryLoading = false;
      },
      error: error => {
        this.quizHistoryError = normalizeApiError(error, 'quiz-history', 'Unable to load quiz history.').message;
        this.quizHistoryLoading = false;
      }
    });
  }
}
