namespace MusicBookingApp.DTOs;

public class Result<T>
{
    public T? Content { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResponseDescription { get; set; }
    public bool IsSuccess { get; set; } = true;
}