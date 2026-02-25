namespace VocabularyApp.WebApi.DTOs;

public class StartQuizRequestDto
{
  public int QuestionCount { get; set; } = 10;
  public string Mode { get; set; } = "mixed";
}

public class QuizOptionDto
{
  public int OptionId { get; set; }
  public string Text { get; set; } = string.Empty;
}

public class QuizQuestionDto
{
  public Guid QuestionId { get; set; }
  public string QuestionType { get; set; } = string.Empty;
  public string Prompt { get; set; } = string.Empty;
  public List<QuizOptionDto> Options { get; set; } = new();
}

public class QuizStartResponseDto
{
  public Guid SessionId { get; set; }
  public string Mode { get; set; } = "mixed";
  public int QuestionCount { get; set; }
  public DateTime ExpiresAtUtc { get; set; }
  public List<QuizQuestionDto> Questions { get; set; } = new();
}

public class QuizAnswerSubmissionDto
{
  public Guid QuestionId { get; set; }
  public int SelectedOptionId { get; set; }
}

public class QuizSubmitRequestDto
{
  public Guid SessionId { get; set; }
  public List<QuizAnswerSubmissionDto> Answers { get; set; } = new();
}

public class QuizQuestionResultDto
{
  public Guid QuestionId { get; set; }
  public string QuestionType { get; set; } = string.Empty;
  public string Prompt { get; set; } = string.Empty;
  public string CorrectAnswer { get; set; } = string.Empty;
  public string? SelectedAnswer { get; set; }
  public bool IsCorrect { get; set; }
}

public class QuizSubmitResponseDto
{
  public int TotalQuestions { get; set; }
  public int CorrectAnswers { get; set; }
  public double ScorePercentage { get; set; }
  public List<QuizQuestionResultDto> QuestionResults { get; set; } = new();
}

public class QuizHistoryItemDto
{
  public DateTime AttemptedAtUtc { get; set; }
  public int TotalQuestions { get; set; }
  public int CorrectAnswers { get; set; }
  public double ScorePercentage { get; set; }
}

public class QuizHistoryResponseDto
{
  public List<QuizHistoryItemDto> Items { get; set; } = new();
}
