using AutoApiEngine.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Presentation.HubServices
{
    public class ProgressHub : Hub
    {
        public async Task SendProgress(DatabaseProgress progress)
        {
            await Clients.All.SendAsync("ReceiveProgress", progress);
        }
    }
}
