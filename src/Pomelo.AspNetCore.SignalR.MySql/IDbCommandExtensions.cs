// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using Pomelo.Data.MySql;
using System.Threading.Tasks;
using JetBrains.Annotations;


namespace Pomelo.AspNetCore.SignalR.MySql
{
    internal static class IDbCommandExtensions
    {
        private readonly static TimeSpan _dependencyTimeout = TimeSpan.FromSeconds(60);

#if NET451
        public static Task<int> ExecuteNonQueryAsync(this IDbCommand command)
#else
        public static Task<int> ExecuteNonQueryAsync(this DbCommand command)
#endif
        {
            var sqlCommand = command as MySqlCommand;

            if (sqlCommand != null)
            {
#if NET451
                return Task.Factory.FromAsync(
                    (cb, state) => sqlCommand.BeginExecuteNonQuery(cb, state),
                    iar => sqlCommand.EndExecuteNonQuery(iar),
                    null);
#else
                return sqlCommand.ExecuteNonQueryAsync();
#endif
            }
            else
            {
                return Task.FromResult(command.ExecuteNonQuery());
            }
        }
    }
}
