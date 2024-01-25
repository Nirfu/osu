// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Chat;

namespace osu.Game.Tests
{
    public class PollingChatClientConnector : PersistentEndpointClientConnector, IChatClient
    {
        public event Action<Channel>? ChannelJoined;

        public event Action<Channel>? ChannelParted
        {
            add { }
            remove { }
        }

        public event Action<List<Message>>? NewMessages;
        public event Action? PresenceReceived;

        public void FetchInitialMessages()
        {
            // don't really need to do anything special if we poll every second anyway.
        }

        public PollingChatClientConnector(IAPIProvider api)
            : base(api)
        {
        }

        protected sealed override Task<PersistentEndpointClient> BuildConnectionAsync(CancellationToken cancellationToken)
        {
            var client = new PollingChatClient(API);

            client.ChannelJoined += c => ChannelJoined?.Invoke(c);
            client.NewMessages += m => NewMessages?.Invoke(m);
            client.PresenceReceived += () => PresenceReceived?.Invoke();

            return Task.FromResult<PersistentEndpointClient>(client);
        }
    }
}
