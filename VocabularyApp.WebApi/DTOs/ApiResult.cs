
namespace VocabularyApp.WebApi.DTOs
{
  public class ApiResult
  {
    public bool Success { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResult SuccessResult(object? data = null, string? message = null)
    {
      return new ApiResult
      {
        Success = true,
        Data = data,
        Message = message ?? "Request succeeded."
      };
    }

    public static ApiResult ErrorResult(string message, object? data = null)
    {
      return new ApiResult
      {
        Success = false,
        Message = message,
        Error = message,
        Data = data
      };
    }

  }

  public class ApiResult<T>
  {
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResult<T> SuccessResult(T data)
    {
      return new ApiResult<T> { Success = true, Data = data };
    }

    public static ApiResult<T> ErrorResult(string error)
    {
      return new ApiResult<T> { Success = false, Error = error };
    }
  }
}
