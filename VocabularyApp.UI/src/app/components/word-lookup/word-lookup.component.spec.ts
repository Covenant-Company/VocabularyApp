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

  it('should remove Save for Later while preserving Add to My Vocabulary', () => {
    component.currentWord = {
      word: 'serendipity',
      partOfSpeechGroups: [],
      source: 'external'
    };
    component.viewingFromVocabularyList = false;
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Save for Later');
    expect(text).toContain('Add to My Vocabulary');
  });

  it('should render saved-word detail and Favorite as separate sibling buttons', () => {
    renderSavedWords(false);

    const detailButton = findButtonByText('Serendipity');
    const favoriteButton = findButtonByLabel('Add Serendipity to favorites');

    expect(detailButton).toBeTruthy();
    expect(favoriteButton).toBeTruthy();
    expect(detailButton.parentElement).toBe(favoriteButton.parentElement);
    expect(detailButton.contains(favoriteButton)).toBeFalse();
    expect(favoriteButton.contains(detailButton)).toBeFalse();
    expect(favoriteButton.getAttribute('aria-pressed')).toBe('false');
  });

  it('should expose the favorited state and removal action in the Favorite button', () => {
    renderSavedWords(true);

    const favoriteButton = findButtonByLabel('Remove Serendipity from favorites');

    expect(favoriteButton).toBeTruthy();
    expect(favoriteButton.getAttribute('aria-pressed')).toBe('true');
  });

  it('should open the correct saved word from its native detail button', () => {
    renderSavedWords(false);
    const word = component.vocabularyResponse!.words[0];
    const viewWordDetails = spyOn(component, 'viewWordDetails');

    findButtonByText('Serendipity').click();

    expect(viewWordDetails).toHaveBeenCalledOnceWith(word);
  });

  it('should toggle Favorite without opening saved-word details', () => {
    renderSavedWords(false);
    const word = component.vocabularyResponse!.words[0];
    const toggleFavorite = spyOn(component, 'toggleFavorite');
    const viewWordDetails = spyOn(component, 'viewWordDetails');

    findButtonByLabel('Add Serendipity to favorites').click();

    expect(toggleFavorite).toHaveBeenCalledOnceWith(word);
    expect(viewWordDetails).not.toHaveBeenCalled();
  });

  it('should associate a programmatic label with the saved-vocabulary filter', () => {
    component.showVocabularyList = true;
    fixture.detectChanges();

    const label = fixture.nativeElement.querySelector('label[for="vocabulary-filter"]') as HTMLLabelElement;
    const input = fixture.nativeElement.querySelector('#vocabulary-filter') as HTMLInputElement;

    expect(label).toBeTruthy();
    expect(label.textContent).toContain('Search saved vocabulary');
    expect(input).toBeTruthy();
    expect(input.id).toBe(label.htmlFor);
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

  it('should include definitions from all parts of speech in the quiz-definition picker', () => {
    const options = component.buildDefinitionOptions([
      { id: 1, definition: 'A noun definition', partOfSpeech: 'noun' },
      { id: 2, definition: 'An adjective definition', partOfSpeech: 'adjective' },
      { id: 3, definition: 'A verb definition', partOfSpeech: 'verb' }
    ]);

    expect(options.map((option: { definition: string }) => option.definition)).toEqual([
      'A noun definition',
      'An adjective definition',
      'A verb definition'
    ]);
    expect(options.some((option: { partOfSpeech: string }) => option.partOfSpeech === 'adjective')).toBeTrue();
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

  it('should clear the vocabulary search when selecting a letter', () => {
    component.vocabularyResponse = {
      words: [
        { id: 1, word: 'Apple', definition: 'A fruit', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 1000,
      totalPages: 1
    };

    component.vocabularySearchQuery = 'fruit';
    component.selectVocabularyLetter('A');

    expect(component.vocabularySearchQuery).toBe('');
    expect(component.selectedVocabularyLetter).toBe('A');
  });

  it('should format letter tooltip with correct plural grammar', () => {
    expect(component.getLetterTooltip('A', 0)).toBe('0 words start with "A"');
    expect(component.getLetterTooltip('G', 1)).toBe('1 word starts with "G"');
    expect(component.getLetterTooltip('P', 2)).toBe('2 words start with "P"');
  });

  it('should highlight the active vocabulary search text', () => {
    component.vocabularySearchQuery = 'luck';

    const highlighted = component.getHighlightedText('Lucky discovery');

    expect(highlighted).toContain('<mark class="search-highlight">Luck</mark>');
  });

  function renderSavedWords(isFavorite: boolean): void {
    component.showVocabularyList = true;
    component.vocabularyResponse = {
      words: [
        {
          id: 1,
          word: 'Serendipity',
          definition: 'Lucky discovery',
          partOfSpeech: 'Noun',
          addedAt: '',
          isFavorite,
          correctAnswers: 0,
          totalAttempts: 0
        }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 1000,
      totalPages: 1
    };
    component.selectedVocabularyLetter = 'S';
    fixture.detectChanges();
  }

  function findButtonByText(text: string): HTMLButtonElement {
    const root = fixture.nativeElement as HTMLElement;
    return Array.from(root.querySelectorAll('button'))
      .find(button => button.textContent?.includes(text)) as HTMLButtonElement;
  }

  function findButtonByLabel(label: string): HTMLButtonElement {
    return fixture.nativeElement.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;
  }
});
