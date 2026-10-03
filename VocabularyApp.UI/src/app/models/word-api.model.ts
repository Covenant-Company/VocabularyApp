// Wire DTOs; presentation models may map null to undefined explicitly.
export interface WordDefinitionDto {
  id: number;
  definition: string;
  example: string | null;
  partOfSpeech: string;
  partOfSpeechAbbreviation: string;
  displayOrder: number;
}

export interface WordDto {
  id: number;
  text: string;
  pronunciation: string | null;
  audioUrl: string | null;
  createdAt: string;
  definitions: WordDefinitionDto[];
}

export interface WordLookupResponse {
  success: boolean;
  errorMessage: string | null;
  word: WordDto | null;
  wasFoundInCache: boolean;
  isInUserVocabulary: boolean;
}

export interface VocabularyItemDto {
  id: number;
  word: string;
  definition: string;
  preferredWordDefinitionId: number | null;
  example: string | null;
  partOfSpeech: string;
  pronunciation: string | null;
  audioUrl: string | null;
  addedAt: string;
  isFavorite: boolean;
  personalNotes: string | null;
  correctAnswers: number;
  totalAttempts: number;
  accuracyRate: number | null;
}

export interface VocabularyResponseDto {
  words: VocabularyItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface AddWordRequest {
  word: string;
  // Accepted for compatibility; definition/example/pronunciation never author canonical data.
  definition?: string | null;
  example?: string | null;
  partOfSpeech?: string | null;
  pronunciation?: string | null;
  preferredWordDefinitionId?: number | null;
}

export interface AddToVocabularyResult {
  userWordId: number;
  wordId: number;
  alreadyExisted: boolean;
  message: string;
}

export interface FavoriteRequest { isFavorite: boolean; }
export interface FavoriteResponse { message: string; userWordId: number; isFavorite: boolean; }
export interface PreferredDefinitionRequest { preferredWordDefinitionId: number; }
export interface PreferredDefinitionResponse { message: string; userWordId: number; preferredWordDefinitionId: number; }

export const wordLookupPath = (word: string): string => `/words/lookup/${encodeURIComponent(word)}`;
export const vocabularySearchPath = (term?: string): string => term === undefined
  ? '/words/vocabulary/search'
  : `/words/vocabulary/search?term=${encodeURIComponent(term)}`;
