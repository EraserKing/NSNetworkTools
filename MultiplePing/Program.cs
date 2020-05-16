using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace MultiplePing
{
    public class Program
    {
        static void Main(string[] args)
        {
            string[] sourceIps = new string[0];

            try
            {
                sourceIps = string.Join(',', File.ReadAllLines("ip.txt")).Split(',');
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error:");
                Console.WriteLine(ex.Message);
                Console.WriteLine("");

                Console.WriteLine("Usage:");
                Console.WriteLine("ip.txt contains the IP addresses to ping, separated by new line or comma");
                return;
            }

            List<Task> tasks = new List<Task>();

            foreach (string ip in sourceIps)
            {
                if (PingUtility.IfIPValid(ip))
                {
                    tasks.Add(Task.Run(() => Console.WriteLine(PingReplyMultiple.Run(ip))));
                }
                else
                {
                    Console.WriteLine($"ERROR IP ADDRESS: {ip}");
                }
            }

            var taskArray = tasks.ToArray();
            Task.WaitAll(taskArray);
            Console.WriteLine("ALL DONE");

        }
    }

    public class PingReplyMultiple
    {
        public PingReply[] Replies;
        public string IP;

        private double? successRate;
        private double? averageRtt;

        private PingReplyMultiple(string ip, IEnumerable<PingReply> replies)
        {
            IP = ip;
            Replies = replies.ToArray();
        }

        public static PingReplyMultiple Run(string ip)
        {
            return new PingReplyMultiple(ip, PingUtility.SendPings(ip));
        }

        public double SuccessRate => successRate ??= 1.0 * Replies.Count(x => x.Status == IPStatus.Success) / Replies.Length;
        public double AverageRtt => averageRtt ??= Replies.Average(x => x.RoundtripTime);
        public override string ToString() => $"{IP}, Success Rate = {SuccessRate * 100.0}%, Avg RTT (ms) = {AverageRtt}";
    }

    public class PingUtility
    {
        private static readonly byte[] Buffer = Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        private static PingOptions Options = new PingOptions()
        {
            DontFragment = true
        };

        public static PingReply[] SendPings(string ip, int timeout = 1000, int count = 20)
        {
            Ping pingSender = new Ping();

            PingReply[] replies = new PingReply[count];
            for (int i = 0; i < count; i++)
            {
                replies[i] = pingSender.Send(ip, timeout, Buffer, Options);
            }

            return replies;
        }

        public static bool IfIPValid(string ip)
        {
            string[] ipSegments = ip.Split('.');
            if (ipSegments.Length != 4)
            {
                return false;
            }
            if (ipSegments.Any(x => !int.TryParse(x, out _)))
            {
                return false;
            }
            if (ipSegments.Any(x => Convert.ToInt32(x) > 255 || Convert.ToInt32(x) < 0))
            {
                return false;
            }
            return true;
        }
    }
}
