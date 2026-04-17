using AutoApiEngine.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IDatabaseManagementService
    {
        Task BackupAsync(
            string backupPath,
             DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default);

        Task RestoreAsync(
            string backupPath,
             DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }

}

