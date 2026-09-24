using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Tatkal.Authentication;
using UserService.Models;

namespace UserService.Controllers;

[ApiController]
[Route("api/users")]
public class AuthController(
    UserDbContext db,
    IPasswordHasher<User> passwordHasher,
    IJwtTokenService tokenService,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Email and password are required.");

        var exists = await db.Users.AnyAsync(u => u.Email == request.Email, ct);
        if (exists) return Conflict("An account with this email already exists.");

        var user = new User
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            FullName = request.FullName,
            Phone = request.Phone,
            Role = UserRole.User
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} registered", user.Id);

        var token = tokenService.IssueToken(user.Id, user.Email, user.Role.ToString());
        return Ok(new AuthResponse(token, user.Id, user.Email, user.Role.ToString(), DateTime.UtcNow.AddMinutes(30)));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email.Trim().ToLowerInvariant(), ct);
        if (user is null) return Unauthorized("Invalid credentials.");

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return Unauthorized("Invalid credentials.");

        var token = tokenService.IssueToken(user.Id, user.Email, user.Role.ToString());
        return Ok(new AuthResponse(token, user.Id, user.Email, user.Role.ToString(), DateTime.UtcNow.AddMinutes(30)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileResponse>> Me(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return NotFound();

        return Ok(new UserProfileResponse(user.Id, user.Email, user.FullName, user.Phone, user.Role.ToString()));
    }
}
