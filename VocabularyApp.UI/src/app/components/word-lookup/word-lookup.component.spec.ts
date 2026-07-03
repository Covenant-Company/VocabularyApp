import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { WordLookupComponent } from './word-lookup.component';

describe('WordLookupComponent', () => {
  let component: WordLookupComponent;
  let fixture: ComponentFixture<WordLookupComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WordLookupComponent, HttpClientTestingModule, RouterTestingModule]
    })
      .compileComponents();

    fixture = TestBed.createComponent(WordLookupComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should clear current word when user starts typing', () => {
    // Setup: Set up a current word and sorted groups
    component.currentWord = {
      word: 'test',
      phonetic: '/test/',
      partOfSpeechGroups: [],
      source: 'external'
    };
    component.sortedGroups = [
      {
        partOfSpeech: 'noun',
        priority: 1,
        definitions: [{ definition: 'test definition' }],
        isExpanded: false,
        primaryDefinitions: [{ definition: 'test definition' }]
      }
    ];
    component.errorMessage = 'some error';

    // Action: Start typing in search
    component.searchTerm = 'new search';
    component.onSearchInput();

    // Assert: Current word and related data should be cleared
    expect(component.currentWord).toBeNull();
    expect(component.sortedGroups).toEqual([]);
    expect(component.errorMessage).toBe('');
  });

  it('should compute letter availability and counts from vocabulary catalog', () => {
    component.vocabularyResponse = {
      words: [
        { id: 1, word: 'Apple', definition: 'A fruit', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 },
        { id: 2, word: 'Banana', definition: 'Yellow fruit', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 }
      ],
      totalCount: 2,
      page: 1,
      pageSize: 1000,
      totalPages: 1
    };

    expect(component.hasWordsForLetter('A')).toBeTrue();
    expect(component.hasWordsForLetter('Z')).toBeFalse();
    expect(component.getWordCountForLetter('A')).toBe(1);
    expect(component.getWordCountForLetter('B')).toBe(1);
  });

  it('should expose server-returned vocabulary list as filtered words', () => {
    component.vocabularyResponse = {
      words: [
        { id: 1, word: 'Serendipity', definition: 'Lucky discovery', example: 'A fortunate surprise', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 1000,
      totalPages: 1
    };

    component.selectedVocabularyLetter = 'S';

    expect(component.filteredVocabularyWords.map(x => x.word)).toEqual(['Serendipity']);
  });

  it('should use contains search across word, definition, and example (case-insensitive)', () => {
    component.vocabularyResponse = {
      words: [
        { id: 1, word: 'Serendipity', definition: 'Lucky discovery', example: 'A fortunate surprise', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 },
        { id: 2, word: 'Pragmatic', definition: 'Practical and realistic', example: 'A pragmatic approach', partOfSpeech: 'Adjective', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 }
      ],
      totalCount: 2,
      page: 1,
      pageSize: 1000,
      totalPages: 1
    };

    component.selectedVocabularyLetter = 'P';
    component.vocabularySearchQuery = 'SURPR';

    expect(component.filteredVocabularyWords.map(x => x.word)).toEqual(['Serendipity']);
  });

  it('should browse by selected letter when search is empty', () => {
    component.vocabularyResponse = {
      words: [
        { id: 1, word: 'Apple', definition: 'A fruit', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 },
        { id: 2, word: 'Banana', definition: 'Yellow fruit', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 }
      ],
      totalCount: 2,
      page: 1,
      pageSize: 1000,
      totalPages: 1
    };

    component.vocabularySearchQuery = '';
    component.selectedVocabularyLetter = 'B';

    expect(component.filteredVocabularyWords.map(x => x.word)).toEqual(['Banana']);
  });

  it('should disable unavailable letter selection', () => {
    component.vocabularyResponse = {
      words: [
        { id: 1, word: 'Apple', definition: 'A fruit', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 1000,
      totalPages: 1
    };

    component.selectVocabularyLetter('A');
    expect(component.selectedVocabularyLetter).toBe('A');

    component.selectVocabularyLetter('Z');
    expect(component.selectedVocabularyLetter).toBe('A');
    expect(component.hasWordsForLetter('Z')).toBeFalse();
  });

  it('should format letter tooltip with correct plural grammar', () => {
    expect(component.getLetterTooltip('A', 0)).toBe('0 words start with "A"');
    expect(component.getLetterTooltip('G', 1)).toBe('1 word starts with "G"');
    expect(component.getLetterTooltip('P', 2)).toBe('2 words start with "P"');
  });
});
