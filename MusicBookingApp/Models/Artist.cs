using Microsoft.Extensions.Logging;

namespace MusicBookingApp.Models;

public class Artist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<Event> Events { get; set; } = new();
}
