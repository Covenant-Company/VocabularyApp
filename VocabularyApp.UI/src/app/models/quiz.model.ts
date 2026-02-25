export type QuizMode = 'mixed' | 'word-to-definition' | 'definition-to-word';

export interface StartQuizRequest {
  questionCount: number;
  mode: QuizMode;
}

export interface QuizOption {
  optionId: number;
  text: string;
}

export interface QuizQuestion {
  questionId: string;
  questionType: 'word-to-definition' | 'definition-to-word';
  prompt: string;
  options: QuizOption[];
}

export interface QuizStartResponse {
  sessionId: string;
  mode: QuizMode;
  questionCount: number;
  expiresAtUtc: string;
  questions: QuizQuestion[];
}

export interface QuizAnswerSubmission {
  questionId: string;
  selectedOptionId: number;
}

export interface QuizSubmitRequest {
  sessionId: string;
  answers: QuizAnswerSubmission[];
}

export interface QuizQuestionResult {
  questionId: string;
  questionType: string;
  prompt: string;
  correctAnswer: string;
  selectedAnswer?: string;
  isCorrect: boolean;
}

export interface QuizSubmitResponse {
  totalQuestions: number;
  correctAnswers: number;
  scorePercentage: number;
  questionResults: QuizQuestionResult[];
}

export interface QuizHistoryItem {
  attemptedAtUtc: string;
  totalQuestions: number;
  correctAnswers: number;
  scorePercentage: number;
}

export interface QuizHistoryResponse {
  items: QuizHistoryItem[];
}
