using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatApi.Messages
{
    public sealed class SetNicknameRequestMessage : IMessage
    {
        public SetNicknameRequestMessage(string nickname)
        {
            Nickname = nickname;
        }

        public string Nickname { get; }
    }
}
