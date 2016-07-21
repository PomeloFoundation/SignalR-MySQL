// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Messaging;
using Microsoft.Extensions.Logging;

namespace Pomelo.AspNetCore.SignalR.MySql
{
    internal class MySqlStream : IDisposable
    {
        private readonly int _streamIndex;
        private readonly ILogger _logger;
        private readonly MySqlSender _sender;
        private readonly MySqlReceiver _receiver;
        private readonly string _loggerPrefix;

        public MySqlStream(int streamIndex, string connectionString, string tableName, ILogger logger, IDbProviderFactory dbProviderFactory)
        {
            _streamIndex = streamIndex;
            _logger = logger;
            _loggerPrefix = String.Format(CultureInfo.InvariantCulture, "Stream {0} : ", _streamIndex);

            Queried += () => { };
            Received += (_, __) => { };
            Faulted += _ => { };

            _sender = new MySqlSender(connectionString, tableName, _logger, dbProviderFactory);
            _receiver = new MySqlReceiver(connectionString, tableName, _logger, _loggerPrefix, dbProviderFactory);
            _receiver.Queried += () => Queried();
            _receiver.Faulted += (ex) => Faulted(ex);
            _receiver.Received += (id, messages) => Received(id, messages);
        }

        public event Action Queried;

        public event Action<ulong, ScaleoutMessage> Received;

        public event Action<Exception> Faulted;

        public Task StartReceiving()
        {
            return _receiver.StartReceiving();
        }

        public Task Send(IList<Message> messages)
        {
             _logger.LogDebug(String.Format("{0}Saving payload with {1} messages(s) to SQL server", _loggerPrefix, messages.Count, _streamIndex));
            
            return _sender.Send(messages);
        }

        public void Dispose()
        {
            _logger.LogInformation(String.Format("{0}Disposing stream {1}", _loggerPrefix, _streamIndex));

            _receiver.Dispose();
        }
    }
}
