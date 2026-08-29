namespace VocabularyApp.WebApi.Models
{
  public enum ServiceFailureType
  {
    None,
    Validation,
    NotFound,
    ServiceUnavailable
  }

  public class ServiceResult<T>
  {
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public ServiceFailureType FailureType { get; set; }

    public static ServiceResult<T> Success(T data, string? message = null)
    {
      return new ServiceResult<T> { IsSuccess = true, Data = data, Message = message };
    }

    public static ServiceResult<T> Failure(
      string message,
      ServiceFailureType failureType = ServiceFailureType.Validation)
    {
      return new ServiceResult<T>
      {
        IsSuccess = false,
        Message = message,
        FailureType = failureType
      };
    }
  }
}
