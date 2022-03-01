using ChatApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatServer
{
    public sealed class ConnectionCollection
    {
        private readonly object _mutex = new();
        private readonly List<ChatConnection> _connections = new();

        public void Add(ChatConnection connection)
        {
            lock (_mutex)
            {
                _connections.Add(connection);
            }
        }

        public void Remove(ChatConnection connection)
        {
            lock (_mutex)
            {
                _connections.Remove(connection);
            }
        }

        public IReadOnlyCollection<ChatConnection> CurrentConnections
        {
            get
            {
                lock (_mutex)
                {
                    return _connections.ToList();
                }
            }
        }
    }
}
