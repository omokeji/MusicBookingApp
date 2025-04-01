namespace MusicBookingApp.Models;

public class Event
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = default!;
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Venue { get; set; } = string.Empty;
    public decimal TicketPrice { get; set; }
    public List<Booking> Bookings { get; set; } = new();
}