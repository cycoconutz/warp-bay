using Microsoft.AspNetCore.SignalR;

namespace WarpBay.Api.Hubs;

public class ScheduleHub : Hub
{
    public Task JoinDay(string day) => Groups.AddToGroupAsync(Context.ConnectionId, $"day:{day}");
}
