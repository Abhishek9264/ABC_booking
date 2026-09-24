namespace Tatkal.Authentication;

public class JwtOptions
{
    public string SigningKey { get; set; } = default!;
    public string Issuer { get; set; } = "tatkal-booking-system";
    public string Audience { get; set; } = "tatkal-clients";
    public int AccessTokenMinutes { get; set; } = 30;
}
