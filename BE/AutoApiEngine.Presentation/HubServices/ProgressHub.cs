using AutoApiEngine.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace AutoApiEngine.Presentation.HubServices
{
    public class ProgressHub : Hub
    {
        public async Task SendProgress(DatabaseProgress progress)
        {
            await Clients.All.SendAsync("ReceiveProgress", progress);
        }

        public Task JoinUser(string userId)
        { 
            var currentUserId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == null || currentUserId != userId)
                throw new HubException("Unauthorized to join this user group.");

            return Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        }

        public Task LeaveUser(string userId)
        {
            var currentUserId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == null || currentUserId != userId)
                throw new HubException("Unauthorized to leave this user group.");

            return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
        }
    }
}
