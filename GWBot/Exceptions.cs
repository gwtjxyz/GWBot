using System;
using System.Collections.Generic;
using System.Text;

namespace GWBot;

[Serializable]
class UserNotInGuildException : Exception
{
    public UserNotInGuildException() { }
    public UserNotInGuildException(string message) : base(message) { }
    public UserNotInGuildException(string message, Exception innerException) : base(message, innerException) { }
}

class MessageNotInGuildException : Exception
{
    public MessageNotInGuildException() { }
    public MessageNotInGuildException(string message) : base(message) { }
    public MessageNotInGuildException(string message, Exception innerException) : base(message, innerException) { }
}
