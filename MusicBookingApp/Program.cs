using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MusicBookingApp;
using MusicBookingApp.Data;
using MusicBookingApp.DTOs;
using MusicBookingApp.Models;
using Npgsql;
using System.Net;
using System;
using System.Text;
using System.Threading.RateLimiting;
using static MusicBookingApp.DTOs.ArtistDTOs;
using static MusicBookingApp.DTOs.EventDTOs;
using static MusicBookingApp.DTOs.AuthDTOs;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Add services to the container.

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Music Booking API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT with Bearer into field",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            new string[] {}
        }
    });
});

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

string jwtKey = builder.Configuration["Jwt:Key"]!;
string jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
string jwtAudience = builder.Configuration["Jwt:Audience"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained<HttpContext>(
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Request.Path.Value ?? "/",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 100,
                    QueueLimit = 2,
                    Window = TimeSpan.FromSeconds(10)
                }
            ))
    );
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

var defaultConnectionString = configuration.GetConnectionString("DefaultConnection");

var dataSource = new NpgsqlDataSourceBuilder(defaultConnectionString)
    .EnableDynamicJson().Build();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(defaultConnectionString));

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Error Handling Middleware
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    if (exception != null)
    {
        var error = exception.Error;
        var statusCode = error switch
        {
            ArgumentException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            KeyNotFoundException => (int)HttpStatusCode.NotFound,
            _ => (int)HttpStatusCode.InternalServerError
        };
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var response = new { StatusCode = statusCode, Message = error.Message, Detailed = app.Environment.IsDevelopment() ? error.StackTrace : null };
        await context.Response.WriteAsJsonAsync(response);
    }
        });
    });

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

static string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToBase64String(hashedBytes);
}

// Helper method to generate JWT token
string GenerateJwtToken(ApplicationUser user, string key, string issuer, string audience)
{
    var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, user.Email)
    };

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: DateTime.Now.AddHours(1),
        signingCredentials: credentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

app.MapPost("/api/auth/signup", async (SignupDto dto, AppDbContext dbContext) =>
{
    if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password))
        return Results.BadRequest(new Result<SignUpResponseDto>
        {
            ResponseDescription = "Email and password are required.",
            ResponseCode = ResponseCodes.Error,
            IsSuccess = false,
        });

    if (await dbContext.Users.AnyAsync(u => u.Email == dto.Email))
        return Results.BadRequest(new Result<SignUpResponseDto>
        {
            ResponseDescription = "Email already exists.",
            ResponseCode = ResponseCodes.Error,
            IsSuccess = false,
        });

    var user = new ApplicationUser
    {
        Email = dto.Email,
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        PhoneNumber = dto.PhoneNumber,
        PasswordHash = HashPassword(dto.Password)
    };

    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/api/auth/signup/{user.Id}", new Result<SignUpResponseDto>
    {
        ResponseDescription = "Signup successful",
        ResponseCode = ResponseCodes.Success,
        IsSuccess = true,
        Content = new SignUpResponseDto { Email = user.Email, UserId = user.Id }
    });
})
.WithName("Signup")
.WithTags("Auth")
.Produces<Result<SignUpResponseDto>>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest);

// Login Endpoint
app.MapPost("/api/auth/login", async (LoginDto dto, AppDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
    if (user == null || user.PasswordHash != HashPassword(dto.Password))
        return Results.BadRequest(new Result<AuthResponseDto>
        {
            ResponseDescription = "Invalid email or password.",
            ResponseCode = ResponseCodes.Error,
            IsSuccess = false,
        });

    var token = GenerateJwtToken(user, jwtKey, jwtIssuer, jwtAudience);

    return Results.Ok(new Result<AuthResponseDto>
    {
        ResponseDescription = "Login Successful",
        ResponseCode = ResponseCodes.Success,
        IsSuccess = true,
        Content = new AuthResponseDto { Token = token }
    });
})
.WithName("Login")
.WithTags("Auth")
.Produces<Result<AuthResponseDto>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// Artist Endpoints
app.MapGet("/api/artists", async (AppDbContext dbContext) => {
        var artists = await dbContext.Artists.ToListAsync();
        var response = new Result<List<Artist>>
        {
            ResponseDescription = $"All artists",
            ResponseCode = ResponseCodes.Success,
            Content = artists
        };

        return Results.Ok(response);
    })
   .WithName("GetArtists")
   .WithTags("Artists")
   .RequireAuthorization()
   .Produces<Result<List<Artist>>>();

