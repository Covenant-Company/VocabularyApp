import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { Router } from '@angular/router';
import { WordLookupResult, PartOfSpeechGroup, SearchSuggestion, POS_PRIORITY, VocabularyResponse } from '../../models/word-lookup.model';
import { ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-word-lookup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './word-lookup.component.html',
  styleUrl: './word-lookup.component.scss'
})
export class WordLookupComponent implements OnInit {
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
  private vocabularyNeedsRefresh = false;

  constructor(private apiService: ApiService, private router: Router, public toastService: ToastService) { }

  backToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }

  ngOnInit(): void { }

  onSearchInput(): void {
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
      example: firstDef?.example ?? ''
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
      this.viewingFromVocabularyList = false;
    } else {
      // Also clear when switching back to lookup view
      this.searchTerm = '';
      this.errorMessage = '';
      this.viewingFromVocabularyList = false;
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
        } else {
          console.error('Invalid vocabulary response format:', res);
          this.vocabularyResponse = { words: [], totalCount: 0, page: 1, pageSize: 1000, totalPages: 0 };
        }
        this.vocabularyNeedsRefresh = false;
        this.vocabularyLoading = false;
      },
      error: (err) => {
        console.error('Error loading vocabulary:', err);
        this.vocabularyLoading = false;
        // Show empty state or error message
        this.vocabularyResponse = { words: [], totalCount: 0, page: 1, pageSize: 1000, totalPages: 0 };
        this.vocabularyNeedsRefresh = false;
      }
    });
  }

  playAudio(audioUrl: string): void {
    if (!audioUrl) {
      console.warn('No audio URL available');
      return;
    }

    const audio = new Audio(audioUrl);
    audio.play().catch(error => {
      console.error('Failed to play audio:', error);
    });
  }

  viewWordDetails(wordText: string): void {
    // Hide vocabulary list and show word details
    this.showVocabularyList = false;
    this.searchTerm = wordText;
    // Fetch the full word details using the lookup endpoint
    this.searchNewWord(wordText, true);
  }

  get filteredVocabularyWords() {
    if (!this.vocabularyResponse?.words) return [];
    if (!this.vocabularySearchQuery.trim()) return []; // Return empty array when no search query

    const query = this.vocabularySearchQuery.toLowerCase().trim();
    return this.vocabularyResponse.words.filter(word =>
      word.word.toLowerCase().startsWith(query) // Use startsWith to match from beginning
    );
  }
}