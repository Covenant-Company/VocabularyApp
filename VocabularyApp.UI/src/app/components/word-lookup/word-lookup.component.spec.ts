import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { WordLookupComponent } from './word-lookup.component';
import { currentLookupSuccess, currentVocabularySuccess } from '../../testing/api-contract.fixtures';
import { DICTIONARY_UNAVAILABLE, INTERNAL_ERROR } from '../../services/api-error';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../services/auth.service';

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
    spyOn(localStorage, 'getItem').and.returnValue(null);
    originalSpeechSynthesisDescriptor = Object.getOwnPropertyDescriptor(window, 'speechSynthesis');
    originalUtteranceDescriptor = Object.getOwnPropertyDescriptor(window, 'SpeechSynthesisUtterance');

    await TestBed.configureTestingModule({
      imports: [WordLookupComponent, HttpClientTestingModule, RouterTestingModule]
    })
      .compileComponents();

    fixture = TestBed.createComponent(WordLookupComponent);
    component = fixture.componentInstance;
    httpTestingController = TestBed.inject(HttpTestingController);
    spyOn(TestBed.inject(AuthService), 'getToken').and.returnValue('test-bearer');
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
    httpTestingController.expectOne(request => request.url.endsWith('/words/lookup/test')).flush(currentLookupSuccess());
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
      .flush({ success: true, data: { words: [], totalCount: 0, page: 1, pageSize: 5, totalPages: 0 } });

    // Assert: Current word and related data should be cleared
    expect(component.currentWord).toBeNull();
    expect(component.sortedGroups).toEqual([]);
    expect(component.errorMessage).toBe('');
  });

  it('should compute letter availability and counts from the loaded vocabulary page', () => {
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
    expect(component.getLetterTooltip('A', 0)).toBe('0 words start with "A" on this page');
    expect(component.getLetterTooltip('G', 1)).toBe('1 word starts with "G" on this page');
    expect(component.getLetterTooltip('P', 2)).toBe('2 words start with "P" on this page');
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
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ word: 'run', definition: 'A run', partOfSpeech: 'noun', example: '', preferredWordDefinitionId: 11 });
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
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
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ preferredWordDefinitionId: 12 });
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    request.flush({ success: true, data: { message: 'Preferred definition updated', userWordId: 7, preferredWordDefinitionId: 12 } });

    expect(item.id).toBe(7);
    expect(item.preferredWordDefinitionId).toBe(12);
    expect(item.definition).toBe('A verb definition');
    expect(item.partOfSpeech).toBe('verb');
    expect(item.isFavorite).toBeTrue();
  });
  const lookupFailures = [
    { status: 400, body: { error: 'Word is required.' }, message: 'Word is required.' },
    { status: 401, body: null, message: 'Please sign in to continue.' },
    { status: 404, body: { error: 'No definitions found.' }, message: 'Word not found. Please check the spelling and try again.' },
    { status: 503, body: { error: 'SECRET provider data' }, message: DICTIONARY_UNAVAILABLE },
    { status: 500, body: { error: 'SECRET exception' }, message: INTERNAL_ERROR }
  ];
  for (const test of lookupFailures) {
    it(`normalizes lookup HTTP ${test.status} and does not expose diagnostics`, () => {
      component.searchNewWord('test');
      const request = httpTestingController.expectOne(environment.apiUrl + '/words/lookup/test');
      expect(request.request.method).toBe('GET');
      request.flush(test.body, { status: test.status, statusText: 'Failure' });
      fixture.detectChanges();
      expect(component.errorMessage).toBe(test.message);
      expect(fixture.nativeElement.textContent).not.toContain('SECRET');
      expect(component.currentWord).toBeNull();
      expect(component.isLoading).toBeFalse();
    });
  }

  function prepareAdd(): void {
    component.currentWord = { word: 'test', source: 'canonical', partOfSpeechGroups: [{
      partOfSpeech: 'noun', priority: 1, definitions: [{ id: 11, definition: 'A trial' }],
      isExpanded: false, primaryDefinitions: []
    }] };
  }
  for (const status of [400, 500]) {
    it(`handles vocabulary add HTTP ${status} without marking the word saved`, () => {
      const error = spyOn(component.toastService, 'error');
      prepareAdd();
      component.addToVocabulary();
      const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/add');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ word: 'test', definition: 'A trial', partOfSpeech: 'noun', example: '', preferredWordDefinitionId: 11 });
      request.flush({ error: 'Selected definition is not valid for this word.' }, { status, statusText: 'Failure' });
      expect(error).toHaveBeenCalledWith(status === 500 ? INTERNAL_ERROR : 'Selected definition is not valid for this word.');
      expect(component.wordAddedToVocabulary).toBeFalse();
      expect(component.currentWord?.source).toBe('canonical');
    });
  }
  for (const body of [{ success: false }, { success: true, data: null }, { success: true, data: { message: 'fictional success' } }]) {
    it('rejects malformed add acknowledgements without a success toast', () => {
      spyOn(component.toastService, 'error');
      const success = spyOn(component.toastService, 'success');
      prepareAdd();
      component.addToVocabulary();
      httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/add').flush(body);
      expect(success).not.toHaveBeenCalled();
      expect(component.wordAddedToVocabulary).toBeFalse();
    });
  }
  for (const status of [401, 404, 500]) {
    it(`rolls back optimistic favorite state on HTTP ${status}`, () => {
      const error = spyOn(component.toastService, 'error');
      const item = { id: 7, word: 'test', definition: 'A trial', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 };
      component.toggleFavorite(item, new Event('click'));
      expect(item.isFavorite).toBeTrue();
      const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/7/favorite');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ isFavorite: true });
      request.flush(null, { status, statusText: 'Failure' });
      expect(item.isFavorite).toBeFalse();
      expect(error).toHaveBeenCalledWith(status === 500 ? INTERNAL_ERROR : status === 404 ? 'This word is unavailable in your vocabulary.' : 'Please sign in to continue.');
    });
  }
  it('rolls back optimistic favorite state on a malformed acknowledgement', () => {
    spyOn(component.toastService, 'error');
    const success = spyOn(component.toastService, 'success');
    const item = { id: 7, word: 'test', definition: 'A trial', partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 };
    component.toggleFavorite(item, new Event('click'));
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/7/favorite').flush({ success: true, data: {} });
    expect(item.isFavorite).toBeFalse();
    expect(success).not.toHaveBeenCalled();
  });
  for (const status of [404, 500]) {
    it(`keeps the preferred-definition editor open on HTTP ${status}`, () => {
      const error = spyOn(component.toastService, 'error');
      const success = spyOn(component.toastService, 'success');
      const item = { id: 7, word: 'test', definition: 'A trial', preferredWordDefinitionId: 11, partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 };
      component.definitionEditorWord = item;
      component.showDefinitionEditor = true;
      component.selectedPreferredDefinitionId = 12;
      const options = [{ id: 12, definition: 'Another meaning', partOfSpeech: 'Verb' }];
      component.definitionOptions = options;
      component.savePreferredDefinition();
      const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/7/preferred-definition');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ preferredWordDefinitionId: 12 });
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      request.flush({ code: 'vocabulary_not_found', error: 'SECRET' }, { status, statusText: 'Failure' });
      expect(error).toHaveBeenCalledWith(status === 500 ? INTERNAL_ERROR : 'This word is unavailable in your vocabulary.');
      expect(component.showDefinitionEditor).toBeTrue();
      expect(component.definitionEditorSaving).toBeFalse();
      expect(component.definitionEditorWord).toBe(item);
      expect(component.selectedPreferredDefinitionId).toBe(12);
      expect(component.definitionOptions).toBe(options);
      expect(success).not.toHaveBeenCalled();
      expect(item.preferredWordDefinitionId).toBe(11);
    });
  }
  it('preserves stale vocabulary and renders a list-load error instead of empty state', () => {
    component.showVocabularyList = true;
    component.loadVocabularyPage(1);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000').flush(currentVocabularySuccess(1000));
    const previous = component.vocabularyResponse;
    const letter = component.selectedVocabularyLetter;
    component.vocabularySearchQuery = 'trial';
    component.loadVocabularyPage(2);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=2&pageSize=1000').flush({ error: 'SECRET' }, { status: 500, statusText: 'Failure' });
    fixture.detectChanges();
    expect(component.vocabularyResponse).toBe(previous);
    expect(component.selectedVocabularyLetter).toBe(letter);
    expect(component.vocabularySearchQuery).toBe('trial');
    expect(component.vocabularyError).toBe(INTERNAL_ERROR);
    expect(fixture.nativeElement.textContent).toContain('Showing the previously loaded vocabulary.');
    expect(fixture.nativeElement.textContent).not.toContain('SECRET');
    expect(fixture.nativeElement.textContent).not.toContain('No words in your vocabulary yet');
    expect(fixture.nativeElement.textContent).toContain('test');
  });
  it('keeps the preferred editor unchanged on a malformed success acknowledgement', () => {
    spyOn(component.toastService, 'error');
    const success = spyOn(component.toastService, 'success');
    const item = { id: 7, word: 'test', definition: 'A trial', preferredWordDefinitionId: 11, partOfSpeech: 'Noun', addedAt: '', isFavorite: false, correctAnswers: 0, totalAttempts: 0 };
    component.definitionEditorWord = item;
    component.showDefinitionEditor = true;
    component.selectedPreferredDefinitionId = 12;
    component.savePreferredDefinition();
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/7/preferred-definition').flush({ success: true, data: {} });
    expect(component.showDefinitionEditor).toBeTrue();
    expect(component.definitionEditorSaving).toBeFalse();
    expect(item.preferredWordDefinitionId).toBe(11);
    expect(success).not.toHaveBeenCalled();
  });
  it('does not fabricate an empty list after a malformed initial response', () => {
    component.showVocabularyList = true;
    component.loadVocabularyPage(1);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000').flush({ success: true, data: {} });
    fixture.detectChanges();
    expect(component.vocabularyResponse).toBeNull();
    expect(component.vocabularyError).toBe('Unable to load your vocabulary.');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(component.vocabularyError);
  });
  it('reports autocomplete failure while retaining the dictionary-search option', () => {
    component.searchUserVocabulary('a & b');
    const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/search?term=a%20%26%20b');
    request.flush({ error: 'SECRET' }, { status: 500, statusText: 'Failure' });
    expect(component.errorMessage).toBe(INTERNAL_ERROR);
    expect(component.suggestions).toEqual([{ word: 'a & b', type: 'new-search', action: 'Search dictionary' }]);
  });
  it('maps nullable wire fields explicitly without losing zero counters', () => {
    component.loadVocabularyPage(1);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000').flush(currentVocabularySuccess(1000));
    const item = component.vocabularyResponse!.words[0];
    expect(item.audioUrl).toBeUndefined();
    expect(item.example).toBeUndefined();
    expect(item.accuracyRate).toBeUndefined();
    expect(item.totalAttempts).toBe(0);
    expect(item.preferredWordDefinitionId).toBe(11);
  });

  it('marks a newly added word saved after the typed acknowledgement', () => {
    prepareAdd();
    const success = spyOn(component.toastService, 'success');
    component.addToVocabulary();
    const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/add');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ word: 'test', definition: 'A trial', partOfSpeech: 'noun', example: '', preferredWordDefinitionId: 11 });
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    request.flush({ success: true, data: { userWordId: 7, wordId: 3, alreadyExisted: false, message: 'Word added to your vocabulary' } });
    expect(component.wordAddedToVocabulary).toBeTrue();
    expect(component.currentWord?.source).toBe('user');
    expect(success).toHaveBeenCalled();
  });

  for (const initial of [true, false]) {
    it(`confirms favorite toggle from ${initial} with an explicit boolean request`, () => {
      const item = { ...currentVocabularySuccess().data.words[0], isFavorite: initial, preferredWordDefinitionId: 11,
        example: undefined, pronunciation: undefined, audioUrl: undefined, personalNotes: undefined, accuracyRate: undefined };
      component.toggleFavorite(item, new Event('click'));
      expect(item.isFavorite).toBe(!initial);
      const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/7/favorite');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ isFavorite: !initial });
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      request.flush({ success: true, data: { userWordId: 7, isFavorite: !initial, message: 'Favorite updated' } });
      expect(item.isFavorite).toBe(!initial);
    });
  }

  it('renders genuine empty vocabulary only after a successful zero-item response', () => {
    component.showVocabularyList = true;
    component.loadVocabularyPage(1);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000')
      .flush({ success: true, data: { words: [], totalCount: 0, page: 1, pageSize: 1000, totalPages: 0 } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No words in your vocabulary yet');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('renders initial load failure without claiming the vocabulary is empty', () => {
    component.showVocabularyList = true;
    component.loadVocabularyPage(1);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000')
      .flush({ error: 'SECRET' }, { status: 500, statusText: 'Failure' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(INTERNAL_ERROR);
    expect(fixture.nativeElement.textContent).not.toContain('No words in your vocabulary yet');
    expect(component.vocabularyResponse).toBeNull();
  });

  it('labels filtering and counts as page-local and uses existing navigation beyond 1000 words', () => {
    component.showVocabularyList = true;
    component.loadVocabularyPage(1);
    const first = currentVocabularySuccess(1000);
    const item = first.data.words[0];
    first.data.words = Array.from({ length: 1000 }, (_, index) => ({ ...item, id: index + 1, word: `alpha${index}` }));
    first.data.totalCount = 1001;
    first.data.totalPages = 2;
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000').flush(first);
    component.vocabularySearchQuery = 'zebra';
    fixture.detectChanges();
    expect(component.filteredVocabularyWords).toEqual([]);
    expect(component.getWordCountForLetter('A')).toBe(1000);
    expect(component.getWordCountForLetter('Z')).toBe(0);
    expect(fixture.nativeElement.textContent).toContain('No matches on this page');
    expect(fixture.nativeElement.textContent).toContain('Search and letter counts apply to the current page');
    expect(fixture.nativeElement.textContent).toContain('1001 words total');
    expect(fixture.nativeElement.querySelector('input[placeholder="Search words on this page"]')).not.toBeNull();
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
    buttons.find(button => button.textContent?.includes('Next'))!.click();
    const second = currentVocabularySuccess(1000);
    second.data.words = [{ ...item, id: 1001, word: 'zebra' }];
    second.data.page = 2;
    second.data.totalCount = 1001;
    second.data.totalPages = 2;
    const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=2&pageSize=1000');
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    request.flush(second);
    fixture.detectChanges();
    expect(component.filteredVocabularyWords.map(word => word.word)).toEqual(['zebra']);
    expect(component.getWordCountForLetter('A')).toBe(0);
    expect(component.getWordCountForLetter('Z')).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Page 2 of 2');
  });

  it('restores an existing favorite after a failed optimistic removal', () => {
    const error = spyOn(component.toastService, 'error');
    const success = spyOn(component.toastService, 'success');
    const item = { id: 7, word: 'test', definition: 'A trial', partOfSpeech: 'Noun', addedAt: '', isFavorite: true, correctAnswers: 0, totalAttempts: 0 };
    component.toggleFavorite(item, new Event('click'));
    expect(item.isFavorite).toBeFalse();
    const request = httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary/7/favorite');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ isFavorite: false });
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    request.flush({ error: 'SECRET' }, { status: 500, statusText: 'Failure' });
    expect(item.isFavorite).toBeTrue();
    expect(error).toHaveBeenCalledWith(INTERNAL_ERROR);
    expect(success).not.toHaveBeenCalled();
    httpTestingController.expectNone(environment.apiUrl + '/words/vocabulary/7/favorite');
  });

  it('preserves loaded vocabulary after a malformed refresh acknowledgement', () => {
    component.showVocabularyList = true;
    component.loadVocabularyPage(1);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000').flush(currentVocabularySuccess(1000));
    const previous = component.vocabularyResponse;
    const letter = component.selectedVocabularyLetter;
    component.loadVocabularyPage(1);
    httpTestingController.expectOne(environment.apiUrl + '/words/vocabulary?page=1&pageSize=1000')
      .flush({ success: true, data: { words: [] } });
    fixture.detectChanges();
    expect(component.vocabularyResponse).toBe(previous);
    expect(component.selectedVocabularyLetter).toBe(letter);
    expect(component.vocabularyLoading).toBeFalse();
    expect(component.vocabularyError).toBe('Unable to load your vocabulary.');
    expect(fixture.nativeElement.textContent).toContain('Showing the previously loaded vocabulary.');
    expect(fixture.nativeElement.textContent).not.toContain('No words in your vocabulary yet');
  });
});
