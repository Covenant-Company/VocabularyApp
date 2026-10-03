import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { Router } from '@angular/router';
import { Definition, WordLookupResult, PartOfSpeechGroup, SearchSuggestion, POS_PRIORITY, VocabularyResponse, VocabularyItem } from '../../models/word-lookup.model';
import { ToastService } from '../../services/toast.service';
import { normalizeApiError } from '../../services/api-error';
import { AddToVocabularyResult, AddWordRequest, FavoriteRequest, FavoriteResponse, PreferredDefinitionRequest, PreferredDefinitionResponse,
  VocabularyItemDto, VocabularyResponseDto, WordLookupResponse, wordLookupPath, vocabularySearchPath } from '../../models/word-api.model';

function vocabularyView(item: VocabularyItemDto): VocabularyItem {
  return { ...item, preferredWordDefinitionId: item.preferredWordDefinitionId ?? undefined,
    example: item.example ?? undefined, pronunciation: item.pronunciation ?? undefined,
    audioUrl: item.audioUrl ?? undefined, personalNotes: item.personalNotes ?? undefined,
    accuracyRate: item.accuracyRate ?? undefined };
}

interface DefinitionOption {
  id: number;
  definition: string;
  example?: string;
  partOfSpeech: string;
  displayOrder?: number;
}

@Component({
  selector: 'app-word-lookup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './word-lookup.component.html',
  styleUrl: './word-lookup.component.scss'
})
export class WordLookupComponent implements OnInit, OnDestroy {
  readonly alphabetLetters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('');

  searchTerm = '';
  suggestions: SearchSuggestion[] = [];
  selectedSuggestionIndex = -1;
  isLoading = false;
  errorMessage = '';

  currentWord: WordLookupResult | null = null;
  sortedGroups: PartOfSpeechGroup[] = [];
  wordAddedToVocabulary = false; // Track if current word was just added
  viewingFromVocabularyList = false;

  // Vocabulary list properties
  showVocabularyList = false;
  vocabularyLoading = false;
  vocabularyError = '';
  vocabularyResponse: VocabularyResponse | null = null;
  vocabularySearchQuery = ''; // Search query for filtering vocabulary list
  selectedVocabularyLetter: string | null = null;
  definitionHighlightTerm = '';
  private vocabularyNeedsRefresh = false;

  showDefinitionEditor = false;
  definitionEditorLoading = false;
  definitionEditorSaving = false;
  definitionEditorWord: VocabularyItem | null = null;
  definitionOptions: DefinitionOption[] = [];
  selectedPreferredDefinitionId: number | null = null;
  activeVocabularyWord: VocabularyItem | null = null;
  constructor(private apiService: ApiService, private router: Router, public toastService: ToastService) { }

  backToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }

  goToQuiz(): void {
    this.router.navigate(['/quiz']);
  }

  ngOnInit(): void { }

  ngOnDestroy(): void {
    this.cancelSpeech();
  }

  get isSpeechSynthesisSupported(): boolean {
    return typeof window !== 'undefined'
      && 'speechSynthesis' in window
      && !!window.speechSynthesis
      && typeof window.SpeechSynthesisUtterance === 'function';
  }

  onSearchInput(): void {
    this.definitionHighlightTerm = '';

    this.cancelSpeech();

    // Clear previous word definition as soon as user starts typing
    if (this.currentWord) {
      this.currentWord = null;
      this.sortedGroups = [];
      this.errorMessage = '';
    }

    if (this.searchTerm.length >= 2) {
      this.searchUserVocabulary(this.searchTerm);
    } else {
      this.suggestions = [];
    }
  }

  searchUserVocabulary(term: string): void {
    this.errorMessage = '';
    // Search user's vocabulary for autocomplete suggestions
    this.apiService.get<VocabularyResponseDto>(vocabularySearchPath(term)).subscribe({
      next: (res) => {
        this.suggestions = [];

        // Add existing words from user's vocabulary
        if (res?.success && res.data && Array.isArray(res.data.words)) {
          const existingSuggestions = res.data.words.slice(0, 5).map(item => ({
            word: item.word,
            type: 'existing' as const,
            partOfSpeech: item.partOfSpeech || 'unknown',
            preview: item.definition?.substring(0, 60) || '',
            action: 'Review word'
          }));
          this.suggestions.push(...existingSuggestions);
        } else {
          this.errorMessage = 'Unable to search your vocabulary.';
        }

        // Always add option to search dictionary
        this.suggestions.push({
          word: term,
          type: 'new-search',
          action: 'Search dictionary'
        });
      },
      error: (err) => {
        this.errorMessage = normalizeApiError(err, 'vocabulary-search', 'Unable to search your vocabulary.').message;
        // On error, just show search dictionary option
        this.suggestions = [
          {
            word: term,
            type: 'new-search',
            action: 'Search dictionary'
          }
        ];
      }
    });
  }

  selectSuggestion(suggestion: SearchSuggestion): void {
    if (suggestion.type === 'existing') {
      // Use searchNewWord for existing words too to get full definitions
      this.searchNewWord(suggestion.word);
    } else {
      this.searchNewWord(suggestion.word);
    }
    this.suggestions = [];
  }

  viewExistingWord(word: string): void {
    // Fetch word from user's vocabulary using search endpoint
    this.cancelSpeech();
    this.isLoading = true;
    this.errorMessage = '';
    this.currentWord = null;

    this.apiService.get<VocabularyResponseDto>(vocabularySearchPath(word)).subscribe({
      next: (res) => {
        if (res?.success && res.data && Array.isArray(res.data.words) && res.data.words.length > 0) {
          // Find the exact match (case-insensitive)
          const userWord = res.data.words.find(w => w.word.toLowerCase() === word.toLowerCase()) || res.data.words[0];
          // Map the user's vocabulary word to WordLookupResult format
          const mapped: WordLookupResult = {
            word: userWord.word,
            phonetic: userWord.pronunciation ?? undefined,
            audioUrl: userWord.audioUrl ?? undefined,
            partOfSpeechGroups: [
              {
                partOfSpeech: userWord.partOfSpeech || 'unknown',
                priority: 1,
                definitions: [
                  {
                    definition: userWord.definition || '',
                    example: userWord.example || ''
                  }
                ],
                isExpanded: false,
                primaryDefinitions: [
                  {
                    definition: userWord.definition || '',
                    example: userWord.example || ''
                  }
                ]
              }
            ],
            source: 'user'
          };
          this.currentWord = mapped;
          this.processWordResult(this.currentWord);
        } else {
          this.errorMessage = 'Word not found in your vocabulary.';
        }
        this.isLoading = false;
      },
      error: (err) => {
        this.errorMessage = normalizeApiError(err, 'vocabulary-search', 'Failed to load word from your vocabulary.').message;
        this.isLoading = false;
      }
    });
  }

  searchNewWord(word: string, fromVocabularyList = false): void {
    this.cancelSpeech();
    this.isLoading = true;
    this.errorMessage = '';
    this.currentWord = null;
    this.suggestions = []; // Clear suggestions to show error message if search fails
    this.wordAddedToVocabulary = false; // Reset flag for new word
    if (!fromVocabularyList) {
      this.activeVocabularyWord = null;
    }
    if (!fromVocabularyList) {
      this.definitionHighlightTerm = '';
    }
    this.viewingFromVocabularyList = fromVocabularyList;
    // Use the lookup endpoint which returns full definitions
    this.apiService.get<WordLookupResponse>(wordLookupPath(word)).subscribe({
      next: (res) => {
        try {
          if (res?.success && res.data?.success) {
            // Backend wraps WordLookupResponse inside ApiResponse.Data
            const lookupResp = res.data;
            const wordDto = lookupResp.word; // WordDto
            if (wordDto && typeof wordDto.text === 'string' && Array.isArray(wordDto.definitions)) {
              // Map WordDto -> UI WordLookupResult shape
              const mapped: WordLookupResult = {
                word: wordDto.text || word,
                phonetic: wordDto.pronunciation ?? undefined,
                audioUrl: wordDto.audioUrl ?? undefined,
                source: lookupResp.isInUserVocabulary ? 'user' : (lookupResp.wasFoundInCache ? 'canonical' : 'external'),
                partOfSpeechGroups: []
              };

              // Group definitions by part of speech
              const groupsMap: Record<string, PartOfSpeechGroup> = {};
              for (const def of (wordDto.definitions || [])) {
                const pos = (def.partOfSpeech || 'unknown').toLowerCase();
                if (!groupsMap[pos]) {
                  groupsMap[pos] = {
                    partOfSpeech: pos,
                    priority: POS_PRIORITY[pos as keyof typeof POS_PRIORITY] ?? 99,
                    definitions: [],
                    isExpanded: false,
                    primaryDefinitions: []
                  };
                }

                const d: Definition = {
                  id: def.id,
                  definition: def.definition,
                  example: def.example ?? undefined
                };

                groupsMap[pos].definitions.push(d);
              }

              // Build groups array and compute primaryDefinitions
              mapped.partOfSpeechGroups = Object.values(groupsMap).map(g => {
                g.primaryDefinitions = this.prioritizeDefinitions(g.definitions);
                return g;
              });

              this.currentWord = mapped;
              this.processWordResult(this.currentWord);
              this.searchTerm = ''; // Clear search input after successful lookup
            } else {
              this.errorMessage = 'Unable to load word definitions.';
            }
          } else {
            this.errorMessage = 'Unable to load word definitions.';
          }
        } catch (ex) {
          this.errorMessage = 'Failed to process word definition.';
        }

        this.isLoading = false;
      },
      error: (err) => {
        this.errorMessage = normalizeApiError(err, 'lookup', 'Unable to load word definitions.').message;

        this.isLoading = false;
      }
    });
  }

  onSearchSubmit(): void {
    if (this.searchTerm.trim()) {
      this.searchNewWord(this.searchTerm.trim());
    }
  }

  toggleExpandGroup(group: PartOfSpeechGroup): void {
    group.isExpanded = !group.isExpanded;
  }

  hasExistingSuggestions(): boolean {
    return this.suggestions.some(s => s.type === 'existing');
  }

  onKeyUp(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      this.onSearchSubmit();
    }
  }

  private processWordResult(result: WordLookupResult): void {
    // Process and sort the word definition groups by priority
    this.sortedGroups = result.partOfSpeechGroups
      .sort((a: PartOfSpeechGroup, b: PartOfSpeechGroup) => {
        const priorityA = POS_PRIORITY[a.partOfSpeech as keyof typeof POS_PRIORITY] || 99;
        const priorityB = POS_PRIORITY[b.partOfSpeech as keyof typeof POS_PRIORITY] || 99;
        return priorityA - priorityB;
      });
  }

  private prioritizeDefinitions(definitions: Definition[]): Definition[] {
    return definitions
      .sort((a, b) => {
        // Prioritize definitions with examples
        if (a.example && !b.example) return -1;
        if (!a.example && b.example) return 1;


        // Then by length (shorter = more common)
        return a.definition.length - b.definition.length;
      })
      .slice(0, 2); // Show top 2 initially
  }

  // Add this method inside the WordLookupComponent class
  addToVocabulary(): void {
    if (!this.currentWord) {
      console.warn('No current word to add');
      return;
    }

    // Preserve the legacy payload; definition/example do not author canonical data.
    const firstDef = this.currentWord.partOfSpeechGroups?.[0]?.definitions?.[0];
    const payload: AddWordRequest = {
      word: this.currentWord.word,
      definition: firstDef?.definition ?? '',
      partOfSpeech: this.currentWord.partOfSpeechGroups?.[0]?.partOfSpeech ?? '',
      example: firstDef?.example ?? '',
      preferredWordDefinitionId: firstDef?.id ?? null
    };

    // Use your ApiService post helper (see next section). Endpoint path is appended to baseUrl.
    this.apiService.post<AddToVocabularyResult, AddWordRequest>('/words/vocabulary/add', payload).subscribe({
      next: (res) => {
        if (!res?.success || !res.data || !Number.isInteger(res.data.userWordId) || res.data.userWordId <= 0
          || !Number.isInteger(res.data.wordId) || res.data.wordId <= 0 || typeof res.data.alreadyExisted !== 'boolean'
          || typeof res.data.message !== 'string') {
          this.toastService.error('Unable to confirm the vocabulary update.');
          return;
        }
        // show user feedback with toast
        const message = res.data?.alreadyExisted
          ? `Word "${this.currentWord?.word}" is already in your vocabulary.`
          : `Word "${this.currentWord?.word}" added to your vocabulary!`;
        this.toastService.success(message);
        // Set flag to disable the button
        this.wordAddedToVocabulary = true;
        if (this.currentWord) {
          this.currentWord.source = 'user';
        }
        this.vocabularyNeedsRefresh = true;
      },
      error: (err) => {
        const msg = normalizeApiError(err, 'vocabulary-add', 'Failed to add word').message;
        this.toastService.error(msg);
      }
    });
  }

  // Vocabulary list methods
  toggleVocabularyView(): void {
    this.showVocabularyList = !this.showVocabularyList;
    if (this.showVocabularyList && (!this.vocabularyResponse || this.vocabularyNeedsRefresh)) {
      this.loadVocabularyPage(1);
    }
    // Clear current word and search term when switching views
    if (this.showVocabularyList) {
      this.currentWord = null;
      this.errorMessage = '';
      this.searchTerm = '';
      this.vocabularySearchQuery = ''; // Clear vocabulary search
      this.definitionHighlightTerm = '';
      this.viewingFromVocabularyList = false;
      this.activeVocabularyWord = null;
    } else {
      // Also clear when switching back to lookup view
      this.searchTerm = '';
      this.errorMessage = '';
      this.definitionHighlightTerm = '';
      this.viewingFromVocabularyList = false;
      this.activeVocabularyWord = null;
    }
  }

  loadVocabularyPage(page: number): void {
    if (page < 1) return;

    this.vocabularyLoading = true;
    this.vocabularyError = '';
    this.apiService.get<VocabularyResponseDto>(`/words/vocabulary?page=${page}&pageSize=1000`).subscribe({
      next: (res) => {
        if (res?.success && res.data && Array.isArray(res.data.words)
          && Number.isFinite(res.data.totalCount) && Number.isFinite(res.data.page)
          && Number.isFinite(res.data.pageSize) && Number.isFinite(res.data.totalPages)) {
          this.vocabularyResponse = { ...res.data, words: res.data.words.map(vocabularyView) };
          this.ensureSelectedLetterIsValid();
          this.vocabularyNeedsRefresh = false;
        } else {
          this.vocabularyError = 'Unable to load your vocabulary.';
          this.vocabularyNeedsRefresh = true;
        }
        this.vocabularyLoading = false;
      },
      error: (err) => {
        this.vocabularyError = normalizeApiError(err, 'vocabulary-list', 'Unable to load your vocabulary.').message;
        this.vocabularyLoading = false;
        this.vocabularyNeedsRefresh = true;
      }
    });
  }

  speakWord(word?: string | null): void {
    const text = word?.trim();
    if (!text || !this.isSpeechSynthesisSupported) {
      return;
    }

    try {
      const synth = window.speechSynthesis;
      synth.cancel();
      const utterance = new window.SpeechSynthesisUtterance(text);
      utterance.lang = 'en-US';
      utterance.rate = 0.95;
      synth.speak(utterance);
    } catch (error) {
      console.error('Speech synthesis failed:', error);
      this.toastService.error('Pronunciation audio is unavailable.');
    }
  }

  private cancelSpeech(): void {
    if (!this.isSpeechSynthesisSupported) {
      return;
    }

    try {
      window.speechSynthesis.cancel();
    } catch (error) {
      console.error('Failed to cancel speech synthesis:', error);
    }
  }

  viewWordDetails(word: VocabularyItem): void {
    this.definitionHighlightTerm = this.vocabularySearchQuery.trim();
    this.activeVocabularyWord = word;

    // Hide vocabulary list and show word details
    this.showVocabularyList = false;
    this.vocabularySearchQuery = '';
    this.searchTerm = word.word;
    // Fetch the full word details using the lookup endpoint
    this.searchNewWord(word.word, true);
  }

  buildDefinitionOptions(definitions: { id: number; definition: string; example?: string | null; partOfSpeech: string; displayOrder?: number }[]): DefinitionOption[] {
    const mappedDefinitions = (definitions || [])
      .filter(d => Number.isFinite(d?.id))
      .map(d => ({
        id: d.id,
        definition: d.definition,
        example: d.example ?? undefined,
        partOfSpeech: d.partOfSpeech,
        displayOrder: d.displayOrder
      }));

    return mappedDefinitions.sort((a, b) => {
      const orderA = a.displayOrder;
      const orderB = b.displayOrder;

      if (orderA !== undefined && orderB !== undefined && orderA !== orderB) {
        return orderA - orderB;
      }

      if (orderA !== undefined && orderB === undefined) {
        return -1;
      }

      if (orderA === undefined && orderB !== undefined) {
        return 1;
      }

      return 0;
    });
  }

  openDefinitionEditor(word: VocabularyItem, event: Event): void {
    event.stopPropagation();

    this.definitionEditorWord = word;
    this.definitionEditorLoading = true;
    this.definitionEditorSaving = false;
    this.definitionOptions = [];
    this.selectedPreferredDefinitionId = null;
    this.showDefinitionEditor = true;

    this.apiService.get<WordLookupResponse>(wordLookupPath(word.word)).subscribe({
      next: (res) => {
        const definitions = res?.success && res.data?.success && res.data.word && Array.isArray(res.data.word.definitions)
          ? res.data.word.definitions : [];

        this.definitionOptions = this.buildDefinitionOptions(definitions);

        this.selectedPreferredDefinitionId =
          word.preferredWordDefinitionId ??
          this.definitionOptions[0]?.id ??
          null;

        if (this.definitionOptions.length === 0) {
          this.toastService.error('No definitions were found for this word.');
          this.closeDefinitionEditor();
        }

        this.definitionEditorLoading = false;
      },
      error: (err) => {
        this.definitionEditorLoading = false;
        this.toastService.error(normalizeApiError(err, 'lookup', 'Failed to load definitions for this word.').message);
        this.closeDefinitionEditor();
      }
    });
  }

  closeDefinitionEditor(): void {
    this.showDefinitionEditor = false;
    this.definitionEditorLoading = false;
    this.definitionEditorSaving = false;
    this.definitionEditorWord = null;
    this.definitionOptions = [];
    this.selectedPreferredDefinitionId = null;
  }

  savePreferredDefinition(): void {
    if (!this.definitionEditorWord || !this.selectedPreferredDefinitionId || this.definitionEditorSaving) {
      return;
    }

    const userWordId = this.definitionEditorWord.id;
    const definitionId = this.selectedPreferredDefinitionId;
    const selectedDefinition = this.definitionOptions.find(option => option.id === definitionId);

    this.definitionEditorSaving = true;
    this.apiService.put<PreferredDefinitionResponse, PreferredDefinitionRequest>(`/words/vocabulary/${userWordId}/preferred-definition`, {
      preferredWordDefinitionId: definitionId
    }).subscribe({
      next: res => {
        if (!res?.success || !res.data || res.data.userWordId !== userWordId || res.data.preferredWordDefinitionId !== definitionId
          || typeof res.data.message !== 'string') {
          this.definitionEditorSaving = false;
          this.toastService.error('Unable to confirm the preferred definition update.');
          return;
        }
        if (this.vocabularyResponse?.words) {
          const target = this.vocabularyResponse.words.find(item => item.id === userWordId);
          if (target) {
            target.preferredWordDefinitionId = definitionId;
            if (selectedDefinition) {
              target.definition = selectedDefinition.definition;
              target.example = selectedDefinition.example;
              target.partOfSpeech = selectedDefinition.partOfSpeech;
            }
          }
        }

        if (this.activeVocabularyWord && this.activeVocabularyWord.id === userWordId) {
          this.activeVocabularyWord.preferredWordDefinitionId = definitionId;
          if (selectedDefinition) {
            this.activeVocabularyWord.partOfSpeech = selectedDefinition.partOfSpeech;
          }
        }

        if (this.definitionEditorWord) {
          this.definitionEditorWord.preferredWordDefinitionId = definitionId;
        }

        this.toastService.success('Preferred quiz definition saved.');
        this.definitionEditorSaving = false;
        this.closeDefinitionEditor();
      },
      error: (err) => {
        this.definitionEditorSaving = false;
        const msg = normalizeApiError(err, 'preferred-definition', 'Failed to save preferred definition').message;
        this.toastService.error(msg);
      }
    });
  }

  toggleFavorite(word: VocabularyItem, event: Event): void {
    event.stopPropagation();

    const newValue = !word.isFavorite;
    const previousValue = word.isFavorite;
    word.isFavorite = newValue;

    this.apiService.put<FavoriteResponse, FavoriteRequest>(`/words/vocabulary/${word.id}/favorite`, { isFavorite: newValue }).subscribe({
      next: res => {
        if (!res?.success || !res.data || res.data.userWordId !== word.id || res.data.isFavorite !== newValue || typeof res.data.message !== 'string') {
          word.isFavorite = previousValue;
          this.toastService.error('Unable to confirm the favorite update.');
          return;
        }
        this.toastService.success(
          newValue ? `"${word.word}" added to favorites` : `"${word.word}" removed from favorites`
        );
      },
      error: (err) => {
        word.isFavorite = previousValue;
        const msg = normalizeApiError(err, 'favorite', 'Failed to update favorite state').message;
        this.toastService.error(msg);
      }
    });
  }

  hasWordsForLetter(letter: string): boolean {
    if (!this.vocabularyResponse?.words?.length) return false;

    const normalizedLetter = letter.toLowerCase();
    return this.vocabularyResponse.words.some(item =>
      (item.word || '').trim().toLowerCase().startsWith(normalizedLetter)
    );
  }

  getWordCountForLetter(letter: string): number {
    if (!this.vocabularyResponse?.words?.length) return 0;

    const normalizedLetter = letter.toLowerCase();
    return this.vocabularyResponse.words.filter(item =>
      (item.word || '').trim().toLowerCase().startsWith(normalizedLetter)
    ).length;
  }

  getLetterTooltip(letter: string, count: number): string {
    const wordLabel = count === 1 ? 'word starts' : 'words start';
    return `${count} ${wordLabel} with "${letter}" on this page`;
  }

  selectVocabularyLetter(letter: string): void {
    if (!this.hasWordsForLetter(letter)) {
      return;
    }

    this.vocabularySearchQuery = '';
    this.selectedVocabularyLetter = letter;
  }

  getHighlightedText(text: string, queryTerm?: string): string {
    const query = (queryTerm ?? this.vocabularySearchQuery).trim();
    if (!query || !text) {
      return this.escapeHtml(text || '');
    }

    const regex = new RegExp(this.escapeRegExp(query), 'ig');
    let highlighted = '';
    let lastIndex = 0;

    for (const match of text.matchAll(regex)) {
      if (match.index === undefined) continue;

      const start = match.index;
      const end = start + match[0].length;

      highlighted += this.escapeHtml(text.slice(lastIndex, start));
      highlighted += `<mark class="search-highlight">${this.escapeHtml(match[0])}</mark>`;
      lastIndex = end;
    }

    highlighted += this.escapeHtml(text.slice(lastIndex));
    return highlighted;
  }

  getVocabularyMatchPreview(word: VocabularyItem): string | null {
    const query = this.vocabularySearchQuery.toLowerCase().trim();
    if (!query) return null;

    const wordText = (word.word || '').toLowerCase();
    if (wordText.includes(query)) {
      return null;
    }

    const definitionText = word.definition || '';
    if (definitionText.toLowerCase().includes(query)) {
      return `Definition: ${definitionText}`;
    }

    const exampleText = word.example || '';
    if (exampleText.toLowerCase().includes(query)) {
      return `Example: ${exampleText}`;
    }

    return null;
  }

  isCurrentPreferredDefinition(definitionId?: number): boolean {
    if (!definitionId || !this.activeVocabularyWord?.preferredWordDefinitionId) {
      return false;
    }

    return this.activeVocabularyWord.preferredWordDefinitionId === definitionId;
  }

  private ensureSelectedLetterIsValid(): void {
    if (!this.vocabularyResponse?.words?.length) {
      this.selectedVocabularyLetter = null;
      return;
    }

    if (this.selectedVocabularyLetter && this.hasWordsForLetter(this.selectedVocabularyLetter)) {
      return;
    }

    this.selectedVocabularyLetter = this.alphabetLetters.find(letter => this.hasWordsForLetter(letter)) ?? null;
  }

  get filteredVocabularyWords() {
    if (!this.vocabularyResponse?.words) return [];

    const query = this.vocabularySearchQuery.toLowerCase().trim();

    // Search takes precedence and matches anywhere in word/definition/example.
    if (query) {
      return this.vocabularyResponse.words.filter(word => {
        const wordText = (word.word || '').toLowerCase();
        const definitionText = (word.definition || '').toLowerCase();
        const exampleText = (word.example || '').toLowerCase();

        return wordText.includes(query) || definitionText.includes(query) || exampleText.includes(query);
      });
    }

    if (!this.selectedVocabularyLetter) {
      return [];
    }

    const selectedLetter = this.selectedVocabularyLetter.toLowerCase();
    return this.vocabularyResponse.words.filter(word =>
      (word.word || '').toLowerCase().startsWith(selectedLetter)
    );
  }

  private escapeRegExp(text: string): string {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  private escapeHtml(text: string): string {
    return text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }
}
