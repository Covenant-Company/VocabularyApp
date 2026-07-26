export interface Definition {
  id?: number;
  definition: string;
  example?: string;
  synonyms?: string[];
  antonyms?: string[];
}

export interface PartOfSpeechGroup {
  partOfSpeech: string;
  priority: number;
  definitions: Definition[];
  isExpanded: boolean;
  primaryDefinitions: Definition[];
}

export interface WordLookupResult {
  word: string;
  phonetic?: string;
  audioUrl?: string;
  partOfSpeechGroups: PartOfSpeechGroup[];
  source: 'user' | 'canonical' | 'external';
}

export interface SearchSuggestion {
  word: string;
  type: 'existing' | 'new-search';
  partOfSpeech?: string;
  preview?: string;
  action: string;
}

export const POS_PRIORITY = {
  'noun': 1,
  'verb': 2,
  'adjective': 3,
  'adverb': 4,
  'preposition': 5,
  'conjunction': 6,
  'interjection': 7,
  'pronoun': 8,
  'determiner': 9,
  'exclamation': 10
} as const;

// Vocabulary list interfaces
export interface VocabularyItem {
  id: number;
  word: string;
  definition: string;
  preferredWordDefinitionId?: number;
  example?: string;
  partOfSpeech: string;
  pronunciation?: string;
  audioUrl?: string;
  addedAt: string;
  isFavorite: boolean;
  personalNotes?: string;
  correctAnswers: number;
  totalAttempts: number;
  accuracyRate?: number;
}

export interface VocabularyResponse {
  words: VocabularyItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}