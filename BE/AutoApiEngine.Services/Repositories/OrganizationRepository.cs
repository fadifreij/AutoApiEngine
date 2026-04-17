using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.Repositories.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Services.Repositories
{
    public class OrganizationRepository : GenericRepository<Organization>, IOrganizationRepository
    {
        public OrganizationRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
        {
            
        }
    }
}
