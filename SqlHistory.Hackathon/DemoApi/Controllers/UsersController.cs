using Microsoft.AspNetCore.Mvc;
using SqlHistory.Hackathon.DemoApi.Services;
using SqlHistory.Hackathon.DemoApi.Telemetry;

namespace SqlHistory.Hackathon.DemoApi.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly UsersService _usersService;

    public UsersController(UsersService usersService)
    {
        _usersService = usersService;
    }

    [HttpGet]
    public async Task<IReadOnlyList<UserDto>> GetUsers(CancellationToken cancellationToken)
    {
        using var activity = DemoTelemetry.Source.StartActivity("UsersController.GetUsers");
        return await _usersService.GetAllUsersAsync(cancellationToken);
    }

    [HttpGet("check")]
    public async Task<object> CheckUsers(CancellationToken cancellationToken)
    {
        using var activity = DemoTelemetry.Source.StartActivity("UsersController.CheckUsers");
        await _usersService.CheckUsersAsync(cancellationToken);
        return new { checkedAtUtc = DateTime.UtcNow };
    }

    [HttpGet("active-count")]
    public async Task<object> GetActiveCount(CancellationToken cancellationToken)
    {
        using var activity = DemoTelemetry.Source.StartActivity("UsersController.GetActiveCount");
        return new { activeUsers = await _usersService.CountActiveUsersAsync(cancellationToken) };
    }
}