app.MapGet("/api/artists/{id}", async (int id, AppDbContext dbContext) =>
   {
       var artist = await dbContext.Artists
            .FirstOrDefaultAsync(x => x.Id == id);

       if (artist == null)
           return Results.BadRequest(new Result<Artist>
           {
                ResponseDescription = "Invalid Id",
                ResponseCode = ResponseCodes.Error,
                IsSuccess = false,
           });

       return Results.Ok(new Result<Artist>
       {
           ResponseDescription = ResponseCodes.Success,
           IsSuccess = true,
           Content = artist
       });
   })
   .WithName("GetArtistById")
   .WithTags("Artists")
   .RequireAuthorization()
   .Produces<Result<Artist>>(StatusCodes.Status200OK)
   .Produces(StatusCodes.Status404NotFound);

    app.MapPost("/api/artists", async (ArtistCreateDto request, AppDbContext dbContext) =>
    {
        if (string.IsNullOrEmpty(request.Name))
            return Results.BadRequest(new Result<Artist>
            {
                ResponseDescription = "Artist name is required.",
                IsSuccess = false,
                ResponseCode = ResponseCodes.Error
            });

        var artist = new Artist
        {
            Name = request.Name,
            Genre = request.Genre,
            Bio = request.Bio,
            Email = request.Email
        };

        dbContext.Artists.Add(artist);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new Result<Artist>
        {
            ResponseCode = ResponseCodes.Success,
            ResponseDescription = $"/api/artists/{artist.Id}",
            IsSuccess = true,
            Content = artist
        });
    })
   .WithName("CreateArtist")
   .WithTags("Artists")
   .RequireAuthorization()
   .Produces<Result<Artist>>(StatusCodes.Status201Created)
   .Produces(StatusCodes.Status400BadRequest);

// Event Endpoints
app.MapGet("/api/events", async (AppDbContext dbContext) =>
    {
        var events = await dbContext.Events.Include(e => e.Artist).ToListAsync();
        var response = new Result<List<Event>>
        {
            ResponseDescription = $"All events",
            ResponseCode = ResponseCodes.Success,
            Content = events
        };

        return Results.Ok(response);
    })
   .WithName("GetEvents")
   .WithTags("Events")
   .RequireAuthorization()
   .Produces<Result<List<Event>>>(StatusCodes.Status200OK);

app.MapPost("/api/events", async (EventCreateDto request, AppDbContext dbContext) =>
{
    if (request.Date < DateTime.UtcNow)
        return Results.BadRequest(new Result<string>
        {
            ResponseDescription = "Event date cannot be in the past.",
            IsSuccess = false,
            ResponseCode = ResponseCodes.Error
        });

    var evt = new Event
    {
        ArtistId = request.ArtistId,
        Title = request.Title,
        Date = request.Date,
        Venue = request.Venue,
        TicketPrice = request.TicketPrice
    };

    dbContext.Events.Add(evt);
    await dbContext.SaveChangesAsync();

    var response = new Result<string>
    {
        ResponseDescription = $"/api/events/{evt.Id}",
        ResponseCode = ResponseCodes.Success
    };

    return Results.Ok(response);
})
   .WithName("CreateEvent")
   .WithTags("Events")
   .RequireAuthorization()
   .Produces<Result<Event>>(StatusCodes.Status201Created)
   .Produces(StatusCodes.Status400BadRequest);

// Booking Endpoints
    app.MapPost("/api/bookings", async (Booking request, AppDbContext dbContext) =>
    {
        var evt = await dbContext.Events.FirstOrDefaultAsync(x => x.Id == request.EventId);
        if (evt == null)
            return Results.BadRequest(new Result<string>
            {
                ResponseDescription = "Event not found.",
                IsSuccess = false,
                ResponseCode = ResponseCodes.Error
            });

        var booking = new Booking
        {
            EventId = request.EventId,
            UserId = request.UserId,
            BookingDate = DateTime.UtcNow, 
            Status = "Pending"
        };

        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        var response = new Result<string>
        {
            ResponseDescription = $"/api/bookings/{booking.Id}",
            ResponseCode = ResponseCodes.Success
        };

        return Results.Ok(response);
    })
   .WithName("CreateBooking")
   .WithTags("Bookings")
   .RequireAuthorization()
   .Produces<Result<Booking>>(StatusCodes.Status201Created)
   .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/bookings/{userId}", async (int userId, AppDbContext dbContext) =>
    {
        var bookings = await dbContext.Bookings
            .Where(b => b.UserId == userId)
            .Include(b => b.Event)
            .ToListAsync();

        var response = new Result<List<Booking>>
        {
            ResponseDescription = $"All Bookings",
            ResponseCode = ResponseCodes.Success,
            Content = bookings
        };

        return Results.Ok(response);
    })
   .WithName("GetBookingsByUser")
   .WithTags("Bookings")
   .RequireAuthorization()
   .Produces<Result<List<Booking>>>(StatusCodes.Status200OK);

app.Run();
