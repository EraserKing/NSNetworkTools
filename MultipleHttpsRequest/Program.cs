using Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Security;

namespace MultipleHttpsRequest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var q = HttpsReplyMultiple.Run("52.5.237.17");
        }
    }

    public class HttpsReplyMultiple : IRequestReply
    {
        public HttpsRequestReply[] Replies;
        public string IP { get; private set; }

        private double? successRate;
        private double? averageRtt;

        private HttpsReplyMultiple(string ip, IEnumerable<HttpsRequestReply> replies)
        {
            IP = ip;
            Replies = replies.ToArray();
        }

        public static HttpsReplyMultiple Run(string ip)
        {
            return new HttpsReplyMultiple(ip, HttpsRequestReply.MakeReplies(ip));
        }

        public double SuccessRate => successRate ??= 1.0 * Replies.Count(x => x.Success) / Replies.Length;

        public double AverageRtt => averageRtt ??= (SuccessRate > 0 ? Replies.Where(x => x.Success).Average(x => x.Rtt) : double.MaxValue);

        public override string ToString() => $"{IP}, Success Rate = {SuccessRate * 100.0}%, Avg RTT (ms) = {AverageRtt}, by HTTPS";
    }

    public class HttpsRequestReply
    {
        private static HttpClientHandler httpClientHandler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            {
                return true;
            }
        };

        public bool Success { get; private set; }
        public long Rtt { get; private set; }

        private HttpsRequestReply()
        {

        }

        public static HttpsRequestReply[] MakeReplies(string ip, int count = 20)
        {
            HttpClient client = new HttpClient(httpClientHandler);
            client.Timeout = new TimeSpan(0, 0, 3);
            HttpsRequestReply[] replies = new HttpsRequestReply[count];

            for (int i = 0; i < count; i++)
            {
                try
                {
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    var response = client.GetAsync($"https://{ip}").Result;
                    sw.Stop();
                    replies[i] = new HttpsRequestReply()
                    {
                        Success = true,
                        Rtt = sw.ElapsedMilliseconds
                    };
                }
                catch (Exception ex)
                {
                    replies[i] = new HttpsRequestReply()
                    {
                        Success = false,
                        Rtt = 0
                    };
                }
            }

            return replies;
        }
    }
}
