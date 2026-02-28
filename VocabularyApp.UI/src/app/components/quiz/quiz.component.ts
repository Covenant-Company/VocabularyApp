import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../services/api.service';
import {
  QuizHistoryItem,
  QuizHistoryResponse,
  QuizAnswerSubmission,
  QuizMode,
  QuizQuestion,
  QuizStartResponse,
  QuizSubmitResponse,
  StartQuizRequest
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
    this.errorMessage = '';
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
    this.apiService.post<QuizStartResponse>('/words/quiz/start', payload).subscribe({
      next: response => {
        if (response.success && response.data) {
          this.quizSession = response.data;
          this.syncSelectedAnswer();
        } else {
          this.errorMessage = response.message || 'Unable to start quiz.';
        }

        this.isLoading = false;
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.error?.errorMessage || 'Unable to start quiz.';
        this.isLoading = false;
      }
    });
  }

  selectOption(optionId: number): void {
    this.selectedOptionId = optionId;
  }

  previousQuestion(): void {
    if (!this.quizSession || this.currentQuestionIndex === 0) {
      return;
    }

    this.persistCurrentAnswer();
    this.currentQuestionIndex--;
    this.syncSelectedAnswer();
  }

  nextQuestion(): void {
    if (!this.quizSession || !this.currentQuestion) {
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
    if (!this.quizSession) {
      return;
    }

    this.persistCurrentAnswer();

    const answers: QuizAnswerSubmission[] = this.quizSession.questions
      .filter(question => this.selectedAnswers.has(question.questionId))
      .map(question => ({
        questionId: question.questionId,
        selectedOptionId: this.selectedAnswers.get(question.questionId) as number
      }));

    this.isSubmitting = true;
    this.apiService.post<QuizSubmitResponse>('/words/quiz/submit', {
      sessionId: this.quizSession.sessionId,
      answers
    }).subscribe({
      next: response => {
        if (response.success && response.data) {
          this.quizResult = response.data;
        } else {
          this.errorMessage = response.message || 'Unable to submit quiz.';
        }

        this.isSubmitting = false;
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.error?.errorMessage || 'Unable to submit quiz.';
        this.isSubmitting = false;
      }
    });
  }

  private loadRecentQuizHistory(): void {
    this.quizHistoryLoading = true;
    this.quizHistoryError = '';

    this.apiService.get<QuizHistoryResponse>('/words/quiz/history?take=5').subscribe({
      next: response => {
        if (response.success && response.data) {
          this.quizHistory = response.data.items || [];
        } else {
          this.quizHistoryError = response.message || 'Unable to load quiz history.';
        }

        this.quizHistoryLoading = false;
      },
      error: error => {
        this.quizHistoryError = error?.error?.error || error?.error?.errorMessage || 'Unable to load quiz history.';
        this.quizHistoryLoading = false;
      }
    });
  }
}
