// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Pomelo.Data.MySql;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Pomelo.AspNetCore.SignalR.MySql
{
    /// <summary>
    /// A DbOperation that continues to execute over and over as new results arrive.
    /// Will attempt to use SQL Query Notifications, otherwise falls back to a polling receive loop.
    /// </summary>
    internal class ObservableDbOperation : DbOperation, IDisposable, IDbBehavior
    {
        private readonly Tuple<int, int>[] _updateLoopRetryDelays = new[] {
            Tuple.Create(0, 3),    // 0ms x 3
            Tuple.Create(10, 3),   // 10ms x 3
            Tuple.Create(50, 2),   // 50ms x 2
            Tuple.Create(100, 2),  // 100ms x 2
            Tuple.Create(200, 2),  // 200ms x 2
            Tuple.Create(1000, 2), // 1000ms x 2
            Tuple.Create(1500, 2), // 1500ms x 2
            Tuple.Create(3000, 1)  // 3000ms x 1
        };
        private readonly object _stopLocker = new object();
        private readonly ManualResetEventSlim _stopHandle = new ManualResetEventSlim(true);
        private readonly IDbBehavior _dbBehavior;

        private volatile bool _disposing;
        private long _notificationState;
        private readonly ILogger _logger;

        public ObservableDbOperation(string connectionString, string commandText, ILogger logger, IDbProviderFactory dbProviderFactory, IDbBehavior dbBehavior)
            : base(connectionString, commandText, logger, dbProviderFactory)
        {
            _dbBehavior = dbBehavior ?? this;
            _logger = logger;

            InitEvents();
        }

        public ObservableDbOperation(string connectionString, string commandText, ILogger logger, params DbParameter[] parameters)
            : base(connectionString, commandText, logger, parameters)
        {
            _dbBehavior = this;
            _logger = logger;

            InitEvents();
        }

        /// <summary>
        /// For use from tests only.
        /// </summary>
        internal long CurrentNotificationState
        {
            get { return _notificationState; }
            set { _notificationState = value; }
        }

        private void InitEvents()
        {
            Faulted += _ => { };
            Queried += () => { };
        }

        public event Action Queried;
        public event Action<Exception> Faulted;

        /// <summary>
        /// Note this blocks the calling thread until a SQL Query Notification can be set up
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "Needs refactoring"),
         SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Errors are reported via the callback")]
#if NET451
        public void ExecuteReaderWithUpdates(Action<IDataRecord, DbOperation> processRecord)
#else
        public void ExecuteReaderWithUpdates(Action<DbDataReader, DbOperation> processRecord)
#endif
        {
            lock (_stopLocker)
            {
                if (_disposing)
                {
                    return;
                }
                _stopHandle.Reset();
            }

            var useNotifications = _dbBehavior.StartSqlDependencyListener();

            var delays = _dbBehavior.UpdateLoopRetryDelays;

            for (var i = 0; i < delays.Count; i++)
            {
                if (i == 0 && useNotifications)
                {
                    // Reset the state to ProcessingUpdates if this is the start of the loop.
                    // This should be safe to do here without Interlocked because the state is protected
                    // in the other two cases using Interlocked, i.e. there should only be one instance of
                    // this running at any point in time.
                    _notificationState = NotificationState.ProcessingUpdates;
                }

                Tuple<int, int> retry = delays[i];
                var retryDelay = retry.Item1;
                var retryCount = retry.Item2;

                for (var j = 0; j < retryCount; j++)
                {
                    if (_disposing)
                    {
                        Stop(null);
                        return;
                    }

                    if (retryDelay > 0)
                    {
                        Logger.LogDebug(String.Format("{0}Waiting {1}ms before checking for messages again", LoggerPrefix, retryDelay));

                        Thread.Sleep(retryDelay);
                    }

                    var recordCount = 0;
                    try
                    {
                        recordCount = ExecuteReader(processRecord);

                        Queried();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(String.Format("{0}Error in SQL receive loop: {1}", LoggerPrefix, ex));

                        Faulted(ex);
                    }

                    if (recordCount > 0)
                    {
                        Logger.LogDebug(String.Format("{0}{1} records received", LoggerPrefix, recordCount));

                        // We got records so start the retry loop again
                        i = -1;
                        break;
                    }

                    Logger.LogDebug("{0}No records received", LoggerPrefix);

                    var isLastRetry = i == delays.Count - 1 && j == retryCount - 1;

                    if (isLastRetry)
                    {
                        // Last retry loop iteration
                        if (!useNotifications)
                        {
                            // Last retry loop and we're not using notifications so just stay looping on the last retry delay
                            j = j - 1;
                        }
                    }
                }
            }

            Logger.LogDebug("{0}Receive loop exiting", LoggerPrefix);
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Disposing")]
        public void Dispose()
        {
            lock (_stopLocker)
            {
                _disposing = true;
            }
            if (Interlocked.Read(ref _notificationState) == NotificationState.ProcessingUpdates)
            {
                _stopHandle.Wait();
            }
            _stopHandle.Dispose();
        }


        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "I need to")]
        protected virtual bool StartSqlDependencyListener()
        {
            return false;
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Stopping is a terminal state on a bg thread")]
        protected virtual void Stop(Exception ex)
        {
            if (ex != null)
            {
                Faulted(ex);
            }

            lock (_stopLocker)
            {
                if (_disposing)
                {
                    _stopHandle.Set();
                }
            }
        }

        internal static class NotificationState
        {
            public const long Enabled = 0;
            public const long ProcessingUpdates = 1;
            public const long AwaitingNotification = 2;
            public const long NotificationReceived = 3;
            public const long Disabled = 4;
        }

        bool IDbBehavior.StartSqlDependencyListener()
        {
            return StartSqlDependencyListener();
        }

        IList<Tuple<int, int>> IDbBehavior.UpdateLoopRetryDelays
        {
            get { return _updateLoopRetryDelays; }
        }
    }
}
