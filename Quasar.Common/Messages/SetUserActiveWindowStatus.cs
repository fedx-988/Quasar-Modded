﻿using ProtoBuf;
using Quasar.Common.Messages.other;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class SetUserActiveWindowStatus : IMessage
    {
        [ProtoMember(1)]
        public string WindowTitle { get; set; }
    }
}