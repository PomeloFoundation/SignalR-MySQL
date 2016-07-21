// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Messaging;
using Pomelo.AspNetCore.SignalR;
using Pomelo.AspNetCore.SignalR.MySql;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SqlServerSignalRServicesBuilderExtensions
    {
        public static SignalRServicesBuilder AddMySql(this SignalRServicesBuilder builder, Action<MySqlScaleoutOptions> configureOptions = null)
        {
            return builder.AddMySql(configuration: null, configureOptions: configureOptions);
        }
        public static SignalRServicesBuilder AddMySql(this SignalRServicesBuilder builder, IConfiguration configuration, Action<MySqlScaleoutOptions> configureOptions = null)
        {
            builder.ServiceCollection.Add(ServiceDescriptor.Singleton<IMessageBus, MySqlMessageBus>());

            if (configuration != null)
            {
                builder.ServiceCollection.Configure<MySqlScaleoutOptions>(configuration);
            }

            if (configureOptions != null)
            {
                builder.ServiceCollection.Configure(configureOptions);
            }

            return builder;
        }
    }
}
