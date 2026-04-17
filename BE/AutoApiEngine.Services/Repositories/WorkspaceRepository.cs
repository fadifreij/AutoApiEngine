using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.Repositories.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Services.Repositories
{
    public class WorkspaceRepository : GenericRepository<Workspace>, IWorkspaceRepository
    {
        public WorkspaceRepository(ApplicationDbContext applicationDbContext):base(applicationDbContext)
        {
            
        }
    }
}
