import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { WordLookupComponent } from './word-lookup.component';

describe('WordLookupComponent', () => {
  let component: WordLookupComponent;
  let fixture: ComponentFixture<WordLookupComponent>;
  let httpTestingController: HttpTestingController;
  let originalSpeechSynthesisDescriptor: PropertyDescriptor | undefined;
  let originalUtteranceDescriptor: PropertyDescriptor | undefined;

  interface FakeUtterance {
    text: string;
    lang: string;
    rate: number;
    pitch: number;
    volume: number;
    voice: SpeechSynthesisVoice | null;
  }

  function installSpeechSynthesisFakes() {
    const callOrder: string[] = [];
    const cancel = jasmine.createSpy('cancel').and.callFake(() => callOrder.push('cancel'));
    const speak = jasmine.createSpy('speak').and.callFake(() => callOrder.push('speak'));
    const utterances: FakeUtterance[] = [];
    const constructorArguments: string[] = [];
    const utteranceConstructor = function (this: FakeUtterance, text: string): void {
      constructorArguments.push(text);
      this.text = text;
      this.lang = '';
      this.rate = 1;
      this.pitch = 1;
      this.volume = 1;
      this.voice = null;
      utterances.push(this);
    };

    Object.defineProperty(window, 'speechSynthesis', {
      configurable: true,
      value: { cancel, speak }
    });
    Object.defineProperty(window, 'SpeechSynthesisUtterance', {
      configurable: true,
      value: utteranceConstructor
    });

    return { callOrder, cancel, speak, utterances, constructorArguments };
  }

  function setUnsupportedBrowser(missingApi: 'speechSynthesis' | 'SpeechSynthesisUtterance'): void {
    Object.defineProperty(window, missingApi, {
      configurable: true,
      value: undefined
    });
  }

  beforeEach(async () => {
    originalSpeechSynthesisDescriptor = Object.getOwnPropertyDescriptor(window, 'speechSynthesis');
    originalUtteranceDescriptor = Object.getOwnPropertyDescriptor(window, 'SpeechSynthesisUtterance');

    await TestBed.configureTestingModule({
      imports: [WordLookupComponent, HttpClientTestingModule, RouterTestingModule]
    })
      .compileComponents();

    fixture = TestBed.createComponent(WordLookupComponent);
    component = fixture.componentInstance;
    httpTestingController = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpTestingController.verify();
    fixture.destroy();

    if (originalSpeechSynthesisDescriptor) {
      Object.defineProperty(window, 'speechSynthesis', originalSpeechSynthesisDescriptor);
    } else {
      delete (window as any).speechSynthesis;
    }

    if (originalUtteranceDescriptor) {
      Object.defineProperty(window, 'SpeechSynthesisUtterance', originalUtteranceDescriptor);
    } else {
      delete (window as any).SpeechSynthesisUtterance;
    }
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should make pronunciation available when the audio URL is null', () => {
    const speech = installSpeechSynthesisFakes();
    component.searchNewWord('test');
    httpTestingController.expectOne(request => request.url.endsWith('/words/lookup/test')).flush({
      success: true,
      data: {
        success: true,
        word: { text: 'test', audioUrl: null, definitions: [] },
        wasFoundInCache: false,
        isInUserVocabulary: false
      }
    });
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[title="Play pronunciation"]');
    expect(button).not.toBeNull();
    expect(button.disabled).toBeFalse();

    button.click();

    expect(speech.speak).toHaveBeenCalledTimes(1);
    expect(speech.utterances[0].text).toBe('test');
  });

  it('should trim and configure the word for en-US speech', () => {
    const speech = installSpeechSynthesisFakes();

    component.speakWord('  example  ');

    expect(speech.constructorArguments).toEqual(['example']);
    expect(speech.utterances[0].text).toBe('example');
    expect(speech.utterances[0].lang).toBe('en-US');
    expect(speech.utterances[0].rate).toBe(0.95);
    expect(speech.utterances[0].pitch).toBe(1);
    expect(speech.utterances[0].volume).toBe(1);
    expect(speech.utterances[0].voice).toBeNull();
  });

  it('should cancel existing speech before each pronunciation', () => {
    const speech = installSpeechSynthesisFakes();

    component.speakWord('first');
    component.speakWord('second');

    expect(speech.callOrder).toEqual(['cancel', 'speak', 'cancel', 'speak']);
    expect(speech.cancel).toHaveBeenCalledTimes(2);
    expect(speech.speak).toHaveBeenCalledTimes(2);
  });

  it('should disable pronunciation when speech synthesis is unsupported', () => {
    setUnsupportedBrowser('speechSynthesis');
    component.currentWord = {
      word: 'silent',
      source: 'canonical',
      partOfSpeechGroups: []
    };
    fixture.detectChanges();

    expect(() => component.speakWord('silent')).not.toThrow();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector(
      'button[title="Pronunciation is not supported by this browser"]');
    expect(button).not.toBeNull();
    expect(button.disabled).toBeTrue();
  });

  it('should not speak when the utterance constructor is unsupported', () => {
    const speech = installSpeechSynthesisFakes();
    setUnsupportedBrowser('SpeechSynthesisUtterance');

    expect(() => component.speakWord('silent')).not.toThrow();
    expect(speech.cancel).not.toHaveBeenCalled();
    expect(speech.speak).not.toHaveBeenCalled();
  });

  it('should not create or speak an utterance for an empty word', () => {
    const speech = installSpeechSynthesisFakes();

    component.speakWord(undefined);
    component.speakWord(null);
    component.speakWord('');
    component.speakWord('   ');

    expect(speech.constructorArguments).toEqual([]);
    expect(speech.cancel).not.toHaveBeenCalled();
    expect(speech.speak).not.toHaveBeenCalled();
  });

  it('should handle a synchronous speech failure without changing the current word', () => {
    const speech = installSpeechSynthesisFakes();
    speech.speak.and.throwError('failed');
    spyOn(console, 'error');
    spyOn(component.toastService, 'error');
    component.currentWord = {
      word: 'test',
      source: 'canonical',
      partOfSpeechGroups: []
    };

    expect(() => component.speakWord(component.currentWord?.word)).not.toThrow();

    expect(component.currentWord.word).toBe('test');
    expect(component.toastService.error).toHaveBeenCalledWith('Pronunciation audio is unavailable.');
  });

  it('should cancel speech when the component is destroyed', () => {
    const speech = installSpeechSynthesisFakes();

    component.ngOnDestroy();

    expect(speech.cancel).toHaveBeenCalled();
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
    httpTestingController
      .expectOne(request => request.url.includes('/words/vocabulary/search?term='))
      .flush({ success: true, data: { words: [] } });

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

  it('should treat an idempotent duplicate add response as success', () => {
    component.currentWord = {
      word: 'run',
      source: 'canonical',
      partOfSpeechGroups: [{
        partOfSpeech: 'noun',
        priority: 1,
        definitions: [{ id: 11, definition: 'A run' }],
        isExpanded: false,
        primaryDefinitions: []
      }]
    };

    component.addToVocabulary();
    const request = httpTestingController.expectOne(request =>
      request.url.endsWith('/words/vocabulary/add'));
    request.flush({
      success: true,
      data: { userWordId: 7, wordId: 3, alreadyExisted: true, message: 'Word already in your vocabulary' }
    });

    expect(component.wordAddedToVocabulary).toBeTrue();
    expect(component.currentWord.source).toBe('user');
  });

  it('should update the same vocabulary item definition and part of speech', () => {
    const item = {
      id: 7,
      word: 'run',
      definition: 'A noun definition',
      preferredWordDefinitionId: 11,
      partOfSpeech: 'noun',
      addedAt: '',
      isFavorite: true,
      correctAnswers: 2,
      totalAttempts: 4
    };
    component.vocabularyResponse = {
      words: [item], totalCount: 1, page: 1, pageSize: 20, totalPages: 1
    };
    component.definitionEditorWord = item;
    component.activeVocabularyWord = item;
    component.definitionOptions = [{
      id: 12,
      definition: 'A verb definition',
      partOfSpeech: 'verb'
    }];
    component.selectedPreferredDefinitionId = 12;

    component.savePreferredDefinition();
    const request = httpTestingController.expectOne(request =>
      request.url.endsWith('/words/vocabulary/7/preferred-definition'));
    request.flush({ success: true, data: { userWordId: 7, preferredWordDefinitionId: 12 } });

    expect(item.id).toBe(7);
    expect(item.preferredWordDefinitionId).toBe(12);
    expect(item.definition).toBe('A verb definition');
    expect(item.partOfSpeech).toBe('verb');
    expect(item.isFavorite).toBeTrue();
  });
});
