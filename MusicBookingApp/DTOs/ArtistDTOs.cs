using System.ComponentModel.DataAnnotations;

namespace MusicBookingApp.DTOs;

public class ArtistDTOs
{
    public class ArtistCreateDto
    {
        [Required]
        public string Name { get; set; }
        [Required]
        public string Genre { get; set; } 
        public string Bio { get; set; }
        public string Email { get; set; }
    }
}
