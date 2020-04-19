using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using MultiplePing;

namespace DNSRewrite
{
    public class Program
    {
        static void Main(string[] args)
        {
            string[] domainLines = new string[0];
            string[] dnsServerLines = new string[0];

            List<LookupClient> dnsClients = new List<LookupClient>();
            Dictionary<string, List<IPAddress>> domains = new Dictionary<string, List<IPAddress>>();

            try
            {
                domainLines = File.ReadAllLines("domains.txt");
                foreach (string domainLine in domainLines)
                {
                    domains[domainLine] = new List<IPAddress>();
                }

                dnsServerLines = File.ReadAllLines("server.txt");
                foreach (string dnsServerLine in dnsServerLines)
                {
                    if (!dnsServerLine.StartsWith("//"))
                    {
                        string dnsServer = dnsServerLine.Split(',')[0].Split(':')[0];
                        int dnsServerPort = Convert.ToInt32(dnsServerLine.Split(',')[0].Split(':')[1]);
                        bool dnsServerViaTcp = Convert.ToBoolean(dnsServerLine.Split(',')[1]);

                        dnsClients.Add(new LookupClient(new IPEndPoint(IPAddress.Parse(dnsServer), dnsServerPort))
                        {
                            UseTcpOnly = dnsServerViaTcp
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error:");
                Console.WriteLine(ex.Message);
                Console.WriteLine();

                Console.WriteLine("Usage:");
                Console.WriteLine("domains.txt contains the domains to query, one line per domain");
                Console.WriteLine();
                Console.WriteLine("server.txt contains the DNS server(s) to query, format is \"IP:PORT,TCP ONLY\", e.g. \"8.8.8.8:53,false\", one line per server");
                Console.WriteLine("// comments the line");
                Console.WriteLine();

                Console.WriteLine("Result:");
                Console.WriteLine("When there's only one DNS server added, the result will directly be output");
                Console.WriteLine("When multiple DNS servers are added, the resolved IPs are pinged to choose the fastest one, or the first one if all fails ping");
                Console.WriteLine("The output is in dnsmasq format, and you can copy it to your dnsmasq settings");

                return;
            }

            foreach (LookupClient client in dnsClients)
            {
                Console.WriteLine($"DNS server is {client.NameServers.FirstOrDefault().Endpoint}, queries done below will be based on this");
                foreach (string domain in domainLines)
                {
                    var result = client.Query(domain, QueryType.A).Answers.ARecords().Select(x => x.Address);
                    Console.WriteLine($"Query {domain} = {string.Join(',', result)}");
                    domains[domain].AddRange(result.Where(x => !domains[domain].Contains(x)));
                }
                Console.WriteLine();
            }
            Console.WriteLine();

            QueryResultCollection finalResults = new QueryResultCollection();

            if (dnsClients.Count == 1)
            {
                foreach (string domain in domains.Keys)
                {
                    finalResults.Add(new QueryResultCollection.QueryResult(domain, domains[domain].First().ToString()));
                }
            }
            else
            {
                Console.WriteLine("Ping all queried IPs");
                List<string> totalIps = new List<string>();
                foreach (string domain in domains.Keys)
                {
                    totalIps.AddRange(domains[domain].Select(x => x.ToString()).Where(x => !totalIps.Contains(x)));
                }

                ConcurrentDictionary<string, PingReplyCollection> pingResults = new ConcurrentDictionary<string, PingReplyCollection>();
                List<Task> pingTasks = new List<Task>();
                foreach(string ip in totalIps)
                {
                    pingTasks.Add(Task.Run(() =>
                    {
                        var pingReplies = PingUtility.SendPings(ip);
                        Console.WriteLine(pingReplies);
                        pingResults.AddOrUpdate(ip, pingReplies, (ip, value) => value);
                    }));
                }
                var pingTaskArray = pingTasks.ToArray();
                Task.WaitAll(pingTaskArray);
                Console.WriteLine();

                Console.WriteLine("Query results per domain as group");
                foreach (string domain in domains.Keys)
                {
                    Console.WriteLine($">> {domain}");

                    var ips = domains[domain];
                    var pingResultsOfDomain = ips.Select(x => pingResults[x.ToString()]);

                    Console.WriteLine(string.Join(Environment.NewLine, pingResultsOfDomain));

                    // All fail, means the domains bans ping - pick the first one
                    if (pingResultsOfDomain.All(x => x.SuccessRate == 0))
                    {
                        finalResults.Add(new QueryResultCollection.QueryResult(domain, pingResultsOfDomain.First().Ip));
                    }

                    // Else, pick the lowest RTT, but success rate > 0
                    else
                    {
                        PingReplyCollection lowestRttReply = pingResultsOfDomain.First(x => x.SuccessRate > 0);
                        double lowestRtt = lowestRttReply.AverageRtt;

                        foreach (PingReplyCollection replyCollection in pingResultsOfDomain.Where(x => x.SuccessRate > 0))
                        {
                            if (replyCollection.AverageRtt < lowestRtt)
                            {
                                lowestRttReply = replyCollection;
                                lowestRtt = replyCollection.AverageRtt;
                            }
                        }

                        finalResults.Add(new QueryResultCollection.QueryResult(domain, lowestRttReply.Ip));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Query results in the best combination");
            foreach (var finalResult in finalResults)
            {
                Console.WriteLine(finalResult);
            }
        }

        public class QueryResultCollection : IEnumerable<QueryResultCollection.QueryResult>
        {
            public List<QueryResult> results = new List<QueryResult>();

            public void Add(QueryResult queryResult) => results.Add(queryResult);

            public IEnumerator<QueryResult> GetEnumerator()
            {
                return ((IEnumerable<QueryResult>)results).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable<QueryResult>)results).GetEnumerator();
            }

            public class QueryResult
            {
                public string Domain { get; set; }
                public string Ip { get; set; }

                public QueryResult(string domain, string ip)
                {
                    Domain = domain;
                    Ip = ip;
                }

                public override string ToString() => $"address=/{Domain}/{Ip}";
            }
        }
    }
}
