namespace UserService.Models;

public enum UserRole { User, Admin, System }

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string? Phone { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<PassengerProfile> Passengers { get; set; } = new List<PassengerProfile>();
}

/// <summary>Saved passenger details a user can attach to a booking without retyping them.</summary>
public class PassengerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string FullName { get; set; } = default!;
    public int Age { get; set; }
    public string Gender { get; set; } = default!;
    public string? IdProofNumber { get; set; }
}
