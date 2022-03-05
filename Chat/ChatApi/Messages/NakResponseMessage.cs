using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatApi.Messages
{
    public sealed class NakResponseMessage : IMessage
    {
        public NakResponseMessage(Guid requestId)
        {
            RequestId = requestId;
        }

        public Guid RequestId { get; }
    }
}
