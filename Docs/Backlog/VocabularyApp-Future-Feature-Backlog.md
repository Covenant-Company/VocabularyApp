# VocabularyApp Future Feature Backlog

## Purpose

This document preserves proposed features for consideration after the current F1–F5 roadmap. F6–F17 are currently backlog identifiers; their priority and implementation sequence are **not finalized**. Features may be reordered or renumbered later.

Adding an item to this document does not approve it for immediate implementation. Each feature should receive its own analysis and implementation plan before development begins.

## Future Feature Backlog

### F6 — Spaced Repetition / Smart Review

Automatically determine which words a user should review based on factors such as quiz performance, difficulty, and time since the last review.

This could potentially leverage existing learning-history information such as `QuizResult`, review timestamps, correct-answer history, and attempt counts.

### F7 — Mastery Levels

Introduce learning states for saved words, such as:

- New
- Learning
- Familiar
- Mastered

These states could eventually appear in the user's vocabulary list, dashboard, analytics, and study workflows.

### F8 — Personalized Daily Learning Session

Introduce a **Study Today** workflow that automatically assembles a short personalized learning session using combinations of:

- Words due for review
- Difficult or weak words
- Recently added words
- New learning material
- A short quiz

### F9 — Contextual Example Sentences

Expand the existing sample-sentence capability so users learn vocabulary in context rather than relying only on definitions.

Potential exercises could include:

- Choosing the correct word for a sentence
- Identifying which definition applies in context
- Sentence completion
- Reviewing examples containing saved vocabulary

### F10 — Multiple Quiz Types

Expand the quiz system beyond standard multiple-choice questions. Potential quiz modes include:

- Word → definition
- Definition → word
- Fill-in-the-blank
- Synonym/antonym
- Sentence completion
- Spelling
- Potentially pronunciation-related exercises

The final quiz architecture should allow additional question types to be introduced later.

### F11 — Weak Words / Trouble Words

Automatically identify vocabulary that a user repeatedly struggles with. Introduce a **Trouble Words** or equivalent experience that can:

- Identify repeatedly missed words
- Prioritize those words during review
- Show where the user is having difficulty
- Feed this information into future learning sessions

### F12 — Vocabulary Goals & Streaks

Allow users to establish learning goals such as:

- Words learned per day
- Words mastered per week or month
- Review goals
- Study-frequency goals

Potentially include streaks and progress indicators. Gamification should support actual vocabulary learning rather than becoming the primary objective.

### F13 — Vocabulary Collections

Allow users to organize vocabulary into collections or categories. Examples include:

- Work
- College
- SAT
- Rail Industry
- Programming
- Books I'm Reading

The future design should consider whether a word can belong to multiple collections.

### F14 — Import Vocabulary

Allow users to add vocabulary in bulk rather than entering every word individually. Initial possibilities include:

- Pasting a list of words
- Importing a simple supported file
- Validating words before import
- Preventing duplicate vocabulary entries

A later extension could identify candidate vocabulary automatically from pasted text.

### F15 — Reading Mode

Allow users to paste an article, paragraph, or other passage into VocabularyApp and interact with vocabulary directly in the text. Potential capabilities include:

- Highlighting saved vocabulary
- Selecting unfamiliar words
- Displaying definitions in context
- Adding words directly to the user's vocabulary
- Connecting reading activity with the learning system

### F16 — AI Vocabulary Tutor

Introduce an AI-assisted vocabulary tutor that uses the user's actual VocabularyApp learning information rather than functioning as a generic chatbot. Potential capabilities include:

- Practicing difficult words
- Asking contextual questions
- Explaining incorrect answers
- Generating targeted exercises
- Reinforcing weak vocabulary
- Adapting conversations to the user's learning history

Any future implementation must include a separate architecture, security, privacy, cost, and provider analysis before development.

### F17 — Adaptive Learning Engine

Create a higher-level learning engine that combines information such as:

- Mastery level
- Quiz history
- Review intervals
- Weak/trouble words
- Learning goals
- Recent activity
- Other learning signals

The engine would eventually determine what the user should study next and dynamically adjust future learning sessions.

This should be treated as a later-stage feature that builds on several earlier learning features rather than an isolated feature. Dependencies and implementation sequence still require evaluation during future planning.

## Product Direction

These proposed features support the following long-term learning loop:

**Add a word → Learn it → Review it → Quiz it → Measure performance → Schedule the next review → Master it**

The longer-term goal is to evolve VocabularyApp from primarily a dictionary and saved-word application into a personalized vocabulary-learning system.

## Backlog Status

- F6–F17 are proposed future features.
- They are not currently prioritized.
- Their numbering represents backlog identifiers, not necessarily implementation order.
- The existing F1–F5 roadmap remains unchanged by this document.
- Dependencies must be evaluated before moving an item into active development.
- Features may be combined, split, reordered, renamed, or removed during future roadmap planning.
