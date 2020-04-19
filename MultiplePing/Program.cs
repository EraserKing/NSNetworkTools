using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
                if (PingUtility.CheckIfIpValid(ip))
                {
                    tasks.Add(Task.Run(() => Console.WriteLine(PingUtility.SendPings(ip))));
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

    public class PingReplyCollection
    {
        public PingReply[] Replies;
        public string Ip;

        public PingReplyCollection(string ip, IEnumerable<PingReply> replies)
        {
            Ip = ip;
            Replies = replies.ToArray();
        }

        public double SuccessRate => 1.0 * Replies.Count(x => x.Status == IPStatus.Success) / Replies.Length;
        public double AverageRtt => Replies.Average(x => x.RoundtripTime);
        public override string ToString() => $"{Ip}, Success Rate = {SuccessRate * 100.0}, Avg RTT = {AverageRtt}";
    }

    public class PingUtility
    {
        public static PingReplyCollection SendPings(string ip, int timeout = 1000, int count = 20)
        {
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions()
            {
                DontFragment = true
            };

            byte[] buffer = Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            PingReply[] replies = new PingReply[count];
            for (int i = 0; i < count; i++)
            {
                replies[i] = pingSender.Send(ip, timeout, buffer, options);
            }

            return new PingReplyCollection(ip, replies);
        }

        public static bool CheckIfIpValid(string ip)
        {
            string[] ipSegments = ip.Split('.');
            if (ipSegments.Length != 4)
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
