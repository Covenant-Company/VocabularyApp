import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { Router } from '@angular/router';
import { WordLookupResult, PartOfSpeechGroup, SearchSuggestion, POS_PRIORITY, VocabularyResponse, VocabularyItem } from '../../models/word-lookup.model';
import { ToastService } from '../../services/toast.service';

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
export class WordLookupComponent implements OnInit {
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
  private pronunciationAudio: HTMLAudioElement | null = null;

  constructor(private apiService: ApiService, private router: Router, public toastService: ToastService) { }

  backToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }

  goToQuiz(): void {
    this.router.navigate(['/quiz']);
  }

  ngOnInit(): void { }

  onSearchInput(): void {
    this.definitionHighlightTerm = '';

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
    // Search user's vocabulary for autocomplete suggestions
    this.apiService.get<any>(`/words/vocabulary/search?term=${encodeURIComponent(term)}`).subscribe({
      next: (res) => {
        this.suggestions = [];

        // Add existing words from user's vocabulary
        if (res?.data?.words && Array.isArray(res.data.words)) {
          const existingSuggestions = res.data.words.slice(0, 5).map((item: any) => ({
            word: item.word,
            type: 'existing' as const,
            partOfSpeech: item.partOfSpeech || 'unknown',
            preview: item.definition?.substring(0, 60) || '',
            action: 'Review word'
          }));
          this.suggestions.push(...existingSuggestions);
        }

        // Always add option to search dictionary
        this.suggestions.push({
          word: term,
          type: 'new-search',
          action: 'Search dictionary'
        });
      },
      error: (err) => {
        console.error('Error searching vocabulary:', err);
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
    this.isLoading = true;
    this.errorMessage = '';
    this.currentWord = null;

    this.apiService.get<any>(`/words/vocabulary/search?term=${encodeURIComponent(word)}`).subscribe({
      next: (res) => {
        if (res?.data?.words && res.data.words.length > 0) {
          // Find the exact match (case-insensitive)
          const userWord = res.data.words.find((w: any) => w.word.toLowerCase() === word.toLowerCase()) || res.data.words[0];
          // Map the user's vocabulary word to WordLookupResult format
          const mapped: WordLookupResult = {
            word: userWord.word,
            phonetic: userWord.pronunciation,
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
        console.error('Error fetching word from vocabulary:', err);
        this.errorMessage = 'Failed to load word from your vocabulary.';
        this.isLoading = false;
      }
    });
  }

  searchNewWord(word: string, fromVocabularyList = false): void {
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
    this.apiService.get<any>(`/words/lookup/${encodeURIComponent(word)}`).subscribe({
      next: (res) => {
        try {
          if (res && (res as any).success && (res as any).data) {
            // Backend wraps WordLookupResponse inside ApiResponse.Data
            const lookupResp = (res as any).data; // WordLookupResponse from backend
            const wordDto = lookupResp.word; // WordDto
            if (wordDto) {
              // Map WordDto -> UI WordLookupResult shape
              const mapped: WordLookupResult = {
                word: wordDto.text || word,
                phonetic: wordDto.pronunciation,
                audioUrl: wordDto.audioUrl,
                source: lookupResp.isInUserVocabulary ? 'user' : (lookupResp.wasFoundInCache ? 'canonical' : 'external'),
                partOfSpeechGroups: []
              } as any;

              // Group definitions by part of speech
              const groupsMap: Record<string, PartOfSpeechGroup> = {};
              for (const def of (wordDto.definitions || [])) {
                const pos = (def.partOfSpeech || 'unknown').toLowerCase();
                if (!groupsMap[pos]) {
                  groupsMap[pos] = {
                    partOfSpeech: pos,
                    priority: (POS_PRIORITY as any)[pos] ?? 99,
                    definitions: [],
                    isExpanded: false,
                    primaryDefinitions: []
                  } as PartOfSpeechGroup;
                }

                const d = {
                  id: def.id,
                  definition: def.definition,
                  example: def.example,
                  synonyms: def.synonyms,
                  antonyms: def.antonyms
                } as any;

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
              this.errorMessage = lookupResp.errorMessage || 'No definitions found for this word.';
            }
          } else {
            this.errorMessage = (res as any).errorMessage || 'No definitions found for this word.';
          }
        } catch (ex) {
          console.error('Mapping error:', ex);
          this.errorMessage = 'Failed to process word definition.';
        }

        this.isLoading = false;
      },
      error: (err) => {
        console.error('API error searching word:', err);

        // Handle different error scenarios
        if (err.status === 404) {
          this.errorMessage = `Word "${word}" not found. Please check the spelling and try again.`;
        } else if (err.error?.error) {
          this.errorMessage = err.error.error;
        } else if (err.error?.errorMessage) {
          this.errorMessage = err.error.errorMessage;
        } else if (err.message) {
          this.errorMessage = err.message;
        } else {
          this.errorMessage = `Unable to find "${word}". Please check the spelling or try a different word.`;
        }

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

  private processWordResult(result: any): void {
    // Process and sort the word definition groups by priority
    this.sortedGroups = result.partOfSpeechGroups
      .sort((a: PartOfSpeechGroup, b: PartOfSpeechGroup) => {
        const priorityA = POS_PRIORITY[a.partOfSpeech as keyof typeof POS_PRIORITY] || 99;
        const priorityB = POS_PRIORITY[b.partOfSpeech as keyof typeof POS_PRIORITY] || 99;
        return priorityA - priorityB;
      });
  }

  private prioritizeDefinitions(definitions: any[]): any[] {
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

    // Build payload for the backend AddWordRequest DTO
    // We send a single "primary" definition for simplicity; you can change this to send many.
    const firstDef = this.currentWord.partOfSpeechGroups?.[0]?.definitions?.[0];
    const payload = {
      word: this.currentWord.word,
      definition: firstDef?.definition ?? '',
      partOfSpeech: this.currentWord.partOfSpeechGroups?.[0]?.partOfSpeech ?? '',
      example: firstDef?.example ?? '',
      preferredWordDefinitionId: firstDef?.id ?? null
    };

    // Use your ApiService post helper (see next section). Endpoint path is appended to baseUrl.
    this.apiService.post<any>('/words/vocabulary/add', payload).subscribe({
      next: (res) => {
        console.log('Add to vocabulary response:', res);
        // show user feedback with toast
        this.toastService.success(`Word "${this.currentWord?.word}" added to your vocabulary!`);
        // Set flag to disable the button
        this.wordAddedToVocabulary = true;
        if (this.currentWord) {
          this.currentWord.source = 'user';
        }
        this.vocabularyNeedsRefresh = true;
      },
      error: (err) => {
        console.error('Error adding word:', err);
        const msg = err?.error?.message || err?.error?.errorMessage || 'Failed to add word';
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
    // Load all words (use a large page size to get everything for search functionality)
    this.apiService.get<any>(`/words/vocabulary?page=${page}&pageSize=1000`).subscribe({
      next: (res) => {
        if (res && res.success && res.data) {
          this.vocabularyResponse = res.data;
          this.ensureSelectedLetterIsValid();
        } else {
          console.error('Invalid vocabulary response format:', res);
          this.vocabularyResponse = { words: [], totalCount: 0, page: 1, pageSize: 1000, totalPages: 0 };
          this.selectedVocabularyLetter = null;
        }
        this.vocabularyNeedsRefresh = false;
        this.vocabularyLoading = false;
      },
      error: (err) => {
        console.error('Error loading vocabulary:', err);
        this.vocabularyLoading = false;
        // Show empty state or error message
        this.vocabularyResponse = { words: [], totalCount: 0, page: 1, pageSize: 1000, totalPages: 0 };
        this.selectedVocabularyLetter = null;
        this.vocabularyNeedsRefresh = false;
      }
    });
  }

  playAudio(audioUrl: string): void {
    const normalizedAudioUrl = this.normalizeAudioUrl(audioUrl);
    if (!normalizedAudioUrl) {
      this.playSpeechSynthesisFallback();
      return;
    }

    try {
      if (this.pronunciationAudio) {
        this.pronunciationAudio.pause();
        this.pronunciationAudio.currentTime = 0;
      }

      this.pronunciationAudio = new Audio(normalizedAudioUrl);
      this.pronunciationAudio.preload = 'auto';

      this.pronunciationAudio.play().catch(error => {
        console.error('Failed to play pronunciation audio:', error);
        this.playSpeechSynthesisFallback();
      });
    } catch (error) {
      console.error('Audio setup failed:', error);
      this.playSpeechSynthesisFallback();
    }
  }

  private normalizeAudioUrl(audioUrl?: string | null): string | null {
    if (!audioUrl || !audioUrl.trim()) {
      return null;
    }

    const trimmed = audioUrl.trim();

    // Dictionary API data can contain protocol-relative or insecure HTTP audio links.
    if (trimmed.startsWith('//')) {
      return `https:${trimmed}`;
    }

    if (trimmed.startsWith('http://')) {
      return `https://${trimmed.substring('http://'.length)}`;
    }

    return trimmed;
  }

  private playSpeechSynthesisFallback(): void {
    const text = this.currentWord?.word?.trim();
    const synth = typeof window !== 'undefined' ? window.speechSynthesis : null;

    if (!text || !synth) {
      return;
    }

    try {
      synth.cancel();
      const utterance = new SpeechSynthesisUtterance(text);
      utterance.lang = 'en-US';
      utterance.rate = 0.95;
      synth.speak(utterance);
    } catch (error) {
      console.error('Speech synthesis fallback failed:', error);
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

  buildDefinitionOptions(definitions: any[]): DefinitionOption[] {
    const mappedDefinitions = (definitions || [])
      .filter((d: any) => Number.isFinite(d?.id))
      .map((d: any) => ({
        id: d.id,
        definition: d.definition,
        example: d.example,
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

    this.apiService.get<any>(`/words/lookup/${encodeURIComponent(word.word)}`).subscribe({
      next: (res) => {
        const definitions = (res as any)?.data?.word?.definitions || [];

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
        console.error('Failed to load definitions for editor:', err);
        this.definitionEditorLoading = false;
        this.toastService.error('Failed to load definitions for this word.');
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
    this.apiService.put<any>(`/words/vocabulary/${userWordId}/preferred-definition`, {
      preferredWordDefinitionId: definitionId
    }).subscribe({
      next: () => {
        if (this.vocabularyResponse?.words) {
          const target = this.vocabularyResponse.words.find(item => item.id === userWordId);
          if (target) {
            target.preferredWordDefinitionId = definitionId;
            if (selectedDefinition) {
              target.definition = selectedDefinition.definition;
              target.example = selectedDefinition.example;
            }
          }
        }

        if (this.activeVocabularyWord && this.activeVocabularyWord.id === userWordId) {
          this.activeVocabularyWord.preferredWordDefinitionId = definitionId;
        }

        if (this.definitionEditorWord) {
          this.definitionEditorWord.preferredWordDefinitionId = definitionId;
        }

        this.toastService.success('Preferred quiz definition saved.');
        this.definitionEditorSaving = false;
        this.closeDefinitionEditor();
      },
      error: (err) => {
        console.error('Error saving preferred definition:', err);
        this.definitionEditorSaving = false;
        const msg = err?.error?.error || err?.error?.errorMessage || 'Failed to save preferred definition';
        this.toastService.error(msg);
      }
    });
  }

  toggleFavorite(word: VocabularyItem, event: Event): void {
    event.stopPropagation();

    const newValue = !word.isFavorite;
    const previousValue = word.isFavorite;
    word.isFavorite = newValue;

    this.apiService.put<any>(`/words/vocabulary/${word.id}/favorite`, { isFavorite: newValue }).subscribe({
      next: () => {
        this.toastService.success(
          newValue ? `"${word.word}" added to favorites` : `"${word.word}" removed from favorites`
        );
      },
      error: (err) => {
        console.error('Error updating favorite state:', err);
        word.isFavorite = previousValue;
        const msg = err?.error?.error || err?.error?.errorMessage || 'Failed to update favorite state';
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
    return `${count} ${wordLabel} with "${letter}"`;
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