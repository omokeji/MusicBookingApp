namespace MusicBookingApp.DTOs;

public class EventDTOs
{
    public class EventCreateDto
    {
        public int ArtistId { get; set; }
        public string Title { get; set; }
        public DateTime Date { get; set; }
        public string Venue { get; set; }
        public decimal TicketPrice { get; set; }
    }
}
