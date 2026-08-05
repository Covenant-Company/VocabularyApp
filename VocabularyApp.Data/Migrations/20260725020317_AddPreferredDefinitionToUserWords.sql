-- Migration: 20260725020317_AddPreferredDefinitionToUserWords
-- Generated from: VocabularyApp.Data/Migrations/20260725020317_AddPreferredDefinitionToUserWords.cs
-- Target: SQL Server
/* =========================
 UP MIGRATION
 ========================= */
BEGIN TRANSACTION;
GO
ALTER TABLE [UserWords]
ADD [PreferredWordDefinitionId] int NULL;
GO CREATE INDEX [IX_UserWords_PreferredWordDefinitionId] ON [UserWords] ([PreferredWordDefinitionId]);
GO
ALTER TABLE [UserWords]
ADD CONSTRAINT [FK_UserWords_WordDefinitions_PreferredWordDefinitionId] FOREIGN KEY ([PreferredWordDefinitionId]) REFERENCES [WordDefinitions] ([Id]);
GO COMMIT;
GO
  /* =========================
   DOWN MIGRATION (rollback)
   ========================= */
  BEGIN TRANSACTION;
GO
ALTER TABLE [UserWords] DROP CONSTRAINT [FK_UserWords_WordDefinitions_PreferredWordDefinitionId];
GO DROP INDEX [IX_UserWords_PreferredWordDefinitionId] ON [UserWords];
GO
ALTER TABLE [UserWords] DROP COLUMN [PreferredWordDefinitionId];
GO COMMIT;
GO