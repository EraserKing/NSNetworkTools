using System;

namespace Interfaces
{
    public interface IRequestReply
    {
        double SuccessRate { get; }

        double AverageRtt { get; }

        string IP { get; }
    }
}
